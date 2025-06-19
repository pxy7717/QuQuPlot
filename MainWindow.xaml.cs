using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Media;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;
using System.Windows.Input;
// using QuquPlot.Utils;
using ScottPlot;
// using CsvHelper;
using System.Globalization;
using System.Threading.Tasks;
using System.Data;
// using ClosedXML.Excel;
using MediaColor = System.Windows.Media.Color;
using QuquPlot.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using ExcelDataReader;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32;
using ScottPlot.WPF;
using Color = System.Windows.Media.Color;
using System.Windows.Documents;

namespace QuquPlot
{
    public partial class MainWindow : Window
    {
        private const int COLOR_BLOCK_SIZE = 25;  // 色块尺寸
        private const int COLOR_PANEL_WIDTH = 150;  // 调色板宽度 (6 * 25)
        private const int COLOR_PANEL_HEIGHT = 150;  // 调色板高度 (6 * 25)
#if DEBUG
        private bool SHOW_DEBUG_PANEL = true;
#else
        private bool SHOW_DEBUG_PANEL = false;
#endif

        private Dictionary<string, (CurveInfo Info, IPlottable Plot)> _curveMap = new();
        private ObservableCollection<CurveInfo> _curveInfos = new ObservableCollection<CurveInfo>();
        public ObservableCollection<CurveInfo> CurveInfos => _curveInfos;
        private bool isCurveListVisible = true;
        private GridLength? lastPlotRowHeight;
        private GridLength? lastCurveListRowHeight;
        private RowDefinition? plotRowDef;
        private RowDefinition? curveListRowDef;

        private readonly string[] _colorPalette = new string[]
        {
            "#D32F2F", "#FF5252", "#FF8A80", "#1976D2", "#448AFF", "#82B1FF",
            "#00C853", "#69F0AE", "#B9F6CA", "#9C27B0", "#CE93D8", "#E1BEE7",
            "#FBC02D", "#FFEB3B", "#FFF59D", "#0097A7", "#4DD0E1", "#B2EBF2",
            "#F57C00", "#FFB74D", "#FFE0B2", "#C2185B", "#F48FB1", "#F8BBD0",
            "#5D4037", "#A1887F", "#D7CCC8", "#303F9F", "#7986CB", "#C5CAE9",
            "#212121", "#616161", "#9E9E9E", "#BDBDBD", "#EEEEEE", "#FAFAFA"
        };

        // 工具方法：将hex字符串转为ScottPlot.Color
        private static ScottPlot.Color ColorFromHex(string hex, double alpha = 1.0)
        {
            hex = hex.Replace("#", "");
            byte a = (byte)(255 * alpha);
            int start = 0;
            if (hex.Length == 8) { a = Convert.ToByte(hex.Substring(0, 2), 16); start = 2; }
            byte r = Convert.ToByte(hex.Substring(start, 2), 16);
            byte g = Convert.ToByte(hex.Substring(start + 2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(start + 4, 2), 16);
            // Convert byte values (0-255) to float values (0-1) for ScottPlot.Color
            return new ScottPlot.Color(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
        }

        private double _lastSplitterPosition = 0.5; // 默认30%的宽度给设置面板

        private Point _dragStartPoint;
        private int _insertionIndex = -1;
        private AdornerLayer? _adornerLayer;
        private InsertionAdorner? _insertionAdorner;

        private DispatcherTimer? _colorBlockClickTimer;
        private bool _isColorBlockDrag = false;
        private Border? _pendingColorBlock = null;
        private MouseButtonEventArgs? _pendingColorBlockEventArgs = null;

        public MainWindow()
        {
            InitializeComponent();
            
            // 添加轴标签文本框的事件处理
            XAxisLabelTextBox.TextChanged += AxisLabel_TextChanged;
            YAxisLabelTextBox.TextChanged += AxisLabel_TextChanged;
            this.DataContext = this;

            // 监听曲线可见性变化
            _curveInfos.CollectionChanged += CurveInfos_CollectionChanged;
            CurveListView.ItemsSource = _curveInfos;
            plotRowDef = (PlotView.Parent as Grid)?.RowDefinitions[0];
            curveListRowDef = (PlotView.Parent as Grid)?.RowDefinitions[2];

            // 订阅曲线信息变化事件
            _curveInfos.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (CurveInfo info in e.NewItems)
                    {
                        info.PropertyChanged += CurveInfo_PropertyChanged;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (CurveInfo info in e.OldItems)
                    {
                        info.PropertyChanged -= CurveInfo_PropertyChanged;
                    }
                }
            };

            // 1. 创建 FontStyler 实例
            var fontStyler = new ScottPlot.Stylers.FontStyler(PlotView!.Plot);

            // 2. 自动检测最佳字体（推荐，能自动适配中英文）
            fontStyler.Automatic();

            // 3. 或者手动指定字体
            // fontStyler.Set("DejaVu Sans");

            this.Loaded += (s, e) =>
            {
                PlotView!.Plot.Clear();

                // 恢复坐标轴字体设置
                PlotView.Plot.Axes.Bottom.Label.FontSize = 32;
                PlotView.Plot.Axes.Left.Label.FontSize = 32;
                PlotView.Plot.Axes.Bottom.TickLabelStyle.FontSize = 28;
                PlotView.Plot.Axes.Left.TickLabelStyle.FontSize = 28;

                // 恢复图例设置
                PlotView.Plot.Legend.FontSize = 28;
                PlotView.Plot.Legend.BackgroundColor = ColorFromHex("#F2F2F2", 0.8);
                PlotView.Plot.Legend.ShadowColor = ColorFromHex("#FFFFFF", 0.0);
                PlotView.Plot.Legend.OutlineColor = ColorFromHex("#FFFFFF", 0.0);

                // 禁用右键缩放
                // var uip = PlotView.UserInputProcessor;
                // uip.RightClickDragZoom(false);

                PlotView.Plot.Legend.Alignment = ScottPlot.Alignment.LowerLeft;
                PlotView.Refresh();

                // 设置初始状态：隐藏曲线设置面板
                var middleGrid = MainGrid.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetColumn(g) == 1);
                if (middleGrid != null)
                {
                    BGrid.Visibility = Visibility.Collapsed;
                    middleGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                    middleGrid.RowDefinitions[2].Height = new GridLength(0);
                    TableButton.Style = (Style)FindResource("ToolbarButtonStyle");
                    TableButton.ToolTip = "显示数据表格";
                }
            };

            if (!SHOW_DEBUG_PANEL)
            {
                // 隐藏右侧调试面板
                var debugPanel = MainGrid.ColumnDefinitions[2];
                debugPanel.Width = new GridLength(0);
                // 也可以隐藏Grid的内容
                var debugGrid = MainGrid.Children
                    .OfType<Grid>()
                    .FirstOrDefault(g => Grid.GetColumn(g) == 2);
                if (debugGrid != null)
                    debugGrid.Visibility = Visibility.Collapsed;
            }

            // 启动时模拟输入，设置坐标轴默认值
            XAxisLabelTextBox.Text = "X";
            YAxisLabelTextBox.Text = "Y";
            // 触发TextChanged事件，模拟用户输入
            XAxisLabelTextBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
            YAxisLabelTextBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));

            this.AllowDrop = true;
            this.DragEnter += MainWindow_DragEnter;
            this.DragOver += MainWindow_DragOver;
            this.Drop += MainWindow_Drop;
        }    

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "数据文件|*.csv;*.xls;*.xlsx;*.txt|CSV文件|*.csv|Excel文件|*.xls;*.xlsx|文本文件|*.txt|所有文件|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    foreach (var filePath in openFileDialog.FileNames)
                    {
                        string fileExtension = System.IO.Path.GetExtension(filePath).ToLower();

                        if (fileExtension == ".csv")
                        {
                            ProcessDataFile(filePath);
                        }
                        else if (fileExtension == ".xls" || fileExtension == ".xlsx")
                        {
                            LoadExcelFile(filePath);
                        }
                        else if (fileExtension == ".txt")
                        {
                            LoadTxtFile(filePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"打开文件时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadTxtFile(string filePath)
        {
            AppendDebugInfo($"开始加载TXT文件: {filePath}");
            
            // 读取文件内容
            var lines = File.ReadAllLines(filePath);
            if (lines.Length == 0)
                {
                AppendDebugInfo("文件为空");
                return;
            }

            // 检测分隔符
            var delimiter = DetectDelimiter(lines);
            if (delimiter == null)
                        {
                // 如果没有检测到分隔符，尝试按空格分割
                delimiter = " ";
                        }

            // 将数据转换为标准格式
            var processedLines = new List<string>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                // 分割行并去除空白
                var parts = line.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => p.Trim())
                               .Where(p => !string.IsNullOrWhiteSpace(p))
                               .ToList();
                
                if (parts.Count > 0)
                {
                    processedLines.Add(string.Join(",", parts));
                }
            }

            // 直接处理数据
            ProcessDataLines(processedLines, Path.GetFileName(filePath));
        }

        private void LoadExcelFile(string filePath)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);
            using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
            var result = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration()
            {
                ConfigureDataTable = (_) => new ExcelDataReader.ExcelDataTableConfiguration
                {
                    UseHeaderRow = true
                }
            });

            if (result.Tables.Count == 0)
            {
                AppendDebugInfo("Excel文件中没有找到数据表");
                return;
            }

            var dataTable = result.Tables[0];
            AppendDebugInfo($"读取到数据表: {dataTable.TableName}");
            AppendDebugInfo($"列数: {dataTable.Columns.Count}, 行数: {dataTable.Rows.Count}");

            // 将Excel数据转换为标准格式
            var processedLines = new List<string>();
            
            // 添加表头
            var headers = new List<string>();
            for (int i = 0; i < dataTable.Columns.Count; i++)
            {
                var columnName = dataTable.Columns[i].ColumnName;
                headers.Add(columnName ?? $"列{i+1}");
            }
            processedLines.Add(string.Join(",", headers));

            // 添加数据行
            foreach (System.Data.DataRow row in dataTable.Rows)
            {
                var values = new List<string>();
                for (int i = 0; i < dataTable.Columns.Count; i++)
                {
                    var value = row[i];
                    if (value == DBNull.Value)
                    {
                        values.Add(string.Empty);
                    }
                    else
                    {
                        var strValue = value.ToString();
                        if (strValue != null)
                        {
                            values.Add(strValue);
                        }
                        else
                        {
                            values.Add(string.Empty);
                        }
                    }
                }
                processedLines.Add(string.Join(",", values));
            }

            // 直接处理数据
            ProcessDataLines(processedLines, Path.GetFileName(filePath));
        }

        private void ProcessDataFile(string filePath)
        {
            AppendDebugInfo($"开始处理数据文件: {filePath}");
            var lines = File.ReadAllLines(filePath);
            ProcessDataLines(lines.ToList(), Path.GetFileName(filePath));
        }

        private void ProcessDataLines(List<string> lines, string sourceFileName)
        {
            if (lines.Count == 0)
            {
                AppendDebugInfo("没有数据需要处理");
                return;
            }

            var delimiter = ","; // 使用逗号作为标准分隔符，因为数据已经被预处理为标准格式
            var (firstDataIndex, lastDataIndex) = FindDataRange(lines.ToArray());

            if (firstDataIndex >= lastDataIndex)
            {
                AppendDebugInfo($"未找到有效数据");
                return;
            }

            var data = new List<double[]>();
            string[]? headers = null;

            // 检查第一行是否为表头
            if (firstDataIndex > 0)
            {
                string[] potentialHeader = lines[firstDataIndex - 1].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (potentialHeader.All(p => !double.TryParse(p, out _)))
                {
                    headers = potentialHeader;
                    if (headers.Length >= 2)
                    {
                        if (XAxisLabelTextBox.Text == "X" && !string.IsNullOrEmpty(headers[0])) 
                            XAxisLabelTextBox.Text = headers[0];
                        if (YAxisLabelTextBox.Text == "Y" && !string.IsNullOrEmpty(headers[1])) 
                            YAxisLabelTextBox.Text = headers[1];
                    }
                }
            }

            // 处理有效数据范围
            for (int i = firstDataIndex; i <= lastDataIndex; i++)
            {
                string[] parts = lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 1) continue;
                var row = new double[parts.Length];
                bool valid = true;
                for (int j = 0; j < parts.Length; j++)
                {
                    if (!double.TryParse(parts[j], out row[j]))
                    {
                        valid = false;
                        break;
                    }
                }
                if (valid)
                    data.Add(row);
            }

            if (data.Count == 0) return;

            int colCount = data[0].Length;
            int rowCount = data.Count;

            // 单列数据处理
            if (colCount == 1)
            {
                double[] ys = new double[rowCount];
                for (int row = 0; row < rowCount; row++)
                    ys[row] = data[row][0];
                double[] xs = Enumerable.Range(0, rowCount).Select(i => (double)i).ToArray();
                AppendDebugInfo($"检测到单列数据，使用索引作为X轴，数量={rowCount}");
                var label = headers != null && headers.Length > 0 && !string.IsNullOrEmpty(headers[0]) ? headers[0] : "Y";
                AddCurveToPlot(label, xs, ys, sourceFileName, true);
                return;
            }

            // 检查是否所有列都是数值
            bool allColumnsAreNumeric = true;
            for (int col = 0; col < colCount; col++)
            {
                bool columnIsNumeric = true;
                for (int row = 0; row < rowCount; row++)
                {
                    if (!double.TryParse(data[row][col].ToString(), out _))
                    {
                        columnIsNumeric = false;
                        break;
                    }
                }
                if (!columnIsNumeric)
                {
                    allColumnsAreNumeric = false;
                    break;
                }
            }

            // 如果所有列都是数值，使用第一列作为X轴
            if (allColumnsAreNumeric)
            {
                // 提取X值（第一列）
                double[] xs = new double[rowCount];
                for (int row = 0; row < rowCount; row++)
                {
                    xs[row] = data[row][0];
                }
                AppendDebugInfo($"X轴范围: {xs[0]} 到 {xs[xs.Length-1]}");

                // 处理Y值（其他列）
                bool shouldLimitVisibility = colCount > 6;
            for (int col = 1; col < colCount; col++)
            {
                    double[] ys = new double[rowCount];
                    for (int row = 0; row < rowCount; row++)
                    {
                        ys[row] = data[row][col];
                    }
                    string label = headers != null && headers.Length > col ? headers[col] : $"列{col+1}";
                    AddCurveToPlot(label, xs, ys, sourceFileName, !shouldLimitVisibility || col == 1);
                }
            }
            else
            {
                // 如果有非数值列，将所有列都作为Y值，使用索引作为X轴
                AppendDebugInfo("检测到非数值列，所有列将作为Y值，使用索引作为X轴");
                double[] xs = Enumerable.Range(0, rowCount).Select(i => (double)i).ToArray();
                
                for (int col = 0; col < colCount; col++)
            {
                    double[] ys = new double[rowCount];
                    bool columnIsValid = true;
                    
                    for (int row = 0; row < rowCount; row++)
                    {
                        if (!double.TryParse(data[row][col].ToString(), out ys[row]))
                        {
                            columnIsValid = false;
                            break;
                        }
                    }
                    
                    if (columnIsValid)
                    {
                        string label = headers != null && headers.Length > col ? headers[col] : $"列{col+1}";
                        AddCurveToPlot(label, xs, ys, sourceFileName, true);
            }
                }
            }

            PlotView.Plot.Axes.AutoScale();
            PlotView.Refresh();
            AppendDebugInfo("数据处理完成");
        }

        private string? DetectDelimiter(string[] lines)
        {
            foreach (var line in lines.Take(10))
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (line.Contains('\t')) return "\t";
                if (line.Contains(',')) return ",";
            }
            return null;
        }

        private (int firstDataIndex, int lastDataIndex) FindDataRange(string[] lines)
        {
            AppendDebugInfo($"开始检测数据范围，文件行数: {lines.Length}");
            var delimiter = DetectDelimiter(lines);
            
            // 查找最后一个有效数据行
            int lastDataIndex = -1;
            int numColumns = 0;
            for (int i = lines.Length - 1; i >= 0; i--)
                {
                if (string.IsNullOrEmpty(lines[i])) continue;
                string[] columns = delimiter == null
                    ? new[] { lines[i].Trim() }
                    : lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length < 1) continue;

                if (double.TryParse(columns[0], out _))
                {
                    lastDataIndex = i;
                    numColumns = columns.Length;
                    AppendDebugInfo($"找到最后数据行，索引: {lastDataIndex}, 列数: {numColumns}");
                    break;
                }
            }

            if (lastDataIndex == -1)
            {
                AppendDebugInfo("未找到有效数据");
                return (-1, -1);
            }

            // 查找第一个有效数据行
            int firstDataIndex = -1;
            for (int i = 0; i <= lastDataIndex; i++)
                {
                if (string.IsNullOrEmpty(lines[i])) continue;
                string[] columns = delimiter == null
                    ? new[] { lines[i].Trim() }
                    : lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length != numColumns) continue;

                if (double.TryParse(columns[0], out _))
                {
                    firstDataIndex = i;
                    AppendDebugInfo($"找到第一个数据行，索引: {firstDataIndex}");
                    break;
                }
            }

            AppendDebugInfo($"数据范围: {firstDataIndex} 到 {lastDataIndex} (共 {lastDataIndex - firstDataIndex + 1} 行)");
            return (firstDataIndex, lastDataIndex);
        }

        private void SaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PNG图片|*.png|所有文件|*.*",
                DefaultExt = ".png"
            };

            if (dialog.ShowDialog() == true)
            {
                PlotView.Plot.SavePng(dialog.FileName, 1500, 1000);
            }
        }

        private void ZoomResetButton_Click(object sender, RoutedEventArgs e)
        {
            PlotView.Plot.Axes.AutoScale();
            PlotView.Refresh();
        }

        private void OnAboutPopupClick(object sender, RoutedEventArgs e)
        {
            AboutPopup.IsOpen = true;
        }

        private void TogglePanel_Click(object sender, RoutedEventArgs e)
        {
            isCurveListVisible = !isCurveListVisible;

            if (plotRowDef == null || curveListRowDef == null)
                return;

            if (!isCurveListVisible)
            {
                // 记录当前高度
                lastPlotRowHeight = plotRowDef.Height;
                lastCurveListRowHeight = curveListRowDef.Height;
                // 隐藏曲线列表，绘图区铺满
                plotRowDef.Height = new GridLength(1, GridUnitType.Star);
                curveListRowDef.Height = new GridLength(0);
            }
            else
            {
                // 恢复上一次的高度
                if (lastPlotRowHeight.HasValue && lastCurveListRowHeight.HasValue)
                {
                    plotRowDef.Height = lastPlotRowHeight.Value;
                    curveListRowDef.Height = lastCurveListRowHeight.Value;
                }
                else
                {
                    plotRowDef.Height = new GridLength(1, GridUnitType.Star);
                    curveListRowDef.Height = GridLength.Auto;
                }
            }
        }

        private void CurvesDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 暂时保持空实现
        }

        private void AxisLabel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (PlotView?.Plot == null) return;

            // 更新坐标轴标签
            PlotView.Plot.XLabel(XAxisLabelTextBox.Text);
            PlotView.Plot.YLabel(YAxisLabelTextBox.Text);
            
            // 刷新显示
            PlotView.Refresh();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            AppendDebugInfo("清除所有曲线");
            PlotView.Plot.Clear();
            _curveMap.Clear();
            _curveInfos.Clear();
            // 不再调用UpdateOtherCurves
            PlotView.Refresh();
            AppendDebugInfo($"当前曲线数量: {_curveInfos.Count}, 曲线映射数量: {_curveMap.Count}");
        }

        private void DeleteCurveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CurveInfo curveInfo)
            {
                AppendDebugInfo($"删除曲线: {curveInfo.Name}, HashId={curveInfo.HashId}");
                
                if (_curveMap.TryGetValue(curveInfo.HashId, out var curveData))
                {
                    PlotView.Plot.Remove(curveData.Plot);
                    _curveMap.Remove(curveInfo.HashId);
                }
                
                _curveInfos.Remove(curveInfo);
                // 增量移除：从所有曲线的OtherCurves中移除被删曲线
                foreach (var ci in _curveInfos)
                {
                    ci.OtherCurves.Remove(curveInfo);
                }
                // 在删除曲线后重新调整坐标轴
                PlotView.Plot.Axes.AutoScale();
                PlotView.Refresh();
                AppendDebugInfo($"当前曲线数量: {_curveInfos.Count}, 曲线映射数量: {_curveMap.Count}");
            }
        }

        private void CurveInfos_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is CurveInfo ci)
                        ci.PropertyChanged += CurveInfo_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is CurveInfo ci)
                        ci.PropertyChanged -= CurveInfo_PropertyChanged;
                }
            }
        }

        private void CurveInfo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            AppendDebugInfo($"[事件] CurveInfo_PropertyChanged: {e.PropertyName}");
            if (sender is not CurveInfo curveInfo || e.PropertyName == null)
                return;

            int idx = _curveInfos.IndexOf(curveInfo);
            if (idx < 0 || idx >= _curveMap.Count)
                return;

            if (_curveMap.Values.ElementAt(idx).Plot is not ScottPlot.Plottables.Scatter scatter)
                return;

            switch (e.PropertyName)
            {
                case nameof(CurveInfo.Width):
                    scatter.LineWidth = (float)curveInfo.Width;
                    AppendDebugInfo($"更新线宽: {curveInfo.Width}");
                    break;
                case nameof(CurveInfo.Name):
                    scatter.LegendText = curveInfo.Name;
                    AppendDebugInfo($"更新名称: {curveInfo.Name}");
                    break;
                case nameof(CurveInfo.Visible):
                    scatter.IsVisible = curveInfo.Visible;
                    AppendDebugInfo($"更新可见性: {curveInfo.Visible}");
                    // 在可见性改变后重新调整坐标轴
                    PlotView.Plot.Axes.AutoScale();
                    break;
                case nameof(CurveInfo.LineStyle):
                    var pattern = curveInfo.GetLinePattern();
                    scatter.LinePattern = pattern;
                    AppendDebugInfo($"更新线型: {curveInfo.LineStyle} -> {pattern}");
                    break;
                case nameof(CurveInfo.Opacity):
                case nameof(CurveInfo.PlotColor):
                            scatter.Color = new ScottPlot.Color(
                        curveInfo.PlotColor.R / 255.0f,
                        curveInfo.PlotColor.G / 255.0f,
                        curveInfo.PlotColor.B / 255.0f,
                        (float)curveInfo.Opacity);
                    AppendDebugInfo($"更新颜色: R={curveInfo.PlotColor.R}, G={curveInfo.PlotColor.G}, B={curveInfo.PlotColor.B}, 透明度={curveInfo.Opacity}");
                    break;
                case nameof(CurveInfo.OperationType):
                case nameof(CurveInfo.TargetCurve):
                    AppendDebugInfo($"开始处理曲线计算: {curveInfo.Name}");
                    AppendDebugInfo($"当前操作类型: {curveInfo.OperationType}");
                    AppendDebugInfo($"目标曲线: {curveInfo.TargetCurve?.Name ?? "无"}");
                    // 不再自动隐藏曲线，只更新数据
                    AppendDebugInfo($"即将调用 UpdateCurveData for {curveInfo.Name}");
                    curveInfo.UpdateCurveData();
                    AppendDebugInfo($"已调用 UpdateCurveData for {curveInfo.Name}");
                    UpdateOperationEnabledStates();
                    AppendDebugInfo($"完成曲线计算更新: {curveInfo.Name}");
                    PlotView.Refresh();
                    break;
                case nameof(CurveInfo.Ys):
                    AppendDebugInfo($"更新Ys: {curveInfo.Name}");
                    RedrawCurve(curveInfo);
                    break;
            }

            PlotView.Refresh();
        }

        // 颜色块点击，弹出Popup调色板
        private void OnCurveColorBlockPressed(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is Border border)
            {
                _isColorBlockDrag = false;
                _pendingColorBlock = border;
                _pendingColorBlockEventArgs = e;
                _colorBlockClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _colorBlockClickTimer.Tick += ColorBlockClickTimer_Tick;
                _colorBlockClickTimer.Start();
                border.MouseLeftButtonUp += ColorBlock_MouseLeftButtonUp;
            }
        }

        private void ColorBlockClickTimer_Tick(object? sender, EventArgs e)
        {
            _colorBlockClickTimer?.Stop();
            _colorBlockClickTimer = null;
            _isColorBlockDrag = true; // 超时，视为拖拽
        }

        private void ColorBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_colorBlockClickTimer != null)
            {
                _colorBlockClickTimer.Stop();
                _colorBlockClickTimer = null;
                if (!_isColorBlockDrag && _pendingColorBlock != null && _pendingColorBlockEventArgs != null)
                {
                    // 0.5秒内松开，视为单击，弹出调色板
                    ShowColorPicker(_pendingColorBlock, _pendingColorBlockEventArgs);
                }
            }
            if (sender is Border border)
                border.MouseLeftButtonUp -= ColorBlock_MouseLeftButtonUp;
            _pendingColorBlock = null;
            _pendingColorBlockEventArgs = null;
            _isColorBlockDrag = false;
        }

        private void ShowColorPicker(Border border, MouseButtonEventArgs e)
        {
            // 填充颜色块
            ColorGrid.Children.Clear();
            foreach (var colorHex in _colorPalette)
            {
                var brushConverter = new BrushConverter();
                var brush = brushConverter.ConvertFrom(colorHex);
                if (brush is Brush backgroundBrush)
                {
                    var btn = new Button
                    {
                        Background = backgroundBrush,
                        Tag = colorHex,
                        Width = COLOR_BLOCK_SIZE,
                        Height = COLOR_BLOCK_SIZE,
                        Padding = new Thickness(0),
                        Margin = new Thickness(1),
                        BorderThickness = new Thickness(0),
                        BorderBrush = Brushes.LightGray,
                        Focusable = false,
                        DataContext = border.DataContext
                    };
                    btn.Click += OnColorSelect;
                    ColorGrid.Children.Add(btn);
                }
            }
            // 记录当前DataContext，便于OnColorSelect使用
            ColorGrid.DataContext = border.DataContext;
            ColorPickerPopup.IsOpen = true;
            e.Handled = true;
        }

        // 色板点击，更新曲线颜色
        private void OnColorSelect(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex && ColorGrid.DataContext is CurveInfo info)
            {
                try
                {
                    var colorObj = System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    if (colorObj is System.Windows.Media.Color color)
                    {
                info.PlotColor = color;
                        // 确保UI更新颜色方块
                        info.OnPropertyChanged(nameof(info.Brush));
                // 更新对应曲线颜色
                int idx = _curveInfos.IndexOf(info);
                        if (idx >= 0 && idx < _curveMap.Count)
                {
                            if (_curveMap.Values.ElementAt(idx).Plot is ScottPlot.Plottables.Scatter scatter)
                    {
                        scatter.Color = new ScottPlot.Color(
                            color.R / 255.0f,
                            color.G / 255.0f,
                            color.B / 255.0f,
                            (float)info.Opacity
                        );
                        PlotView.Refresh();
                    }
                }
                ColorPickerPopup.IsOpen = false;
            }
                    else
                    {
                        AppendDebugInfo($"颜色转换失败: 无法将 {hex} 转换为颜色");
                    }
                }
                catch (Exception ex)
                {
                    AppendDebugInfo($"颜色转换失败: {ex.Message}");
                }
            }
        }

        private void AppendInfo(string message)
        {
            // 如果需要，可以在这里实现日志记录功能
            System.Diagnostics.Debug.WriteLine(message);
        }

        private ScottPlot.Plottables.Scatter DrawCurve(CurveInfo curveInfo)
        {
            AppendDebugInfo($"绘制曲线: {curveInfo.Name}");
            var scatter = PlotView.Plot.Add.Scatter(
                curveInfo.Xs,
                curveInfo.Ys,
                color: new ScottPlot.Color(
                    curveInfo.PlotColor.R / 255.0f,
                    curveInfo.PlotColor.G / 255.0f,
                    curveInfo.PlotColor.B / 255.0f,
                    (float)curveInfo.Opacity));

            scatter.LegendText = curveInfo.Name;
            scatter.LineWidth = (float)curveInfo.Width;
            scatter.LinePattern = curveInfo.GetLinePattern();
            scatter.IsVisible = curveInfo.Visible;
            scatter.MarkerSize = (float)curveInfo.MarkerSize;

            _curveMap[curveInfo.HashId] = (curveInfo, scatter);
            AppendDebugInfo($"曲线属性: 线宽={curveInfo.Width}, 线型={curveInfo.LineStyle}, 可见性={curveInfo.Visible}, 标记大小={curveInfo.MarkerSize}");
            return scatter;
        }

        private void RedrawCurve(CurveInfo curveInfo)
        {
            AppendDebugInfo($"重绘曲线: {curveInfo.Name}");
            if (_curveMap.TryGetValue(curveInfo.HashId, out var curveData))
            {
                PlotView.Plot.Remove(curveData.Plot);
            }
            DrawCurve(curveInfo);
            PlotView.Plot.Axes.AutoScale();
            PlotView.Refresh();
            AppendDebugInfo("重绘完成");
        }

        private void RedrawAllCurves()
        {
            AppendDebugInfo("开始重绘所有曲线");
            PlotView.Plot.Clear();
            _curveMap.Clear();
            foreach (var curveInfo in _curveInfos)
            {
                DrawCurve(curveInfo);
            }
            PlotView.Plot.Axes.AutoScale();
            PlotView.Refresh();
            AppendDebugInfo("重绘完成");
        }

        private void ClearDebugButton_Click(object sender, RoutedEventArgs e)
        {
            DebugTextBox.Clear();
        }

        private void CopyDebugButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(DebugTextBox.Text))
            {
                System.Windows.Clipboard.SetText(DebugTextBox.Text);
            }
        }

        private void AppendDebugInfo(string message)
        {
            DebugTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            DebugTextBox.ScrollToEnd();
        }

        private int _colorIndex = 0;
        private ScottPlot.Color GetNextColor()
        {
            // 获取当前已使用的颜色
            var usedColors = _curveInfos.Select(c => c.PlotColor).ToList();
            // 先尝试找未用过的色盘颜色
            foreach (var hex in _colorPalette)
            {
                var color = ColorFromHex(hex);
                if (!usedColors.Any(uc => uc.R == color.R && uc.G == color.G && uc.B == color.B))
                    return color;
            }
            // 如果都用过了，循环使用色盘
            var hex2 = _colorPalette[_colorIndex % _colorPalette.Length];
            _colorIndex++;
            return ColorFromHex(hex2);
        }

        private string GetSmartSuffix(string? newFileName, List<string> existingFiles)
        {
            if (string.IsNullOrEmpty(newFileName) || existingFiles == null || existingFiles.Count == 0)
                return string.Empty;

            string[] newParts = Path.GetFileNameWithoutExtension(newFileName).Split('_');
            var existingPartsList = existingFiles
                .Select(f => Path.GetFileNameWithoutExtension(f).Split('_'))
                .ToList();

            // 从后往前找最后一个不同的片段
            for (int i = newParts.Length - 1; i >= 0; i--)
            {
                string part = newParts[i];
                bool isUnique = existingPartsList.All(parts =>
                    i >= parts.Length || parts[i] != part);
                if (isUnique && !string.IsNullOrWhiteSpace(part))
                {
                    return part;
                }
            }
            // 如果都相同，返回整个文件名
            return Path.GetFileNameWithoutExtension(newFileName);
        }

        private ScottPlot.IPlottable? AddCurveToPlot(string label, double[] xs, double[] ys, string sourceFileName, bool isFirstCurveInFile)
        {
            if (xs == null || ys == null || xs.Length != ys.Length)
            {
                AppendDebugInfo($"数据点数量不匹配或数据为空: X={xs?.Length ?? 0}, Y={ys?.Length ?? 0}");
                return null;
            }

            AppendDebugInfo($"数据点数量: X={xs.Length}, Y={ys.Length}");

            AppendDebugInfo($"AddCurveToPlot: label={label}, xs前5项={string.Join(", ", xs.Take(5))}, ys前5项={string.Join(", ", ys.Take(5))}");

            AppendDebugInfo($"创建 CurveInfo 并传递日志委托: label={label}");
            var curveInfo = new CurveInfo(AppendDebugInfo);
            curveInfo.Name = label ?? "未命名曲线";
            curveInfo.Xs = xs;
            curveInfo.Ys = ys;
            curveInfo.Visible = isFirstCurveInFile;
            curveInfo.SourceFileName = sourceFileName ?? "未知文件";
            curveInfo.GenerateHashId();

            // 检查是否已存在相同名称的曲线
            if (_curveInfos.Any(c => c.HashId == curveInfo.HashId))
            {
                AppendDebugInfo($"曲线 {label ?? "未命名曲线"} 已存在，跳过");
                ShowDuplicateCurveToast();
                return null;
            }

            AppendDebugInfo($"创建曲线信息: {label ?? "未命名曲线"}, HashId={curveInfo.HashId}");

            // 设置曲线颜色
            var nextColor = GetNextColor();
            curveInfo.PlotColor = System.Windows.Media.Color.FromRgb(nextColor.R, nextColor.G, nextColor.B);
            // 确保UI更新颜色方块
            curveInfo.OnPropertyChanged(nameof(curveInfo.Brush));
            AppendDebugInfo($"设置曲线颜色: R={curveInfo.PlotColor.R}, G={curveInfo.PlotColor.G}, B={curveInfo.PlotColor.B}");

            // 检查名字冲突，自动加智能后缀
            string uniqueLabel = curveInfo.Name;
            int suffixIndex = 1;
            var existingNames = _curveInfos.Select(ci => ci.Name).ToHashSet();
            var existingFiles = _curveInfos.Select(ci => ci.SourceFileName).ToList();
            string smartSuffix = GetSmartSuffix(sourceFileName, existingFiles);
            
            while (existingNames.Contains(uniqueLabel))
            {
                if (suffixIndex == 1)
                {
                    if (!string.IsNullOrWhiteSpace(smartSuffix))
                        uniqueLabel = $"{curveInfo.Name}-{smartSuffix}";
                    else
                        uniqueLabel = $"{curveInfo.Name}({Path.GetFileNameWithoutExtension(sourceFileName)})";
                }
                else
                {
                    uniqueLabel = $"{curveInfo.Name}-{smartSuffix}_{suffixIndex}";
                }
                suffixIndex++;
            }

            curveInfo.Name = uniqueLabel;
            AppendDebugInfo($"更新曲线名称: {curveInfo.Name}");

            var scatter = PlotView.Plot.Add.Scatter(xs, ys);
            if (scatter != null)
            {
                scatter.Color = new ScottPlot.Color(
                    curveInfo.PlotColor.R / 255.0f,
                    curveInfo.PlotColor.G / 255.0f,
                    curveInfo.PlotColor.B / 255.0f,
                    (float)curveInfo.Opacity);
                scatter.LineWidth = (float)curveInfo.Width;
                scatter.MarkerSize = (float)curveInfo.MarkerSize;
                scatter.LegendText = curveInfo.Name;
                scatter.IsVisible = isFirstCurveInFile;

                _curveInfos.Add(curveInfo);
                if (curveInfo.HashId != null)
                {
                    _curveMap[curveInfo.HashId] = (curveInfo, scatter);
                }
                else
                {
                    AppendDebugInfo("警告：曲线HashId为空，无法添加到映射中");
                }

                // 增量更新OtherCurves
                foreach (var ci in _curveInfos)
                {
                    if (ci != curveInfo && !ci.OtherCurves.Contains(curveInfo))
                        ci.OtherCurves.Add(curveInfo);
                }
                curveInfo.OtherCurves.Clear();
                foreach (var ci in _curveInfos)
                {
                    if (ci != curveInfo)
                        curveInfo.OtherCurves.Add(ci);
                }
                AppendDebugInfo($"当前曲线数量: {_curveInfos.Count}, 映射数量: {_curveMap.Count}");
                return scatter;
            }
            return null;
        }

        private void OnTableClick(object sender, RoutedEventArgs e)
        {
            // 获取中间区域的Grid
            var middleGrid = MainGrid.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetColumn(g) == 1);
            if (middleGrid == null) return;

            if (BGrid.Visibility == Visibility.Visible)
            {
                // 保存当前比例
                var totalHeight = middleGrid.RowDefinitions[0].Height.Value + middleGrid.RowDefinitions[2].Height.Value;
                _lastSplitterPosition = middleGrid.RowDefinitions[2].Height.Value / totalHeight;
                
                // 隐藏设置面板
                BGrid.Visibility = Visibility.Collapsed;
                TableButton.ToolTip = "显示数据表格";
                TableButton.Style = (Style)FindResource("ToolbarButtonStyle");
                
                // 设置图表区域占满
                middleGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                middleGrid.RowDefinitions[2].Height = new GridLength(0);
            }
            else
            {
                // 显示设置面板
                BGrid.Visibility = Visibility.Visible;
                TableButton.ToolTip = "隐藏数据表格";
                TableButton.Style = (Style)FindResource("HighlightedButtonStyle");
                
                // 恢复之前的比例
                if (_lastSplitterPosition > 0 && _lastSplitterPosition < 1)
                {
                    middleGrid.RowDefinitions[0].Height = new GridLength(1 - _lastSplitterPosition, GridUnitType.Star);
                    middleGrid.RowDefinitions[2].Height = new GridLength(_lastSplitterPosition, GridUnitType.Star);
                }
                else
                {
                    // 默认比例
                    middleGrid.RowDefinitions[0].Height = new GridLength(2, GridUnitType.Star);
                    middleGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
                }
            }
        }

        private void ShowDuplicateCurveToast()
        {
            DuplicateCurveToast.Visibility = Visibility.Visible;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, e) =>
            {
                DuplicateCurveToast.Visibility = Visibility.Collapsed;
                timer.Stop();
            };
            timer.Start();
        }

        private void UpdateOperationEnabledStates()
        {
            AppendDebugInfo("开始更新操作状态");
            
            var selectedTargets = _curveInfos
                .Where(ci => ci.OperationType == "减" && ci.TargetCurve != null)
                .Select(ci => ci.TargetCurve)
                .ToHashSet();
            
            AppendDebugInfo($"找到 {selectedTargets.Count} 个被选中的目标曲线");
            foreach (var target in selectedTargets)
            {
                AppendDebugInfo($"目标曲线: {target?.Name}");
            }

            foreach (var ci in _curveInfos)
            {
                AppendDebugInfo($"处理曲线: {ci.Name}");
                AppendDebugInfo($"当前操作类型: {ci.OperationType}");
                AppendDebugInfo($"当前目标曲线: {ci.TargetCurve?.Name ?? "无"}");
                
                if (selectedTargets.Contains(ci))
                {
                    AppendDebugInfo($"曲线 {ci.Name} 是被选中的目标，需要禁用操作");
                    if (ci.OperationType != "无")
                    {
                        AppendDebugInfo($"重置曲线 {ci.Name} 的操作类型为'无'");
                        ci.OperationType = "无";
                        ci.UpdateCurveData();
                    }
                    ci.IsOperationEnabled = false;
                    if (ci.TargetCurve != null)
                    {
                        AppendDebugInfo($"清除曲线 {ci.Name} 的目标曲线");
                        ci.TargetCurve = null;
                    }
                    ci.IsTargetCurveEnabled = false;
                }
                else
                {
                    AppendDebugInfo($"曲线 {ci.Name} 不是被选中的目标，启用操作");
                    ci.IsOperationEnabled = true;
                    // ci.IsTargetCurveEnabled = true;
                }
            }
            
            AppendDebugInfo("操作状态更新完成");
        }

        private void UpdateOtherCurves()
        {
            AppendDebugInfo("开始更新其他曲线列表");
            AppendDebugInfo($"当前曲线总数: {_curveInfos.Count}");
            
            foreach (var ci in _curveInfos)
            {
                AppendDebugInfo($"处理曲线: {ci.Name}");
                AppendDebugInfo($"当前其他曲线数量: {ci.OtherCurves.Count}");
                
                ci.OtherCurves.Clear();
                foreach (var other in _curveInfos)
                {
                    // 确保不添加自身到OtherCurves列表中
                    if (other.HashId != ci.HashId)
                    {
                        AppendDebugInfo($"添加其他曲线: {other.Name} 到 {ci.Name} 的列表中");
                        ci.OtherCurves.Add(other);
                    }
                }
                
                AppendDebugInfo($"更新后其他曲线数量: {ci.OtherCurves.Count}");
            }
            
            AppendDebugInfo("其他曲线列表更新完成");
        }

        private void CurveListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void CurveListView_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is not ListView listView) return;
                    var listViewItem = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
                    if (listViewItem == null) return;
                    var curveInfoObj = listView.ItemContainerGenerator.ItemFromContainer(listViewItem);
                    if (curveInfoObj is not CurveInfo curveInfo) return;
                    DragDrop.DoDragDrop(listViewItem, curveInfo, DragDropEffects.Move);
                }
            }
        }

        private void CurveListView_DragOver(object sender, DragEventArgs e)
        {
            if (sender is not ListView listView) return;
            Point position = e.GetPosition(listView);
            int index = GetCurrentIndex(listView, position);
            if (index != _insertionIndex)
            {
                RemoveInsertionAdorner();
                _insertionIndex = index;
                ShowInsertionAdorner(listView, index);
            }
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void CurveListView_Drop(object sender, DragEventArgs e)
        {
            RemoveInsertionAdorner();
            if (e.Data.GetDataPresent(typeof(CurveInfo)))
            {
                var droppedData = e.Data.GetData(typeof(CurveInfo)) as CurveInfo;
                if (sender is not ListView listView) return;
                var collection = CurveListView.ItemsSource as ObservableCollection<CurveInfo>;
                if (collection == null || droppedData == null) return;
                int oldIndex = collection.IndexOf(droppedData);
                int newIndex = _insertionIndex;
                var target = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
                if (target != null)
                {
                    var targetDataObj = listView.ItemContainerGenerator.ItemFromContainer(target);
                    if (targetDataObj is CurveInfo targetData)
                        newIndex = collection.IndexOf(targetData);
                }
                // 修正newIndex边界，防止越界
                if (newIndex < 0) newIndex = 0;
                if (newIndex > collection.Count - 1) newIndex = collection.Count - 1;
                if (oldIndex != newIndex && oldIndex != -1 && newIndex != -1)
                {
                    collection.Move(oldIndex, newIndex);
                    RedrawAllCurves(); // 拖动排序后重绘，保证legend顺序
                }
            }
            _insertionIndex = -1;
        }

        private int GetCurrentIndex(ListView listView, Point position)
        {
            if (listView == null) return 0;
            for (int i = 0; i < listView.Items.Count; i++)
            {
                var item = listView.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (item != null)
                {
                    Rect bounds = VisualTreeHelper.GetDescendantBounds(item);
                    Point topLeft = item.TranslatePoint(new Point(0, 0), listView);
                    Rect itemRect = new Rect(topLeft, bounds.Size);
                    if (position.Y < itemRect.Top + itemRect.Height / 2)
                        return i;
                }
            }
            return listView?.Items.Count ?? 0;
        }

        private void ShowInsertionAdorner(ListView listView, int index)
        {
            if (_adornerLayer == null)
                _adornerLayer = AdornerLayer.GetAdornerLayer(listView);
            if (_adornerLayer == null)
                return;
            _insertionAdorner = new InsertionAdorner(listView, index);
            _adornerLayer.Add(_insertionAdorner);
        }

        private void RemoveInsertionAdorner()
        {
            if (_insertionAdorner != null && _adornerLayer != null)
            {
                _adornerLayer.Remove(_insertionAdorner);
                _insertionAdorner = null;
            }
        }

        // 辅助方法：查找父级
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T t)
                {
                    return t;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // 插入线Adorner类
        private class InsertionAdorner : Adorner
        {
            private readonly ListView _listView;
            private readonly int _index;
            public InsertionAdorner(ListView listView, int index) : base(listView)
            {
                _listView = listView;
                _index = index;
                IsHitTestVisible = false;
            }
            protected override void OnRender(DrawingContext drawingContext)
            {
                double y = 0;
                if (_index < _listView.Items.Count)
                {
                    var item = _listView.ItemContainerGenerator.ContainerFromIndex(_index) as ListViewItem;
                    if (item != null)
                    {
                        Point topLeft = item.TranslatePoint(new Point(0, 0), _listView);
                        y = topLeft.Y;
                    }
                }
                else if (_listView.Items.Count > 0)
                {
                    var item = _listView.ItemContainerGenerator.ContainerFromIndex(_listView.Items.Count - 1) as ListViewItem;
                    if (item != null)
                    {
                        Point topLeft = item.TranslatePoint(new Point(0, 0), _listView);
                        y = topLeft.Y + item.ActualHeight;
                    }
                }
                Pen pen = new Pen(Brushes.DodgerBlue, 2);
                drawingContext.DrawLine(pen, new Point(0, y), new Point(_listView.ActualWidth, y));
            }
        }

        private void MainWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void MainWindow_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var filePath in files)
                {
                    try
                    {
                        string fileExtension = System.IO.Path.GetExtension(filePath).ToLower();
                        if (fileExtension == ".csv")
                        {
                            ProcessDataFile(filePath);
                        }
                        else if (fileExtension == ".xls" || fileExtension == ".xlsx")
                        {
                            LoadExcelFile(filePath);
                        }
                        else if (fileExtension == ".txt")
                        {
                            LoadTxtFile(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"拖拽文件时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            e.Handled = true;
        }
    }
} 