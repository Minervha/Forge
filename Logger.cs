using System.Threading;
using Newtonsoft.Json;

public static class Logger {
    const string EVENT_PREFIX = "##FORGE_EVENT##";

    static int _modErrorCount = 0;
    public static int ModErrorCount => _modErrorCount;

    /// <summary>Test-only: reset counters between tests.</summary>
    internal static void ResetCountersForTesting() {
        Interlocked.Exchange(ref _modErrorCount, 0);
    }

    public static void EmitEvent(string stage, object payload) {
        // SerializeObject default settings produce single-line JSON, safe for line-based parsing.
        var line = EVENT_PREFIX + JsonConvert.SerializeObject(new { stage, payload });
        System.Console.WriteLine(line);
        System.Console.Out.Flush();
    }

    public static void EmitModError(string modId, ModData? mod, string reason) {
        Interlocked.Increment(ref _modErrorCount);
        EmitEvent("mod-error", new {
            modId,
            modName = mod?.ModName ?? modId,
            sourcePath = mod?.SourcePath ?? "",
            variant = mod?.Variant.ToString() ?? "Unknown",
            reason
        });
    }

    public static void EmitSummary(int totalMods, bool pakOk) {
        EmitEvent("summary", new {
            totalMods,
            failedMods = _modErrorCount,
            succeededMods = totalMods - _modErrorCount,
            pakOk
        });
    }
}
