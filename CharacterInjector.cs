using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

/// <summary>
/// Adds new characters and copies outfit lists between characters. A character is one row in
/// DT_NPCStaticData (mesh, outfit list, customization) and one in DT_NPCData_Sandbox (name, profile).
/// The game rebuilds CDT_NPCData from its parent tables, so the composite is left alone.
/// Tables load from their _Debug.json: AutoMod_P is optional and Studio builds normally don't have it.
/// </summary>
public static class CharacterInjector
{
    const string StaticDt = "DT_NPCStaticData";
    const string SandboxDt = "DT_NPCData_Sandbox";
    const string OutfitsDt = "DT_GameCharacterOutfits";
    const string CompositeDt = "CDT_NPCData";

    static readonly string[] DefaultOutfitFields = [
        "DefaultHeadOutfitSlotIndex", "DefaultChestOutfitSlotIndex", "DefaultHandsOutfitSlotIndex",
        "DefaultLegsOutfitSlotIndex", "DefaultFeetOutfitSlotIndex",
    ];

    // ENPCOption bit that keeps a row out of the game's character lists (TestDummy, Grace, RangerGuardSmall…).
    const int HiddenFromCharacterList = 1;

    /// <summary>DataTable → pak subfolder. Written only when a Character mod actually adds a row.</summary>
    public static readonly IReadOnlyDictionary<string, string> DtPaths = new Dictionary<string, string>
    {
        [StaticDt] = "WildLifeC/Content/DataTables/NPC",
        [SandboxDt] = "WildLifeC/Content/DataTables/NPC",
    };

    /// <summary>
    /// Explicit template, then the base's own row, then Empty{base}, then the first row built on the base.
    /// </summary>
    public static string? ResolveNpcTemplate(
        string explicitTemplate, string baseRow, IReadOnlyDictionary<string, string> staticByNpcRow)
    {
        if (!string.IsNullOrWhiteSpace(explicitTemplate))
            return staticByNpcRow.ContainsKey(explicitTemplate) ? explicitTemplate : null;
        if (staticByNpcRow.ContainsKey(baseRow)) return baseRow;
        if (staticByNpcRow.ContainsKey($"Empty{baseRow}")) return $"Empty{baseRow}";
        return staticByNpcRow.FirstOrDefault(kv => kv.Value == baseRow).Key;
    }

    /// <summary>
    /// Character mods with the same id (any case): the last one wins, as for outfits and tattoos, and each one
    /// dropped gets a warning naming the mod kept, so Studio shows why a character is missing.
    /// </summary>
    public static void DropDuplicateCharacters(List<ModData> mods)
    {
        var kept = new Dictionary<string, ModData>(StringComparer.OrdinalIgnoreCase);
        for (int i = mods.Count - 1; i >= 0; i--)
        {
            if (mods[i].CharacterEntry is not { } character) continue;
            if (kept.TryAdd(character.Id, mods[i])) continue;
            Console.WriteLine($"  DEDUP: dropping duplicate character '{character.Id}' from {mods[i].ModName}");
            Logger.EmitModWarning(mods[i].ModId, mods[i],
                $"{kept[character.Id].ModName} also adds the character '{character.Id}': this one was ignored");
            mods.RemoveAt(i);
        }
    }

    /// <summary>
    /// Runs before injection, on both forge paths: turns Outfit Set mods into Port entries, and points
    /// Add/Port mods aimed at a new character to the list that character uses.
    /// </summary>
    public static void PrepareOutfitMods(string buildDir, string usmapPath, List<ModData> mods)
    {
        var characters = mods.Where(m => m.CharacterEntry != null).ToList();
        var sets = mods.Where(m => m.Variant == ModVariant.OutfitSet).ToList();
        var redirected = mods.Where(m => m.Variant is ModVariant.Add or ModVariant.Port
            && characters.Any(c => SameName(c.CharacterEntry!.Id, m.Character))).ToList();
        if (sets.Count == 0 && redirected.Count == 0) return;

        var mappings = LoadMappings(usmapPath);
        var outfits = LoadTable(buildDir, OutfitsDt, mappings);
        if (outfits == null)
        {
            foreach (var mod in sets.Concat(redirected))
            {
                Logger.EmitModError(mod.ModId, mod, $"this game build's data has no {OutfitsDt}");
                mods.Remove(mod);
            }
            return;
        }

        var staticTable = LoadTable(buildDir, StaticDt, mappings);
        var outfitRows = Rows(outfits).Select(r => r.Name.ToString()).ToList();

        string? ListOf(string name)
        {
            var character = characters.FirstOrDefault(c => SameName(c.CharacterEntry!.Id, name))?.CharacterEntry;
            if (character != null)
                name = string.IsNullOrWhiteSpace(character.Outfits) ? character.Base : character.Outfits;
            // A static row names its list directly: Aoi and Sonia use Outfits_SexBot_AB_NSFW.
            var row = staticTable == null ? null : Rows(staticTable).FirstOrDefault(r => SameName(r.Name.ToString(), name));
            var listed = row == null ? null : RowHandleTarget(row, "Outfits");
            return listed != null && outfitRows.Contains(listed) ? listed : OutfitRows.Resolve(name, outfitRows);
        }

        foreach (var mod in redirected)
        {
            var list = ListOf(mod.Character);
            if (list == null)
            {
                Logger.EmitModError(mod.ModId, mod, $"no outfit list for character '{mod.Character}'");
                mods.Remove(mod);
                continue;
            }
            mod.Character = OutfitRows.CleanName(list);
        }

        foreach (var set in sets)
        {
            var from = ListOf(set.OutfitsFrom);
            var to = ListOf(set.OutfitsTo);
            if (from == null || to == null || from == to)
            {
                var reason = from == null ? $"no outfit list for source character '{set.OutfitsFrom}'"
                    : to == null ? $"no outfit list for target character '{set.OutfitsTo}'"
                    : $"'{set.OutfitsFrom}' and '{set.OutfitsTo}' already share the same outfit list";
                Logger.EmitModError(set.ModId, set, reason);
                mods.Remove(set);
                continue;
            }

            var target = OutfitRows.CleanName(to);
            var present = new HashSet<string>(ClothingIds(outfits, mods, to), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var id in ClothingIds(outfits, mods, from).ToList())
            {
                if (!present.Add(id)) continue;
                mods.Add(new ModData
                {
                    Variant = ModVariant.Port,
                    ModId = set.ModId,
                    ModName = set.ModName,
                    Author = set.Author,
                    SourcePath = set.SourcePath,
                    Character = target,
                    ClothingId = id,
                });
                added++;
            }
            Console.WriteLine($"  Outfit Set {set.OutfitsFrom} -> {set.OutfitsTo}: {added} outfit(s) added.");
        }
    }

    /// <summary>
    /// Injects Character mods and returns only the tables that changed (none when nothing was added).
    /// A mod that can't be applied gets a mod-error and leaves <paramref name="mods"/>: its assets stay out of the pak.
    /// </summary>
    public static Dictionary<string, UAsset> Inject(string buildDir, string usmapPath, List<ModData> mods)
    {
        var result = new Dictionary<string, UAsset>();
        var characterMods = mods.Where(m => m.CharacterEntry != null).ToList();
        if (characterMods.Count == 0) return result;

        var mappings = LoadMappings(usmapPath);
        var staticTable = LoadTable(buildDir, StaticDt, mappings);
        var sandboxTable = LoadTable(buildDir, SandboxDt, mappings);
        if (staticTable == null || sandboxTable == null)
        {
            foreach (var mod in characterMods)
            {
                Logger.EmitModError(mod.ModId, mod,
                    $"this game build's data has no {StaticDt}/{SandboxDt}: update the game data in Minervha Studio");
                mods.Remove(mod);
            }
            return result;
        }

        // Optional: only used to pick default outfits, builds without them fall back on the template's.
        var compositeTable = LoadTable(buildDir, CompositeDt, mappings);
        var outfitsTable = LoadTable(buildDir, OutfitsDt, mappings);

        foreach (var mod in characterMods)
        {
            try
            {
                AddCharacter(staticTable, sandboxTable, compositeTable, outfitsTable, mod.CharacterEntry!);
                Console.WriteLine($"  Character {mod.CharacterEntry!.Id} based on {mod.CharacterEntry.Base}");
            }
            catch (Exception ex)
            {
                Logger.EmitModError(mod.ModId, mod, $"character: {ex.Message}");
                mods.Remove(mod);
            }
        }

        if (mods.Any(m => m.CharacterEntry != null))
        {
            result[StaticDt] = staticTable;
            result[SandboxDt] = sandboxTable;
        }
        return result;
    }

    // All checks come first: a refused character must leave both tables untouched.
    static void AddCharacter(UAsset staticTable, UAsset sandboxTable, UAsset? compositeTable, UAsset? outfitsTable, CharacterData c)
    {
        var staticRows = Rows(staticTable);
        var npcRows = Rows(sandboxTable);
        if (staticRows.Any(r => SameName(r.Name.ToString(), c.Id)) || npcRows.Any(r => SameName(r.Name.ToString(), c.Id)))
            throw new InvalidDataException($"'{c.Id}' already exists in this game build");

        var baseRow = staticRows.FirstOrDefault(r => SameName(r.Name.ToString(), c.Base))
            ?? throw new InvalidDataException($"base character '{c.Base}' not found in {StaticDt}");

        var staticByNpcRow = npcRows.ToDictionary(r => r.Name.ToString(), r => RowHandleTarget(r, "StaticData") ?? "");
        var templateName = ResolveNpcTemplate(c.NpcTemplate, baseRow.Name.ToString(), staticByNpcRow)
            ?? throw new InvalidDataException($"no {SandboxDt} row to use as a template for '{c.Base}'");

        string? outfitList = null;
        if (!string.IsNullOrWhiteSpace(c.Outfits))
        {
            // A static row (Aoi) or a list name that no static row carries (SexBot) both work.
            var source = staticRows.FirstOrDefault(r => SameName(r.Name.ToString(), c.Outfits));
            outfitList = source != null
                ? RowHandleTarget(source, "Outfits")
                : OutfitRows.Resolve(c.Outfits, staticRows.Select(r => RowHandleTarget(r, "Outfits")).OfType<string>().Distinct().ToList());
            if (outfitList == null)
                throw new InvalidDataException($"no outfit list for character '{c.Outfits}'");
        }

        staticTable.AddNameReference(new FString(c.Id));
        var newStatic = AssetInjector.DeepClone(baseRow);
        newStatic.Name = FName.FromString(staticTable, c.Id);
        var mesh = AssetInjector.Find<SoftObjectPropertyData>(newStatic, "Mesh")
            ?? throw new InvalidDataException($"{StaticDt} rows have no Mesh field");
        AssetInjector.SetSoftObject(staticTable, mesh, c.MeshPath, c.MeshName);
        if (outfitList != null) SetRowHandleTarget(staticTable, newStatic, "Outfits", outfitList);

        sandboxTable.AddNameReference(new FString(c.Id));
        var newNpc = AssetInjector.DeepClone(npcRows.First(r => r.Name.ToString() == templateName));
        newNpc.Name = FName.FromString(sandboxTable, c.Id);
        SetRowHandleTarget(sandboxTable, newNpc, "StaticData", c.Id);
        if (!string.IsNullOrWhiteSpace(c.Profile))
            SetRowHandleTarget(sandboxTable, newNpc, "customizationProfile", c.Profile);

        ApplyDefaultOutfits(newNpc, compositeTable, outfitsTable,
            string.IsNullOrWhiteSpace(c.Outfits) ? c.Base : c.Outfits,
            outfitList ?? RowHandleTarget(baseRow, "Outfits"));

        var options = AssetInjector.Find<IntPropertyData>(newNpc, "Options");
        AssetInjector.SetInt(options, (options?.Value ?? 0) & ~HiddenFromCharacterList);

        // A fresh key: reusing the template's would show its translated name instead of this one.
        var displayName = AssetInjector.Find<TextPropertyData>(newNpc, "DisplayName")
            ?? throw new InvalidDataException($"{SandboxDt} rows have no DisplayName field");
        displayName.Value = new FString($"Minervha_Character_{c.Id}");
        displayName.CultureInvariantString = new FString(c.DisplayName);

        staticRows.Add(newStatic);
        npcRows.Add(newNpc);
    }

    /// <summary>
    /// A default outfit is an index in the character's list, so a template's values only mean something
    /// on its own list: EmptyMaya asks for outfit 63, and Alissa has 17, which leaves the character bare.
    /// Takes them from the character owning the list, then brings any index past the list back to the first.
    /// </summary>
    static void ApplyDefaultOutfits(StructPropertyData npc, UAsset? compositeTable, UAsset? outfitsTable, string owner, string? listRow)
    {
        var rows = compositeTable == null ? [] : Rows(compositeTable);
        var donor = rows.FirstOrDefault(r => SameName(r.Name.ToString(), owner))
            ?? rows.FirstOrDefault(r => SameName(RowHandleTarget(r, "StaticData") ?? "", owner));
        var list = outfitsTable == null || listRow == null ? null : Rows(outfitsTable).FirstOrDefault(r => r.Name.ToString() == listRow);
        var count = list == null ? (int?)null : AssetInjector.Find<ArrayPropertyData>(list, "Outfits")?.Value?.Length;

        foreach (var field in DefaultOutfitFields)
        {
            var value = AssetInjector.Find<IntPropertyData>(npc, field);
            if (value == null) continue;
            var fromDonor = donor == null ? null : AssetInjector.Find<IntPropertyData>(donor, field);
            var index = fromDonor?.Value ?? value.Value;
            AssetInjector.SetInt(value, count is { } n && index >= n ? 0 : index);
        }
    }

    /// <summary>The list's vanilla outfits, then those mods already add to it.</summary>
    static IEnumerable<string> ClothingIds(UAsset outfits, List<ModData> mods, string listRow)
    {
        var rowNames = Rows(outfits).Select(r => r.Name.ToString()).ToList();
        var row = Rows(outfits).First(r => r.Name.ToString() == listRow);
        var handles = AssetInjector.Find<ArrayPropertyData>(row, "Outfits")?.Value ?? [];
        foreach (var handle in handles.OfType<StructPropertyData>())
        {
            var id = AssetInjector.Find<NamePropertyData>(handle, "RowName")?.Value?.ToString();
            if (!string.IsNullOrEmpty(id) && id != "None") yield return id;
        }
        foreach (var mod in mods.Where(m => m.Variant is ModVariant.Add or ModVariant.Port))
            if (OutfitRows.Resolve(mod.Character, rowNames) == listRow && !string.IsNullOrEmpty(mod.ClothingId))
                yield return mod.ClothingId;
    }

    static Usmap? LoadMappings(string usmapPath) => File.Exists(usmapPath) ? SafeLoad.Usmap(usmapPath) : null;

    static UAsset? LoadTable(string buildDir, string dtName, Usmap? mappings)
    {
        var path = Path.Combine(buildDir, $"{dtName}_Debug.json");
        if (!File.Exists(path)) return null;
        var asset = UAsset.DeserializeJson(File.ReadAllText(path));
        if (mappings != null) asset.Mappings = mappings;
        return asset;
    }

    static List<StructPropertyData> Rows(UAsset asset) => ((DataTableExport)asset.Exports[0]).Table.Data;

    static bool SameName(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    static string? RowHandleTarget(StructPropertyData row, string field)
    {
        var handle = AssetInjector.Find<StructPropertyData>(row, field);
        var name = handle == null ? null : AssetInjector.Find<NamePropertyData>(handle, "RowName")?.Value?.ToString();
        return string.IsNullOrEmpty(name) || name == "None" ? null : name;
    }

    static void SetRowHandleTarget(UAsset asset, StructPropertyData row, string field, string target)
    {
        var handle = AssetInjector.Find<StructPropertyData>(row, field)
            ?? throw new InvalidDataException($"rows have no {field} field");
        var rowName = AssetInjector.Find<NamePropertyData>(handle, "RowName")
            ?? throw new InvalidDataException($"{field} has no RowName");
        asset.AddNameReference(new FString(target));
        rowName.Value = FName.FromString(asset, target);
    }
}
