using System.Globalization;
using System.Windows;

namespace QuquPlot;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = CultureInfo.CurrentUICulture.Name;
        // 调试输出

        string lang = culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
        // lang = "zh-CN";
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(lang);
        Thread.CurrentThread.CurrentCulture = new CultureInfo(lang);

        string dictPath = lang == "zh-CN"
            ? "assets/Strings.zh-CN.xaml"
            : "assets/Strings.en-US.xaml";
        // 调试输出
        // System.IO.File.AppendAllText("startup_debug.log", $"CurrentUICulture: {culture}{Environment.NewLine}");
        // System.IO.File.AppendAllText("startup_debug.log", $"Loading resource dictionary: {dictPath}{Environment.NewLine}");

        var dictionaries = Resources.MergedDictionaries;
        dictionaries.Clear();
        var dict = new ResourceDictionary { Source = new Uri(dictPath, UriKind.Relative) };
        dictionaries.Add(dict);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}

