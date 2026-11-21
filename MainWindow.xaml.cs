using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SZOptimizer
{
    public partial class MainWindow : Window
    {
        private readonly string _settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private bool _timerBoosted = false, _watchActive = false, _tweaksApplied = false, _isLoading = false;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NOTIFYICONDATA { public int cbSize; public IntPtr hWnd; public int uID; public int uFlags; public int uCallbackMessage; public IntPtr hIcon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
        [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
        private NOTIFYICONDATA _nid;
        private bool _forceExit = false;
        private Process? _trackedGame;
        private CrosshairOverlay? _overlay;
        private bool _overlayActive = false, _f8WasDown = false, _rmbWasHidden = false, _hideOnRmb = true, _waitingForKey = false;
                private int _hotkeyVk = 0x2D, _hotkeyMod = 0, _profVk = 0, _profMod = 0, _holsterVk = 0x48, _activeCrossProf = 1, _curWeaponSlot = 1;
        private bool _rmbHidden = false, _profKeyWasDown = false, _waitingForProfKey = false, _waitingForHolsterKey = false, _w1WasDown = false, _w2WasDown = false, _w3WasDown = false, _w4WasDown = false, _w5WasDown = false, _holsterWasDown = false, _weaponHidden = false, _chatTyping = false, _enterWasDown = false, _tWasDown = false;
        private readonly string[] _profData = new string[] { "", "Dot-2x2|#FF4D4F|100|0|0|" + new string('0', 256), "Cross-Small|#00D4AA|100|0|0|" + new string('0', 256), "Dot-1x1|#FFD700|100|0|0|" + new string('0', 256), "Dot-1x1|#4DABF7|80|0|0|" + new string('0', 256) };
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
        private LowLevelMouseProc? _mouseHookProc;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private string _hotkeyName = "Insert", _currentPreset = "Cross-dot";
        private Color _crossColor = Colors.White;
        private double _hue = 168.0, _sat = 1.0, _val = 0.83;
        private int _offsetX = 0, _offsetY = 0;
        private readonly List<bool> _pixels = Enumerable.Repeat(false, 256).ToList();
        private readonly List<Button> _gridButtons = new(), _presetButtons = new();
        private int _autoTrimSec = 600, _trimTick = 0; private string _customGameDir = "";

                [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);
        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] byte[] pathArray, ref uint numModeInfoArrayElements, [Out] byte[] modeInfoArray, IntPtr currentTopologyId);
        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPathArrayElements, byte[] pathArray, uint numModeInfoArrayElements, byte[] modeInfoArray, uint flags);

                private static void AutoClickIntelStretchedAsync()
        {
            Task.Run(async () =>
            {
                try
                {
                    bool launchedByUs = false;
                    var proc = Process.GetProcessesByName("IntelGraphicsCommandCenter").FirstOrDefault();
                    if (proc == null)
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\AppUp.IntelGraphicsExperience_8j3eq9eme6ctt!App") { UseShellExecute = true });
                        launchedByUs = true;
                        await Task.Delay(2500);
                    }
                    var root = System.Windows.Automation.AutomationElement.RootElement;
                    var winCond = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "Центр управления графикой Intel");
                    var winCondEn = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "Intel® Graphics Command Center");
                    var win = root.FindFirst(System.Windows.Automation.TreeScope.Children, new System.Windows.Automation.OrCondition(winCond, winCondEn));
                    if (win != null)
                    {
                        var comboCond = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.ComboBox);
                        var combos = win.FindAll(System.Windows.Automation.TreeScope.Descendants, comboCond);
                        foreach (System.Windows.Automation.AutomationElement cb in combos)
                        {
                            if (cb.TryGetCurrentPattern(System.Windows.Automation.ExpandCollapsePattern.Pattern, out object pat))
                            {
                                ((System.Windows.Automation.ExpandCollapsePattern)pat).Expand();
                                await Task.Delay(200);
                                var items = cb.FindAll(System.Windows.Automation.TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                                foreach (System.Windows.Automation.AutomationElement it in items)
                                {
                                    string n = it.Current.Name ?? "";
                                    if (n.Contains("Stretched", StringComparison.OrdinalIgnoreCase) || n.Contains("Растянут", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (it.TryGetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern, out object selPat))
                                            ((System.Windows.Automation.SelectionItemPattern)selPat).Select();
                                        break;
                                    }
                                }
                                ((System.Windows.Automation.ExpandCollapsePattern)pat).Collapse();
                            }
                        }
                        if (launchedByUs && win.TryGetCurrentPattern(System.Windows.Automation.WindowPattern.Pattern, out object wPat))
                            ((System.Windows.Automation.WindowPattern)wPat).Close();
                    }
                }
                catch { }
            });
        }
        private static void ForceIntelStretchedScaling()
        {
            try
            {
                if (GetDisplayConfigBufferSizes(2, out uint paths, out uint modes) != 0) return;
                byte[] pathBuf = new byte[paths * 72];
                byte[] modeBuf = new byte[modes * 64];
                if (QueryDisplayConfig(2, ref paths, pathBuf, ref modes, modeBuf, IntPtr.Zero) != 0) return;
                for (int i = 0; i < paths; i++)
                {
                    int scalingOffset = i * 72 + 56;
                    if (scalingOffset + 4 <= pathBuf.Length)
                        BitConverter.GetBytes(3).CopyTo(pathBuf, scalingOffset);
                }
                SetDisplayConfig(paths, pathBuf, modes, modeBuf, 0x00000080 | 0x00000002 | 0x00000004);
                using var cfgRoot = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration", true);
                if (cfgRoot != null)
                {
                    foreach (var sub1 in cfgRoot.GetSubKeyNames())
                    {
                        using var k1 = cfgRoot.OpenSubKey(sub1, true);
                        if (k1 == null) continue;
                        foreach (var sub2 in k1.GetSubKeyNames())
                        {
                            using var k2 = k1.OpenSubKey(sub2, true);
                            if (k2?.GetValue("Scaling") != null) k2.SetValue("Scaling", 3, Microsoft.Win32.RegistryValueKind.DWord);
                            if (k2 == null) continue;
                            foreach (var sub3 in k2.GetSubKeyNames())
                            {
                                using var k3 = k2.OpenSubKey(sub3, true);
                                if (k3?.GetValue("Scaling") != null) k3.SetValue("Scaling", 3, Microsoft.Win32.RegistryValueKind.DWord);
                            }
                        }
                    }
                }
            }
            catch { }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DEVMODE { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName; public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput; public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName; public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);
                [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE { public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString; public int StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct SZMONINFO { public int cbSize; public int rcL, rcT, rcR, rcB; public int wL, wT, wR, wB; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref SZMONINFO lpmi);
        private static void FixGameWindowBottomGap(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            int style = GetWindowLong(hWnd, -16);
            int cleanStyle = style & ~(0x00C00000 | 0x00040000 | 0x00800000 | 0x00400000);
            if (style != cleanStyle) SetWindowLong(hWnd, -16, cleanStyle);
            IntPtr hMon = MonitorFromWindow(hWnd, 2);
            var mi = new SZMONINFO { cbSize = Marshal.SizeOf<SZMONINFO>() };
            if (GetMonitorInfo(hMon, ref mi))
                SetWindowPos(hWnd, IntPtr.Zero, mi.rcL, mi.rcT, mi.rcR - mi.rcL, (mi.rcB - mi.rcT) + 2, 0x0040 | 0x0004);
        }
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);
        private sealed class MonInfo { public string DevName = ""; public bool Primary; public int NatW, NatH, NatHz, MaxW, MaxH, MaxHz, SavedW, SavedH, SavedHz; }
        private readonly List<MonInfo> _monitors = new();
        private int _selMon = 0;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);
        private int _nativeW = 1920, _nativeH = 1080, _nativeHz = 60, _maxMonW = 1920, _maxMonH = 1080, _maxMonHz = 60, _targetResW = 0, _targetResH = 0, _targetResHz = 0;
        private bool _resAppliedByGame = false;
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("ntdll.dll")] private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        public MainWindow()
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            InitializeComponent(); Loaded += (s, e) => InitExtraUiAndJvmAck(); ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(50)); ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(25000)); ToolTipService.BetweenShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(0));
            ChkHideOnRmb.Checked += (s, e) => { _hideOnRmb = true; SaveUserSettings(); };
            ChkHideOnRmb.Unchecked += (s, e) => { _hideOnRmb = false; SaveUserSettings(); };
            InitPixelEditor();
            InitPresetGallery();
            InitHotkeyTimer(); InstallWheelHook();
            RunDiagnostics();
            CheckAutoStartTask();
            ApplyOsCompatibilityUi(); InitHardwareTab(); RefreshServerRegionUi(); InitResolutionTab(); InitJvmTab();
            bool hasConfig = File.Exists(_settingsFile);
            LoadUserSettings();
            if (!hasConfig) AutoTuneByHardware(false);
            else UpdateAutoProfileLabel();
            new Thread(() => { while (true) { Thread.Sleep(1000); if (_autoTrimSec > 0) { _trimTick++; if (_trimTick >= _autoTrimSec) { _trimTick = 0; RunSilentTrim(); } } } }) { IsBackground = true }.Start();
            this.Loaded += (s, e) => { SyncHsvVisualsFromColor(_crossColor); if (!_watchActive) BtnAutoWatch_Click(this, new RoutedEventArgs()); };
        }
        protected override void OnSourceInitialized(EventArgs e) { base.OnSourceInitialized(e); IntPtr hwnd = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(hwnd)?.AddHook(TrayWndProc); _nid = new NOTIFYICONDATA { cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = hwnd, uID = 1, uFlags = 7, uCallbackMessage = 0x8001, hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)), szTip = "SZ Optimizer (STALZONE)" }; Shell_NotifyIcon(0, ref _nid); if (Environment.GetCommandLineArgs().Contains("-autostart")) HideToTray(); }
        private IntPtr TrayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) { if (msg == 0x8001) { int ev = lParam.ToInt32(); if (ev == 0x0202 || ev == 0x0203) RestoreFromTray(); else if (ev == 0x0205) { var cm = new ContextMenu(); var m1 = new MenuItem { Header = "Открыть SZ Optimizer" }; m1.Click += (s, a) => RestoreFromTray(); var m2 = new MenuItem { Header = "Выход" }; m2.Click += (s, a) => { _forceExit = true; Close(); }; cm.Items.Add(m1); cm.Items.Add(m2); cm.IsOpen = true; } } return IntPtr.Zero; }
        private void HideToTray() { this.Hide(); this.ShowInTaskbar = false; }
        private void RestoreFromTray() { this.Show(); this.ShowInTaskbar = true; this.WindowState = WindowState.Normal; this.Activate(); }
        protected override void OnStateChanged(EventArgs e) { if (this.WindowState == WindowState.Minimized) HideToTray(); base.OnStateChanged(e); }
                                private void SelectTab(Button activeBtn, UIElement activePanel)
        {
            PanelWindows.Visibility = Visibility.Collapsed;
            PanelResolution.Visibility = Visibility.Collapsed;
            PanelCrosshair.Visibility = Visibility.Collapsed;
            PanelHardware.Visibility = Visibility.Collapsed; if (PanelJvm != null) PanelJvm.Visibility = Visibility.Collapsed; if (PanelSzCfg != null) PanelSzCfg.Visibility = Visibility.Collapsed; if (PanelSettings != null) PanelSettings.Visibility = Visibility.Collapsed; if (PanelJvm != null) PanelJvm.Visibility = Visibility.Collapsed; if (PanelSzCfg != null) PanelSzCfg.Visibility = Visibility.Collapsed; if (PanelSettings != null) PanelSettings.Visibility = Visibility.Collapsed;
            TabWinBtn.Background = Brushes.Transparent;
            TabResBtn.Background = Brushes.Transparent;
            TabCrossBtn.Background = Brushes.Transparent;
            TabHwBtn.Background = Brushes.Transparent; if (TabJvmBtn != null) TabJvmBtn.Background = Brushes.Transparent; if (TabSzCfgBtn != null) { TabSzCfgBtn.Background = Brushes.Transparent; if (TabSettingsBtn != null) TabSettingsBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18191C")); TabSzCfgBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B5BAC1")); } if (TabJvmBtn != null) TabJvmBtn.Background = Brushes.Transparent; if (TabSzCfgBtn != null) { TabSzCfgBtn.Background = Brushes.Transparent; if (TabSettingsBtn != null) TabSettingsBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18191C")); TabSzCfgBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B5BAC1")); }
            activePanel.Visibility = Visibility.Visible;
            activeBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31"));
        }
        private void TabWinBtn_Click(object sender, RoutedEventArgs e) => SelectTab(TabWinBtn, PanelWindows);
        private void TabResBtn_Click(object sender, RoutedEventArgs e) => SelectTab(TabResBtn, PanelResolution);
        private void TabCrossBtn_Click(object sender, RoutedEventArgs e) => SelectTab(TabCrossBtn, PanelCrosshair);
        private void TabHwBtn_Click(object sender, RoutedEventArgs e) => SelectTab(TabHwBtn, PanelHardware);         private int _currentThemeIdx = 0;
        private double _panelOpacity = 0.75;
        private string JvmAckFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SZOptimizer", "jvm_ack.flag");
        private string ExtraCfgFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SZOptimizer", "extra_ui.txt");

        private void InitExtraUiAndJvmAck()
        {
            if (File.Exists(JvmAckFile) && JvmDisclaimerBanner != null && JvmControlsContainer != null)
            {
                JvmDisclaimerBanner.Visibility = Visibility.Collapsed;
                JvmControlsContainer.IsEnabled = true;
                JvmControlsContainer.Opacity = 1.0;
            }
            if (File.Exists(ExtraCfgFile))
            {
                try
                {
                    var p = File.ReadAllText(ExtraCfgFile).Split('|');
                    if (p.Length >= 5)
                    {
                        int.TryParse(p[0], out _currentThemeIdx);
                        double.TryParse(p[1].Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out _panelOpacity);
                        if (ChkStartMinimized != null) ChkStartMinimized.IsChecked = p[2] == "1";
                        if (ChkMinimizeOnClose != null) ChkMinimizeOnClose.IsChecked = p[3] == "1";
                        if (ChkSoundFeedback != null) ChkSoundFeedback.IsChecked = p[4] == "1";
                        if (SldPanelOpacity != null) SldPanelOpacity.Value = Math.Clamp(_panelOpacity * 100, 45, 98);
                    }
                }
                catch { }
            }
            ApplyThemeLive(_currentThemeIdx, _panelOpacity);
            _isLoading = false; if (ChkStartMinimized?.IsChecked == true) WindowState = WindowState.Minimized;
        }

        private void SaveExtraUiCfg()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ExtraCfgFile)!);
                File.WriteAllText(ExtraCfgFile, $"{_currentThemeIdx}|{_panelOpacity.ToString("F2", CultureInfo.InvariantCulture)}|{(ChkStartMinimized?.IsChecked == true ? 1 : 0)}|{(ChkMinimizeOnClose?.IsChecked == true ? 1 : 0)}|{(ChkSoundFeedback?.IsChecked == true ? 1 : 0)}");
            }
            catch { }
        }

        private void BtnAcknowledgeJvm_Click(object sender, RoutedEventArgs e)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(JvmAckFile)!); File.WriteAllText(JvmAckFile, "1"); } catch { }
            if (JvmDisclaimerBanner != null) JvmDisclaimerBanner.Visibility = Visibility.Collapsed;
            if (JvmControlsContainer != null) { JvmControlsContainer.IsEnabled = true; JvmControlsContainer.Opacity = 1.0; }
        }

                private void TabSettingsBtn_Click(object sender, RoutedEventArgs e) => SelectTab(TabSettingsBtn, PanelSettings);

        private void ApplyThemeLive(int idx, double opacity)
        {
            _currentThemeIdx = Math.Clamp(idx, 0, 5);
            _panelOpacity = Math.Clamp(opacity, 0.45, 0.98);
            string[] accents = { "#00D4AA", "#E03E42", "#76FF03", "#FF7A00", "#00E5FF", "#F5C542" };
            string[] cardHex = { "#161D1E", "#1F1719", "#172117", "#211B16", "#141A29", "#212017" };
            string[] sideHex = { "#111617", "#171214", "#111711", "#171310", "#0E121C", "#171611" };

            var accentColor = (Color)ColorConverter.ConvertFromString(accents[_currentThemeIdx]);
            var accentBrush = new SolidColorBrush(accentColor);

            byte alpha = (byte)(_panelOpacity * 255);
            var cCard = (Color)ColorConverter.ConvertFromString(cardHex[_currentThemeIdx]);
            cCard.A = alpha;
            var cardBrush = new SolidColorBrush(cCard);

            var cSide = (Color)ColorConverter.ConvertFromString(sideHex[_currentThemeIdx]);
            cSide.A = (byte)Math.Min(255, alpha + 35);
            var sideBrush = new SolidColorBrush(cSide);

            if (Content is Grid rootGrid)
            {
                try
                {
                    var uri = new Uri($"pack://application:,,,/Assets/bg_{_currentThemeIdx}.png", UriKind.Absolute);
                    rootGrid.Background = new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(uri)) { Stretch = Stretch.UniformToFill, Opacity = 0.96 };
                }
                catch { }

                foreach (var b in FindVisualChildren<Border>(rootGrid))
                {
                    if (b.BorderThickness.Right == 1 && b.CornerRadius.TopLeft == 0)
                        b.Background = sideBrush;
                    else if (b.Background is SolidColorBrush sb && sb.Color.R < 55 && sb.Color.G < 55 && sb.Color.B < 60 && b.CornerRadius.TopLeft >= 6)
                        b.Background = cardBrush;
                }

                foreach (var tb in FindVisualChildren<TextBlock>(rootGrid))
                {
                    if (tb.Name != "JvmStatusText" && tb.Name != "ServerRegionText" && !(System.Windows.Media.VisualTreeHelper.GetParent(tb) is StackPanel sp && System.Windows.Media.VisualTreeHelper.GetParent(sp) is ContentPresenter cp && cp.TemplatedParent is CheckBox) && tb.Foreground is SolidColorBrush fg && (accents.Any(h => (Color)ColorConverter.ConvertFromString(h) == fg.Color) || fg.Color == Colors.White) && tb.Text != "!") { if (tb.FontWeight == FontWeights.Bold || tb.Text == "?" || tb.Name == "DiagnosticsText" || tb.Name == "DiagText" || tb.Name == "BackupStatusText" || tb.Name == "TxtOpacityVal") tb.Foreground = accentBrush; else tb.Foreground = Brushes.White; } if (false)
                        tb.Foreground = accentBrush;
                }

                if (BtnAutoWatch != null) { BtnAutoWatch.Background = accentBrush; BtnAutoWatch.Foreground = _currentThemeIdx == 1 ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111214")); }
                if (BtnApplyHw != null) { BtnApplyHw.Background = accentBrush; BtnApplyHw.Foreground = _currentThemeIdx == 1 ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111214")); }
                if (TabSettingsBtn != null) TabSettingsBtn.Foreground = accentBrush;
            }
            if (TxtOpacityVal != null) TxtOpacityVal.Text = $"{(int)(_panelOpacity * 100)}%";
            SaveExtraUiCfg();
        }



                private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                if (child is T t) yield return t;
                foreach (var childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
            }
        }

        private void BtnThemeSelect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int idx)) ApplyThemeLive(idx, _panelOpacity);
        }

        private void SldPanelOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IsLoaded && !_isLoading) ApplyThemeLive(_currentThemeIdx, e.NewValue / 100.0);
        }

        private void ChkExtraSetting_Changed(object sender, RoutedEventArgs e) => SaveExtraUiCfg();

        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SZOptimizer");
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Экспорт настроек SZ Optimizer", Filter = "ZIP архив (*.zip)|*.zip", FileName = "SZOptimizer_Profile.zip" };
            if (dlg.ShowDialog() == true && Directory.Exists(dir))
            {
                if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                System.IO.Compression.ZipFile.CreateFromDirectory(dir, dlg.FileName);
                MessageBox.Show("✅ Все профили, прицелы и настройки успешно экспортированы!", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SZOptimizer");
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Импорт настроек SZ Optimizer", Filter = "ZIP архив (*.zip)|*.zip" };
            if (dlg.ShowDialog() == true)
            {
                Directory.CreateDirectory(dir);
                System.IO.Compression.ZipFile.ExtractToDirectory(dlg.FileName, dir, true);
                MessageBox.Show("✅ Настройки импортированы! Перезапустите SZ Optimizer для применения.", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnCheckGithubUpdates_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://github.com") { UseShellExecute = true });

        private void TabSzCfgBtn_Click(object sender, RoutedEventArgs e) => SelectTab(TabSzCfgBtn, PanelSzCfg);

        private List<string> FindAllGameOptionsJson()
        {
            var found = new List<string>();
            string[] roots = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EXBO"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EXBO"),
                @"C:\EXBO", @"D:\EXBO", @"B:\EXBO",
                @"C:\Program Files (x86)\Steam\steamapps\common\STALCRAFT",
                @"D:\SteamLibrary\steamapps\common\STALCRAFT",
                @"B:\SteamLibrary\steamapps\common\STALCRAFT"
            };
            foreach (var r in roots.Where(Directory.Exists))
                try { found.AddRange(Directory.GetFiles(r, "options.json", SearchOption.AllDirectories)); } catch { }
            return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private bool ShowTimedCloseGameDialog(string title, string bodyText, bool isExtreme = false)
        {
            bool confirmed = false;
            var dlg = new Window
            {
                Title = title, Width = 460, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1F22")),
                Foreground = Brushes.White
            };
            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = isExtreme ? "⚠️ ВНИМАНИЕ: ЭКСТРЕМАЛЬНЫЙ РЕЖИМ + ЗАКРОЙТЕ ИГРУ" : "⚠️ ПЕРЕД ПРИМЕНЕНИЕМ ЗАКРОЙТЕ ИГРУ",
                FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isExtreme ? "#FF4D4F" : "#FFD700")),
                Margin = new Thickness(0, 0, 0, 10)
            });
            root.Children.Add(new TextBlock
            {
                Text = bodyText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBDEE1")),
                Margin = new Thickness(0, 0, 0, 16)
            });
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnCancel = new Button
            {
                Content = "Отмена", Height = 34, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B5BAC1")),
                BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand
            };
            btnCancel.Click += (s, e) => dlg.Close();

            var btnOk = new Button
            {
                Content = "⏳ Подождите (3 сек)...", Height = 34, Padding = new Thickness(16, 0, 16, 0), IsEnabled = false,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80848E")),
                FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand
            };
            btnOk.Click += (s, e) => { confirmed = true; dlg.Close(); };

            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnOk);
            root.Children.Add(btnRow);
            dlg.Content = root;

            int secLeft = 3;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) =>
            {
                secLeft--;
                if (secLeft > 0) btnOk.Content = $"⏳ Подождите ({secLeft} сек)...";
                else
                {
                    timer.Stop();
                    btnOk.IsEnabled = true;
                    btnOk.Content = "✅ Игра закрыта — Применить";
                    btnOk.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isExtreme ? "#D9363E" : "#00D4AA"));
                    btnOk.Foreground = isExtreme ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111214"));
                }
            };
            dlg.Loaded += (s, e) => timer.Start();
            dlg.Closed += (s, e) => timer.Stop();
            dlg.ShowDialog();
            return confirmed;
        }

        private void ApplySzGraphicsPreset(string presetName, Dictionary<string, string> overrides)
        {
            if (Process.GetProcesses().Any(p => new[] { "stalzone", "stalzonew", "stalcraft", "stalcraftw" }.Contains(p.ProcessName.ToLowerInvariant())))
            {
                MessageBox.Show("⚠️ Закройте STALZONE перед применением настроек!\n\nПока игра запущена, изменение графики заблокировано, чтобы клиент не перезаписал options.json.", "SZ Optimizer — Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (presetName.Contains("МАКС"))
            {
                var warn = MessageBox.Show(
                    "⚠️ ВНИМАНИЕ! МАКС. ПРОИЗВОДИТЕЛЬНОСТЬ\n\nЭтот пресет полностью ВЫРЕЗАЕТ тени, траву, частицы, воду, блики, размытие и снижает текстуры до минимума ради предельного FPS.\n🛡️ Дальность прорисовки игроков НЕ урезается.\n\nПрименить экстремальные настройки?",
                    "⚠️ Подтверждение — МАКС. ПРОИЗВОДИТЕЛЬНОСТЬ",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (warn != MessageBoxResult.Yes) return;
            }            
            var files = FindAllGameOptionsJson();
            if (files.Count == 0)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Укажите options.json игры", Filter = "options.json|options.json|JSON (*.json)|*.json" };
                if (dlg.ShowDialog() == true) files.Add(dlg.FileName); else return;
            }
            int updated = 0;
            foreach (var file in files)
            {
                try
                {
                    if (!File.Exists(file + ".sz_backup")) File.Copy(file, file + ".sz_backup", false);
                    string json = File.ReadAllText(file, Encoding.UTF8);
                    foreach (var kv in overrides)
                    {
                        string pat = $"\"{System.Text.RegularExpressions.Regex.Escape(kv.Key)}\"\\s*:\\s*([^,\\r\\n\\}}]+)";
                        if (System.Text.RegularExpressions.Regex.IsMatch(json, pat))
                            json = System.Text.RegularExpressions.Regex.Replace(json, pat, $"\"{kv.Key}\": {kv.Value}");
                    }
                    File.WriteAllText(file, json, Encoding.UTF8);
                    updated++;
                }
                catch { }
            }
            if (SzCfgStatusText != null) SzCfgStatusText.Text = $"✅ Применён пресет «{presetName}» ({updated} конфиг.)";
            MessageBox.Show($"Пресет «{presetName}» применён ({updated} файл(ов) options.json).\nДальность прорисовки игроков не затронута!", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSzPresetUltra_Click(object sender, RoutedEventArgs e) => ApplySzGraphicsPreset("Ультра", new() {
            ["shadows_dynamic"]="true", ["shadows_pcf"]="true", ["shadows_map_size"]="3", ["soft_lighting_mode"]="2", ["lights_enabled"]="true", ["ssao"]="2", ["bloom"]="true", ["godrays_enabled"]="true", ["water"]="3", ["fxaa"]="true", ["antialiasing_mode"]="2", ["grass_flag"]="true", ["grass_exp"]="4", ["tree_models"]="2", ["particles_resolution"]="2", ["details_amount"]="3", ["texture_quality_blocks"]="3", ["texture_quality_objects"]="3", ["filtering_mode"]="4", ["vsyncEnabled"]="false", ["multithread_loading"]="true", ["memory_mapping"]="true"
        });

        private void BtnSzPresetHigh_Click(object sender, RoutedEventArgs e) => ApplySzGraphicsPreset("Высокие + FPS", new() {
            ["shadows_dynamic"]="true", ["shadows_pcf"]="false", ["shadows_map_size"]="2", ["soft_lighting_mode"]="2", ["lights_enabled"]="false", ["ssao"]="1", ["bloom"]="true", ["godrays_enabled"]="false", ["water"]="2", ["fxaa"]="true", ["antialiasing_mode"]="1", ["grass_flag"]="true", ["grass_exp"]="3", ["tree_models"]="1", ["particles_resolution"]="1", ["details_amount"]="2", ["texture_quality_blocks"]="3", ["texture_quality_objects"]="2", ["filtering_mode"]="4", ["blurAiming"]="false", ["blur_hud_elements"]="false", ["vsyncEnabled"]="false", ["multithread_loading"]="true", ["memory_mapping"]="true"
        });

        private void BtnSzPresetMed_Click(object sender, RoutedEventArgs e) => ApplySzGraphicsPreset("Средние + FPS", new() {
            ["shadows_dynamic"]="true", ["shadows_pcf"]="false", ["shadows_map_size"]="1", ["soft_lighting_mode"]="1", ["lights_enabled"]="false", ["ssao"]="0", ["bloom"]="false", ["godrays_enabled"]="false", ["water"]="1", ["fxaa"]="true", ["antialiasing_mode"]="0", ["grass_flag"]="true", ["grass_exp"]="2", ["tree_models"]="1", ["particles_resolution"]="1", ["details_amount"]="2", ["texture_quality_blocks"]="2", ["texture_quality_objects"]="2", ["filtering_mode"]="2", ["blurAiming"]="false", ["blur_hud_elements"]="false", ["vsyncEnabled"]="false", ["multithread_loading"]="true", ["memory_mapping"]="true"
        });

        private void BtnSzPresetPerf_Click(object sender, RoutedEventArgs e) => ApplySzGraphicsPreset("Производительность (PvP / FPS)", new() {
            ["shadows_dynamic"]="false", ["shadows_pcf"]="false", ["shadows_map_size"]="0", ["soft_lighting_mode"]="0", ["lights_enabled"]="false", ["ssao"]="0", ["bloom"]="false", ["godrays_enabled"]="false", ["water"]="0", ["fxaa"]="false", ["antialiasing_mode"]="0", ["grass_flag"]="true", ["grass_exp"]="1", ["tree_models"]="0", ["particles_resolution"]="0", ["details_amount"]="1", ["texture_quality_blocks"]="2", ["texture_quality_objects"]="1", ["filtering_mode"]="1", ["bullet_holes"]="false", ["blurAiming"]="false", ["blur_hud_elements"]="false", ["vsyncEnabled"]="false", ["precompile_shaders"]="true", ["multithread_loading"]="true", ["memory_mapping"]="true"
        });

        private void BtnSzPresetMaxPerf_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("⚠️ ВНИМАНИЕ! МАКС. ПРОИЗВОДИТЕЛЬНОСТЬ\n\nЭтот пресет полностью ВЫРЕЗАЕТ тени, траву, частицы, воду, блики, размытие и снижает текстуры до минимума ради предельного FPS.\n🛡️ Дальность прорисовки игроков НЕ урезается.\n\nПродолжить?", "⚠️ Экстремальные настройки графики", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            ApplySzGraphicsPreset("МАКС. ПРОИЗВОДИТЕЛЬНОСТЬ", new() {
                ["shadows_dynamic"]="false", ["shadows_pcf"]="false", ["shadows_map_size"]="0", ["soft_lighting_mode"]="0", ["lights_enabled"]="false", ["ssao"]="0", ["bloom"]="false", ["godrays_enabled"]="false", ["water"]="0", ["fxaa"]="false", ["antialiasing_mode"]="0", ["grass_flag"]="false", ["grass_exp"]="0", ["tree_models"]="0", ["particles_resolution"]="0", ["details_amount"]="0", ["texture_quality_blocks"]="1", ["texture_quality_objects"]="1", ["filtering_mode"]="0", ["bullet_holes"]="false", ["blurAiming"]="false", ["blur_hud_elements"]="false", ["vsyncEnabled"]="false", ["precompile_shaders"]="true", ["multithread_loading"]="true", ["memory_mapping"]="true"
            });
        }

        private void BtnSzPresetRestore_Click(object sender, RoutedEventArgs e)
        {
            
            if (Process.GetProcesses().Any(p => new[] { "stalzone", "stalzonew", "stalcraft", "stalcraftw" }.Contains(p.ProcessName.ToLowerInvariant())))
            {
                MessageBox.Show("⚠️ Закройте STALZONE перед восстановлением настроек!", "SZ Optimizer — Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            int n = 0;
            foreach (var f in FindAllGameOptionsJson()) { if (File.Exists(f + ".sz_backup")) { File.Copy(f + ".sz_backup", f, true); n++; } }
            if (n > 0)
            {
                if (SzCfgStatusText != null) SzCfgStatusText.Text = $"↩️ Восстановлен исходный options.json ({n} конфиг.) — пресет сброшен";
                MessageBox.Show($"Исходный options.json восстановлен ({n} файл(ов))!", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Резервная копия ещё не создана.", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void TabJvmBtn_Click(object sender, RoutedEventArgs e) => SelectTab(TabJvmBtn, PanelJvm);
                private bool ValidateResInput(out int w, out int h, out int hz)
        {
            w = 0; h = 0; hz = _nativeHz;
            if (!int.TryParse(TxtCustomW.Text, out w) || !int.TryParse(TxtCustomH.Text, out h) || !int.TryParse(TxtCustomHz.Text, out hz))
            { MessageBox.Show("Введите только целые числа для ширины, высоты и герцовки!", "Защита от ошибок — SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
            if (w < 640 || h < 480 || hz < 30)
            { MessageBox.Show("Слишком маленькое значение! Минимум: 640 × 480 @ 30 Гц.", "Защита от ошибок — SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
            if (w > _maxMonW || h > _maxMonH || hz > _maxMonHz)
            {
                MessageBox.Show($"⚠️ Защита от некорректных значений!\n\nВы ввели: {w} × {h} @ {hz} Гц.\nМаксимальный предел вашего монитора: {_maxMonW} × {_maxMonH} @ {_maxMonHz} Гц.\n\nЗначения автоматически исправлены на безопасный максимум вашего экрана.", "Защита монитора — SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                if (w > _maxMonW) { w = _maxMonW; TxtCustomW.Text = w.ToString(); }
                if (h > _maxMonH) { h = _maxMonH; TxtCustomH.Text = h.ToString(); }
                if (hz > _maxMonHz) { hz = _maxMonHz; TxtCustomHz.Text = hz.ToString(); }
                return false;
            }
            return true;
        }
        private void BtnApplyCustomRes_Click(object sender, RoutedEventArgs e) { if (ValidateResInput(out int w, out int h, out int hz)) SetDisplayResolution(w, h, hz); }
        private void BtnSaveCustomRes_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateResInput(out int w, out int h, out int hz))
            {
                _targetResW = w;
                _targetResH = h;
                _targetResHz = hz;
                var plist = LoadCustomPresets();
                string pkey = $"{w}x{h}";
                if (!plist.Contains(pkey)) { plist.Add(pkey); SaveCustomPresets(plist); }
                if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets();
                UpdateResStatusUi(); if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); if (SavedCustomResText != null) SavedCustomResText.Text = $"Выбранный профиль для монитора {(_monitors.FindIndex(x => x.MaxW == _maxMonW && x.MaxH == _maxMonH) + 1)}: {_targetResW} × {_targetResH} @ {_targetResHz} Гц"; if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets();
                SaveUserSettings();
                MessageBox.Show($"Разрешение {w}×{h} @ {hz} Гц добавлено в быстрые пресеты и сохранено в профиль!", "SZ Optimizer v1.5", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnOpenIntelGraphics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\AppUp.IntelGraphicsExperience_8j3eq9eme6ctt!App") { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show("Intel Graphics Command Center не найден в системе.\nЕсли у вас встроенная графика Intel, установите приложение из Microsoft Store и выберите Scale → Stretched.", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRestoreNativeRes_Click(object sender, RoutedEventArgs e) { SetDisplayResolution(_nativeW, _nativeH, _nativeHz, true); _resAppliedByGame = false; }
        private void BtnWriteGameRes_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtCustomW.Text, out int w) || !int.TryParse(TxtCustomH.Text, out int h)) return;
            string? opt = FindGameOptionsFile();
            if (opt == null) { MessageBox.Show("Файл options.json не найден.", "SZ Optimizer v1.5"); return; }
            string json = File.ReadAllText(opt);
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"window_width\"\\s*:\\s*\\d+", $"\"window_width\": {w}");
            int gameH = (w == 2304 && h == 1440) ? 1442 : h; json = System.Text.RegularExpressions.Regex.Replace(json, "\"window_height\"\\s*:\\s*\\d+", $"\"window_height\": {gameH}");
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"window_x\"\\s*:\\s*-?\\d+", "\"window_x\": 0");
        json = System.Text.RegularExpressions.Regex.Replace(json, "\"window_y\"\\s*:\\s*-?\\d+", "\"window_y\": 0");
        json = System.Text.RegularExpressions.Regex.Replace(json, "\"window_maximized\"\\s*:\\s*(true|false)", "\"window_maximized\": true");
        json = System.Text.RegularExpressions.Regex.Replace(json, "\"borderless\"\\s*:\\s*(true|false)", "\"borderless\": true");
        File.WriteAllText(opt, json);
            MessageBox.Show($"Разрешение {w}×{h} записано в options.json игры!", "SZ Optimizer v1.5");
        }
                                private void InitResolutionTab()
        {
            _monitors.Clear();
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint idx = 0; EnumDisplayDevices(null, idx, ref dd, 0) && _monitors.Count < 4; idx++)
            {
                if ((dd.StateFlags & 0x1) == 0) continue;
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(dd.DeviceName, -1, ref dm)) continue;
                var mi = new MonInfo { DevName = dd.DeviceName, Primary = (dd.StateFlags & 0x4) != 0, NatW = dm.dmPelsWidth, NatH = dm.dmPelsHeight, NatHz = dm.dmDisplayFrequency, MaxW = dm.dmPelsWidth, MaxH = dm.dmPelsHeight, MaxHz = dm.dmDisplayFrequency };
                var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                for (int m = 0; EnumDisplaySettings(dd.DeviceName, m, ref mode); m++)
                {
                    if (mode.dmPelsWidth > mi.MaxW || (mode.dmPelsWidth == mi.MaxW && mode.dmPelsHeight > mi.MaxH)) { mi.MaxW = mode.dmPelsWidth; mi.MaxH = mode.dmPelsHeight; }
                    if (mode.dmDisplayFrequency > mi.MaxHz) mi.MaxHz = mode.dmDisplayFrequency;
                }
                _monitors.Add(mi);
                dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            }
            if (_monitors.Count == 0) _monitors.Add(new MonInfo { DevName = "", Primary = true, NatW = 1920, NatH = 1080, NatHz = 60, MaxW = 1920, MaxH = 1080, MaxHz = 60 });
            _selMon = 0; SyncActiveMonitorFields(true); RebuildMonitorButtons(); if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets();
        }
        private void SyncActiveMonitorFields(bool fillBoxes)
        {
            var m = _monitors.ElementAt(_selMon);
            _nativeW = m.NatW; _nativeH = m.NatH; _nativeHz = m.NatHz; _maxMonW = m.MaxW; _maxMonH = m.MaxH; _maxMonHz = m.MaxHz; if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets(); _targetResW = m.SavedW; _targetResH = m.SavedH; _targetResHz = m.SavedHz;
            if (fillBoxes && TxtCustomW != null) { TxtCustomW.Text = (m.SavedW > 0 ? m.SavedW : m.MaxW).ToString(); TxtCustomH.Text = (m.SavedH > 0 ? m.SavedH : m.MaxH).ToString(); TxtCustomHz.Text = (m.SavedHz > 0 ? m.SavedHz : m.MaxHz).ToString(); }
            UpdateResStatusUi(); if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); if (SavedCustomResText != null) SavedCustomResText.Text = $"Выбранный профиль для монитора {(_monitors.FindIndex(x => x.MaxW == _maxMonW && x.MaxH == _maxMonH) + 1)}: {_targetResW} × {_targetResH} @ {_targetResHz} Гц"; if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets();
        }
        private void RebuildMonitorButtons()
        {
            if (MonitorsGrid == null) return;
            MonitorsGrid.Children.Clear(); MonitorsGrid.Columns = Math.Max(1, _monitors.Count);
            for (int i = 0; i < _monitors.Count; i++)
            {
                int idx = i; var m = _monitors.ElementAt(i); bool active = idx == _selMon;
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = $"🖥️ Монитор {idx + 1}" + (m.Primary ? " (Осн.)" : ""), FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = active ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111214")) : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = $"Макс: {m.MaxW}×{m.MaxH} @ {m.MaxHz} Гц", FontSize = 11, Foreground = active ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111214")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA")), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
                var btn = new Button { Height = 50, Margin = new Thickness(0, 0, idx < _monitors.Count - 1 ? 6 : 0, 0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#00D4AA" : "#2B2D31")), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#00D4AA" : "#383A40")), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Content = sp };
                btn.Click += (_, _) => { _selMon = idx; SyncActiveMonitorFields(true); RebuildMonitorButtons(); };
                MonitorsGrid.Children.Add(btn);
            }
        }
        private void BtnSetMaxMonitorRes_Click(object sender, RoutedEventArgs e) { var m = _monitors.ElementAt(_selMon); TxtCustomW.Text = m.MaxW.ToString(); TxtCustomH.Text = m.MaxH.ToString(); TxtCustomHz.Text = m.MaxHz.ToString(); SetDisplayResolution(m.MaxW, m.MaxH, m.MaxHz); }
        private void UpdateResStatusUi()
        {
            if (CurrentResText == null) return;
            var m = _monitors.ElementAt(_selMon); var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() }; string dev = string.IsNullOrEmpty(m.DevName) ? null! : m.DevName;
            if (EnumDisplaySettings(dev, -1, ref dm)) CurrentResText.Text = $"🖥️ Монитор {_selMon + 1} — текущее: {dm.dmPelsWidth} × {dm.dmPelsHeight} @ {dm.dmDisplayFrequency} Гц";
            NativeResText.Text = $"Родное разрешение монитора {_selMon + 1}: {m.NatW} × {m.NatH} @ {m.NatHz} Гц";
            if (MaxMonitorResText != null) MaxMonitorResText.Text = $"Максимальный предел монитора {_selMon + 1}: {m.MaxW} × {m.MaxH} @ {m.MaxHz} Гц";
            SavedCustomResText.Text = m.SavedW > 0 ? $"Сохранённый профиль для монитора {_selMon + 1}: {m.SavedW} × {m.SavedH} @ {m.SavedHz} Гц" : $"Профиль для монитора {_selMon + 1}: по умолчанию ({m.MaxW}×{m.MaxH} @ {m.MaxHz} Гц)";
        }
        private bool SetDisplayResolution(int w, int h, int hz, bool silent = false)
        {
            var m = _monitors.ElementAt(_selMon); string? dev = string.IsNullOrEmpty(m.DevName) ? null : m.DevName;
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() }; EnumDisplaySettings(dev, -1, ref dm);
            dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmDisplayFrequency = hz > 0 ? hz : m.NatHz; dm.dmFields = 0x00080000 | 0x00100000 | 0x00400000 | 0x20000000; dm.dmDisplayFixedOutput = 1;
            int res = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, 0x00000001, IntPtr.Zero);
            if (res != 0) { dm.dmFields = 0x00080000 | 0x00100000; res = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, 0x00000001, IntPtr.Zero); }
            if (res == 0) { m.SavedW = w; m.SavedH = h; m.SavedHz = hz > 0 ? hz : m.NatHz; _targetResW = m.SavedW; _targetResH = m.SavedH; _targetResHz = m.SavedHz; UpdateResStatusUi(); if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); if (SavedCustomResText != null) SavedCustomResText.Text = $"Выбранный профиль для монитора {(_monitors.FindIndex(x => x.MaxW == _maxMonW && x.MaxH == _maxMonH) + 1)}: {_targetResW} × {_targetResH} @ {_targetResHz} Гц"; if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets(); SaveUserSettings(); return true; }
            UpdateResStatusUi(); if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); if (SavedCustomResText != null) SavedCustomResText.Text = $"Выбранный профиль для монитора {(_monitors.FindIndex(x => x.MaxW == _maxMonW && x.MaxH == _maxMonH) + 1)}: {_targetResW} × {_targetResH} @ {_targetResHz} Гц"; if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets();
            if (!silent) MessageBox.Show($"Драйвер видеокарты отклонил режим {w}×{h} для Монитора {_selMon + 1}.", "SZ Optimizer v1.5", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
                private void TxtRes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtCustomW.Text, out int w) && w > _maxMonW) TxtCustomW.Text = _maxMonW.ToString();
            if (int.TryParse(TxtCustomH.Text, out int h) && h > _maxMonH) TxtCustomH.Text = _maxMonH.ToString();
            if (int.TryParse(TxtCustomHz.Text, out int hz) && hz > _maxMonHz) TxtCustomHz.Text = _maxMonHz.ToString();
        }
        private static string GetCustomPresetsFile() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SZOptimizer", "custom_presets.txt");

        private List<string> LoadCustomPresets()
        {
            try
            {
                string f = GetCustomPresetsFile();
                if (File.Exists(f)) return File.ReadAllLines(f).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            }
            catch { }
            return new List<string>();
        }

        private void SaveCustomPresets(List<string> list)
        {
            try
            {
                string f = GetCustomPresetsFile();
                Directory.CreateDirectory(Path.GetDirectoryName(f)!);
                File.WriteAllLines(f, list.Distinct());
            }
            catch { }
        }

        private void RebuildResolutionPresets()
        {
            if (PresetsUniformGrid == null) return;
            PresetsUniformGrid.Children.Clear();

            var items = new List<(string Label, string Tag, bool IsCustom, bool Highlight)>();
            int mw = _maxMonW > 0 ? _maxMonW : 1920;
            int mh = _maxMonH > 0 ? _maxMonH : 1080;

            var supported = new HashSet<(int W, int H)>();
            var curMon = _monitors.FirstOrDefault(x => x.MaxW == mw && x.MaxH == mh) ?? _monitors.FirstOrDefault();
            if (curMon != null)
            {
                string dev = null!;
            for (int di = 1; di <= 8; di++)
            {
                string cand = $"\\\\.\\DISPLAY{di}";
                DEVMODE probe = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                int maxW = 0, maxH = 0;
                for (int m = 0; EnumDisplaySettings(cand, m, ref probe); m++)
                {
                    if (probe.dmPelsWidth >= maxW && probe.dmPelsHeight >= maxH) { maxW = probe.dmPelsWidth; maxH = probe.dmPelsHeight; }
                }
                if (maxW == curMon.MaxW && maxH == curMon.MaxH) { dev = cand; break; }
            }
            dev ??= "\\\\.\\DISPLAY1";
                DEVMODE dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                for (int mode = 0; EnumDisplaySettings(dev, mode, ref dm); mode++)
                {
                    if (dm.dmPelsWidth >= 1024 && dm.dmPelsHeight >= 768)
                        supported.Add((dm.dmPelsWidth, dm.dmPelsHeight));
                }
            }

            var orderedModes = supported
                .Where(m => mh >= 2160 ? m.H >= 1800 : (mh >= 1440 ? (m.H >= 1280 && m.H <= 1440 && m.W != 2048 || (m.W == 2048 && m.H == 1280)) : m.H <= 1080))
                .OrderByDescending(m => m.W * m.H)
                .ThenByDescending(m => m.W)
                .ToList();

            

            foreach (var m in orderedModes)
            {
                double ratio = (double)m.W / m.H;
                string tagRatio = Math.Abs(ratio - 16.0 / 9.0) < 0.03 ? "16:9" :
                                  Math.Abs(ratio - 16.0 / 10.0) < 0.03 ? "16:10" :
                                  Math.Abs(ratio - 4.0 / 3.0) < 0.03 ? "4:3" :
                                  Math.Abs(ratio - 5.0 / 4.0) < 0.03 ? "5:4" : "";
                bool hl = tagRatio == "16:10" || tagRatio == "4:3";
                string lbl = (m.W == 2048 && m.H == 1280) ? "2048 × 1280 (16:10 Точный)" :
                             (m.W == 1920 && m.H == 1200) ? "1920 × 1200 (16:10 VESA)" :
                             (string.IsNullOrEmpty(tagRatio) ? $"{m.W} × {m.H}" : $"{m.W} × {m.H} ({tagRatio})");
                items.Add((lbl, $"{m.W}x{m.H}", false, hl));
            }

            foreach (var cp in LoadCustomPresets())
            {
                var parts = cp.Split('x');
                if (parts.Length == 2 && int.TryParse(parts.FirstOrDefault(), out int cw) && int.TryParse(parts.LastOrDefault(), out int ch))
                {
                    if (cw <= mw && ch <= mh)
                        items.Insert(0, ($"⭐ {cw} × {ch} (Свой)", cp, true, true));
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                bool isActive = (it.Tag == $"{_targetResW}x{_targetResH}");
                var btn = new Button
                {
                    Content = it.Label,
                    Tag = it.Tag,
                    Height = 34,
                    Margin = new Thickness(0, 0, (i % 3 == 2) ? 0 : 6, 6),
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isActive ? "#1B2E2B" : "#2B2D31")),
                    Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(it.IsCustom ? "#FFD700" : (isActive || it.Highlight ? "#00D4AA" : "White"))),
                    BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isActive ? "#00D4AA" : "#383A40")),
                    BorderThickness = new Thickness(isActive ? 1.5 : 1),
                    FontWeight = (isActive || it.Highlight) ? FontWeights.SemiBold : FontWeights.Normal,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = it.IsCustom ? "ЛКМ — применить | ПКМ — удалить свой пресет" : "Нажмите, чтобы применить разрешение"
                };
                btn.Click += BtnResPreset_Click;
                if (it.IsCustom)
                {
                    string tagCopy = it.Tag;
                    btn.MouseRightButtonUp += (s2, ev) =>
                    {
                        var list = LoadCustomPresets();
                        list.Remove(tagCopy);
                        SaveCustomPresets(list);
                        if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets();
                    };
                }
                PresetsUniformGrid.Children.Add(btn);
            }
        }

        private async void BtnRefreshResList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SZOptimizer", "tools");
                string r64 = Path.Combine(dir, "restart64.exe");
                if (File.Exists(r64))
                {
                    var p = Process.Start(new ProcessStartInfo(r64) { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true });
                    if (p != null) await p.WaitForExitAsync();
                    await Task.Delay(1200);
                }
                InitResolutionTab();
                if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets();
            }
            catch { InitResolutionTab(); if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets(); }
        }

        private void BtnOpenCru_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SZOptimizer", "tools");
                Directory.CreateDirectory(dir);
                string cruExe = Path.Combine(dir, "CRU.exe");

                if (!File.Exists(cruExe))
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    foreach (var resName in asm.GetManifestResourceNames().Where(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        string fileName = resName.Split('.').Skip(Math.Max(0, resName.Split('.').Length - 2)).Aggregate((a, b) => a + "." + b);
                        using var s = asm.GetManifestResourceStream(resName);
                        using var fs = File.Create(Path.Combine(dir, fileName));
                        s?.CopyTo(fs);
                    }
                }

                if (File.Exists(cruExe))
                {
                    var pCru = Process.Start(new ProcessStartInfo(cruExe) { WorkingDirectory = dir, UseShellExecute = false });
                    if (pCru != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            await pCru.WaitForExitAsync();
                            await Dispatcher.InvokeAsync(() => { InitResolutionTab(); if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets(); });
                        });
                    }
                    return;
                }

                var res = MessageBox.Show(
                    "Утилита CRU (Custom Resolution Utility) позволяет добавить любое кастомное EDID-разрешение (например, 2048×1280 @ 100 Гц для чистого 16:10 без полосок).\n\n" +
                    "• Нажмите «Да», чтобы указать скачанный файл CRU.exe на вашем ПК (он сохранится внутри SZ Optimizer).\n" +
                    "• Нажмите «Нет», чтобы открыть официальную страницу загрузки CRU.",
                    "SZ Optimizer — CRU (EDID)", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Выберите CRU.exe", Filter = "CRU.exe|CRU.exe|Все программы (*.exe)|*.exe" };
                    if (dlg.ShowDialog() == true)
                    {
                        File.Copy(dlg.FileName, cruExe, true);
                        var pCru = Process.Start(new ProcessStartInfo(cruExe) { WorkingDirectory = dir, UseShellExecute = false });
                    if (pCru != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            await pCru.WaitForExitAsync();
                            await Dispatcher.InvokeAsync(() => { InitResolutionTab(); if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets(); });
                        });
                    }
                    }
                }
                else if (res == MessageBoxResult.No)
                {
                    Process.Start(new ProcessStartInfo("https://monitortests.com") { UseShellExecute = true });
                }
            }
            catch (Exception ex) { MessageBox.Show("Ошибка запуска CRU: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnScalingHelp_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MessageBox.Show(
                "📘 КАК УБРАТЬ ПОЛОСКУ СНИЗУ И ЧЁРНЫЕ ПОЛОСЫ ПРИ 16:10 / 4:3:\n\n" +
                "🔵 INTEL (встроенная графика UHD / Iris):\n" +
                "1. Откройте «Центр управления графикой Intel» (Intel Graphics Command Center).\n" +
                "2. Перейдите в «Дисплей» (Display) → «Общие» (General).\n" +
                "3. Выберите нужное разрешение (например, 2304×1440) и в пункте «Масштаб» (Scale) выберите «Растянутый» (Stretched).\n\n" +
                "🟢 NVIDIA (GeForce):\n" +
                "1. Откройте «Панель управления NVIDIA» → «Регулировка размера и положения рабочего стола».\n" +
                "2. Выберите режим масштабирования «Во весь экран» (Full-screen).\n" +
                "3. В пункте «Выполнять масштабирование на:» выберите «ГП» (GPU) и поставьте галочку «Замещение режима масштабирования».\n\n" +
                "🔴 AMD (Radeon):\n" +
                "1. Откройте «AMD Software: Adrenalin Edition» → «Настройки» (шестерёнка) → «Дисплей».\n" +
                "2. Включите «Масштабирование ГП» (GPU Scaling) → Вкл.\n" +
                "3. Выберите «Режим масштабирования: Полная панель» (Full Panel).",
                "SZ Optimizer — Инструкция по масштабированию (Intel / NVIDIA / AMD)",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnResPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag)
            {
                var p = tag.Split('x');
                if (p.Length == 2 && int.TryParse(p.ElementAt(0), out int w) && int.TryParse(p.ElementAt(1), out int h))
                {
                    int hz = int.TryParse(TxtCustomHz.Text, out int parsedHz) ? parsedHz : _nativeHz;
                    if (hz > _maxMonHz) { hz = _maxMonHz; TxtCustomHz.Text = hz.ToString(); }
                    if (w > _maxMonW || h > _maxMonH)
                    {
                        MessageBox.Show($"⚠️ Ваш монитор не поддерживает разрешение {w} × {h}!\n\nМаксимальный предел вашего экрана: {_maxMonW} × {_maxMonH} @ {_maxMonHz} Гц.", "Защита монитора — SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    TxtCustomW.Text = w.ToString(); TxtCustomH.Text = h.ToString();
                    SetDisplayResolution(w, h, hz);
                }
            }
        }
                private bool RunSafePowerCmd(string args, CheckBox? chk, string featureName)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("powercfg", args) { CreateNoWindow = true, UseShellExecute = false });
                p?.WaitForExit(3000);
                if (p == null || p.ExitCode != 0) throw new Exception("Код возврата драйвера питания: " + (p?.ExitCode ?? -1));
                return true;
            }
            catch (Exception ex)
            {
                if (chk != null) { chk.IsChecked = false; SaveUserSettings(); }
                MessageBox.Show($"🛑 КРИТИЧЕСКАЯ ЗАЩИТА ОТ СИНЕГО ЭКРАНА (BSOD)!\n\nФункция «{featureName}» вызвала конфликт с драйвером питания/видеокарты вашего ПК ({ex.Message}).\n\nПринудительное включение может привести к синему экрану (BSOD). Мы автоматически отключили эту функцию и сняли галочку — лучше не пользуйтесь ей на данном железе.", "Защита от BSOD — SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
                        private void ApplyOsCompatibilityUi()
        {
            var v = Environment.OSVersion.Version;
            bool isWin10Plus = v.Major >= 10;
            bool isWin7Plus = (v.Major > 6) || (v.Major == 6 && v.Minor >= 1);
            if (PanelHardware == null) return;
            foreach (var cb in FindVisualChildren<CheckBox>(PanelHardware))
            {
                string txt = cb.Content?.ToString() ?? "";
                if (!isWin10Plus && (txt.Contains("HAGS") || txt.Contains("Игровой режим") || txt.Contains("дискретной видеокарте")))
                {
                    if (cb.Parent is FrameworkElement row) row.Visibility = Visibility.Collapsed;
                    else cb.Visibility = Visibility.Collapsed;
                }
                if (!isWin7Plus && txt.Contains("парковку ядер"))
                {
                    if (cb.Parent is FrameworkElement row) row.Visibility = Visibility.Collapsed;
                    else cb.Visibility = Visibility.Collapsed;
                }
            }
        }

                [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
        private int _totalPcRamGb = 16, _jvmRamGb = 0;
        private string _jvmProfile = "Balanced";
        private static readonly string[] JvmTargets = { "stalzone.exe", "stalzonew.exe", "stalcraft.exe", "stalcraftw.exe" };

        private int GetEffectiveJvmRamGb()
        {
            if (_jvmRamGb > 0) return _jvmRamGb;
            if (_totalPcRamGb >= 24) return 12;
            if (_totalPcRamGb >= 16) return 8;
            return 6;
        }
        private void InitJvmTab()
        {
            var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref ms)) _totalPcRamGb = Math.Max(4, (int)Math.Round(ms.ullTotalPhys / 1073741824.0));
            UpdateJvmUi();
        }
        private void UpdateJvmUi()
        {
            if (SystemRamInfoText == null) return;
            int maxSafe = Math.Max(6, (int)(_totalPcRamGb * 0.65));
            int eff = GetEffectiveJvmRamGb();
            SystemRamInfoText.Text = $"Установлено ОЗУ в ПК: {_totalPcRamGb} ГБ | Рекомендуется: {( _totalPcRamGb >= 16 ? 8 : 6 )}–{maxSafe} ГБ (Выбрано: {eff} ГБ)";
            var ramBtns = new[] { (BtnJvmRamAuto, 0), (BtnJvmRam6, 6), (BtnJvmRam8, 8), (BtnJvmRam10, 10), (BtnJvmRam12, 12), (BtnJvmRam16, 16) };
            foreach (var (b, val) in ramBtns)
            {
                if (b == null) continue;
                bool sel = _jvmRamGb == val;
                b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sel ? "#00D4AA" : "#2B2D31"));
                b.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sel ? "#111214" : "White"));
            }
            var profBtns = new[] { (BtnJvmProfBalanced, "Balanced"), (BtnJvmProfEsports, "Esports"), (BtnJvmProfSafe, "Safe") };
            foreach (var (b, tag) in profBtns)
            {
                if (b == null) continue;
                bool sel = _jvmProfile == tag;
                b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sel ? "#00D4AA" : "#2B2D31"));
                b.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sel ? "#111214" : "White"));
            }
            bool active = false;
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\stalzonew.exe");
                string? dbg = k?.GetValue("Debugger") as string;
                if (!string.IsNullOrEmpty(dbg) && dbg.Contains("service.exe", StringComparison.OrdinalIgnoreCase)) active = true;
            }
            catch { }
            JvmStatusText.Text = active ? $"🟢 Статус враппера: АКТИВЕН (Выделено {eff} ГБ ОЗУ, профиль: {_jvmProfile})" : "⚪ Статус враппера: Выключен (игра использует стандартные ~6 ГБ ОЗУ)";
            JvmStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#51CF66" : "White"));
        }
        private void BtnJvmRam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int val))
            {
                int maxSafe = Math.Max(6, (int)(_totalPcRamGb * 0.65));
                if (val > maxSafe)
                {
                    MessageBox.Show($"⚠️ Защита от нехватки памяти!\n\nНа вашем ПК установлено {_totalPcRamGb} ГБ ОЗУ. Выделение {val} ГБ под Java оставит системе слишком мало памяти и вызовет жёсткие фризы из-за файла подкачки.\n\nМаксимальный безопасный лимит для вашего ПК: {maxSafe} ГБ.", "Защита ОЗУ — SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _jvmRamGb = val; UpdateJvmUi(); SaveUserSettings();
            }
        }
        private void BtnJvmProf_Click(object sender, RoutedEventArgs e) { if (sender is Button b && b.Tag is string p) { _jvmProfile = p; UpdateJvmUi(); SaveUserSettings(); } }
        private void ChkJvmOpt_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkJvmPreTouch?.IsChecked == true && _totalPcRamGb < 12)
            {
                ChkJvmPreTouch.IsChecked = false;
                MessageBox.Show("⚠️ Функция AlwaysPreTouch требует минимум 12 ГБ оперативной памяти в ПК (у вас обнаружено " + _totalPcRamGb + " ГБ).\n\nНа 8 ГБ эта опция автоматически отключена во избежание вылетов.", "Защита ОЗУ — SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (!_isLoading) SaveUserSettings();
        }
                private static string GetOfficialWrapperDir() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SZOptimizer", "jvm_wrapper");

        private void BtnOpenExboGithub_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(new UriBuilder("https", "github.com", -1, "EXBO-Community/stalcraft-jvm-optimization/releases").Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }

        private async Task ApplyAndInstallExboWrapperAsync(string sourceDir)
        {
            string dir = GetOfficialWrapperDir();
            Directory.CreateDirectory(dir);
            if (!string.Equals(Path.GetFullPath(sourceDir).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var f in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(sourceDir, f);
                    string dst = Path.Combine(dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(f, dst, true);
                }
            }
            foreach (var f in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories))
            {
                string dst = Path.Combine(dir, Path.GetFileName(f));
                if (!string.Equals(f, dst, StringComparison.OrdinalIgnoreCase)) File.Copy(f, dst, true);
            }

            string cliExe = Path.Combine(dir, "cli.exe");
            string svcExe = Path.Combine(dir, "service.exe");
            if (!File.Exists(cliExe) || !File.Exists(svcExe))
                throw new Exception("В выбранном архиве/папке не найдены файлы cli.exe и service.exe.\nУбедитесь, что вы скачали именно релизный архив из раздела Releases (Assets), а не Source code.");

            var p = Process.Start(new ProcessStartInfo(cliExe, "install") { WorkingDirectory = dir, CreateNoWindow = true, UseShellExecute = false });
            if (p != null) await p.WaitForExitAsync();

            int ram = GetEffectiveJvmRamGb();
            bool preTouch = ChkJvmPreTouch?.IsChecked == true;
            bool strDedup = ChkJvmStrDedup?.IsChecked == true;
            int maxPause = _jvmProfile == "Esports" ? 50 : (_jvmProfile == "Safe" ? 150 : 100);

            foreach (var cfgFile in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    string json = File.ReadAllText(cfgFile);
                    if (json.Contains("heap_size_gb") || json.Contains("max_gc_pause_millis"))
                    {
                        json = System.Text.RegularExpressions.Regex.Replace(json, "\"heap_size_gb\"\\s*:\\s*\\d+", $"\"heap_size_gb\": {ram}");
                        json = System.Text.RegularExpressions.Regex.Replace(json, "\"pre_touch\"\\s*:\\s*(true|false)", $"\"pre_touch\": {(preTouch ? "true" : "false")}");
                        json = System.Text.RegularExpressions.Regex.Replace(json, "\"use_string_deduplication\"\\s*:\\s*(true|false)", $"\"use_string_deduplication\": {(strDedup ? "true" : "false")}");
                        json = System.Text.RegularExpressions.Regex.Replace(json, "\"max_gc_pause_millis\"\\s*:\\s*\\d+", $"\"max_gc_pause_millis\": {maxPause}");
                    }
                    json = System.Text.RegularExpressions.Regex.Replace(json, "-Xmx\\d+[GgMm]", $"-Xmx{ram}G");
                    json = System.Text.RegularExpressions.Regex.Replace(json, "-Xms\\d+[GgMm]", $"-Xms{ram}G");
                    File.WriteAllText(cfgFile, json);
                }
                catch { }
            }

            SaveUserSettings();
            UpdateJvmUi();
            MessageBox.Show($"✅ EXBO JVM Wrapper успешно установлен и настроен!\n\n• Выделено памяти для игры: {ram} ГБ\n• Профиль GC: {_jvmProfile} (макс. пауза {maxPause} мс)\n• AlwaysPreTouch: {(preTouch ? "Вкл" : "Выкл")} | StringDeduplication: {(strDedup ? "Вкл" : "Выкл")}\n\nТеперь можно запускать STALZONE / STALCRAFT!", "SZ Optimizer — EXBO JVM Wrapper", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnImportJvmZip_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Выберите скачанный архив EXBO JVM Wrapper (.zip)", Filter = "ZIP архив (*.zip)|*.zip" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                string tmpDir = Path.Combine(Path.GetTempPath(), "SZ_JvmExtract_" + Guid.NewGuid().ToString("N")[..6]);
                Directory.CreateDirectory(tmpDir);
                System.IO.Compression.ZipFile.ExtractToDirectory(dlg.FileName, tmpDir, true);
                await ApplyAndInstallExboWrapperAsync(tmpDir);
                try { Directory.Delete(tmpDir, true); } catch { }
            }
            catch (Exception ex) { MessageBox.Show("Ошибка при установке из архива:\n" + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void BtnImportJvmFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Выберите папку с распакованным EXBO JVM Wrapper (где лежат cli.exe и service.exe)" };
            if (dlg.ShowDialog() != true) return;
            try { await ApplyAndInstallExboWrapperAsync(dlg.FolderName); }
            catch (Exception ex) { MessageBox.Show("Ошибка при установке из папки:\n" + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnDisableJvmWrapper_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = GetOfficialWrapperDir();
                string cliExe = Path.Combine(dir, "cli.exe");
                if (File.Exists(cliExe)) Process.Start(new ProcessStartInfo(cliExe, "uninstall") { WorkingDirectory = dir, CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000);
                foreach (var t in JvmTargets)
                {
                    Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{t}", false);
                    Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{t}", false);
                }
                UpdateJvmUi();
                MessageBox.Show("✅ EXBO JVM Wrapper полностью отключён!", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void InitHardwareTab() { try { string cpu = "Не определён"; using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor")) foreach (ManagementObject o in s.Get()) { cpu = o["Name"]?.ToString()?.Trim() ?? cpu; break; } HwCpuText.Text = $"CPU: {cpu} ({Environment.ProcessorCount} потоков)"; long totalMb = 0; using (var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")) foreach (ManagementObject o in s.Get()) totalMb = Convert.ToInt64(o["TotalPhysicalMemory"]) / (1024 * 1024); int totalGb = totalMb > 24000 ? 32 : (totalMb > 12000 ? 16 : (totalMb > 6000 ? 8 : 4)); HwRamText.Text = $"ОЗУ: {totalGb} ГБ ({totalMb} МБ)"; var gpus = new List<string>(); using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController")) foreach (ManagementObject o in s.Get()) { var n = o["Name"]?.ToString(); if (!string.IsNullOrWhiteSpace(n)) gpus.Add(n); } bool transit = gpus.Count > 1 && gpus.Any(g => g.Contains("UHD", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase)); HwGpuText.Text = $"GPU: {string.Join(" + ", gpus)}" + (transit ? "\n💡 Активны дискретная и встроенная графика — рекомендуется привязка GPU ниже." : ""); } catch { } }
        private void UpdateAutoProfileLabel() { int threads = Environment.ProcessorCount; bool highCpu = threads >= 12; bool hybridGpu = HwGpuText.Text.Contains("+"); DiagText.Text = $"• ПРОФИЛЬ: {(highCpu ? $"CPU {threads}п (все ядра)" : $"CPU {threads}п (изоляция CPU0)")}{(hybridGpu ? " + Гибрид GPU" : " + Дискрет. GPU")}"; }
        private void BtnAutoTune_Click(object sender, RoutedEventArgs e) => AutoTuneByHardware(true);
        private void AutoTuneByHardware(bool notify) { int threads = Environment.ProcessorCount; bool highCpu = threads >= 12; bool hybridGpu = HwGpuText.Text.Contains("+"); bool lowRam = HwRamText.Text.Contains("8 ГБ") || HwRamText.Text.Contains("4 ГБ"); ChkAffinity.IsChecked = !highCpu; ChkGameBoost.IsChecked = !hybridGpu; ChkSoftBrowsers.IsChecked = true; ChkCleanLogs.IsChecked = true; ChkTrayMode.IsChecked = true; ChkTimer.IsChecked = false; ChkNetwork.IsChecked = false; ChkInput.IsChecked = false; ChkSleepLaunchers.IsChecked = true; ChkPcieAspm.IsChecked = true; ChkGpuPref.IsChecked = true; ChkCorePark.IsChecked = true; ChkGameMode.IsChecked = true; CmbAutoTrim.SelectedIndex = lowRam ? 4 : 11; UpdateAutoProfileLabel(); SaveUserSettings(); if (notify) MessageBox.Show($"Автопрофиль применён!\n\n• Потоков CPU: {threads}\n• Авто-очистка кэша: {(lowRam ? "каждые 3 мин" : "каждые 10 мин")}", "SZ Optimizer v1.5"); }
                private void BtnApplyHw_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ChkPcieAspm.IsChecked == true)
                {
                    Process.Start(new ProcessStartInfo("powercfg", "/SETACVALUEINDEX SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(1500);
                    Process.Start(new ProcessStartInfo("powercfg", "/SETACTIVE SCHEME_CURRENT") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(1500);
                }
                if (ChkCorePark.IsChecked == true)
                {
                    Process.Start(new ProcessStartInfo("powercfg", "/SETACVALUEINDEX SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 100") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(1500);
                    Process.Start(new ProcessStartInfo("powercfg", "/SETACTIVE SCHEME_CURRENT") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(1500);
                }
                if (ChkGameMode.IsChecked == true)
                {
                    using var gm = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
                    gm?.SetValue("AutoGameModeEnabled", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    using var gd = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                    gd?.SetValue("HwSchMode", 2, Microsoft.Win32.RegistryValueKind.DWord);
                    // Отключаем MPO (убирает статтеры OpenGL/LWJGL в Win10/11) и ставим киберспортивный квант планировщика CPU (0x26)
                    using var dwm = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\Dwm");
                    dwm?.SetValue("OverlayTestMode", 5, Microsoft.Win32.RegistryValueKind.DWord);
                    using var prio = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                    prio?.SetValue("Win32PrioritySeparation", 38, Microsoft.Win32.RegistryValueKind.DWord);
                }
                if (ChkGpuPref.IsChecked == true)
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
                    using var fso = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in Process.GetProcesses())
                    {
                        try { string n = p.ProcessName.ToLower(); if (n == "javaw" || n.Contains("stalzone") || n.Contains("stalcraft")) { string? ph = p.MainModule?.FileName; if (!string.IsNullOrEmpty(ph)) paths.Add(ph); } }
                        catch { } finally { p.Dispose(); }
                    }
                    foreach (var dir in GetAllGameDirs())
                    {
                        if (!Directory.Exists(dir)) continue;
                        foreach (var f in Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories))
                        {
                            string fn = Path.GetFileName(f).ToLower();
                            if (fn == "javaw.exe" || fn.Contains("stalzone") || fn.Contains("stalcraft")) paths.Add(f);
                        }
                    }
                    foreach (var ph in paths)
                    {
                        key?.SetValue(ph, "GpuPreference=2;");
                        fso?.SetValue(ph, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE");
                    }
                }
                SaveUserSettings();
                MessageBox.Show("Аппаратные настройки применены!\n• PCIe ASPM и парковка ядер отключены\n• Привязана дискретная GPU и отключён FSO (инпут-лаг)\n• Отключён MPO и настроен квант CPU (0x26)", "SZ Optimizer v1.5");
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "SZ Optimizer v1.5"); }
        }
        private void CmbAutoTrim_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (CmbAutoTrim == null) return; int idx = CmbAutoTrim.SelectedIndex; _autoTrimSec = idx == 0 ? 0 : (idx == 1 ? 30 : (idx - 1) * 60); _trimTick = 0; SaveUserSettings(); }
        private void RunSilentTrim() { if (IsJvmWrapperInstalled()) return; foreach (var p in Process.GetProcesses()) { try { string n = p.ProcessName.ToLower(); if (!n.Contains("szoptimizer") && (n.Contains("stalzone") || n.Contains("stalcraft") || n == "javaw")) { IntPtr h = OpenProcess(0x0500, false, p.Id); if (h != IntPtr.Zero) { EmptyWorkingSet(h); CloseHandle(h); } } } catch { } finally { p.Dispose(); } } }
        private void BtnTrimNow_Click(object sender, RoutedEventArgs e) { if (IsJvmWrapperInstalled()) { MessageBox.Show("Очистка ОЗУ пропущена: активен JVM Враппер (защита от фризов).", "SZ Optimizer"); return; } RunSilentTrim(); MessageBox.Show("Застоявшийся кэш ОЗУ игры очищен!", "SZ Optimizer v1.5"); }
                                        private List<string> GetAllGameDirs()
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(_customGameDir) && Directory.Exists(_customGameDir)) list.Add(_customGameDir);
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86); string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); 
            string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            list.Add(Path.Combine(up, @"EXBO\runtime\stalcraft"));
            list.Add(Path.Combine(up, @"EXBO\runtime\stalzone"));
            foreach (var drv in new[] { "C", "D", "E", "F", "G" })
            {
                list.Add($@"{drv}:\EXBO\runtime\stalcraft");
                list.Add($@"{drv}:\EXBO\runtime\stalzone");
                list.Add($@"{drv}:\Program Files (x86)\Steam\steamapps\common\STALCRAFT");
                list.Add($@"{drv}:\Program Files (x86)\Steam\steamapps\common\STALZONE");
                list.Add($@"{drv}:\Steam\steamapps\common\STALCRAFT");
                list.Add($@"{drv}:\SteamLibrary\steamapps\common\STALCRAFT");
                list.Add($@"{drv}:\SteamLibrary\steamapps\common\STALZONE");
                list.Add($@"{drv}:\GamesMailRu\STALCRAFT");
                list.Add($@"{drv}:\VKPlay\STALCRAFT");
                list.Add($@"{drv}:\GameCenter\STALCRAFT");
                list.Add($@"{drv}:\Epic Games\STALCRAFT");
                list.Add($@"{drv}:\Program Files\Epic Games\STALCRAFT");
            }
            try
            {
                string? steamPath = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    steamPath = steamPath.Replace('/', '\\');
                    list.Add(Path.Combine(steamPath, @"steamapps\common\STALCRAFT"));
                    list.Add(Path.Combine(steamPath, @"steamapps\common\STALZONE"));
                    string vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        {
                            string lib = m.Groups.Cast<System.Text.RegularExpressions.Group>().Last().Value.Replace("\\\\", "\\");
                            list.Add(Path.Combine(lib, @"steamapps\common\STALCRAFT"));
                            list.Add(Path.Combine(lib, @"steamapps\common\STALZONE"));
                        }
                }
                string epicMan = Path.Combine(progData, @"Epic\EpicGamesLauncher\Data\Manifests");
                if (Directory.Exists(epicMan))
                    foreach (var mf in Directory.EnumerateFiles(epicMan, "*.item"))
                    {
                        string txt = File.ReadAllText(mf);
                        if (txt.Contains("STALCRAFT", StringComparison.OrdinalIgnoreCase) || txt.Contains("STALZONE", StringComparison.OrdinalIgnoreCase))
                        {
                            var im = System.Text.RegularExpressions.Regex.Match(txt, "\"InstallLocation\"\\s*:\\s*\"([^\"]+)\"");
                            if (im.Success) list.Add(im.Groups.Cast<System.Text.RegularExpressions.Group>().Last().Value.Replace("\\\\", "\\"));
                        }
                    }
            }
            catch { }
            return list.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        private string? FindGameOptionsFile()
        {
            foreach (var dir in GetAllGameDirs())
            {
                string opt1 = Path.Combine(dir, @"config\options.json"); if (File.Exists(opt1)) return opt1;
                string opt2 = Path.Combine(dir, "options.json"); if (File.Exists(opt2)) return opt2;
            }
            return null;
        }
        private string? FindGameRootDir()
        {
            var candidates = GetAllGameDirs();
            if (!string.IsNullOrWhiteSpace(_customGameDir)) candidates.Add(_customGameDir);
            foreach (var drv in new[] { "C", "D", "E", "F", "G" })
            {
                candidates.Add($@"{drv}:\EXBO\runtime\stalcraft");
                candidates.Add($@"{drv}:\Program Files (x86)\Steam\steamapps\common\STALCRAFT");
                candidates.Add($@"{drv}:\SteamLibrary\steamapps\common\STALCRAFT");
            }
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86); string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); 
            candidates.Add(Path.Combine(up, @"EXBO\runtime\stalcraft"));
            foreach (var dir in candidates.Distinct())
                if (Directory.Exists(dir) && (Directory.Exists(Path.Combine(dir, "bin_global")) || Directory.Exists(Path.Combine(dir, "bin_ru")) || Directory.Exists(Path.Combine(dir, "bin_global_bak")) || Directory.Exists(Path.Combine(dir, "bin")) || Directory.Exists(Path.Combine(dir, "config"))))
                    return dir;
            return null;
        }
        private void RefreshServerRegionUi()
        {
            if (ServerRegionText == null) return;
            string? dir = FindGameRootDir();
            if (dir == null) { ServerRegionText.Text = "🌍 Активный сервер: папка игры не найдена"; ServerRegionText.Foreground = Brushes.Gray; GameFolderText.Text = "Укажите папку с bin_global / bin_ru вручную ->"; return; }
            GameFolderText.Text = dir;
            bool hasGlobal = Directory.Exists(Path.Combine(dir, "bin_global"));
            bool hasRu = Directory.Exists(Path.Combine(dir, "bin_ru"));
            bool hasBak = Directory.Exists(Path.Combine(dir, "bin_global_bak"));
            if (hasGlobal && (hasRu || hasBak)) { ServerRegionText.Text = "🌍 Активный сервер: EU / Global (bin_global)"; ServerRegionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4DABF7")); }
            else if (hasRu && !hasGlobal) { ServerRegionText.Text = "🌍 Активный сервер: RU (bin_ru → активен)"; ServerRegionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA")); }
            else { ServerRegionText.Text = "🌍 Активный сервер: стандартный клиент (одна папка bin)"; ServerRegionText.Foreground = Brushes.LightGray; }
        }
        private void BtnPickGameDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Выберите корневую папку STALCRAFT / STALZONE (где лежит bin_global)" };
            if (dlg.ShowDialog() == true) { _customGameDir = dlg.FolderName; SaveUserSettings(); RefreshServerRegionUi(); }
        }
        private void BtnSwitchRegion_Click(object sender, RoutedEventArgs e)
        {
            bool running = Process.GetProcesses().Any(p => { try { string n = p.ProcessName.ToLower(); return !n.Contains("szoptimizer") && (n == "stalzonew" || n == "stalzone" || n == "stalcraft" || n == "stalcraftw" || n.Contains("stalcraft") || n == "javaw"); } catch { return false; } finally { p.Dispose(); } });
            if (running) { MessageBox.Show("Сначала закройте игру и лаунчер перед сменой сервера!", "SZ Optimizer v1.5", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string? dir = FindGameRootDir();
            if (dir == null) { MessageBox.Show("Папка игры не найдена. Нажмите «📂 Папка» и укажите каталог игры.", "SZ Optimizer v1.5"); return; }
            string pGlobal = Path.Combine(dir, "bin_global"), pRu = Path.Combine(dir, "bin_ru"), pBak = Path.Combine(dir, "bin_global_bak");
            if (!Directory.Exists(pRu) && !Directory.Exists(pBak)) { MessageBox.Show("В папке игры не найдена вторая региональная директория (bin_ru / bin_global_bak).\n\nУбедитесь, что в лаунчере скачаны файлы обоих регионов или папка bin_ru существует.", "SZ Optimizer v1.5"); return; }
            var ask = MessageBox.Show("⚠️ ВАЖНОЕ ПРЕДУПРЕЖДЕНИЕ!\n\nНа серверах RU и EU используются РАЗНЫЕ аккаунты — персонажи, снаряжение и прогресс между ними НЕ переносятся.\n\nВыполнить переключение папок региона сейчас?", "Смена региона RU ↔ EU — SZ Optimizer v1.5", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes) return;
            try
            {
                if (Directory.Exists(pGlobal) && Directory.Exists(pRu)) { Directory.Move(pGlobal, pBak); Directory.Move(pRu, pGlobal); }
                else if (Directory.Exists(pGlobal) && Directory.Exists(pBak)) { Directory.Move(pGlobal, pRu); Directory.Move(pBak, pGlobal); }
                else if (!Directory.Exists(pGlobal) && Directory.Exists(pRu)) { Directory.Move(pRu, pGlobal); }
                RefreshServerRegionUi();
                MessageBox.Show("Регион серверов успешно переключён! Можете запускать игру.", "SZ Optimizer v1.5");
            }
            catch (Exception ex) { MessageBox.Show("Ошибка при переименовании папок (возможно, файлы заняты лаунчером):\n" + ex.Message, "SZ Optimizer v1.5"); }
        }
        private void BtnCleanTemp_Click(object sender, RoutedEventArgs e)
        {
            var targets = new List<string> { Path.GetTempPath() };
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (var p in new[] { Path.Combine(local, "EXBO"), Path.Combine(roaming, "EXBO"), @"C:\EXBO\logs", @"D:\EXBO\logs" })
                if (Directory.Exists(p)) targets.Add(p);

            long freed = 0; int deleted = 0, skipped = 0;
            foreach (var dir in targets.Distinct())
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories); } catch { continue; }
                foreach (var f in files)
                {
                    try
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        bool isTemp = dir.Equals(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);
                        if (!isTemp && ext != ".log" && ext != ".tmp" && ext != ".cache" && ext != ".old" && !f.Contains("cache", StringComparison.OrdinalIgnoreCase) && !f.Contains("logs", StringComparison.OrdinalIgnoreCase)) continue;
                        long len = new FileInfo(f).Length;
                        File.Delete(f);
                        freed += len; deleted++;
                    }
                    catch { skipped++; }
                }
            }
            string sizeStr = freed >= 1024L * 1024 * 1024 ? $"{freed / (1024.0 * 1024 * 1024):F2} ГБ" : $"{freed / (1024.0 * 1024):F1} МБ";
            MessageBox.Show($"Очистка кэша EXBO и %TEMP% завершена!\n\n• Удалено файлов: {deleted}\n• Освобождено: {sizeStr}\n• Пропущено (занято системой): {skipped}", "SZ Optimizer v1.5");
        }
        private void BtnCleanShaders_Click(object sender, RoutedEventArgs e) { var warn = MessageBox.Show("⚠️ ВНИМАНИЕ! ЧАСТОЕ ИСПОЛЬЗОВАНИЕ ЭТОЙ ФУНКЦИИ ВРЕДНО!\n\nПосле очистки кэша шейдеров видеокарте придётся заново компилировать текстуры и эффекты — в первые 10–15 минут игры возможны микрофризы.\n\nИспользуйте сброс кэша шейдеров ТОЛЬКО после крупных обновлений STALZONE или драйвера видеокарты (при закрытой игре).\n\nОчистить кэш шейдеров сейчас?", "⚠️ ПРЕДУПРЕЖДЕНИЕ — SZ Optimizer v1.5", MessageBoxButton.YesNo, MessageBoxImage.Warning); if (warn != MessageBoxResult.Yes) return; string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); string[] dirs = { Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(local, "NVIDIA", "GLCache"), Path.Combine(local, "AMD", "DxCache"), Path.Combine(local, "AMD", "GLCache"), Path.Combine(local, "Intel", "ShaderCache"), Path.Combine(local, "D3DSCache") }; long freed = 0; int cnt = 0; foreach (var d in dirs) { if (!Directory.Exists(d)) continue; foreach (var file in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) { try { long l = new FileInfo(file).Length; File.Delete(file); freed += l; cnt++; } catch { } } } ReassertJvmWrapper(); MessageBox.Show($"Кэш шейдеров GPU очищен!\n\n• Удалено файлов: {cnt}\n• Освобождено: {Math.Round(freed / (1024.0 * 1024.0), 1)} МБ", "SZ Optimizer v1.5"); }
        private void BtnApplyGfxPreset_Click(object sender, RoutedEventArgs e) { try { string? opt = FindGameOptionsFile(); if (opt == null) { MessageBox.Show("Файл options.json не найден.", "SZ Optimizer v1.5"); return; } string bak = opt + ".szbak"; if (!File.Exists(bak)) File.Copy(opt, bak); int idx = CmbGfxPreset.SelectedIndex; if (idx == 2) { if (File.Exists(bak)) File.Copy(bak, opt, true); MessageBox.Show("Исходный options.json восстановлен!", "SZ Optimizer v1.5"); return; } string json = File.ReadAllText(opt); void SetJson(string k, string v) { json = System.Text.RegularExpressions.Regex.Replace(json, $"\"{k}\"\\s*:\\s*[^,\\r\\n]+", $"\"{k}\": {v}"); } SetJson("blur_hud_elements", "false"); SetJson("godrays_enabled", "false"); SetJson("ssao", "0"); SetJson("shadows_dynamic", "false"); SetJson("shadows_pcf", "false"); SetJson("multithread_loading", "true"); SetJson("precompile_shaders", "true"); if (idx == 0) { SetJson("bloom", "false"); SetJson("grass_flag", "false"); SetJson("bullet_holes", "false"); SetJson("particles_resolution", "0"); SetJson("details_amount", "0"); } else { SetJson("bloom", "true"); SetJson("grass_flag", "true"); SetJson("particles_resolution", "1"); } json = System.Text.RegularExpressions.Regex.Replace(json, "\"window_x\"\\s*:\\s*-?\\d+", "\"window_x\": 0");
        json = System.Text.RegularExpressions.Regex.Replace(json, "\"window_y\"\\s*:\\s*-?\\d+", "\"window_y\": 0");
        json = System.Text.RegularExpressions.Regex.Replace(json, "\"window_maximized\"\\s*:\\s*(true|false)", "\"window_maximized\": true");
        json = System.Text.RegularExpressions.Regex.Replace(json, "\"borderless\"\\s*:\\s*(true|false)", "\"borderless\": true");
        File.WriteAllText(opt, json); MessageBox.Show("Пресет графики применён к options.json!", "SZ Optimizer v1.5"); } catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "SZ Optimizer v1.5"); } }
        private void InitPixelEditor() { for (int i = 0; i < 256; i++) { var btn = new Button { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26282C")), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#383A40")), BorderThickness = new Thickness(0.5), Margin = new Thickness(0), IsHitTestVisible = false }; _gridButtons.Add(btn); PixelEditorGrid.Children.Add(btn); } PixelEditorGrid.PreviewMouseDown += (s, e) => HandleGridDraw(e); PixelEditorGrid.PreviewMouseMove += (s, e) => HandleGridDraw(e); }
        private void HandleGridDraw(MouseEventArgs e) { if (e.LeftButton != MouseButtonState.Pressed && e.RightButton != MouseButtonState.Pressed) return; var pos = e.GetPosition(PixelEditorGrid); int x = (int)(pos.X / (PixelEditorGrid.ActualWidth / 16.0)), y = (int)(pos.Y / (PixelEditorGrid.ActualHeight / 16.0)); if (x < 0 || x >= 16 || y < 0 || y >= 16) return; int idx = y * 16 + x; if (e.LeftButton == MouseButtonState.Pressed && !_pixels[idx]) { _pixels[idx] = true; _gridButtons[idx].Background = new SolidColorBrush(_crossColor); _currentPreset = "PIXEL"; UpdateOverlay(); SaveUserSettings(); } else if (e.RightButton == MouseButtonState.Pressed && _pixels[idx]) { _pixels[idx] = false; _gridButtons[idx].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26282C")); _currentPreset = "PIXEL"; UpdateOverlay(); SaveUserSettings(); } }
        private void RefreshGridColors() { for (int i = 0; i < 256; i++) _gridButtons[i].Background = _pixels[i] ? new SolidColorBrush(_crossColor) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26282C")); }
        private void BtnClearGrid_Click(object sender, RoutedEventArgs e) { for (int i = 0; i < 256; i++) _pixels[i] = false; RefreshGridColors(); UpdateOverlay(); SaveUserSettings(); }
        private void InitPresetGallery() { var items = new List<(string Tag, string Name)> { ("Dot-2x2", "Точка 2px"), ("Dot-4x4", "Точка 4px"), ("Tiny-cross", "Мини-крест"), ("Cross-dot-small", "Крест мал."), ("Cross-dot", "Крест"), ("T-shape-dot", "Т-точка"), ("T-shape-chevron", "Т-уголок"), ("Chevron-small", "Уголок мал."), ("Chevron-medium", "Уголок"), ("Circle-dot", "Круг-точка"), ("Circle-cross", "Круг-крест"), ("Cross-diag-dot", "Косой крест"), ("Square-cross-dot", "Квадрат"), ("2-sectors-dot", "Сектора"), ("Horiz-dot", "Линии-точка"), ("PIXEL", "Свой") }; foreach (var item in items) { var canvas = new Canvas { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center }; DrawMiniIcon(canvas, item.Tag); var sp = new StackPanel(); sp.Children.Add(canvas); sp.Children.Add(new TextBlock { Text = item.Name, FontSize = 9.5, Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center }); var btn = new Button { Content = sp, Tag = item.Tag, Height = 46, Margin = new Thickness(2), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18191C")), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Tag == _currentPreset ? "#00D4AA" : "#313338")), BorderThickness = new Thickness(item.Tag == _currentPreset ? 1.5 : 1), Cursor = Cursors.Hand }; btn.Click += (s, e) => { _currentPreset = item.Tag; HighlightSelectedPreset(); UpdateOverlay(); SaveUserSettings(); }; _presetButtons.Add(btn); PresetGalleryGrid.Children.Add(btn); } }
        private void HighlightSelectedPreset() { foreach (var b in _presetButtons) { bool sel = (string)b.Tag == _currentPreset; b.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sel ? "#00D4AA" : "#313338")); b.BorderThickness = new Thickness(sel ? 1.5 : 1); } }
        private void DrawMiniIcon(Canvas c, string tag) { var b = Brushes.White; void D(double sz) { var e = new System.Windows.Shapes.Ellipse { Width = sz, Height = sz, Fill = b }; Canvas.SetLeft(e, 14 - sz / 2); Canvas.SetTop(e, 14 - sz / 2); c.Children.Add(e); } void L(double x1, double y1, double x2, double y2, double t = 1.5) { c.Children.Add(new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = t }); } switch (tag) { case "Dot-2x2": D(2.5); break; case "Dot-4x4": D(4.5); break; case "Tiny-cross": L(10, 14, 18, 14); L(14, 10, 14, 18); break; case "Cross-dot-small": D(2); L(7, 14, 11, 14); L(17, 14, 21, 14); L(14, 7, 14, 11); L(14, 17, 14, 21); break; case "Cross-dot": D(2); L(4, 14, 10, 14); L(18, 14, 24, 14); L(14, 4, 14, 10); L(14, 18, 14, 24); break; case "T-shape-dot": D(2); L(5, 14, 11, 14); L(17, 14, 23, 14); L(14, 17, 14, 23); break; case "T-shape-chevron": L(5, 14, 10, 14); L(18, 14, 23, 14); L(11, 16, 14, 13); L(14, 13, 17, 16); L(14, 18, 14, 23); break; case "Chevron-small": L(10, 16, 14, 12); L(14, 12, 18, 16); break; case "Chevron-medium": L(8, 18, 14, 11); L(14, 11, 20, 18); break; case "Circle-dot": D(2.5); var e1 = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, Stroke = b, StrokeThickness = 1.5 }; Canvas.SetLeft(e1, 6); Canvas.SetTop(e1, 6); c.Children.Add(e1); break; case "Circle-cross": var e2 = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Stroke = b, StrokeThickness = 1.5 }; Canvas.SetLeft(e2, 7); Canvas.SetTop(e2, 7); c.Children.Add(e2); L(4, 14, 9, 14); L(19, 14, 24, 14); L(14, 4, 14, 9); L(14, 19, 14, 24); break; case "Cross-diag-dot": D(2); L(7, 7, 11, 11); L(17, 17, 21, 21); L(21, 7, 17, 11); L(11, 17, 7, 21); break; case "Square-cross-dot": D(2); L(6, 6, 10, 6); L(6, 6, 6, 10); L(18, 6, 22, 6); L(22, 6, 22, 10); L(6, 22, 10, 22); L(6, 18, 6, 22); L(18, 22, 22, 22); L(22, 18, 22, 22); break; case "2-sectors-dot": D(2.5); L(7, 8, 5, 14, 2.5); L(5, 14, 7, 20, 2.5); L(21, 8, 23, 14, 2.5); L(23, 14, 21, 20, 2.5); break; case "Horiz-dot": D(2.5); L(3, 14, 9, 14, 1.8); L(19, 14, 25, 14, 1.8); break; case "PIXEL": c.Children.Add(new TextBlock { Text = "✏️", FontSize = 14, Margin = new Thickness(5, 3, 0, 0) }); break; } }
                        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExKbd(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        private LowLevelMouseProc? _kbdHookProc;
        private IntPtr _kbdHookId = IntPtr.Zero;
                private void InstallWheelHook()
        {
            if (_mouseHookId == IntPtr.Zero)
            {
                _mouseHookProc = (nCode, wParam, lParam) =>
                {
                    if (nCode >= 0)
                    {
                        if (wParam == (IntPtr)0x0204) Dispatcher.BeginInvoke(() => { if (_overlayActive && _overlay != null && ChkHideOnRmb != null && ChkHideOnRmb.IsChecked == true) _overlay.Hide(); });
                        else if (wParam == (IntPtr)0x0205) Dispatcher.BeginInvoke(() => { if (_overlay != null) _overlay.Show(); });
                        else if (wParam == (IntPtr)0x020A && ChkWeaponAutoSwitch != null && ChkWeaponAutoSwitch.IsChecked == true && !_waitingForKey && !_waitingForProfKey && !_waitingForHolsterKey)
                        {
                            int delta = (short)((Marshal.ReadInt32(lParam, 8) >> 16) & 0xffff);
                            if (delta != 0)
                            {
                                int next = delta < 0 ? (_curWeaponSlot % 5) + 1 : (_curWeaponSlot == 1 ? 5 : _curWeaponSlot - 1);
                                Dispatcher.BeginInvoke(() => ApplyWeaponSlot(next));
                            }
                        }
                    }
                    return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                };
                _mouseHookId = SetWindowsHookEx(14, _mouseHookProc, IntPtr.Zero, 0);
            }
            if (_kbdHookId == IntPtr.Zero)
            {
                _kbdHookProc = (nCode, wParam, lParam) =>
                {
                    if (nCode >= 0 && (wParam == (IntPtr)0x0100 || wParam == (IntPtr)0x0104) && ChkWeaponAutoSwitch != null && ChkWeaponAutoSwitch.IsChecked == true && !_waitingForKey && !_waitingForProfKey && !_waitingForHolsterKey)
                    {
                        int vk = Marshal.ReadInt32(lParam);
                        if (vk == 0x31) Dispatcher.BeginInvoke(() => ApplyWeaponSlot(1));
                        else if (vk == 0x32) Dispatcher.BeginInvoke(() => ApplyWeaponSlot(2));
                        else if (vk == 0x33) Dispatcher.BeginInvoke(() => ApplyWeaponSlot(3));
                        else if (vk == 0x34 || vk == 0x35 || (_holsterVk > 0 && vk == _holsterVk)) Dispatcher.BeginInvoke(() => ApplyWeaponSlot(4));
                    }
                    return CallNextHookEx(_kbdHookId, nCode, wParam, lParam);
                };
                _kbdHookId = SetWindowsHookEx(13, _kbdHookProc, IntPtr.Zero, 0);
            }
        }
        private void InitHotkeyTimer() { new Thread(() => { while (true) { try { Thread.Sleep(25); bool modOk = (_hotkeyMod == 0) || ((_hotkeyMod == 1 && (GetAsyncKeyState(0x12) & 0x8000) != 0) || (_hotkeyMod == 2 && (GetAsyncKeyState(0x11) & 0x8000) != 0) || (_hotkeyMod == 3 && (GetAsyncKeyState(0x10) & 0x8000) != 0)); bool keyDown = !_waitingForKey && modOk && ((GetAsyncKeyState(_hotkeyVk) & 0x8000) != 0); if (keyDown && !_f8WasDown) Dispatcher.BeginInvoke(ToggleOverlay); _f8WasDown = keyDown; bool pModOk = (_profMod == 0) || ((_profMod == 1 && (GetAsyncKeyState(0x12) & 0x8000) != 0) || (_profMod == 2 && (GetAsyncKeyState(0x11) & 0x8000) != 0) || (_profMod == 3 && (GetAsyncKeyState(0x10) & 0x8000) != 0)); bool pDown = !_waitingForKey && !_waitingForProfKey && _profVk > 0 && pModOk && ((GetAsyncKeyState(_profVk) & 0x8000) != 0); if (pDown && !_profKeyWasDown) Dispatcher.BeginInvoke(() => SwitchCrossProfile(_activeCrossProf == 1 ? 2 : 1)); _profKeyWasDown = pDown;
if (_overlayActive && _overlay != null && ChkHideOnRmb != null && ChkHideOnRmb.IsChecked == true)
{
    bool rmb = (GetAsyncKeyState(0x02) & 0x8000) != 0;
    if (rmb && !_rmbHidden) { _overlay.Hide(); _rmbHidden = true; }
    else if (!rmb && _rmbHidden) { _overlay.Show(); _rmbHidden = false; }
}
else if (_rmbHidden && _overlay != null && _overlayActive) { _overlay.Show(); _rmbHidden = false; }
if (ChkWeaponAutoSwitch != null && ChkWeaponAutoSwitch.IsChecked == true && _overlayActive && _overlay != null && !_waitingForKey && !_waitingForProfKey)
{
    bool enterDown = (GetAsyncKeyState(0x0D) & 0x8000) != 0;
    bool tDown = (GetAsyncKeyState(0x54) & 0x8000) != 0;
    bool escDown = (GetAsyncKeyState(0x1B) & 0x8000) != 0;
    if (enterDown && !_enterWasDown) _chatTyping = !_chatTyping;
    else if (tDown && !_tWasDown && !_chatTyping) _chatTyping = true;
    else if (escDown) _chatTyping = false;
    _enterWasDown = enterDown; _tWasDown = tDown;
    if (!_chatTyping && !_waitingForHolsterKey)
    {
        bool k1 = (GetAsyncKeyState(0x31) & 0x8000) != 0;
        bool k2 = (GetAsyncKeyState(0x32) & 0x8000) != 0;
        bool k3 = (GetAsyncKeyState(0x33) & 0x8000) != 0;
        bool k4 = (GetAsyncKeyState(0x34) & 0x8000) != 0;
        bool k5 = (GetAsyncKeyState(0x35) & 0x8000) != 0;
        bool khol = _holsterVk > 0 && (GetAsyncKeyState(_holsterVk) & 0x8000) != 0;
        if (k1 && !_w1WasDown) ApplyWeaponSlot(1);
        if (k2 && !_w2WasDown) ApplyWeaponSlot(2);
        if (k3 && !_w3WasDown) ApplyWeaponSlot(3);
        if (k4 && !_w4WasDown) ApplyWeaponSlot(4);
        if (k5 && !_w5WasDown) ApplyWeaponSlot(5);
        if (khol && !_holsterWasDown) ApplyWeaponSlot(4);
        _w1WasDown = k1; _w2WasDown = k2; _w3WasDown = k3; _w4WasDown = k4; _w5WasDown = k5; _holsterWasDown = khol;
    }
}  } catch { } } }) { IsBackground = true }.Start(); }
        private void ChkHideOnRmb_Changed(object sender, RoutedEventArgs e) { if (_overlay != null) _overlay.Show(); if (!_isLoading) SaveUserSettings(); }
        private void ToggleOverlay() { _overlayActive = !_overlayActive; if (_overlayActive) { if (_overlay == null) _overlay = new CrosshairOverlay(); _overlay.Show(); UpdateOverlay(); BtnToggleOverlay.Content = $"🛑 ВЫКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})"; BtnToggleOverlay.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A2326")); } else { _overlay?.Dispose(); _overlay = null; BtnToggleOverlay.Content = $"🎯 ВКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})"; BtnToggleOverlay.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2D31")); } SaveUserSettings(); }
        private void BtnBindHotkey_Click(object sender, RoutedEventArgs e) { if (_waitingForKey) return; _waitingForKey = true; BtnBindHotkey.Content = "НАЖМИТЕ КЛАВИШУ..."; BtnBindHotkey.Background = new SolidColorBrush(Colors.DarkGoldenrod); this.Activate(); Keyboard.Focus(this); KeyEventHandler? kh = null; MouseButtonEventHandler? mh = null; void FinishBind(int mod, int vk, string name) { _hotkeyMod = mod; _hotkeyVk = vk; _hotkeyName = name; _waitingForKey = false; this.RemoveHandler(Keyboard.PreviewKeyDownEvent, kh); this.RemoveHandler(Mouse.PreviewMouseDownEvent, mh); BtnBindHotkey.Content = $"Клавиша: {_hotkeyName}"; BtnBindHotkey.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18191C")); BtnToggleOverlay.Content = _overlayActive ? $"🛑 ВЫКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})" : $"🎯 ВКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})"; SaveUserSettings(); } kh = (s, ev) => { var k = ev.Key == Key.System ? ev.SystemKey : ev.Key; if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift) return; int mod = 0; string mStr = ""; if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) { mod = 1; mStr = "Alt+"; } else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { mod = 2; mStr = "Ctrl+"; } else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { mod = 3; mStr = "Shift+"; } FinishBind(mod, KeyInterop.VirtualKeyFromKey(k), mStr + k.ToString()); ev.Handled = true; }; mh = (s, ev) => { if (ev.ChangedButton != MouseButton.XButton1 && ev.ChangedButton != MouseButton.XButton2) return; FinishBind(0, ev.ChangedButton == MouseButton.XButton1 ? 0x05 : 0x06, ev.ChangedButton == MouseButton.XButton1 ? "Mouse4" : "Mouse5"); ev.Handled = true; }; this.AddHandler(Keyboard.PreviewKeyDownEvent, kh, true); this.AddHandler(Mouse.PreviewMouseDownEvent, mh, true); }
                private string PackCurrentProfile() { var sb = new StringBuilder(); for (int i = 0; i < 256; i++) sb.Append(_pixels[i] ? '1' : '0'); return $"{_currentPreset}|#{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}|{(int)ScaleSlider.Value}|{_offsetX}|{_offsetY}|{sb}"; }
        private void UnpackProfile(string? raw) { if (string.IsNullOrEmpty(raw)) return; var p = raw.Split('|'); if (p.Length != 6 || p.ElementAt(5).Length != 256) return; _currentPreset = p.ElementAt(0); HighlightSelectedPreset(); _crossColor = (Color)ColorConverter.ConvertFromString(p.ElementAt(1)); ColorPreviewBox.Background = new SolidColorBrush(_crossColor); HexColorBox.Text = p.ElementAt(1); RgbInfoText.Text = $"R: {_crossColor.R}   G: {_crossColor.G}   B: {_crossColor.B}"; if (double.TryParse(p.ElementAt(2), NumberStyles.Any, CultureInfo.InvariantCulture, out double sc)) ScaleSlider.Value = Math.Clamp(sc, 50, 300); _offsetX = int.Parse(p.ElementAt(3)); _offsetY = int.Parse(p.ElementAt(4)); OffsetXText.Text = _offsetX.ToString(); OffsetYText.Text = _offsetY.ToString(); string bits = p.ElementAt(5); for (int i = 0; i < 256; i++) _pixels[i] = bits.ElementAt(i) == '1'; RefreshGridColors(); UpdateOverlay(); }
                private void UpdateProfButtonsUi()
        {
            if (BtnCrossProf1 == null) return;
            var btns = new[] { BtnCrossProf1, BtnCrossProf2, BtnCrossProf3, BtnCrossProf4, BtnCrossProf5 };
            for (int i = 0; i < btns.Length; i++)
            {
                if (btns.ElementAt(i) == null) continue;
                bool act = _activeCrossProf == (i + 1);
                btns.ElementAt(i).Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(act ? "#00D4AA" : "#2B2D31"));
                btns.ElementAt(i).Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(act ? "#111214" : "#B5BAC1"));
            }
        }
        private void ApplyWeaponSlot(int slot)
        {
            _curWeaponSlot = Math.Clamp(slot, 1, 5);
            int targetProf = _curWeaponSlot switch { 1 => 3, 2 => 4, 3 => 5, _ => 1 };
            if (_activeCrossProf != targetProf) SwitchCrossProfile(targetProf);
        }
        private void BtnBindHolsterKey_Click(object sender, RoutedEventArgs e)
        {
            if (_waitingForHolsterKey) return;
            _waitingForHolsterKey = true; BtnBindHolsterKey.Content = "ЖМИ..."; this.Activate(); Keyboard.Focus(this);
            KeyEventHandler? kh = null;
            kh = (s, ev) =>
            {
                var k = ev.Key == Key.System ? ev.SystemKey : ev.Key;
                if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift) return;
                _waitingForHolsterKey = false; this.RemoveHandler(Keyboard.PreviewKeyDownEvent, kh);
                if (k == Key.Escape) { _holsterVk = 0; BtnBindHolsterKey.Content = "Убрать: Нет"; }
                else { _holsterVk = KeyInterop.VirtualKeyFromKey(k); BtnBindHolsterKey.Content = $"Убрать: {k}"; }
                SaveUserSettings(); ev.Handled = true;
            };
            this.AddHandler(Keyboard.PreviewKeyDownEvent, kh, true);
        }
        private void SwitchCrossProfile(int target) { if (target == _activeCrossProf) return; _profData[_activeCrossProf - 1] = PackCurrentProfile(); _activeCrossProf = target; bool prevLoad = _isLoading; _isLoading = true; UnpackProfile(_profData[_activeCrossProf - 1]); _isLoading = prevLoad; UpdateProfButtonsUi(); SaveUserSettings(); }
        private void BtnCrossProf_Click(object sender, RoutedEventArgs e) { if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int t)) SwitchCrossProfile(t); }
        private void BtnBindProfHotkey_Click(object sender, RoutedEventArgs e) { if (_waitingForProfKey) return; _waitingForProfKey = true; BtnBindProfHotkey.Content = "ЖМИ..."; this.Activate(); Keyboard.Focus(this); KeyEventHandler? kh = null; MouseButtonEventHandler? mh = null; void Done(int mod, int vk, string name) { _profMod = mod; _profVk = vk; _waitingForProfKey = false; this.RemoveHandler(Keyboard.PreviewKeyDownEvent, kh); this.RemoveHandler(Mouse.PreviewMouseDownEvent, mh); BtnBindProfHotkey.Content = $"Смена: {name}"; SaveUserSettings(); } kh = (s, ev) => { var k = ev.Key == Key.System ? ev.SystemKey : ev.Key; if (k == Key.Escape) { Done(0, 0, "Нет"); ev.Handled = true; return; } if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift) return; int mod = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? 1 : ((Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 2 : ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 3 : 0)); string m = mod == 1 ? "Alt+" : (mod == 2 ? "Ctrl+" : (mod == 3 ? "Shift+" : "")); Done(mod, KeyInterop.VirtualKeyFromKey(k), m + k); ev.Handled = true; }; mh = (s, ev) => { if (ev.ChangedButton != MouseButton.XButton1 && ev.ChangedButton != MouseButton.XButton2) return; Done(0, ev.ChangedButton == MouseButton.XButton1 ? 0x05 : 0x06, ev.ChangedButton == MouseButton.XButton1 ? "Mouse4" : "Mouse5"); ev.Handled = true; }; this.AddHandler(Keyboard.PreviewKeyDownEvent, kh, true); this.AddHandler(Mouse.PreviewMouseDownEvent, mh, true); }
        private void ChkWeaponAutoSwitch_Changed(object sender, RoutedEventArgs e) { _chatTyping = false; if (_weaponHidden && _overlay != null && _overlayActive) { _overlay.Show(); _weaponHidden = false; } if (!_isLoading) SaveUserSettings(); }
        private void BtnToggleOverlay_Click(object sender, RoutedEventArgs e) => ToggleOverlay();
        private void UpdateOverlay() { if (_overlay != null && _overlayActive) _overlay.Render(_currentPreset, _crossColor, (ScaleSlider.Value / 100.0) * 1.5, _offsetX, _offsetY, _pixels, _rmbWasHidden); }
        private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e) => UpdateHue(e.GetPosition(HueCanvas).Y);
        private void HueCanvas_MouseMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) UpdateHue(e.GetPosition(HueCanvas).Y); }
        private void UpdateHue(double y) { y = Math.Clamp(y, 0, 128); Canvas.SetTop(HueMarker, y - 1.5); _hue = (y / 128.0) * 360.0; SvCanvas.Background = new SolidColorBrush(HsvToColor(_hue, 1, 1)); ApplyHsv(); }
        private void SvCanvas_MouseDown(object sender, MouseButtonEventArgs e) => UpdateSv(e.GetPosition(SvCanvas));
        private void SvCanvas_MouseMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) UpdateSv(e.GetPosition(SvCanvas)); }
        private void UpdateSv(Point p) { double x = Math.Clamp(p.X, 0, 138), y = Math.Clamp(p.Y, 0, 128); Canvas.SetLeft(SvMarker, x - 5); Canvas.SetTop(SvMarker, y - 5); _sat = x / 138.0; _val = 1.0 - (y / 128.0); ApplyHsv(); }
        private void ApplyHsv() { if (ColorPreviewBox == null || HexColorBox == null) return; _crossColor = HsvToColor(_hue, _sat, _val); ColorPreviewBox.Background = new SolidColorBrush(_crossColor); RgbInfoText.Text = $"R: {_crossColor.R}   G: {_crossColor.G}   B: {_crossColor.B}"; HexColorBox.Text = $"#{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}"; RefreshGridColors(); UpdateOverlay(); SaveUserSettings(); }
        private void HexColorBox_LostFocus(object sender, RoutedEventArgs e) { try { _crossColor = (Color)ColorConverter.ConvertFromString(HexColorBox.Text.Trim()); SyncHsvVisualsFromColor(_crossColor); ColorPreviewBox.Background = new SolidColorBrush(_crossColor); RgbInfoText.Text = $"R: {_crossColor.R}   G: {_crossColor.G}   B: {_crossColor.B}"; RefreshGridColors(); UpdateOverlay(); SaveUserSettings(); } catch { } }
                private void SyncHsvVisualsFromColor(Color c)
        {
            if (SvCanvas == null || SvMarker == null || HueMarker == null) return;
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            double h = 0, s = (max == 0) ? 0 : d / max, v = max;
            if (d > 0)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * (((b - r) / d) + 2);
                else h = 60 * (((r - g) / d) + 4);
                if (h < 0) h += 360;
            }
            _hue = h; _sat = s; _val = v;
            Canvas.SetTop(HueMarker, Math.Clamp((_hue / 360.0) * 128.0, 0, 128) - 1.5);
            SvCanvas.Background = new SolidColorBrush(HsvToColor(_hue, 1, 1));
            Canvas.SetLeft(SvMarker, Math.Clamp(_sat * 138.0, 0, 138) - 5);
            Canvas.SetTop(SvMarker, Math.Clamp((1.0 - _val) * 128.0, 0, 128) - 5);
            if (ColorPreviewBox != null) ColorPreviewBox.Background = new SolidColorBrush(c);
            if (RgbInfoText != null) RgbInfoText.Text = $"R: {c.R}   G: {c.G}   B: {c.B}";
            if (HexColorBox != null) HexColorBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        private static Color HsvToColor(double h, double s, double v) { double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0; if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; } else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; } return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255)); }
        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ScaleLabel != null) ScaleLabel.Text = $"3. Размер (Масштаб): {(int)ScaleSlider.Value}%"; UpdateOverlay(); SaveUserSettings(); }
        private void Offset_Click(object sender, RoutedEventArgs e) { if (sender is Button b && b.Tag is string t) { if (t == "X-") _offsetX--; else if (t == "X+") _offsetX++; else if (t == "Y-") _offsetY--; else if (t == "Y+") _offsetY++; OffsetXText.Text = _offsetX.ToString(); OffsetYText.Text = _offsetY.ToString(); UpdateOverlay(); SaveUserSettings(); } }
        private void BtnExportCode_Click(object sender, RoutedEventArgs e) { var sb = new StringBuilder(); for (int i = 0; i < 256; i++) sb.Append(_pixels.ElementAt(i) ? '1' : '0'); Clipboard.SetText($"SZ2:#{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}:{(int)ScaleSlider.Value}:{_offsetX}:{_offsetY}:{sb}"); MessageBox.Show("Код скопирован!", "SZ Optimizer"); }
        private void BtnImportCode_Click(object sender, RoutedEventArgs e) { try { var p = Clipboard.GetText().Trim().Split(':'); if (p.Length == 6 && p.ElementAt(0) == "SZ2" && p.ElementAt(5).Length == 256) { _crossColor = (Color)ColorConverter.ConvertFromString(p.ElementAt(1)); ScaleSlider.Value = double.Parse(p.ElementAt(2)); _offsetX = int.Parse(p.ElementAt(3)); _offsetY = int.Parse(p.ElementAt(4)); OffsetXText.Text = _offsetX.ToString(); OffsetYText.Text = _offsetY.ToString(); string bits = p.ElementAt(5); for (int i = 0; i < 256; i++) _pixels[i] = bits.ElementAt(i) == '1'; _currentPreset = "PIXEL"; HighlightSelectedPreset(); RefreshGridColors(); UpdateOverlay(); SaveUserSettings(); } } catch { } }
        private void RunDiagnostics() { try { long mb = 0; using (var s = new ManagementObjectSearcher("SELECT AllocatedBaseSize FROM Win32_PageFileUsage")) foreach (ManagementObject o in s.Get()) mb += Convert.ToInt64(o["AllocatedBaseSize"]); DiagnosticsText.Text = $"● ДИАГНОСТИКА: Ядер CPU: {Environment.ProcessorCount}  |  Файл подкачки: {Math.Round(mb / 1024.0)} ГБ (ОК)"; } catch { DiagnosticsText.Text = "● ДИАГНОСТИКА: Система готова"; } }
        private void CheckAutoStartTask() { try { using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false); bool regOk = rk?.GetValue("SZOptimizer") != null; var p = Process.Start(new ProcessStartInfo("schtasks", "/Query /TN \"SZOptimizerAuto\"") { CreateNoWindow = true, UseShellExecute = false }); p?.WaitForExit(2000); ChkAutoStart.IsChecked = regOk || (p?.ExitCode == 0); } catch { } }
        private void ChkAutoStart_Changed(object sender, RoutedEventArgs e) { if (!IsLoaded || _isLoading) return; string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? ""; try { using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true); if (ChkAutoStart.IsChecked == true && File.Exists(exe)) { rk?.SetValue("SZOptimizer", $"\"{exe}\" -autostart"); Process.Start(new ProcessStartInfo("schtasks", $"/Create /TN \"SZOptimizerAuto\" /TR \"\\\"{exe}\\\" -autostart\" /SC ONLOGON /RL HIGHEST /F") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000); } else { rk?.DeleteValue("SZOptimizer", false); Process.Start(new ProcessStartInfo("schtasks", "/Delete /TN \"SZOptimizerAuto\" /F") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000); } } catch { } }
                private void LoadUserSettings() { _isLoading = true; try { if (!File.Exists(_settingsFile)) return; var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_settingsFile)); if (d == null) return; ChkAffinity.IsChecked = d.GetValueOrDefault("1", "False") == "True"; ChkGameBoost.IsChecked = d.GetValueOrDefault("2", "True") == "True"; ChkSoftBrowsers.IsChecked = d.GetValueOrDefault("3", "True") == "True"; ChkCleanLogs.IsChecked = d.GetValueOrDefault("4", "True") == "True"; ChkTrayMode.IsChecked = d.GetValueOrDefault("5", "True") == "True"; ChkTimer.IsChecked = d.GetValueOrDefault("6", "False") == "True"; ChkNetwork.IsChecked = d.GetValueOrDefault("7", "False") == "True"; ChkInput.IsChecked = d.GetValueOrDefault("8", "False") == "True"; ChkSleepLaunchers.IsChecked = d.GetValueOrDefault("9", "True") == "True"; ChkAutoCrosshair.IsChecked = d.GetValueOrDefault("AutoCross", "True") == "True"; ChkHideOnRmb.IsChecked = d.GetValueOrDefault("Rmb", "True") == "True"; _hideOnRmb = ChkHideOnRmb.IsChecked == true; if (d.ContainsKey("Pcie")) ChkPcieAspm.IsChecked = d["Pcie"] == "True"; if (d.ContainsKey("GpuP")) ChkGpuPref.IsChecked = d["GpuP"] == "True"; if (d.ContainsKey("Park")) ChkCorePark.IsChecked = d["Park"] == "True"; if (d.ContainsKey("GMode")) ChkGameMode.IsChecked = d["GMode"] == "True"; if (d.ContainsKey("Trim")) CmbAutoTrim.SelectedIndex = int.Parse(d["Trim"]); if (d.ContainsKey("HkVk")) { _hotkeyVk = int.Parse(d["HkVk"]); _hotkeyMod = int.Parse(d.GetValueOrDefault("HkMod", "0")); _hotkeyName = d.GetValueOrDefault("HkName", "Insert"); BtnBindHotkey.Content = $"Клавиша: {_hotkeyName}"; BtnToggleOverlay.Content = _overlayActive ? $"🛑 ВЫКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})" : $"🎯 ВКЛЮЧИТЬ ПРИЦЕЛ ({_hotkeyName})"; } if (d.ContainsKey("Preset")) { _currentPreset = d["Preset"]; HighlightSelectedPreset(); } if (d.ContainsKey("Scale") && double.TryParse(d["Scale"].Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double sc) && sc >= 50 && sc <= 300) ScaleSlider.Value = sc; if (d.ContainsKey("OffX")) { _offsetX = int.Parse(d["OffX"]); OffsetXText.Text = _offsetX.ToString(); } if (d.ContainsKey("OffY")) { _offsetY = int.Parse(d["OffY"]); OffsetYText.Text = _offsetY.ToString(); } if (d.ContainsKey("Color")) { _crossColor = (Color)ColorConverter.ConvertFromString(d["Color"]); ColorPreviewBox.Background = new SolidColorBrush(_crossColor); HexColorBox.Text = d["Color"]; RefreshGridColors(); } if (d.ContainsKey("Prof1") && !string.IsNullOrEmpty(d["Prof1"])) _profData.SetValue(d["Prof1"], 0);
if (d.ContainsKey("Prof2") && !string.IsNullOrEmpty(d["Prof2"])) _profData.SetValue(d["Prof2"], 1); if (d.ContainsKey("Prof3") && !string.IsNullOrEmpty(d["Prof3"])) _profData.SetValue(d["Prof3"], 2); if (d.ContainsKey("Prof4") && !string.IsNullOrEmpty(d["Prof4"])) _profData.SetValue(d["Prof4"], 3); if (d.ContainsKey("Prof5") && !string.IsNullOrEmpty(d["Prof5"])) _profData.SetValue(d["Prof5"], 4); if (d.ContainsKey("HolsterVk") && int.TryParse(d["HolsterVk"], out int hvk)) _holsterVk = hvk; if (d.ContainsKey("HolsterName") && BtnBindHolsterKey != null) BtnBindHolsterKey.Content = d["HolsterName"];
if (d.ContainsKey("ActProf") && int.TryParse(d["ActProf"], out int ap)) { _activeCrossProf = ap; UpdateProfButtonsUi(); }
if (d.ContainsKey("MonProfiles")) { var mp = d["MonProfiles"].Split(';'); for (int i = 0; i < mp.Length && i < _monitors.Count; i++) { var a = mp.ElementAt(i).Split('x', '@'); if (a.Length == 3 && int.TryParse(a.ElementAt(0), out int sw) && int.TryParse(a.ElementAt(1), out int sh) && int.TryParse(a.ElementAt(2), out int shz)) { _monitors.ElementAt(i).SavedW = sw; _monitors.ElementAt(i).SavedH = sh; _monitors.ElementAt(i).SavedHz = shz; } } SyncActiveMonitorFields(true); } if (d.ContainsKey("WeaponAuto") && ChkWeaponAutoSwitch != null) ChkWeaponAutoSwitch.IsChecked = d["WeaponAuto"] == "True"; if (d.ContainsKey("JvmRamChoice") && int.TryParse(d["JvmRamChoice"], out int jrc)) _jvmRamGb = jrc; if (d.ContainsKey("JvmProfile")) _jvmProfile = d["JvmProfile"]; if (d.ContainsKey("JvmPreTouch") && ChkJvmPreTouch != null) ChkJvmPreTouch.IsChecked = d["JvmPreTouch"] == "True"; if (d.ContainsKey("JvmStrDedup") && ChkJvmStrDedup != null) ChkJvmStrDedup.IsChecked = d["JvmStrDedup"] == "True"; UpdateJvmUi(); if (d.ContainsKey("AutoRes")) ChkAutoResGame.IsChecked = d["AutoRes"] == "True"; if (d.ContainsKey("ResW") && int.TryParse(d["ResW"], out int rw)) _targetResW = rw; if (d.ContainsKey("ResH") && int.TryParse(d["ResH"], out int rh)) _targetResH = rh; if (d.ContainsKey("ResHz") && int.TryParse(d["ResHz"], out int rhz)) _targetResHz = rhz; if (TxtCustomW != null) { TxtCustomW.Text = _maxMonW.ToString(); TxtCustomH.Text = _maxMonH.ToString(); TxtCustomHz.Text = _maxMonHz.ToString(); } UpdateResStatusUi(); if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); if (SavedCustomResText != null) SavedCustomResText.Text = $"Выбранный профиль для монитора {(_monitors.FindIndex(x => x.MaxW == _maxMonW && x.MaxH == _maxMonH) + 1)}: {_targetResW} × {_targetResH} @ {_targetResHz} Гц"; if (_targetResW <= 0 && _maxMonW > 0) { _targetResW = _maxMonW; _targetResH = _maxMonH; _targetResHz = _maxMonHz; if (TxtCustomW != null) TxtCustomW.Text = _targetResW.ToString(); if (TxtCustomH != null) TxtCustomH.Text = _targetResH.ToString(); } RebuildResolutionPresets(); if (d.ContainsKey("GameDir")) { _customGameDir = d["GameDir"]; RefreshServerRegionUi(); } if (d.ContainsKey("ProfVk")) { _profVk = int.Parse(d["ProfVk"]); _profMod = int.Parse(d.GetValueOrDefault("ProfMod", "0")); BtnBindProfHotkey.Content = d.GetValueOrDefault("ProfHkName", "Смена: Нет"); }
if (d.ContainsKey("CrossOn") && d["CrossOn"] == "True" && !_overlayActive) ToggleOverlay(); } catch { } finally { _isLoading = false; } }
        private void SaveUserSettings() { if (_isLoading || !IsInitialized) return; try { var d = new Dictionary<string, string> { {"1", (ChkAffinity.IsChecked == true).ToString()}, {"2", (ChkGameBoost.IsChecked == true).ToString()}, {"3", (ChkSoftBrowsers.IsChecked == true).ToString()}, {"4", (ChkCleanLogs.IsChecked == true).ToString()}, {"5", (ChkTrayMode.IsChecked == true).ToString()}, {"6", (ChkTimer.IsChecked == true).ToString()}, {"7", (ChkNetwork.IsChecked == true).ToString()}, {"8", (ChkInput.IsChecked == true).ToString()}, {"9", (ChkSleepLaunchers.IsChecked == true).ToString()}, {"AutoCross", (ChkAutoCrosshair.IsChecked == true).ToString()}, {"Rmb", (ChkHideOnRmb.IsChecked == true).ToString()}, {"Pcie", (ChkPcieAspm.IsChecked == true).ToString()}, {"GpuP", (ChkGpuPref.IsChecked == true).ToString()}, {"Park", (ChkCorePark.IsChecked == true).ToString()}, {"GMode", (ChkGameMode.IsChecked == true).ToString()}, {"Trim", CmbAutoTrim.SelectedIndex.ToString()}, {"CrossOn", _overlayActive.ToString()}, {"HkVk", _hotkeyVk.ToString()}, {"HkMod", _hotkeyMod.ToString()}, {"HkName", _hotkeyName}, {"Preset", _currentPreset}, {"Scale", ScaleSlider.Value.ToString(CultureInfo.InvariantCulture)}, {"OffX", _offsetX.ToString()}, {"OffY", _offsetY.ToString()}, {"Color", $"#{_crossColor.R:X2}{_crossColor.G:X2}{_crossColor.B:X2}"}, {"ActProf", _activeCrossProf.ToString()}, {"Prof1", _activeCrossProf == 1 ? PackCurrentProfile() : _profData.ElementAt(0)}, {"Prof2", _activeCrossProf == 2 ? PackCurrentProfile() : _profData.ElementAt(1)}, {"Prof3", _activeCrossProf == 3 ? PackCurrentProfile() : _profData.ElementAt(2)}, {"Prof4", _activeCrossProf == 4 ? PackCurrentProfile() : _profData.ElementAt(3)}, {"Prof5", _activeCrossProf == 5 ? PackCurrentProfile() : _profData.ElementAt(4)}, {"HolsterVk", _holsterVk.ToString()}, {"HolsterName", BtnBindHolsterKey?.Content?.ToString() ?? "Убрать: H (Р)"}, {"ProfVk", _profVk.ToString()}, {"ProfMod", _profMod.ToString()}, {"ProfHkName", BtnBindProfHotkey.Content?.ToString() ?? "Смена: Нет"}, {"GameDir", _customGameDir}, {"ResW", _targetResW.ToString()}, {"ResH", _targetResH.ToString()}, {"ResHz", _targetResHz.ToString()}, {"AutoRes", (ChkAutoResGame.IsChecked == true).ToString()}, {"MonProfiles", string.Join(";", _monitors.Select(x => $"{x.SavedW}x{x.SavedH}@{x.SavedHz}"))}, {"WeaponAuto", (ChkWeaponAutoSwitch.IsChecked == true).ToString()}, {"JvmRamGb", GetEffectiveJvmRamGb().ToString()}, {"JvmRamChoice", _jvmRamGb.ToString()}, {"JvmProfile", _jvmProfile}, {"JvmPreTouch", (ChkJvmPreTouch?.IsChecked == true).ToString()}, {"JvmStrDedup", (ChkJvmStrDedup?.IsChecked == true).ToString()} }; File.WriteAllText(_settingsFile, JsonSerializer.Serialize(d)); } catch { } }
        private void UpdateIndicators(bool active) { var off = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6D6F78")); var on = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA")); Ind1.Fill = (active && ChkAffinity.IsChecked == true) ? on : off; Ind2.Fill = (active && ChkGameBoost.IsChecked == true) ? on : off; Ind3.Fill = (active && ChkSoftBrowsers.IsChecked == true) ? on : off; Ind4.Fill = (active && ChkCleanLogs.IsChecked == true) ? on : off; Ind5.Fill = _watchActive ? on : off; Ind6.Fill = (active && ChkTimer.IsChecked == true) ? on : off; Ind7.Fill = (active && ChkNetwork.IsChecked == true) ? on : off; Ind8.Fill = (active && ChkInput.IsChecked == true) ? on : off; Ind9.Fill = (active && ChkSleepLaunchers.IsChecked == true) ? on : off; }
        private void ApplySelectedTweaks(Process[] gameProcs) { if (ChkAffinity.IsChecked == true && Environment.ProcessorCount > 4) { long mask = ((1L << Environment.ProcessorCount) - 1) & ~1L; foreach (var p in gameProcs) try { p.ProcessorAffinity = (IntPtr)mask; } catch { } } if (ChkGameBoost.IsChecked == true) { foreach (var p in gameProcs) try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { } } foreach (var s in new[] { "WSearch", "DiagTrack" }) try { using var sc = new ServiceController(s); if (sc.Status == ServiceControllerStatus.Running) sc.Stop(); } catch { } if (ChkSoftBrowsers.IsChecked == true) { var targets = new[] { "chrome", "msedge", "opera", "yandex", "firefox" }; foreach (var p in Process.GetProcesses()) { try { if (targets.Contains(p.ProcessName.ToLower()) && p.MainWindowHandle == IntPtr.Zero) p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { } finally { p.Dispose(); } } } if (ChkSleepLaunchers.IsChecked == true) { var launchers = new[] { "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "eadesktop", "eabackgroundservice", "riotclientservices", "overwolf", "overwolfbrowser" }; foreach (var p in Process.GetProcesses()) { try { if (launchers.Contains(p.ProcessName.ToLower())) { p.PriorityClass = ProcessPriorityClass.Idle; IntPtr h = OpenProcess(0x0500, false, p.Id); if (h != IntPtr.Zero) { EmptyWorkingSet(h); CloseHandle(h); } } } catch { } finally { p.Dispose(); } } } if (ChkTimer.IsChecked == true && !_timerBoosted) { int trStatus = NtSetTimerResolution(10000, true, out uint curRes); if (trStatus != 0 || curRes > 15000) { NtSetTimerResolution(10000, false, out _); Dispatcher.BeginInvoke(() => { ChkTimer.IsChecked = false; SaveUserSettings(); MessageBox.Show("⚠️ Сжатие системного таймера (пункт 6) вызвало конфликт с текущей сборкой Windows или драйвером.\n\nФункция автоматически отключена, а галочка снята, чтобы избежать микрофризов и сбоев.", "Защита от конфликтов — SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning); }); } _timerBoosted = true; } _tweaksApplied = true; BackupStatusText.Text = "🛡️ Статус: Буст активен"; UpdateIndicators(true); }
        private async void BtnAutoWatch_Click(object sender, RoutedEventArgs e) { _watchActive = !_watchActive; if (!_watchActive) { BtnAutoWatch.Content = "▶ ВКЛЮЧИТЬ АВТО-ОЖИДАНИЕ"; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA")); RollbackAll(); UpdateIndicators(false); return; } BtnAutoWatch.Content = "⏸ ОЖИДАНИЕ ЗАПУСКА STALZONE..."; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")); UpdateIndicators(false); while (_watchActive) { if (_trackedGame != null) { try { if (_trackedGame.HasExited) { _trackedGame.Dispose(); _trackedGame = null; } } catch { _trackedGame = null; } } if (_trackedGame == null) { foreach (var p in Process.GetProcesses()) { try { string n = p.ProcessName.ToLower(); if (!n.Contains("szoptimizer") && (n == "stalzonew" || n == "stalzone" || n == "stalcraft" || n == "stalcraftw" || n.Contains("stalcraft") || n == "javaw")) { _trackedGame = p; break; } else p.Dispose(); } catch { } } if (_trackedGame != null) { if (!_tweaksApplied) { ApplySelectedTweaks(new[] { _trackedGame }); if (ChkAutoCrosshair.IsChecked == true && !_overlayActive) ToggleOverlay(); Dispatcher.BeginInvoke(() => { if (ChkAutoResGame.IsChecked == true && _targetResW > 0 && _targetResH > 0 && SetDisplayResolution(_targetResW, _targetResH, _targetResHz, true)) _resAppliedByGame = true; }); BtnAutoWatch.Content = "⚡ ИГРА АКТИВНА — БУСТ ВКЛЮЧЁН"; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#51CF66")); } } else { if (_tweaksApplied) { RollbackAll(); if (ChkAutoCrosshair.IsChecked == true && _overlayActive) ToggleOverlay(); if (_resAppliedByGame) { SetDisplayResolution(_nativeW, _nativeH, _nativeHz, true); _resAppliedByGame = false; } RestoreFromTray(); BtnAutoWatch.Content = "⏸ ОЖИДАНИЕ ЗАПУСКА STALZONE..."; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")); } } } await Task.Delay(3000); } }
        private void BtnApplyNow_Click(object sender, RoutedEventArgs e) { SaveUserSettings(); var list = new List<Process>(); foreach (var p in Process.GetProcesses()) { try { string n = p.ProcessName.ToLower(); if (!n.Contains("szoptimizer") && (n == "stalzonew" || n == "stalzone" || n == "stalcraft" || n == "stalcraftw" || n.Contains("stalcraft") || n == "javaw")) list.Add(p); else p.Dispose(); } catch { p.Dispose(); } } ApplySelectedTweaks(list.ToArray()); foreach (var gp in list) gp.Dispose(); MessageBox.Show("Безопасные оптимизации применены!", "SZ Optimizer v1.5"); }
        private void RollbackAll() { if (_timerBoosted) { NtSetTimerResolution(156250, false, out _); _timerBoosted = false; } foreach (var s in new[] { "WSearch", "DiagTrack" }) try { using var sc = new ServiceController(s); if (sc.Status == ServiceControllerStatus.Stopped) sc.Start(); } catch { } _tweaksApplied = false; BackupStatusText.Text = "🛡️ Статус: Ожидание игры"; UpdateIndicators(false); }
        private void BtnRollback_Click(object sender, RoutedEventArgs e) { _watchActive = false; RollbackAll(); if (_overlayActive) ToggleOverlay(); BtnAutoWatch.Content = "👁️ ВКЛЮЧИТЬ АВТО-ОЖИДАНИЕ"; BtnAutoWatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA")); MessageBox.Show("Настройки возвращены!", "SZ Optimizer v1.5"); }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) { SaveUserSettings(); if (!_forceExit && ChkMinimizeOnClose?.IsChecked == true) { e.Cancel = true; HideToTray(); return; } Shell_NotifyIcon(2, ref _nid); SaveUserSettings(); _trackedGame?.Dispose(); if (_tweaksApplied) RollbackAll(); _overlay?.Dispose(); 
    
    }

                private bool IsJvmWrapperInstalled() { try { foreach (var t in JvmTargets) { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{t}"); string? dbg = k?.GetValue("Debugger") as string; if (!string.IsNullOrEmpty(dbg) && dbg.Contains("service.exe", StringComparison.OrdinalIgnoreCase)) return true; } } catch { } return false; }
        private void ReassertJvmWrapper() { try { string dir = GetOfficialWrapperDir(), cli = Path.Combine(dir, "cli.exe"); if (File.Exists(cli) && File.Exists(Path.Combine(dir, "service.exe"))) { Process.Start(new ProcessStartInfo(cli, "install") { WorkingDirectory = dir, CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000); Dispatcher.BeginInvoke(UpdateJvmUi); } } catch { } }
}




}
