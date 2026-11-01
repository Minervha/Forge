using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

/// <summary>
/// v2 injector: manipulates UAsset objects directly in memory (no JSON round-trip).
/// Loads base .uasset binaries from AutoMod_P, clones existing rows, overwrites values,
/// and returns ready-to-Write() UAsset objects.
/// </summary>
public static class AssetInjector
{
    static readonly string[] SlotNames = ["Head", "Chest", "Hands", "Legs", "Feet"];
    static readonly string[] SlotKeys = ["HEAD", "CHEST", "HANDS", "LEGS", "FEET"];

    /// <summary>
    /// Inject all mod data into base .uasset files. Returns dtName → UAsset ready for Write().
    /// Returns null if AutoMod_P is missing (caller falls back to v1 TemplateInjector).
    /// </summary>
    public static Dictionary<string, UAsset>? InjectAll(
        string buildDir, string usmapPath, ModData[] mods, bool fillEmptySexSlots)
    {
        var autoModBase = Path.Combine(buildDir, "AutoMod_P");
        if (!Directory.Exists(autoModBase)) return null;

        Usmap? mappings = null;
        if (File.Exists(usmapPath))
            mappings = SafeLoad.Usmap(usmapPath);

        var result = new Dictionary<string, UAsset>();

        // ── DT_ClothesOutfit ──
        var clothesPath = Path.Combine(autoModBase, "WildLifeC/Content/DataTables/DT_ClothesOutfit.uasset");
        if (File.Exists(clothesPath))
        {
            var addMods = mods.Where(m => m.Variant == ModVariant.Add).ToArray();
            var asset = SafeLoad.UAsset(clothesPath, EngineVersion.VER_UE5_4, mappings);
            InjectClothesOutfit(asset, addMods);
            if (fillEmptySexSlots) FillEmptySexMeshSlots(asset);
            result["DT_ClothesOutfit"] = asset;
        }

        // ── DT_GameCharacterOutfits ──
        var outfitsPath = Path.Combine(autoModBase, "WildLifeC/Content/DataTables/NPC/DT_GameCharacterOutfits.uasset");
        if (File.Exists(outfitsPath))
        {
            var outfitMods = mods.Where(m => m.Variant == ModVariant.Add || m.Variant == ModVariant.Port).ToArray();
            var asset = SafeLoad.UAsset(outfitsPath, EngineVersion.VER_UE5_4, mappings);
            InjectGameCharacterOutfits(asset, outfitMods);
            result["DT_GameCharacterOutfits"] = asset;
        }

        // ── DT_GameCharacterCustomization ──
        var custPath = Path.Combine(autoModBase, "WildLifeC/Content/DataTables/NPC/DT_GameCharacterCustomization.uasset");
        if (File.Exists(custPath))
        {
            var custCharFile = Path.Combine(buildDir, "CustomizerCharacters.txt");
            var custChars = File.Exists(custCharFile)
                ? File.ReadAllLines(custCharFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray()
                : [];
            var custMods = mods.Where(m => m.Variant == ModVariant.CharacterCustomization).ToArray();
            var asset = SafeLoad.UAsset(custPath, EngineVersion.VER_UE5_4, mappings);
            InjectGameCharacterCustomization(asset, custMods, custChars);
            result["DT_GameCharacterCustomization"] = asset;
        }

        // ── DT_Tattoo ──
        var tattooPath = Path.Combine(autoModBase, "WildLifeC/Content/DataTables/DT_Tattoo.uasset");
        if (File.Exists(tattooPath))
        {
            var tattooMods = mods.Where(m => m.Variant == ModVariant.Tattoo).ToArray();
            var asset = SafeLoad.UAsset(tattooPath, EngineVersion.VER_UE5_4, mappings);
            InjectTattoo(asset, tattooMods);
            result["DT_Tattoo"] = asset;
        }

        return result;
    }

    // ── DT_ClothesOutfit ────────────────────────────────────────────────────

    static void InjectClothesOutfit(UAsset asset, ModData[] addMods)
    {
        if (addMods.Length == 0) return;
        var dtExport = asset.Exports[0] as DataTableExport;
        if (dtExport == null) return;

        var rows = dtExport.Table.Data;
        if (rows.Count == 0) return;

        // Use first row as clone template (has correct property structure)
        var templateRow = rows[0];

        foreach (var mod in addMods)
        {
            try
            {
                // Add all NameMap entries for this mod
                foreach (var nm in mod.NameMapEntries)
                    asset.AddNameReference(new FString(nm));

                var row = DeepClone(templateRow);
                row.Name = FName.FromString(asset, mod.ClothingId);

                // OutfitName
                var outfitName = Find<TextPropertyData>(row, "OutfitName");
                if (outfitName != null)
                    outfitName.CultureInvariantString = new FString(mod.ClothingName);

                // Slots
                for (int i = 0; i < SlotNames.Length; i++)
                {
                    var slotStruct = Find<StructPropertyData>(row, SlotNames[i]);
                    if (slotStruct == null) continue;
                    if (!mod.Slots.TryGetValue(SlotKeys[i], out var slotData)) continue;

                    // ── Reset cloned slot to clean defaults (Naked row has body-specific values) ──
                    SetInt(Find<IntPropertyData>(slotStruct, "CensoringAreas"), 0);

                    // Meshes array [0]=normal, [1]=sex
                    var meshes = Find<ArrayPropertyData>(slotStruct, "Meshes");
                    if (meshes?.Value != null && meshes.Value.Length >= 2)
                    {
                        SetSoftObject(asset, meshes.Value[0] as SoftObjectPropertyData,
                            slotData.MeshPath, slotData.MeshName);
                        SetSoftObject(asset, meshes.Value[1] as SoftObjectPropertyData,
                            slotData.SexMeshPath, slotData.SexMeshName);
                    }

                    // PreviewIcon
                    SetSoftObject(asset, Find<SoftObjectPropertyData>(slotStruct, "PreviewIcon"),
                        slotData.IconPath, slotData.IconName);

                    // Numeric fields — set ALL to mod values (overrides Naked defaults)
                    SetInt(Find<IntPropertyData>(slotStruct, "physicsAreas"), slotData.PhysicsAreas);
                    SetInt(Find<IntPropertyData>(slotStruct, "MuscleFlexRegions"), slotData.FlexRegions);
                    SetFloat(Find<FloatPropertyData>(slotStruct, "MorphTargetValue"), slotData.MorphTargetValue);
                    SetFloat(Find<FloatPropertyData>(slotStruct, "ArousalBlend"), slotData.ArousalBlend);

                    // MorphTarget — NamePropertyData. IsZero is ALWAYS false in v1 output.
                    var morphProp = Find<NamePropertyData>(slotStruct, "MorphTarget");
                    if (morphProp != null)
                    {
                        if (slotData.MorphTarget == "null")
                            morphProp.Value = FName.FromString(asset, "None");
                        else
                            morphProp.Value = FName.FromString(asset, slotData.MorphTarget);
                        morphProp.IsZero = false; // always false in game data
                    }

                    // ConstraintProfile — same pattern
                    var constraintProp = Find<NamePropertyData>(slotStruct, "ConstraintProfile");
                    if (constraintProp != null)
                    {
                        if (slotData.ConstraintProfile == "null")
                            constraintProp.Value = FName.FromString(asset, "None");
                        else
                            constraintProp.Value = FName.FromString(asset, slotData.ConstraintProfile);
                        constraintProp.IsZero = false;
                    }

                    // FurMasks array
                    var furMasks = Find<ArrayPropertyData>(slotStruct, "FurMasks");
                    if (furMasks?.Value != null)
                    {
                        bool hasFur = slotData.FurMaskPath != "None" && !string.IsNullOrEmpty(slotData.FurMaskPath);
                        if (hasFur)
                        {
                            // Create fur mask entries
                            var furEntries = new List<PropertyData>();
                            var normalFur = new SoftObjectPropertyData();
                            SetSoftObject(asset, normalFur, slotData.FurMaskPath, slotData.FurMaskName);
                            furEntries.Add(normalFur);

                            if (slotData.SexFurMaskPath != "None" && !string.IsNullOrEmpty(slotData.SexFurMaskPath))
                            {
                                var sexFur = new SoftObjectPropertyData();
                                SetSoftObject(asset, sexFur, slotData.SexFurMaskPath, slotData.SexFurMaskName);
                                furEntries.Add(sexFur);
                            }
                            furMasks.Value = furEntries.ToArray();
                        }
                        else
                        {
                            furMasks.Value = []; // empty
                        }
                    }
                }

                rows.Add(row);
            }
            catch (System.Exception ex)
            {
                Logger.EmitModError(mod.ModId, mod, $"v2-inject(ClothesOutfit): {ex.GetType().Name}: {ex.Message}");
                // Continue with next mod — partial NameMap orphans are harmless.
            }
        }
    }

    /// <summary>
    /// Fill empty sex-mesh slots on every BASE row: where Meshes[0] is set and
    /// Meshes[1] (the sex mesh) is empty, copy [0] → [1]. Meshes only — no furmask.
    /// Runs unconditionally on the loaded base asset (independent of add-mods).
    /// </summary>
    internal static void FillEmptySexMeshSlots(UAsset asset)
    {
        var dtExport = asset.Exports[0] as DataTableExport;
        if (dtExport == null) return;

        foreach (var row in dtExport.Table.Data)
        {
            foreach (var slotName in SlotNames)
            {
                var slotStruct = Find<StructPropertyData>(row, slotName);
                if (slotStruct == null) continue;

                var meshes = Find<ArrayPropertyData>(slotStruct, "Meshes");
                if (meshes?.Value == null || meshes.Value.Length < 2) continue;

                var regular = meshes.Value[0] as SoftObjectPropertyData;
                var sex = meshes.Value[1] as SoftObjectPropertyData;
                if (regular == null || sex == null) continue;

                // Copy regular → sex only when regular is set and sex is empty.
                // The FNames are already referenced by [0], so no AddNameReference.
                if (ShouldFillSexSlot(regular, sex, out var packagePath, out var assetName))
                    SetSoftObject(asset, sex, packagePath, assetName);
            }
        }
    }

    /// <summary>
    /// Decide whether the sex mesh (<paramref name="sex"/>) should be filled from the
    /// regular mesh (<paramref name="regular"/>): true only when regular is set and sex
    /// is empty. On true, outputs regular's package + asset name strings to copy across.
    /// Never overwrites a set sex mesh; never fabricates when regular is also empty.
    /// </summary>
    internal static bool ShouldFillSexSlot(
        SoftObjectPropertyData regular, SoftObjectPropertyData sex,
        out string packagePath, out string assetName)
    {
        packagePath = ReadPackageName(regular);
        assetName = ReadAssetName(regular);

        bool regularSet = packagePath != "None" && !string.IsNullOrEmpty(packagePath);
        var sexPackage = ReadPackageName(sex);
        bool sexEmpty = sexPackage == "None" || string.IsNullOrEmpty(sexPackage);

        return regularSet && sexEmpty;
    }

    static string ReadPackageName(SoftObjectPropertyData prop)
        => prop.Value.AssetPath.PackageName?.Value?.Value ?? "None";

    static string ReadAssetName(SoftObjectPropertyData prop)
        => prop.Value.AssetPath.AssetName?.Value?.Value ?? "None";

    // ── DT_GameCharacterOutfits ─────────────────────────────────────────────

    static void InjectGameCharacterOutfits(UAsset asset, ModData[] outfitMods)
    {
        if (outfitMods.Length == 0) return;
        var dtExport = asset.Exports[0] as DataTableExport;
        if (dtExport == null) return;

        var rows = dtExport.Table.Data;

        // The outfits DT has one row per character. Each row is a StructPropertyData
        // containing an "Outfits" ArrayPropertyData of DataTableRowHandle structs.
        // We find the matching character row and append handles.
        var rowNames = rows.Select(r => r.Name.ToString()).ToList();
        foreach (var unresolved in outfitMods.Where(m => OutfitRows.Resolve(m.Character, rowNames) == null))
            Logger.EmitModError(unresolved.ModId, unresolved, $"no outfit list for character '{unresolved.Character}'");

        foreach (var group in outfitMods.GroupBy(m => OutfitRows.Resolve(m.Character, rowNames)).Where(g => g.Key != null))
        {
            var charMods = group.ToArray();
            var charRow = rows.First(r => r.Name.ToString() == group.Key);

            // Find the Outfits array
            var outfitsArray = Find<ArrayPropertyData>(charRow, "Outfits");
            if (outfitsArray == null) continue;

            var handles = new List<PropertyData>(outfitsArray.Value);

            // Use existing handle as clone template (if any)
            StructPropertyData? handleTemplate = handles.OfType<StructPropertyData>().FirstOrDefault();

            // Continue the vanilla sequential numbering for struct element names
            int nextIndex = handles.Count;

            foreach (var mod in charMods)
            {
                try
                {
                    asset.AddNameReference(new FString(mod.ClothingId));

                    if (handleTemplate != null)
                    {
                        var handle = DeepClone(handleTemplate);
                        handle.Name = FName.FromString(asset, nextIndex.ToString());

                        var rowNameProp = Find<NamePropertyData>(handle, "RowName");
                        if (rowNameProp != null)
                            rowNameProp.Value = FName.FromString(asset, mod.ClothingId);

                        handles.Add(handle);
                        nextIndex++;
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.EmitModError(mod.ModId, mod, $"v2-inject(GameCharacterOutfits): {ex.GetType().Name}: {ex.Message}");
                    // Continue with next mod — partial NameMap orphans are harmless.
                }
            }

            outfitsArray.Value = handles.ToArray();
        }
    }

    // ── DT_GameCharacterCustomization ───────────────────────────────────────

    static readonly Dictionary<string, string> CustTargetToProperty = new()
    {
        ["Hair"] = "HairMeshes", ["Beard"] = "BeardMeshes",
        ["Skin"] = "SkinMaterials", ["PubicHair"] = "PubicHairMaterials",
        ["Eyes"] = "EyeMaterials", ["EyeLiner"] = "EyeLinerMaterials",
        ["EyeShadow"] = "EyeShadowMaterials", ["Lipstick"] = "LipstickMaterials",
        ["Tanlines"] = "TanlineMaterials",
    };

    static void InjectGameCharacterCustomization(UAsset asset, ModData[] custMods, string[] custChars)
    {
        if (custMods.Length == 0) return;
        var dtExport = asset.Exports[0] as DataTableExport;
        if (dtExport == null) return;

        var rows = dtExport.Table.Data;

        foreach (var charName in custChars)
        {
            var charModEntries = custMods
                .Where(m => m.Character.Equals(charName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(m => m.CustEntries)
                .ToList();
            if (charModEntries.Count == 0) continue;

            // Add NameMap entries
            foreach (var mod in custMods.Where(m => m.Character.Equals(charName, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    foreach (var nm in mod.NameMapEntries)
                        asset.AddNameReference(new FString(nm));
                }
                catch (System.Exception ex)
                {
                    Logger.EmitModError(mod.ModId, mod, $"v2-inject(GameCharacterCustomization): {ex.GetType().Name}: {ex.Message}");
                    // Continue with next mod — partial NameMap orphans are harmless.
                }
            }

            var charRow = rows.FirstOrDefault(r => r.Name.Value.Value == charName);
            if (charRow == null) continue;

            foreach (var (target, propName) in CustTargetToProperty)
            {
                var entries = charModEntries.Where(e => e.Target == target).ToList();
                if (entries.Count == 0) continue;

                var array = Find<ArrayPropertyData>(charRow, propName);
                if (array == null) continue;

                var items = new List<PropertyData>(array.Value);

                foreach (var entry in entries)
                {
                    if (target == "Hair")
                    {
                        // SkeletalMeshTuple struct
                        StructPropertyData? tmpl = items.OfType<StructPropertyData>().FirstOrDefault();
                        if (tmpl != null)
                        {
                            var clone = DeepClone(tmpl);
                            clone.Name = FName.FromString(asset, entry.Name);

                            SetSoftObject(asset, Find<SoftObjectPropertyData>(clone, "skinnedMesh"),
                                entry.Path, entry.Name);
                            SetSoftObject(asset, Find<SoftObjectPropertyData>(clone, "physicsMesh"),
                                entry.PhysicsPath, entry.PhysicsName);

                            items.Add(clone);
                        }
                    }
                    else
                    {
                        // SoftObjectPropertyData directly
                        var softObj = new SoftObjectPropertyData
                        {
                            Name = FName.FromString(asset, entry.Name),
                        };
                        SetSoftObject(asset, softObj, entry.Path, entry.Name);
                        items.Add(softObj);
                    }
                }

                array.Value = items.ToArray();
            }
        }
    }

    // ── DT_Tattoo ──────────────────────────────────────────────────────────

    static void InjectTattoo(UAsset asset, ModData[] tattooMods)
    {
        var allEntries = tattooMods.SelectMany(m => m.TattooEntries).ToList();
        if (allEntries.Count == 0) return;

        var dtExport = asset.Exports[0] as DataTableExport;
        if (dtExport == null) return;

        var rows = dtExport.Table.Data;
        if (rows.Count == 0) return;

        var templateRow = rows[0];

        // Add NameMap entries from all tattoo mods
        foreach (var mod in tattooMods)
        {
            try
            {
                foreach (var nm in mod.NameMapEntries)
                    asset.AddNameReference(new FString(nm));
            }
            catch (System.Exception ex)
            {
                Logger.EmitModError(mod.ModId, mod, $"v2-inject(Tattoo): {ex.GetType().Name}: {ex.Message}");
                // Continue with next mod — partial NameMap orphans are harmless.
            }
        }

        foreach (var tattoo in allEntries)
        {
            asset.AddNameReference(new FString(tattoo.TattooId));

            var row = DeepClone(templateRow);
            row.Name = FName.FromString(asset, tattoo.TattooId);

            // DisplayName
            var displayName = Find<TextPropertyData>(row, "DisplayName");
            if (displayName != null)
                displayName.CultureInvariantString = new FString(tattoo.DisplayName);

            // Texture
            SetSoftObject(asset, Find<SoftObjectPropertyData>(row, "Texture"),
                tattoo.TexturePath, tattoo.TextureName);

            // PreviewIcon
            SetSoftObject(asset, Find<SoftObjectPropertyData>(row, "PreviewIcon"),
                tattoo.IconPath, tattoo.IconName);

            // DefaultColor (LinearColor struct)
            var colorStruct = Find<StructPropertyData>(row, "DefaultColor");
            if (colorStruct != null)
            {
                var lc = colorStruct.Value.OfType<LinearColorPropertyData>().FirstOrDefault();
                if (lc != null)
                {
                    lc.Value = new FLinearColor(tattoo.ColorR, tattoo.ColorG, tattoo.ColorB, tattoo.ColorA);
                    lc.IsZero = false;
                }
            }

            // coveredSlots
            SetInt(Find<IntPropertyData>(row, "coveredSlots"), tattoo.CoveredSlots);

            // Cost
            SetInt(Find<IntPropertyData>(row, "Cost"), tattoo.Cost);

            // UVSet (EnumPropertyData)
            var uvSet = Find<EnumPropertyData>(row, "UVSet");
            if (uvSet != null)
            {
                uvSet.Value = FName.FromString(asset, tattoo.UVSet);
                uvSet.IsZero = false;
            }

            // TraderType (EnumPropertyData) — "None" means unset/default in UE.
            // v1 uses JSON null, v2 uses FName "None" — both produce identical binary
            // because UAssetAPI treats FName("None") as the default/unset enum value.
            var trader = Find<EnumPropertyData>(row, "TraderType");
            if (trader != null)
            {
                if (tattoo.TraderType == "None" || string.IsNullOrEmpty(tattoo.TraderType))
                    trader.Value = FName.FromString(asset, "None");
                else
                    trader.Value = FName.FromString(asset, tattoo.TraderType);
                trader.IsZero = false;
            }

            rows.Add(row);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Recursive deep clone: UAssetAPI's Clone() copies structs but shares array elements.
    /// This method ensures ALL nested PropertyData objects are independent copies.
    /// </summary>
    internal static StructPropertyData DeepClone(StructPropertyData src)
    {
        var clone = (StructPropertyData)src.Clone();
        // Clone() creates new Value list with same element refs — replace each element
        for (int i = 0; i < clone.Value.Count; i++)
        {
            clone.Value[i] = DeepCloneProperty(clone.Value[i]);
        }
        return clone;
    }

    static PropertyData DeepCloneProperty(PropertyData prop)
    {
        if (prop is StructPropertyData sp)
        {
            var c = (StructPropertyData)sp.Clone();
            for (int i = 0; i < c.Value.Count; i++)
                c.Value[i] = DeepCloneProperty(c.Value[i]);
            return c;
        }
        if (prop is ArrayPropertyData ap)
        {
            var c = (ArrayPropertyData)ap.Clone();
            if (c.Value != null)
            {
                var newArr = new PropertyData[c.Value.Length];
                for (int i = 0; i < c.Value.Length; i++)
                    newArr[i] = DeepCloneProperty(c.Value[i]);
                c.Value = newArr;
            }
            return c;
        }
        // For leaf types (SoftObject, Int, Float, Bool, Name, Text, Enum, Object),
        // Clone() is sufficient since their values are value types or immutable.
        return (PropertyData)prop.Clone();
    }

    internal static T? Find<T>(StructPropertyData s, string name) where T : PropertyData
        => s.Value.OfType<T>().FirstOrDefault(p => p.Name.Value.Value == name);

    internal static void SetSoftObject(UAsset asset, SoftObjectPropertyData? prop, string packagePath, string assetName)
    {
        if (prop == null) return;
        // SoftObjectPropertyData.IsZero is ALWAYS false in game data.
        prop.IsZero = false;
        if (packagePath == "None" || string.IsNullOrEmpty(packagePath))
        {
            // "None" path: use null SubPathString (matches v1 JSON output)
            var none = FName.FromString(asset, "None");
            prop.Value = new FSoftObjectPath(
                new FTopLevelAssetPath(none, none), null);
        }
        else
        {
            asset.AddNameReference(new FString(packagePath));
            asset.AddNameReference(new FString(assetName));
            prop.Value = new FSoftObjectPath(
                new FTopLevelAssetPath(
                    FName.FromString(asset, packagePath),
                    FName.FromString(asset, assetName)),
                null); // SubPathString = null (matches v1)
        }
    }

    internal static void SetInt(IntPropertyData? prop, int value)
    {
        if (prop == null) return;
        prop.Value = value;
        prop.IsZero = value == 0;
    }

    static void SetFloat(FloatPropertyData? prop, float value)
    {
        if (prop == null) return;
        prop.Value = value;
        prop.IsZero = value == 0f;
    }
}
