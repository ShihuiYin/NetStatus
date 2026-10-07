using System;
using System.Drawing;
using System.Windows.Forms;

namespace NetStatus
{
    /// <summary>一个托盘图标的位置，负责图标句柄的创建与释放。</summary>
    internal sealed class TrayIconSlot : IDisposable
    {
        private readonly NotifyIcon _icon = new NotifyIcon();
        private IntPtr _handle = IntPtr.Zero;

        public void Attach(ContextMenuStrip menu, MouseEventHandler onMouseUp, EventHandler onDoubleClick)
        {
            _icon.ContextMenuStrip = menu;
            _icon.MouseUp += onMouseUp;
            _icon.DoubleClick += onDoubleClick;
        }

        public void SetIcon(Color color, string text, string unit)
        {
            Size size = SystemInformation.SmallIconSize;
            int width = size.Width > 0 ? size.Width : 16;
            int height = size.Height > 0 ? size.Height : width;

            Bitmap bitmap = TrayIconRenderer.Render(width, height, color, text, unit);
            IntPtr handle = IconFactory.CreateHIcon(bitmap);
            bitmap.Dispose();
            if (handle == IntPtr.Zero) { return; }

            Icon previous = _icon.Icon;
            _icon.Icon = Icon.FromHandle(handle);
            if (previous != null) { previous.Dispose(); }

            IntPtr old = _handle;
            _handle = handle;
            if (old != IntPtr.Zero) { IconFactory.DestroyIcon(old); }
        }

        public void SetText(string text)
        {
            if (text == null) { text = ""; }
            if (text.Length > 62) { text = text.Substring(0, 62); }
            _icon.Text = text;
        }

        public void ShowBalloon(int timeoutMs, string title, string text, ToolTipIcon icon)
        {
            _icon.ShowBalloonTip(timeoutMs, title, text, icon);
        }

        public void Show(bool visible)
        {
            _icon.Visible = visible;
        }

        public void Dispose()
        {
            try { _icon.Visible = false; }
            catch (Exception) { }
            if (_handle != IntPtr.Zero)
            {
                IconFactory.DestroyIcon(_handle);
                _handle = IntPtr.Zero;
            }
            _icon.Dispose();
        }
    }
}
