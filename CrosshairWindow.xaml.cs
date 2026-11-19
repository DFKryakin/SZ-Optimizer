using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SZOptimizer
{
    public partial class CrosshairWindow : Window
    {
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags); [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);
        [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

        private IntPtr _hwnd = IntPtr.Zero;

        public CrosshairWindow() => InitializeComponent();

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(_hwnd, -16, unchecked((int)0x80000000));
            SetWindowLong(_hwnd, -20, GetWindowLong(_hwnd, -20) | 0x00000020 | 0x00000080);
            SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010);
        }

        public void RenderCrosshair(string preset, Color color, double scale, int offsetX, int offsetY, List<bool> pixels, string? pngPath)
        {
            if (_hwnd == IntPtr.Zero) return;
            this.Left = (SystemParameters.PrimaryScreenWidth - 64) / 2.0 + offsetX;
            this.Top = (SystemParameters.PrimaryScreenHeight - 64) / 2.0 + offsetY;

            RootGrid.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            IntPtr fullRgn = CreateRectRgn(0, 0, 0, 0);

            void AddRect(int x, int y, int w, int h)
            {
                int sx = (int)Math.Round(32 + (x - 32) * scale);
                int sy = (int)Math.Round(32 + (y - 32) * scale);
                int sw = Math.Max(1, (int)Math.Round(w * scale));
                int sh = Math.Max(1, (int)Math.Round(h * scale));
                IntPtr r = CreateRectRgn(sx, sy, sx + sw, sy + sh);
                CombineRgn(fullRgn, fullRgn, r, 2);
                DeleteObject(r);
            }

            if (preset == "PNG" && !string.IsNullOrEmpty(pngPath) && System.IO.File.Exists(pngPath))
            {
                CustomImage.Visibility = Visibility.Visible;
                CustomImage.Source = new BitmapImage(new Uri(pngPath));
                AddRect(16, 16, 32, 32);
            }
            else
            {
                CustomImage.Visibility = Visibility.Collapsed;
                switch (preset)
                {
                    case "Dot-2x2": AddRect(31, 31, 2, 2); break;
                    case "Dot-4x4": AddRect(30, 30, 4, 4); break;
                    case "Tiny-cross": AddRect(28, 31, 8, 2); AddRect(31, 28, 2, 8); break;
                    case "Cross-dot-small": AddRect(31, 31, 2, 2); AddRect(24, 31, 5, 2); AddRect(35, 31, 5, 2); AddRect(31, 24, 2, 5); AddRect(31, 35, 2, 5); break;
                    case "Cross-dot": AddRect(31, 31, 2, 2); AddRect(20, 31, 8, 2); AddRect(36, 31, 8, 2); AddRect(31, 20, 2, 8); AddRect(31, 36, 2, 8); break;
                    case "T-shape-dot": AddRect(31, 31, 2, 2); AddRect(21, 31, 7, 2); AddRect(36, 31, 7, 2); AddRect(31, 36, 2, 7); break;
                    case "T-shape-chevron": AddRect(21, 31, 6, 2); AddRect(37, 31, 6, 2); AddRect(31, 37, 2, 6); AddRect(31, 31, 2, 2); AddRect(29, 33, 2, 2); AddRect(33, 33, 2, 2); break;
                    case "Chevron-small": AddRect(31, 31, 2, 2); AddRect(29, 33, 2, 2); AddRect(33, 33, 2, 2); AddRect(27, 35, 2, 2); AddRect(35, 35, 2, 2); break;
                    case "Chevron-medium": AddRect(31, 30, 2, 2); AddRect(29, 32, 2, 2); AddRect(33, 32, 2, 2); AddRect(27, 34, 2, 2); AddRect(35, 34, 2, 2); AddRect(25, 36, 2, 2); AddRect(37, 36, 2, 2); break;
                    case "Circle-dot": AddRect(31, 31, 2, 2); AddRect(26, 23, 12, 2); AddRect(26, 39, 12, 2); AddRect(23, 26, 2, 12); AddRect(39, 26, 2, 12); break;
                    case "Circle-cross": AddRect(26, 23, 12, 2); AddRect(26, 39, 12, 2); AddRect(23, 26, 2, 12); AddRect(39, 26, 2, 12); AddRect(19, 31, 6, 2); AddRect(39, 31, 6, 2); AddRect(31, 19, 2, 6); AddRect(31, 39, 2, 6); break;
                    case "Cross-diag-dot": AddRect(31, 31, 2, 2); for (int k = 0; k < 5; k++) { AddRect(24 + k, 24 + k, 2, 2); AddRect(35 + k, 35 + k, 2, 2); AddRect(39 - k, 24 + k, 2, 2); AddRect(28 - k, 35 + k, 2, 2); } break;
                    case "Square-cross-dot": AddRect(31, 31, 2, 2); AddRect(23, 23, 5, 2); AddRect(23, 23, 2, 5); AddRect(36, 23, 5, 2); AddRect(39, 23, 2, 5); AddRect(23, 39, 5, 2); AddRect(23, 36, 2, 5); AddRect(36, 39, 5, 2); AddRect(39, 36, 2, 5); break;
                    case "2-sectors-dot": AddRect(31, 31, 2, 2); AddRect(21, 26, 2, 12); AddRect(41, 26, 2, 12); break;
                    case "PIXEL":
                        if (pixels.Count == 1024)
                            for (int i = 0; i < 1024; i++)
                                if (pixels[i]) AddRect(16 + (i % 32), 16 + (i / 32), 1, 1);
                        break;
                }
            }
            SetWindowRgn(_hwnd, fullRgn, true);
        }
    }
}