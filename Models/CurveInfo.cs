using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Media;
using ScottPlot;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace QuquPlot.Models
{
    public class CurveInfo : INotifyPropertyChanged
    {
        private string _name = "";
        private double[] _xs = Array.Empty<double>();
        private double[] _ys = Array.Empty<double>();
        public bool _visible { get; private set; } = true;
        private double _width = 5;
        private double _opacity = 1;
        private string _lineStyle = " ——  ";
        private string _sourceFileName = "";
        private string _sourceFileFullPath = "";
        // private string _hashId = "";
        private Color _plotColor = Colors.Blue;
        private SolidColorBrush _brush = new(Colors.Blue);
        private readonly Action<string>? _logAction;
        private double _markerSize;  // 默认标记大小为0
        private string operationType = "无";
        private CurveInfo? targetCurve;
        private double[] originalYs = Array.Empty<double>();
        private Action? autoScaleAction;
        private string _lengthLabel = "长度："; // 默认中文
        public string HashId { get; private set; } = string.Empty;
        public ObservableCollection<CurveInfo> OtherCurves { get; set; } = new ObservableCollection<CurveInfo>();
        private bool isOperationEnabled = true;
        private bool isTargetCurveEnabled = true;
        private int xMagnitude;
        private int lastXMagnitude;
        private double[]? modifiedXs;
        private bool reverseX;
        private int _smooth; // 0-4, 默认0
        public bool isStreamData = false;
        public bool Y2
        {
            get => _y2;
            set { if (_y2 != value) { _y2 = value; OnPropertyChanged(); } }
        }
        private bool _y2;

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
        public Color PlotColor
        {
            get => _plotColor;
            set 
            { 
                _plotColor = value; 
                _brush = new SolidColorBrush(value);
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
                string newValue = value ?? " ——  ";
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
                    OnPropertyChanged();
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
        public int Smooth
        {
            get => _smooth;
            set
            {
                int v = Math.Max(0, Math.Min(4, value));
                if (_smooth != v)
                {
                    _smooth = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Ys));
                }
            }
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
            if (Ys.Length == 0 || isStreamData)
            {
                HashId = Guid.NewGuid().ToString();
                return;
            }
            using var sha256 = SHA256.Create();
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
                ApplySmoothing();
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
                ApplySmoothing();
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
            ApplySmoothing();
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
        private void ApplySmoothing()
        {
            if (originalYs == null || originalYs.Length == 0)
                return;
            if (Smooth <= 0)
            {
                _ys = originalYs.ToArray();
                return;
            }
            int[] windowSizes = { 7, 11, 15, 21 };
            int window = windowSizes[Math.Max(0, Math.Min(Smooth - 1, windowSizes.Length - 1))];
            if (window >= originalYs.Length) window = originalYs.Length | 1;
            if (window % 2 == 0) window += 1;
            if (window < 5) window = 5;
            int polyOrder = Math.Min(3, window - 2);
            try
            {
                _ys = SavitzkyGolaySmooth(originalYs, window, polyOrder);
            }
            catch { _ys = originalYs.ToArray(); }
        }

        /// <summary>
        /// 简单Savgol滤波实现
        /// </summary>
        private static double[] SavitzkyGolaySmooth(double[] y, int windowSize, int polyOrder)
        {
            // 计算卷积系数
            int half = windowSize / 2;
            var coeffs = SavitzkyGolayCoefficients(windowSize, polyOrder);
            double[] result = new double[y.Length];
            for (int i = 0; i < y.Length; i++)
            {
                double sum = 0;
                for (int j = -half; j <= half; j++)
                {
                    int idx = i + j;
                    if (idx < 0) idx = 0;
                    if (idx >= y.Length) idx = y.Length - 1;
                    sum += coeffs[j + half] * y[idx];
                }
                result[i] = sum;
            }
            return result;
        }
        // 计算Savgol卷积系数
        private static double[] SavitzkyGolayCoefficients(int windowSize, int polyOrder)
        {
            // 仅支持polyOrder=2或3，windowSize奇数
            // 这里用最常见的polyOrder=2的通用公式
            // 更高阶可用线性代数法求解
            int half = windowSize / 2;
            var a = new double[windowSize, polyOrder + 1];
            for (int i = -half; i <= half; i++)
                for (int j = 0; j <= polyOrder; j++)
                    a[i + half, j] = Math.Pow(i, j);
            // 正规方程 (A^T A) c = A^T e0
            var ata = new double[polyOrder + 1, polyOrder + 1];
            var at = new double[polyOrder + 1, windowSize];
            for (int i = 0; i <= polyOrder; i++)
                for (int j = 0; j < windowSize; j++)
                    at[i, j] = a[j, i];
            for (int i = 0; i <= polyOrder; i++)
                for (int j = 0; j <= polyOrder; j++)
                    for (int k = 0; k < windowSize; k++)
                        ata[i, j] += at[i, k] * a[k, j];
            // e0 = [1,0,0,...,0]^T
            var e0 = new double[windowSize];
            e0[half] = 1;
            // rhs = A^T e0
            var rhs = new double[polyOrder + 1];
            for (int i = 0; i <= polyOrder; i++)
                for (int k = 0; k < windowSize; k++)
                    rhs[i] += at[i, k] * e0[k];
            // 解正规方程
            var c = SolveLinear(ata, rhs);
            // 得到卷积系数
            var coeffs = new double[windowSize];
            for (int n = 0; n < windowSize; n++)
            {
                coeffs[n] = 0;
                for (int m = 0; m <= polyOrder; m++)
                    coeffs[n] += a[n, m] * c[m];
            }
            return coeffs;
        }
        // 高斯消元法解线性方程组
        private static double[] SolveLinear(double[,] a, double[] b)
        {
            int n = b.Length;
            var M = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    M[i, j] = a[i, j];
                M[i, n] = b[i];
            }
            for (int i = 0; i < n; i++)
            {
                // 主元
                int maxRow = i;
                for (int k = i + 1; k < n; k++)
                    if (Math.Abs(M[k, i]) > Math.Abs(M[maxRow, i]))
                        maxRow = k;
                for (int k = i; k < n + 1; k++)
                {
                    double tmp = M[maxRow, k];
                    M[maxRow, k] = M[i, k];
                    M[i, k] = tmp;
                }
                // 消元
                for (int k = i + 1; k < n; k++)
                {
                    double f = M[k, i] / M[i, i];
                    for (int j = i; j < n + 1; j++)
                        M[k, j] -= f * M[i, j];
                }
            }
            // 回代
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                x[i] = M[i, n] / M[i, i];
                for (int k = i - 1; k >= 0; k--)
                    M[k, n] -= M[k, i] * x[i];
            }
            return x;
        }
        // 获取平滑后的Ys
        public double[] GetSmoothedYs()
        {
            if (_ys == null || _ys.Length < 7 || Smooth <= 0)
                return _ys ?? Array.Empty<double>();
            // 窗口为数据长度的百分比
            double[] percent = { 0.01, 0.03, 0.05, 0.10 };
            int window = (int)Math.Round(_ys.Length * percent[Math.Max(0, Math.Min(Smooth - 1, percent.Length - 1))]);
            if (window % 2 == 0) window += 1;
            if (window < 5) window = 5;
            if (window >= _ys.Length) window = _ys.Length | 1;
            int polyOrder = Math.Min(3, window - 2);
            try { return SavitzkyGolaySmooth(_ys, window, polyOrder); }
            catch { return _ys; }
        }
        public string RawSourceFileName => _sourceFileName;
    }
} 