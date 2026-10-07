using System.Drawing;

namespace NetStatus
{
    /// <summary>指标等级（CPU 温度 / 占用率用）：低=蓝色，中=橙色，高=红色。</summary>
    internal enum MetricLevel
    {
        Unknown = 0,
        Low = 1,
        Mid = 2,
        High = 3
    }

    internal static class MetricLevelInfo
    {
        public static Color ColorOf(MetricLevel level)
        {
            switch (level)
            {
                case MetricLevel.Low: return Color.FromArgb(56, 132, 246);   // 蓝：凉快
                case MetricLevel.Mid: return Color.FromArgb(249, 146, 12);   // 橙：中间
                case MetricLevel.High: return Color.FromArgb(239, 68, 68);   // 红：偏高
                default: return Color.FromArgb(145, 145, 150);               // 灰：读不到
            }
        }

        public static string TextOf(MetricLevel level)
        {
            switch (level)
            {
                case MetricLevel.Low: return "偏低";
                case MetricLevel.Mid: return "中等";
                case MetricLevel.High: return "偏高";
                default: return "不可用";
            }
        }
    }
}
