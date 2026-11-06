using System.Diagnostics;
using System.Runtime;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

public static class ForgeGenerator
{
    static readonly UTF8Encoding Utf8NoBom = new(false);

    // DataTable name → subfolder inside the pak
    static readonly Dictionary<string, string> DtPaths = new()
    {
        ["DT_ClothesOutfit"] = "WildLifeC/Content/DataTables",
        ["DT_GFur"] = "WildLifeC/Content/DataTables",
        ["DT_GameCharacterOutfits"] = "WildLifeC/Content/DataTables/NPC",
        ["DT_GameCharacterCustomization"] = "WildLifeC/Content/DataTables/NPC",
        ["DT_SandboxProps"] = "WildLifeC/Content/DataTables/Sandbox",
        ["DT_Tattoo"] = "WildLifeC/Content/DataTables",
    };

    // Without these the pak would load but every modded outfit would be missing from the game.
    static readonly string[] CoreTables = ["DT_ClothesOutfit", "DT_GameCharacterOutfits", "DT_GameCharacterCustomization"];

    /// <summary>Mods parsed by the current run, so a fatal error can be attributed.</summary>
    public static IReadOnlyList<ModData> ParsedMods { get; private set; } = [];

    /// <summary>Emit a structured progress line for the Electron UI to parse.</summary>
    static void EmitProgress(int percent, string message)
    {
        Console.WriteLine($"PROGRESS:{percent}:{message}");
        Console.Out.Flush();
    }

    public static int Generate(ForgeConfig config)
    {
        // Batch GC mode: reduces stop-the-world pauses on large heap (60MB+ JSON strings)
        var prevLatency = GCSettings.LatencyMode;
        GCSettings.LatencyMode = GCLatencyMode.Batch;
        // ForgeConfig.Mods is initialized to [] (ForgeConfig.cs:12), so the field is non-null;
        // skipping the `?.` avoids a CS8602 cascade on the foreach below that flow-analyzes
        // through the null path even though it's unreachable.
        var originalModCount = config.Mods.Length;

        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        var stepSw = new System.Diagnostics.Stopwatch();

        void StartStep(string name) { stepSw.Restart(); Console.Write($"  [{name}]"); }
        void EndStep() { Console.WriteLine($" {stepSw.ElapsedMilliseconds}ms"); }

        EmitProgress(0, "Parsing mods...");
        Console.WriteLine("[1/5] Parsing mods...");

        // 1. Parse all mod .txt files
        var mods = new List<ModData>();
        var assetOnlyModIds = new HashSet<string>();
        ParsedMods = mods;
        foreach (var modId in config.Mods)
        {
            var modDir = ModDirResolver.Resolve(config, modId);
            if (!Directory.Exists(modDir))
            {
                Logger.EmitModError(modId, null, $"mod dir not found: {modDir}");
                continue;
            }
            // Scan both the mod root and AutoMod/ — legacy wlmm imports keep
            // their rule files under AutoMod/ instead of the mod root. Studio
            // used to flatten them into a single dir during its staging copy;
            // now that we read straight from ModCache, the scan has to do it.
            var ruleFiles = ModDirResolver.ListRuleFiles(modDir);
            if (ruleFiles.Count == 0)
            {
                // No rules means no new table entry: the files can only overwrite game
                // assets of the same path. Valid, but the user must be told about it.
                var assetFiles = ModDirResolver.CountAssetFiles(modDir);
                if (assetFiles > 0)
                {
                    assetOnlyModIds.Add(modId);
                    Logger.EmitEvent("asset-only-mod", new { modId, assetFiles });
                }
                else
                {
                    Logger.EmitModError(modId, null, "No rule file and no game file: nothing to add to the pak");
                }
                continue;
            }
            foreach (var txt in ruleFiles)
            {
                try
                {
                    var mod = ModParser.Parse(txt);
                    mod.ModId = modId;  // attribute the mod to its parent directory
                    mods.Add(mod);
                    Console.WriteLine($"  {Path.GetFileName(txt)} -> {mod.Variant} ({mod.ModName})");
                }
                catch (Exception ex)
                {
                    Logger.EmitModError(modId, null, $"parse-error: {Path.GetFileName(txt)}: {ex.Message}");
                }
            }
        }
        Console.WriteLine($"  {mods.Count} mod entries parsed.");

        // 1.5 Validate parsed mods — catch known structural failures (empty FStrings,
        // missing required IDs) before they reach UAssetAPI. Invalid mods are emitted
        // as mod-error events and removed from the working list; valid mods proceed.
        {
            var dropped = 0;
            for (int i = mods.Count - 1; i >= 0; i--) {
                var errors = ModValidator.Validate(mods[i]);
                if (errors.Count > 0) {
                    Logger.EmitModError(mods[i].ModId, mods[i], string.Join("; ", errors));
                    mods.RemoveAt(i);
                    dropped++;
                }
            }
            if (dropped > 0)
                Console.WriteLine($"  {dropped} mod(s) failed validation and were skipped.");
            foreach (var mod in mods) ModValidator.CompleteNameMap(mod);
        }

        // Deduplicate Add mods by ClothingId — last-wins (mirrors file-level conflict resolution).
        // When the same outfit exists in both a shop mod and a wlmm import, both get staged
        // with identical ClothingIds. Duplicate row names in a UE DataTable cause undefined
        // behavior (some entries silently disappear in-game).
        {
            var addCountBefore = mods.Count(m => m.Variant == ModVariant.Add);
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = mods.Count - 1; i >= 0; i--)
            {
                if (mods[i].Variant != ModVariant.Add) continue;
                if (!seen.TryAdd(mods[i].ClothingId, i))
                {
                    Console.WriteLine($"  DEDUP: dropping duplicate ClothingId '{mods[i].ClothingId}' from {mods[i].ModName}");
                    Logger.EmitModWarning(mods[i].ModId, mods[i], $"Another mod defines the same outfit id '{mods[i].ClothingId}': this one was ignored");
                    mods.RemoveAt(i);
                }
            }
            var dedupCount = addCountBefore - mods.Count(m => m.Variant == ModVariant.Add);
            if (dedupCount > 0)
                Console.WriteLine($"  {dedupCount} duplicate ClothingId(s) removed.");
        }

        // Deduplicate Tattoo mods by TattooId — last-wins (mirrors Add dedup / file-level conflict resolution).
        {
            var tattooCountBefore = mods.SelectMany(m => m.TattooEntries).Count();
            var seenTattoos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var tattooMods = mods.Where(m => m.Variant == ModVariant.Tattoo).ToList();
            // Reverse pass: first occurrence in reverse = last-wins
            for (int mi = tattooMods.Count - 1; mi >= 0; mi--)
            {
                var mod = tattooMods[mi];
                for (int ti = mod.TattooEntries.Count - 1; ti >= 0; ti--)
                {
                    var t = mod.TattooEntries[ti];
                    if (!seenTattoos.TryAdd(t.TattooId, true))
                    {
                        Console.WriteLine($"  DEDUP: dropping duplicate TattooId '{t.TattooId}' from {mod.ModName}");
                        mod.TattooEntries.RemoveAt(ti);
                    }
                }
            }
            var tattooDedup = tattooCountBefore - mods.SelectMany(m => m.TattooEntries).Count();
            if (tattooDedup > 0)
                Console.WriteLine($"  {tattooDedup} duplicate TattooId(s) removed.");
        }

        CharacterInjector.DropDuplicateCharacters(mods);

        CharacterInjector.PrepareOutfitMods(config.BuildDir, config.UsmapPath, mods);

        EmitProgress(15, $"{mods.Count} mods parsed");

        // 2. Read character lists
        var characters = ReadLines(Path.Combine(config.BuildDir, "Characters.txt"));
        foreach (var mod in mods)
        {
            if (ModValidator.FindCharacterWarning(mod, characters) is string warning)
                Logger.EmitModWarning(mod.ModId, mod, warning);
        }

        // 3. Inject — try v2 (direct UAsset manipulation) first, v1 (JSON) as fallback
        EmitProgress(20, "Injecting mods...");
        Console.WriteLine("[2/5] Injecting mods...");

        Dictionary<string, UAsset>? directAssets = null;
        Dictionary<string, string>? injected = null;

        StartStep("v2-inject");
        directAssets = AssetInjector.InjectAll(
            config.BuildDir, config.UsmapPath, mods.ToArray(), config.FillEmptySexSlots);
        EndStep();

        // Mod breakdown for diagnostics
        var addModCount = mods.Count(m => m.Variant == ModVariant.Add);
        var portModCount = mods.Count(m => m.Variant == ModVariant.Port);
        var custModCount = mods.Count(m => m.Variant == ModVariant.CharacterCustomization);
        var tattooModCount = mods.Count(m => m.Variant == ModVariant.Tattoo);
        var characterModCount = mods.Count(m => m.Variant == ModVariant.Character);
        Console.WriteLine($"  Mods breakdown: {addModCount} Add, {portModCount} Port, {custModCount} CharCust, {tattooModCount} Tattoo, {characterModCount} Character");

        if (directAssets != null)
        {
            Console.WriteLine($"  v2: {directAssets.Count} DataTables injected (direct).");
            foreach (var (name, asset) in directAssets)
            {
                var dtExport = asset.Exports[0] as DataTableExport;
                if (dtExport != null)
                    Console.WriteLine($"    {name}: {dtExport.Table.Data.Count} rows, NameMap: {asset.GetNameMapIndexList().Count} entries");
            }
        }
        else
        {
            // v1 fallback: JSON template injection
            Console.Write("  v1 fallback (no AutoMod_P)...");
            StartStep("v1-inject");
            injected = TemplateInjector.InjectAll(
                config.BuildDir, mods.ToArray(), characters);
            EndStep();
            Console.WriteLine($" {injected.Count} DataTables injected.");
        }

        var characterAssets = CharacterInjector.Inject(config.BuildDir, config.UsmapPath, mods);

        EmitProgress(35, "DataTables injected");

        // 4. Convert to .uasset/.uexp
        EmitProgress(40, "Converting DataTables...");
        Console.WriteLine("[3/5] Writing .uasset/.uexp...");

        Usmap? mappings = null;
        if (File.Exists(config.UsmapPath))
        {
            mappings = SafeLoad.Usmap(config.UsmapPath);
            Console.WriteLine($"  Loaded mappings: {config.UsmapPath}");
        }
        else
        {
            Console.Error.WriteLine($"  WARNING: usmap not found: {config.UsmapPath}");
        }

        // Use a fresh GUID-based staging dir per run to avoid collisions with
        // locked residual directories from prior executions (e.g. Output/anonymous/).
        // This sidesteps NTFS handle-lock issues entirely — we never touch old dirs.
        var stagingDir = Path.Combine(config.OutputDir, $"staging_{Guid.NewGuid():N}");

        var autoModBase = Path.Combine(config.BuildDir, "AutoMod_P");

        // ── Write all DTs in parallel (v2 assets are independent) ──
        var writeTasks = new List<Task>();
        var writeErrors = new System.Collections.Concurrent.ConcurrentBag<string>();

        foreach (var (dtName, subPath) in DtPaths)
        {
            var basePath = Path.Combine(autoModBase, subPath, $"{dtName}.uasset");
            var outDir = Path.Combine(stagingDir, subPath);
            Directory.CreateDirectory(outDir);

            // v2 path: direct UAsset write (parallelizable — each asset is independent)
            if (directAssets != null && directAssets.ContainsKey(dtName))
            {
                var asset = directAssets[dtName];
                var dn = dtName; // capture for closure
                writeTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        asset.Write(Path.Combine(outDir, $"{dn}.uasset"));
                    }
                    catch (Exception ex)
                    {
                        writeErrors.Add($"{dn}: {ex.GetType().Name}: {ex.Message}");
                    }
                }));
                continue;
            }

            // File copy path: unchanged DTs (GFur, SandboxProps)
            if (File.Exists(basePath) &&
                (dtName == "DT_GFur" || (injected == null || !injected.ContainsKey(dtName))))
            {
                File.Copy(basePath, Path.Combine(outDir, $"{dtName}.uasset"), true);
                var uexpPath = Path.ChangeExtension(basePath, ".uexp");
                if (File.Exists(uexpPath))
                    File.Copy(uexpPath, Path.Combine(outDir, $"{dtName}.uexp"), true);
                continue;
            }

            // v1 fallback: JSON-based DeserializeJson (sequential — too heavy to parallelize)
            if (injected != null && injected.ContainsKey(dtName))
            {
                var asset = DeserializeJsonWithDiag(injected[dtName], dtName, config.OutputDir);
                // v1 parity with AssetInjector (v2): fill empty sex-mesh slots on the
                // base DT_ClothesOutfit rows when the option is on. Same method, same
                // UAsset shape — the v1 path just reaches the asset via DeserializeJson.
                if (config.FillEmptySexSlots && dtName == "DT_ClothesOutfit")
                    AssetInjector.FillEmptySexMeshSlots(asset);
                if (mappings != null) asset.Mappings = mappings;
                asset.Write(Path.Combine(outDir, $"{dtName}.uasset"));
            }
            else if (File.Exists(basePath))
            {
                string jsonSource;
                if (dtName != "DT_GFur" && injected != null && injected.ContainsKey(dtName))
                    jsonSource = injected[dtName];
                else
                {
                    var debugPath = Path.Combine(config.BuildDir, $"{dtName}_Debug.json");
                    if (!File.Exists(debugPath)) continue;
                    jsonSource = File.ReadAllText(debugPath);
                }
                var asset = DeserializeJsonWithDiag(jsonSource, dtName, config.OutputDir);
                if (mappings != null) asset.Mappings = mappings;
                try { Directory.CreateDirectory(Path.GetDirectoryName(basePath)!); asset.Write(basePath); } catch { }
                asset.Write(Path.Combine(outDir, $"{dtName}.uasset"));
            }
        }

        foreach (var (dtName, asset) in characterAssets)
        {
            var outDir = Path.Combine(stagingDir, CharacterInjector.DtPaths[dtName]);
            Directory.CreateDirectory(outDir);
            asset.Write(Path.Combine(outDir, $"{dtName}.uasset"));
        }

        // Wait for all parallel writes
        if (writeTasks.Count > 0)
        {
            StartStep("parallel-write");
            Task.WaitAll(writeTasks.ToArray());
            EndStep();
        }

        if (!writeErrors.IsEmpty)
        {
            foreach (var err in writeErrors)
                Console.Error.WriteLine($"  ERROR: {err}");
            Logger.EmitFatal($"Could not write the game tables: {writeErrors.First()}", ModValidator.FindSuspects(string.Join(" ", writeErrors), mods));
            Logger.EmitSummary(originalModCount, pakOk: false);
            return 1;
        }

        var missingTables = CoreTables
            .Where(dt => !File.Exists(Path.Combine(stagingDir, DtPaths[dt], $"{dt}.uasset")))
            .ToList();
        if (missingTables.Count > 0)
        {
            Logger.EmitFatal($"The DataTables of this game version are incomplete (missing {string.Join(", ", missingTables)})");
            Logger.EmitSummary(originalModCount, pakOk: false);
            return 1;
        }

        foreach (var (dtName, _) in DtPaths)
            Console.WriteLine($"  {dtName} OK");
        EmitProgress(75, "DataTables written");

        // 5. Copy mod assets (WildLifeC/ trees) — only for mods that survived validation.
        //    A mod that was dropped during validation should not contaminate the pak with
        //    its asset tree, even if its other shapes (DataTable injections) were skipped.
        EmitProgress(80, "Copying mod assets...");
        Console.WriteLine("[4/5] Copying mod assets...");
        var keptModIds = new HashSet<string>(mods.Select(m => m.ModId).Concat(assetOnlyModIds));
        foreach (var modId in config.Mods)
        {
            if (!keptModIds.Contains(modId)) continue;  // dropped at validation, skip its assets too
            var wlcSrc = Path.Combine(ModDirResolver.Resolve(config, modId), "WildLifeC");
            if (Directory.Exists(wlcSrc))
            {
                CopyDirRecursive(wlcSrc, Path.Combine(stagingDir, "WildLifeC"));
                Console.WriteLine($"  {modId}/WildLifeC -> staging");
            }
        }

        // 6. Pack with repak
        EmitProgress(92, "Packing .pak...");
        Console.WriteLine("[5/5] Packing .pak...");
        var pakDir = string.IsNullOrEmpty(config.UserId)
            ? config.OutputDir
            : Path.Combine(config.OutputDir, config.UserId);
        Directory.CreateDirectory(pakDir);
        var pakPath = Path.Combine(pakDir, $"{config.PakName}_P.pak");

        var psi = new ProcessStartInfo(config.RepakExe,
            $"pack \"{stagingDir}\" \"{pakPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine($"repak failed (exit {proc.ExitCode}):\n{stderr}\n{stdout}");
            Logger.EmitFatal($"repak could not build the pak (exit {proc.ExitCode})");
            Logger.EmitSummary(originalModCount, pakOk: false);
            return 1;
        }

        Console.WriteLine($"  Created: {pakPath}");

        // Try to clean up our fresh staging dir. Safe because we just created it
        // and repak has finished — no lingering handles expected.
        // If it fails (antivirus hold), it's harmless: the GUID name won't collide
        // with future runs, and main.js can sweep it later.
        try { Directory.Delete(stagingDir, true); }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARN: could not remove staging dir: {ex.Message}");
        }

        GCSettings.LatencyMode = prevLatency;
        totalSw.Stop();
        EmitProgress(100, "Done");
        Console.WriteLine($"Done! Total: {totalSw.ElapsedMilliseconds}ms");
        Logger.EmitSummary(originalModCount, pakOk: true);
        return 0;
    }

    /// <summary>
    /// Merge injected template data (NameMap + Table.Data) into the Debug.json
    /// which has full $type annotations needed by UAsset.DeserializeJson().
    /// </summary>
    static string MergeInjectedIntoDebug(string debugJson, string injectedJson)
    {
        // Parse both with NaN support
        JToken debugObj;
        using (var r = new JsonTextReader(new StringReader(debugJson))
            { FloatParseHandling = FloatParseHandling.Double })
            debugObj = JToken.Load(r);

        JToken injectedObj;
        using (var r = new JsonTextReader(new StringReader(injectedJson))
            { FloatParseHandling = FloatParseHandling.Double })
            injectedObj = JToken.Load(r);

        // Replace NameMap
        var injectedNameMap = injectedObj["NameMap"];
        if (injectedNameMap != null)
            debugObj["NameMap"] = injectedNameMap.DeepClone();

        // Replace Exports[0].Table.Data
        var injectedData = injectedObj["Exports"]?[0]?["Table"]?["Data"];
        if (injectedData != null)
        {
            var debugData = debugObj["Exports"]?[0]?["Table"]?["Data"];
            if (debugData != null)
                debugData.Replace(injectedData.DeepClone());
        }

        // Serialize back preserving $type as plain JSON properties and NaN as native doubles.
        // Use JToken.WriteTo() instead of JsonSerializer with TypeNameHandling.Auto —
        // the latter can add/modify $type annotations in ways that break UAssetAPI.
        var sb = new StringBuilder();
        using (var sw = new StringWriter(sb))
        using (var writer = new JsonTextWriter(sw) { Formatting = Formatting.Indented })
        {
            debugObj.WriteTo(writer);
        }
        return sb.ToString();
    }

    static string[] ReadLines(string path)
    {
        if (!File.Exists(path)) return [];
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
    }

    static void CopyDirRecursive(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
        {
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (var dir in Directory.GetDirectories(src))
        {
            CopyDirRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }

    // Wraps UAsset.DeserializeJson with post-mortem diagnostics for the
    // "Cannot add an empty FString" crash. On match, emits a structured
    // dt-deserialize-error event with the suspect positions and dumps the
    // failing JSON to disk for offline analysis. The exception is re-thrown
    // unchanged so the surrounding error-handling path stays identical.
    static UAsset DeserializeJsonWithDiag(string json, string dtName, string outputDir)
    {
        try
        {
            return UAsset.DeserializeJson(json);
        }
        catch (System.ArgumentException ex) when (ex.Message.Contains("empty FString") || ex.Message.Contains("name map"))
        {
            try
            {
                var diag = JsonDiagnostics.DiagnoseEmptyFString(json);
                Logger.EmitEvent("dt-deserialize-error", new {
                    dtName,
                    errorType = "empty-FString",
                    errorMessage = ex.Message,
                    diagnostics = diag,
                });
            }
            catch (System.Exception diagEx)
            {
                System.Console.Error.WriteLine($"  Could not produce diagnostics: {diagEx.Message}");
            }

            try
            {
                Directory.CreateDirectory(outputDir);
                var dumpPath = Path.Combine(outputDir, $"{dtName}_failed_{System.DateTime.UtcNow:yyyyMMddHHmmss}.json");
                File.WriteAllText(dumpPath, json);
                System.Console.Error.WriteLine($"  Dumped failing JSON to {dumpPath}");
            }
            catch (System.Exception dumpEx)
            {
                System.Console.Error.WriteLine($"  Could not dump failing JSON: {dumpEx.Message}");
            }
            throw;
        }
    }
}
