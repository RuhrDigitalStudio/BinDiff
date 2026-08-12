using System.Globalization;
using System.Net;
using System.Text;
using BinDiff.Core.Model;

namespace BinDiff.Core.Reporting;

/// <summary>
/// Renders a <see cref="ComparisonResult"/> to a single self-contained HTML file
/// (inline CSS + inline SVG, no external assets) for defensive documentation.
/// </summary>
public static class HtmlReportWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Write(ComparisonResult result, string path) =>
        File.WriteAllText(path, ToHtml(result));

    public static string ToHtml(ComparisonResult r)
    {
        var sb = new StringBuilder();
        sb.Append("""
<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>BinDiff report</title>
<style>
:root{--bg:#0f1420;--card:#1a2130;--fg:#e6ebf2;--muted:#8b97ab;--accent:#4f9dff;--warn:#ffb454;--a:#4f9dff;--b:#ff7a7a;--shared:#3ecf8e;--uniq:#6b7688;}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 system-ui,Segoe UI,Roboto,sans-serif;padding:24px}
h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:0 0 12px;border-bottom:1px solid #2c3547;padding-bottom:6px}
.card{background:var(--card);border:1px solid #2c3547;border-radius:10px;padding:18px;margin:16px 0}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:16px}
table{border-collapse:collapse;width:100%;font-size:13px}td,th{padding:5px 8px;border-bottom:1px solid #2c3547;text-align:left;vertical-align:top}
th{color:var(--muted);font-weight:600}
.mono{font-family:Consolas,Menlo,monospace;font-size:12px;word-break:break-all}
.big{font-size:44px;font-weight:700;color:var(--accent)}
.bar{height:14px;border-radius:7px;background:#2c3547;overflow:hidden}.bar>span{display:block;height:100%;background:var(--accent)}
.muted{color:var(--muted)}.warn{color:var(--warn)}
.pill{display:inline-block;padding:1px 8px;border-radius:10px;font-size:11px;background:#2c3547;color:var(--muted)}
.overflow{overflow-x:auto}
</style></head><body>
""");
        sb.Append("<h1>BinDiff — similarity analysis</h1>");
        sb.Append($"<div class=\"muted\">Created: {E(r.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv))}</div>");

        sb.Append("<div class=\"card\">");
        sb.Append($"<div class=\"big\">{r.OverallSimilarityPercent.ToString("0.00", Inv)} %</div>");
        sb.Append("<div class=\"muted\">Weighted overall similarity</div>");
        sb.Append(Bar(r.OverallSimilarityPercent));
        sb.Append("<div class=\"grid\" style=\"margin-top:16px\">");
        sb.Append(FileCard("File A", r.FileA));
        sb.Append(FileCard("File B", r.FileB));
        sb.Append("</div>");
        if (r.Warnings.Count > 0)
        {
            sb.Append("<div class=\"warn\" style=\"margin-top:10px\">");
            foreach (var w in r.Warnings) sb.Append($"⚠ {E(w)}<br>");
            sb.Append("</div>");
        }
        sb.Append("</div>");

        foreach (var s in r.Sections)
            sb.Append(RenderSection(s));

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string FileCard(string label, BinaryImageInfo f) =>
        $"<div><div class=\"muted\">{E(label)}</div><div><b>{E(f.Name)}</b></div>" +
        $"<div class=\"muted\">{f.Size.ToString("N0", Inv)} Bytes</div>" +
        $"<div class=\"mono muted\">SHA256 {E(f.Sha256)}</div></div>";

    private static string RenderSection(IAnalysisSection s)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"card\">");
        sb.Append($"<h2>{E(s.Title)} ");
        sb.Append(s.SimilarityPercent is null
            ? "<span class=\"pill\">not scored</span>"
            : $"<span class=\"pill\">{s.SimilarityPercent.Value.ToString("0.00", Inv)} %</span>");
        sb.Append("</h2>");

        if (s.Error != null)
        {
            sb.Append($"<div class=\"warn\">Error: {E(s.Error)}</div></div>");
            return sb.ToString();
        }

        sb.Append("<div class=\"overflow\"><table>");
        foreach (var m in s.Metrics)
            sb.Append($"<tr><th>{E(m.Key)}</th><td class=\"mono\">{E(m.Value)}</td></tr>");
        sb.Append("</table></div>");

        switch (s)
        {
            case EntropySection es:
                sb.Append("<div style=\"margin-top:12px\">");
                sb.Append(EntropySvg(es));
                sb.Append("</div>");
                break;
            case ByteDiffSection bd:
                sb.Append("<div style=\"margin-top:12px\">");
                sb.Append("<div class=\"muted\">File A</div>").Append(DiffBar(bd.MapA));
                sb.Append("<div class=\"muted\" style=\"margin-top:6px\">File B</div>").Append(DiffBar(bd.MapB));
                sb.Append("</div>");
                break;
            case FormatSection fs when fs.Applicable:
                sb.Append(FormatTables(fs));
                break;
            case PatternSection ps:
                sb.Append(PatternTables(ps));
                break;
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Bar(double pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        return $"<div class=\"bar\" style=\"margin-top:8px\"><span style=\"width:{pct.ToString("0.##", Inv)}%\"></span></div>";
    }

    private static string EntropySvg(EntropySection es)
    {
        string PathFor(double[] p, string color)
        {
            if (p.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < p.Length; i++)
            {
                double x = p.Length == 1 ? 0 : (double)i / (p.Length - 1) * 600;
                double y = 120 - Math.Clamp(p[i], 0, 8) / 8.0 * 120;
                sb.Append(i == 0 ? "M" : "L").Append(x.ToString("0.#", Inv)).Append(' ').Append(y.ToString("0.#", Inv)).Append(' ');
            }
            return $"<path d=\"{sb}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\"/>";
        }
        return "<svg viewBox=\"0 0 600 130\" width=\"100%\" preserveAspectRatio=\"none\" style=\"background:#141a26;border-radius:6px\">"
             + "<line x1=\"0\" y1=\"120\" x2=\"600\" y2=\"120\" stroke=\"#2c3547\"/>"
             + PathFor(es.ProfileA, "var(--a)") + PathFor(es.ProfileB, "var(--b)")
             + "</svg><div class=\"muted\" style=\"font-size:12px\">blue = A, red = B (0–8 bits/byte)</div>";
    }

    private static string DiffBar(List<ByteSpan> spans)
    {
        long total = 0;
        foreach (var sp in spans) total += sp.Length;
        if (total == 0) return "<div class=\"bar\"></div>";
        var sb = new StringBuilder("<div style=\"display:flex;height:14px;border-radius:7px;overflow:hidden\">");
        foreach (var sp in spans)
        {
            double w = (double)sp.Length / total * 100;
            string c = sp.Shared ? "var(--shared)" : "var(--uniq)";
            sb.Append($"<span style=\"width:{w.ToString("0.####", Inv)}%;background:{c}\"></span>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string FormatTables(FormatSection fs)
    {
        var sb = new StringBuilder("<div class=\"overflow\" style=\"margin-top:12px\"><table>");
        sb.Append("<tr><th>Section</th><th>in A</th><th>in B</th><th>Similarity</th></tr>");
        foreach (var c in fs.SectionComparisons)
            sb.Append($"<tr><td class=\"mono\">{E(c.Name)}</td><td>{(c.InA ? "✓" : "–")}</td><td>{(c.InB ? "✓" : "–")}</td>"
                    + $"<td>{(c.SimilarityPercent is null ? "n/a" : c.SimilarityPercent.Value.ToString("0.0", Inv) + " %")}</td></tr>");
        sb.Append("</table></div>");
        if (fs.ImportsOnlyA.Count + fs.ImportsOnlyB.Count > 0)
        {
            sb.Append("<div class=\"grid\" style=\"margin-top:10px\">");
            sb.Append("<div><div class=\"muted\">Imports only in A</div><div class=\"mono\">").Append(E(string.Join(", ", fs.ImportsOnlyA))).Append("</div></div>");
            sb.Append("<div><div class=\"muted\">Imports only in B</div><div class=\"mono\">").Append(E(string.Join(", ", fs.ImportsOnlyB))).Append("</div></div>");
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    private static string PatternTables(PatternSection ps)
    {
        string Table(string title, List<PatternHit> hits)
        {
            var sb = new StringBuilder($"<div><div class=\"muted\" style=\"margin-top:10px\">{E(title)} ({hits.Count})</div><div class=\"overflow\"><table>");
            sb.Append("<tr><th>Hex</th><th>A</th><th>B</th></tr>");
            foreach (var h in hits.Take(25))
                sb.Append($"<tr><td class=\"mono\">{E(h.Hex)}</td><td>{h.CountA}</td><td>{h.CountB}</td></tr>");
            sb.Append("</table></div></div>");
            return sb.ToString();
        }
        return Table("Shared patterns", ps.CommonPatterns)
             + Table("Only in A (candidate indicators)", ps.UniqueToA)
             + Table("Only in B (candidate indicators)", ps.UniqueToB);
    }

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
}
