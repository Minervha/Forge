using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

public class LoggerTests {
    // Capture Console.Out for the duration of one test, restoring after.
    static string Capture(System.Action action) {
        var prev = System.Console.Out;
        using var sw = new StringWriter();
        System.Console.SetOut(sw);
        try { action(); } finally { System.Console.SetOut(prev); }
        return sw.ToString();
    }

    [Fact]
    public void EmitEvent_writes_a_single_line_with_the_prefix_and_valid_json() {
        var output = Capture(() => Logger.EmitEvent("test-stage", new { foo = 1, bar = "baz" }));

        Assert.StartsWith("##FORGE_EVENT##", output.TrimEnd());

        var jsonPart = output.TrimEnd().Substring("##FORGE_EVENT##".Length);
        var obj = JObject.Parse(jsonPart);
        Assert.Equal("test-stage", obj["stage"]?.ToString());
        Assert.Equal(1, obj["payload"]?["foo"]?.Value<int>());
        Assert.Equal("baz", obj["payload"]?["bar"]?.ToString());

        // No embedded newlines inside the event line itself
        var trimmed = output.TrimEnd('\r', '\n');
        Assert.DoesNotContain('\n', trimmed);
    }

    [Fact]
    public void EmitModError_increments_the_static_counter_each_call() {
        Logger.ResetCountersForTesting();

        Capture(() => {
            Logger.EmitModError("mod_a", null, "first failure");
            Logger.EmitModError("mod_b", null, "second failure");
        });

        Assert.Equal(2, Logger.ModErrorCount);
    }

    [Fact]
    public void EmitModError_writes_modId_and_reason_in_payload_with_safe_fallbacks_when_mod_is_null() {
        Logger.ResetCountersForTesting();

        var output = Capture(() => Logger.EmitModError("mod_xyz", null, "boom"));

        var jsonPart = output.TrimEnd().Substring("##FORGE_EVENT##".Length);
        var obj = JObject.Parse(jsonPart);
        Assert.Equal("mod-error", obj["stage"]?.ToString());
        Assert.Equal("mod_xyz", obj["payload"]?["modId"]?.ToString());
        Assert.Equal("mod_xyz", obj["payload"]?["modName"]?.ToString());     // falls back to modId
        Assert.Equal("", obj["payload"]?["sourcePath"]?.ToString());
        Assert.Equal("Unknown", obj["payload"]?["variant"]?.ToString());
        Assert.Equal("boom", obj["payload"]?["reason"]?.ToString());
    }

    [Fact]
    public void EmitModError_uses_modName_and_sourcePath_from_ModData_when_provided() {
        Logger.ResetCountersForTesting();

        var mod = new ModData {
            ModName = "Cute Hat",
            SourcePath = @"C:\cache\mod_42\hat.txt",
            Variant = ModVariant.Add,
        };

        var output = Capture(() => Logger.EmitModError("mod_42", mod, "broken"));

        var jsonPart = output.TrimEnd().Substring("##FORGE_EVENT##".Length);
        var obj = JObject.Parse(jsonPart);
        Assert.Equal("mod_42", obj["payload"]?["modId"]?.ToString());
        Assert.Equal("Cute Hat", obj["payload"]?["modName"]?.ToString());
        Assert.Equal(@"C:\cache\mod_42\hat.txt", obj["payload"]?["sourcePath"]?.ToString());
        Assert.Equal("Add", obj["payload"]?["variant"]?.ToString());
    }

    [Fact]
    public void EmitSummary_reads_modErrorCount_and_computes_succeeded_from_total() {
        Logger.ResetCountersForTesting();
        Capture(() => {
            Logger.EmitModError("a", null, "x");
            Logger.EmitModError("b", null, "y");
        });

        var output = Capture(() => Logger.EmitSummary(totalMods: 10, pakOk: true));

        // Last line is the summary
        var summaryLine = output.TrimEnd().Split('\n')[^1];
        var jsonPart = summaryLine.Substring(summaryLine.IndexOf("##FORGE_EVENT##") + "##FORGE_EVENT##".Length);
        var obj = JObject.Parse(jsonPart);
        Assert.Equal("summary", obj["stage"]?.ToString());
        Assert.Equal(10, obj["payload"]?["totalMods"]?.Value<int>());
        Assert.Equal(2, obj["payload"]?["failedMods"]?.Value<int>());
        Assert.Equal(8, obj["payload"]?["succeededMods"]?.Value<int>());
        Assert.True(obj["payload"]?["pakOk"]?.Value<bool>());
    }
}
