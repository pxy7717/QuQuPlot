using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Media;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;
using System.Windows.Input;
using System.Globalization;
using System.Threading.Tasks;
using System.Data;
using MediaColor = System.Windows.Media.Color;
using QuquPlot.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using ExcelDataReader;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32;
using ScottPlot;
using ScottPlot.WPF;
using Color = System.Windows.Media.Color;
using System.Windows.Documents;
using QuquPlot.Utils;

namespace QuquPlot
{
    public partial class MainWindow : Window
    {
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

        private double _lastSplitterPosition = 0.5; // 默认50%的宽度给设置面板

        private Point _dragStartPoint;
        private int _insertionIndex = -1;
        private AdornerLayer? _adornerLayer;
        private InsertionAdorner? _insertionAdorner;

        private DispatcherTimer? _colorBlockClickTimer;
        private bool _isColorBlockDrag = false;
        private Border? _pendingColorBlock = null;
        private MouseButtonEventArgs? _pendingColorBlockEventArgs = null;
        private ScottPlot.Plottables.Annotation mouseCoordLabel = null!;

        private ScottPlot.Plottables.Crosshair crosshair = null!;
        private bool enableCrosshair = false;
        private bool _isLoaded = false;

        // 实时数据流相关成员
        private Dictionary<string, (CurveInfo Info, ScottPlot.Plottables.Scatter Plot)> _realtimeCurveMap = new();
        private QuquPlot.Utils.RealTimeDataServer? _realTimeServer;

        public MainWindow()
        {
            InitializeComponent();
            
            // 添加轴标签文本框的事件处理
            XAxisLabelTextBox.TextChanged += AxisLabel_TextChanged;
            YAxisLabelTextBox.TextChanged += AxisLabel_TextChanged;
            XAxisMinTextBox.LostFocus += OnXAxisRangeChanged;
            XAxisMaxTextBox.LostFocus += OnXAxisRangeChanged;
            XAxisMinTextBox.KeyDown += OnXAxisRangeKeyDown;
            XAxisMaxTextBox.KeyDown += OnXAxisRangeKeyDown;
            YAxisMinTextBox.LostFocus += OnYAxisRangeChanged;
            YAxisMaxTextBox.LostFocus += OnYAxisRangeChanged;
            YAxisMinTextBox.KeyDown += OnYAxisRangeKeyDown;
            YAxisMaxTextBox.KeyDown += OnYAxisRangeKeyDown;
            AutoScaleYCheckBox.Checked += OnAutoScaleYChanged;
            AutoScaleYCheckBox.Unchecked += OnAutoScaleYChanged;
            this.DataContext = this;

            // 监听曲线可见性变化
            _curveInfos.CollectionChanged += CurveInfos_CollectionChanged;
            CurveListView.ItemsSource = _curveInfos;
            plotRowDef = (PlotView.Parent as Grid)?.RowDefinitions[0];
            curveListRowDef = (PlotView.Parent as Grid)?.RowDefinitions[2];

            // 1. 创建 FontStyler 实例
            var fontStyler = new ScottPlot.Stylers.FontStyler(PlotView!.Plot);

            // 2. 自动检测最佳字体（推荐，能自动适配中英文）
            fontStyler.Automatic();

            // 3. 或者手动指定字体
            // fontStyler.Set("DejaVu Sans");

            this.Loaded += (s, e) =>
            {
                _isLoaded = true;
                PlotView!.Plot.Clear();

                // 恢复坐标轴字体设置
                PlotUtils.SetAxisFonts(PlotView.Plot, 32, 28);

                // 设置表格四周的padding
                // 顺序是左右下上
                PixelPadding padding = new(150, 100, 150, 100);
                PlotView.Plot.Layout.Fixed(padding);

                // 设置（垂直于）x轴grid样式
                var xAxisGridStyle = new ScottPlot.GridStyle();
                xAxisGridStyle.IsVisible = true;
                xAxisGridStyle.MajorLineStyle.Width = 2;
                xAxisGridStyle.MajorLineStyle.Color = ColorUtils.ColorFromHex("#2B2B2B", 0.2);
                xAxisGridStyle.MinorLineStyle.Width = 0;
                xAxisGridStyle.MinorLineStyle.Color = ColorUtils.ColorFromHex("#2B2B2B", 0.5);
                xAxisGridStyle.IsBeneathPlottables = true;

                var yAxisGridStyle = new ScottPlot.GridStyle();
                yAxisGridStyle.IsVisible = true;
                yAxisGridStyle.MajorLineStyle.Width = 2;
                yAxisGridStyle.MajorLineStyle.Color = ColorUtils.ColorFromHex("#2B2B2B", 0.1);
                yAxisGridStyle.MinorLineStyle.Width = 0;
                yAxisGridStyle.MinorLineStyle.Color = ColorUtils.ColorFromHex("#2B2B2B", 0.5);
                yAxisGridStyle.IsBeneathPlottables = true;
                
                // 应用x轴grid样式
                PlotView.Plot.Grid.XAxisStyle = xAxisGridStyle;
                PlotView.Plot.Grid.YAxisStyle = yAxisGridStyle;

                // 恢复图例设置
                PlotUtils.SetLegendStyle(PlotView.Plot, 28, ColorUtils.ColorFromHex("#F2F2F2", 0.8));

                var uip = PlotView.UserInputProcessor;
                uip.DoubleLeftClickBenchmark(false);

                // 先移除右键右键缩放
                uip.RightClickDragZoom(false);
                
                // 将左键拖拽改为右键拖拽
                var panResponse = uip.UserActionResponses
                    .OfType<ScottPlot.Interactivity.UserActionResponses.MouseDragPan>()
                    .FirstOrDefault();

                if (panResponse != null)
                {
                    uip.UserActionResponses.Remove(panResponse);
                    // 例如修改 Button
                    var panButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
                    panResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragPan(panButton);
                    uip.UserActionResponses.Add(panResponse);
                }

                // 添加鼠标移动事件，用于刷新坐标
                PlotView.MouseMove += PlotView_MouseMove_ScottPlot;
                // 设置crosshair
                crosshair = PlotView.Plot.Add.Crosshair(0, 0);
                crosshair.IsVisible = enableCrosshair;
                crosshair.LineWidth = 1;
                crosshair.LineColor = ColorUtils.ColorFromHex("#8A8A8A", 0.8);
                
                // 设置图例

                PlotView.Plot.Legend.Alignment = ScottPlot.Alignment.LowerLeft;
                mouseCoordLabel = PlotView.Plot.Add.Annotation("", ScottPlot.Alignment.UpperLeft);
                mouseCoordLabel.LabelBorderWidth = 0;
                mouseCoordLabel.LabelBackgroundColor = ColorUtils.ColorFromHex("#FAFAFA", 0.0);
                mouseCoordLabel.LabelFontColor = ColorUtils.ColorFromHex("#9b9b9b", 1.0);
                mouseCoordLabel.LabelShadowColor = ColorUtils.ColorFromHex("#000000", 0.0);
                mouseCoordLabel.LabelFontSize = 35;
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

                // 启动实时数据服务器
                _realTimeServer = new QuquPlot.Utils.RealTimeDataServer(9000);
                _realTimeServer.DataReceived += (curveId, x, y) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!_realtimeCurveMap.TryGetValue(curveId, out var tuple))
                        {
                            // 新建 CurveInfo
                            var curveInfo = new CurveInfo(AppendDebugInfo, null, "长度：");
                            curveInfo.Name = curveId;
                            curveInfo.Xs = new double[] { x };
                            curveInfo.Ys = new double[] { y };
                            curveInfo.Visible = true;
                            curveInfo.isStreamData = true;
                            // 为streamdata生成唯一HashId
                            // curveInfo.HashId = Guid.NewGuid().ToString();
                            curveInfo.GenerateHashId(); // 兼容后续逻辑
                            _curveInfos.Add(curveInfo);
                            var scatter = PlotView.Plot.Add.Scatter(curveInfo.Xs, curveInfo.Ys);
                            scatter.LegendText = curveId;
                            scatter.LineWidth = (float)curveInfo.Width;
                            scatter.Color = ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity);
                            _realtimeCurveMap[curveInfo.HashId] = (curveInfo, scatter);
                            _curveMap[curveInfo.HashId] = (curveInfo, scatter);
                            AppendDebugInfo($"添加流数据曲线: {curveInfo.HashId}");
                        }
                        else
                        {
                            var (curveInfo, scatter) = tuple;
                            // 追加数据
                            curveInfo.Xs = curveInfo.Xs.Append(x).ToArray();
                            curveInfo.Ys = curveInfo.Ys.Append(y).ToArray();
                            // scatter.Update(curveInfo.Xs, curveInfo.Ys);
                            _realtimeCurveMap[curveInfo.HashId] = (curveInfo, scatter);
                            _curveMap[curveInfo.HashId] = (curveInfo, scatter);
                        }
                        PlotView.Refresh();
                    });
                };
                _realTimeServer.Start();
                UpdateXAxisInputState(); // 启动时刷新X轴范围显示
                UpdateYAxisInputState(); // 启动时刷新Y轴范围显示
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
            SaveCurvesConfigButton.Click += SaveCurvesConfigButton_Click;
            LoadCurvesConfigButton.Click += LoadCurvesConfigButton_Click;
            AutoScaleXCheckBox.Checked += OnAutoScaleXChanged;
            AutoScaleXCheckBox.Unchecked += OnAutoScaleXChanged;
            // UpdateXAxisInputState();
        }

        protected override void OnClosed(EventArgs e)
        {
            _realTimeServer?.Dispose();
            base.OnClosed(e);
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "数据文件|*.csv;*.xls;*.xlsx;*.txt;*.s;*.s2p;*.s3p;*.s4p|CSV文件|*.csv|Excel文件|*.xls;*.xlsx|文本文件|*.txt|S参数文件|*.s;*.s2p;*.s3p;*.s4p|所有文件|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    foreach (var filePath in openFileDialog.FileNames)
                    {
                        ProcessFile(filePath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"打开文件时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ProcessFile(string filePath)
        {
            string fileExtension = FileUtils.GetFileExtension(filePath);
            List<string> processedLines;

            switch (fileExtension)
            {
                case ".csv":
                    processedLines = FileUtils.ProcessCsvFile(filePath, AppendDebugInfo);
                    break;
                case ".xls":
                case ".xlsx":
                    processedLines = FileUtils.ProcessExcelFile(filePath, AppendDebugInfo);
                    break;
                case ".txt":
                    processedLines = FileUtils.ProcessTxtFile(filePath, AppendDebugInfo);
                    break;
                case ".s":
                case ".s2p":
                case ".s3p":
                case ".s4p":
                    // S参数文件专用解析
                    try
                    {
                        var sparam = QuquPlot.Models.SParameterFileParser.ParseFile(filePath);
                        AppendDebugInfo($"S参数文件解析成功，频点数: {sparam.Frequencies.Count}");
                        foreach (var kv in sparam.Magnitudes)
                        {
                            var ys = kv.Value.ToArray();
                            var xs = sparam.Frequencies.ToArray();
                            var label = kv.Key;
                            AddCurveToPlot(label, xs, ys, filePath, true);
                        }
                        // 自动缩放Y轴（分位数）
                        var allY = sparam.Magnitudes.Values.SelectMany(arr => arr)
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
                        PlotView.Plot.Axes.SetLimitsY(minY - padding, maxY + padding, PlotView.Plot.Axes.Left);
                        // 设置X轴范围
                        double minX = sparam.Frequencies.Min();
                        double maxX = sparam.Frequencies.Max();
                        double xPadding = (maxX - minX) * 0.02;
                        PlotView.Plot.Axes.SetLimitsX(minX - xPadding, maxX + xPadding, PlotView.Plot.Axes.Bottom);
                        PlotView.Refresh();
                        return;
                    }
                    catch (Exception ex)
                    {
                        AppendDebugInfo($"S参数文件解析失败: {ex.Message}");
                        return;
                    }
                default:
                    AppendDebugInfo($"不支持的文件类型: {fileExtension}");
                    return;
            }

            if (processedLines.Count > 0)
            {
                FileUtils.ProcessDataLines(
                    processedLines, 
                    filePath,
                    AppendDebugInfo,
                    AddCurveToPlot,
                    UpdateAxisLabels
                );
            }
        }

        private void UpdateAxisLabels(string xLabel, string yLabel)
        {
            if (XAxisLabelTextBox.Text == "X" && !string.IsNullOrEmpty(xLabel)) 
                XAxisLabelTextBox.Text = xLabel;
            if (YAxisLabelTextBox.Text == "Y" && !string.IsNullOrEmpty(yLabel)) 
                YAxisLabelTextBox.Text = yLabel;
        }

        private void SaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PNG图片|*.png|所有文件|*.*",
                DefaultExt = ".png"
            };

            // 获取用户设置的分辨率
            if (!int.TryParse(ImageWidthTextBox.Text, out int width) || width <= 0)
            {
                MessageBox.Show("请输入有效的宽度值（大于0的整数）", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                ImageWidthTextBox.Focus();
                return;
            }

            if (!int.TryParse(ImageHeightTextBox.Text, out int height) || height <= 0)
            {
                MessageBox.Show("请输入有效的高度值（大于0的整数）", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                ImageHeightTextBox.Focus();
                return;
            }

            // 临时禁用crosshair和mouseCoordLabel
            crosshair.IsVisible = false;
            mouseCoordLabel.IsVisible = false;

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    
                    PlotView.Plot.SavePng(dialog.FileName, width, height);
                    AppendDebugInfo($"图片已保存: {dialog.FileName}, 分辨率: {width}x{height}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存图片时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendDebugInfo($"保存图片失败: {ex.Message}");
                }
            }

            // 恢复crosshair和mouseCoordLabel
            crosshair.IsVisible = enableCrosshair;
            mouseCoordLabel.IsVisible = true;
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
            PlotUtils.SetAxisLabels(PlotView.Plot, XAxisLabelTextBox.Text, YAxisLabelTextBox.Text);
            
            // 刷新显示
            PlotView.Refresh();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            AppendDebugInfo("清除所有曲线");
            
            // 1. 先移除所有曲线，但保留 crosshair 和 mouseCoordLabel
            // 先移除所有 Scatter（或你自定义的曲线类型）
            var toRemove = PlotView.Plot.GetPlottables()
                .Where(p => p != crosshair && p != mouseCoordLabel)
                .ToList();
            foreach (var p in toRemove)
                PlotView.Plot.Remove(p);
            
            _curveMap.Clear();
            _curveInfos.Clear();
            // 不再调用UpdateOtherCurves
            if (AutoScaleXCheckBox.IsChecked == true)
                PlotView.Plot.Axes.AutoScaleX();
            if (AutoScaleYCheckBox.IsChecked == true)
                PlotView.Plot.Axes.AutoScaleY();
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
                if (AutoScaleXCheckBox.IsChecked == true)
                    PlotView.Plot.Axes.AutoScaleX();
                if (AutoScaleYCheckBox.IsChecked == true)
                    PlotView.Plot.Axes.AutoScaleY();
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
            if (sender is not CurveInfo curveInfo || e.PropertyName == null)
                return;

            int idx = _curveInfos.IndexOf(curveInfo);
            if (idx < 0 || idx >= _curveMap.Count)
                return;

            if (_curveMap.Values.ElementAt(idx).Plot is not ScottPlot.Plottables.Scatter scatter)
                return;
            
            bool needRefresh = false;
            switch (e.PropertyName)
            {
                case nameof(CurveInfo.Width):
                    if (curveInfo.Width != scatter.LineWidth)
                    {
                        scatter.LineWidth = (float)curveInfo.Width;
                        AppendDebugInfo($"更新线宽: {curveInfo.Width}");
                        needRefresh = true;
                    }
                    break;
                case nameof(CurveInfo.Name):
                    scatter.LegendText = curveInfo.Name;
                    AppendDebugInfo($"更新名称: {curveInfo.Name}");
                    needRefresh = true;
                    break;
                case nameof(CurveInfo.Visible):
                    scatter.IsVisible = curveInfo.Visible;
                    AppendDebugInfo($"更新可见性: {curveInfo.Visible}");
                    // 在可见性改变后重新调整坐标轴
                    PlotView.Plot.Axes.AutoScale();
                    needRefresh = true;
                    break;
                case nameof(CurveInfo.LineStyle):
                    var pattern = curveInfo.GetLinePattern();
                    scatter.LinePattern = pattern;
                    AppendDebugInfo($"更新线型: {curveInfo.LineStyle} -> {pattern}");
                    needRefresh = true;
                    break;
                case nameof(CurveInfo.Opacity):
                    if (curveInfo.Opacity != scatter.Color.A)
                    {
                        scatter.Color = ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity);
                        AppendDebugInfo($"更新透明度: {curveInfo.Opacity}");
                        needRefresh = true;
                    }
                    break;
                case nameof(CurveInfo.PlotColor):
                    scatter.Color = ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity);
                    AppendDebugInfo($"更新颜色: R={curveInfo.PlotColor.R}, G={curveInfo.PlotColor.G}, B={curveInfo.PlotColor.B}, 透明度={curveInfo.Opacity}");
                    needRefresh = true;
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
                    needRefresh = true;
                    break;
                case nameof(CurveInfo.Ys):
                    AppendDebugInfo($"更新Ys: {curveInfo.Name}");
                    needRefresh = true;
                    break;
                case nameof(CurveInfo.XMagnitude):
                    AppendDebugInfo($"更新X缩放: {curveInfo.XMagnitude}");
                    needRefresh = true;
                    break;
                case nameof(CurveInfo.ReverseX):
                    AppendDebugInfo($"反转X顺序: {curveInfo.ReverseX}");
                    needRefresh = true;
                    break;
                case nameof(CurveInfo.Smooth):
                    AppendDebugInfo($"更新平滑: {curveInfo.Smooth}");
                    needRefresh = true;
                    break;
            }

            if (needRefresh)
                RedrawCurve(curveInfo);
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
            foreach (var colorHex in ColorUtils.ColorPalette)
            {
                var brushConverter = new BrushConverter();
                var brush = brushConverter.ConvertFrom(colorHex);
                if (brush is Brush backgroundBrush)
                {
                    var btn = new Button
                    {
                        Background = backgroundBrush,
                        Tag = colorHex,
                        Width = ColorUtils.ColorBlockSize,
                        Height = ColorUtils.ColorBlockSize,
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
            // 注册全局鼠标事件，点击外部时关闭Popup
            Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(this, OnMouseDownOutsideColorPicker);
            Mouse.Capture(ColorPickerPopup.Child, CaptureMode.SubTree);
        }

        private void OnMouseDownOutsideColorPicker(object sender, MouseButtonEventArgs e)
        {
            ColorPickerPopup.IsOpen = false;
            Mouse.RemovePreviewMouseDownOutsideCapturedElementHandler(this, OnMouseDownOutsideColorPicker);
            Mouse.Capture(null);
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
                                scatter.Color = ColorUtils.ToScottPlotColor(color, info.Opacity);
                                PlotView.Refresh();
                            }
                        }
                        ColorPickerPopup.IsOpen = false;
                        // 注销全局鼠标事件
                        Mouse.RemovePreviewMouseDownOutsideCapturedElementHandler(this, OnMouseDownOutsideColorPicker);
                        Mouse.Capture(null);
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
            var scatter = PlotUtils.DrawCurve(PlotView.Plot, curveInfo, AppendDebugInfo);
            _curveMap[curveInfo.HashId] = (curveInfo, scatter); // 维护映射
            return scatter;
        }

        private void RedrawCurve(CurveInfo curveInfo)
        {
            // 单独绘制会导致legend顺序发生变化，放到最后，所以暂时还是直接全部重绘
           RedrawAllCurves();
        }

        private void RedrawAllCurves()
        {
            PlotView.Plot.Clear();
            _curveMap.Clear();
            foreach (var curveInfo in _curveInfos)
            {
                DrawCurve(curveInfo);
            }
            if (AutoScaleXCheckBox.IsChecked == true)
                PlotView.Plot.Axes.AutoScaleX();
            if (AutoScaleYCheckBox.IsChecked == true)
                PlotView.Plot.Axes.AutoScaleY();
            PlotView.Plot.MoveToTop(crosshair);
            PlotView.Refresh();
            UpdateXAxisInputState();
            UpdateYAxisInputState(); // 重绘all时同步Y轴输入框
            AppendDebugInfo("重绘all完成");
        }

        private void ClearDebugButton_Click(object sender, RoutedEventArgs e)
        {
            DebugUtils.ClearDebug(DebugTextBox);
        }

        private void CopyDebugButton_Click(object sender, RoutedEventArgs e)
        {
            DebugUtils.CopyDebug(DebugTextBox);
        }

        private void AppendDebugInfo(string message)
        {
            DebugUtils.AppendDebugInfo(DebugTextBox, message);
        }

        private ScottPlot.Color GetNextColor()
        {
            var usedColors = _curveInfos.Select(c => c.PlotColor).ToList();
            return PlotUtils.GetNextColor(usedColors);
        }

        private ScottPlot.IPlottable? AddCurveToPlot(string label, double[] xs, double[] ys, string sourceFileName, bool isFirstCurveInFile)
        {
            if (xs == null || ys == null || xs.Length != ys.Length)
            {
                AppendDebugInfo($"数据点数量不匹配或数据为空: X={xs?.Length ?? 0}, Y={ys?.Length ?? 0}");
                return null;
            }

            AppendDebugInfo($"AddCurveToPlot: {label}, 数据点: {xs.Length}");

            var lengthLabel = TryFindResource("LengthLabel") as string ?? "长度：";
            var curveInfo = new CurveInfo(AppendDebugInfo, null, lengthLabel);
            curveInfo.Name = label ?? "未命名曲线";
            curveInfo.Xs = xs;
            curveInfo.Ys = ys;
            curveInfo.Visible = isFirstCurveInFile;
            curveInfo.SourceFileFullPath = sourceFileName;
            curveInfo.SourceFileName = System.IO.Path.GetFileName(sourceFileName);
            curveInfo.GenerateHashId();

            // 检查是否已存在相同名称的曲线
            if (_curveInfos.Any(c => c.HashId == curveInfo.HashId))
            {
                AppendDebugInfo($"曲线 {label ?? "未命名曲线"} 已存在，跳过");
                ShowDuplicateCurveToast();
                return null;
            }

            // 设置曲线颜色
            var nextColor = GetNextColor();
            curveInfo.PlotColor = System.Windows.Media.Color.FromRgb(nextColor.R, nextColor.G, nextColor.B);
            // 确保UI更新颜色方块
            curveInfo.OnPropertyChanged(nameof(curveInfo.Brush));

            // 检查名字冲突，自动加智能后缀
            string uniqueLabel = curveInfo.Name;
            int suffixIndex = 1;
            var existingNames = _curveInfos.Select(ci => ci.Name).ToHashSet();
            var existingFiles = _curveInfos.Select(ci => ci.SourceFileName).ToList();
            string smartSuffix = FileUtils.GetSmartSuffix(sourceFileName, existingFiles);
            
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

            var scatter = PlotView.Plot.Add.Scatter(xs, ys);
            if (scatter != null)
            {
                scatter.Color = ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity);
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

                // 确保所有曲线的OtherCurves都正确更新
                UpdateOtherCurvesIncremental(curveInfo);
                
                AppendDebugInfo($"当前曲线数量: {_curveInfos.Count}, 映射数量: {_curveMap.Count}");
                
                // 刷新显示
                PlotView.Plot.MoveToTop(crosshair);
                if (AutoScaleXCheckBox.IsChecked == true)
                    PlotView.Plot.Axes.AutoScaleX();
                if (AutoScaleYCheckBox.IsChecked == true)
                    PlotView.Plot.Axes.AutoScaleY();
                PlotView.Refresh();
                UpdateXAxisInputState();
                UpdateYAxisInputState(); // 添加曲线时同步Y轴输入框
                
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

            foreach (var ci in _curveInfos)
            {
                // 检查当前选择的目标曲线是否数据点数量匹配
                if (ci.TargetCurve != null && ci.Xs.Length != ci.TargetCurve.Xs.Length)
                {
                    AppendDebugInfo($"目标曲线数据点数量不匹配，清除选择: {ci.TargetCurve.Name} ({ci.TargetCurve.Xs.Length} vs {ci.Xs.Length})");
                    ci.TargetCurve = null;
                    if (ci.OperationType != "无")
                    {
                        ci.OperationType = "无";
                        ci.UpdateCurveData();
                    }
                }
                
                if (selectedTargets.Contains(ci))
                {
                    if (ci.OperationType != "无")
                    {
                        ci.OperationType = "无";
                        ci.UpdateCurveData();
                    }
                    ci.IsOperationEnabled = false;
                    if (ci.TargetCurve != null)
                    {
                        ci.TargetCurve = null;
                    }
                    ci.IsTargetCurveEnabled = false;
                }
                else
                {
                    ci.IsOperationEnabled = true;
                    ci.IsTargetCurveEnabled = true;
                }
            }
            
            AppendDebugInfo("操作状态更新完成");
        }

        private void UpdateOtherCurves()
        {
            // 减少debug输出，只在关键节点输出
            AppendDebugInfo($"开始更新其他曲线列表，曲线总数: {_curveInfos.Count}");
            
            // 按数据点数量分组，避免重复计算
            var curvesByDataPointCount = _curveInfos.GroupBy(ci => ci.Xs.Length).ToDictionary(g => g.Key, g => g.ToList());
            
            foreach (var ci in _curveInfos)
            {
                ci.OtherCurves.Clear();
                
                // 只查找具有相同数据点数量的曲线
                if (curvesByDataPointCount.TryGetValue(ci.Xs.Length, out var compatibleCurves))
                {
                    foreach (var other in compatibleCurves)
                    {
                        if (other.HashId != ci.HashId)
                        {
                            ci.OtherCurves.Add(other);
                        }
                    }
                }
            }
            
            AppendDebugInfo("其他曲线列表更新完成");
        }

        /// <summary>
        /// 增量更新其他曲线列表，只处理新添加的曲线
        /// </summary>
        /// <param name="newCurve">新添加的曲线</param>
        private void UpdateOtherCurvesIncremental(CurveInfo newCurve)
        {
            // 为新曲线添加其他曲线
            newCurve.OtherCurves.Clear();
            foreach (var existing in _curveInfos)
            {
                if (existing.HashId != newCurve.HashId && existing.Xs.Length == newCurve.Xs.Length)
                {
                    newCurve.OtherCurves.Add(existing);
                }
            }
            
            // 为现有曲线添加新曲线
            foreach (var existing in _curveInfos)
            {
                if (existing.HashId != newCurve.HashId && existing.Xs.Length == newCurve.Xs.Length)
                {
                    existing.OtherCurves.Add(newCurve);
                }
            }
        }

        private void CurveListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void CurveListView_MouseMove(object sender, MouseEventArgs e)
        {
            // 如果拖动的是Slider，不处理
            DependencyObject? original = e.OriginalSource as DependencyObject;
            while (original != null)
            {
                if (original is Slider)
                    return;
                original = VisualTreeHelper.GetParent(original);
            }

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
                        ProcessFile(filePath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"拖拽文件时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            e.Handled = true;
        }

        private void PlotView_MouseMove_ScottPlot(object sender, MouseEventArgs e)
        {

            // 获取鼠标在图表坐标系中的位置  
            Point p = e.GetPosition(PlotView);
            ScottPlot.Pixel mousePixel = new(p.X * PlotView.DisplayScale, p.Y * PlotView.DisplayScale);
            ScottPlot.Coordinates mouseCoordinates = PlotView.Plot.GetCoordinates(mousePixel);
            crosshair.Position = mouseCoordinates;


            mouseCoordLabel.Text = $"({FormatNumber(mouseCoordinates.X)}, {FormatNumber(mouseCoordinates.Y)})";
            PlotView.Refresh();
        }

        private string FormatNumber(double value)
        {
            if (Math.Abs(value) < 0.01 || Math.Abs(value) > 100)
                return value.ToString("0.00E+0"); // 科学计数法，两位小数
            else
                return value.ToString("0.00");    // 普通，两位小数（去掉多余的0）
        }

        private void OnXMagnitudePreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // 允许负号和数字
            string text = e.Text;
            if (text == "-")
            {
                // 只允许第一个字符为负号
                if (sender is TextBox tb && tb.SelectionStart == 0 && !tb.Text.Contains("-"))
                {
                    e.Handled = false;
                    return;
                }
                else
                {
                    e.Handled = true;
                    return;
                }
            }
            // 只允许数字
            foreach (char c in text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
            e.Handled = false;
        }

        private void OnImageResolutionPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // 只允许数字
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
            e.Handled = false;
        }

        private void LegendPositionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (PlotView?.Plot == null || LegendPositionComboBox.SelectedItem == null) return;

            var selectedItem = LegendPositionComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem?.Content == null) return;

            ScottPlot.Alignment alignment = selectedItem.Content.ToString() switch
            {
                "左下" or "Lower Left" => ScottPlot.Alignment.LowerLeft,
                "左上" or "Upper Left" => ScottPlot.Alignment.UpperLeft,
                "右下" or "Lower Right" => ScottPlot.Alignment.LowerRight,
                "右上" or "Upper Right" => ScottPlot.Alignment.UpperRight,
                _ => ScottPlot.Alignment.LowerLeft
            };

            PlotView.Plot.Legend.Alignment = alignment;
            PlotView.Refresh();
            AppendDebugInfo($"图例位置已更改为: {selectedItem.Content}");
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

        private void CurveListView_DragLeave(object sender, DragEventArgs e)
        {
            RemoveInsertionAdorner();
            _insertionIndex = -1;
        }

        private void OnXAxisRangeChanged(object? sender, RoutedEventArgs e)
        {
            if (double.TryParse(XAxisMinTextBox.Text, out double min) && double.TryParse(XAxisMaxTextBox.Text, out double max) && min < max)
            {
                PlotView.Plot.Axes.SetLimitsX(min, max);
                PlotView.Refresh();
            }
        }
        private void OnXAxisRangeKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnXAxisRangeChanged(sender, e);
            }
        }

        private void OnAutoScaleXChanged(object? sender, RoutedEventArgs e)
        {
            UpdateXAxisInputState();
            if (AutoScaleXCheckBox.IsChecked == true)
            {
                PlotView.Plot.Axes.AutoScaleX();
                PlotView.Refresh();
                UpdateXAxisInputState();
            }
            else
            {
                OnXAxisRangeChanged(null, new RoutedEventArgs());
            }
        }
        private void UpdateXAxisInputState()
        {
            bool auto = AutoScaleXCheckBox.IsChecked == true;
            XAxisMinTextBox.IsEnabled = !auto;
            XAxisMaxTextBox.IsEnabled = !auto;
            XAxisMinTextBox.IsReadOnly = auto;
            XAxisMaxTextBox.IsReadOnly = auto;
            if (auto)
            {
                // 获取当前自动缩放的X轴范围并填入
                var xAxis = PlotView.Plot.Axes.Bottom;
                XAxisMinTextBox.Text = xAxis.Min.ToString("G4");
                XAxisMaxTextBox.Text = xAxis.Max.ToString("G4");
            }
        }

        private void OnYAxisRangeChanged(object? sender, RoutedEventArgs e)
        {
            if (double.TryParse(YAxisMinTextBox.Text, out double min) && double.TryParse(YAxisMaxTextBox.Text, out double max) && min < max)
            {
                PlotView.Plot.Axes.SetLimitsY(min, max, PlotView.Plot.Axes.Left);
                PlotView.Refresh();
            }
        }
        private void OnYAxisRangeKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnYAxisRangeChanged(sender, e);
            }
        }

        private void OnAutoScaleYChanged(object? sender, RoutedEventArgs e)
        {
            UpdateYAxisInputState();
            if (AutoScaleYCheckBox.IsChecked == true)
            {
                PlotView.Plot.Axes.AutoScaleY();
                PlotView.Refresh();
                UpdateYAxisInputState();
            }
            else
            {
                OnYAxisRangeChanged(null, new RoutedEventArgs());
            }
        }
        private void UpdateYAxisInputState()
        {
            bool auto = AutoScaleYCheckBox.IsChecked == true;
            YAxisMinTextBox.IsEnabled = !auto;
            YAxisMaxTextBox.IsEnabled = !auto;
            YAxisMinTextBox.IsReadOnly = auto;
            YAxisMaxTextBox.IsReadOnly = auto;
            if (auto)
            {
                var yAxis = PlotView.Plot.Axes.Left;
                YAxisMinTextBox.Text = yAxis.Min.ToString("G4");
                YAxisMaxTextBox.Text = yAxis.Max.ToString("G4");
            }
        }

        private void SaveCurvesConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "曲线配置文件|*.json|所有文件|*.*",
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var appSettings = new AppSettingsSerializable
                    {
                        XAxisLabel = XAxisLabelTextBox.Text,
                        YAxisLabel = YAxisLabelTextBox.Text,
                        ImageWidth = int.TryParse(ImageWidthTextBox.Text, out var w) ? w : 1200,
                        ImageHeight = int.TryParse(ImageHeightTextBox.Text, out var h) ? h : 800,
                        LegendPosition = LegendPositionComboBox.SelectedIndex,
                        XAxisMin = XAxisMinTextBox.Text,
                        XAxisMax = XAxisMaxTextBox.Text,
                        AutoScaleX = AutoScaleXCheckBox.IsChecked == true,
                        YAxisMin = YAxisMinTextBox.Text,
                        YAxisMax = YAxisMaxTextBox.Text,
                        AutoScaleY = AutoScaleYCheckBox.IsChecked == true
                    };
                    var projectConfig = new ProjectConfigSerializable
                    {
                        AppSettings = appSettings,
                        Curves = _curveInfos.Select(ci => new CurveConfigSerializable(ci)).ToList()
                    };
                    var json = System.Text.Json.JsonSerializer.Serialize(projectConfig, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(dialog.FileName, json);
                    AppendDebugInfo($"曲线配置已保存: {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存曲线配置时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendDebugInfo($"保存曲线配置失败: {ex.Message}");
                }
            }
        }

        private void LoadCurvesConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "曲线配置文件|*.json|所有文件|*.*",
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string json = System.IO.File.ReadAllText(dialog.FileName);
                    var projectConfig = System.Text.Json.JsonSerializer.Deserialize<ProjectConfigSerializable>(json);
                    if (projectConfig == null)
                    {
                        AppendDebugInfo($"配置文件无效或为空: {dialog.FileName}");
                        return;
                    }
                    // 恢复AppSettings
                    var appSettings = projectConfig.AppSettings;
                    if (appSettings != null)
                    {
                        XAxisLabelTextBox.Text = appSettings.XAxisLabel;
                        YAxisLabelTextBox.Text = appSettings.YAxisLabel;
                        ImageWidthTextBox.Text = appSettings.ImageWidth.ToString();
                        ImageHeightTextBox.Text = appSettings.ImageHeight.ToString();
                        if (appSettings.LegendPosition >= 0 && appSettings.LegendPosition < LegendPositionComboBox.Items.Count)
                            LegendPositionComboBox.SelectedIndex = appSettings.LegendPosition;
                        XAxisMinTextBox.Text = (appSettings.XAxisMin ?? string.Empty).ToString();
                        XAxisMaxTextBox.Text = (appSettings.XAxisMax ?? string.Empty).ToString();
                        // 自动应用X轴范围
                        OnXAxisRangeChanged(null, new RoutedEventArgs());
                        AutoScaleXCheckBox.IsChecked = appSettings.AutoScaleX;
                        UpdateXAxisInputState();
                        YAxisMinTextBox.Text = (appSettings.YAxisMin ?? string.Empty).ToString();
                        YAxisMaxTextBox.Text = (appSettings.YAxisMax ?? string.Empty).ToString();
                        AutoScaleYCheckBox.IsChecked = appSettings.AutoScaleY;
                        UpdateYAxisInputState();
                    }
                    // 恢复曲线
                    var curveConfigs = projectConfig.Curves;
                    if (curveConfigs == null)
                    {
                        AppendDebugInfo($"配置文件无曲线: {dialog.FileName}");
                        return;
                    }
                    int restored = 0, failed = 0;
                    foreach (var cfg in curveConfigs)
                    {
                        string dataPath = string.IsNullOrWhiteSpace(cfg.SourceFileFullPath) ? cfg.SourceFileName : cfg.SourceFileFullPath;
                        if (string.IsNullOrWhiteSpace(dataPath) || !System.IO.File.Exists(dataPath))
                        {
                            AppendDebugInfo($"找不到数据文件: {dataPath}");
                            failed++;
                            continue;
                        }
                        try
                        {
                            // 复用已有的文件处理逻辑
                            // 只加载数据，不添加到_plot，只生成xs,ys
                            string ext = QuquPlot.Utils.FileUtils.GetFileExtension(dataPath);
                            List<string> processedLines;
                            switch (ext)
                            {
                                case ".csv":
                                    processedLines = QuquPlot.Utils.FileUtils.ProcessCsvFile(dataPath, AppendDebugInfo);
                                    break;
                                case ".xls":
                                case ".xlsx":
                                    processedLines = QuquPlot.Utils.FileUtils.ProcessExcelFile(dataPath, AppendDebugInfo);
                                    break;
                                case ".txt":
                                    processedLines = QuquPlot.Utils.FileUtils.ProcessTxtFile(dataPath, AppendDebugInfo);
                                    break;
                                case ".s":
                                case ".s2p":
                                case ".s3p":
                                case ".s4p":
                                    // S参数文件暂不支持批量恢复
                                    AppendDebugInfo($"暂不支持S参数文件批量恢复: {dataPath}");
                                    failed++;
                                    continue;
                                default:
                                    AppendDebugInfo($"不支持的文件类型: {ext}");
                                    failed++;
                                    continue;
                            }
                            if (processedLines.Count > 0)
                            {
                                // 只取第一个曲线
                                QuquPlot.Utils.FileUtils.ProcessDataLines(
                                    processedLines,
                                    System.IO.Path.GetFileName(dataPath),
                                    AppendDebugInfo,
                                    (label, xs, ys, src, isFirst) =>
                                    {
                                        // 恢复设置
                                        var ci = new CurveInfo(AppendDebugInfo, null, "长度：");
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
                                        var plot = AddCurveToPlot(ci.Name, ci.Xs, ci.Ys, ci.SourceFileFullPath, true);
                                        if (plot != null)
                                        {
                                            var added = _curveInfos.LastOrDefault();
                                            if (added != null)
                                            {
                                                added.Visible = ci.Visible;
                                                added.Width = ci.Width;
                                                added.Opacity = ci.Opacity;
                                                added.LineStyle = ci.LineStyle;
                                                added.PlotColor = ci.PlotColor;
                                                added.MarkerSize = ci.MarkerSize;
                                                added.XMagnitude = ci.XMagnitude;
                                                added.ReverseX = ci.ReverseX;
                                                added.Smooth = ci.Smooth;
                                                RedrawCurve(added);
                                            }
                                        }
                                        restored++;
                                        return null;
                                    },
                                    (xLabel, yLabel) => { }
                                );
                            }
                            else
                            {
                                AppendDebugInfo($"数据文件无有效数据: {dataPath}");
                                failed++;
                            }
                        }
                        catch (Exception ex2)
                        {
                            AppendDebugInfo($"恢复曲线失败: {cfg.Name}, 错误: {ex2.Message}");
                            failed++;
                        }
                    }
                    AppendDebugInfo($"曲线配置恢复完成，成功: {restored}，失败: {failed}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"读取曲线配置时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendDebugInfo($"读取曲线配置失败: {ex.Message}");
                }
            }
        }

        // 用于序列化CurveInfo的必要属性
        private class CurveConfigSerializable
        {
            public string Name { get; set; } = null!;
            public bool Visible { get; set; }
            public double Width { get; set; }
            public double Opacity { get; set; }
            public string LineStyle { get; set; } = null!;
            public string SourceFileName { get; set; } = null!;
            public string SourceFileFullPath { get; set; } = null!;
            public int[] PlotColor { get; set; } = Array.Empty<int>();
            public double MarkerSize { get; set; }
            public int XMagnitude { get; set; }
            public bool ReverseX { get; set; }
            public int Smooth { get; set; }

            public CurveConfigSerializable() { }

            public CurveConfigSerializable(CurveInfo ci)
            {
                Name = ci.Name;
                Visible = ci.Visible;
                Width = ci.Width;
                Opacity = ci.Opacity;
                LineStyle = ci.LineStyle;
                SourceFileName = ci.RawSourceFileName; // 直接用字段
                SourceFileFullPath = ci.SourceFileFullPath; // 这个没问题
                PlotColor = new int[] { ci.PlotColor.A, ci.PlotColor.R, ci.PlotColor.G, ci.PlotColor.B };
                MarkerSize = ci.MarkerSize;
                XMagnitude = ci.XMagnitude;
                ReverseX = ci.ReverseX;
                Smooth = ci.Smooth;
            }
        }
        private class AppSettingsSerializable
        {
            public string XAxisLabel { get; set; } = "";
            public string YAxisLabel { get; set; } = "";
            public int ImageWidth { get; set; }
            public int ImageHeight { get; set; }
            public int LegendPosition { get; set; }
            public string? XAxisMin { get; set; }
            public string? XAxisMax { get; set; }
            public bool AutoScaleX { get; set; }
            public string? YAxisMin { get; set; }
            public string? YAxisMax { get; set; }
            public bool AutoScaleY { get; set; }
        }
        private class ProjectConfigSerializable
        {
            public AppSettingsSerializable AppSettings { get; set; } = new();
            public List<CurveConfigSerializable> Curves { get; set; } = new();
        }
    }
} 