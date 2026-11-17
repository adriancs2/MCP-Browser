using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MCP_Browser.Browser
{
    /// <summary>Win32 window-state helpers that WinForms does not expose.</summary>
    internal static class NativeMethods
    {
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const int WPF_RESTORETOMAXIMIZED = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>True when the window is really on screen (not hidden, not minimized).</summary>
        public static bool IsShown(Form form)
        {
            if (form == null || form.IsDisposed) return false;
            return IsWindowVisible(form.Handle) && !IsIconic(form.Handle);
        }

        /// <summary>
        /// Makes a hidden window visible. When the AI client starts this process with
        /// "start hidden" (STARTF_USESHOWWINDOW + SW_HIDE), Windows applies SW_HIDE to the
        /// process's first ShowWindow call, so Form.Show() leaves the window invisible.
        /// The second explicit call is honoured.
        /// </summary>
        public static void EnsureVisible(Form form)
        {
            if (form == null || form.IsDisposed) return;
            IntPtr hwnd = form.Handle;
            for (int i = 0; i < 2 && !IsWindowVisible(hwnd); i++)
                ShowWindow(hwnd, SW_SHOW);
        }
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        /// <summary>
        /// Restores (if minimized), raises and focuses the window. Windows refuses
        /// SetForegroundWindow from a process that is not already in the foreground,
        /// which is exactly our situation (the AI client is), so this thread's input
        /// queue is briefly attached to the foreground window's thread first.
        /// </summary>
        public static void ForceForeground(Form form)
        {
            if (form == null || form.IsDisposed) return;
            IntPtr hwnd = form.Handle;

            EnsureVisible(form);
            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);

            IntPtr fg = GetForegroundWindow();
            if (fg != hwnd)
            {
                uint current = GetCurrentThreadId();
                uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
                bool attached = fgThread != 0 && fgThread != current && AttachThreadInput(current, fgThread, true);
                try
                {
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                }
                finally
                {
                    if (attached) AttachThreadInput(current, fgThread, false);
                }

                // Last resort: at least put it on top of the z-order so it is visible.
                if (GetForegroundWindow() != hwnd)
                {
                    form.TopMost = true;
                    form.TopMost = false;
                }
            }

            form.Activate();
        }

        /// <summary>
        /// Brings a minimized window back on screen without activating it, so the
        /// user's current window keeps keyboard focus. Returns true when the window
        /// was minimized (the caller should give it a moment to repaint).
        /// </summary>
        public static bool RestoreIfMinimized(Form form)
        {
            if (form == null || form.IsDisposed) return false;
            IntPtr hwnd = form.Handle;
            if (!IsWindowVisible(hwnd))
            {
                EnsureVisible(form);
                return true;
            }
            if (!IsIconic(hwnd)) return false;

            // SW_SHOWNOACTIVATE always restores to the "normal" rectangle. A window
            // that was maximized before being minimized needs SW_RESTORE to come
            // back maximized.
            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
            bool wasMaximized = GetWindowPlacement(hwnd, ref wp) && (wp.flags & WPF_RESTORETOMAXIMIZED) != 0;

            ShowWindow(hwnd, wasMaximized ? SW_RESTORE : SW_SHOWNOACTIVATE);
            return true;
        }
    }
}
