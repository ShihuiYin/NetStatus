using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NetStatus
{
    internal static class Program
    {
        private const string MutexName = "NetStatus.Tray.SingleInstance";

        [STAThread]
        internal static void Main()
        {
            bool createdNew;
            Mutex mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("网络延时监测已经在运行了，请看任务栏右下角的图标。", "网络延时监测",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(OnUnhandledException);
            Application.ThreadException += new ThreadExceptionEventHandler(OnThreadException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            AppSettings settings = AppSettings.Load(AppSettings.DefaultPath);
            Application.Run(new TrayApp(settings));

            GC.KeepAlive(mutex);
            mutex.Close();
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ReportError(e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportError(e.ExceptionObject as Exception);
        }

        private static void ReportError(Exception error)
        {
            string message = error == null ? "未知错误" : error.ToString();
            try
            {
                Directory.CreateDirectory(AppSettings.AppDataDir);
                File.AppendAllText(Path.Combine(AppSettings.AppDataDir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
                    message + Environment.NewLine + Environment.NewLine, new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }
    }
}
