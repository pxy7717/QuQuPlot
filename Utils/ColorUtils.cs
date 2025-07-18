using ScottPlot;

namespace QuquPlot.Utils
{
    public static class ColorUtils
    {
        public static readonly string[] ColorPalette = {
            "#D32F2F", "#FF5252", "#FF8A80", "#1976D2", "#448AFF", "#82B1FF",
            "#00C853", "#69F0AE", "#B9F6CA", "#9C27B0", "#CE93D8", "#E1BEE7",
            "#FBC02D", "#FFEB3B", "#FFF59D", "#0097A7", "#4DD0E1", "#B2EBF2",
            "#F57C00", "#FFB74D", "#FFE0B2", "#C2185B", "#F48FB1", "#F8BBD0",
            "#5D4037", "#A1887F", "#D7CCC8", "#303F9F", "#7986CB", "#C5CAE9",
            "#212121", "#616161", "#9E9E9E", "#BDBDBD", "#EEEEEE", "#FAFAFA"
        };

        public const int ColorBlockSize = 25;  // 色块尺寸
        public const int ColorPanelWidth = 150;  // 调色板宽度 (6 * 25)
        public const int ColorPanelHeight = 150;  // 调色板高度 (6 * 25)

        /// <summary>
        /// 将hex字符串转为ScottPlot.Color
        /// </summary>
        /// <param name="hex">十六进制颜色字符串，如 "#FF0000"</param>
        /// <param name="alpha">透明度，0-1</param>
        /// <returns>ScottPlot.Color对象</returns>
        public static Color ColorFromHex(string hex, double alpha = 1.0)
        {
            hex = hex.Replace("#", "");
            byte a = (byte)(255 * alpha);
            int start = 0;
            if (hex.Length == 8) { a = Convert.ToByte(hex.Substring(0, 2), 16); start = 2; }
            byte r = Convert.ToByte(hex.Substring(start, 2), 16);
            byte g = Convert.ToByte(hex.Substring(start + 2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(start + 4, 2), 16);
            // Convert byte values (0-255) to float values (0-1) for ScottPlot.Color
            return new Color(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
        }

        /// <summary>
        /// 将WPF颜色转换为ScottPlot颜色
        /// </summary>
        /// <param name="wpfColor">WPF颜色</param>
        /// <param name="opacity">透明度，0-1</param>
        /// <returns>ScottPlot.Color对象</returns>
        public static Color ToScottPlotColor(System.Windows.Media.Color wpfColor, double opacity = 1.0)
        {
            return new Color(
                wpfColor.R / 255.0f,
                wpfColor.G / 255.0f,
                wpfColor.B / 255.0f,
                (float)opacity
            );
        }
    }
} 