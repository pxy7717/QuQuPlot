using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Linq;
using System.Windows.Media;
using ScottPlot;

namespace QuquPlot.Models
{
    public class CurveInfo : INotifyPropertyChanged
    {
        private string _name = "";
        private double[] _xs = Array.Empty<double>();
        private double[] _ys = Array.Empty<double>();
        private bool _visible = true;
        private double _width = 4;  // 默认线宽改为3
        private double _opacity = 1;
        private string _lineStyle = " ——  ";
        private string _sourceFileName = "";
        private string _sourceFileFullPath = "";
        // private string _hashId = "";
        private System.Windows.Media.Color _plotColor = System.Windows.Media.Colors.Blue;
        private System.Windows.Media.SolidColorBrush _brush = new(System.Windows.Media.Colors.Blue);
        private readonly Action<string>? _logAction;
        private double _markerSize = 0;  // 默认标记大小为0
        private string operationType = "无";
        private CurveInfo? targetCurve = null;
        private double[] originalYs = Array.Empty<double>();
        private Action? autoScaleAction;
        private string _lengthLabel = "长度："; // 默认中文
        public string HashId { get; private set; } = string.Empty;
        public ObservableCollection<CurveInfo> OtherCurves { get; set; } = new ObservableCollection<CurveInfo>();
        private bool isOperationEnabled = true;
        private bool isTargetCurveEnabled = true;
        private int xMagnitude = 0;
        private int lastXMagnitude = 0;
        private double[]? modifiedXs;
        private bool reverseX = false;

        public bool IsOperationEnabled
        {
            get => isOperationEnabled;
            set { if (isOperationEnabled != value) { isOperationEnabled = value; OnPropertyChanged(); } }
        }
        public bool IsTargetCurveEnabled
        {
            get => isTargetCurveEnabled;
            set { if (isTargetCurveEnabled != value) { isTargetCurveEnabled = value; OnPropertyChanged(); } }
        }
        public CurveInfo(Action<string>? logAction = null, Action? autoScaleAction = null, string? lengthLabel = null)
        {
            _logAction = logAction ?? (s => { });
            this.autoScaleAction = autoScaleAction;
            if (!string.IsNullOrEmpty(lengthLabel))
            {
                _lengthLabel = lengthLabel;
            }
        }
        private void Log(string message)
        {
            _logAction?.Invoke("[CurveInfo] " + message);
        }
        public double[] Xs { get => _xs; set { _xs = value; OnPropertyChanged(); OnPropertyChanged(nameof(DataPointCount)); OnPropertyChanged(nameof(SourceFileTooltip)); } }
        public double[] Ys { get => _ys; set { _ys = value; OnPropertyChanged(); } }
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string SourceFileName
        {
            get => GetShortFileName(_sourceFileName, 45);
            set { _sourceFileName = value; OnPropertyChanged(); }
        }
        public string SourceFileFullPath
        {
            get => _sourceFileFullPath;
            set { _sourceFileFullPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourceFileTooltip)); }
        }
        
        /// <summary>
        /// 源文件tooltip信息，包含文件路径和长度
        /// </summary>
        public string SourceFileTooltip
        {
            get => $"{_sourceFileFullPath}\n{_lengthLabel}{DataPointCount}";
        }
        public string OperationType
        {
            get => operationType;
            set {
                if (operationType != value) {
                    operationType = value;
                    OnPropertyChanged();
                    if (operationType == "无")
                        IsTargetCurveEnabled = false;
                    else
                        IsTargetCurveEnabled = true;
                }
            }
        }
        public CurveInfo? TargetCurve
        {
            get => targetCurve;
            set { if (targetCurve != value) { targetCurve = value; OnPropertyChanged(); } }
        }
        private static string GetShortFileName(string name, int maxLen)
        {
            if (string.IsNullOrEmpty(name) || name.Length <= maxLen) return name;
            return "..." + name.Substring(name.Length - maxLen);
        }
        public System.Windows.Media.Color PlotColor
        {
            get => _plotColor;
            set 
            { 
                _plotColor = value; 
                _brush = new System.Windows.Media.SolidColorBrush(value);
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(Brush)); 
            }
        }
        public Brush Brush => _brush;
        public double Width { get => _width; set { _width = Math.Round(Math.Max(1, Math.Min(17, value))); OnPropertyChanged(); } }
        public double Opacity
        {
            get => _opacity;
            set { double snapped = Math.Round(value * 10) / 10.0; _opacity = Math.Max(0, Math.Min(1, snapped)); OnPropertyChanged(); OnPropertyChanged(nameof(Brush)); }
        }
        public bool Visible { get => _visible; set { _visible = value; OnPropertyChanged(); } }
        public string LineStyle
        {
            get => _lineStyle;
            set 
            { 
                string newValue = value?.ToString() ?? " ——  ";
                if (_lineStyle != newValue) 
                { 
                    _lineStyle = newValue; 
                    OnPropertyChanged();
                    OnPropertyChanged("NeedsRedraw");
                } 
            }
        }
        public int XMagnitude
        {
            get => xMagnitude;
            set { if (xMagnitude != value) { xMagnitude = value; modifiedXs = null; OnPropertyChanged(); OnPropertyChanged(nameof(ModifiedXs)); } }
        }
        public bool ReverseX
        {
            get => reverseX;
            set { if (reverseX != value) { reverseX = value; modifiedXs = null; OnPropertyChanged(); OnPropertyChanged(nameof(ModifiedXs)); } }
        }
        public double[] ModifiedXs
        {
            get
            {
                if (modifiedXs == null || xMagnitude != lastXMagnitude)
                {
                    var xs = Xs.Select(x => x * Math.Pow(10, xMagnitude)).ToArray();
                    if (ReverseX)
                        xs = xs.Reverse().ToArray();
                    modifiedXs = xs;
                    lastXMagnitude = xMagnitude;
                }
                return modifiedXs;
            }
        }
        public double MarkerSize
        {
            get => _markerSize;
            set
            {
                if (_markerSize != value)
                {
                    _markerSize = value;
                    OnPropertyChanged(nameof(MarkerSize));
                }
            }
        }
        
        /// <summary>
        /// 数据点数量（只读属性）
        /// </summary>
        public int DataPointCount
        {
            get => Xs.Length;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null, string? debugInfo = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (debugInfo != null)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("DebugInfo"));
            if (propertyName == nameof(Ys))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("NeedsRedraw"));
        }
        public void SaveOriginalYs()
        {
            originalYs = Ys.ToArray();
        }
        public void RestoreOriginalYs()
        {
            if (originalYs != null && Ys != null && originalYs.Length == Ys.Length && !Ys.SequenceEqual(originalYs))
            {
                Ys = originalYs.ToArray();
                OnPropertyChanged(nameof(Ys));
            }
        }
        public void GenerateHashId()
        {
            if (Ys.Length == 0)
            {
                HashId = Guid.NewGuid().ToString();
                return;
            }
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = new byte[Ys.Length * sizeof(double)];
            Buffer.BlockCopy(Ys, 0, bytes, 0, bytes.Length);
            var hash = sha256.ComputeHash(bytes);
            HashId = Convert.ToBase64String(hash);
        }
        public void UpdateCurveData()
        {
            // 如果没有保存原始数据，先保存
            if (originalYs.Length == 0)
            {
                SaveOriginalYs();
            }
            
            if (OperationType == "无" || TargetCurve == null)
            {
                RestoreOriginalYs();
                return;
            }

            // 插值目标曲线的 Y 到当前曲线的 X 上
            if ((OperationType == "减" || OperationType == "-") && TargetCurve != null)
            {
                var targetYsInterp = Interpolate(TargetCurve!.Xs, TargetCurve!.Ys, Xs);
                var result = new double[Xs.Length];
                for (int i = 0; i < Xs.Length; i++)
                {
                    result[i] = originalYs[i] - targetYsInterp[i];
                }
                Ys = result;
                OnPropertyChanged(nameof(Ys));
                return;
            }

            // 默认操作
            if (Xs.Length != TargetCurve!.Xs.Length)
            {
                Log($"错误：曲线 {Name} 和 {TargetCurve!.Name} 的X轴数据点数量不匹配: {Xs.Length} vs {TargetCurve!.Xs.Length}");
                return;
            }

            var defaultResult = new double[Xs.Length];
            for (int i = 0; i < Xs.Length; i++)
            {
                switch (OperationType)
                {
                    case "减":
                        defaultResult[i] = originalYs[i] - TargetCurve!.Ys[i];
                        break;
                    case "-":
                        defaultResult[i] = originalYs[i] - TargetCurve!.Ys[i];
                        break;
                    default:
                        defaultResult[i] = originalYs[i];
                        break;
                }
            }
            Ys = defaultResult;
            OnPropertyChanged(nameof(Ys));
        }

        /// <summary>
        /// 线性插值，将 ySrc 映射到 xDst 上
        /// </summary>
        public static double[] Interpolate(double[] xSrc, double[] ySrc, double[] xDst)
        {
            var yDst = new double[xDst.Length];
            int n = xSrc.Length;
            for (int i = 0; i < xDst.Length; i++)
            {
                double x = xDst[i];
                if (x <= xSrc[0])
                {
                    yDst[i] = ySrc[0];
                }
                else if (x >= xSrc[n - 1])
                {
                    yDst[i] = ySrc[n - 1];
                }
                else
                {
                    int j = Array.BinarySearch(xSrc, x);
                    if (j < 0) j = ~j - 1;
                    if (j < 0) j = 0;
                    if (j >= n - 1) j = n - 2;
                    double x0 = xSrc[j], x1 = xSrc[j + 1];
                    double y0 = ySrc[j], y1 = ySrc[j + 1];
                    yDst[i] = y0 + (y1 - y0) * (x - x0) / (x1 - x0);
                }
            }
            return yDst;
        }
        public LinePattern GetLinePattern()
        {
            var pattern = LineStyle switch
            {
                " — —" => LinePattern.Dashed,
                " . . . . ." => LinePattern.Dotted,
                " - - - -" => LinePattern.DenselyDashed,
                " ——  " => LinePattern.Solid,
                _ => LinePattern.Solid
            };
            
            return pattern;
        }
        public System.Drawing.Color Color => System.Drawing.Color.FromArgb(
            (int)(_opacity * 255),
            _plotColor.R,
            _plotColor.G,
            _plotColor.B);
    }
} 