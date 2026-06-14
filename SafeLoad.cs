using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

/// <summary>
/// Concurrency-safe loaders for UAssetAPI inputs.
///
/// UAssetAPI opens files with <c>FileShare.None</c> (exclusive). When several
/// <c>generate</c> jobs run at once against the SAME build they share read-only
/// inputs — the <c>.usmap</c> and the <c>AutoMod_P</c> base <c>.uasset</c> files —
/// and the simultaneous exclusive opens collide, aborting all-but-one process
/// (on Linux .NET emulates share modes via <c>flock</c>, so it bites there too).
///
/// We sidestep it by copying the input to a unique temp path first (<see cref="File.Copy"/>
/// opens the source with shared access) and letting UAssetAPI open the private copy.
/// The copy reuses UAssetAPI's own path-based loading (incl. usmap decompression),
/// so behaviour is unchanged — only the file handle is no longer contended.
/// </summary>
public static class SafeLoad
{
    public static Usmap Usmap(string usmapPath)
    {
        var tmp = TempCopy(usmapPath);
        try { return new Usmap(tmp); }
        finally { TryDelete(tmp); }
    }

    public static UAsset UAsset(string uassetPath, EngineVersion version, Usmap? mappings)
    {
        var tmp = TempCopy(uassetPath);
        // UAsset also reads the sibling .uexp — copy it next to the temp .uasset.
        var srcUexp = Path.ChangeExtension(uassetPath, ".uexp");
        var tmpUexp = Path.ChangeExtension(tmp, ".uexp");
        var hasUexp = File.Exists(srcUexp);
        if (hasUexp) File.Copy(srcUexp, tmpUexp, true);
        try { return new UAsset(tmp, version, mappings); }
        finally
        {
            TryDelete(tmp);
            if (hasUexp) TryDelete(tmpUexp);
        }
    }

    static string TempCopy(string src)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dtf_in");
        Directory.CreateDirectory(dir);
        var dst = Path.Combine(dir, $"{Guid.NewGuid():N}{Path.GetExtension(src)}");
        File.Copy(src, dst, true);
        return dst;
    }

    static void TryDelete(string p)
    {
        try { File.Delete(p); } catch { /* orphan temp swept by /tmp cleanup */ }
    }
}
