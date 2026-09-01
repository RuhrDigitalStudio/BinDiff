using System.Text;
using BinDiff.Core.Model;

namespace BinDiff.Core.Analyzers;

/// <summary>Compares bounded printable ASCII and UTF-16LE runs without interpreting them.</summary>
public sealed class StringAnalyzer : IAnalyzer
{
    public AnalyzerModule Module => AnalyzerModule.Strings;

    public IAnalysisSection Analyze(BinaryImage a, BinaryImage b, ComparisonOptions options)
    {
        var section = new StringSection();
        try
        {
            var minimum = Math.Clamp(options.MinStringLength, 1, 1_024);
            var maximum = Math.Clamp(options.MaxStringLength, minimum, 16_384);
            var extractedLimit = Math.Clamp(options.MaxExtractedStrings, 1, 1_000_000);
            var reportLimit = Math.Clamp(options.MaxReportedStrings, 0, 10_000);
            var stringsA = Extract(a.Data, minimum, maximum, extractedLimit);
            var stringsB = Extract(b.Data, minimum, maximum, extractedLimit);
            section.MinimumLength = minimum;
            section.DistinctStringsA = stringsA.Count;
            section.DistinctStringsB = stringsB.Count;

            var keysA = stringsA.Keys.ToHashSet();
            var keysB = stringsB.Keys.ToHashSet();
            var common = keysA.Intersect(keysB).ToArray();
            var union = keysA.Union(keysB).Count();
            section.SimilarityPercent = union == 0 ? 100 : 100.0 * common.Length / union;
            section.CommonStrings = Select(common, stringsA, stringsB, reportLimit);
            section.UniqueToA = Select(keysA.Except(keysB), stringsA, null, reportLimit);
            section.UniqueToB = Select(keysB.Except(keysA), null, stringsB, reportLimit);
            return section;
        }
        catch (Exception ex)
        {
            return new StringSection { Error = ex.Message };
        }
    }

    private static Dictionary<StringKey, Occurrence> Extract(
        byte[] data, int minimum, int maximum, int distinctLimit)
    {
        var values = new Dictionary<StringKey, Occurrence>();
        ExtractAscii(data, minimum, maximum, distinctLimit, values);
        ExtractUtf16(data, minimum, maximum, distinctLimit, values);
        return values;
    }

    private static void ExtractAscii(byte[] data, int minimum, int maximum, int limit,
        Dictionary<StringKey, Occurrence> values)
    {
        var start = 0;
        while (start < data.Length)
        {
            while (start < data.Length && !Printable(data[start])) start++;
            var end = start;
            while (end < data.Length && Printable(data[end])) end++;
            if (end - start >= minimum)
            {
                var length = Math.Min(end - start, maximum);
                Add(values, new StringKey("ASCII", Encoding.ASCII.GetString(data, start, length)), start, limit);
            }
            start = Math.Max(end, start + 1);
        }
    }

    private static void ExtractUtf16(byte[] data, int minimum, int maximum, int limit,
        Dictionary<StringKey, Occurrence> values)
    {
        for (var alignment = 0; alignment < 2; alignment++)
        {
            var start = alignment;
            while (start + 1 < data.Length)
            {
                while (start + 1 < data.Length && !Utf16RunStarts(data, start, alignment)) start += 2;
                var end = start;
                while (end + 1 < data.Length && PrintableUtf16(data, end)) end += 2;
                var characters = (end - start) / 2;
                if (characters >= minimum)
                {
                    var length = Math.Min(characters, maximum) * 2;
                    Add(values, new StringKey("UTF-16LE", Encoding.Unicode.GetString(data, start, length)), start, limit);
                }
                start = Math.Max(end, start + 2);
            }
        }
    }

    private static void Add(Dictionary<StringKey, Occurrence> values, StringKey key, long offset, int limit)
    {
        if (values.TryGetValue(key, out var occurrence))
        {
            occurrence.Count++;
            return;
        }
        if (values.Count < limit) values[key] = new Occurrence(offset);
    }

    private static List<StringHit> Select(
        IEnumerable<StringKey> keys,
        IReadOnlyDictionary<StringKey, Occurrence>? a,
        IReadOnlyDictionary<StringKey, Occurrence>? b,
        int limit) => keys
        .Select(key => new StringHit
        {
            Value = key.Value,
            Encoding = key.Encoding,
            FirstOffsetA = a is not null && a.TryGetValue(key, out var hitA) ? hitA.Offset : -1,
            FirstOffsetB = b is not null && b.TryGetValue(key, out var hitB) ? hitB.Offset : -1,
            CountA = a is not null && a.TryGetValue(key, out hitA) ? hitA.Count : 0,
            CountB = b is not null && b.TryGetValue(key, out hitB) ? hitB.Count : 0
        })
        .OrderByDescending(item => item.CountA + item.CountB)
        .ThenByDescending(item => item.Value.Length)
        .ThenBy(item => item.Encoding, StringComparer.Ordinal)
        .ThenBy(item => item.Value, StringComparer.Ordinal)
        .Take(limit).ToList();

    private static bool Printable(byte value) => value is >= 0x20 and <= 0x7e;

    private static bool PrintableUtf16(byte[] data, int offset) => data[offset + 1] == 0 && Printable(data[offset]);

    // A printable ASCII byte immediately before a candidate usually means we
    // landed on the tail of an ASCII run ("x\0w\0...") rather than its UTF-16 boundary.
    private static bool Utf16RunStarts(byte[] data, int offset, int alignment) =>
        PrintableUtf16(data, offset) && (offset == alignment || offset == 0 || !Printable(data[offset - 1]));

    private readonly record struct StringKey(string Encoding, string Value);

    private sealed class Occurrence(long offset)
    {
        public long Offset { get; } = offset;
        public int Count { get; set; } = 1;
    }
}
