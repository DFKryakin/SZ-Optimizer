using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace SZOptimizer
{
    public class CrosshairOverlay : IDisposable
    {
        private IntPtr _hwnd;
        private bool _visible;
        private readonly ManualResetEventSlim _ready = new(false);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX { public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; public IntPtr hIconSm; }
        [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int CX, CY; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate? _wndProc;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? mod);

        public CrosshairOverlay()
        {
            var t = new Thread(() =>
            {
                IntPtr hInst = GetModuleHandle(null);
                string cls = "SZCrossLight_" + Guid.NewGuid().ToString("N");
                _wndProc = WndProc;
                var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc), hInstance = hInst, lpszClassName = cls };
                RegisterClassEx(ref wc);
                _hwnd = CreateWindowEx(0x08080028, cls, "", 0x80000000, 0, 0, 2, 2, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
                _ready.Set();
                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0)) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            _ready.Wait(1000);
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == 0x0084) return new IntPtr(-1);
            if (msg == 0x0010) { PostQuitMessage(0); return IntPtr.Zero; }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Show() { if (_hwnd != IntPtr.Zero && !_visible) { ShowWindow(_hwnd, 8); _visible = true; } }
        public void Hide() { if (_hwnd != IntPtr.Zero && _visible) { ShowWindow(_hwnd, 0); _visible = false; } }
        public void SetHidden(bool hidden) { if (hidden) Hide(); else Show(); }
        public void Close() => Dispose();
        public void Dispose() { if (_hwnd != IntPtr.Zero) { PostMessage(_hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); _hwnd = IntPtr.Zero; } }
        public void Render(string preset, Color color, double scale, int offsetX, int offsetY, List<bool> pixels, bool hidden)
        {
            if (_hwnd == IntPtr.Zero) return;
            if (hidden) { Hide(); return; }

            int sz = Math.Max(48, (int)Math.Ceiling(48 * scale) + 8);
            if (sz % 2 != 0) sz++;
            double mid = sz / 2.0, s = scale, t = Math.Max(0.85, 0.95 * s);
            byte[] alpha = new byte[sz * sz];

            void Plot(int x, int y, double a) { if (x >= 0 && x < sz && y >= 0 && y < sz && a > 0) { byte v = (byte)Math.Min(255, Math.Round(a * 255)); int idx = y * sz + x; if (v > alpha[idx]) alpha[idx] = v; } }
            void Dot(double r) { double rad = Math.Max(1.0, (r * s) / 2.0); for (int y = 0; y < sz; y++) for (int x = 0; x < sz; x++) { double d = Math.Sqrt(Math.Pow(x + 0.5 - mid, 2) + Math.Pow(y + 0.5 - mid, 2)); Plot(x, y, rad + 0.6 - d); } }
            void Ln(double x1, double y1, double x2, double y2) { double ax = mid + x1 * s, ay = mid + y1 * s, bx = mid + x2 * s, by = mid + y2 * s, dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy; for (int y = 0; y < sz; y++) for (int x = 0; x < sz; x++) { double px = x + 0.5, py = y + 0.5, u = len2 > 0 ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / len2)) : 0; double d = Math.Sqrt(Math.Pow(px - (ax + u * dx), 2) + Math.Pow(py - (ay + u * dy), 2)); Plot(x, y, t + 0.6 - d); } }
            void Ring(double r, double a1 = -999, double a2 = 999) { double rad = r * s; for (int y = 0; y < sz; y++) for (int x = 0; x < sz; x++) { double dx = x + 0.5 - mid, dy = y + 0.5 - mid, d = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - rad); if (d < t + 0.8) { double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI; if (a1 < -500 || (ang >= a1 && ang <= a2)) Plot(x, y, t + 0.6 - d); } } }

            switch (preset)
            {
                case "Dot-2x2": Dot(2.2); break;
                case "Dot-4x4": Dot(4.0); break;
                case "Tiny-cross": Ln(-4, 0, 4, 0); Ln(0, -4, 0, 4); break;
                case "Cross-dot-small": Dot(2.0); Ln(-8, 0, -3.5, 0); Ln(3.5, 0, 8, 0); Ln(0, -8, 0, -3.5); Ln(0, 3.5, 0, 8); break;
                case "Cross-dot": Dot(2.0); Ln(-12, 0, -4, 0); Ln(4, 0, 12, 0); Ln(0, -12, 0, -4); Ln(0, 4, 0, 12); break;
                case "T-shape-dot": Dot(2.0); Ln(-11, 0, -4, 0); Ln(4, 0, 11, 0); Ln(0, 4, 0, 11); break;
                case "T-shape-chevron": Ln(-11, 0, -5, 0); Ln(5, 0, 11, 0); Ln(-3, 2, 0, -1); Ln(0, -1, 3, 2); Ln(0, 4, 0, 10); break;
                case "Chevron-small": Ln(-5, 3, 0, -2); Ln(0, -2, 5, 3); break;
                case "Chevron-medium": Ln(-8, 5, 0, -3); Ln(0, -3, 8, 5); break;
                case "Circle-dot": Dot(2.2); Ring(8); break;
                case "Circle-cross": Ring(7); Ln(-12, 0, -7, 0); Ln(7, 0, 12, 0); Ln(0, -12, 0, -7); Ln(0, 7, 0, 12); break;
                case "Cross-diag-dot": Dot(2.0); Ln(-8, -8, -3.5, -3.5); Ln(3.5, 3.5, 8, 8); Ln(8, -8, 3.5, -3.5); Ln(-3.5, 3.5, -8, 8); break;
                case "Square-cross-dot": Dot(2.0); Ln(-9, -9, -5, -9); Ln(-9, -9, -9, -5); Ln(5, -9, 9, -9); Ln(9, -9, 9, -5); Ln(-9, 9, -5, 9); Ln(-9, 5, -9, 9); Ln(5, 9, 9, 9); Ln(9, 5, 9, 9); break;
                case "2-sectors-dot": Dot(2.2); Ring(9, -40, 40); Ring(9, 140, 180); Ring(9, -180, -140); break;
                case "Horiz-dot": Dot(2.2); Ln(-12, 0, -4.5, 0); Ln(4.5, 0, 12, 0); break;
                case "PIXEL": if (pixels.Count >= 256) for (int i = 0; i < 256; i++) if (pixels[i]) { int x0 = (int)Math.Round(mid + ((i % 16) - 8) * s), y0 = (int)Math.Round(mid + ((i / 16) - 8) * s), st = Math.Max(1, (int)Math.Round(s)); for (int dy = 0; dy < st; dy++) for (int dx = 0; dx < st; dx++) Plot(x0 + dx, y0 + dy, 1.0); } break;
            }

            int minX = sz, minY = sz, maxX = -1, maxY = -1;
            for (int y = 0; y < sz; y++) for (int x = 0; x < sz; x++) if (alpha[y * sz + x] > 0) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (maxX < minX) { minX = sz / 2; maxX = minX; minY = sz / 2; maxY = minY; }

            int wOut = maxX - minX + 1, hOut = maxY - minY + 1;
            int[] buf = new int[wOut * hOut];
            for (int y = 0; y < hOut; y++) for (int x = 0; x < wOut; x++) { uint a = alpha[(minY + y) * sz + (minX + x)]; if (a > 0) { uint r = (color.R * a) / 255, g = (color.G * a) / 255, b = (color.B * a) / 255; buf[y * wOut + x] = unchecked((int)((a << 24) | (r << 16) | (g << 8) | b)); } }

            IntPtr memDc = CreateCompatibleDC(IntPtr.Zero);
            var bmi = new BITMAPINFOHEADER { biSize = 40, biWidth = wOut, biHeight = -hOut, biPlanes = 1, biBitCount = 32 };
            IntPtr hBmp = CreateDIBSection(memDc, ref bmi, 0, out IntPtr bitsPtr, IntPtr.Zero, 0);
            IntPtr oldBmp = SelectObject(memDc, hBmp);
            Marshal.Copy(buf, 0, bitsPtr, buf.Length);

            var ptDst = new POINT { X = (int)((SystemParameters.PrimaryScreenWidth - sz) / 2.0) + minX + offsetX, Y = (int)((SystemParameters.PrimaryScreenHeight - sz) / 2.0) + minY + offsetY };
            var size = new SIZE { CX = wOut, CY = hOut };
            var ptSrc = new POINT { X = 0, Y = 0 };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };

            UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, 2);
            Show();
            SelectObject(memDc, oldBmp); DeleteObject(hBmp); DeleteDC(memDc);
        }
    }
}
