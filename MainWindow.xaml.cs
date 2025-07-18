using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using QuquPlot.Models;
using QuquPlot.Utils;
using ScottPlot;
using ScottPlot.Interactivity;
using ScottPlot.Interactivity.UserActionResponses;
using ScottPlot.Plottables;
using ScottPlot.Stylers;
using Color = System.Windows.Media.Color;
using Key = System.Windows.Input.Key;
using MouseButton = System.Windows.Input.MouseButton;

namespace QuquPlot
{
    public partial class MainWindow
    {
#if DEBUG
        private readonly bool SHOW_DEBUG_PANEL = true;
#else
        private bool SHOW_DEBUG_PANEL = false;
#endif

        private Dictionary<string, (CurveInfo Info, IPlottable Plot)> _curveMap = new();
        private ObservableCollection<CurveInfo> _curveInfos = new ObservableCollection<CurveInfo>();
        public ObservableCollection<CurveInfo> CurveInfos => _curveInfos;
        private bool isCurveListVisible = true;
        private GridLength? lastPlotRowHeight;
        private GridLength? lastCurveListRowHeight;

        private double _lastSplitterPosition = 0.5; // 默认50%的宽度给设置面板

        private Point _dragStartPoint;
        private int _insertionIndex = -1;
        private AdornerLayer? _adornerLayer;
        private InsertionAdorner? _insertionAdorner;

        private DispatcherTimer? _colorBlockClickTimer;
        private bool _isColorBlockDrag;
        private Border? _pendingColorBlock;
        private MouseButtonEventArgs? _pendingColorBlockEventArgs;
        private Annotation mouseCoordLabel = null!;

        private Crosshair crosshair = null!;
        private const bool enableCrosshair = true;
        private bool _isLoaded;
        private bool _isBatchLoading;

        // 实时数据流相关成员
        private Dictionary<string, (CurveInfo Info, Scatter Plot)> _realtimeCurveMap = new();
        private RealTimeDataServer? _realTimeServer;

        // 在MainWindow类中添加成员变量：
        private VerticalLine? probeLine;
        private List<Text> probeAnnotations = new();
        private List<Marker> probeMarkers = new();

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
            Y2AxisLabelTextBox.TextChanged += Y2AxisLabel_TextChanged;
            Y2AxisMinTextBox.LostFocus += OnY2AxisRangeChanged;
            Y2AxisMaxTextBox.LostFocus += OnY2AxisRangeChanged;
            Y2AxisMinTextBox.KeyDown += OnY2AxisRangeKeyDown;
            Y2AxisMaxTextBox.KeyDown += OnY2AxisRangeKeyDown;
            AutoScaleY2CheckBox.Checked += OnAutoScaleY2Changed;
            AutoScaleY2CheckBox.Unchecked += OnAutoScaleY2Changed;
            DataContext = this;

            // 监听曲线可见性变化
            _curveInfos.CollectionChanged += CurveInfos_CollectionChanged;
            CurveListView.ItemsSource = _curveInfos;

            // 1. 创建 FontStyler 实例
            var fontStyler = new FontStyler(PlotView!.Plot);

            // 2. 自动检测最佳字体（推荐，能自动适配中英文）
            fontStyler.Automatic();

            // 3. 或者手动指定字体
            // fontStyler.Set("DejaVu Sans");
            // PlotView.Plot.ScaleFactor = 2;

            Loaded += (s, e) =>
            {
                _isLoaded = true;
                PlotView!.Plot.Clear();

                // 恢复坐标轴字体设置
                PlotUtils.SetAxisFonts(PlotView.Plot, 32, 28);

                // 设置表格四周的padding
                // 顺序是左右下上
                PixelPadding padding = new(150, 170, 150, 100);
                PlotView.Plot.Layout.Fixed(padding);

                // 设置（垂直于）x轴grid样式
                var xAxisGridStyle = new GridStyle();
                xAxisGridStyle.IsVisible = true;
                xAxisGridStyle.MajorLineStyle.Width = 2;
                xAxisGridStyle.MajorLineStyle.Color = ColorUtils.ColorFromHex("#2B2B2B", 0.2);
                xAxisGridStyle.MinorLineStyle.Width = 0;
                xAxisGridStyle.MinorLineStyle.Color = ColorUtils.ColorFromHex("#2B2B2B", 0.5);
                xAxisGridStyle.IsBeneathPlottables = true;

                var yAxisGridStyle = new GridStyle();
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

                // 鼠标中键自定义缩放
                // 1. 先移除鼠标中键缩放
                var zoomResponse = uip.UserActionResponses
                    .OfType<SingleClickAutoscale>()
                    .FirstOrDefault();

                if (zoomResponse != null)
                {
                    uip.UserActionResponses.Remove(zoomResponse);
                }

                // 2. 添加鼠标中键自定义缩放
                PlotView.MouseDown += (sender2, e2) =>
                {
                    if (e2.ChangedButton == MouseButton.Middle)
                    {
                        AutoScaleAllVisibleAxes();
                        PlotView.Refresh();
                        e2.Handled = true;
                    }
                };
                
                // 将左键拖拽改为右键拖拽
                var panResponse = uip.UserActionResponses
                    .OfType<MouseDragPan>()
                    .FirstOrDefault();

                if (panResponse != null)
                {
                    uip.UserActionResponses.Remove(panResponse);
                    var panButton = StandardMouseButtons.Right;
                    panResponse = new MouseDragPan(panButton);
                    uip.UserActionResponses.Add(panResponse);
                }

                // 添加鼠标移动事件，用于刷新坐标
                PlotView.MouseMove += PlotView_MouseMove_ScottPlot;
                // 设置crosshair
                AddMouseAnnotations();

                // PlotView.Refresh();

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
                _realTimeServer = new RealTimeDataServer();
                _realTimeServer.DataReceived += (curveId, x, y) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!_realtimeCurveMap.TryGetValue(curveId, out var tuple))
                        {
                            // 新建 CurveInfo
                            var curveInfo = new CurveInfo(AppendDebugInfo, null, "长度：");
                            curveInfo.Name = curveId;
                            curveInfo.Xs = new[] { x };
                            curveInfo.Ys = new[] { y };
                            curveInfo.Visible = true;
                            curveInfo.isStreamData = true;
                            // 为streamData生成唯一HashId
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
                UpdateY2AxisInputState(); // 启动时刷新Y2轴范围显示

                
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

            AllowDrop = true;
            DragEnter += MainWindow_DragEnter;
            DragOver += MainWindow_DragOver;
            Drop += MainWindow_Drop;
            SaveCurvesConfigButton.Click += SaveCurvesConfigButton_Click;
            LoadCurvesConfigButton.Click += LoadCurvesConfigButton_Click;
            AutoScaleXCheckBox.Checked += OnAutoScaleXChanged;
            AutoScaleXCheckBox.Unchecked += OnAutoScaleXChanged;
            // UpdateXAxisInputState();

            // 在窗口Loaded或构造函数注册MouseDown事件时，扩展如下：
            // ...已有代码...
            PlotView.MouseDown += (sender, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    AddProbeAtMouse(e);
                    e.Handled = true;
                }
                // ...已有中键缩放逻辑...
            };
            // ...已有代码...
        }

        protected override void OnClosed(EventArgs e)
        {
            _realTimeServer?.Dispose();
            base.OnClosed(e);
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
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
                        var sparam = SParameterFileParser.ParseFile(filePath);
                        AppendDebugInfo($"S参数文件解析成功，频点数: {sparam.Frequencies.Count}");
                        foreach (var kv in sparam.Magnitudes)
                        {
                            var ys = kv.Value.ToArray();
                            var xs = sparam.Frequencies.ToArray();
                            var label = kv.Key;
                            AddCurveToPlot(label, xs, ys, filePath, true);
                        }
                        AutoScaleAllVisibleAxes();
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

                AutoScaleAllVisibleAxes();
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

            // 临时隐藏红线、marker和text
            // bool probeLineWasVisible = probeLine?.IsVisible ?? false;
            // if (probeLine != null) probeLine.IsVisible = false;
            // var markerVis = probeMarkers.Select(m => m.IsVisible).ToList();
            // for (int i = 0; i < probeMarkers.Count; i++)
            //     probeMarkers[i].IsVisible = false;
            // var textVis = probeAnnotations.Select(t => t.IsVisible).ToList();
            // for (int i = 0; i < probeAnnotations.Count; i++)
            //     probeAnnotations[i].IsVisible = false;

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

            // 恢复红线、marker和text
            // if (probeLine != null) probeLine.IsVisible = probeLineWasVisible;
            // for (int i = 0; i < probeMarkers.Count; i++)
            //     probeMarkers[i].IsVisible = markerVis[i];
            // for (int i = 0; i < probeAnnotations.Count; i++)
            //     probeAnnotations[i].IsVisible = textVis[i];
        }

        private void ZoomResetButton_Click(object sender, RoutedEventArgs e)
        {
            AutoScaleAllVisibleAxes();
            PlotView.Refresh();
        }

        private void OnAboutPopupClick(object sender, RoutedEventArgs e)
        {
            AboutPopup.IsOpen = true;
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
            AutoScaleAllVisibleAxes();
            PlotView.Refresh();

            // 复位坐标轴设置
            XAxisLabelTextBox.Text = TryFindResource("XAxisLabel") as string ?? "X";
            YAxisLabelTextBox.Text = TryFindResource("YAxisLabel") as string ?? "Y";
            Y2AxisLabelTextBox.Text = TryFindResource("Y2AxisLabel") as string ?? "Y2";
            AutoScaleXCheckBox.IsChecked = true;
            AutoScaleYCheckBox.IsChecked = true;
            AutoScaleY2CheckBox.IsChecked = true;
            UpdateXAxisInputState();
            UpdateYAxisInputState();
            UpdateY2AxisInputState();

            LegendPositionComboBox.SelectedIndex = 0;
            UpdateLegends();
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
                AutoScaleAllVisibleAxes();
                PlotView.Refresh();
                AppendDebugInfo($"当前曲线数量: {_curveInfos.Count}, 曲线映射数量: {_curveMap.Count}");
            }
        }

        private void CurveInfos_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

        private void CurveInfo_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isBatchLoading) return;
            if (sender is not CurveInfo curveInfo || e.PropertyName == null)
                return;

            int idx = _curveInfos.IndexOf(curveInfo);
            if (idx < 0 || idx >= _curveMap.Count)
                return;

            if (_curveMap.Values.ElementAt(idx).Plot is not Scatter scatter)
                return;
            
            bool needRefresh = false;
            bool needUpdateProbe = false;
            switch (e.PropertyName)
            {
                case nameof(CurveInfo.Width):
                    if (curveInfo.Width != scatter.LineWidth)
                    {
                        scatter.LineWidth = (float)curveInfo.Width;
                        AppendDebugInfo($"更新线宽: {curveInfo.Width}");
                        needRefresh = true;
                        UpdateLegends();
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
                    // 在可见性改变后重新调整坐标轴（用自定义智能缩放）
                    AutoScaleAllVisibleAxes();
                    needRefresh = true;
                    needUpdateProbe = true;
                    break;
                case nameof(CurveInfo.LineStyle):
                    var pattern = curveInfo.GetLinePattern();
                    scatter.LinePattern = pattern;
                    UpdateLegends();
                    AppendDebugInfo($"更新线型: {curveInfo.LineStyle} -> {pattern}");
                    needRefresh = true;
                    break;
                case nameof(CurveInfo.Opacity):
                    if (curveInfo.Opacity != scatter.Color.A)
                    {
                        scatter.Color = ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity);
                        AppendDebugInfo($"更新透明度: {curveInfo.Opacity}");
                        UpdateLegends();
                        needRefresh = true;
                    }
                    break;
                case nameof(CurveInfo.PlotColor):
                    scatter.Color = ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity);
                    UpdateLegends();
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
                    needUpdateProbe = true;
                    break;
                case nameof(CurveInfo.Ys):
                    AppendDebugInfo($"更新Ys: {curveInfo.Name}");
                    needRefresh = true;
                    needUpdateProbe = true;
                    break;
                case nameof(CurveInfo.XMagnitude):
                    AppendDebugInfo($"更新X缩放: {curveInfo.XMagnitude}");
                    needRefresh = true;
                    needUpdateProbe = true;
                    break;
                case nameof(CurveInfo.ReverseX):
                    AppendDebugInfo($"反转X顺序: {curveInfo.ReverseX}");
                    needRefresh = true;
                    needUpdateProbe = true;
                    break;
                case nameof(CurveInfo.Smooth):
                    AppendDebugInfo($"更新平滑: {curveInfo.Smooth}");
                    needRefresh = true;
                    needUpdateProbe = true;
                    break;
                case nameof(CurveInfo.Y2):
                    AppendDebugInfo($"更新Y2: {curveInfo.Y2}");
                    UpdateYAxisRightVisibility();
                    // 新增：如果Y2轴自动缩放已勾选，自动缩放右轴并刷新输入框
                    if (AutoScaleY2CheckBox.IsChecked == true)
                    {
                        UpdateY2AxisInputState();
                    }
                    needRefresh = true;
                    needUpdateProbe = true;
                    break;
            }

            if (needRefresh)
                RedrawCurve(curveInfo);
            // 只有数据相关属性变动时才重绘探针标注
            if (needUpdateProbe && probeLine != null)
                AddProbeAtX(probeLine.Position);
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
                    var colorObj = ColorConverter.ConvertFromString(hex);
                    if (colorObj is Color color)
                    {
                        info.PlotColor = color;
                        // 确保UI更新颜色方块
                        info.OnPropertyChanged(nameof(info.Brush));
                        // 更新对应曲线颜色
                        int idx = _curveInfos.IndexOf(info);
                        if (idx >= 0 && idx < _curveMap.Count)
                        {
                            if (_curveMap.Values.ElementAt(idx).Plot is Scatter scatter)
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
            Debug.WriteLine(message);
        }

        private Scatter DrawCurve(CurveInfo curveInfo)
        {
            var scatter = PlotUtils.DrawCurve(PlotView.Plot, curveInfo, AppendDebugInfo);
            _curveMap[curveInfo.HashId] = (curveInfo, scatter); // 维护映射
            return scatter;
        }

        private LegendItem BuildLegendItem(CurveInfo curveInfo)
        {
            LegendItem curveLegend = new()
            {
                LineColor = ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity),
                LineWidth = (float)curveInfo.Width,
                LinePattern = curveInfo.GetLinePattern(),
                LabelText = curveInfo.Name
            };


            return curveLegend;
        }

        private void UpdateLegends()
        {
            AppendDebugInfo("开始更新图例");
            var items = new List<LegendItem>();
            foreach (var _curveInfo in _curveInfos)
            {
                if (_curveInfo._visible)
                {
                    items.Add(BuildLegendItem(_curveInfo));
                }
            }
            AppendDebugInfo($"图例数量: {items.Count}");
            PlotView.Plot.Legend.DisplayPlottableLegendItems = false;
            PlotView.Plot.ShowLegend(items.ToArray());
            var comboItem = LegendPositionComboBox.SelectedItem as ComboBoxItem;
            if (comboItem != null)
                PlotView.Plot.Legend.Alignment = GetLegendAlignment(comboItem);            
            PlotView.Refresh();
        }

        private void RedrawCurve(CurveInfo curveInfo)
        {
            if (_isBatchLoading) return;
            // 先移除旧的
            if (_curveMap.TryGetValue(curveInfo.HashId, out var curveData))
            {
                PlotView.Plot.Remove(curveData.Plot);
            }
            var scatter = DrawCurve(curveInfo);
            AutoScaleAllVisibleAxes();
            SortPlotLayers();
            AppendDebugInfo("重绘完成");
            UpdateLegends();
            // 不再自动刷新探针
            PlotView.Refresh(); 
        }

        private void RedrawAllCurves()
        {
            PlotView.Plot.Clear();
            AddMouseAnnotations();
            _curveMap.Clear();

            foreach (var curveInfo in _curveInfos)
            {
                DrawCurve(curveInfo);
            }
            // 改进自动缩放逻辑：只考虑可见曲线
            AutoScaleAllVisibleAxes();
            UpdateYAxisRightVisibility();
            
            SortPlotLayers();
            
            UpdateXAxisInputState();
            UpdateYAxisInputState(); // 重绘all时同步Y轴输入框
            UpdateY2AxisInputState(); // 重绘all时同步Y2轴输入框

            UpdateLegends();
            // 不再自动刷新探针
            PlotView.Refresh();
            AppendDebugInfo("重绘all完成");
        }

        private void SortPlotLayers()
        {
            if (probeLine != null)
                PlotView.Plot.MoveToTop(probeLine);
            foreach (var a in probeAnnotations)
            {
                PlotView.Plot.MoveToTop(a);
            }
            PlotView.Plot.MoveToTop(crosshair);
        }

        private (double min, double max) GetSmartAxisLimits(IEnumerable<double> values, double lowerPercentile = 0.1, double upperPercentile = 99.9, double outlierThreshold = 200)
        {
            var filtered = values.Where(y => !double.IsNaN(y) && !double.IsInfinity(y) && Math.Abs(y) < outlierThreshold).OrderBy(y => y).ToArray();
            if (filtered.Length > 10)
            {
                double min = Percentile(filtered, lowerPercentile);
                double max = Percentile(filtered, upperPercentile);
                return (min, max);
            }

            if (filtered.Length > 0)
            {
                return (filtered.Min(), filtered.Max());
            }

            return (-60, 0); // fallback
        }

        private void AutoScaleXVisibleCurves()
        {
            var visibleCurves = _curveInfos.Where(ci => ci.Visible && ci.ModifiedXs != null && ci.ModifiedXs.Length > 0);
            if (!visibleCurves.Any()) return;
            var allX = visibleCurves.SelectMany(ci => ci.ModifiedXs).Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).OrderBy(x => x).ToArray();
            if (allX.Length == 0) return;
            double minX = allX.Min();
            double maxX = allX.Max();
            double padding = (maxX - minX) * 0.02;
            PlotView.Plot.Axes.SetLimitsX(minX - padding, maxX + padding);
        }

        private void AutoScaleYVisibleCurves()
        {
            var visibleCurves = _curveInfos.Where(ci => ci.Visible && (!ci.Y2) && ci.Ys != null && ci.Ys.Length > 0);
            if (!visibleCurves.Any()) return;
            var allY = visibleCurves.SelectMany(ci => ci.Ys);
            var (minY, maxY) = GetSmartAxisLimits(allY);
            double padding = (maxY - minY) * 0.05;
            PlotView.Plot.Axes.SetLimitsY(minY - padding, maxY + padding, PlotView.Plot.Axes.Left);
        }

        private void AutoScaleY2VisibleCurves()
        {
            var visibleCurves = _curveInfos.Where(ci => ci.Visible && ci.Y2 && ci.Ys != null && ci.Ys.Length > 0);
            if (!visibleCurves.Any()) return;
            var allY = visibleCurves.SelectMany(ci => ci.Ys);
            var (minY, maxY) = GetSmartAxisLimits(allY);
            double padding = (maxY - minY) * 0.05;
            PlotView.Plot.Axes.SetLimitsY(minY - padding, maxY + padding, PlotView.Plot.Axes.Right);
        }

        private void UpdateYAxisRightVisibility()
        {
            bool hasY2Curve = _curveInfos.Any(ci => ci.Y2);
            var rightAxis = PlotView.Plot.Axes.Right;

            rightAxis.MajorTickStyle.Length = hasY2Curve? 10 : 0;
            rightAxis.MinorTickStyle.Length = hasY2Curve? 5 : 0;
            rightAxis.TickLabelStyle.IsVisible = hasY2Curve;
            rightAxis.Label.IsVisible = hasY2Curve;
            rightAxis.IsVisible = true;
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

        private IPlottable? AddCurveToPlot(string label, double[] xs, double[] ys, string sourceFileName, bool isFirstCurveInFile)
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
            curveInfo.SourceFileName = Path.GetFileName(sourceFileName);
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
            curveInfo.PlotColor = Color.FromRgb(nextColor.R, nextColor.G, nextColor.B);
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
                // scatter.LegendText = curveInfo.Name;
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
                AutoScaleAllVisibleAxes();

                
                PlotView.Refresh();
                UpdateXAxisInputState();
                UpdateYAxisInputState(); // 添加曲线时同步Y轴输入框
                UpdateY2AxisInputState(); // 添加曲线时同步Y2轴输入框
                
                UpdateLegends();
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
            var timer = new DispatcherTimer
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
            Pixel mousePixel = new(p.X * PlotView.DisplayScale, p.Y * PlotView.DisplayScale);
            
            Coordinates mouseCoordinates = PlotView.Plot.GetCoordinates(mousePixel);
            crosshair.Position = mouseCoordinates;


            mouseCoordLabel.Text = $"({FormatNumber(mouseCoordinates.X)}, {FormatNumber(mouseCoordinates.Y)})";
            PlotView.Refresh();
        }

        public static string FormatNumber(double value)
        {

            if (Math.Abs(value) < 0.01 || Math.Abs(value) > 100)
            {
                // 科学计数法，最多三位有效数字，去掉多余的0和小数点
                string sci = value.ToString("0.###E+0");
                // 处理 0E+0 的特殊情况
                if (sci.StartsWith("0E") || sci.StartsWith("-0E"))
                    return "0";
                return sci;
            }

            // 普通显示，最多三位小数，去掉多余的0和小数点
            string norm = value.ToString("0.###");
            return norm;
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

                e.Handled = true;
                return;
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

            Alignment alignment = GetLegendAlignment(selectedItem);
            PlotView.Plot.Legend.Alignment = alignment;
            PlotView.Refresh();
            AppendDebugInfo($"图例位置已更改为: {selectedItem.Content}");
        }

        private Alignment GetLegendAlignment(ComboBoxItem selectedComboBoxItem)
        {
            Alignment alignment = selectedComboBoxItem.Content.ToString() switch
            {
                "左下" or "Lower L" => Alignment.LowerLeft,
                "左上" or "Upper L" => Alignment.UpperLeft,
                "右下" or "Lower R" => Alignment.LowerRight,
                "右上" or "Upper R" => Alignment.UpperRight,
                _ => Alignment.LowerLeft
            };

            AppendDebugInfo($"ComboBoxItem.Content={selectedComboBoxItem?.Content}, Type={selectedComboBoxItem?.GetType()}");
            return alignment;
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
                AutoScaleXVisibleCurves();
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
                AutoScaleYVisibleCurves();
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

        private void Y2AxisLabel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (PlotView?.Plot == null) return;
            PlotView.Plot.Axes.Right.Label.Text = Y2AxisLabelTextBox.Text;
            PlotView.Refresh();
        }

        private void OnY2AxisRangeChanged(object? sender, RoutedEventArgs e)
        {
            if (double.TryParse(Y2AxisMinTextBox.Text, out double min) && double.TryParse(Y2AxisMaxTextBox.Text, out double max) && min < max)
            {
                PlotView.Plot.Axes.SetLimitsY(min, max, PlotView.Plot.Axes.Right);
                PlotView.Refresh();
            }
        }
        private void OnY2AxisRangeKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnY2AxisRangeChanged(sender, e);
            }
        }

        private void OnAutoScaleY2Changed(object? sender, RoutedEventArgs e)
        {
            UpdateY2AxisInputState();
            if (AutoScaleY2CheckBox.IsChecked == true)
            {
                AutoScaleY2VisibleCurves();
                PlotView.Refresh();
                UpdateY2AxisInputState();
            }
            else
            {
                OnY2AxisRangeChanged(null, new RoutedEventArgs());
            }
        }
        private void UpdateY2AxisInputState()
        {
            bool auto = AutoScaleY2CheckBox.IsChecked == true;
            Y2AxisMinTextBox.IsEnabled = !auto;
            Y2AxisMaxTextBox.IsEnabled = !auto;
            Y2AxisMinTextBox.IsReadOnly = auto;
            Y2AxisMaxTextBox.IsReadOnly = auto;
            if (auto)
            {
                var y2Axis = PlotView.Plot.Axes.Right;
                if (double.IsInfinity(y2Axis.Min) || double.IsInfinity(y2Axis.Max))
                {
                    Y2AxisMinTextBox.Text = "";
                    Y2AxisMaxTextBox.Text = "";
                }
                else
                {
                    Y2AxisMinTextBox.Text = y2Axis.Min.ToString("G4");
                    Y2AxisMaxTextBox.Text = y2Axis.Max.ToString("G4");
                }
            }
        }

        private void SaveCurvesConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
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
                        AutoScaleY = AutoScaleYCheckBox.IsChecked == true,
                        Y2AxisLabel = Y2AxisLabelTextBox.Text,
                        Y2AxisMin = Y2AxisMinTextBox.Text,
                        Y2AxisMax = Y2AxisMaxTextBox.Text,
                        AutoScaleY2 = AutoScaleY2CheckBox.IsChecked == true
                    };
                    var projectConfig = new ProjectConfigSerializable
                    {
                        AppSettings = appSettings,
                        Curves = _curveInfos.Select(ci => new CurveConfigSerializable(ci)).ToList()
                    };
                    var json = JsonSerializer.Serialize(projectConfig, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
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
            var dialog = new OpenFileDialog
            {
                Filter = "曲线配置文件|*.json|所有文件|*.*",
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(dialog.FileName);
                    var projectConfig = JsonSerializer.Deserialize<ProjectConfigSerializable>(json);
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
                        XAxisMinTextBox.Text = (appSettings.XAxisMin ?? string.Empty);
                        XAxisMaxTextBox.Text = (appSettings.XAxisMax ?? string.Empty);
                        // 自动应用X轴范围
                        OnXAxisRangeChanged(null, new RoutedEventArgs());
                        AutoScaleXCheckBox.IsChecked = appSettings.AutoScaleX;
                        UpdateXAxisInputState();
                        YAxisMinTextBox.Text = (appSettings.YAxisMin ?? string.Empty);
                        YAxisMaxTextBox.Text = (appSettings.YAxisMax ?? string.Empty);
                        AutoScaleYCheckBox.IsChecked = appSettings.AutoScaleY;
                        UpdateYAxisInputState();
                        Y2AxisLabelTextBox.Text = appSettings.Y2AxisLabel;
                        Y2AxisMinTextBox.Text = (appSettings.Y2AxisMin ?? string.Empty);
                        Y2AxisMaxTextBox.Text = (appSettings.Y2AxisMax ?? string.Empty);
                        AutoScaleY2CheckBox.IsChecked = appSettings.AutoScaleY2;
                        UpdateY2AxisInputState();
                    }
                    // 恢复曲线
                    var curveConfigs = projectConfig.Curves;
                    if (curveConfigs == null)
                    {
                        AppendDebugInfo($"配置文件无曲线: {dialog.FileName}");
                        return;
                    }
                    int restored = 0, failed = 0;
                    _isBatchLoading = true;
                    try
                    {
                        foreach (var cfg in curveConfigs)
                        {
                            string dataPath = string.IsNullOrWhiteSpace(cfg.SourceFileFullPath) ? cfg.SourceFileName : cfg.SourceFileFullPath;
                            if (string.IsNullOrWhiteSpace(dataPath) || !File.Exists(dataPath))
                            {
                                AppendDebugInfo($"找不到数据文件: {dataPath}");
                                failed++;
                                continue;
                            }
                            try
                            {
                                // 复用已有的文件处理逻辑
                                // 只加载数据，不添加到_plot，只生成xs,ys
                                string ext = FileUtils.GetFileExtension(dataPath);
                                List<string> processedLines;
                                switch (ext)
                                {
                                    case ".csv":
                                        processedLines = FileUtils.ProcessCsvFile(dataPath, AppendDebugInfo);
                                        break;
                                    case ".xls":
                                    case ".xlsx":
                                        processedLines = FileUtils.ProcessExcelFile(dataPath, AppendDebugInfo);
                                        break;
                                    case ".txt":
                                        processedLines = FileUtils.ProcessTxtFile(dataPath, AppendDebugInfo);
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

                                    FileUtils.ProcessDataLines(
                                        processedLines,
                                        Path.GetFileName(dataPath),
                                        AppendDebugInfo,
                                        (label, xs, ys, src, isFirst) =>
                                        {
                                            var temp = new CurveInfo();
                                            temp.Xs = xs;
                                            temp.Ys = ys;
                                            temp.GenerateHashId();
                                            if (temp.HashId == cfg.HashId)
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
                                                // 只添加到 _curveInfos，不触发重绘
                                                _curveInfos.Add(ci);
                                                restored++;
                                            }
                                            
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
                    }
                    finally
                    {
                        _isBatchLoading = false;
                    }
                    RedrawAllCurves(); // 只在最后重绘一次
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
            public string HashId { get; set; } = "";

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
                HashId = ci.HashId;
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
            public string? Y2AxisLabel { get; set; }
            public string? Y2AxisMin { get; set; }
            public string? Y2AxisMax { get; set; }
            public bool AutoScaleY2 { get; set; }
        }
        private class ProjectConfigSerializable
        {
            public AppSettingsSerializable AppSettings { get; set; } = new();
            public List<CurveConfigSerializable> Curves { get; set; } = new();
        }

        private void VisibleAllButton_Click(object sender, RoutedEventArgs e)
        {
            bool allVisible = _curveInfos.All(c => c.Visible);
            foreach (var curve in _curveInfos)
                curve.Visible = !allVisible;
            PlotView.Refresh();
        }

        // 1. 定义统一入口方法
        private void AutoScaleAllVisibleAxes()
        {
            AutoScaleXVisibleCurves();
            AutoScaleYVisibleCurves();
            AutoScaleY2VisibleCurves();
        }

        private void AddMouseAnnotations()
        {
            crosshair = PlotView.Plot.Add.Crosshair(0, 0);
            crosshair.IsVisible = enableCrosshair;
            crosshair.LineWidth = 1;
            crosshair.LineColor = ColorUtils.ColorFromHex("#2b2b2b", 0.8);
            crosshair.LinePattern = LinePattern.Dashed;

            mouseCoordLabel = PlotView.Plot.Add.Annotation("");
            mouseCoordLabel.LabelBorderWidth = 0;
            mouseCoordLabel.LabelBackgroundColor = ColorUtils.ColorFromHex("#FAFAFA", 0.0);
            mouseCoordLabel.LabelFontColor = ColorUtils.ColorFromHex("#9b9b9b");
            mouseCoordLabel.LabelShadowColor = ColorUtils.ColorFromHex("#000000", 0.0);
            mouseCoordLabel.LabelFontSize = 35;
            // mouseCoordLabel.OffsetY = -20;
        }

        private void AddProbeAtX(double probeX)
        {
            const double X_THRESHOLD_RATIO = 0.05; // 5%
            const double X_THRESHOLD_MIN = 1e-6;
            // 移除旧的竖线、标注和marker
            if (probeLine != null)
                PlotView.Plot.Remove(probeLine);
            foreach (var txt in probeAnnotations)
                PlotView.Plot.Remove(txt);
            probeAnnotations.Clear();
            foreach (var marker in probeMarkers)
                PlotView.Plot.Remove(marker);
            probeMarkers.Clear();

            // 添加红色竖线
            probeLine = PlotView.Plot.Add.VerticalLine(probeX, color: ScottPlot.Color.FromHex("#FF0000"));
            probeLine.IsDraggable = true;
            probeLine.LineWidth = 2;
            probeLine.Position = probeX;

            // 对每条可见曲线，找最近点并标注
            foreach (var curve in _curveInfos.Where(ci => ci.Visible && ci.ModifiedXs.Length > 0))
            {
                var xs = curve.ModifiedXs;
                var ys = curve.Ys;
                int idx = Array.FindIndex(xs, x => x >= probeX);
                if (idx < 0) idx = xs.Length - 1;
                if (idx > 0 && (Math.Abs(xs[idx] - probeX) > Math.Abs(xs[idx - 1] - probeX)))
                    idx--;

                double px = xs[idx];
                double py = ys[idx];

                // 计算阈值
                double xMin = xs.Min();
                double xMax = xs.Max();
                double xRange = Math.Abs(xMax - xMin);
                double threshold = Math.Max(xRange * X_THRESHOLD_RATIO, X_THRESHOLD_MIN);
                if (Math.Abs(px - probeX) > threshold)
                    continue; // 距离太远，不标注

                // 选择Y轴
                var yAxis = curve.Y2 ? PlotView.Plot.Axes.Right : PlotView.Plot.Axes.Left;

                // 添加marker
                var marker = PlotView.Plot.Add.Marker(px, py);
                marker.Size = 10;
                marker.Shape = MarkerShape.FilledCircle;
                marker.Color = ScottPlot.Color.FromHex("#FA0000");
                marker.Axes.YAxis = yAxis; // ScottPlot 5.x: 绑定到对应Y轴
                probeMarkers.Add(marker);

                // 添加文本
                var valueStr = $"({FormatNumber(px)}, {FormatNumber(py)})";
                var txt = PlotView.Plot.Add.Text(valueStr, px, py);
                txt.LabelFontSize = 30;
                txt.LabelBackgroundColor = ColorUtils.ColorFromHex("#eeeeee", 0.6);
                txt.LabelFontColor = ColorUtils.ToScottPlotColor(curve.PlotColor, curve.Opacity);
                txt.Axes.YAxis = yAxis;
                var padding3 = new ScottPlot.PixelPadding(5,5,2,2);
                txt.LabelPixelPadding = padding3;
                txt.LabelBorderRadius = 10;
                txt.LabelShadowColor = ColorUtils.ColorFromHex("#f2f2f2", 0.2);

                // 计算红线像素位置
                double probePixelX = PlotView.Plot.GetPixel(new Coordinates(probeX, py)).X;
                // 找出所有曲线数据中Y值最小和最大值
                double coordMaxX = double.MinValue;
                if (curve.Visible)
                {
                    coordMaxX = Math.Max(coordMaxX, curve.ModifiedXs.Max());
                }
                double plotPixelMaxX = PlotView.Plot.GetPixel(new Coordinates(coordMaxX, 0)).X;
                // 判断是否靠近右侧1/5
                txt.LabelAlignment = probePixelX > plotPixelMaxX * 4.0 / 5.0 ? Alignment.MiddleRight : Alignment.MiddleLeft;
                probeAnnotations.Add(txt);
            }
            
            // 应用智能避让算法，避免标注重叠
            AdjustAnnotationPositions(probeAnnotations);
        }

        private async void AdjustAnnotationPositions(List<Text> textLabels)
        {
            if (textLabels.Count <= 1) return;

            PlotView.Refresh();
            foreach (var a in textLabels)
            {
                a.LabelOffsetY = 0;
            }

            const int maxIterations = 50;
            const float contractPx = 0;
            const float pushStep = 10; // 每次推开的像素步长
            bool changed;


            for (int iter = 0; iter < maxIterations; iter++)
            {
                changed = false;
                PlotView.Refresh();
                await Task.Delay(10);

                // 找出所有曲线数据中Y值最小和最大值
                double coordMinY = double.MaxValue;
                double coordMaxY = double.MinValue;
                foreach (var curve in _curveInfos)
                {
                    if (curve.Visible)
                    {
                        coordMinY = Math.Min(coordMinY, curve.Ys.Min());
                        coordMaxY = Math.Max(coordMaxY, curve.Ys.Max());
                    }
                }
                double plotTop = PlotView.Plot.GetPixel(new Coordinates(0, coordMaxY)).Y;
                double plotBottom = PlotView.Plot.GetPixel(new Coordinates(0, coordMinY)).Y;
                double pixelCenterY = (plotTop + plotBottom) / 2.0;

                // 1. 获取所有标注的最新像素区间
                var labelRects = textLabels.Select(a => a.LabelLastRenderPixelRect.Contract(contractPx)).ToList();

                // 2. 分组（按像素中心Y分组）
                var topHalfTextList = new List<Text>();
                var bottomHalfTextList = new List<Text>();
                for (int i = 0; i < labelRects.Count; i++)
                {
                    if (labelRects[i].Top <= pixelCenterY)
                        topHalfTextList.Add(textLabels[i]);
                    else
                        bottomHalfTextList.Add(textLabels[i]);
                }
                // 上组按像素Y升序，下组按像素Y降序
                bottomHalfTextList.Sort((i, j) => j.LabelLastRenderPixelRect.Top.CompareTo(i.LabelLastRenderPixelRect.Top));
                topHalfTextList.Sort((i, j) => i.LabelLastRenderPixelRect.Top.CompareTo(j.LabelLastRenderPixelRect.Top));
                

                // 3. 组内优先级避让

                for (int i = 0; i < topHalfTextList.Count; i++)
                {
                    var currentRect = topHalfTextList[i].LabelLastRenderPixelRect.Contract(contractPx);
                    double targetPos = plotTop;
                    if (i > 0)
                        targetPos = topHalfTextList[i-1].LabelLastRenderPixelRect.Contract(contractPx).Bottom;
                    while (currentRect.Top < targetPos)
                    {
                        topHalfTextList[i].OffsetY += pushStep;
                        PlotView.Refresh();
                        await Task.Delay(10);
                        currentRect = topHalfTextList[i].LabelLastRenderPixelRect.Contract(contractPx);
                        if (i > 0)
                            targetPos = topHalfTextList[i-1].LabelLastRenderPixelRect.Contract(contractPx).Bottom;
                    }
                }
                
                for (int i = 0; i < bottomHalfTextList.Count; i++)
                {
                    var currentRect = bottomHalfTextList[i].LabelLastRenderPixelRect.Contract(contractPx);
                    double targetPos = plotBottom;
                    if (i > 0)
                        targetPos = bottomHalfTextList[i-1].LabelLastRenderPixelRect.Contract(contractPx).Top;
                    while (currentRect.Bottom > targetPos)
                    {
                        bottomHalfTextList[i].OffsetY -= pushStep;
                        PlotView.Refresh();
                        await Task.Delay(10);
                        currentRect = bottomHalfTextList[i].LabelLastRenderPixelRect.Contract(contractPx);
                        if (i > 0)
                            targetPos = bottomHalfTextList[i-1].LabelLastRenderPixelRect.Contract(contractPx).Top;
                    }
                }
                
                PlotView.Refresh();
                if (!changed)
                {
                    AppendDebugInfo($"[避让] 迭代 {iter + 1} 无重叠，结束");
                    break;
                }

            }

            // 最终判定
            var finalRects = textLabels.Select(a => a.LabelLastRenderPixelRect.Contract(contractPx)).ToList();
            bool valid = true;
            for (int i = 0; i < textLabels.Count; i++)
            {
                for (int j = i + 1; j < textLabels.Count; j++)
                {
                    var rectA = finalRects[i];
                    var rectB = finalRects[j];
                    bool overlap = !(rectA.Right < rectB.Left || rectA.Left > rectB.Right ||
                                     rectA.Bottom < rectB.Top || rectA.Top > rectB.Bottom);
                    if (overlap)
                    {
                        AppendDebugInfo($"[最终重叠] 标注{i} 与 标注{j}");
                        valid = false;
                    }
                }
            }
            if (!valid)
                AppendDebugInfo("最终仍有标注重叠，请检查数据或调整参数。");
        }

        private void AddProbeAtMouse(MouseButtonEventArgs e)
        {
            // 获取鼠标在图表坐标系中的位置
            Point p = e.GetPosition(PlotView);
            Pixel mousePixel = new(p.X * PlotView.DisplayScale, p.Y * PlotView.DisplayScale);
            Coordinates mouseCoordinates = PlotView.Plot.GetCoordinates(mousePixel);
            double probeX = mouseCoordinates.X;
            AddProbeAtX(probeX);
            PlotView.Refresh();
        }

        // 曲线名字TextBox首次点击时全选
        private void CurveNameTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.Focus();
                textBox.SelectAll();
                e.Handled = true;
            }
        }

        // 曲线名字TextBox获得焦点时全选
        private void CurveNameTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
                e.Handled = true;
            }
        }
    }
} 