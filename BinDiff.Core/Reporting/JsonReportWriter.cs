using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BinDiff.Core.Model;

namespace BinDiff.Core.Reporting;

/// <summary>Serialises a <see cref="ComparisonResult"/> to machine-readable JSON for pipelines.</summary>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static string ToJson(ComparisonResult result) => JsonSerializer.Serialize(result, Options);

    public static void Write(ComparisonResult result, string path) =>
        File.WriteAllText(path, ToJson(result));
}
