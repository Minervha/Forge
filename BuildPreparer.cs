using UAssetAPI;
using UAssetAPI.Unversioned;

/// <summary>
/// One-time-per-build step: materialise the <c>AutoMod_P/</c> base <c>.uasset</c> files
/// from a build's <c>*_Debug.json</c> exports. Their presence flips <see cref="ForgeGenerator"/>
/// from the slow v1 JSON fallback to the fast v2 binary injector (AssetInjector), which is
/// already parallelised. The output is permanent — base game DataTables never change for a
/// given build version, so this runs once and is then reused by every <c>generate</c>.
/// </summary>
public static class BuildPreparer
{
    // DataTable → AutoMod_P relative subfolder. MUST match the paths AssetInjector
    // reads from (see AssetInjector.InjectAll). Only the 4 injectable tables are
    // produced: GFur/SandboxProps are passthrough, and emitting them here would add
    // base tables to every pak that the current v1 output does not contain.
    public static readonly IReadOnlyDictionary<string, string> AutoModDtPaths =
        new Dictionary<string, string>
        {
            ["DT_ClothesOutfit"] = "WildLifeC/Content/DataTables",
            ["DT_GameCharacterOutfits"] = "WildLifeC/Content/DataTables/NPC",
            ["DT_GameCharacterCustomization"] = "WildLifeC/Content/DataTables/NPC",
            ["DT_Tattoo"] = "WildLifeC/Content/DataTables",
        };

    /// <summary>Absolute output path of the AutoMod_P base .uasset for <paramref name="dtName"/>.</summary>
    public static string AutoModAssetPath(string buildDir, string dtName)
        => Path.Combine(buildDir, "AutoMod_P", AutoModDtPaths[dtName], $"{dtName}.uasset");

    public static int Prepare(string buildDir, string usmapPath)
    {
        if (!Directory.Exists(buildDir))
        {
            Console.Error.WriteLine($"buildDir not found: {buildDir}");
            return 1;
        }

        Usmap? mappings = File.Exists(usmapPath) ? new Usmap(usmapPath) : null;
        if (mappings == null)
            Console.Error.WriteLine($"  WARNING: usmap not found: {usmapPath}");

        Console.WriteLine($"[prepare-build] {buildDir}");
        var written = 0;
        foreach (var (dtName, _) in AutoModDtPaths)
        {
            var debugPath = Path.Combine(buildDir, $"{dtName}_Debug.json");
            if (!File.Exists(debugPath))
            {
                Console.WriteLine($"  SKIP {dtName} (no {dtName}_Debug.json)");
                continue;
            }

            Console.Write($"  {dtName}...");
            // Debug.json is the faithful UAssetAPI serialization of the vanilla table
            // (full $type), so DeserializeJson reconstructs the base asset exactly —
            // the same round-trip the v1 path performs per job, done once here.
            var asset = UAsset.DeserializeJson(File.ReadAllText(debugPath));
            if (mappings != null) asset.Mappings = mappings;

            var outPath = AutoModAssetPath(buildDir, dtName);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            asset.Write(outPath); // also emits the sibling .uexp
            written++;
            Console.WriteLine($" -> {outPath}");
        }

        Console.WriteLine($"Done! {written} base DataTable(s) under {Path.Combine(buildDir, "AutoMod_P")}");
        return written > 0 ? 0 : 1;
    }
}
