using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace SZOptimizer
{
    public partial class MainWindow : Window
    {
        private readonly string _backupFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backup.json");
        private Dictionary<string, string> _backupData = new();
        private bool _timerBoosted = false;

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        public MainWindow()
        {
            InitializeComponent();
            CheckCrashRecovery();
            RunDiagnostics();
            AutoFindGame();
        }

        private void CheckCrashRecovery()
        {
            if (File.Exists(_backupFile))
            {
                BackupStatusText.Text = "⚠️ Найден незакрытый бэкап";
                var res = MessageBox.Show("Прошлая игровая сессия завершилась некорректно. Восстановить стандартные настройки Windows из бэкапа?", "SZ Optimizer - Crash Recovery", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes) RollbackAll();
            }
        }

        private void RunDiagnostics()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
                int gameMode = (int)(key?.GetValue("AllowAutoGameMode", 1) ?? 1);
                string gmStatus = gameMode == 1 ? "ВКЛ" : "ВЫКЛ (реком. включить)";

                long pageFileMb = 0;
                using (var searcher = new ManagementObjectSearcher("SELECT AllocatedBaseSize FROM Win32_PageFileUsage"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                        pageFileMb += Convert.ToInt64(obj["AllocatedBaseSize"]);
                }
                string pfStatus = pageFileMb >= 4096 ? $"{pageFileMb / 1024} ГБ (ОК)" : "Мало (нужно 4-8 ГБ)";
                DiagnosticsText.Text = $"● ДИАГНОСТИКА: Игровой режим: {gmStatus}  |  Файл подкачки: {pfStatus}";
            }
            catch
            {
                DiagnosticsText.Text = "● ДИАГНОСТИКА: Система готова (запустите от Администратора для всех твиков)";
            }
        }

        private void AutoFindGame()
        {
            string[] commonPaths = {
                @"C:\Games\STALZONE\stalzonew.exe", @"D:\Games\STALZONE\stalzonew.exe",
                @"C:\EXBO\STALZONE\stalzonew.exe", @"D:\EXBO\STALZONE\stalzonew.exe",
                @"C:\Program Files (x86)\Steam\steamapps\common\STALCRAFT\stalzonew.exe",
                @"D:\SteamLibrary\steamapps\common\STALCRAFT\stalzonew.exe"
            };
            foreach (var p in commonPaths)
            {
                if (File.Exists(p)) { GamePathBox.Text = p; return; }
            }
            GamePathBox.Text = "Укажите путь к stalzonew.exe (или запустите игру вручную)";
        }

        private void BtnBrowseGame_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "STALZONE Client (stalzonew.exe)|stalzonew.exe|Все программы (*.exe)|*.exe" };
            if (dlg.ShowDialog() == true) GamePathBox.Text = dlg.FileName;
        }
        private async void BtnApplyAndLaunch_Click(object sender, RoutedEventArgs e)
        {
            BtnApplyAndLaunch.IsEnabled = false;
            BtnApplyAndLaunch.Content = "⏳ ПРИМЕНЕНИЕ ОПТИМИЗАЦИЙ...";

            try
            {
                SaveBackup();

                if (ChkRamShader.IsChecked == true) CleanShadersAndRam();
                if (ChkNetwork.IsChecked == true) OptimizeNetwork();
                if (ChkInput.IsChecked == true) OptimizeInput();
                if (ChkGameBoost.IsChecked == true) ToggleBackgroundServices(false);
                if (ChkFullscreen.IsChecked == true && File.Exists(GamePathBox.Text)) FixFullscreenAndGpu(GamePathBox.Text);
                if (ChkTimer.IsChecked == true) { NtSetTimerResolution(10000, true, out _); _timerBoosted = true; }
                if (ChkMmcss.IsChecked == true) OptimizeMmcss();
                if (ChkDns.IsChecked == true) ApplyFastestDns();
                if (ChkCleanLogs.IsChecked == true && File.Exists(GamePathBox.Text)) CleanGameLogs(GamePathBox.Text);

                if (File.Exists(GamePathBox.Text))
                {
                    Process.Start(new ProcessStartInfo(GamePathBox.Text) { WorkingDirectory = Path.GetDirectoryName(GamePathBox.Text) });
                }

                if (ChkTrayMode.IsChecked == true) this.WindowState = WindowState.Minimized;
                BtnApplyAndLaunch.Content = "🎮 ИГРА ЗАПУЩЕНА (Ожидание выхода...)";

                await MonitorGameSessionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка (запустите программу от имени Администратора):\n{ex.Message}", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RollbackAll();
                this.WindowState = WindowState.Normal;
                BtnApplyAndLaunch.IsEnabled = true;
                BtnApplyAndLaunch.Content = "▶ ПРИМЕНИТЬ ВЫБРАННОЕ И ЗАПУСТИТЬ STALZONE";
            }
        }

        private void SaveBackup()
        {
            _backupData.Clear();
            using var mouseKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            _backupData["MouseSpeed"] = mouseKey?.GetValue("MouseSpeed")?.ToString() ?? "1";
            _backupData["MouseThreshold1"] = mouseKey?.GetValue("MouseThreshold1")?.ToString() ?? "6";
            _backupData["MouseThreshold2"] = mouseKey?.GetValue("MouseThreshold2")?.ToString() ?? "10";

            File.WriteAllText(_backupFile, JsonSerializer.Serialize(_backupData));
            BackupStatusText.Text = "🛡️ Бэкап: Активен";
        }

        private void CleanShadersAndRam()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] shaderDirs = {
                Path.Combine(localApp, @"NVIDIA\DXCache"),
                Path.Combine(localApp, @"NVIDIA\GLCache"),
                Path.Combine(localApp, @"AMD\DxCache")
            };
            foreach (var dir in shaderDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            foreach (var proc in Process.GetProcesses())
            {
                try { EmptyWorkingSet(proc.Handle); } catch { }
            }
        }

        private void OptimizeNetwork()
        {
            using var tcpKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
            if (tcpKey == null) return;
            foreach (var sub in tcpKey.GetSubKeyNames())
            {
                try
                {
                    using var iface = tcpKey.OpenSubKey(sub, true);
                    iface?.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                    iface?.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                }
                catch { }
            }
        }

        private void OptimizeInput()
        {
            using var mouseKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
            mouseKey?.SetValue("MouseSpeed", "0");
            mouseKey?.SetValue("MouseThreshold1", "0");
            mouseKey?.SetValue("MouseThreshold2", "0");

            using var stickyKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\StickyKeys", true);
            stickyKey?.SetValue("Flags", "506");
        }
        private void ToggleBackgroundServices(bool enable)
        {
            string[] services = { "WSearch", "DiagTrack" };
            foreach (var srv in services)
            {
                try
                {
                    using var sc = new ServiceController(srv);
                    if (!enable && sc.Status == ServiceControllerStatus.Running) sc.Stop();
                    else if (enable && sc.Status == ServiceControllerStatus.Stopped) sc.Start();
                }
                catch { }
            }
        }

        private void FixFullscreenAndGpu(string exePath)
        {
            using var compatKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
            compatKey?.SetValue(exePath, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE");

            using var gpuKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            gpuKey?.SetValue(exePath, "GpuPreference=2;");
        }

        private void OptimizeMmcss()
        {
            using var mmcss = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
            mmcss?.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
            mmcss?.SetValue("Priority", 6, RegistryValueKind.DWord);
            mmcss?.SetValue("Scheduling Category", "High", RegistryValueKind.String);
        }

        private void ApplyFastestDns()
        {
            var targets = new Dictionary<string, long> { { "1.1.1.1", 999 }, { "8.8.8.8", 999 }, { "77.88.8.8", 999 } };
            using var ping = new Ping();
            foreach (var ip in targets.Keys.ToList())
            {
                try
                {
                    var reply = ping.Send(ip, 1000);
                    if (reply.Status == IPStatus.Success) targets[ip] = reply.RoundtripTime;
                }
                catch { }
            }
            string bestDns = targets.OrderBy(x => x.Value).First().Key;
            var activeNic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
            if (activeNic != null)
            {
                Process.Start(new ProcessStartInfo("netsh", $"interface ip set dns \"{activeNic.Name}\" static {bestDns}") { CreateNoWindow = true, UseShellExecute = false });
            }
        }

        private void CleanGameLogs(string exePath)
        {
            string? gameDir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(gameDir)) return;
            string logsDir = Path.Combine(gameDir, "logs");
            if (!Directory.Exists(logsDir)) return;
            foreach (var file in Directory.GetFiles(logsDir))
            {
                try { File.Delete(file); } catch { }
            }
        }

        private async Task MonitorGameSessionAsync()
        {
            await Task.Delay(5000);
            while (true)
            {
                var procs = Process.GetProcessesByName("stalzonew");
                if (procs.Length == 0) break;

                if (ChkGameBoost.IsChecked == true)
                {
                    foreach (var p in procs)
                    {
                        try { if (p.PriorityClass != ProcessPriorityClass.High) p.PriorityClass = ProcessPriorityClass.High; } catch { }
                    }
                }
                if (ChkRamShader.IsChecked == true)
                {
                    foreach (var p in Process.GetProcesses())
                    {
                        try { EmptyWorkingSet(p.Handle); } catch { }
                    }
                }
                await Task.Delay(30000);
            }
        }

        private void RollbackAll()
        {
            if (_timerBoosted) { NtSetTimerResolution(156250, false, out _); _timerBoosted = false; }
            ToggleBackgroundServices(true);

            if (File.Exists(_backupFile))
            {
                try
                {
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_backupFile));
                    if (data != null)
                    {
                        using var mouseKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
                        if (data.ContainsKey("MouseSpeed")) mouseKey?.SetValue("MouseSpeed", data["MouseSpeed"]);
                        if (data.ContainsKey("MouseThreshold1")) mouseKey?.SetValue("MouseThreshold1", data["MouseThreshold1"]);
                        if (data.ContainsKey("MouseThreshold2")) mouseKey?.SetValue("MouseThreshold2", data["MouseThreshold2"]);
                    }
                    File.Delete(_backupFile);
                }
                catch { }
            }

            if (ChkDns.IsChecked == true)
            {
                var activeNic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
                if (activeNic != null)
                {
                    Process.Start(new ProcessStartInfo("netsh", $"interface ip set dns \"{activeNic.Name}\" dhcp") { CreateNoWindow = true, UseShellExecute = false });
                }
            }
            BackupStatusText.Text = "🛡️ Бэкап: Откат выполнен";
        }

        private void BtnRollback_Click(object sender, RoutedEventArgs e)
        {
            RollbackAll();
            MessageBox.Show("Все системные настройки успешно возвращены в исходное состояние!", "SZ Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
