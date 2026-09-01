using System.Xml.Linq;

namespace BinDiff.Tests;

public sealed class GuiThemeResourceTests
{
    [Fact]
    public void MainWindow_ThemesNativeTabAndProgressControls_AndOverviewLabels()
    {
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "Gui", "MainWindow.xaml");
        var xaml = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var styles = xaml.Descendants(presentation + "Style").ToList();
        Assert.Contains(styles, style => (string?)style.Attribute("TargetType") == "TabItem"
            && style.Descendants(presentation + "ControlTemplate").Any());
        Assert.Contains(styles, style => (string?)style.Attribute("TargetType") == "ProgressBar"
            && style.Descendants(presentation + "ControlTemplate").Any());
        Assert.Contains(xaml.Descendants(presentation + "TextBlock"), block =>
            (string?)block.Attribute("Text") == "{Binding Title}"
            && (string?)block.Attribute("Foreground") == "{StaticResource Fg}");
    }

    [Fact]
    public void MainWindow_ExposesStringAndDotNetComparisonViews()
    {
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "Gui", "MainWindow.xaml");
        var xaml = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var headers = xaml.Descendants(presentation + "TabItem")
            .Select(item => (string?)item.Attribute("Header")).ToHashSet();
        var names = xaml.Descendants()
            .Select(item => (string?)item.Attribute(x + "Name")).Where(name => name is not null).ToHashSet();

        Assert.Contains("Strings", headers);
        Assert.Contains(".NET metadata", headers);
        Assert.Contains("ModStrings", names);
        Assert.Contains("ModDotNet", names);
        Assert.Contains("StringCommonGrid", names);
        Assert.Contains("DotNetChangesGrid", names);
    }
}
