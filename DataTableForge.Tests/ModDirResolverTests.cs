using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

public class ModDirResolverTests
{
    [Fact]
    public void Resolve_returns_ModsDir_join_modId_when_ModSources_is_null()
    {
        var cfg = new ForgeConfig { ModsDir = "C:/cache", ModSources = null };
        var result = ModDirResolver.Resolve(cfg, "abc-123");
        Assert.Equal(Path.Combine("C:/cache", "abc-123"), result);
    }

    [Fact]
    public void Resolve_falls_back_when_ModSources_does_not_contain_the_modId()
    {
        var cfg = new ForgeConfig
        {
            ModsDir = "C:/cache",
            ModSources = new Dictionary<string, string> { ["other-mod"] = "C:/overlay/other-mod" },
        };
        var result = ModDirResolver.Resolve(cfg, "abc-123");
        Assert.Equal(Path.Combine("C:/cache", "abc-123"), result);
    }

    [Fact]
    public void Resolve_returns_override_when_ModSources_contains_the_modId()
    {
        var overridePath = "D:/staging-overlay/abc-123";
        var cfg = new ForgeConfig
        {
            ModsDir = "C:/cache",
            ModSources = new Dictionary<string, string> { ["abc-123"] = overridePath },
        };
        var result = ModDirResolver.Resolve(cfg, "abc-123");
        Assert.Equal(overridePath, result);
    }

    [Fact]
    public void Resolve_falls_back_when_override_value_is_empty()
    {
        // Defensive: an empty override is treated as "no override" rather than
        // a valid (broken) path. The fallback may still not exist, but at least
        // the error message will point at the meaningful default location.
        var cfg = new ForgeConfig
        {
            ModsDir = "C:/cache",
            ModSources = new Dictionary<string, string> { ["abc-123"] = "" },
        };
        var result = ModDirResolver.Resolve(cfg, "abc-123");
        Assert.Equal(Path.Combine("C:/cache", "abc-123"), result);
    }

    [Fact]
    public void Resolve_throws_on_null_config()
    {
        Assert.Throws<ArgumentNullException>(() => ModDirResolver.Resolve(null!, "abc"));
    }

    [Fact]
    public void Resolve_throws_on_empty_modId()
    {
        var cfg = new ForgeConfig { ModsDir = "C:/cache" };
        Assert.Throws<ArgumentException>(() => ModDirResolver.Resolve(cfg, ""));
    }

    [Fact]
    public void Resolve_picks_override_independently_per_modId_in_the_same_run()
    {
        var cfg = new ForgeConfig
        {
            ModsDir = "C:/cache",
            ModSources = new Dictionary<string, string>
            {
                ["filtered-mod"] = "D:/overlay/filtered-mod",
                // direct-mod intentionally absent — must fall back
            },
        };
        Assert.Equal("D:/overlay/filtered-mod", ModDirResolver.Resolve(cfg, "filtered-mod"));
        Assert.Equal(Path.Combine("C:/cache", "direct-mod"), ModDirResolver.Resolve(cfg, "direct-mod"));
    }

    static string TempModDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"forge-moddir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ListRuleFiles_reads_the_mod_root_and_AutoMod()
    {
        var dir = TempModDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "AutoMod"));
            File.WriteAllText(Path.Combine(dir, "AutoMod", "b.txt"), "x");

            var names = ModDirResolver.ListRuleFiles(dir).Select(Path.GetFileName).OrderBy(n => n);

            Assert.Equal(new[] { "a.txt", "b.txt" }, names);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CountAssetFiles_counts_files_under_WildLifeC_and_is_zero_without_it()
    {
        var dir = TempModDir();
        try
        {
            Assert.Equal(0, ModDirResolver.CountAssetFiles(dir));

            var deep = Path.Combine(dir, "WildLifeC", "Content", "X");
            Directory.CreateDirectory(deep);
            File.WriteAllText(Path.Combine(deep, "a.uasset"), "x");
            File.WriteAllText(Path.Combine(deep, "a.uexp"), "x");

            Assert.Equal(2, ModDirResolver.CountAssetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
