using System;
using System.IO;
using System.Text.Json;
using QuquPlot.Models;
using System.Linq;
using System.Windows.Media;
using QuquPlot;

namespace QuquPlot.Utils
{
    public static class CurveConfigIO
    {
        public static void SaveConfig(string filePath, ProjectConfigSerializable config)
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        public static ProjectConfigSerializable? LoadConfig(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ProjectConfigSerializable>(json);
        }

        // 收集 MainWindow 的 UI 状态和曲线，组装为 ProjectConfigSerializable
        public static ProjectConfigSerializable CollectConfigFromUI(dynamic window)
        {
            var mainWindow = (MainWindow)window;
            int w, h;
            var config = new ProjectConfigSerializable
            {
                AppSettings = new AppSettingsSerializable
                {
                    XAxisLabel = window.XAxisLabelTextBox.Text,
                    YAxisLabel = window.YAxisLabelTextBox.Text,
                    ImageWidth = int.TryParse(window.ImageWidthTextBox.Text, out w) ? w : 1200,
                    ImageHeight = int.TryParse(window.ImageHeightTextBox.Text, out h) ? h : 800,
                    LegendPosition = window.LegendPositionComboBox.SelectedIndex,
                    XAxisMin = window.XAxisMinTextBox.Text,
                    XAxisMax = window.XAxisMaxTextBox.Text,
                    AutoScaleX = window.AutoScaleXCheckBox.IsChecked == true,
                    YAxisMin = window.YAxisMinTextBox.Text,
                    YAxisMax = window.YAxisMaxTextBox.Text,
                    AutoScaleY = window.AutoScaleYCheckBox.IsChecked == true,
                    Y2AxisLabel = window.Y2AxisLabelTextBox.Text,
                    Y2AxisMin = window.Y2AxisMinTextBox.Text,
                    Y2AxisMax = window.Y2AxisMaxTextBox.Text,
                    AutoScaleY2 = window.AutoScaleY2CheckBox.IsChecked == true
                },
                Curves = mainWindow.CurveInfos.Select(ci => new CurveConfigSerializable(ci)).ToList()
            };
            return config;
        }

        // 将 ProjectConfigSerializable 内容还原到 MainWindow 的 UI 和 _curveInfos
        public static void RestoreConfigToUI(dynamic window, ProjectConfigSerializable config)
        {
            var mainWindow = (MainWindow)window;
            var appSettings = config.AppSettings;
            if (appSettings != null)
            {
                mainWindow.XAxisLabelTextBox.Text = appSettings.XAxisLabel;
                mainWindow.YAxisLabelTextBox.Text = appSettings.YAxisLabel;
                mainWindow.ImageWidthTextBox.Text = appSettings.ImageWidth.ToString();
                mainWindow.ImageHeightTextBox.Text = appSettings.ImageHeight.ToString();
                if (appSettings.LegendPosition >= 0 && appSettings.LegendPosition < mainWindow.LegendPositionComboBox.Items.Count)
                    mainWindow.LegendPositionComboBox.SelectedIndex = appSettings.LegendPosition;
                mainWindow.XAxisMinTextBox.Text = (appSettings.XAxisMin ?? string.Empty);
                mainWindow.XAxisMaxTextBox.Text = (appSettings.XAxisMax ?? string.Empty);
                mainWindow.OnXAxisRangeChanged(null, new System.Windows.RoutedEventArgs());
                mainWindow.AutoScaleXCheckBox.IsChecked = appSettings.AutoScaleX;
                mainWindow.UpdateXAxisInputState();
                mainWindow.YAxisMinTextBox.Text = (appSettings.YAxisMin ?? string.Empty);
                mainWindow.YAxisMaxTextBox.Text = (appSettings.YAxisMax ?? string.Empty);
                mainWindow.AutoScaleYCheckBox.IsChecked = appSettings.AutoScaleY;
                mainWindow.UpdateYAxisInputState();
                mainWindow.Y2AxisLabelTextBox.Text = appSettings.Y2AxisLabel;
                mainWindow.Y2AxisMinTextBox.Text = (appSettings.Y2AxisMin ?? string.Empty);
                mainWindow.Y2AxisMaxTextBox.Text = (appSettings.Y2AxisMax ?? string.Empty);
                mainWindow.AutoScaleY2CheckBox.IsChecked = appSettings.AutoScaleY2;
                mainWindow.UpdateY2AxisInputState();
            }
            // 恢复曲线
            var curveConfigs = config.Curves;
            if (curveConfigs == null) return;
            int restored = 0, failed = 0;
            mainWindow._isBatchLoading = true;
            try
            {
                foreach (var cfg in curveConfigs)
                {
                    string dataPath = string.IsNullOrWhiteSpace(cfg.SourceFileFullPath) ? cfg.SourceFileName : cfg.SourceFileFullPath;
                    if (string.IsNullOrWhiteSpace(dataPath) || !System.IO.File.Exists(dataPath))
                    {
                        mainWindow.AppendDebugInfo($"找不到数据文件: {dataPath}");
                        failed++;
                        continue;
                    }
                    try
                    {
                        // 复用已有的文件处理逻辑
                        // 只加载数据，不添加到_plot，只生成xs,ys
                        string ext = FileUtils.GetFileExtension(dataPath);
                        System.Collections.Generic.List<string> processedLines;
                        switch (ext)
                        {
                            case ".csv":
                                processedLines = FileUtils.ProcessCsvFile(dataPath, msg => mainWindow.AppendDebugInfo(msg));
                                break;
                            case ".xls":
                            case ".xlsx":
                                processedLines = FileUtils.ProcessExcelFile(dataPath, msg => mainWindow.AppendDebugInfo(msg));
                                break;
                            case ".txt":
                                processedLines = FileUtils.ProcessTxtFile(dataPath, msg => mainWindow.AppendDebugInfo(msg));
                                break;
                            case ".s":
                            case ".s2p":
                            case ".s3p":
                            case ".s4p":
                                mainWindow.AppendDebugInfo($"暂不支持S参数文件批量恢复: {dataPath}");
                                failed++;
                                continue;
                            default:
                                mainWindow.AppendDebugInfo($"不支持的文件类型: {ext}");
                                failed++;
                                continue;
                        }
                        if (processedLines.Count > 0)
                        {
                            FileUtils.ProcessDataLines(
                                processedLines,
                                System.IO.Path.GetFileName(dataPath),
                                msg => mainWindow.AppendDebugInfo(msg),
                                (label, xs, ys, src, isFirst) =>
                                {
                                    var temp = new CurveInfo();
                                    temp.Xs = xs;
                                    temp.Ys = ys;
                                    temp.GenerateHashId();
                                    if (temp.HashId == cfg.HashId)
                                    {
                                        // 恢复设置
                                        var ci = new CurveInfo(msg => mainWindow.AppendDebugInfo(msg), null, "长度：");
                                        ci.Name = cfg.Name;
                                        ci.Xs = xs;
                                        ci.Ys = ys;
                                        ci.Visible = cfg.Visible;
                                        ci.Width = cfg.Width;
                                        ci.Opacity = cfg.Opacity;
                                        ci.LineStyle = cfg.LineStyle;
                                        ci.SourceFileName = cfg.SourceFileName;
                                        ci.SourceFileFullPath = cfg.SourceFileFullPath;
                                        if (cfg.PlotColor != null && cfg.PlotColor.Length == 4)
                                            ci.PlotColor = Color.FromArgb((byte)cfg.PlotColor[0], (byte)cfg.PlotColor[1], (byte)cfg.PlotColor[2], (byte)cfg.PlotColor[3]);
                                        ci.MarkerSize = cfg.MarkerSize;
                                        ci.XMagnitude = cfg.XMagnitude;
                                        ci.ReverseX = cfg.ReverseX;
                                        ci.Smooth = cfg.Smooth;
                                        ci.GenerateHashId();
                                        // 只添加到 _curveInfos，不触发重绘
                                        mainWindow.CurveInfos.Add(ci);
                                        restored++;
                                    }
                                    return null;
                                },
                                (xLabel, yLabel) => { }
                            );
                        }
                        else
                        {
                            mainWindow.AppendDebugInfo($"数据文件无有效数据: {dataPath}");
                            failed++;
                        }
                    }
                    catch (Exception ex2)
                    {
                        mainWindow.AppendDebugInfo($"恢复曲线失败: {cfg.Name}, 错误: {ex2.Message}");
                        failed++;
                    }
                }
            }
            finally
            {
                mainWindow._isBatchLoading = false;
            }
            mainWindow.RedrawAllCurves();
            mainWindow.AppendDebugInfo($"曲线配置恢复完成，成功: {restored}，失败: {failed}");
        }
    }
} 