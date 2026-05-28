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
}
