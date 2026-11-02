using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SampleViewer.Utils
{
    /// <summary>
    /// 複数ディスプレイ環境で、基準ウィンドウと同じディスプレイの中央にウィンドウを配置する。
    /// WPFのLeft/Topはメインディスプレイ基準のDIP座標のため、サブディスプレイ（負座標・DPI違い）では
    /// ずれる。ここではディスプレイの作業領域をピクセルで取得し、Win32 APIで直接配置する。
    /// </summary>
    public static class WindowPlacement
    {
        public static void CenterOnSameMonitor(Window target, Window baseWindow)
        {
            var baseHandle = new WindowInteropHelper(baseWindow).Handle;
            if (baseHandle == IntPtr.Zero)
                return;

            var monitor = MonitorFromWindow(baseHandle, MONITOR_DEFAULTTONEAREST);

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info))
                return;

            var work = info.rcWork;

            target.WindowStartupLocation = WindowStartupLocation.Manual;

            // ハンドル生成直後（表示前）に配置する
            target.SourceInitialized += (_, _) => Place(target, work);
        }

        private static void Place(Window target, RECT work)
        {
            var hwnd = new WindowInteropHelper(target).Handle;

            // まず対象ディスプレイへ移動する（DPIが違えばここでWPFがサイズを再計算する）
            SetWindowPos(hwnd, IntPtr.Zero, work.Left, work.Top, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

            if (!GetWindowRect(hwnd, out var rect))
                return;

            int workWidth = work.Right - work.Left;
            int workHeight = work.Bottom - work.Top;

            // 作業領域より大きい場合は収まるように縮める
            int width = Math.Min(rect.Right - rect.Left, workWidth);
            int height = Math.Min(rect.Bottom - rect.Top, workHeight);

            int x = work.Left + (workWidth - width) / 2;
            int y = work.Top + (workHeight - height) / 2;

            SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        #region Win32

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        #endregion
    }
}
