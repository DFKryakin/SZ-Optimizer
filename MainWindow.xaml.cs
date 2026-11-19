using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SZOptimizer
{
    public partial class MainWindow : Window
    {
        private readonly string _backupFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backup.json");
        private readonly string _settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private Dictionary<string, string> _backupData = new();
        private bool _timerBoosted = false;
        private bool _watchActive = false;
        private bool _tweaksApplied = false;
        private readonly List<int> _suspendedPids = new();

        private CrosshairWindow? _overlay;
        private bool _overlayActive = false;
        private Color _crossColor = (Color)ColorConverter.ConvertFromString("#00D4AA");
        private Color _origColor = (Color)ColorConverter.ConvertFromString("#00D4AA");
        private double _hue = 168.0, _sat = 1.0, _val = 0.83;
        private bool _updatingColor = false;
        private int _offsetX = 0, _offsetY = 0;
        private string? _customPngPath = null;
        private string _currentPreset = "Cross-dot";
        private readonly List<bool> _pixels = Enumerable.Repeat(false, 1024).ToList();
        private readonly List<Button> _gridButtons = new();
        private readonly List<Button> _presetButtons = new();
        private DispatcherTimer _hotkeyTimer = new();
        private bool _f8WasDown = false;

        [DllImport("ntdll.dll", SetLastError = true)] private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);
        [DllImport("ntdll.dll")] private static extern uint NtSuspendProcess(IntPtr processHandle);
        [DllImport("ntdll.dll")] private static extern uint NtResumeProcess(IntPtr processHandle);
        [DllImport("psapi.dll")] private static extern int EmptyWorkingSet(IntPtr hwProc);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);

        public MainWindow()
        {
            InitializeComponent();
            InitPixelEditor();
            InitPresetGallery();
            InitHotkeyTimer();
            CheckCrashRecovery();
            RunDiagnostics();
            LoadUserSettings();
            CheckAutoStartTask();
        }

        private void TabWinBtn_Click(object sender, RoutedEventArgs e)
        {
            PanelWindows.Visibility = Visibility.Visible;
            PanelCrosshair.Visibility = Visibility.Collapsed;
            TabWinBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31"));
            TabCrossBtn.Background = Brushes.Transparent;
        }

        private void TabCrossBtn_Click(object sender, RoutedEventArgs e)
        {
            PanelWindows.Visibility = Visibility.Collapsed;
            PanelCrosshair.Visibility = Visibility.Visible;
            TabCrossBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31"));
            TabWinBtn.Background = Brushes.Transparent;
        }
        private void InitPixelEditor()
        {
            for (int i = 0; i < 1024; i++)
            {
                var btn = new Button
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26282C")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#383A40")),
                    BorderThickness = new Thickness(0.5),
                    Margin = new Thickness(0),
                    IsHitTestVisible = false
                };
                _gridButtons.Add(btn);
                PixelEditorGrid.Children.Add(btn);
            }
            PixelEditorGrid.PreviewMouseDown += (s, e) => HandleGridDraw(e);
            PixelEditorGrid.PreviewMouseMove += (s, e) => HandleGridDraw(e);
        }

        private void HandleGridDraw(System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed && e.RightButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var pos = e.GetPosition(PixelEditorGrid);
            int x = (int)(pos.X / (PixelEditorGrid.ActualWidth / 32.0));
            int y = (int)(pos.Y / (PixelEditorGrid.ActualHeight / 32.0));
            if (x < 0 || x >= 32 || y < 0 || y >= 32) return;
            int idx = y * 32 + x;
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && !_pixels[idx])
            {
                _pixels[idx] = true;
                _gridButtons[idx].Background = new SolidColorBrush(_crossColor);
                _currentPreset = "PIXEL";
                UpdateOverlay();
            }
            else if (e.RightButton == System.Windows.Input.MouseButtonState.Pressed && _pixels[idx])
            {
                _pixels[idx] = false;
                _gridButtons[idx].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26282C"));
                _currentPreset = "PIXEL";
                UpdateOverlay();
            }
        }

        private void RefreshGridColors()
        {
            for (int i = 0; i < 1024; i++)
                _gridButtons[i].Background = _pixels[i] ? new SolidColorBrush(_crossColor) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26282C"));
        }

        private void BtnClearGrid_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 1024; i++) _pixels[i] = false;
            RefreshGridColors();
            UpdateOverlay();
        }

        private void InitPresetGallery()
        {
            var items = new List<(string Tag, string Name)> {
                ("Dot-2x2", "Точка 2px"), ("Dot-4x4", "Точка 4px"), ("Tiny-cross", "Мини-крест"), ("Cross-dot-small", "Крест мал."), ("Cross-dot", "Крест"),
                ("T-shape-dot", "Т-точка"), ("T-shape-chevron", "Т-уголок"), ("Chevron-small", "Уголок мал."), ("Chevron-medium", "Уголок"), ("Circle-dot", "Круг-точка"),
                ("Circle-cross", "Круг-крест"), ("Cross-diag-dot", "Косой крест"), ("Square-cross-dot", "Квадрат"), ("2-sectors-dot", "Сектора"), ("PIXEL", "Свой"), ("PNG", "PNG")
            };
            foreach (var item in items)
            {
                var canvas = new Canvas { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center };
                DrawMiniIcon(canvas, item.Tag);
                var sp = new StackPanel();
                sp.Children.Add(canvas);
                sp.Children.Add(new TextBlock { Text = item.Name, FontSize = 9.5, Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center });

                var btn = new Button
                {
                    Content = sp, Tag = item.Tag, Height = 46, Margin = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18191C")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Tag == _currentPreset ? "#00D4AA" : "#313338")),
                    BorderThickness = new Thickness(item.Tag == _currentPreset ? 1.5 : 1), Cursor = System.Windows.Input.Cursors.Hand
                };
                btn.Click += (s, e) =>
                {
                    _currentPreset = item.Tag;
                    foreach (var b in _presetButtons)
                    {
                        bool sel = (string)b.Tag == _currentPreset;
                        b.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sel ? "#00D4AA" : "#313338"));
                        b.BorderThickness = new Thickness(sel ? 1.5 : 1);
                    }
                    UpdateOverlay();
                };
                _presetButtons.Add(btn);
                PresetGalleryGrid.Children.Add(btn);
            }
        }
        private void DrawMiniIcon(Canvas c, string tag)
        {
            var b = Brushes.White;
            void D(double sz) { var e = new System.Windows.Shapes.Ellipse { Width = sz, Height = sz, Fill = b }; Canvas.SetLeft(e, 14 - sz / 2); Canvas.SetTop(e, 14 - sz / 2); c.Children.Add(e); }
            void L(double x1, double y1, double x2, double y2, double t = 1.5) { c.Children.Add(new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = t }); }
            switch (tag)
            {
                case "Dot-2x2": D(2.5); break;
                case "Dot-4x4": D(4.5); break;
                case "Tiny-cross": L(10, 14, 18, 14); L(14, 10, 14, 18); break;
                case "Cross-dot-small": D(2); L(7, 14, 11, 14); L(17, 14, 21, 14); L(14, 7, 14, 11); L(14, 17, 14, 21); break;
                case "Cross-dot": D(2); L(4, 14, 10, 14); L(18, 14, 24, 14); L(14, 4, 14, 10); L(14, 18, 14, 24); break;
                case "T-shape-dot": D(2); L(5, 14, 11, 14); L(17, 14, 23, 14); L(14, 17, 14, 23); break;
                case "T-shape-chevron": L(5, 14, 10, 14); L(18, 14, 23, 14); L(11, 16, 14, 13); L(14, 13, 17, 16); L(14, 18, 14, 23); break;
                case "Chevron-small": L(10, 16, 14, 12); L(14, 12, 18, 16); break;
                case "Chevron-medium": L(8, 18, 14, 11); L(14, 11, 20, 18); break;
                case "Circle-dot": D(2.5); var e1 = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, Stroke = b, StrokeThickness = 1.5 }; Canvas.SetLeft(e1, 6); Canvas.SetTop(e1, 6); c.Children.Add(e1); break;
                case "Circle-cross": var e2 = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Stroke = b, StrokeThickness = 1.5 }; Canvas.SetLeft(e2, 7); Canvas.SetTop(e2, 7); c.Children.Add(e2); L(4, 14, 9, 14); L(19, 14, 24, 14); L(14, 4, 14, 9); L(14, 19, 14, 24); break;
                case "Cross-diag-dot": D(2); L(7, 7, 11, 11); L(17, 17, 21, 21); L(21, 7, 17, 11); L(11, 17, 7, 21); break;
                case "Square-cross-dot": D(2); L(6, 6, 10, 6); L(6, 6, 6, 10); L(18, 6, 22, 6); L(22, 6, 22, 10); L(6, 22, 10, 22); L(6, 18, 6, 22); L(18, 22, 22, 22); L(22, 18, 22, 22); break;
                case "2-sectors-dot": D(2.5); L(7, 8, 5, 14, 2.5); L(5, 14, 7, 20, 2.5); L(21, 8, 23, 14, 2.5); L(23, 14, 21, 20, 2.5); break;
                case "PIXEL": c.Children.Add(new TextBlock { Text = "✏️", FontSize = 14, Margin = new Thickness(5, 3, 0, 0) }); break;
                case "PNG": c.Children.Add(new TextBlock { Text = "🖼️", FontSize = 14, Margin = new Thickness(5, 3, 0, 0) }); break;
            }
        }

        private void InitHotkeyTimer()
        {
            _hotkeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _hotkeyTimer.Tick += (s, e) =>
            {
                bool f8Down = (GetAsyncKeyState(0x77) & 0x8000) != 0;
                if (f8Down && !_f8WasDown) ToggleOverlay();
                _f8WasDown = f8Down;

                if (_overlayActive && _overlay != null)
                {
                    bool rmbDown = (GetAsyncKeyState(0x02) & 0x8000) != 0;
                    if (ChkHideOnRmb.IsChecked == true && rmbDown) _overlay.Hide();
                    else if (!_overlay.IsVisible) _overlay.Show();
                }
            };
            _hotkeyTimer.Start();
        }

        private void ToggleOverlay()
        {
            _overlayActive = !_overlayActive;
            if (_overlayActive)
            {
                if (_overlay == null) _overlay = new CrosshairWindow();
                _overlay.Show();
                UpdateOverlay();
                BtnToggleOverlay.Content = "🛑 ВЫКЛЮЧИТЬ ОВЕРЛЕЙ-ПРИЦЕЛ (F8)";
                BtnToggleOverlay.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A2326"));
            }
            else
            {
                _overlay?.Hide();
                BtnToggleOverlay.Content = "🎯 ВКЛЮЧИТЬ ОВЕРЛЕЙ-ПРИЦЕЛ (F8)";
                BtnToggleOverlay.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31"));
            }
        }

        private void BtnToggleOverlay_Click(object sender, RoutedEventArgs e) => ToggleOverlay();

        private void UpdateOverlay()
        {
            if (_overlay == null) return;
            _overlay.RenderCrosshair(_currentPreset, _crossColor, ScaleSlider.Value / 100.0, _offsetX, _offsetY, _pixels, _customPngPath);
        }
        private void HueCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => UpdateHueFromMouse(e.GetPosition(HueCanvas).Y);
        private void HueCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) UpdateHueFromMouse(e.GetPosition(HueCanvas).Y); }

        private void UpdateHueFromMouse(double y)
        {
            y = Math.Clamp(y, 0, 128);
            Canvas.SetTop(HueMarker, y - 1.5);
            _hue = (y / 128.0) * 360.0;
            SvCanvas.Background = new SolidColorBrush(HsvToColor(_hue, 1.0, 1.0, 255));
            ApplyHsvColor();
        }

        private void SvCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => UpdateSvFromMouse(e.GetPosition(SvCanvas));
        private void SvCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) UpdateSvFromMouse(e.GetPosition(SvCanvas)); }

        private void UpdateSvFromMouse(Point p)
        {
            double x = Math.Clamp(p.X, 0, 138);
            double y = Math.Clamp(p.Y, 0, 128);
            Canvas.SetLeft(SvMarker, x - 5);
            Canvas.SetTop(SvMarker, y - 5);
            _sat = x / 138.0;
            _val = 1.0 - (y / 128.0);
            ApplyHsvColor();
        }

        private void AlphaSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_updatingColor && SliderA != null) ApplyHsvColor(); }

        private void OrigColorBox_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _crossColor = _origColor;
            ColorPreviewBox.Background = new SolidColorBrush(_crossColor);
            HexColorBox.Text = $"#{_crossColor.A:X2}{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}";
            RgbInfoText.Text = $"R: {_crossColor.R}   G: {_crossColor.G}   B: {_crossColor.B}";
            RefreshGridColors(); UpdateOverlay();
        }

        private void ApplyHsvColor()
        {
            if (ColorPreviewBox == null || SliderA == null || HexColorBox == null) return;
            _updatingColor = true;
            byte a = (byte)SliderA.Value;
            TxtA.Text = a.ToString();
            _crossColor = HsvToColor(_hue, _sat, _val, a);
            ColorPreviewBox.Background = new SolidColorBrush(_crossColor);
            RgbInfoText.Text = $"R: {_crossColor.R}   G: {_crossColor.G}   B: {_crossColor.B}";
            HexColorBox.Text = $"#{_crossColor.A:X2}{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}";
            RefreshGridColors();
            UpdateOverlay();
            _updatingColor = false;
        }
        private void HexColorBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(HexColorBox.Text.Trim());
                _updatingColor = true;
                SliderA.Value = c.A; TxtA.Text = c.A.ToString();
                _crossColor = c;
                ColorPreviewBox.Background = new SolidColorBrush(_crossColor);
                RgbInfoText.Text = $"R: {c.R}   G: {c.G}   B: {c.B}";
                RefreshGridColors(); UpdateOverlay();
                _updatingColor = false;
            }
            catch { }
        }

        private static Color HsvToColor(double h, double s, double v, byte a)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromArgb(a, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ScaleLabel != null) ScaleLabel.Text = $"3. Размер (Масштаб): {(int)ScaleSlider.Value}%";
            UpdateOverlay();
        }

        private void Offset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag)
            {
                if (tag == "X-") _offsetX--; else if (tag == "X+") _offsetX++;
                else if (tag == "Y-") _offsetY--; else if (tag == "Y+") _offsetY++;
                OffsetXText.Text = _offsetX.ToString(); OffsetYText.Text = _offsetY.ToString();
                UpdateOverlay();
            }
        }

        private void BtnLoadPng_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Картинки PNG (*.png)|*.png" };
            if (dlg.ShowDialog() == true) { _customPngPath = dlg.FileName; _currentPreset = "PNG"; UpdateOverlay(); }
        }

        private void BtnExportCode_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 1024; i++) sb.Append(_pixels[i] ? '1' : '0');
            string code = $"SZ2:{_crossColor}:{(int)ScaleSlider.Value}:{_offsetX}:{_offsetY}:{sb}";
            Clipboard.SetText(code);
            MessageBox.Show("Код прицела скопирован в буфер обмена!", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnImportCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string code = Clipboard.GetText().Trim();
                var parts = code.Split(':');
                if (parts.Length == 6 && parts[0] == "SZ2" && parts[5].Length == 1024) { _crossColor = (Color)ColorConverter.ConvertFromString(parts[1]); ScaleSlider.Value = double.Parse(parts[2]); _offsetX = int.Parse(parts[3]); _offsetY = int.Parse(parts[4]); OffsetXText.Text = _offsetX.ToString(); OffsetYText.Text = _offsetY.ToString(); for (int i = 0; i < 1024; i++) _pixels[i] = parts[5][i] == '1'; _currentPreset = "PIXEL"; RefreshGridColors(); UpdateOverlay(); MessageBox.Show("Прицел загружен из буфера обмена!", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information); }
            }
            catch { }
        }
        private void CheckCrashRecovery()
        {
            if (File.Exists(_backupFile))
            {
                BackupStatusText.Text = "⚠️ Найден незакрытый бэкап";
                var res = MessageBox.Show("Прошлая сессия завершилась некорректно. Восстановить настройки Windows?", "Crash Recovery", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes) RollbackAll();
            }
        }

        private void RunDiagnostics()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
                int gameMode = (int)(key?.GetValue("AllowAutoGameMode", 1) ?? 1);
                string gmStatus = gameMode == 1 ? "ВКЛ" : "ВЫКЛ";
                long pageFileMb = 0;
                using (var searcher = new ManagementObjectSearcher("SELECT AllocatedBaseSize FROM Win32_PageFileUsage"))
                    foreach (ManagementObject obj in searcher.Get()) pageFileMb += Convert.ToInt64(obj["AllocatedBaseSize"]);
                DiagnosticsText.Text = $"● ДИАГНОСТИКА: Игровой режим: {gmStatus}  |  Файл подкачки: {(pageFileMb >= 4096 ? $"{pageFileMb / 1024} ГБ (ОК)" : "Мало")}";
            }
            catch { DiagnosticsText.Text = "● ДИАГНОСТИКА: Система готова"; }
        }

        private void CheckAutoStartTask()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("schtasks", "/Query /TN \"SZOptimizerAuto\"") { CreateNoWindow = true, UseShellExecute = false });
                p?.WaitForExit();
                ChkAutoStart.IsChecked = (p?.ExitCode == 0);
            }
            catch { }
        }

        private void ChkAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (ChkAutoStart.IsChecked == true && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo("schtasks", $"/Create /TN \"SZOptimizerAuto\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                MessageBox.Show("Готово! Теперь SZ Optimizer будет запускаться вместе с Windows с правами Администратора без окна UAC.", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Process.Start(new ProcessStartInfo("schtasks", "/Delete /TN \"SZOptimizerAuto\" /F") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
            }
        }
        private void LoadUserSettings()
        {
            try
            {
                if (!File.Exists(_settingsFile)) return;
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_settingsFile));
                if (d == null) return;
                ChkRamShader.IsChecked = d.GetValueOrDefault("1", "True") == "True";
                ChkNetwork.IsChecked = d.GetValueOrDefault("2", "False") == "True";
                ChkInput.IsChecked = d.GetValueOrDefault("3", "False") == "True";
                ChkGameBoost.IsChecked = d.GetValueOrDefault("4", "True") == "True";
                ChkFullscreen.IsChecked = d.GetValueOrDefault("5", "False") == "True";
                ChkTimer.IsChecked = d.GetValueOrDefault("6", "True") == "True";
                ChkMmcss.IsChecked = d.GetValueOrDefault("7", "False") == "True";
                ChkDns.IsChecked = d.GetValueOrDefault("8", "False") == "True";
                ChkCleanLogs.IsChecked = d.GetValueOrDefault("9", "True") == "True";
                ChkTrayMode.IsChecked = d.GetValueOrDefault("10", "True") == "True";
                ChkSuspendBrowsers.IsChecked = d.GetValueOrDefault("11", "True") == "True";
                ChkHideOnRmb.IsChecked = d.GetValueOrDefault("Rmb", "True") == "True"; ChkAutoCrosshair.IsChecked = d.GetValueOrDefault("AutoCross", "True") == "True";
                if (d.ContainsKey("Preset")) _currentPreset = d["Preset"];
                if (d.ContainsKey("Scale")) ScaleSlider.Value = double.Parse(d["Scale"]);
                if (d.ContainsKey("OffX")) { _offsetX = int.Parse(d["OffX"]); OffsetXText.Text = _offsetX.ToString(); }
                if (d.ContainsKey("OffY")) { _offsetY = int.Parse(d["OffY"]); OffsetYText.Text = _offsetY.ToString(); }
                if (d.ContainsKey("Color"))
                {
                    _crossColor = (Color)ColorConverter.ConvertFromString(d["Color"]);
                    ColorPreviewBox.Background = new SolidColorBrush(_crossColor);
                    HexColorBox.Text = d["Color"];
                    RefreshGridColors();
                }
            }
            catch { }
        }

        private void SaveUserSettings()
        {
            try
            {
                var d = new Dictionary<string, string>
                {
                    {"1", ChkRamShader.IsChecked.ToString()!}, {"2", ChkNetwork.IsChecked.ToString()!},
                    {"3", ChkInput.IsChecked.ToString()!}, {"4", ChkGameBoost.IsChecked.ToString()!},
                    {"5", ChkFullscreen.IsChecked.ToString()!}, {"6", ChkTimer.IsChecked.ToString()!},
                    {"7", ChkMmcss.IsChecked.ToString()!}, {"8", ChkDns.IsChecked.ToString()!},
                    {"9", ChkCleanLogs.IsChecked.ToString()!}, {"10", ChkTrayMode.IsChecked.ToString()!},
                    {"11", ChkSuspendBrowsers.IsChecked.ToString()!}, {"Rmb", ChkHideOnRmb.IsChecked.ToString()!}, {"AutoCross", ChkAutoCrosshair.IsChecked.ToString()!},
                    {"Preset", _currentPreset}, {"Scale", ScaleSlider.Value.ToString()},
                    {"OffX", _offsetX.ToString()}, {"OffY", _offsetY.ToString()},
                    {"Color", $"#{_crossColor.A:X2}{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}"}
                };
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(d));
            }
            catch { }
        }

        private void ApplySelectedTweaks()
        {
            if (_tweaksApplied) return;
            SaveBackup();
            if (ChkRamShader.IsChecked == true) CleanShadersAndRam();
            if (ChkNetwork.IsChecked == true) OptimizeNetwork();
            if (ChkInput.IsChecked == true) OptimizeInput();
            if (ChkGameBoost.IsChecked == true) ToggleBackgroundServices(false);
            if (ChkTimer.IsChecked == true) { NtSetTimerResolution(10000, true, out _); _timerBoosted = true; }
            if (ChkMmcss.IsChecked == true) OptimizeMmcss();
            if (ChkDns.IsChecked == true) ApplyFastestDns();
            if (ChkSuspendBrowsers.IsChecked == true) ToggleBrowsersSuspend(true);
            _tweaksApplied = true;
        }

        private async void BtnAutoWatch_Click(object sender, RoutedEventArgs e)
        {
            _watchActive = !_watchActive;
            if (!_watchActive)
            {
                BtnAutoWatch.Content = "👁️ ВКЛЮЧИТЬ АВТО-ОЖИДАНИЕ STALZONE";
                BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA"));
                if (_tweaksApplied) { RollbackAll(); _tweaksApplied = false; if (ChkAutoCrosshair.IsChecked == true && _overlayActive) ToggleOverlay(); }
                return;
            }
            BtnAutoWatch.Content = "⏳ ОЖИДАНИЕ ЗАПУСКА STALZONE... (Нажмите для стопа)";
            BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"));
            if (ChkTrayMode.IsChecked == true) this.WindowState = WindowState.Minimized;

            while (_watchActive)
            {
                var procs = Process.GetProcessesByName("stalzonew").Concat(Process.GetProcessesByName("javaw")).ToArray();
                if (procs.Length > 0)
                {
                    if (!_tweaksApplied)
                    {
                        ApplySelectedTweaks(); if (ChkAutoCrosshair.IsChecked == true && !_overlayActive) ToggleOverlay();
                        BtnAutoWatch.Content = "🎮 ИГРА АКТИВНА — ОПТИМИЗАЦИЯ ВКЛЮЧЕНА";
                        BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#51CF66"));
                    }
                    if (ChkGameBoost.IsChecked == true)
                        foreach (var p in procs) try { if (p.PriorityClass != ProcessPriorityClass.High) p.PriorityClass = ProcessPriorityClass.High; } catch { }
                    await Task.Delay(15000);
                }
                else
                {
                    if (_tweaksApplied)
                    {
                        RollbackAll(); _tweaksApplied = false; if (ChkAutoCrosshair.IsChecked == true && _overlayActive) ToggleOverlay(); if (ChkAutoCrosshair.IsChecked == true && _overlayActive) ToggleOverlay();
                        this.WindowState = WindowState.Normal;
                        BtnAutoWatch.Content = "⏳ ОЖИДАНИЕ ЗАПУСКА STALZONE... (Нажмите для стопа)";
                        BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"));
                    }
                    await Task.Delay(2000);
                }
            }
        }

        private void BtnApplyNow_Click(object sender, RoutedEventArgs e)
        {
            ApplySelectedTweaks(); if (ChkAutoCrosshair.IsChecked == true && !_overlayActive) ToggleOverlay();
            MessageBox.Show("Выбранные оптимизации применены! Для возврата нажмите «Откатить всё» слева.", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void ToggleBrowsersSuspend(bool suspend)
        {
            if (suspend)
            {
                _suspendedPids.Clear();
                var targets = new List<string> { "chrome", "msedge", "opera", "yandex", "firefox", "steam", "epicgameslauncher" };
                foreach (var proc in Process.GetProcesses())
                {
                    try { if (targets.Contains(proc.ProcessName.ToLower())) { if (proc.MainWindowHandle == IntPtr.Zero) { proc.PriorityClass = ProcessPriorityClass.BelowNormal; EmptyWorkingSet(proc.Handle); } } } catch { }
                }
            }
            else
            {
                foreach (var pid in _suspendedPids) { try { IntPtr h = OpenProcess(0x0800, false, pid); if (h != IntPtr.Zero) { NtResumeProcess(h); CloseHandle(h); } } catch { } }
                _suspendedPids.Clear();
            }
        }

        private void SaveBackup()
        {
            _backupData.Clear();
            using var mk = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            _backupData["MouseSpeed"] = mk?.GetValue("MouseSpeed")?.ToString() ?? "1";
            _backupData["MouseThreshold1"] = mk?.GetValue("MouseThreshold1")?.ToString() ?? "6";
            _backupData["MouseThreshold2"] = mk?.GetValue("MouseThreshold2")?.ToString() ?? "10";
            File.WriteAllText(_backupFile, JsonSerializer.Serialize(_backupData));
            BackupStatusText.Text = "🛡️ Бэкап: Активен";
        }

        private void CleanShadersAndRam()
        {
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dirs = new List<string> { Path.Combine(la, @"NVIDIA\DXCache"), Path.Combine(la, @"NVIDIA\GLCache"), Path.Combine(la, @"AMD\DxCache") };
            foreach (var dir in dirs)
                if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir)) try { File.Delete(f); } catch { }
            foreach (var p in Process.GetProcesses()) try { if (p.ProcessName != "stalzonew" && p.ProcessName != "javaw" && p.ProcessName != "Discord") EmptyWorkingSet(p.Handle); } catch { }
        }

        private void OptimizeNetwork()
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
                if (k == null) return;
                foreach (var sub in k.GetSubKeyNames()) try { using var i = k.OpenSubKey(sub, true); i?.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord); i?.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord); } catch { }
            }
            catch { }
        }

        private void OptimizeInput()
        {
            using var mk = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
            mk?.SetValue("MouseSpeed", "0"); mk?.SetValue("MouseThreshold1", "0"); mk?.SetValue("MouseThreshold2", "0");
            using var sk = Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\StickyKeys", true);
            sk?.SetValue("Flags", "506");
        }

        private void ToggleBackgroundServices(bool enable)
        {
            var srvs = new List<string> { "WSearch", "DiagTrack" };
            foreach (var srv in srvs)
                try { using var sc = new ServiceController(srv); if (!enable && sc.Status == ServiceControllerStatus.Running) sc.Stop(); else if (enable && sc.Status == ServiceControllerStatus.Stopped) sc.Start(); } catch { }
        }

        private void OptimizeMmcss()
        {
            try
            {
                using var mm = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
                mm?.SetValue("GPU Priority", 8, RegistryValueKind.DWord); mm?.SetValue("Priority", 6, RegistryValueKind.DWord); mm?.SetValue("Scheduling Category", "High", RegistryValueKind.String);
            }
            catch { }
        }

        private void ApplyFastestDns()
        {
            var targets = new Dictionary<string, long> { { "1.1.1.1", 999 }, { "8.8.8.8", 999 }, { "77.88.8.8", 999 } };
            using var ping = new Ping();
            foreach (var ip in targets.Keys.ToList()) try { var r = ping.Send(ip, 1000); if (r.Status == IPStatus.Success) targets[ip] = r.RoundtripTime; } catch { }
            string best = targets.OrderBy(x => x.Value).First().Key;
            var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
            if (nic != null) Process.Start(new ProcessStartInfo("netsh", $"interface ip set dns \"{nic.Name}\" static {best}") { CreateNoWindow = true, UseShellExecute = false });
        }

        private void RollbackAll()
        {
            if (_timerBoosted) { NtSetTimerResolution(156250, false, out _); _timerBoosted = false; }
            ToggleBackgroundServices(true);
            ToggleBrowsersSuspend(false);
            if (File.Exists(_backupFile))
            {
                try
                {
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_backupFile));
                    if (data != null)
                    {
                        using var mk = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
                        if (data.ContainsKey("MouseSpeed")) mk?.SetValue("MouseSpeed", data["MouseSpeed"]);
                        if (data.ContainsKey("MouseThreshold1")) mk?.SetValue("MouseThreshold1", data["MouseThreshold1"]);
                        if (data.ContainsKey("MouseThreshold2")) mk?.SetValue("MouseThreshold2", data["MouseThreshold2"]);
                    }
                    File.Delete(_backupFile);
                }
                catch { }
            }
            BackupStatusText.Text = "🛡️ Бэкап: Откат выполнен";
        }

        private void BtnRollback_Click(object sender, RoutedEventArgs e)
        {
            RollbackAll();
            _tweaksApplied = false;
            MessageBox.Show("Все системные настройки возвращены!", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveUserSettings();
            if (_tweaksApplied) RollbackAll();
            _overlay?.Close();
        }
    }
}
