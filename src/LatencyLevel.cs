using System.Drawing;

namespace NetStatus
{
    /// <summary>网络延时水平，决定任务栏图标的颜色。</summary>
    internal enum LatencyLevel
    {
        Unknown = 0,
        Fast = 1,
        Slow = 2,
        Bad = 3
    }

    internal static class LatencyLevelInfo
    {
        public static Color ColorOf(LatencyLevel level)
        {
            switch (level)
            {
                case LatencyLevel.Fast: return Color.FromArgb(34, 197, 94);
                case LatencyLevel.Slow: return Color.FromArgb(249, 146, 12);
                case LatencyLevel.Bad: return Color.FromArgb(239, 68, 68);
                default: return Color.FromArgb(145, 145, 150);
            }
        }

        public static string TextOf(LatencyLevel level)
        {
            switch (level)
            {
                case LatencyLevel.Fast: return "很快";
                case LatencyLevel.Slow: return "偏慢";
                case LatencyLevel.Bad: return "卡顿";
                default: return "检测中";
            }
        }
    }
}
