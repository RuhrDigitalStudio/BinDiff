using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using BinDiff.Core;
using BinDiff.Core.Model;
using BinDiff.Core.Reporting;
using Path = System.IO.Path;

namespace BinDiff.Gui;

/// <summary>
/// Interaction logic for MainWindow.xaml. Thin view layer: it collects two files and
/// options, calls <see cref="AnalyzerEngine"/> off the UI thread, and renders the result.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Brush SharedBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0xCF, 0x8E));
    private static readonly Brush UniqueBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x76, 0x88));
    private static readonly Brush CurveA = new SolidColorBrush(Color.FromRgb(0x4F, 0x9D, 0xFF));
    private static readonly Brush CurveB = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x7A));

    private string? _pathA;
    private string? _pathB;
    private ComparisonResult? _result;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Drop_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (sender is Border b) b.BorderBrush = CurveA;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Drop_DragLeave(object sender, DragEventArgs e) => ResetBorder(sender as Border);

    private void ResetBorder(Border? b)
    {
        if (b != null) b.BorderBrush = (Brush)FindResource("Line");
    }

    private void Drop_A(object sender, DragEventArgs e)
    {
        ResetBorder(sender as Border);
        if (FirstFile(e) is { } f) SetA(f);
    }

    private void Drop_B(object sender, DragEventArgs e)
    {
        ResetBorder(sender as Border);
        if (FirstFile(e) is { } f) SetB(f);
    }

    private static string? FirstFile(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files ? files[0] : null;

    private void Pick_A(object sender, MouseButtonEventArgs e)
    {
        if (PickFile() is { } f) SetA(f);
    }

    private void Pick_B(object sender, MouseButtonEventArgs e)
    {
        if (PickFile() is { } f) SetB(f);
    }

    private static string? PickFile()
    {
        var dlg = new OpenFileDialog { Title = "Select binary file", CheckFileExists = true };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void SetA(string path)
    {
        _pathA = path;
        PathA.Text = Path.GetFileName(path);
        InfoA.Text = DescribeFile(path);
    }

    private void SetB(string path)
    {
        _pathB = path;
        PathB.Text = Path.GetFileName(path);
        InfoB.Text = DescribeFile(path);
    }

    private static string DescribeFile(string path)
    {
        try
        {
            return new FileInfo(path).Length.ToString("N0", Inv) + " Bytes";
        }
        catch
        {
            return path;
        }
    }

    private ComparisonOptions BuildOptions()
    {
        var o = new ComparisonOptions
        {
            PatternLength = ParseInt(OptPatternLen.Text, 16, 1),
            ChunkAvgSize = ParseInt(OptBlockSize.Text, 2048, 8),
            ShingleK = ParseInt(OptShingleK.Text, 8, 1),
            EntropyBlockSize = ParseInt(OptEntropyBlock.Text, 256, 1),
            MinPatternOccurrences = ParseInt(OptMinOcc.Text, 2, 1),
            MinStringLength = ParseInt(OptStringMin.Text, 5, 1)
        };

        var modules = new HashSet<AnalyzerModule>();
        if (ModByteDiff.IsChecked == true) modules.Add(AnalyzerModule.ByteDiff);
        if (ModFuzzy.IsChecked == true) modules.Add(AnalyzerModule.FuzzyHash);
        if (ModFormat.IsChecked == true) modules.Add(AnalyzerModule.Format);
        if (ModEntropy.IsChecked == true) modules.Add(AnalyzerModule.Entropy);
        if (ModPatterns.IsChecked == true) modules.Add(AnalyzerModule.Patterns);
        if (ModStrings.IsChecked == true) modules.Add(AnalyzerModule.Strings);
        if (ModDotNet.IsChecked == true) modules.Add(AnalyzerModule.DotNet);
        if (modules.Count > 0) o.EnabledModules = modules;
        return o;
    }

    private static int ParseInt(string text, int fallback, int min)
    {
        if (int.TryParse(text, NumberStyles.Integer, Inv, out var v) && v >= min) return v;
        return fallback;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_pathA) || string.IsNullOrEmpty(_pathB))
        {
            StatusText.Text = "Select two files.";
            return;
        }
        if (!File.Exists(_pathA) || !File.Exists(_pathB))
        {
            StatusText.Text = "One of the files no longer exists.";
            return;
        }

        var options = BuildOptions();
        SetBusy(true);
        try
        {
            string a = _pathA, b = _pathB;
            var result = await Task.Run(() => new AnalyzerEngine().Compare(a, b, options));
            _result = result;
            Populate(result);
            BtnJson.IsEnabled = true;
            BtnHtml.IsEnabled = true;
            StatusText.Text = $"Complete — {result.OverallSimilarityPercent.ToString("0.0", Inv)} % overall";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Analysis error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Analysis failed.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        BtnAnalyze.IsEnabled = !busy;
        if (busy) StatusText.Text = "Analyzing…";
    }

    private void Populate(ComparisonResult r)
    {
        OverallText.Text = r.OverallSimilarityPercent.ToString("0.00", Inv) + " %";
        OverallBar.Value = Math.Clamp(r.OverallSimilarityPercent, 0, 100);
        WarnList.ItemsSource = r.Warnings;
        SectionSummary.ItemsSource = r.Sections.Select(s => new SectionSummaryVm(
            s.Title,
            s.SimilarityPercent is null ? "n/a" : s.SimilarityPercent.Value.ToString("0.00", Inv) + " %",
            Math.Clamp(s.SimilarityPercent ?? 0, 0, 100))).ToList();

        var bd = r.Section(AnalyzerModule.ByteDiff) as ByteDiffSection;
        ByteMetrics.ItemsSource = bd?.Metrics;

        var fmt = r.Section(AnalyzerModule.Format) as FormatSection;
        if (fmt is { Error: null })
        {
            FormatHeader.Text = fmt.Applicable
                ? $"Format A: {fmt.A?.Format ?? "-"} | Format B: {fmt.B?.Format ?? "-"}"
                : "No PE/ELF format detected; this result is informational only.";
            SectionGrid.ItemsSource = fmt.SectionComparisons;
            ImportsText.Text = fmt.Applicable
                ? $"Shared imports: {fmt.ImportsCommon.Count} | only in A: {string.Join(", ", fmt.ImportsOnlyA)} | only in B: {string.Join(", ", fmt.ImportsOnlyB)}"
                : "";
        }
        else
        {
            FormatHeader.Text = fmt?.Error is { } err ? "Error: " + err : "Format module was not run.";
            SectionGrid.ItemsSource = null;
            ImportsText.Text = "";
        }

        var pat = r.Section(AnalyzerModule.Patterns) as PatternSection;
        CommonGrid.ItemsSource = pat?.CommonPatterns;
        UniqueAGrid.ItemsSource = pat?.UniqueToA;
        UniqueBGrid.ItemsSource = pat?.UniqueToB;

        var ent = r.Section(AnalyzerModule.Entropy) as EntropySection;
        EntropyMetrics.ItemsSource = ent?.Metrics;

        var strings = r.Section(AnalyzerModule.Strings) as StringSection;
        StringMetrics.ItemsSource = strings?.Metrics;
        StringCommonGrid.ItemsSource = strings?.CommonStrings;
        StringUniqueAGrid.ItemsSource = strings?.UniqueToA;
        StringUniqueBGrid.ItemsSource = strings?.UniqueToB;

        var dotNet = r.Section(AnalyzerModule.DotNet) as DotNetSection;
        DotNetMetrics.ItemsSource = dotNet?.Metrics;
        DotNetChangesGrid.ItemsSource = dotNet is null ? null : MetadataChanges(dotNet);

        RedrawVisuals();
    }

    private void Map_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawVisuals();

    private void RedrawVisuals()
    {
        if (_result is null) return;
        var bd = _result.Section(AnalyzerModule.ByteDiff) as ByteDiffSection;
        DrawMap(MapACanvas, bd?.MapA);
        DrawMap(MapBCanvas, bd?.MapB);

        var ent = _result.Section(AnalyzerModule.Entropy) as EntropySection;
        DrawEntropy(EntropyCanvas, ent?.ProfileA, ent?.ProfileB);
    }

    private static void DrawMap(Canvas canvas, List<ByteSpan>? spans)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w <= 0 || h <= 0 || spans is null || spans.Count == 0) return;

        long total = 0;
        foreach (var s in spans) total += s.Length;
        if (total <= 0) return;

        double x = 0;
        foreach (var s in spans)
        {
            double segW = (double)s.Length / total * w;
            var rect = new Rectangle
            {
                Width = Math.Max(0.5, segW),
                Height = h,
                Fill = s.Shared ? SharedBrush : UniqueBrush
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, 0);
            canvas.Children.Add(rect);
            x += segW;
        }
    }

    private static void DrawEntropy(Canvas canvas, double[]? a, double[]? b)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        AddCurve(canvas, a, w, h, CurveA);
        AddCurve(canvas, b, w, h, CurveB);
    }

    private static void AddCurve(Canvas canvas, double[]? profile, double w, double h, Brush brush)
    {
        if (profile is null || profile.Length == 0) return;
        var line = new Polyline { Stroke = brush, StrokeThickness = 1.5 };
        for (int i = 0; i < profile.Length; i++)
        {
            double x = profile.Length == 1 ? 0 : (double)i / (profile.Length - 1) * w;
            double y = h - Math.Clamp(profile[i], 0, 8) / 8.0 * h;
            line.Points.Add(new Point(x, y));
        }
        canvas.Children.Add(line);
    }

    private static List<MetadataChangeVm> MetadataChanges(DotNetSection section)
    {
        var changes = new List<MetadataChangeVm>();
        Add("Reference", "Only A", section.ReferencesOnlyA);
        Add("Reference", "Only B", section.ReferencesOnlyB);
        Add("Type", "Only A", section.TypesOnlyA);
        Add("Type", "Only B", section.TypesOnlyB);
        Add("Method", "Only A", section.MethodsOnlyA);
        Add("Method", "Only B", section.MethodsOnlyB);
        Add("P/Invoke", "Only A", section.PInvokesOnlyA);
        Add("P/Invoke", "Only B", section.PInvokesOnlyB);
        return changes;

        void Add(string category, string presence, IEnumerable<string> values)
        {
            foreach (var value in values) changes.Add(new MetadataChangeVm(category, presence, value));
        }
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e) =>
        Export("JSON", "json", "JSON-Report (*.json)|*.json", p => JsonReportWriter.Write(_result!, p));

    private void ExportHtml_Click(object sender, RoutedEventArgs e) =>
        Export("HTML", "html", "HTML-Report (*.html)|*.html", p => HtmlReportWriter.Write(_result!, p));

    private void Export(string label, string ext, string filter, Action<string> write)
    {
        if (_result is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = ext,
            FileName = $"bindiff-report.{ext}"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            write(dlg.FileName);
            StatusText.Text = $"{label} report saved: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed record SectionSummaryVm(string Title, string ScoreText, double Percent);

    private sealed record MetadataChangeVm(string Category, string Presence, string Value);
}
