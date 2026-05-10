using System.Collections.Generic;
using System.Linq;

// Invariants currently checked:
//   - NameMapEntries: no null / empty / whitespace
//   - Add: non-empty ClothingId
//   - Port: non-empty Character
//   - Tattoo: non-empty TattooEntries with non-empty TattooId on each
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

            case ModVariant.CharacterCustomization:
                // Per spec — no required field at this level. CustEntries may legitimately
                // be empty (mod adds nothing to that character). Future invariants can be
                // added here as new failure shapes emerge.
                break;
        }

        return errors;
    }
}
