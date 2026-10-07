using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace NetStatus
{
    /// <summary>
    /// 把位图转换成带 alpha 通道的图标句柄。
    /// 托盘图标是 16x16 的小图，圆角/抗锯齿都要靠 alpha，直接 GetHicon() 容易丢透明信息，
    /// 所以这里走 CreateIconIndirect（Windows 官方推荐做法）。
    /// </summary>
    internal static class IconFactory
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public int fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>创建图标句柄。调用方负责不再使用时调用 DestroyIcon。</summary>
        public static IntPtr CreateHIcon(Bitmap bitmap)
        {
            IntPtr color = IntPtr.Zero;
            IntPtr mask = IntPtr.Zero;
            try
            {
                color = bitmap.GetHbitmap(Color.FromArgb(0));
                mask = CreateEmptyMask(bitmap.Width, bitmap.Height);

                ICONINFO info = new ICONINFO();
                info.fIcon = 1;
                info.hbmMask = mask;
                info.hbmColor = color;

                IntPtr icon = CreateIconIndirect(ref info);
                if (icon != IntPtr.Zero) { return icon; }
            }
            catch (Exception)
            {
                // 落到下面的兜底方案
            }
            finally
            {
                if (color != IntPtr.Zero) { DeleteObject(color); }
                if (mask != IntPtr.Zero) { DeleteObject(mask); }
            }

            // 兜底：极端情况下仍能显示图标，只是透明边缘可能不够理想。
            return bitmap.GetHicon();
        }

        private static IntPtr CreateEmptyMask(int width, int height)
        {
            int stride = ((width + 31) / 32) * 4;
            byte[] bits = new byte[stride * height];
            return CreateBitmap(width, height, 1, 1, bits);
        }
    }
}
