using System;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WindowHijacking
{
    class Program
    {
        #region Delegates

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        #endregion

        #region Imports

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetDC")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(IntPtr hwnd, out Rect lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        #endregion

        #region Enums

        enum GWL : int
        {
            WNDPROC = -4,
            HINSTANCE = -6,
            HWNDPARENT = -8,
            STYLE = -16,
            EXSTYLE = -20,
            USERDATA = -21,
            ID = -12
        }

        enum WS : long
        {
            //..
            VISIBLE = 0x10000000L
            //..
        }
        enum WS_EX : long
        {
            //..
            LAYERED = 0x00080000,
            TRANSPARENT = 0x00000020L
            //..
        }

        #endregion

        #region Structs

        // The Win32 RECT is not binary compatible with System.Drawing.Rectangle.
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;    // x position of upper-left corner
            public int Top;     // y position of upper-left corner
            public int Right;   // x position of lower-right corner
            public int Bottom;  // y position of lower-right corner
        }

        public struct WindowFinderParams
        {
            public string TitleContains;
            public bool CheckSize;
            public int MinWidth;
            public int MinHeight;
            public bool CheckAttributes;

            public WindowFinderParams(string title_contains = null, bool chech_size = false, int min_width = 0, int min_height = 0, bool check_attributes = false)
            {
                this.TitleContains = title_contains;
                this.CheckSize = chech_size;
                this.MinWidth = min_width;
                this.MinHeight = min_height;
                this.CheckAttributes = check_attributes;
            }
        }

        #endregion

        #region ConsoleExtensions

        static void Log(string text)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"[+] {text}");
            Console.ResetColor();
        }

        static void LogInfo(string info)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[?] {info}");
            Console.ResetColor();
        }

        static void LogDebug(string message)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[?] {message}");
            Console.ResetColor();
        }

        static void LogError(string error)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[!] {error}");
            Console.ResetColor();
        }

        #endregion

        #region WindowsExtensions

        static IEnumerable<IntPtr> FindUsableWindows(WindowFinderParams window_finder_params)
        {
            return FindWindows((hWnd, param) =>
            {
                var window_title = GetWindowText(hWnd);
                if (window_finder_params.TitleContains != string.Empty)
                    if (window_finder_params.TitleContains != null && !window_title.ToLower().Contains(window_finder_params.TitleContains.ToLower()))
                        return false;

                GetWindowRect(hWnd, out var lpRect);
                if (window_finder_params.CheckSize && (lpRect.Right < window_finder_params.MinWidth || lpRect.Bottom < window_finder_params.MinHeight))
                    return false;

                if (window_finder_params.CheckAttributes)
                {
                    var window_style = GetWindowLongPtr(hWnd, (int)GWL.STYLE);
                    if ((window_style.ToInt64() & (long)WS.VISIBLE) == 0)
                        return false;

                    var window_style_ex = GetWindowLongPtr(hWnd, (int)GWL.EXSTYLE);
                    if ((window_style_ex.ToInt64() & ((long)WS_EX.LAYERED | (long)WS_EX.TRANSPARENT)) == 0)
                        return false;
                }

            #if DEBUG
                var window_thread = GetWindowThreadProcessId(hWnd, out var lpdwProcessId);
                var window_process = Process.GetProcessById((int)lpdwProcessId);

                LogDebug($"hWnd: 0x{hWnd.ToInt32():X8} | thread: {window_thread} | process: [{window_process.Id}]({window_process.ProcessName}) | title: {window_title} | size: {lpRect.Right}x{lpRect.Bottom}");
            #endif

                return true;
            });
        }

        static IEnumerable<IntPtr> FindWindows(EnumWindowsProc filter)
        {
            var windows = new List<IntPtr>();

            EnumWindows(delegate (IntPtr hWnd, IntPtr param)
            {
                if (filter(hWnd, param))
                    windows.Add(hWnd);

                return true;
            }, IntPtr.Zero);

            return windows;
        }

        static string GetWindowText(IntPtr hWnd)
        {
            var size = GetWindowTextLength(hWnd);
            if (size <= 0)
                return "<none>";

            var buf = new StringBuilder(size + 1);
            GetWindowText(hWnd, buf, buf.Capacity);

            return buf.ToString();
        }

        #endregion

        static void Main(string[] args)
        {
            Log("searching usable windows...");

            var window_finder_params = new WindowFinderParams("editor");
            var hWnds = FindUsableWindows(window_finder_params).ToList();

            if (!hWnds.Any())
            {
                LogError("could not find any usable windows");
                goto EXIT;
            }

            if (hWnds.Count > 1)
            {
                LogInfo("skipped drawing, more than one window found");
                goto EXIT;
            }

            Log("attempting to draw stuff...");

            var hWnd = hWnds.FirstOrDefault();
            var hDc = GetDC(hWnd);

            var font = new Font("Tahoma", 26);
            var brush = new SolidBrush(Color.Magenta);

            var username = Environment.UserName;
            var next_alive_print = DateTime.Now;

            while (true)
            {
                Thread.Sleep(1);

                if (!IsWindow(hWnd))
                {
                    LogError("lost window");
                    break;
                }

                using (var graphics = Graphics.FromHdc(hDc))
                    graphics.DrawString($"Hello: {username}", font, brush, 5f, 5f);

                var current_datetime = DateTime.Now;
                if (DateTime.Compare(current_datetime, next_alive_print) < 0)
                    continue;

                LogInfo("drawing");

                next_alive_print = current_datetime.AddSeconds(5);
            }

        EXIT:
            Console.ReadKey();
        }
    }
}
