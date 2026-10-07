using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace NetStatus
{
    /// <summary>
    /// 绘制任务栏图标：带底色的圆角方块 + 数值 +（可选）单位，或者一个纯色圆点。
    ///
    /// 托盘图标很小（100% 缩放 16x16，150% 缩放 24x24），所以字号要榨干每一像素：
    /// 按“字形墨迹高度”而不是“字体行高”来定字号（数字没有下伸部，用行高会白白浪费四分之一的竖向空间），
    /// 再按可用宽度自动缩小。比例常数来自实测：数字墨迹高约 0.72em、两位数字宽约 1.14em，
    /// 小写 “ms” 墨迹高约 0.50em。
    /// </summary>
    internal static class TrayIconRenderer
    {
        private static readonly FontFamily UiFont = ResolveFontFamily();

        private const float DigitInkRatio = 0.72f;   // 数字 / 大写字母 / °C / %
        private const float LowerInkRatio = 0.50f;   // 小写字母 ms
        private const float ThinInkRatio = 0.50f;    // × 和 --

        private const float NumberInkHeight = 0.50f;      // 有单位时，数字墨迹占图标高度比例
        private const float NumberInkHeightAlone = 0.64f; // 没单位时，数字可以更大
        private const float UnitInkHeight = 0.22f;        // 单位墨迹占图标高度比例
        private const float NumberCenterY = 0.37f;        // 数字墨迹中心（有单位时）
        private const float UnitCenterY = 0.76f;          // 单位墨迹中心
        private const float WidthBudget = 0.86f;          // 文字最多占图标宽度的比例

        public static Bitmap Render(int width, int height, Color color, string text, string unit)
        {
            if (width < 8) { width = 8; }
            if (height < 8) { height = 8; }

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                if (string.IsNullOrEmpty(text)) { DrawDot(g, width, height, color); }
                else if (text == "--") { DrawBars(g, width, height); }
                else { DrawBadge(g, width, height, color, text, unit); }
            }
            return bitmap;
        }

        private static void DrawDot(Graphics g, int width, int height, Color color)
        {
            float pad = width * 0.17f;
            RectangleF rect = new RectangleF(pad, pad, width - pad * 2f, height - pad * 2f);
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, rect);
            }
            using (SolidBrush highlight = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
            {
                g.FillEllipse(highlight, rect.X + rect.Width * 0.20f, rect.Y + rect.Height * 0.16f,
                    rect.Width * 0.34f, rect.Height * 0.34f);
            }
        }

        /// <summary>“--”（暂时拿不到数据）用两根横杠画，位置比字形更可控。</summary>
        private static void DrawBars(Graphics g, int width, int height)
        {
            float barHeight = Math.Max(2f, height * 0.13f);
            float barWidth = width * 0.32f;
            float gap = width * 0.13f;
            float left = (width - (barWidth * 2f + gap)) / 2f;
            float top = (height - barHeight) / 2f;
            using (SolidBrush brush = new SolidBrush(Color.White))
            using (GraphicsPath first = RoundedRect(new RectangleF(left, top, barWidth, barHeight), barHeight / 2f))
            using (GraphicsPath second = RoundedRect(new RectangleF(left + barWidth + gap, top, barWidth, barHeight), barHeight / 2f))
            {
                g.FillPath(brush, first);
                g.FillPath(brush, second);
            }
        }

        private static void DrawBadge(Graphics g, int width, int height, Color color, string text, string unit)
        {
            RectangleF bounds = new RectangleF(0.5f, 0.5f, width - 1f, height - 1f);
            using (GraphicsPath path = RoundedRect(bounds, width * 0.22f))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }

            bool hasUnit = !string.IsNullOrEmpty(unit);
            float widthBudget = width * WidthBudget;

            DrawCenteredText(g, text, width,
                hasUnit ? height * NumberCenterY : height * 0.5f,
                hasUnit ? height * NumberInkHeight : height * NumberInkHeightAlone,
                widthBudget, InkRatioOf(text), Color.White);

            if (hasUnit)
            {
                DrawCenteredText(g, unit, width, height * UnitCenterY, height * UnitInkHeight,
                    widthBudget, InkRatioOf(unit), Color.FromArgb(230, 255, 255, 255));
            }
        }

        /// <summary>把文字按“墨迹高度”定字号并画在指定的竖直中心位置。</summary>
        private static void DrawCenteredText(Graphics g, string text, int width, float centerY,
            float inkHeight, float maxWidth, float inkRatio, Color color)
        {
            float em = inkHeight / inkRatio;
            using (Font font = FitFont(g, text, maxWidth, em))
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = format.FormatFlags | StringFormatFlags.NoWrap;

                // 行盒比字形本身高，按行盒居中，字形也就落在 centerY 附近
                float boxHeight = em * 1.35f;
                RectangleF rect = new RectangleF(0f, centerY - boxHeight / 2f, width, boxHeight);
                g.DrawString(text, font, brush, rect, format);
            }
        }

        private static float InkRatioOf(string text)
        {
            if (string.IsNullOrEmpty(text)) { return DigitInkRatio; }
            if (text == "×" || text == "--" || text == "—") { return ThinInkRatio; }
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9') { return DigitInkRatio; }
            }
            // 纯小写（ms）以外的都按大写处理（°C / %）
            bool hasUpper = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= 'A' && c <= 'Z') { hasUpper = true; }
            }
            return hasUpper ? DigitInkRatio : LowerInkRatio;
        }

        /// <summary>宽度放不下时逐步缩小字号。</summary>
        private static Font FitFont(Graphics g, string text, float maxWidth, float startSize)
        {
            float size = startSize;
            float minSize = Math.Max(4f, startSize * 0.45f);

            Font font = new Font(UiFont, size, FontStyle.Bold, GraphicsUnit.Pixel);
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                while (size > minSize)
                {
                    SizeF measured = g.MeasureString(text, font, new SizeF(1000f, 1000f), format);
                    if (measured.Width <= maxWidth) { break; }
                    size -= 0.5f;
                    font.Dispose();
                    font = new Font(UiFont, size, FontStyle.Bold, GraphicsUnit.Pixel);
                }
            }
            return font;
        }

        private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2f;
            if (diameter <= 0.6f || diameter > bounds.Width || diameter > bounds.Height)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private static FontFamily ResolveFontFamily()
        {
            string[] candidates = new string[] { "Segoe UI", "Microsoft YaHei UI", "Tahoma", "Arial" };
            for (int i = 0; i < candidates.Length; i++)
            {
                try { return new FontFamily(candidates[i]); }
                catch (ArgumentException) { }
            }
            return FontFamily.GenericSansSerif;
        }
    }
}
