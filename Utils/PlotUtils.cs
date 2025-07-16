using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ScottPlot;
using QuquPlot.Models;
using QuquPlot.Utils;

namespace QuquPlot.Utils
{
    public static class PlotUtils
    {
        private static int _colorIndex = 0;

        /// <summary>
        /// 获取下一个可用的颜色
        /// </summary>
        /// <param name="usedColors">已使用的颜色列表</param>
        /// <returns>下一个可用的颜色</returns>
        public static ScottPlot.Color GetNextColor(IEnumerable<System.Windows.Media.Color> usedColors)
        {
            var usedColorsList = usedColors.ToList();
            // 先尝试找未用过的色盘颜色
            foreach (var hex in ColorUtils.ColorPalette)
            {
                var color = ColorUtils.ColorFromHex(hex);
                if (!usedColorsList.Any(uc => uc.R == color.R && uc.G == color.G && uc.B == color.B))
                    return color;
            }
            // 如果都用过了，循环使用色盘
            var hex2 = ColorUtils.ColorPalette[_colorIndex % ColorUtils.ColorPalette.Length];
            _colorIndex++;
            return ColorUtils.ColorFromHex(hex2);
        }

        /// <summary>
        /// 绘制曲线
        /// </summary>
        /// <param name="plot">ScottPlot绘图对象</param>
        /// <param name="curveInfo">曲线信息</param>
        /// <param name="logAction">日志记录委托</param>
        /// <returns>绘制的散点图对象</returns>
        public static ScottPlot.Plottables.Scatter DrawCurve(ScottPlot.Plot plot, CurveInfo curveInfo, Action<string>? logAction = null)
        {
            logAction?.Invoke($"绘制曲线: {curveInfo.Name}");
            // // 选择Y轴
            var yAxis = curveInfo.Y2 ? plot.Axes.Right : plot.Axes.Left;
            var xAxis = plot.Axes.Bottom;
            // 如果Y2且右轴不存在，则添加
            // if (curveInfo.Y2 && plot.Axes.Right == null)
            // {
            //     plot.Axes.AddRightAxis();
            //     plot.Axes.Right.Label.Text = "Y2";
            // }
            var scatter = plot.Add.Scatter(
                curveInfo.ModifiedXs,
                curveInfo.GetSmoothedYs(),
                color: ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity)
            );
            scatter.LegendText = curveInfo.Name;
            scatter.LineWidth = (float)curveInfo.Width;
            scatter.LinePattern = curveInfo.GetLinePattern();
            scatter.IsVisible = curveInfo.Visible;
            scatter.MarkerSize = (float)curveInfo.MarkerSize;
            scatter.Axes.YAxis = yAxis;
            logAction?.Invoke($"曲线属性: 线宽={curveInfo.Width}, 线型={curveInfo.LineStyle}, 可见性={curveInfo.Visible}, 标记大小={curveInfo.MarkerSize}, Y2={curveInfo.Y2}");
            return scatter;
        }


        /// <summary>
        /// 创建散点图对象
        /// </summary>
        /// <param name="plot">ScottPlot绘图对象</param>
        /// <param name="xs">X轴数据</param>
        /// <param name="ys">Y轴数据</param>
        /// <param name="color">颜色</param>
        /// <param name="opacity">透明度</param>
        /// <param name="lineWidth">线宽</param>
        /// <param name="markerSize">标记大小</param>
        /// <param name="legendText">图例文本</param>
        /// <param name="isVisible">是否可见</param>
        /// <returns>散点图对象</returns>
        public static ScottPlot.Plottables.Scatter CreateScatter(ScottPlot.Plot plot, double[] xs, double[] ys, ScottPlot.Color color, double opacity, double lineWidth, double markerSize, string legendText, bool isVisible)
        {
            var scatter = plot.Add.Scatter(xs, ys);
            scatter.Color = color;
            scatter.LineWidth = (float)lineWidth;
            scatter.MarkerSize = (float)markerSize;
            scatter.LegendText = legendText;
            scatter.IsVisible = isVisible;
            return scatter;
        }

        /// <summary>
        /// 更新散点图属性
        /// </summary>
        /// <param name="scatter">散点图对象</param>
        /// <param name="curveInfo">曲线信息</param>
        public static void UpdateScatterProperties(ScottPlot.Plottables.Scatter scatter, CurveInfo curveInfo)
        {
            scatter.Color = ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity);
            scatter.LineWidth = (float)curveInfo.Width;
            scatter.LinePattern = curveInfo.GetLinePattern();
            scatter.IsVisible = curveInfo.Visible;
            scatter.MarkerSize = (float)curveInfo.MarkerSize;
            scatter.LegendText = curveInfo.Name;
        }

        /// <summary>
        /// 设置坐标轴标签
        /// </summary>
        /// <param name="plot">ScottPlot绘图对象</param>
        /// <param name="xLabel">X轴标签</param>
        /// <param name="yLabel">Y轴标签</param>
        public static void SetAxisLabels(ScottPlot.Plot plot, string xLabel, string yLabel)
        {
            plot.XLabel(xLabel);
            plot.YLabel(yLabel);
        }

        /// <summary>
        /// 设置图例样式
        /// </summary>
        /// <param name="plot">ScottPlot绘图对象</param>
        /// <param name="fontSize">字体大小</param>
        /// <param name="backgroundColor">背景颜色</param>
        /// <param name="alignment">对齐方式</param>
        public static void SetLegendStyle(ScottPlot.Plot plot, double fontSize, ScottPlot.Color backgroundColor, ScottPlot.Alignment alignment = ScottPlot.Alignment.LowerLeft)
        {
            plot.Legend.FontSize = (float)fontSize;
            plot.Legend.BackgroundColor = backgroundColor;
            plot.Legend.ShadowColor = ColorUtils.ColorFromHex("#FFFFFF", 0.0);
            plot.Legend.OutlineColor = ColorUtils.ColorFromHex("#FFFFFF", 0.0);
            plot.Legend.Alignment = alignment;
            plot.Legend.SymbolWidth = 50;
            plot.Legend.SymbolPadding = 10;
        }

        /// <summary>
        /// 设置坐标轴字体
        /// </summary>
        /// <param name="plot">ScottPlot绘图对象</param>
        /// <param name="labelFontSize">标签字体大小</param>
        /// <param name="tickFontSize">刻度字体大小</param>
        /// <param name="fontName">字体名称</param>
        public static void SetAxisFonts(ScottPlot.Plot plot, double labelFontSize, double tickFontSize, string fontName = "Microsoft YaHei UI")
        {
            plot.Axes.Bottom.FrameLineStyle.Width = 2;
            plot.Axes.Left.FrameLineStyle.Width = 2;
            plot.Axes.Right.FrameLineStyle.Width = 2;
            plot.Axes.Top.FrameLineStyle.Width = 2;

            plot.Axes.Bottom.Label.FontName = fontName;
            plot.Axes.Left.Label.FontName = fontName;
            plot.Axes.Right.Label.FontName = fontName;

            plot.Axes.Right.Label.IsVisible = false;
            plot.Axes.Right.Label.Rotation = (float)-90;

            plot.Axes.Bottom.Label.FontSize = (float)labelFontSize;
            plot.Axes.Left.Label.FontSize = (float)labelFontSize;
            plot.Axes.Right.Label.FontSize = (float)labelFontSize;

            plot.Axes.Bottom.Label.OffsetY = (float)10;
            plot.Axes.Left.Label.OffsetX = (float)-25;
            plot.Axes.Right.Label.OffsetX = (float)-10;

            plot.Axes.Bottom.TickLabelStyle.FontName = fontName;
            plot.Axes.Left.TickLabelStyle.FontName = fontName;
            plot.Axes.Right.TickLabelStyle.FontName = fontName;

            plot.Axes.Bottom.TickLabelStyle.OffsetY = (float)5;
            plot.Axes.Left.TickLabelStyle.OffsetX = (float)-5;
            plot.Axes.Right.TickLabelStyle.OffsetX = (float)5;

            plot.Axes.Bottom.TickLabelStyle.FontSize = (float)tickFontSize;
            plot.Axes.Left.TickLabelStyle.FontSize = (float)tickFontSize;
            plot.Axes.Right.TickLabelStyle.FontSize = (float)tickFontSize;


            plot.Axes.Bottom.MajorTickStyle.Length = 10;
            plot.Axes.Left.MajorTickStyle.Length = 10;
            plot.Axes.Right.MajorTickStyle.Length = 10;

            plot.Axes.Bottom.MinorTickStyle.Length = 8;
            plot.Axes.Left.MinorTickStyle.Length = 8;
            plot.Axes.Right.MinorTickStyle.Length = 8;

            plot.Axes.Bottom.MajorTickStyle.Width = 2;
            plot.Axes.Left.MajorTickStyle.Width = 2;
            plot.Axes.Right.MajorTickStyle.Width = 2;

            plot.Axes.Bottom.MinorTickStyle.Width = (float)1.5;
            plot.Axes.Left.MinorTickStyle.Width = (float)1.5;
            plot.Axes.Right.MinorTickStyle.Width = (float)1.5;

            ScottPlot.TickGenerators.NumericAutomatic tickGenX = new();
            tickGenX.LabelFormatter = value => MainWindow.FormatNumber(value);
            tickGenX.MinimumTickSpacing = 100;
            tickGenX.TickDensity = 0.5;
            plot.Axes.Bottom.TickGenerator = tickGenX;

            ScottPlot.TickGenerators.NumericAutomatic tickGenY = new();
            tickGenY.LabelFormatter = value => MainWindow.FormatNumber(value);
            tickGenY.MinimumTickSpacing = 100;
            tickGenY.TickDensity = 0.5;
            plot.Axes.Left.TickGenerator = tickGenY;

            ScottPlot.TickGenerators.NumericAutomatic tickGenY2 = new();
            tickGenY2.LabelFormatter = value => MainWindow.FormatNumber(value);
            tickGenY2.MinimumTickSpacing = 100;
            tickGenY2.TickDensity = 0.5;
            plot.Axes.Right.TickGenerator = tickGenY2;

            // plot.Axes.Left.TickGenerator.MaxTickCount = 10;
            // plot.Axes.Right.TickGenerator.MaxTickCount = 10;
            // plot.Axes.Bottom.TickGenerator.MaxTickCount = 10;

            // plot.Axes.Left.TickLabelFormatter = value => MainWindow.FormatNumber(value);
            // plot.Axes.Right.TickLabelFormatter = value => MainWindow.FormatNumber(value);
        }
    }
} 