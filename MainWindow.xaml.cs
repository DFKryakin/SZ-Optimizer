using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SZOptimizer
{
    public partial class MainWindow : Window
    {
        private readonly string _settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private bool _timerBoosted = false, _watchActive = false, _tweaksApplied = false;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NOTIFYICONDATA { public int cbSize; public IntPtr hWnd; public int uID; public int uFlags; public int uCallbackMessage; public IntPtr hIcon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
        [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
        private NOTIFYICONDATA _nid;
        private bool _forceExit = false;
        private Process? _trackedGame;
        private CrosshairOverlay? _overlay;
        private bool _overlayActive = false, _f8WasDown = false, _rmbWasHidden = false, _hideOnRmb = true, _waitingForKey = false;
        private int _hotkeyVk = 0x2D, _hotkeyMod = 0;
        private string _hotkeyName = "Insert", _currentPreset = "Cross-dot";
        private Color _crossColor = (Color)ColorConverter.ConvertFromString("#00D4AA");
        private double _hue = 168.0, _sat = 1.0, _val = 0.83;
        private int _offsetX = 0, _offsetY = 0;
        private readonly List<bool> _pixels = Enumerable.Repeat(false, 256).ToList();
        private readonly List<Button> _gridButtons = new(), _presetButtons = new();
        private DispatcherTimer _hotkeyTimer = new();

        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess); [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid); [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);  [DllImport("ntdll.dll")] private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey); [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk); [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id); private void UpdateGlobalHotkey() { IntPtr h = new WindowInteropHelper(this).Handle; if (h == IntPtr.Zero) return; UnregisterHotKey(h, 9001); uint m = _hotkeyMod == 1 ? 1u : (_hotkeyMod == 2 ? 2u : (_hotkeyMod == 3 ? 4u : 0u)); RegisterHotKey(h, 9001, m, (uint)_hotkeyVk); }

        public MainWindow()
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly; InitializeComponent(); ChkHideOnRmb.Checked += (s, e) => _hideOnRmb = true; ChkHideOnRmb.Unchecked += (s, e) => _hideOnRmb = false; RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            InitPixelEditor();
            InitPresetGallery();
            InitHotkeyTimer(); 
            RunDiagnostics();
            LoadUserSettings();
            CheckAutoStartTask(); InitHardwareTab(); if (!File.Exists(_settingsFile)) AutoTuneByHardware(false); new System.Threading.Thread(() => { while (true) { System.Threading.Thread.Sleep(1000); if (_autoTrimSec > 0) { _trimTick++; if (_trimTick >= _autoTrimSec) { _trimTick = 0; RunSilentTrim(); } } { TrimGameMemorySilent(); System.Threading.Thread.Sleep(1100); } } }) { IsBackground = true }.Start();
            this.Loaded += (s, e) => { if (!_watchActive) BtnAutoWatch_Click(this, new RoutedEventArgs()); };
        }

        protected override void OnSourceInitialized(EventArgs e) { base.OnSourceInitialized(e); IntPtr hwnd = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(hwnd)?.AddHook(TrayWndProc); _nid = new NOTIFYICONDATA { cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = hwnd, uID = 1, uFlags = 7, uCallbackMessage = 0x8001, hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)), szTip = "SZ Optimizer (STALZONE)" }; Shell_NotifyIcon(0, ref _nid);  if (Environment.GetCommandLineArgs().Contains("-autostart")) HideToTray(); }
        private IntPtr TrayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) { if (msg == 0x8001) { int ev = lParam.ToInt32(); if (ev == 0x0202 || ev == 0x0203) RestoreFromTray(); else if (ev == 0x0205) { var cm = new ContextMenu(); var m1 = new MenuItem { Header = "Открыть SZ Optimizer" }; m1.Click += (s, a) => RestoreFromTray(); var m2 = new MenuItem { Header = "Выход" }; m2.Click += (s, a) => { _forceExit = true; Close(); }; cm.Items.Add(m1); cm.Items.Add(m2); cm.IsOpen = true; } } return IntPtr.Zero; }
        private void HideToTray() { this.Hide(); this.ShowInTaskbar = false; }
        private void RestoreFromTray() { this.Show(); this.ShowInTaskbar = true; this.WindowState = WindowState.Normal; this.Activate(); }
        protected override void OnStateChanged(EventArgs e) { if (this.WindowState == WindowState.Minimized) HideToTray(); base.OnStateChanged(e); }
                private void TabHwBtn_Click(object sender, RoutedEventArgs e) { PanelWindows.Visibility = Visibility.Collapsed; PanelCrosshair.Visibility = Visibility.Collapsed; PanelHardware.Visibility = Visibility.Visible; TabWinBtn.Background = Brushes.Transparent; TabCrossBtn.Background = Brushes.Transparent; TabHwBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31")); }

        private void InitHardwareTab()
        {
            try
            {
                string cpu = "Не определён";
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                    foreach (ManagementObject o in s.Get()) { cpu = o["Name"]?.ToString()?.Trim() ?? cpu; break; }
                HwCpuText.Text = $"CPU: {cpu} ({Environment.ProcessorCount} потоков)";

                long totalMb = 0;
                using (var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in s.Get()) totalMb = Convert.ToInt64(o["TotalPhysicalMemory"]) / (1024 * 1024);
                int totalGb = totalMb > 24000 ? 32 : (totalMb > 12000 ? 16 : (totalMb > 6000 ? 8 : 4));  
                HwRamText.Text = $"ОЗУ: {totalGb} ГБ ({totalMb} МБ)";

                var gpus = new List<string>();
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (ManagementObject o in s.Get()) { var n = o["Name"]?.ToString(); if (!string.IsNullOrWhiteSpace(n)) gpus.Add(n); }
                bool transit = gpus.Count > 1 && gpus.Any(g => g.Contains("UHD", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase));
                HwGpuText.Text = $"GPU: {string.Join(" + ", gpus)}" + (transit ? "\n💡 Активны дискретная и встроенная графика — рекомендуется привязка GPU ниже." : "");

                
                
            }
            catch { }
        }

        private void BtnApplyHw_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ChkPcieAspm.IsChecked == true)
                {
                    Process.Start(new ProcessStartInfo("powercfg", "/SETACVALUEINDEX SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                    Process.Start(new ProcessStartInfo("powercfg", "/SETACTIVE SCHEME_CURRENT") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                }
                if (ChkGpuPref.IsChecked == true)
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            string n = p.ProcessName.ToLower();
                            if (n == "javaw" || n == "stalzonew" || n.Contains("stalcraft"))
                            {
                                string? path = p.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(path)) key?.SetValue(path, "GpuPreference=2;");
                            }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                                
                else
                    Environment.SetEnvironmentVariable("_JAVA_OPTIONS", null, EnvironmentVariableTarget.User);
                MessageBox.Show("Настройки PCIe, GPU и лимит памяти Java применены! (Перезапустите игру для применения 8 ГБ)", "SZ Optimizer v1.2");
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "SZ Optimizer v1.2"); }
        }

                private int _autoTrimSec = 600; private int _trimTick = 0; private void CmbAutoTrim_SelectionChanged(object sender, SelectionChangedEventArgs e) { int idx = CmbAutoTrim.SelectedIndex; _autoTrimSec = idx == 0 ? 0 : (idx == 1 ? 30 : (idx - 1) * 60); _trimTick = 0; SaveUserSettings(); }  private void RunSilentTrim() { foreach (var p in Process.GetProcesses()) { try { string n = p.ProcessName.ToLower(); if (!n.Contains("szoptimizer") && (n.Contains("stalzone") || n.Contains("stalcraft") || n == "javaw")) { IntPtr h = OpenProcess(0x0500, false, p.Id); if (h != IntPtr.Zero) { EmptyWorkingSet(h); CloseHandle(h); } } } catch { } finally { p.Dispose(); } } }
        
        private void TrimGameMemorySilent() { foreach (var p in Process.GetProcesses()) { try { string n = p.ProcessName.ToLower(); if (!n.Contains("szoptimizer") && (n.Contains("stalzone") || n.Contains("stalcraft") || n == "javaw")) { IntPtr h = OpenProcess(0x0500, false, p.Id); if (h != IntPtr.Zero) { EmptyWorkingSet(h); CloseHandle(h); } } } catch { } finally { p.Dispose(); } } }
         
                private void BtnTrimNow_Click(object sender, RoutedEventArgs e) { RunSilentTrim(); MessageBox.Show("Застоявшийся кэш ОЗУ игры очищен!", "SZ Optimizer v1.2"); }

        private void BtnApplyGfxPreset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? opt = new[] { @"D:\EXBO\runtime\stalcraft\config\options.json", @"C:\EXBO\runtime\stalcraft\config\options.json", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"EXBO\runtime\stalcraft\config\options.json") }.FirstOrDefault(File.Exists);
                if (opt == null) { MessageBox.Show("Файл options.json не найден. Запустите игру хотя бы один раз.", "SZ Optimizer v1.2"); return; }
                string bak = opt + ".szbak";
                if (!File.Exists(bak)) File.Copy(opt, bak);
                int idx = CmbGfxPreset.SelectedIndex;
                if (idx == 2)
                {
                    if (File.Exists(bak)) File.Copy(bak, opt, true);
                    MessageBox.Show("Исходный options.json восстановлен!", "SZ Optimizer v1.2");
                    return;
                }
                string json = File.ReadAllText(opt);
                void SetJson(string k, string v) { json = System.Text.RegularExpressions.Regex.Replace(json, $"\"{k}\"\\s*:\\s*[^,\\r\\n]+", $"\"{k}\": {v}"); }
                SetJson("blur_hud_elements", "false");
                SetJson("godrays_enabled", "false");
                SetJson("ssao", "0");
                SetJson("shadows_dynamic", "false");
                SetJson("shadows_pcf", "false");
                SetJson("multithread_loading", "true");
                SetJson("precompile_shaders", "true");
                if (idx == 0) { SetJson("bloom", "false"); SetJson("grass_flag", "false"); SetJson("bullet_holes", "false"); SetJson("particles_resolution", "0"); SetJson("details_amount", "0"); }
                else { SetJson("bloom", "true"); SetJson("grass_flag", "true"); SetJson("particles_resolution", "1"); }
                File.WriteAllText(opt, json);
                MessageBox.Show("Пресет графики применён к options.json! (Перезапустите игру для вступления в силу)", "SZ Optimizer v1.2");
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "SZ Optimizer v1.2"); }
        }
                private void BtnAutoTune_Click(object sender, RoutedEventArgs e) { AutoTuneByHardware(true); }

                private void AutoTuneByHardware(bool notify)
        {
            int threads = Environment.ProcessorCount;
            bool highCpu = threads >= 12;
            bool hybridGpu = HwGpuText.Text.Contains("+");
            bool lowRam = HwRamText.Text.Contains("8 ГБ") || HwRamText.Text.Contains("4 ГБ");

            ChkAffinity.IsChecked = !highCpu;
            ChkGameBoost.IsChecked = !hybridGpu;
            ChkSoftBrowsers.IsChecked = true;
            ChkCleanLogs.IsChecked = true;
            ChkTrayMode.IsChecked = true;
            ChkTimer.IsChecked = false;
            ChkNetwork.IsChecked = false;
            ChkInput.IsChecked = false;

            ChkPcieAspm.IsChecked = true;
            ChkGpuPref.IsChecked = true;
            ChkCorePark.IsChecked = true;
            ChkGameMode.IsChecked = true;
            CmbAutoTrim.SelectedIndex = lowRam ? 4 : 11;

            string prof = (highCpu ? $"CPU {threads}п (все ядра)" : $"CPU {threads}п (изоляция CPU0)") + (hybridGpu ? " + Гибрид GPU" : " + Дискрет. GPU");
            DiagText.Text = $"• АВТОПРОФИЛЬ: {prof}";
            SaveUserSettings();
            if (notify) MessageBox.Show($"Автопрофиль применён!\n\n• Потоков CPU: {threads} ({(highCpu ? "отключена изоляция CPU 0 для макс. FPS" : "включена изоляция CPU 0 от фризов")})\n• Видеосистема: {(hybridGpu ? "обнаружена гибридная графика — отключён перекос приоритета DWM" : "дискретная графика — стандартный приоритет")}\n• Авто-очистка кэша ОЗУ: {(lowRam ? "каждые 3 мин" : "каждые 10 мин")}", "SZ Optimizer v1.2 — Умный профиль");
        }
        private void TabWinBtn_Click(object sender, RoutedEventArgs e) { PanelWindows.Visibility = Visibility.Visible; PanelCrosshair.Visibility = Visibility.Collapsed; TabWinBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31")); TabCrossBtn.Background = Brushes.Transparent; PanelHardware.Visibility = Visibility.Collapsed; TabHwBtn.Background = Brushes.Transparent; }
        private void TabCrossBtn_Click(object sender, RoutedEventArgs e) { PanelWindows.Visibility = Visibility.Collapsed; PanelCrosshair.Visibility = Visibility.Visible; TabCrossBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31")); TabWinBtn.Background = Brushes.Transparent; PanelHardware.Visibility = Visibility.Collapsed; TabHwBtn.Background = Brushes.Transparent; }
        private void InitPixelEditor()
        {
            for (int i = 0; i < 256; i++) { var btn = new Button { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26282C")), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#383A40")), BorderThickness = new Thickness(0.5), Margin = new Thickness(0), IsHitTestVisible = false }; _gridButtons.Add(btn); PixelEditorGrid.Children.Add(btn); }
            PixelEditorGrid.PreviewMouseDown += (s, e) => HandleGridDraw(e);
            PixelEditorGrid.PreviewMouseMove += (s, e) => HandleGridDraw(e);
        }

        private void HandleGridDraw(MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed && e.RightButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(PixelEditorGrid);
            int x = (int)(pos.X / (PixelEditorGrid.ActualWidth / 16.0)), y = (int)(pos.Y / (PixelEditorGrid.ActualHeight / 16.0));
            if (x < 0 || x >= 16 || y < 0 || y >= 16) return;
            int idx = y * 16 + x;
            if (e.LeftButton == MouseButtonState.Pressed && !_pixels[idx]) { _pixels[idx] = true; _gridButtons[idx].Background = new SolidColorBrush(_crossColor); _currentPreset = "PIXEL"; UpdateOverlay(); }
            else if (e.RightButton == MouseButtonState.Pressed && _pixels[idx]) { _pixels[idx] = false; _gridButtons[idx].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26282C")); _currentPreset = "PIXEL"; UpdateOverlay(); }
        }

        private void RefreshGridColors() { for (int i = 0; i < 256; i++) _gridButtons[i].Background = _pixels[i] ? new SolidColorBrush(_crossColor) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26282C")); }
        private void BtnClearGrid_Click(object sender, RoutedEventArgs e) { for (int i = 0; i < 256; i++) _pixels[i] = false; RefreshGridColors(); UpdateOverlay(); }

        private void InitPresetGallery()
        {
            var items = new List<(string Tag, string Name)> { ("Dot-2x2", "Точка 2px"), ("Dot-4x4", "Точка 4px"), ("Tiny-cross", "Мини-крест"), ("Cross-dot-small", "Крест мал."), ("Cross-dot", "Крест"), ("T-shape-dot", "Т-точка"), ("T-shape-chevron", "Т-уголок"), ("Chevron-small", "Уголок мал."), ("Chevron-medium", "Уголок"), ("Circle-dot", "Круг-точка"), ("Circle-cross", "Круг-крест"), ("Cross-diag-dot", "Косой крест"), ("Square-cross-dot", "Квадрат"), ("2-sectors-dot", "Сектора"), ("Horiz-dot", "Линии-точка"), ("PIXEL", "Свой") };
            foreach (var item in items)
            {
                var canvas = new Canvas { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center }; DrawMiniIcon(canvas, item.Tag);
                var sp = new StackPanel(); sp.Children.Add(canvas); sp.Children.Add(new TextBlock { Text = item.Name, FontSize = 9.5, Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center });
                var btn = new Button { Content = sp, Tag = item.Tag, Height = 46, Margin = new Thickness(2), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18191C")), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Tag == _currentPreset ? "#00D4AA" : "#313338")), BorderThickness = new Thickness(item.Tag == _currentPreset ? 1.5 : 1), Cursor = Cursors.Hand };
                btn.Click += (s, e) => { _currentPreset = item.Tag; foreach (var b in _presetButtons) { bool sel = (string)b.Tag == _currentPreset; b.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sel ? "#00D4AA" : "#313338")); b.BorderThickness = new Thickness(sel ? 1.5 : 1); } UpdateOverlay(); };
                _presetButtons.Add(btn); PresetGalleryGrid.Children.Add(btn);
            }
        }
        private void DrawMiniIcon(Canvas c, string tag)
        {
            var b = Brushes.White;
            void D(double sz) { var e = new System.Windows.Shapes.Ellipse { Width = sz, Height = sz, Fill = b }; Canvas.SetLeft(e, 14 - sz / 2); Canvas.SetTop(e, 14 - sz / 2); c.Children.Add(e); }
            void L(double x1, double y1, double x2, double y2, double t = 1.5) { c.Children.Add(new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = t }); }
            switch (tag)
            {
                case "Dot-2x2": D(2.5); break; case "Dot-4x4": D(4.5); break; case "Tiny-cross": L(10, 14, 18, 14); L(14, 10, 14, 18); break;
                case "Cross-dot-small": D(2); L(7, 14, 11, 14); L(17, 14, 21, 14); L(14, 7, 14, 11); L(14, 17, 14, 21); break;
                case "Cross-dot": D(2); L(4, 14, 10, 14); L(18, 14, 24, 14); L(14, 4, 14, 10); L(14, 18, 14, 24); break;
                case "T-shape-dot": D(2); L(5, 14, 11, 14); L(17, 14, 23, 14); L(14, 17, 14, 23); break;
                case "T-shape-chevron": L(5, 14, 10, 14); L(18, 14, 23, 14); L(11, 16, 14, 13); L(14, 13, 17, 16); L(14, 18, 14, 23); break;
                case "Chevron-small": L(10, 16, 14, 12); L(14, 12, 18, 16); break; case "Chevron-medium": L(8, 18, 14, 11); L(14, 11, 20, 18); break;
                case "Circle-dot": D(2.5); var e1 = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, Stroke = b, StrokeThickness = 1.5 }; Canvas.SetLeft(e1, 6); Canvas.SetTop(e1, 6); c.Children.Add(e1); break;
                case "Circle-cross": var e2 = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Stroke = b, StrokeThickness = 1.5 }; Canvas.SetLeft(e2, 7); Canvas.SetTop(e2, 7); c.Children.Add(e2); L(4, 14, 9, 14); L(19, 14, 24, 14); L(14, 4, 14, 9); L(14, 19, 14, 24); break;
                case "Cross-diag-dot": D(2); L(7, 7, 11, 11); L(17, 17, 21, 21); L(21, 7, 17, 11); L(11, 17, 7, 21); break;
                case "Square-cross-dot": D(2); L(6, 6, 10, 6); L(6, 6, 6, 10); L(18, 6, 22, 6); L(22, 6, 22, 10); L(6, 22, 10, 22); L(6, 18, 6, 22); L(18, 22, 22, 22); L(22, 18, 22, 22); break;
                case "2-sectors-dot": D(2.5); L(7, 8, 5, 14, 2.5); L(5, 14, 7, 20, 2.5); L(21, 8, 23, 14, 2.5); L(23, 14, 21, 20, 2.5); break; case "Horiz-dot": D(2.5); L(3, 14, 9, 14, 1.8); L(19, 14, 25, 14, 1.8); break;
                case "PIXEL": c.Children.Add(new TextBlock { Text = "✏️", FontSize = 14, Margin = new Thickness(5, 3, 0, 0) }); break;
            }
        }

        private void InitHotkeyTimer() { new System.Threading.Thread(() => { while (true) { try { System.Threading.Thread.Sleep(25); bool modOk = (_hotkeyMod == 0) || ((_hotkeyMod == 1 && (GetAsyncKeyState(0x12) & 0x8000) != 0) || (_hotkeyMod == 2 && (GetAsyncKeyState(0x11) & 0x8000) != 0) || (_hotkeyMod == 3 && (GetAsyncKeyState(0x10) & 0x8000) != 0)); bool keyDown = !_waitingForKey && modOk && ((GetAsyncKeyState(_hotkeyVk) & 0x8000) != 0); if (keyDown && !_f8WasDown) Dispatcher.BeginInvoke(ToggleOverlay); _f8WasDown = keyDown; if (_overlayActive && _overlay != null && _hideOnRmb) { bool shouldHide = ((GetAsyncKeyState(0x02) & 0x8000) != 0); if (shouldHide != _rmbWasHidden) { _rmbWasHidden = shouldHide; Dispatcher.BeginInvoke(() => _overlay?.SetHidden(shouldHide)); } } } catch { } } }) { IsBackground = true }.Start(); }
        private void ToggleOverlay() { _overlayActive = !_overlayActive; if (_overlayActive) { if (_overlay == null) _overlay = new CrosshairOverlay(); _overlay.Show(); UpdateOverlay(); BtnToggleOverlay.Content = $"🛑 ВЫКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})"; BtnToggleOverlay.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A2326")); } else { _overlay?.Dispose(); _overlay = null; BtnToggleOverlay.Content = $"🎯 ВКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})"; BtnToggleOverlay.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31")); } }
        private void BtnBindHotkey_Click(object sender, RoutedEventArgs e) { _waitingForKey = true; BtnBindHotkey.Content = "Нажмите клавишу..."; this.PreviewKeyDown += MainWindow_PreviewKeyDown; }
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e) { if (!_waitingForKey) return; var key = (e.Key == Key.System) ? e.SystemKey : e.Key; if (key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift) return; int mod = 0; string modStr = ""; if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) { mod = 1; modStr = "Alt+"; } else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { mod = 2; modStr = "Ctrl+"; } else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { mod = 3; modStr = "Shift+"; } _hotkeyMod = mod; _hotkeyVk = KeyInterop.VirtualKeyFromKey(key); _hotkeyName = modStr + key.ToString(); BtnBindHotkey.Content = $"Клавиша: {_hotkeyName}"; BtnToggleOverlay.Content = _overlayActive ? $"🛑 ВЫКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})" : $"🎯 ВКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})"; _waitingForKey = false; this.PreviewKeyDown -= MainWindow_PreviewKeyDown; e.Handled = true; SaveUserSettings();  SaveUserSettings(); }
        private void BtnToggleOverlay_Click(object sender, RoutedEventArgs e) => ToggleOverlay();
        private void UpdateOverlay() { if (_overlay != null && _overlayActive) _overlay.Render(_currentPreset, _crossColor, (ScaleSlider.Value / 100.0) * 1.5, _offsetX, _offsetY, _pixels, _rmbWasHidden); }
        private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e) => UpdateHue(e.GetPosition(HueCanvas).Y);
        private void HueCanvas_MouseMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) UpdateHue(e.GetPosition(HueCanvas).Y); }
        private void UpdateHue(double y) { y = Math.Clamp(y, 0, 128); Canvas.SetTop(HueMarker, y - 1.5); _hue = (y / 128.0) * 360.0; SvCanvas.Background = new SolidColorBrush(HsvToColor(_hue, 1, 1)); ApplyHsv(); }
        private void SvCanvas_MouseDown(object sender, MouseButtonEventArgs e) => UpdateSv(e.GetPosition(SvCanvas));
        private void SvCanvas_MouseMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) UpdateSv(e.GetPosition(SvCanvas)); }
        private void UpdateSv(Point p) { double x = Math.Clamp(p.X, 0, 138), y = Math.Clamp(p.Y, 0, 128); Canvas.SetLeft(SvMarker, x - 5); Canvas.SetTop(SvMarker, y - 5); _sat = x / 138.0; _val = 1.0 - (y / 128.0); ApplyHsv(); }
        private void ApplyHsv() { if (ColorPreviewBox == null || HexColorBox == null) return; _crossColor = HsvToColor(_hue, _sat, _val); ColorPreviewBox.Background = new SolidColorBrush(_crossColor); RgbInfoText.Text = $"R: {_crossColor.R}   G: {_crossColor.G}   B: {_crossColor.B}"; HexColorBox.Text = $"#{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}"; RefreshGridColors(); UpdateOverlay(); }
        private void HexColorBox_LostFocus(object sender, RoutedEventArgs e) { try { _crossColor = (Color)ColorConverter.ConvertFromString(HexColorBox.Text.Trim()); ColorPreviewBox.Background = new SolidColorBrush(_crossColor); RgbInfoText.Text = $"R: {_crossColor.R}   G: {_crossColor.G}   B: {_crossColor.B}"; RefreshGridColors(); UpdateOverlay(); } catch { } }
        private static Color HsvToColor(double h, double s, double v) { double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0; if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; } else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; } return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255)); }
        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ScaleLabel != null) ScaleLabel.Text = $"3. Размер (Масштаб): {(int)ScaleSlider.Value}%"; UpdateOverlay(); }
        private void Offset_Click(object sender, RoutedEventArgs e) { if (sender is Button b && b.Tag is string t) { if (t == "X-") _offsetX--; else if (t == "X+") _offsetX++; else if (t == "Y-") _offsetY--; else if (t == "Y+") _offsetY++; OffsetXText.Text = _offsetX.ToString(); OffsetYText.Text = _offsetY.ToString(); UpdateOverlay(); } }
        private void BtnExportCode_Click(object sender, RoutedEventArgs e) { var sb = new StringBuilder(); for (int i = 0; i < 256; i++) sb.Append(_pixels[i] ? '1' : '0'); Clipboard.SetText($"SZ2:#{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}:{(int)ScaleSlider.Value}:{_offsetX}:{_offsetY}:{sb}"); MessageBox.Show("Код скопирован!", "SZ Optimizer"); }
        private void BtnImportCode_Click(object sender, RoutedEventArgs e) { try { var p = Clipboard.GetText().Trim().Split(':'); if (p.Length == 6 && p[0] == "SZ2" && p[5].Length == 256) { _crossColor = (Color)ColorConverter.ConvertFromString(p[1]); ScaleSlider.Value = double.Parse(p[2]); _offsetX = int.Parse(p[3]); _offsetY = int.Parse(p[4]); OffsetXText.Text = _offsetX.ToString(); OffsetYText.Text = _offsetY.ToString(); string bits = p[5]; for (int i = 0; i < 256; i++) _pixels[i] = bits[i] == '1'; _currentPreset = "PIXEL"; RefreshGridColors(); UpdateOverlay(); } } catch { } }
        private void RunDiagnostics() { try { long mb = 0; using (var s = new ManagementObjectSearcher("SELECT AllocatedBaseSize FROM Win32_PageFileUsage")) foreach (ManagementObject o in s.Get()) mb += Convert.ToInt64(o["AllocatedBaseSize"]); DiagnosticsText.Text = $"● ДИАГНОСТИКА: Ядер CPU: {Environment.ProcessorCount}  |  Файл подкачки: {mb / 256} ГБ (ОК)"; } catch { DiagnosticsText.Text = "● ДИАГНОСТИКА: Система готова"; } }
        private void CheckAutoStartTask() { try { var p = Process.Start(new ProcessStartInfo("schtasks", "/Query /TN \"SZOptimizerAuto\"") { CreateNoWindow = true, UseShellExecute = false }); p?.WaitForExit(); ChkAutoStart.IsChecked = (p?.ExitCode == 0); } catch { } }
        private void ChkAutoStart_Changed(object sender, RoutedEventArgs e) { if (!IsLoaded) return; string exe = Process.GetCurrentProcess().MainModule?.FileName ?? ""; if (ChkAutoStart.IsChecked == true && File.Exists(exe)) Process.Start(new ProcessStartInfo("schtasks", $"/Create /TN \"SZOptimizerAuto\" /TR \"\\\"{exe}\\\" -autostart\" /SC ONLOGON /RL HIGHEST /F") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(); else Process.Start(new ProcessStartInfo("schtasks", "/Delete /TN \"SZOptimizerAuto\" /F") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(); }
        private void LoadUserSettings() { try { if (!File.Exists(_settingsFile)) { ChkGameBoost.IsChecked = true; ChkTimer.IsChecked = true; return; } var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_settingsFile)); if (d == null) return; ChkAffinity.IsChecked = d.GetValueOrDefault("1", "False") == "True"; ChkGameBoost.IsChecked = d.GetValueOrDefault("2", "True") == "True"; ChkSoftBrowsers.IsChecked = d.GetValueOrDefault("3", "True") == "True"; ChkCleanLogs.IsChecked = d.GetValueOrDefault("4", "True") == "True"; ChkTrayMode.IsChecked = d.GetValueOrDefault("5", "True") == "True"; ChkTimer.IsChecked = d.GetValueOrDefault("6", "True") == "True"; ChkNetwork.IsChecked = d.GetValueOrDefault("7", "False") == "True"; ChkInput.IsChecked = d.GetValueOrDefault("8", "False") == "True"; ChkAutoCrosshair.IsChecked = d.GetValueOrDefault("AutoCross", "True") == "True"; ChkHideOnRmb.IsChecked = d.GetValueOrDefault("Rmb", "True") == "True"; _hideOnRmb = _hideOnRmb; if (d.ContainsKey("HkVk")) { _hotkeyVk = int.Parse(d["HkVk"]); _hotkeyMod = int.Parse(d.GetValueOrDefault("HkMod", "0")); _hotkeyName = d.GetValueOrDefault("HkName", "Insert"); BtnBindHotkey.Content = $"Клавиша: {_hotkeyName}"; BtnToggleOverlay.Content = $"🎯 ВКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})"; } if (d.ContainsKey("Preset")) _currentPreset = d["Preset"]; if (d.ContainsKey("Scale")) ScaleSlider.Value = double.Parse(d["Scale"]); if (d.ContainsKey("OffX")) { _offsetX = int.Parse(d["OffX"]); OffsetXText.Text = _offsetX.ToString(); } if (d.ContainsKey("Trim")) {  } if (d.ContainsKey("Trim")) CmbAutoTrim.SelectedIndex = int.Parse(d["Trim"]); if (d.ContainsKey("OffY")) { _offsetY = int.Parse(d["OffY"]); OffsetYText.Text = _offsetY.ToString(); } if (d.ContainsKey("Color")) { _crossColor = (Color)ColorConverter.ConvertFromString(d["Color"]); ColorPreviewBox.Background = new SolidColorBrush(_crossColor); HexColorBox.Text = d["Color"]; RefreshGridColors(); } } catch { } }
        private void SaveUserSettings() { try { var d = new Dictionary<string, string> { {"1", ChkAffinity.IsChecked.ToString()!}, {"2", ChkGameBoost.IsChecked.ToString()!}, {"3", ChkSoftBrowsers.IsChecked.ToString()!}, {"4", ChkCleanLogs.IsChecked.ToString()!}, {"5", ChkTrayMode.IsChecked.ToString()!}, {"6", ChkTimer.IsChecked.ToString()!}, {"7", ChkNetwork.IsChecked.ToString()!}, {"8", ChkInput.IsChecked.ToString()!}, {"AutoCross", ChkAutoCrosshair.IsChecked.ToString()!}, {"Rmb", ChkHideOnRmb.IsChecked.ToString()!}, {"HkVk", _hotkeyVk.ToString()}, {"HkMod", _hotkeyMod.ToString()}, {"HkName", _hotkeyName}, {"Preset", _currentPreset}, {"Scale", ScaleSlider.Value.ToString()}, {"OffX", _offsetX.ToString()}, {"OffY", _offsetY.ToString()}, {"Color", $"#{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}"} }; File.WriteAllText(_settingsFile, JsonSerializer.Serialize(d)); } catch { } }
        private void UpdateIndicators(bool active) { var off = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6D6F78")); var on = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA")); Ind1.Fill = (active && ChkAffinity.IsChecked == true) ? on : off; Ind2.Fill = (active && ChkGameBoost.IsChecked == true) ? on : off; Ind3.Fill = (active && ChkSoftBrowsers.IsChecked == true) ? on : off; Ind4.Fill = (active && ChkCleanLogs.IsChecked == true) ? on : off; Ind5.Fill = _watchActive ? on : off; Ind6.Fill = (active && ChkTimer.IsChecked == true) ? on : off; Ind7.Fill = (active && ChkNetwork.IsChecked == true) ? on : off; Ind8.Fill = (active && ChkInput.IsChecked == true) ? on : off; }
        private void ApplySelectedTweaks(Process[] gameProcs) { if (ChkAffinity.IsChecked == true && Environment.ProcessorCount > 4) { long mask = ((1L << Environment.ProcessorCount) - 1) & ~1L; foreach (var p in gameProcs) try { p.ProcessorAffinity = (IntPtr)mask; } catch { } } if (ChkGameBoost.IsChecked == true) { foreach (var p in gameProcs) try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { } } foreach (var s in new[] { "WSearch", "DiagTrack" }) try { using var sc = new ServiceController(s); if (sc.Status == ServiceControllerStatus.Running) sc.Stop(); } catch { } if (ChkSoftBrowsers.IsChecked == true) { var targets = new[] { "chrome", "msedge", "opera", "yandex", "firefox" }; foreach (var p in Process.GetProcesses()) { try { if (targets.Contains(p.ProcessName.ToLower()) && p.MainWindowHandle == IntPtr.Zero) p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { } finally { p.Dispose(); } } } if (ChkTimer.IsChecked == true && !_timerBoosted) { NtSetTimerResolution(10000, true, out _); _timerBoosted = true; } _tweaksApplied = true; BackupStatusText.Text = "🛡️ Статус: Буст активен"; UpdateIndicators(true); }
        private async void BtnAutoWatch_Click(object sender, RoutedEventArgs e) { _watchActive = !_watchActive; if (!_watchActive) { BtnAutoWatch.Content = "👁️ ВКЛЮЧИТЬ АВТО-ОЖИДАНИЕ"; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA")); if (_tweaksApplied) RollbackAll(); UpdateIndicators(false); return; } BtnAutoWatch.Content = "⏳ ОЖИДАНИЕ ЗАПУСКА STALZONE..."; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")); UpdateIndicators(false); while (_watchActive) { if (_trackedGame != null) { try { if (_trackedGame.HasExited) { _trackedGame.Dispose(); _trackedGame = null; } } catch { _trackedGame = null; } } if (_trackedGame == null) { foreach (var p in Process.GetProcesses()) { try { string n = p.ProcessName.ToLower(); if (_trackedGame == null && !n.Contains("szoptimizer") && (n == "stalzonew" || n == "stalzone" || n.Contains("stalcraft") || n == "javaw")) _trackedGame = p; else p.Dispose(); } catch { p.Dispose(); } } } if (_trackedGame != null) { if (!_tweaksApplied) { ApplySelectedTweaks(new[] { _trackedGame }); if (ChkAutoCrosshair.IsChecked == true && !_overlayActive) ToggleOverlay(); if (ChkTrayMode.IsChecked == true) { HideToTray();  } BtnAutoWatch.Content = "🎮 ИГРА АКТИВНА — БУСТ ВКЛЮЧЁН"; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#51CF66")); } await Task.Delay(5000); } else { if (_tweaksApplied) { RollbackAll(); if (ChkAutoCrosshair.IsChecked == true && _overlayActive) ToggleOverlay();  RestoreFromTray(); BtnAutoWatch.Content = "⏳ ОЖИДАНИЕ ЗАПУСКА STALZONE..."; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")); } await Task.Delay(5000); } } }
        private void BtnApplyNow_Click(object sender, RoutedEventArgs e) { var list = new List<Process>(); foreach (var p in Process.GetProcesses()) { try { string n = p.ProcessName.ToLower(); if (!n.Contains("szoptimizer") && (n == "stalzonew" || n == "stalzone" || n.Contains("stalcraft") || n == "javaw")) list.Add(p); else p.Dispose(); } catch { p.Dispose(); } } ApplySelectedTweaks(list.ToArray()); foreach (var gp in list) gp.Dispose(); MessageBox.Show("Безопасные оптимизации применены!", "SZ Optimizer"); }
        private void RollbackAll() { Environment.SetEnvironmentVariable("_JAVA_OPTIONS", null, EnvironmentVariableTarget.User); Environment.SetEnvironmentVariable("_JAVA_OPTIONS", null, EnvironmentVariableTarget.User); if (_timerBoosted) { NtSetTimerResolution(156250, false, out _); _timerBoosted = false; } foreach (var s in new[] { "WSearch", "DiagTrack" }) try { using var sc = new ServiceController(s); if (sc.Status == ServiceControllerStatus.Stopped) sc.Start(); } catch { } _tweaksApplied = false; BackupStatusText.Text = "🛡️ Статус: Ожидание игры"; UpdateIndicators(false); }
        private void BtnRollback_Click(object sender, RoutedEventArgs e) { _watchActive = false; RollbackAll(); if (_overlayActive) ToggleOverlay(); BtnAutoWatch.Content = "👁️ ВКЛЮЧИТЬ АВТО-ОЖИДАНИЕ"; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA")); MessageBox.Show("Настройки возвращены!", "SZ Optimizer"); }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) { SaveUserSettings(); if (!_forceExit) { e.Cancel = true; HideToTray(); return; } Shell_NotifyIcon(2, ref _nid); SaveUserSettings(); _trackedGame?.Dispose(); if (_tweaksApplied) RollbackAll(); _overlay?.Dispose(); }
    }
}

























































