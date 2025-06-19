using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using Avalonia.Input;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExcelDataReader;
using System.Data;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using OxyPlot;
using System.Windows.Input;
using Avalonia;
using System.Security.Cryptography;
using Avalonia.Data.Converters;
using QuquPlot.Models;

namespace QuquPlot.Views
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // 在 MainWindow 类内部添加一个列表来存储曲线
        private List<ScottPlot.IPlottable> loadedPlottables = new List<ScottPlot.IPlottable>();
        private ObservableCollection<CurveInfo> curveInfos = new ObservableCollection<CurveInfo>();
        private int curveCounter = 0; // 用于分配颜色和唯一名称
        private bool isCurveListVisible = true;
        public bool IsCurveListVisible
        {
            get => isCurveListVisible;
            set
            {
                if (isCurveListVisible != value)
                {
                    isCurveListVisible = value;
                    OnPropertyChanged(nameof(IsCurveListVisible));
                }
            }
        }

        // 定义颜色映射表，顺序与 XAML 色盘一致（从左到右，从上到下）
        private readonly (string name, string hexColor)[] colorPalette = new[]
        {
            ("Red1", "#FF8A80"), ("Red2", "#FF5252"), ("Red3", "#D32F2F"),
            ("Blue1", "#82B1FF"), ("Blue2", "#448AFF"), ("Blue3", "#1976D2"),
            ("Green1", "#B9F6CA"), ("Green2", "#69F0AE"), ("Green3", "#00C853"),
            ("Purple1", "#E1BEE7"), ("Purple2", "#CE93D8"), ("Purple3", "#9C27B0"),
            ("Yellow1", "#FFF59D"), ("Yellow2", "#FFEB3B"), ("Yellow3", "#FBC02D"),
            ("Cyan1", "#B2EBF2"), ("Cyan2", "#4DD0E1"), ("Cyan3", "#0097A7"),
            ("Orange1", "#FFE0B2"), ("Orange2", "#FFB74D"), ("Orange3", "#F57C00"),
            ("Pink1", "#F8BBD0"), ("Pink2", "#F48FB1"), ("Pink3", "#C2185B"),
            ("Brown1", "#D7CCC8"), ("Brown2", "#A1887F"), ("Brown3", "#5D4037"),
            ("Indigo1", "#C5CAE9"), ("Indigo2", "#7986CB"), ("Indigo3", "#303F9F"),
            ("Gray1", "#FAFAFA"), ("Gray2", "#EEEEEE"), ("Gray3", "#BDBDBD"),
            ("Gray4", "#9E9E9E"), ("Gray5", "#616161"), ("Gray6", "#212121"),
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

        // 线型选项
        public static IReadOnlyList<string> LineStyleOptions { get; } = new[] { "实线", "长段", "短段", "点状" };
        // 操作类型选项
        public static IReadOnlyList<string> OperationTypeOptions { get; } = new[] { "无", "减" };
        // 目标曲线选项（直接绑定CurveInfos即可）

        public ICommand MoveCurveUpCommand { get; }
        public ICommand MoveCurveDownCommand { get; }

        private GridLength? lastPlotRowHeight = null;
        private GridLength? lastCurveListRowHeight = null;
        private RowDefinition? plotRowDef;
        private RowDefinition? curveListRowDef;

        private string xAxisLabel = "X";
        private string yAxisLabel = "Y";
        private bool isXAxisLabelManuallyEdited = false;
        private bool isYAxisLabelManuallyEdited = false;

        public string XAxisLabel
        {
            get => xAxisLabel;
            set
            {
                if (xAxisLabel != value)
                {
                    xAxisLabel = value;
                    OnPropertyChanged(nameof(XAxisLabel));
                    if (avaPlot?.Plot?.Axes?.Bottom?.Label != null)
                    {
                        avaPlot.Plot.Axes.Bottom.Label.Text = xAxisLabel;
                        avaPlot.Refresh();
                    }
                    isXAxisLabelManuallyEdited = true;
                }
            }
        }
        public string YAxisLabel
        {
            get => yAxisLabel;
            set
            {
                if (yAxisLabel != value)
                {
                    yAxisLabel = value;
                    OnPropertyChanged(nameof(YAxisLabel));
                    if (avaPlot?.Plot?.Axes?.Left?.Label != null)
                    {
                        avaPlot.Plot.Axes.Left.Label.Text = yAxisLabel;
                        avaPlot.Refresh();
                    }
                    isYAxisLabelManuallyEdited = true;
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            // 获取RowDefinition引用
            plotRowDef = this.Find<RowDefinition>("PlotRow");
            curveListRowDef = this.Find<RowDefinition>("CurveListRow");
            // 设置默认label
            XAxisLabel = "X";
            YAxisLabel = "Y";
            if (avaPlot?.Plot?.Axes?.Bottom?.Label != null)
                avaPlot.Plot.Axes.Bottom.Label.Text = XAxisLabel;
            if (avaPlot?.Plot?.Axes?.Left?.Label != null)
                avaPlot.Plot.Axes.Left.Label.Text = YAxisLabel;


            // 1. 创建 FontStyler 实例
            var fontStyler = new ScottPlot.Stylers.FontStyler(avaPlot!.Plot);

            // 2. 自动检测最佳字体（推荐，能自动适配中英文）
            fontStyler.Automatic();

            // 3. 或者手动指定字体
            // fontStyler.Set("DejaVu Sans");

            this.Loaded += (s, e) =>
            {
                avaPlot!.Plot.Clear();
                // avaPlot.Plot.Add.Signal(Enumerable.Range(0, 1000).Select(i => Math.Sin(i * 0.01)).ToArray());

                // 恢复坐标轴字体设置
                avaPlot.Plot.Axes.Bottom.Label.FontSize = 22;
                avaPlot.Plot.Axes.Left.Label.FontSize = 22;
                avaPlot.Plot.Axes.Bottom.TickLabelStyle.FontSize = 18;
                avaPlot.Plot.Axes.Left.TickLabelStyle.FontSize = 18;

                // 恢复图例设置
                avaPlot.Plot.Legend.FontSize = 18;
                avaPlot.Plot.Legend.BackgroundColor = ColorFromHex("#FFFFFF", 0.8);
                avaPlot.Plot.Legend.ShadowColor = ColorFromHex("#FFFFFF", 0.0);
                avaPlot.Plot.Legend.OutlineColor = ColorFromHex("#FFFFFF", 0.0);

                // 禁用右键缩放
                var uip = avaPlot.UserInputProcessor;
                uip.RightClickDragZoom(false);

                

                avaPlot.Plot.Legend.Alignment = ScottPlot.Alignment.LowerLeft;
                avaPlot.Refresh();
            };

            // 添加点击事件处理器来关闭关于面板
            this.PointerPressed += OnWindowPointerPressed;

            // 设置 ListBox 的数据源
            CurveInfoList.ItemsSource = curveInfos;
            CurveInfoList.DataContext = this;  // 添加这行以支持目标曲线选择

            // 注册拖放事件
            DropBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            DropBorder.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
            DropBorder.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            DropBorder.AddHandler(DragDrop.DropEvent, OnDrop);

            MoveCurveUpCommand = new RelayCommand<CurveInfo>(MoveCurveUp, CanMoveCurveUp);
            MoveCurveDownCommand = new RelayCommand<CurveInfo>(MoveCurveDown, CanMoveCurveDown);
            (MoveCurveUpCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
            (MoveCurveDownCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();

            // 监听窗口大小变化和曲线数量变化，动态调整曲线列表区高度
            this.GetObservable(BoundsProperty).Subscribe(_ => UpdateCurveListRowHeight());
            curveInfos.CollectionChanged += (s, e) => UpdateCurveListRowHeight();
        }

        private void CurveInfo_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is CurveInfo info)
            {
                var hashId = info.HashId;
                var idx = curveInfos.ToList().FindIndex(ci => ci.HashId == hashId);
                // AppendInfo($"[CurveInfo_PropertyChanged] Property={e.PropertyName}, Index={idx}, TotalCurves={curveInfos.Count}");
                if (idx >= 0 && idx < loadedPlottables.Count)
                {
                    dynamic plotObj = loadedPlottables[idx];
                    if (e.PropertyName == nameof(CurveInfo.Width))
                    {
                        try { plotObj.LineWidth = (float)info.Width; } catch { }
                        avaPlot.Refresh();
                    }
                    else if (e.PropertyName == nameof(CurveInfo.Name))
                    {
                        try { plotObj.Label = info.Name; } catch { }
                        avaPlot.Plot.ShowLegend();
                    }
                    else if (e.PropertyName == nameof(CurveInfo.Visible))
                    {
                        try 
                        { 
                            // AppendInfo($"[CurveInfo_PropertyChanged] Setting visibility for curve {info.Name} to {info.Visible}");
                            plotObj.IsVisible = info.Visible;
                            avaPlot.Refresh();
                        } 
                        catch (Exception ex) 
                        { 
                            AppendInfo($"[CurveInfo_PropertyChanged] Error setting visibility: {ex.Message}");
                        }
                    }
                    else if (e.PropertyName == nameof(CurveInfo.LineStyle))
                    {
                        try 
                        { 
                            plotObj.LineStyle.Pattern = info.LineStyle switch
                            {
                                "长段" => ScottPlot.LinePattern.Dashed,
                                "点状" => ScottPlot.LinePattern.Dotted,
                                "短段" => ScottPlot.LinePattern.DenselyDashed,
                                _ => ScottPlot.LinePattern.Solid
                            };
                            avaPlot.Refresh();
                        } 
                        catch { }
                    }
                    else if (e.PropertyName == nameof(CurveInfo.Opacity))
                    {
                        try
                        {
                            plotObj.Color = new ScottPlot.Color(
                                info.PlotColor.R,
                                info.PlotColor.G,
                                info.PlotColor.B,
                                (byte)(info.Opacity * 255)
                            );
                            avaPlot.Refresh();
                        } 
                        catch { }
                    }
                    else if (e.PropertyName == nameof(CurveInfo.XMagnitude))
                    {
                        try
                        {
                            // 重新创建曲线
                            avaPlot.Plot.Remove(plotObj);
                            var scatter = avaPlot.Plot.Add.Scatter(info.ModifiedXs, info.Ys);
                            scatter.Color = info.PlotColor;
                            scatter.LegendText = info.Name;
                            scatter.LineWidth = (float)info.Width;
                            scatter.MarkerSize = 0;
                            scatter.IsVisible = info.Visible;
                            loadedPlottables[idx] = scatter;
                            avaPlot.Plot.Axes.AutoScale();
                            avaPlot.Refresh();
                        }
                        catch { }
                    }
                    else if (e.PropertyName == "NeedsRedraw")
                    {
                        AppendInfo($"[NeedsRedraw] Updating curve {info.Name}, Ys length={info.Ys.Length}");
                        try
                        {
                            // 重新创建曲线
                            avaPlot.Plot.Remove(plotObj);
                            ScottPlot.IPlottable newPlot;

                            var scatter = avaPlot.Plot.Add.Scatter(info.ModifiedXs, info.Ys);
                            scatter.Color = info.PlotColor;
                            scatter.LegendText = info.Name;
                            scatter.LineWidth = (float)info.Width;
                            scatter.MarkerSize = 0;
                            scatter.IsVisible = info.Visible;
                            newPlot = scatter;
                            loadedPlottables[idx] = newPlot;
                            avaPlot.Refresh();
                        }
                        catch (Exception ex)
                        {
                            AppendInfo($"[NeedsRedraw] Error: {ex.Message}");
                        }
                    }
                    else if (e.PropertyName == nameof(CurveInfo.OperationType) || e.PropertyName == nameof(CurveInfo.TargetCurve))
                    {
                        try
                        {
                            info.UpdateCurveData();
                            UpdateOperationEnabledStates();
                            avaPlot.Refresh();
                        }
                        catch { }
                    }
                    else if (e.PropertyName == "NeedsAutoScale")
                    {
                        avaPlot.Plot.Axes.AutoScale();
                        avaPlot.Refresh();
                    }
                }
            }
        }



        // 自动检测分隔符（逗号或Tab），若无分隔符返回null
        private string? DetectDelimiter(string[] lines)
        {
            foreach (var line in lines.Take(10))
            {
                if (line.Contains('\t')) return "\t";
                if (line.Contains(',')) return ",";
            }
            return null; // 没有分隔符
        }

        private (int firstDataIndex, int lastDataIndex) FindDataRange(string[] lines)
        {
            AppendInfo($"[FindDataRange] Starting data range detection for file with {lines.Length} lines");
            var delimiter = DetectDelimiter(lines);
            // Find the last valid data row
            int lastDataIndex = -1;
            int numColumns = 0;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string[] columns = delimiter == null
                    ? new[] { lines[i].Trim() }
                    : lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length < 1) continue;

                if (double.TryParse(columns[0], out _))
                {
                    lastDataIndex = i;
                    numColumns = columns.Length;
                    AppendInfo($"[FindDataRange] Found last data row at index {lastDataIndex} with {numColumns} columns");
                    AppendInfo($"[FindDataRange] Last data row content: {lines[lastDataIndex]}");
                    break;
                }
            }

            if (lastDataIndex == -1)
            {
                AppendInfo("[FindDataRange] No valid data found in file");
                return (-1, -1); // No valid data found
            }

            // Find the first valid data row
            int firstDataIndex = -1;
            for (int i = 0; i <= lastDataIndex; i++)
            {
                string[] columns = delimiter == null
                    ? new[] { lines[i].Trim() }
                    : lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length != numColumns) continue;

                if (double.TryParse(columns[0], out _))
                {
                    firstDataIndex = i;
                    AppendInfo($"[FindDataRange] Found first data row at index {firstDataIndex}");
                    AppendInfo($"[FindDataRange] First data row content: {lines[firstDataIndex]}");
                    break;
                }
            }

            AppendInfo($"[FindDataRange] Data range: {firstDataIndex} to {lastDataIndex} (total {lastDataIndex - firstDataIndex + 1} rows)");
            // Check for potential header
            if (firstDataIndex > 0)
            {
                var potentialHeader = lines[firstDataIndex - 1];
                AppendInfo($"[FindDataRange] Potential header row: {potentialHeader}");
            }
            return (firstDataIndex, lastDataIndex);
        }

        private async Task<(double[] xs, List<(string label, double[] ys)>)> LoadCsvMultiAsync(string filePath)
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            var delimiter = DetectDelimiter(lines);
            var (firstDataIndex, lastDataIndex) = FindDataRange(lines);
            
            if (firstDataIndex >= lastDataIndex)
            {
                AppendInfo($"[LoadCsvMultiAsync] No valid data found in file: {filePath}");
                return (Array.Empty<double>(), new List<(string, double[])>());
            }

            var data = new List<double[]>();
            string[]? headers = null;

            // Check if the line before firstDataIndex contains headers
            if (firstDataIndex > 0)
            {
                string[] potentialHeader = delimiter == null
                    ? new[] { lines[firstDataIndex - 1].Trim() }
                    : lines[firstDataIndex - 1].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (potentialHeader.All(p => !double.TryParse(p, out _)))
                {
                    headers = potentialHeader;
                    // 自动设置label（仅当为默认值时）
                    if (headers.Length >= 2)
                    {
                        if (XAxisLabel == "X") XAxisLabel = headers[0];
                        if (YAxisLabel == "Y") YAxisLabel = headers[1];
                    }
                }
            }

            // Process only the valid data range
            for (int i = firstDataIndex; i <= lastDataIndex; i++)
            {
                string[] parts = delimiter == null
                    ? new[] { lines[i].Trim() }
                    : lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
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

            if (data.Count == 0) return (Array.Empty<double>(), new List<(string, double[])>());

            int colCount = data[0].Length;
            int rowCount = data.Count;

            // 单列数据处理
            if (colCount == 1)
            {
                double[] ys = new double[rowCount];
                for (int row = 0; row < rowCount; row++)
                    ys[row] = data[row][0];
                double[] xs = Enumerable.Range(0, rowCount).Select(i => (double)i).ToArray();
                AppendInfo($"[LoadCsvMultiAsync] Single column detected, using index as X-axis, count={rowCount}");
                var yss = new List<(string, double[])> { (headers != null && headers.Length > 0 ? headers[0] : "Y", ys) };
                return (xs, yss);
            }

            // Extract X values from the first column
            double[] xs2 = new double[rowCount];
            for (int row = 0; row < rowCount; row++)
            {
                xs2[row] = data[row][0];
            }
            AppendInfo($"[LoadCsvMultiAsync] X-axis range: {xs2[0]} to {xs2[xs2.Length-1]}");

            var yss2 = new List<(string, double[])>();
            // Start from column 1 (second column) for Y values
            for (int col = 1; col < colCount; col++)
            {
                double[] ys = new double[rowCount];
                for (int row = 0; row < rowCount; row++)
                {
                    ys[row] = data[row][col];
                }
                string label = headers != null && headers.Length > col ? headers[col] : $"Col-{col+1}";
                yss2.Add((label, ys));
            }
            return (xs2, yss2);
        }

        private async Task<(double[] xs, List<(string label, double[] ys)>)> LoadTxtMultiAsync(string filePath)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                var delimiter = DetectDelimiter(lines);
                var (firstDataIndex, lastDataIndex) = FindDataRange(lines);
                
                if (firstDataIndex >= lastDataIndex)
                {
                    AppendInfo($"[LoadTxtMultiAsync] No valid data found in file: {filePath}");
                    return (Array.Empty<double>(), new List<(string, double[])>());
                }

                var data = new List<List<double>>();
                string[]? columnNames = null;

                // Check if the line before firstDataIndex contains headers
                if (firstDataIndex > 0)
                {
                    string[] potentialHeader = delimiter == null
                        ? new[] { lines[firstDataIndex - 1].Trim() }
                        : lines[firstDataIndex - 1].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                    if (potentialHeader.All(p => !double.TryParse(p, out _)))
                    {
                        columnNames = potentialHeader;
                        // 自动设置label（仅当为默认值时）
                        if (columnNames.Length >= 2)
                        {
                            if (XAxisLabel == "X") XAxisLabel = columnNames[0];
                            if (YAxisLabel == "Y") YAxisLabel = columnNames[1];
                        }
                    }
                }

                // Process only the valid data range
                for (int i = firstDataIndex; i <= lastDataIndex; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("!"))
                        continue;

                    string[] parts = delimiter == null
                        ? new[] { line }
                        : line.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 1) continue;
                    var values = parts
                        .Select(v => v.Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => double.TryParse(v, out double result) ? result : double.NaN)
                        .ToList();

                    if (values.Count >= 1 && values.All(v => !double.IsNaN(v)))
                    {
                        data.Add(values);
                    }
                }

                if (data.Count == 0)
                {
                    AppendInfo($"[LoadTxtMultiAsync] No valid data found in TXT file: {filePath}");
                    return (Array.Empty<double>(), new List<(string, double[])>());
                }

                int colCount = data[0].Count;
                int rowCount = data.Count;

                // 单列数据处理
                if (colCount == 1)
                {
                    double[] ys = new double[rowCount];
                    for (int row = 0; row < rowCount; row++)
                        ys[row] = data[row][0];
                    double[] xs = Enumerable.Range(0, rowCount).Select(i => (double)i).ToArray();
                    AppendInfo($"[LoadTxtMultiAsync] Single column detected, using index as X-axis, count={rowCount}");
                    var yss = new List<(string, double[])> { (columnNames != null && columnNames.Length > 0 ? columnNames[0] : "Y", ys) };
                    return (xs, yss);
                }

                // Extract X values from the first column
                double[] xs2 = new double[rowCount];
                for (int row = 0; row < rowCount; row++)
                {
                    xs2[row] = data[row][0];
                }
                AppendInfo($"[LoadTxtMultiAsync] X-axis range: {xs2[0]} to {xs2[xs2.Length-1]}");

                var yss2 = new List<(string, double[])>();
                // Start from column 1 (second column) for Y values
                for (int col = 1; col < colCount; col++)
                {
                    double[] ys = new double[rowCount];
                    for (int row = 0; row < rowCount; row++)
                    {
                        ys[row] = data[row][col];
                    }
                    string label = col < columnNames?.Length ? columnNames[col] : $"列{col + 1}";
                    yss2.Add((label, ys));
                }

                if (xs2.Length == 0 || yss2.Count == 0)
                {
                    AppendInfo($"[LoadTxtMultiAsync] Invalid data structure in TXT file: {filePath}");
                    return (Array.Empty<double>(), new List<(string, double[])>());
                }

                return (xs2, yss2);
            }
            catch (Exception ex)
            {
                AppendInfo($"[LoadTxtMultiAsync] Error loading TXT file {filePath}: {ex.Message}");
                return (Array.Empty<double>(), new List<(string, double[])>());
            }
        }

        private async Task<(double[] xs, List<(string label, double[] ys)>)> LoadExcelMultiAsync(string filePath)
        {
            try
            {
                // Register encoding provider for Chinese characters
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                return await Task.Run(() =>
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    using (var reader = ExcelReaderFactory.CreateReader(stream))
                    {
                        var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                        {
                            ConfigureDataTable = (_) => new ExcelDataTableConfiguration
                            {
                                UseHeaderRow = true // Try to use the first row as header
                            }
                        });

                        if (result.Tables.Count == 0) throw new InvalidOperationException("No data tables found in Excel file.");

                        var dataTable = result.Tables[0]; // Use the first data table
                        var data = new List<List<double>>();
                        var columnNames = new List<string>();

                        // Get column names (either from header or generate default)
                        if (dataTable.Rows.Count > 0 && dataTable.Rows[0].ItemArray.All(item => item != null && !double.TryParse(item.ToString(), out _)))
                        {
                            // Use header row
                            foreach (DataColumn column in dataTable.Columns)
                            {
                                columnNames.Add(column.ColumnName);
                            }
                             // Start reading data from the second row if header is used
                            for (int row = 1; row < dataTable.Rows.Count; row++)
                            {
                                var rowData = new List<double>();
                                bool hasValidData = false;
                                for (int col = 0; col < dataTable.Columns.Count; col++)
                                {
                                    var value = dataTable.Rows[row][col];
                                    if (value != DBNull.Value && double.TryParse(value.ToString(), out double parsedValue))
                                    {
                                        rowData.Add(parsedValue);
                                        hasValidData = true;
                                    }
                                    else
                                    {
                                        rowData.Add(double.NaN); // Add NaN for invalid data
                                    }
                                }
                                // Only add the row if it contains at least one valid number
                                if (hasValidData && rowData.Count >= 2 && rowData.All(v => !double.IsNaN(v)))
                                {
                                     data.Add(rowData);
                                }
                            }
                            }
                            else
                        {
                            // No header row, generate default column names and read from the first row
                            for (int i = 0; i < dataTable.Columns.Count; i++)
                            {
                                columnNames.Add($"列{i + 1}");
                            }
                            for (int row = 0; row < dataTable.Rows.Count; row++)
                            {
                                var rowData = new List<double>();
                                bool hasValidData = false;
                                for (int col = 0; col < dataTable.Columns.Count; col++)
                                {
                                    var value = dataTable.Rows[row][col];
                                    if (value != DBNull.Value && double.TryParse(value.ToString(), out double parsedValue))
                                    {
                                        rowData.Add(parsedValue);
                                        hasValidData = true;
                                    }
                                    else
                                    {
                                        rowData.Add(double.NaN); // Add NaN for invalid data
                                    }
                                }
                                // Only add the row if it contains at least one valid number
                                 if (hasValidData && rowData.Count >= 2 && rowData.All(v => !double.IsNaN(v)))
                                {
                                     data.Add(rowData);
                                }
                            }
                        }

                        if (data.Count == 0)
                        {
                             AppendInfo($"No valid data found in Excel file: {filePath}");
                            return (Array.Empty<double>(), new List<(string, double[])>());
                        }

                        int colCount = data[0].Count;
                        int rowCount = data.Count;
                        double[] xs = new double[rowCount];
                        var yss = new List<(string, double[])>();

                         for (int col = 0; col < colCount; col++)
                        {
                            double[] ys = new double[rowCount];
                            for (int row = 0; row < rowCount; row++)
                            {
                                ys[row] = data[row][col];
                            }

                            // Use the first column as X values
                            if (col == 0)
                            {
                                xs = ys;
                            }
                            else // Use subsequent columns as Y values
                            {
                                 string label = col < columnNames.Count ? columnNames[col] : $"列{col + 1}";
                                 yss.Add((label, ys));
                            }
                        }

                         // If the first column was not treated as X or if there are no Y columns, return empty.
                         if (xs.Length == 0 || yss.Count == 0)
                         {
                             Console.WriteLine($"Invalid data structure in Excel file: {filePath}");
                             return (Array.Empty<double>(), new List<(string, double[])>());
                         }

                        return (xs, yss);
                    }
                }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading Excel file {filePath}: {ex.Message}");
                return (Array.Empty<double>(), new List<(string, double[])>());
            }
        }



        private void OnDragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                DropBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.LightGray);
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnDragLeave(object? sender, DragEventArgs e)
        {
            DropBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White);
            e.Handled = true;
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
                e.DragEffects = DragDropEffects.Copy;
            else
                e.DragEffects = DragDropEffects.None;
            e.Handled = true;
        }

        private async void OnDrop(object? sender, DragEventArgs e)
        {
            DropBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White);
            
            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles();
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        var path = file.Path.LocalPath;
                        await LoadAndPlotFile(path);
                    }
                }
            }
            e.Handled = true;
        }

        private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider != null)
            {
                 var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                 {
                     Title = "选择文件",
                     AllowMultiple = true,  // 修改为允许多选
                     FileTypeFilter = new[]
                     {
                         new FilePickerFileType("数据文件") { Patterns = new[] { "*.csv", "*.txt", "*.xlsx", "*.xls", "*.s2p", "*.s3p", "*.s4p" } }
                     }
                 });

                 if (files != null && files.Count > 0)
                 {
                     foreach (var file in files)
                     {
                         var path = file.Path.LocalPath;
                         await LoadAndPlotFile(path);
                     }
                 }
            }
        }

        private async void OnSaveImageClick(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider != null)
            {
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "保存图片",
                    DefaultExtension = "png",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("PNG 图片") { Patterns = new[] { "*.png" } }
                    }
                });

                if (file != null)
                {
                    int exportWidth = 1200;
                    int exportHeight = 800;
                    try
                    {
                        avaPlot.Plot.SavePng(file.Path.LocalPath, exportWidth, exportHeight);
                        AppendInfo($"[SaveImage] Successfully saved to {file.Path.LocalPath}");
                    }
                    catch (Exception ex)
                    {
                        AppendInfo($"[SaveImage] Error saving PNG: {ex.Message}");
                        // 可选：弹窗提示
                    }
                }
            }
        }

        // Centralized method to load and plot files
        private async Task LoadAndPlotFile(string path)
        {
            // 注入调试输出
            SParameterFileParser.AppendInfo = AppendInfo;
            bool hasDuplicate = false;
            int addedCount = 0;
            string fileName = Path.GetFileName(path);

            if (path.EndsWith(".s4p", StringComparison.OrdinalIgnoreCase) || 
                path.EndsWith(".s2p", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var sParamData = SParameterFileParser.ParseFile(path);
                    if (sParamData.Frequencies.Count > 0)
                    {
                        // 先清除所有曲线
                        avaPlot.Plot.Clear();
                        loadedPlottables.Clear();
                        curveInfos.Clear();
                        curveCounter = 0;

                        var parameters = sParamData.Magnitudes.Keys.ToList();
                        bool shouldLimitVisibility = parameters.Count > 5;
                        for (int i = 0; i < parameters.Count; i++)
                        {
                            var param = parameters[i];
                            if (sParamData.Magnitudes[param].Count > 0)
                            {
                                var newPlot = AddCurveToPlot(param, sParamData.Frequencies.ToArray(), sParamData.Magnitudes[param].ToArray(), fileName, !shouldLimitVisibility || i == 0);
                                if (newPlot == null)
                                {
                                    hasDuplicate = true;
                                    continue;
                                }
                                var curveInfo = curveInfos.Last();
                                curveInfo.SourceFileName = fileName;
                                addedCount++;
                            }
                        }
                        if (addedCount > 0)
                        {
                            // 设置Y轴范围，为S参数预留足够的空间（分位数缩放）
                            var allY = sParamData.Magnitudes.Values.SelectMany(arr => arr)
                                .Where(y => !double.IsNaN(y) && !double.IsInfinity(y) && Math.Abs(y) < 200)
                                .OrderBy(y => y).ToArray();
                            double minY, maxY;
                            if (allY.Length > 10)
                            {
                                minY = Percentile(allY, 0.1);
                                maxY = Percentile(allY, 99.9);
                            }
                            else if (allY.Length > 0)
                            {
                                minY = allY.Min();
                                maxY = allY.Max();
                            }
                            else
                            {
                                minY = -60;
                                maxY = 0;
                            }
                            double padding = (maxY - minY) * 0.05;
                            avaPlot.Plot.Axes.Left.Min = minY - padding;
                            avaPlot.Plot.Axes.Left.Max = maxY + padding;

                            // 设置X轴范围
                            double minX = sParamData.Frequencies.Min();
                            double maxX = sParamData.Frequencies.Max();
                            double xPadding = (maxX - minX) * 0.02; // 添加5%的边距
                            avaPlot.Plot.Axes.Bottom.Min = minX - xPadding;
                            avaPlot.Plot.Axes.Bottom.Max = maxX + xPadding;

                            // 设置坐标轴标签
                            avaPlot.Plot.Axes.Bottom.Label.Text = "Frequency (GHz)";
                            avaPlot.Plot.Axes.Left.Label.Text = "Magnitude (dB)";

                            avaPlot.Refresh();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendInfo($"[LoadAndPlotFile] Error loading S-parameter file: {ex.Message}");
                }
            }
            else if (path.EndsWith(".s3p", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var sParamData = SParameterFileParser.ParseFile(path);
                    if (sParamData.Frequencies.Count > 0 && sParamData.Magnitudes.ContainsKey("Ssd21"))
                    {
                        var ys = sParamData.Magnitudes["Ssd21"].ToArray();
                        var xs = sParamData.Frequencies.ToArray();
                        var newPlot = AddCurveToPlot("Ssd21", xs, ys, fileName, true);
                        if (newPlot != null)
                        {
                            avaPlot.Plot.Axes.AutoScale();
                            avaPlot.Refresh();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendInfo($"[LoadAndPlotFile] Error loading S3P file: {ex.Message}");
                }
            }
            else if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
             {
                 var (xs, yss) = await LoadCsvMultiAsync(path);
                 if (yss.Count > 0)
                 {
                    bool shouldLimitVisibility = yss.Count > 5;
                    for (int i = 0; i < yss.Count; i++)
                     {
                        var (label, ys) = yss[i];
                        var newPlot = AddCurveToPlot(label, xs, ys, fileName, !shouldLimitVisibility || i == 0);
                        if (newPlot == null)
                        {
                            hasDuplicate = true;
                            continue;
                        }
                        var curveInfo = curveInfos.Last();
                        curveInfo.SourceFileName = fileName;
                        addedCount++;
                     }
                    if (addedCount > 0)
                    {
                     avaPlot.Plot.Axes.AutoScale();
                     avaPlot.Refresh();
                    }
                 }
             }
             else if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
             {
                 var (xs, yss) = await LoadTxtMultiAsync(path);
                 if (yss.Count > 0)
                 {
                    bool shouldLimitVisibility = yss.Count > 5;
                    for (int i = 0; i < yss.Count; i++)
                     {
                        var (label, ys) = yss[i];
                        var newPlot = AddCurveToPlot(label, xs, ys, fileName, !shouldLimitVisibility || i == 0);
                        if (newPlot == null)
                        {
                            hasDuplicate = true;
                            continue;
                        }
                        var curveInfo = curveInfos.Last();
                        curveInfo.SourceFileName = fileName;
                        addedCount++;
                     }
                    if (addedCount > 0)
                    {
                     avaPlot.Plot.Axes.AutoScale();
                     avaPlot.Refresh();
                    }
                 }
             }
             else if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
             {
                 var (xs, yss) = await LoadExcelMultiAsync(path);
                 if (yss.Count > 0)
                 {
                    bool shouldLimitVisibility = yss.Count > 5;
                    for (int i = 0; i < yss.Count; i++)
                     {
                        var (label, ys) = yss[i];
                        var newPlot = AddCurveToPlot(label, xs, ys, fileName, !shouldLimitVisibility || i == 0);
                        if (newPlot == null)
                        {
                            hasDuplicate = true;
                            continue;
                        }
                        var curveInfo = curveInfos.Last();
                        curveInfo.SourceFileName = fileName;
                        addedCount++;
                     }
                    if (addedCount > 0)
                    {
                     avaPlot.Plot.Axes.AutoScale();
                     avaPlot.Refresh();
                 }
             }
        }

            if (hasDuplicate && addedCount == 0)
            {
                ShowDuplicateTip("未导入重复数据", Avalonia.Media.Brushes.Black);
            }
            else if (hasDuplicate && addedCount > 0)
            {
                ShowDuplicateTip($"部分数据已存在，已跳过重复数据", Avalonia.Media.Brushes.Black);
            }
        }

        // 简单提示面板显示方法
        private async void ShowDuplicateTip(string text, Avalonia.Media.IBrush? color = null)
        {
            var tipPanel = this.FindControl<Border>("DuplicateTipPanel");
            var tipText = this.FindControl<TextBlock>("DuplicateTipText");
            if (tipPanel != null && tipText != null)
            {
                tipText.Text = text;
                tipText.Foreground = color ?? Avalonia.Media.Brushes.Black;
                tipPanel.IsVisible = true;
                await Task.Delay(2000);
                tipPanel.IsVisible = false;
            }
        }

        // 输出调试信息到InfoTextBlock
        private void AppendInfo(string message)
        {
            try
            {
                var infoTextBlock = this.FindControl<TextBox>("InfoTextBlock");
                if (infoTextBlock != null)
                {
                    infoTextBlock.Text = $"{DateTime.Now:HH:mm:ss.fff} - {message}\n{infoTextBlock.Text}";
                }
            }
            catch (Exception)
            {
                // 如果InfoTextBlock不存在或发生其他错误，静默处理
            }
        }

        // 智能后缀：用分隔符将文件名拆分为词，取新文件名独有的词作为后缀
        private string GetSmartSuffix(string newFile, IEnumerable<string> existingFiles)
        {
            string newName = System.IO.Path.GetFileNameWithoutExtension(newFile);
            var newWords = newName.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in existingFiles)
            {
                var words = System.IO.Path.GetFileNameWithoutExtension(file).Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var w in words)
                    existingWords.Add(w);
            }
            var diffWords = newWords.Except(existingWords).ToList();
            if (diffWords.Count > 0)
                return string.Join("-", diffWords);
            return newName;
        }

        // 修改 AddCurveToPlot 方法，增加 sourceFileName 参数
        private ScottPlot.IPlottable? AddCurveToPlot(string label, double[] xs, double[] ys, string sourceFileName, bool isFirstCurveInFile = true)
        {
            // 检查是否已存在相同的曲线（数据完全相同）
            bool isDuplicate = curveInfos.Any(info =>
                info.Name == label &&
                info.Xs.Length == xs.Length &&
                info.Ys.Length == ys.Length &&
                info.Xs.SequenceEqual(xs) &&
                info.Ys.SequenceEqual(ys));

            if (isDuplicate)
            {
                return null;
            }

            // 检查名字冲突，自动加智能后缀
            string uniqueLabel = label;
            int suffixIndex = 1;
            var existingNames = curveInfos.Select(ci => ci.Name).ToHashSet();
            var existingFiles = curveInfos.Select(ci => ci.SourceFileName).ToList();
            string smartSuffix = GetSmartSuffix(sourceFileName, existingFiles);
            while (existingNames.Contains(uniqueLabel))
            {
                if (suffixIndex == 1)
                {
                    if (!string.IsNullOrWhiteSpace(smartSuffix) && !uniqueLabel.EndsWith("-" + smartSuffix))
                        uniqueLabel = $"{label}-{smartSuffix}";
                    else
                        uniqueLabel = $"{label}({System.IO.Path.GetFileNameWithoutExtension(sourceFileName)})";
                }
                else
                {
                    uniqueLabel = $"{label}-{smartSuffix}_{suffixIndex}";
                }
                suffixIndex++;
            }

            var colorInfo = colorPalette[curveCounter % colorPalette.Length];
            var customColor = ColorFromHex(colorInfo.hexColor);
            var curveInfo = new CurveInfo(AppendInfo, () => {
                avaPlot.Plot.Axes.AutoScale();
                avaPlot.Refresh();
            })
            {
                Name = uniqueLabel,
                PlotColor = customColor,
                Width = 3.0,
                Opacity = 1.0,
                Visible = isFirstCurveInFile, // 只有第一条曲线默认可见
                Xs = xs,
                Ys = ys,
                LineStyle = "实线"
            };
            curveInfo.GenerateHashId();
            curveInfo.PropertyChanged += CurveInfo_PropertyChanged;
            curveInfos.Add(curveInfo);
            curveInfo.SaveOriginalYs(); // 新增：保存原始Ys
            curveInfo.SourceFileName = sourceFileName;
            UpdateOtherCurves();
            (MoveCurveUpCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
            (MoveCurveDownCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
            // 只用Scatter
            var scatter = avaPlot.Plot.Add.Scatter(curveInfo.ModifiedXs, curveInfo.Ys);
            scatter.Color = customColor;
            scatter.LegendText = uniqueLabel;
            scatter.LineWidth = (float)curveInfo.Width;
            scatter.MarkerSize = 0;
            scatter.IsVisible = curveInfo.Visible; // 设置初始可见性
            loadedPlottables.Add(scatter);
            curveCounter++;
            // 确保图例显示并刷新
            avaPlot.Plot.ShowLegend();
            avaPlot.Refresh();
            return scatter;
        }

        private void RedrawAllCurves()
        {
            AppendInfo($"[RedrawAllCurves] curveInfos.Count={curveInfos.Count}");
            avaPlot.Plot.Clear();
            loadedPlottables.Clear();
            foreach (var info in curveInfos)
            {
                ScottPlot.IPlottable plot;
                // 只用Scatter
                var scatter = avaPlot.Plot.Add.Scatter(info.ModifiedXs, info.Ys);
                scatter.Color = info.PlotColor;
                scatter.LegendText = info.Name;
                scatter.LineWidth = (float)info.Width;
                scatter.MarkerSize = 0; // 不显示圆点
                plot = scatter;
                loadedPlottables.Add(plot);
            }
            avaPlot.Plot.ShowLegend();
            avaPlot.Refresh();
            (MoveCurveUpCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
            (MoveCurveDownCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
        }

        private void OnClearClick(object? sender, RoutedEventArgs e)
        {
            avaPlot.Plot.Clear();
            loadedPlottables.Clear();
            curveInfos.Clear();
            // curves.Clear();
            curveCounter = 0;
            UpdateOtherCurves();
            avaPlot.Plot.ShowLegend();
            avaPlot.Refresh();
            (MoveCurveUpCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
            (MoveCurveDownCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
        }

        private void OnColorSelect(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is string colorName)
            {
                if (CurveInfoList.SelectedItem is CurveInfo info)
                {
                    var colorInfo = colorPalette.FirstOrDefault(c => c.name == colorName);
                    if (colorInfo.name != null)
                    {
                        var customColor = ColorFromHex(colorInfo.hexColor);
                        info.PlotColor = customColor;
                        int idx = curveInfos.IndexOf(info);
                        if (idx >= 0 && idx < loadedPlottables.Count)
                        {
                            dynamic plotObj = loadedPlottables[idx];
                            try { plotObj.Color = customColor; } catch { }
                            avaPlot.Refresh();
                        }
                    }
                }
            }
        }



        private void OnInfoTextBoxSelectAll(object? sender, RoutedEventArgs e)
        {
            if (this.FindControl<TextBox>("InfoTextBlock") is TextBox infoBox)
                infoBox.SelectAll();
        }
        private void OnInfoTextBoxCopy(object? sender, RoutedEventArgs e)
        {
            if (this.FindControl<TextBox>("InfoTextBlock") is TextBox infoBox)
                infoBox.Copy();
        }

        private bool CanMoveCurveUp(CurveInfo? info)
        {
            if (info == null)
            {
                AppendInfo($"[CanMoveCurveUp] info is null");
                return false;
            }
            var hashId = info.HashId;
            var idx = curveInfos.ToList().FindIndex(ci => ci.HashId == hashId);
            bool result = idx > 0;
            AppendInfo($"[CanMoveCurveUp] idx={idx}, count={curveInfos.Count}, result={result}");
            return result;
        }
        private bool CanMoveCurveDown(CurveInfo? info)
        {
            if (info == null)
            {
                AppendInfo($"[CanMoveCurveDown] info is null");
                return false;
            }
            var hashId = info.HashId;
            var idx = curveInfos.ToList().FindIndex(ci => ci.HashId == hashId);
            bool result = idx >= 0 && idx < curveInfos.Count - 1;
            AppendInfo($"[CanMoveCurveDown] idx={idx}, count={curveInfos.Count}, result={result}");
            return result;
        }
        private void MoveCurveUp(CurveInfo? info)
        {
            if (info == null) { AppendInfo("[MoveCurveUp] info is null"); return; }
            var hashId = info.HashId;
            var idx = curveInfos.ToList().FindIndex(ci => ci.HashId == hashId);
            AppendInfo($"[MoveCurveUp] idx={idx}, count={curveInfos.Count}");
            if (idx > 0)
            {
                curveInfos.Move(idx, idx - 1);
                RedrawAllCurves();
                (MoveCurveUpCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
                (MoveCurveDownCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
            }
        }
        private void MoveCurveDown(CurveInfo? info)
        {
            if (info == null) { AppendInfo("[MoveCurveDown] info is null"); return; }
            var hashId = info.HashId;
            var idx = curveInfos.ToList().FindIndex(ci => ci.HashId == hashId);
            AppendInfo($"[MoveCurveDown] idx={idx}, count={curveInfos.Count}");
            if (idx >= 0 && idx < curveInfos.Count - 1)
            {
                curveInfos.Move(idx, idx + 1);
                RedrawAllCurves();
                (MoveCurveUpCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
                (MoveCurveDownCommand as RelayCommand<CurveInfo>)?.RaiseCanExecuteChanged();
            }
        }

        private void MoveCurve(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || newIndex < 0 || oldIndex >= curveInfos.Count || newIndex >= curveInfos.Count)
                return;
            curveInfos.Move(oldIndex, newIndex);
            RedrawAllCurves();
        }

        private void OnMoveCurveUpClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CurveInfo info)
            {
                int idx = curveInfos.IndexOf(info);
                if (idx > 0)
                    MoveCurve(idx, idx - 1);
            }
        }
        private void OnMoveCurveDownClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CurveInfo info)
            {
                int idx = curveInfos.IndexOf(info);
                if (idx < curveInfos.Count - 1)
                    MoveCurve(idx, idx + 1);
            }
        }

        private void OnDeleteCurveClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CurveInfo info)
            {
                var hashId = info.HashId;
                var idx = curveInfos.ToList().FindIndex(ci => ci.HashId == hashId);
                if (idx >= 0)
                {
                    // 从列表中移除
                    curveInfos.RemoveAt(idx);
                    UpdateOtherCurves();
                    // 从绘图中移除
                    if (idx < loadedPlottables.Count)
                    {
                        loadedPlottables.RemoveAt(idx);
                    }
                    // 重新绘制
                    RedrawAllCurves();
                }
            }
        }

        private void OnCurveColorBlockPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                Avalonia.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(border);
            }
        }

        private void OnAboutClick(object? sender, RoutedEventArgs e)
        {
            var aboutPanel = this.FindControl<Border>("AboutPanel");
            if (aboutPanel != null)
            {
                aboutPanel.IsVisible = true;
            }
        }

        private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var aboutPanel = this.FindControl<Border>("AboutPanel");
            if (aboutPanel != null && aboutPanel.IsVisible)
            {
                // 检查点击是否在面板内
                var point = e.GetPosition(aboutPanel);
                if (point.X < 0 || point.X > aboutPanel.Bounds.Width ||
                    point.Y < 0 || point.Y > aboutPanel.Bounds.Height)
                {
                    aboutPanel.IsVisible = false;
                }
            }
        }

        private void OnResetZoomClick(object? sender, RoutedEventArgs e)
        {
            avaPlot.Plot.Axes.AutoScale();
            avaPlot.Refresh();
        }

        // 添加公共属性以支持目标曲线选择
        public ObservableCollection<CurveInfo> CurveInfos => curveInfos;

        public new event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void OnToggleCurveListClick(object? sender, RoutedEventArgs e)
        {
            IsCurveListVisible = !IsCurveListVisible;

            if (plotRowDef == null || curveListRowDef == null)
                return;

            if (!IsCurveListVisible)
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

        private void UpdateOtherCurves()
        {
            foreach (var ci in curveInfos)
            {
                ci.OtherCurves.Clear();
                foreach (var other in curveInfos)
                {
                    if (!ReferenceEquals(other, ci))
                        ci.OtherCurves.Add(other);
                }
            }
        }

        private void UpdateOperationEnabledStates()
        {
            var selectedTargets = curveInfos
                .Where(ci => ci.OperationType == "减" && ci.TargetCurve != null)
                .Select(ci => ci.TargetCurve)
                .ToHashSet();

            foreach (var ci in curveInfos)
            {
                if (selectedTargets.Contains(ci))
                {
                    if (ci.OperationType != "无")
                    {
                        ci.OperationType = "无";
                        ci.UpdateCurveData();
                    }
                    ci.IsOperationEnabled = false;
                    if (ci.TargetCurve != null)
                        ci.TargetCurve = null;
                    ci.IsTargetCurveEnabled = false;
                }
                else
                {
                    ci.IsOperationEnabled = true;
                    ci.IsTargetCurveEnabled = true;
                }
            }
        }

        private static double Percentile(double[] sortedData, double percentile)
        {
            if (sortedData == null || sortedData.Length == 0) return 0;
            if (percentile <= 0) return sortedData.First();
            if (percentile >= 100) return sortedData.Last();
            double position = (sortedData.Length - 1) * percentile / 100.0;
            int left = (int)Math.Floor(position);
            int right = (int)Math.Ceiling(position);
            if (left == right) return sortedData[left];
            double leftValue = sortedData[left];
            double rightValue = sortedData[right];
            return leftValue + (rightValue - leftValue) * (position - left);
        }

        private void UpdateCurveListRowHeight()
        {
            if (curveListRowDef == null || this.Bounds.Height == 0)
                return;
            double windowHeight = this.Bounds.Height;
            double minHeight = windowHeight * 0.2;
            double maxHeight = windowHeight * 0.3;
            double rowHeight = 26; // 与CurveRowHeight一致
            double desiredHeight = curveInfos.Count * rowHeight + 30; // 30为表头高度
            double finalHeight = Math.Max(minHeight, Math.Min(desiredHeight, maxHeight));
            curveListRowDef.Height = new GridLength(finalHeight);
        }

        private void TryAutoDetectAxisLabels(string fileName, List<string> columnNames)
        {
            if (isXAxisLabelManuallyEdited && isYAxisLabelManuallyEdited)
                return;

            // 尝试从文件名中提取信息
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName).ToLower();
            if (fileNameWithoutExt.Contains("s2p") || fileNameWithoutExt.Contains("s3p"))
            {
                if (!isXAxisLabelManuallyEdited)
                {
                    XAxisLabel = "Frequency (Hz)";
                    isXAxisLabelManuallyEdited = false;
                }
                if (!isYAxisLabelManuallyEdited)
                {
                    YAxisLabel = "Magnitude (dB)";
                    isYAxisLabelManuallyEdited = false;
                }
                return;
            }

            // 尝试从列名中提取信息
            if (columnNames.Count >= 2)
            {
                var firstCol = columnNames[0].ToLower();
                var secondCol = columnNames[1].ToLower();

                // 检测X轴标签
                if (!isXAxisLabelManuallyEdited)
                {
                    if (firstCol.Contains("time") || firstCol.Contains("t"))
                        XAxisLabel = "Time";
                    else if (firstCol.Contains("freq") || firstCol.Contains("f"))
                        XAxisLabel = "Frequency (Hz)";
                    else if (firstCol.Contains("x"))
                        XAxisLabel = "X";
                    else
                        XAxisLabel = firstCol;
                }

                // 检测Y轴标签
                if (!isYAxisLabelManuallyEdited)
                {
                    if (secondCol.Contains("voltage") || secondCol.Contains("v"))
                        YAxisLabel = "Voltage (V)";
                    else if (secondCol.Contains("current") || secondCol.Contains("i"))
                        YAxisLabel = "Current (A)";
                    else if (secondCol.Contains("power") || secondCol.Contains("p"))
                        YAxisLabel = "Power (W)";
                    else if (secondCol.Contains("y"))
                        YAxisLabel = "Y";
                    else
                        YAxisLabel = secondCol;
                }
            }
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> execute;
        private readonly Func<T?, bool>? canExecute;
        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }
        public bool CanExecute(object? parameter) => canExecute == null || canExecute((T?)parameter);
        public void Execute(object? parameter) => execute((T?)parameter);
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class TargetCurveConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is ObservableCollection<CurveInfo> allCurves)
            {
                // 获取当前曲线
                CurveInfo? currentCurve = null;
                if (parameter is ListBoxItem listBoxItem && listBoxItem.DataContext is CurveInfo curveInfo1)
                {
                    currentCurve = curveInfo1;
                }
                else if (parameter is CurveInfo curveInfo2)
                {
                    currentCurve = curveInfo2;
                }

                if (currentCurve != null)
                {
                    DebugLog($"[TargetCurveConverter] Filtering curves, current curve: {currentCurve.Name}, HashId: {currentCurve.HashId}");
                    var filteredCurves = allCurves.Where(c => c.HashId != currentCurve.HashId).ToList();
                    DebugLog($"[TargetCurveConverter] Filtered curves count: {filteredCurves.Count}");
                    return filteredCurves;
                }
            }
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private void DebugLog(string message)
        {
            // 使用 Debug.WriteLine 替代 AppendInfo
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
} 