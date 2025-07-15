using System.Configuration;
using System.Data;
using System.Windows;
using System.Globalization;
using QuquPlot.Utils;

namespace QuquPlot;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        this.ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        // 调试输出

        string lang = culture.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
        // lang = "zh-CN";
        System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(lang);
        System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(lang);

        string dictPath = lang == "zh-CN"
            ? "assets/Strings.zh-CN.xaml"
            : "assets/Strings.en-US.xaml";
        // 调试输出
        // System.IO.File.AppendAllText("startup_debug.log", $"CurrentUICulture: {culture}{Environment.NewLine}");
        // System.IO.File.AppendAllText("startup_debug.log", $"Loading resource dictionary: {dictPath}{Environment.NewLine}");

        var dictionaries = Resources.MergedDictionaries;
        dictionaries.Clear();
        var dict = new System.Windows.ResourceDictionary { Source = new System.Uri(dictPath, System.UriKind.Relative) };
        dictionaries.Add(dict);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}

