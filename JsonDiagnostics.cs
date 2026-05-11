using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Post-mortem JSON scanner used when UAssetAPI throws on a malformed
/// merged DataTable JSON (the "Cannot add an empty FString" crash).
///
/// Locates two failure shapes:
///   1. Empty entries inside the "NameMap" array (e.g. "NameMap":[ "Foo", "", "Bar" ])
///   2. Empty string values on FName-shaped fields elsewhere (e.g. "MeshPath": "")
///
/// Findings are bounded (top 5 NameMap empties, top 10 field empties) so the
/// emitted event payload stays small enough to fit in a single stdout line.
/// </summary>
public static class JsonDiagnostics {
    const int MAX_NAMEMAP_REPORTED = 5;
    const int MAX_FIELD_REPORTED = 10;
    const int SNIPPET_MAX_LEN = 120;

    static readonly Regex EmptyFieldRx = new(
        @"""([A-Za-z_][A-Za-z0-9_]*)""\s*:\s*""""",
        RegexOptions.Compiled);

    /// <summary>
    /// Scans the input JSON for empty FString positions that would crash
    /// UAssetAPI.UAsset.AddNameReference. Always returns a result object — never throws.
    /// </summary>
    public static object DiagnoseEmptyFString(string json) {
        var nameMapEmpties = new List<object>();
        var fieldEmpties = new List<object>();
        int totalNameMapEmpties = 0;
        int totalFieldEmpties = 0;
        int totalLines = 0;

        if (string.IsNullOrEmpty(json)) {
            return new {
                totalNameMapEmpties = 0,
                totalFieldEmpties = 0,
                nameMapEmpties = System.Array.Empty<object>(),
                fieldEmpties = System.Array.Empty<object>(),
                totalLines = 0,
            };
        }

        var lines = json.Split('\n');
        totalLines = lines.Length;

        bool inNameMap = false;
        int nameMapIndex = 0;

        for (int i = 0; i < lines.Length; i++) {
            var line = lines[i];
            var trimmed = line.TrimEnd('\r').Trim();

            // Enter NameMap section. Matches both `"NameMap": [` (start of array)
            // and the merged-template variant `"NameMap": [\n    "Foo",\n    ...`.
            if (!inNameMap && trimmed.Contains("\"NameMap\"") && trimmed.EndsWith("[")) {
                inNameMap = true;
                nameMapIndex = 0;
                continue;
            }

            if (inNameMap) {
                // Exit on closing bracket — either `]` alone or `],`.
                if (trimmed.StartsWith("]")) {
                    inNameMap = false;
                    continue;
                }
                // Tolerate trailing comma after the entry.
                var entry = trimmed.TrimEnd(',').Trim();
                if (entry == "\"\"") {
                    totalNameMapEmpties++;
                    if (nameMapEmpties.Count < MAX_NAMEMAP_REPORTED) {
                        nameMapEmpties.Add(new {
                            index = nameMapIndex,
                            lineNumber = i + 1,
                        });
                    }
                }
                nameMapIndex++;
                continue;
            }

            // Field-level empty scan — only outside NameMap.
            var match = EmptyFieldRx.Match(line);
            if (match.Success) {
                totalFieldEmpties++;
                if (fieldEmpties.Count < MAX_FIELD_REPORTED) {
                    var snippet = trimmed;
                    if (snippet.Length > SNIPPET_MAX_LEN)
                        snippet = snippet.Substring(0, SNIPPET_MAX_LEN) + "...";
                    fieldEmpties.Add(new {
                        fieldName = match.Groups[1].Value,
                        lineNumber = i + 1,
                        snippet,
                    });
                }
            }
        }

        return new {
            totalNameMapEmpties,
            totalFieldEmpties,
            nameMapEmpties = nameMapEmpties.ToArray(),
            fieldEmpties = fieldEmpties.ToArray(),
            totalLines,
        };
    }
}
