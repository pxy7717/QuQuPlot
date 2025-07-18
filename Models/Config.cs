using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace QuquPlot.Models
{
    // 用于序列化CurveInfo的必要属性
    public class CurveConfigSerializable
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

    public class AppSettingsSerializable
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

    public class ProjectConfigSerializable
    {
        public AppSettingsSerializable AppSettings { get; set; } = new();
        public List<CurveConfigSerializable> Curves { get; set; } = new();
    }
} 