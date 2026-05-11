using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

public class JsonDiagnosticsTests {
    static JObject AsJObject(object result) => JObject.FromObject(result);

    [Fact]
    public void Returns_zero_findings_for_clean_JSON() {
        var json = @"{
  ""NameMap"": [
    ""Foo"",
    ""Bar""
  ],
  ""Exports"": [{
    ""MeshPath"": ""/Path/To/Mesh""
  }]
}";
        var result = AsJObject(JsonDiagnostics.DiagnoseEmptyFString(json));

        Assert.Equal(0, result["totalNameMapEmpties"]?.Value<int>());
        Assert.Equal(0, result["totalFieldEmpties"]?.Value<int>());
        Assert.Empty((JArray)result["nameMapEmpties"]!);
        Assert.Empty((JArray)result["fieldEmpties"]!);
    }

    [Fact]
    public void Detects_a_single_empty_NameMap_entry_with_index_and_line_number() {
        var json = @"{
  ""NameMap"": [
    ""Foo"",
    """",
    ""Bar""
  ]
}";
        var result = AsJObject(JsonDiagnostics.DiagnoseEmptyFString(json));

        Assert.Equal(1, result["totalNameMapEmpties"]?.Value<int>());
        var empties = (JArray)result["nameMapEmpties"]!;
        Assert.Single(empties);
        Assert.Equal(1, empties[0]["index"]?.Value<int>());
        Assert.Equal(4, empties[0]["lineNumber"]?.Value<int>());
    }

    [Fact]
    public void Counts_all_NameMap_empties_but_caps_reported_list() {
        var entries = string.Join(",\n    ", Enumerable.Repeat("\"\"", 20));
        var json = $@"{{
  ""NameMap"": [
    {entries}
  ]
}}";
        var result = AsJObject(JsonDiagnostics.DiagnoseEmptyFString(json));

        Assert.Equal(20, result["totalNameMapEmpties"]?.Value<int>());
        Assert.True(((JArray)result["nameMapEmpties"]!).Count <= 5,
            "Reported list must be capped at 5 to keep event payload bounded");
    }

    [Fact]
    public void Detects_empty_field_value_with_name_and_snippet() {
        var json = @"{
  ""Exports"": [{
    ""MeshPath"": """",
    ""MeshName"": ""Foo""
  }]
}";
        var result = AsJObject(JsonDiagnostics.DiagnoseEmptyFString(json));

        var fields = (JArray)result["fieldEmpties"]!;
        Assert.Single(fields);
        Assert.Equal("MeshPath", fields[0]["fieldName"]?.ToString());
        Assert.Equal(3, fields[0]["lineNumber"]?.Value<int>());
        Assert.Contains("MeshPath", fields[0]["snippet"]?.ToString() ?? "");
    }

    [Fact]
    public void Reports_NameMap_and_field_empties_independently() {
        var json = @"{
  ""NameMap"": [
    ""Foo"",
    """"
  ],
  ""Exports"": [{
    ""FurMaskPath"": """"
  }]
}";
        var result = AsJObject(JsonDiagnostics.DiagnoseEmptyFString(json));

        Assert.Equal(1, result["totalNameMapEmpties"]?.Value<int>());
        Assert.Equal(1, result["totalFieldEmpties"]?.Value<int>());
        Assert.Equal("FurMaskPath", ((JArray)result["fieldEmpties"]!)[0]["fieldName"]?.ToString());
    }

    [Fact]
    public void Does_not_misidentify_field_empties_inside_NameMap_section() {
        // Empties inside NameMap are NameMap empties, not field empties.
        var json = @"{
  ""NameMap"": [
    """"
  ]
}";
        var result = AsJObject(JsonDiagnostics.DiagnoseEmptyFString(json));

        Assert.Equal(1, result["totalNameMapEmpties"]?.Value<int>());
        Assert.Equal(0, result["totalFieldEmpties"]?.Value<int>());
    }

    [Fact]
    public void Tolerates_whitespace_around_field_value() {
        var json = "{ \"MeshPath\":   \"\" }";
        var result = AsJObject(JsonDiagnostics.DiagnoseEmptyFString(json));

        Assert.Equal(1, result["totalFieldEmpties"]?.Value<int>());
    }

    [Fact]
    public void Caps_field_empties_at_ten_reported_but_counts_all() {
        var lines = string.Join("\n", Enumerable.Range(0, 15).Select(i => $"  \"Field{i}\": \"\","));
        var json = "{\n" + lines + "\n}";
        var result = AsJObject(JsonDiagnostics.DiagnoseEmptyFString(json));

        Assert.Equal(15, result["totalFieldEmpties"]?.Value<int>());
        Assert.True(((JArray)result["fieldEmpties"]!).Count <= 10);
    }

    [Fact]
    public void Returns_safe_result_on_empty_input() {
        var result = AsJObject(JsonDiagnostics.DiagnoseEmptyFString(""));

        Assert.Equal(0, result["totalNameMapEmpties"]?.Value<int>());
        Assert.Equal(0, result["totalFieldEmpties"]?.Value<int>());
    }
}
