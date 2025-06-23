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
            var scatter = plot.Add.Scatter(
                curveInfo.ModifiedXs,
                curveInfo.GetSmoothedYs(),
                color: ColorUtils.ToScottPlotColor(curveInfo.PlotColor, curveInfo.Opacity));

            scatter.LegendText = curveInfo.Name;
            scatter.LineWidth = (float)curveInfo.Width;
            scatter.LinePattern = curveInfo.GetLinePattern();
            scatter.IsVisible = curveInfo.Visible;
            scatter.MarkerSize = (float)curveInfo.MarkerSize;

            logAction?.Invoke($"曲线属性: 线宽={curveInfo.Width}, 线型={curveInfo.LineStyle}, 可见性={curveInfo.Visible}, 标记大小={curveInfo.MarkerSize}");
            return scatter;
        }

        /// <summary>
        /// 重绘曲线
        /// </summary>
        /// <param name="plot">ScottPlot绘图对象</param>
        /// <param name="curveMap">曲线映射字典</param>
        /// <param name="curveInfo">曲线信息</param>
        /// <param name="logAction">日志记录委托</param>
        public static void RedrawCurve(ScottPlot.Plot plot, Dictionary<string, (CurveInfo Info, IPlottable Plot)> curveMap, CurveInfo curveInfo, Action<string>? logAction = null)
        {
            logAction?.Invoke($"重绘曲线: {curveInfo.Name}");
            if (curveMap.TryGetValue(curveInfo.HashId, out var curveData))
            {
                plot.Remove(curveData.Plot);
            }
            DrawCurve(plot, curveInfo, logAction);
            plot.Axes.AutoScale();
            logAction?.Invoke("重绘完成");
        }

        /// <summary>
        /// 重绘所有曲线
        /// </summary>
        /// <param name="plot">ScottPlot绘图对象</param>
        /// <param name="curveMap">曲线映射字典</param>
        /// <param name="curveInfos">曲线信息集合</param>
        /// <param name="logAction">日志记录委托</param>
        public static void RedrawAllCurves(ScottPlot.Plot plot, Dictionary<string, (CurveInfo Info, IPlottable Plot)> curveMap, IEnumerable<CurveInfo> curveInfos, Action<string>? logAction = null)
        {
            logAction?.Invoke("开始重绘所有曲线");
            plot.Clear();
            curveMap.Clear();
            foreach (var curveInfo in curveInfos)
            {
                DrawCurve(plot, curveInfo, logAction);
            }
            plot.Axes.AutoScale();
            logAction?.Invoke("重绘完成");
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
            plot.Axes.Bottom.Label.FontName = fontName;
            plot.Axes.Left.Label.FontName = fontName;
            plot.Axes.Bottom.Label.FontSize = (float)labelFontSize;
            plot.Axes.Left.Label.FontSize = (float)labelFontSize;

            plot.Axes.Bottom.TickLabelStyle.FontName = fontName;
            plot.Axes.Left.TickLabelStyle.FontName = fontName;
            plot.Axes.Bottom.TickLabelStyle.FontSize = (float)tickFontSize;
            plot.Axes.Left.TickLabelStyle.FontSize = (float)tickFontSize;
        }
    }
} 