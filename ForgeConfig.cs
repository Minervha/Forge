using Newtonsoft.Json;

public class ForgeConfig
{
    [JsonProperty("buildDir")]  public string BuildDir { get; set; } = "";
    [JsonProperty("modsDir")]   public string ModsDir { get; set; } = "";
    [JsonProperty("outputDir")] public string OutputDir { get; set; } = "";
    [JsonProperty("usmapPath")] public string UsmapPath { get; set; } = "";
    [JsonProperty("repakExe")]  public string RepakExe { get; set; } = "";
    [JsonProperty("pakName")]   public string PakName { get; set; } = "";
    [JsonProperty("userId")]    public string UserId { get; set; } = "";
    [JsonProperty("mods")]      public string[] Mods { get; set; } = [];

    // Per-mod content directory override. When a modId is present here, the
    // mapped absolute path replaces the implicit ModsDir/<modId> lookup —
    // letting Studio point most mods at the ModCache directly while keeping
    // a small overlay dir for the ones needing asset filtering.
    // Null on legacy configs; resolution happens through ModDirResolver.Resolve().
    [JsonProperty("modSources")] public Dictionary<string, string>? ModSources { get; set; }
}
