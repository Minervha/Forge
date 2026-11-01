/// <summary>
/// Maps a character name to its row in DT_GameCharacterOutfits. Row names don't follow the character
/// names ("SexBot" is Outfits_SexBot_AB_NSFW) and some are prefixes of others (Outfits_Shey_AB and
/// Outfits_SheyFuta), so a bare StartsWith would depend on the row order of the current game build.
/// </summary>
public static class OutfitRows
{
    static readonly string[] SuffixPatterns = [
        "_AB_NSFW", "_NewModel", "_AB2", "_AB3", "_AB", "_A2", "_A", "_B", "_C", "_2", "_3"
    ];

    /// <summary>The name written to Characters.txt for a row.</summary>
    public static string CleanName(string rowName)
    {
        var name = rowName;
        if (name.StartsWith("Outfits_"))
            name = name["Outfits_".Length..];

        foreach (var suffix in SuffixPatterns)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        if (name == "RyanYoung") name = "Ryan";
        if (name == "Max" && rowName.Contains("NewModel")) name = "MaxB";

        if (name.Length > 2 && char.IsUpper(name[^1]) && char.IsLower(name[^2])
            && name != "SexBot" && name != "MaxB")
        {
            var candidate = name[..^1];
            if (rowName.StartsWith("Outfits_") && candidate.Length > 2)
                name = candidate;
        }

        return name;
    }

    /// <summary>
    /// Exact row, then the first "Outfits_{character}_*" row (Max keeps its vanilla list, Outfits_Max_NewModel),
    /// then the clean name. Returns null when the character has no list.
    /// </summary>
    public static string? Resolve(string character, IReadOnlyCollection<string> rowNames)
    {
        const StringComparison Ci = StringComparison.OrdinalIgnoreCase;
        var exact = $"Outfits_{character}";
        return rowNames.FirstOrDefault(r => r.Equals(exact, Ci))
            ?? rowNames.FirstOrDefault(r => r.StartsWith(exact + "_", Ci))
            ?? rowNames.FirstOrDefault(r => CleanName(r).Equals(character, Ci));
    }
}
