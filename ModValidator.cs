using System.Collections.Generic;
using System.Linq;

// Invariants currently checked:
//   - NameMapEntries: no null / empty / whitespace
//   - Add: non-empty ClothingId
//   - Port: non-empty Character
//   - Tattoo: non-empty TattooEntries with non-empty TattooId on each
//   - Character: id without spaces, display name, base and mesh
//   - OutfitSet: source and target characters
//
// Invariants deferred (add here when a real failure shape demands it):
//   - SlotData paths/names: non-"None" values must also be non-empty (no "" / "None" mixes)
//   - CustEntries field-level checks
public static class ModValidator {
    /// <summary>
    /// Returns a list of human-readable invariant violations for <paramref name="mod"/>.
    /// An empty list means the mod is structurally valid.
    /// </summary>
    public static List<string> Validate(ModData mod) {
        var errors = new List<string>();

        // Invariant: NameMapEntries must not contain null/empty/whitespace strings.
        // Empty FStrings crash UAssetAPI.UAsset.AddNameReference, the user's reported bug.
        if (mod.NameMapEntries != null) {
            for (int i = 0; i < mod.NameMapEntries.Length; i++) {
                if (string.IsNullOrWhiteSpace(mod.NameMapEntries[i])) {
                    errors.Add($"NameMap entry #{i} is empty or whitespace");
                }
            }
        }

        // Variant-specific invariants.
        switch (mod.Variant) {
            case ModVariant.Add:
                if (string.IsNullOrWhiteSpace(mod.ClothingId))
                    errors.Add("ClothingId is empty (required for Add variant)");
                break;

            case ModVariant.Port:
                if (string.IsNullOrWhiteSpace(mod.Character))
                    errors.Add("Character is empty (required for Port variant)");
                break;

            case ModVariant.Tattoo:
                if (mod.TattooEntries == null || mod.TattooEntries.Count == 0) {
                    errors.Add("TattooEntries is empty (required for Tattoo variant)");
                } else {
                    for (int i = 0; i < mod.TattooEntries.Count; i++) {
                        if (string.IsNullOrWhiteSpace(mod.TattooEntries[i].TattooId))
                            errors.Add($"TattooEntries[{i}].TattooId is empty");
                    }
                }
                break;

            case ModVariant.Character:
                var c = mod.CharacterEntry;
                if (c == null || string.IsNullOrWhiteSpace(c.Id) || c.Id.Any(char.IsWhiteSpace))
                    errors.Add("[CHARACTER_ID] is empty or contains spaces");
                if (c == null || string.IsNullOrWhiteSpace(c.DisplayName))
                    errors.Add("[CHARACTER_DISPLAY_NAME] is empty");
                if (c == null || string.IsNullOrWhiteSpace(c.Base))
                    errors.Add("[CHARACTER_BASE] is empty");
                if (c == null || string.IsNullOrWhiteSpace(c.MeshPath) || string.IsNullOrWhiteSpace(c.MeshName))
                    errors.Add("[CHARACTER_MESH_PATH] and [CHARACTER_MESH_NAME] are required");
                break;

            case ModVariant.OutfitSet:
                if (string.IsNullOrWhiteSpace(mod.OutfitsFrom) || string.IsNullOrWhiteSpace(mod.OutfitsTo))
                    errors.Add("[OUTFITS_FROM] and [OUTFITS_TO] are required");
                break;

            case ModVariant.CharacterCustomization:
                // Per spec — no required field at this level. CustEntries may legitimately
                // be empty (mod adds nothing to that character). Future invariants can be
                // added here as new failure shapes emerge.
                break;
        }

        return errors;
    }

    /// <summary>
    /// Adds every name the injectors will reference to the mod's NameMap. A value missing
    /// from the NameMap makes UAssetAPI abort the whole table on write, which takes every
    /// other mod down with it, so an incomplete NameMap is repaired rather than rejected.
    /// </summary>
    public static void CompleteNameMap(ModData mod) {
        var names = new List<string>(mod.NameMapEntries ?? []);
        void Add(string? value) {
            if (string.IsNullOrWhiteSpace(value) || value == "None" || value == "null") return;
            if (!names.Contains(value)) names.Add(value);
        }

        if (mod.Variant is ModVariant.Add or ModVariant.Port) Add(mod.ClothingId);
        foreach (var slot in mod.Slots.Values) {
            Add(slot.MeshPath); Add(slot.MeshName);
            Add(slot.SexMeshPath); Add(slot.SexMeshName);
            Add(slot.IconPath); Add(slot.IconName);
            Add(slot.MorphTarget); Add(slot.ConstraintProfile);
            Add(slot.FurMaskPath); Add(slot.FurMaskName);
            Add(slot.SexFurMaskPath); Add(slot.SexFurMaskName);
        }
        foreach (var entry in mod.CustEntries) {
            Add(entry.Path); Add(entry.Name);
            Add(entry.PhysicsPath); Add(entry.PhysicsName);
        }
        foreach (var tattoo in mod.TattooEntries) {
            Add(tattoo.TattooId);
            Add(tattoo.TexturePath); Add(tattoo.TextureName);
            Add(tattoo.IconPath); Add(tattoo.IconName);
        }
        mod.NameMapEntries = names.ToArray();
    }

    /// <summary>
    /// Warning for an outfit attached to a character this build does not know: the game
    /// would never offer it. Null when fine, or when the build has no character list.
    /// </summary>
    public static string? FindCharacterWarning(ModData mod, string[] characters) {
        if (mod.Variant is not (ModVariant.Add or ModVariant.Port)) return null;
        if (characters.Length == 0) return null;
        if (characters.Contains(mod.Character, System.StringComparer.OrdinalIgnoreCase)) return null;
        return string.IsNullOrWhiteSpace(mod.Character)
            ? "No character set: this outfit will not appear in the game"
            : $"Unknown character '{mod.Character}' in this game version: this outfit will not appear in the game";
    }

    /// <summary>Mods owning a name quoted in a fatal error message, to attribute the failure.</summary>
    public static List<string> FindSuspects(string message, IEnumerable<ModData> mods) {
        var quoted = System.Text.RegularExpressions.Regex.Matches(message, "'([^']+)'")
            .Select(m => m.Groups[1].Value).ToHashSet();
        return mods
            .Where(m => quoted.Contains(m.ClothingId)
                || m.TattooEntries.Any(t => quoted.Contains(t.TattooId))
                || m.NameMapEntries.Any(quoted.Contains))
            .Select(m => m.ModId)
            .Distinct()
            .ToList();
    }
}
