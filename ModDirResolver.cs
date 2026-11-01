// Resolves the absolute content directory for a given mod id.
//
// Default contract: ModsDir/<modId>/ — every mod's content lives in a single
// shared parent (the "staging" dir Studio used to assemble for every run).
//
// Override contract (introduced when Studio stopped duplicating ModCache into
// a per-run staging dir): ForgeConfig.ModSources may carry an explicit path
// per modId. Studio sets ModsDir = ModCache root and only emits ModSources
// entries for the mods that need filtering (disabled assets, legacy Unpacked/
// layout). Forge prefers the override when present and falls back otherwise,
// which keeps both legacy and new config files working unchanged.

public static class ModDirResolver
{
    /// <summary>
    /// Resolve the absolute directory holding a mod's content (.txt rules + WildLifeC/).
    /// </summary>
    /// <remarks>
    /// Returns the override from <see cref="ForgeConfig.ModSources"/> when it
    /// carries <paramref name="modId"/>, otherwise <c>Path.Combine(config.ModsDir, modId)</c>.
    /// The caller is responsible for handling a missing directory — this method
    /// never inspects the filesystem.
    /// </remarks>
    public static string Resolve(ForgeConfig config, string modId)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrEmpty(modId)) throw new ArgumentException("modId must be non-empty", nameof(modId));

        if (config.ModSources is { } sources
            && sources.TryGetValue(modId, out var overridePath)
            && !string.IsNullOrEmpty(overridePath))
        {
            return overridePath;
        }
        return Path.Combine(config.ModsDir, modId);
    }

    /// <summary>Rule files of a mod: its root plus AutoMod/, where legacy wlmm imports keep them.</summary>
    public static List<string> ListRuleFiles(string modDir)
    {
        var ruleFiles = Directory.GetFiles(modDir, "*.txt").ToList();
        var autoModDir = Path.Combine(modDir, "AutoMod");
        if (Directory.Exists(autoModDir))
            ruleFiles.AddRange(Directory.GetFiles(autoModDir, "*.txt"));
        return ruleFiles;
    }

    /// <summary>Number of game files the mod would add to the pak (everything under WildLifeC/).</summary>
    public static int CountAssetFiles(string modDir)
    {
        var wildLife = Path.Combine(modDir, "WildLifeC");
        return Directory.Exists(wildLife)
            ? Directory.GetFiles(wildLife, "*", SearchOption.AllDirectories).Length
            : 0;
    }
}
