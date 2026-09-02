using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoUpdaterDotNET;

namespace ElitLauncher
{
    public class AppSettingsModel
    {
        public string AmongUsPath { get; set; } = string.Empty;
        public int ConnectionTimeoutSeconds { get; set; } = 30;
    }

    public class GitHubReleasePackage
    {
        public string TagName { get; set; } = string.Empty;
        public string ReleaseName { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public bool IsMiraVersion { get; set; } = false;
    }

    public enum ModType
    {
        TOU,
        Mira,
        Vanilla
    }

    public partial class MainWindow : Window
    {
        private AppSettingsModel _applicationSettings;
        private readonly List<GitHubReleasePackage> _availableReleasesCache;
        private static readonly HttpClient _sharedHttpClient = new HttpClient();

        private const string TouRepoApi = "https://api.github.com/repos/eDonnes124/Town-Of-Us-R/releases";
        private const string TouMiraRepoApi = "https://api.github.com/repos/AU-Avengers/TOU-Mira/releases";

        private const string AUnlockerUrl = "https://github.com/astra1dev/AUnlocker/releases/download/v1.3.1/AUnlocker_v1.3.1.dll";
        private const string MiraStatsExporterUrl = "https://github.com/boratsc/Mira-Stats-Exporter/releases/download/1.0.4/MiraStatsExporter.dll";
        private const string AleLuduModUrl = "https://github.com/townofus-pl/AleLuduMod/releases/download/v1.1.3/AleLuduMod.dll";

        private bool _isTaskCurrentlyExecuting;
        private ModType _selectedModType = ModType.TOU;

        public MainWindow()
        {
            InitializeComponent();

            _applicationSettings = new AppSettingsModel();
            _availableReleasesCache = new List<GitHubReleasePackage>();
            _isTaskCurrentlyExecuting = false;

            InitializeLauncherSubsystem();
        }

        private void CheckForApplicationUpdates()
        {
            try
            {
                AppendDiagnosticsLog("Sprawdzanie dostępności aktualizacji Launchera...");

                AutoUpdater.CheckForUpdateEvent -= OnAutoUpdaterCheckForUpdate;
                AutoUpdater.CheckForUpdateEvent += OnAutoUpdaterCheckForUpdate;

                AutoUpdater.Start("https://raw.githubusercontent.com/ojandooo/elit-launcher/refs/heads/main/update.xml");
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd sprawdzania aktualizacji: {ex.Message}");
            }
        }

        private void OnAutoUpdaterCheckForUpdate(UpdateInfoEventArgs args)
        {
            if (args.Error != null)
            {
                AppendDiagnosticsLog($"Błąd pobierania update.xml: {args.Error.Message}");
                return;
            }

            if (args.IsUpdateAvailable)
            {
                AppendDiagnosticsLog($"Wykryto nową wersję Launchera: {args.CurrentVersion}");
                AutoUpdater.ShowUpdateForm(args);
            }
            else
            {
                AppendDiagnosticsLog("Launcher jest w najnowszej wersji.");
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        private void InitializeLauncherSubsystem()
        {
            try
            {
                AppendDiagnosticsLog("Inicjalizacja Elit Launcher...");
                
                CheckForApplicationUpdates();

                SetupHttpClientConfiguration();
                LoadSettingsFromDisk();
                PerformGameExecutableScan();

                _ = Task.Run(async () => await FetchGitHubReleasesAsync());

                AppendDiagnosticsLog("Inicjalizacja zakończona sukcesem.");
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd inicjalizacji: {ex.Message}");
            }
        }

        private void SetupHttpClientConfiguration()
        {
            try
            {
                if (!_sharedHttpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    _sharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "Elit-Launcher-Client");
                }
                _sharedHttpClient.Timeout = TimeSpan.FromSeconds(_applicationSettings.ConnectionTimeoutSeconds);
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd konfiguracji sieci: {ex.Message}");
            }
        }

        private void AppendDiagnosticsLog(string message)
        {
            try
            {
                string formattedLogEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";

                if (Dispatcher.CheckAccess())
                {
                    TxtConsole.AppendText(formattedLogEntry);
                    TxtConsole.ScrollToEnd();
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtConsole.AppendText(formattedLogEntry);
                        TxtConsole.ScrollToEnd();
                    });
                }
            }
            catch
            {
            }
        }

        private void LoadSettingsFromDisk()
        {
            try
            {
                string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "elit_launcher_config.json");
                if (File.Exists(configFilePath))
                {
                    string jsonPayload = File.ReadAllText(configFilePath, Encoding.UTF8);
                    using (JsonDocument jsonDoc = JsonDocument.Parse(jsonPayload))
                    {
                        JsonElement rootElement = jsonDoc.RootElement;
                        if (rootElement.TryGetProperty("AmongUsPath", out JsonElement pathElement))
                        {
                            _applicationSettings.AmongUsPath = pathElement.GetString() ?? string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd odczytu ustawień: {ex.Message}");
            }
        }

        private void PerformGameExecutableScan()
        {
            try
            {
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                string[] searchDirectories = new[]
                {
                    Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Among Us - previous-public", "Among Us.exe"),
                    @"C:\Program Files (x86)\Steam\steamapps\common\Among Us - previous-public\Among Us.exe",
                    @"C:\Program Files\Steam\steamapps\common\Among Us - previous-public\Among Us.exe",
                    Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Among Us", "Among Us.exe"),
                    @"C:\Program Files (x86)\Steam\steamapps\common\Among Us\Among Us.exe",
                    @"C:\Program Files\Steam\steamapps\common\Among Us\Among Us.exe",
                    Path.Combine(localAppData, "Programs", "Among Us", "Among Us.exe"),
                    Path.Combine(programFilesX86, "Epic Games", "AmongUs", "Among Us.exe")
                };

                foreach (string candidatePath in searchDirectories)
                {
                    if (File.Exists(candidatePath))
                    {
                        _applicationSettings.AmongUsPath = candidatePath;
                        AppendDiagnosticsLog($"Wykryto Among Us: {candidatePath}");
                        return;
                    }
                }

                AppendDiagnosticsLog("Brak pliku Among Us.exe na komputerze.");
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Skanowanie gry: {ex.Message}");
            }
        }

        private void PromptSteamPreviousPublicInstructions()
        {
            MessageBoxResult result = MessageBox.Show(
                "Mody wymagają starszej wersji gry ze Steama ('previous-public').\n\n" +
                "KROKI PROSTEJ ZMIANY W STEAMIE:\n" +
                "1. Otwórz klienta Steam.\n" +
                "2. Kliknij PPM na 'Among Us' -> 'Właściwości'.\n" +
                "3. Wejdź w zakładkę 'Bety' (Betas).\n" +
                "4. Rozwiń listę i wybierz wersję 'previous-public'.\n" +
                "5. Poczekaj, aż Steam pobierze aktualizację.\n\n" +
                "Czy chcesz otworzyć Steam teraz?",
                "Wymagana zmiana wersji w Steam",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                SwitchSteamToPreviousPublic();
            }
        }

        private void SwitchSteamToPreviousPublic()
        {
            try
            {
                // Otwiera zakładkę właściwości Among Us bezpośrednio w Steam (AppID: 945360)
                Process.Start(new ProcessStartInfo
                {
                    FileName = "steam://url/SteamIDPage/945360",
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "steam://nav/games",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    AppendDiagnosticsLog($"Błąd otwierania Steam: {ex.Message}");
                }
            }
        }

        private async Task FetchGitHubReleasesAsync()
        {
            if (_selectedModType == ModType.Vanilla) return;

            try
            {
                string endpoint = _selectedModType == ModType.Mira ? TouMiraRepoApi : TouRepoApi;
                AppendDiagnosticsLog($"Pobieranie wydań GitHub ({_selectedModType})...");

                string responseString = await _sharedHttpClient.GetStringAsync(endpoint);

                using (JsonDocument document = JsonDocument.Parse(responseString))
                {
                    JsonElement rootArray = document.RootElement;

                    Dispatcher.Invoke(() =>
                    {
                        CbVersions.Items.Clear();
                        _availableReleasesCache.Clear();
                    });

                    foreach (JsonElement releaseNode in rootArray.EnumerateArray())
                    {
                        string tagName = releaseNode.GetProperty("tag_name").GetString() ?? string.Empty;
                        string name = releaseNode.GetProperty("name").GetString() ?? tagName;
                        string downloadFileUrl = string.Empty;

                        if (releaseNode.TryGetProperty("assets", out JsonElement assetsArray))
                        {
                            List<string> zipUrls = new List<string>();

                            foreach (JsonElement assetItem in assetsArray.EnumerateArray())
                            {
                                string assetName = assetItem.GetProperty("name").GetString()?.ToLower() ?? string.Empty;
                                string browserDownloadUrl = assetItem.GetProperty("browser_download_url").GetString() ?? string.Empty;

                                if (assetName.EndsWith(".zip"))
                                {
                                    if (_selectedModType == ModType.Mira && (assetName.Contains("steam") || assetName.Contains("x86")))
                                    {
                                        downloadFileUrl = browserDownloadUrl;
                                        break;
                                    }
                                    zipUrls.Add(browserDownloadUrl);
                                }
                            }

                            if (string.IsNullOrEmpty(downloadFileUrl) && zipUrls.Count > 0)
                            {
                                downloadFileUrl = zipUrls[0];
                            }
                        }

                        if (!string.IsNullOrEmpty(downloadFileUrl))
                        {
                            var releasePackage = new GitHubReleasePackage
                            {
                                TagName = tagName,
                                ReleaseName = name,
                                DownloadUrl = downloadFileUrl,
                                IsMiraVersion = (_selectedModType == ModType.Mira)
                            };

                            _availableReleasesCache.Add(releasePackage);

                            Dispatcher.Invoke(() =>
                            {
                                CbVersions.Items.Add(name);
                            });
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        if (CbVersions.Items.Count > 0)
                        {
                            CbVersions.SelectedIndex = 0;
                        }
                        else
                        {
                            CbVersions.Items.Add("Brak wydań");
                        }
                        VerifyInstallationStatus();
                    });
                }
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd pobierania z API: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    CbVersions.Items.Add("Błąd / Offline");
                    CbVersions.SelectedIndex = 0;
                    VerifyInstallationStatus();
                });
            }
        }

        private void VerifyInstallationStatus()
        {
            try
            {
                if (_selectedModType == ModType.Vanilla)
                {
                    BtnMainLaunch.Content = "🚀 Uruchom Czystą Grę (Vanilla)";
                    BtnMainLaunch.IsEnabled = !string.IsNullOrEmpty(_applicationSettings.AmongUsPath) && File.Exists(_applicationSettings.AmongUsPath);
                    return;
                }

                if (string.IsNullOrEmpty(_applicationSettings.AmongUsPath))
                {
                    TxtModStatus.Text = "Brak zainstalowanej gry Among Us na Steam!";
                    TxtModStatus.Foreground = Brushes.OrangeRed;
                    BtnDownload.IsEnabled = false;
                    BtnMainLaunch.IsEnabled = false;
                    return;
                }

                string sourceDirectory = GetSteamSourceDirectory();
                string parentFolder = Directory.GetParent(sourceDirectory)?.FullName ?? sourceDirectory;
                string targetFolder = _selectedModType == ModType.Mira ? "Among Us TOUMira" : "Among Us TOU";
                string targetPath = Path.Combine(parentFolder, targetFolder);

                string selectedVersion = CbVersions.SelectedItem?.ToString() ?? "";
                bool isInstalled = Directory.Exists(targetPath) && File.Exists(Path.Combine(targetPath, "Among Us.exe"));

                if (isInstalled)
                {
                    TxtModStatus.Text = $"Zainstalowano [{targetFolder}] ({selectedVersion})";
                    TxtModStatus.Foreground = (Brush)FindResource("SuccessColor");

                    BtnDownload.IsEnabled = false;
                    BtnDownload.Content = "Wersja jest już zainstalowana";
                    
                    BtnMainLaunch.IsEnabled = true;
                    BtnMainLaunch.Content = $"🚀 Uruchom zmodowaną grę ({targetFolder})";
                }
                else
                {
                    TxtModStatus.Text = "Brak modyfikacji na dysku";
                    TxtModStatus.Foreground = (Brush)FindResource("AccentColor");

                    BtnDownload.IsEnabled = true;
                    BtnDownload.Content = "Pobierz i Zainstaluj Wybraną Modyfikację";

                    BtnMainLaunch.IsEnabled = false;
                    BtnMainLaunch.Content = "Zainstaluj moda, aby móc go uruchomić";
                }
            }
            catch
            {
                TxtModStatus.Text = "Błąd podczas weryfikacji";
            }
        }

        private string GetSteamSourceDirectory()
        {
            if (string.IsNullOrEmpty(_applicationSettings.AmongUsPath)) return string.Empty;

            string currentDir = Path.GetDirectoryName(_applicationSettings.AmongUsPath) ?? string.Empty;
            string parentDir = Directory.GetParent(currentDir)?.FullName ?? currentDir;

            string previousPublicFolder = Path.Combine(parentDir, "Among Us - previous-public");
            if (Directory.Exists(previousPublicFolder) && File.Exists(Path.Combine(previousPublicFolder, "Among Us.exe")))
            {
                return previousPublicFolder;
            }

            return currentDir;
        }

        private void RefreshDllStatuses()
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                SetDllStatusUI(TxtStatusAUnlocker, BtnToggleAUnlocker, BtnDeleteAUnlocker, "Brak modyfikacji");
                SetDllStatusUI(TxtStatusMiraStats, BtnToggleMiraStats, BtnDeleteMiraStats, "Brak modyfikacji");
                SetDllStatusUI(TxtStatusAleLudu, BtnToggleAleLudu, BtnDeleteAleLudu, "Brak modyfikacji");
                return;
            }

            string pluginsDir = Path.Combine(targetDir, "BepInEx", "plugins");

            UpdateSingleDllStatusUI(pluginsDir, "AUnlocker.dll", TxtStatusAUnlocker, BtnToggleAUnlocker, BtnDeleteAUnlocker);
            UpdateSingleDllStatusUI(pluginsDir, "MiraStatsExporter.dll", TxtStatusMiraStats, BtnToggleMiraStats, BtnDeleteMiraStats);
            UpdateSingleDllStatusUI(pluginsDir, "AleLuduMod.dll", TxtStatusAleLudu, BtnToggleAleLudu, BtnDeleteAleLudu);
        }

        private void UpdateSingleDllStatusUI(string pluginsDir, string dllName, TextBlock txtStatus, Button btnToggle, Button btnDelete)
        {
            string dllPath = Path.Combine(pluginsDir, dllName);
            string disabledPath = Path.Combine(pluginsDir, dllName + ".disabled");

            if (File.Exists(dllPath))
            {
                txtStatus.Text = "Włączona";
                txtStatus.Foreground = (Brush)FindResource("SuccessColor");
                btnToggle.Content = "Wyłącz";
                btnToggle.IsEnabled = true;
                btnDelete.IsEnabled = true;
            }
            else if (File.Exists(disabledPath))
            {
                txtStatus.Text = "Wyłączona";
                txtStatus.Foreground = Brushes.Orange;
                btnToggle.Content = "Włącz";
                btnToggle.IsEnabled = true;
                btnDelete.IsEnabled = true;
            }
            else
            {
                txtStatus.Text = "Niezainstalowana";
                txtStatus.Foreground = (Brush)FindResource("AccentColor");
                btnToggle.Content = "Wyłącz";
                btnToggle.IsEnabled = false;
                btnDelete.IsEnabled = false;
            }
        }

        private void SetDllStatusUI(TextBlock txtStatus, Button btnToggle, Button btnDelete, string statusText)
        {
            txtStatus.Text = statusText;
            txtStatus.Foreground = (Brush)FindResource("TextMuted");
            btnToggle.IsEnabled = false;
            btnDelete.IsEnabled = false;
        }

        private void CbVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            VerifyInstallationStatus();
        }

        private void BtnSelectTOU_Click(object sender, RoutedEventArgs e)
        {
            _selectedModType = ModType.TOU;
            UpdateTypeButtonsUI();
        }

        private void BtnSelectMira_Click(object sender, RoutedEventArgs e)
        {
            _selectedModType = ModType.Mira;
            UpdateTypeButtonsUI();
        }

        private void BtnSelectVanilla_Click(object sender, RoutedEventArgs e)
        {
            _selectedModType = ModType.Vanilla;
            UpdateTypeButtonsUI();
        }

        private void UpdateTypeButtonsUI()
        {
            BtnSelectTOU.Style = (_selectedModType == ModType.TOU) ? (Style)FindResource("ModernPrimaryButton") : (Style)FindResource("ModernSecondaryButton");
            BtnSelectMira.Style = (_selectedModType == ModType.Mira) ? (Style)FindResource("ModernPrimaryButton") : (Style)FindResource("ModernSecondaryButton");
            BtnSelectVanilla.Style = (_selectedModType == ModType.Vanilla) ? (Style)FindResource("ModernPrimaryButton") : (Style)FindResource("ModernSecondaryButton");

            if (_selectedModType == ModType.Vanilla)
            {
                PanelModControls.Visibility = Visibility.Collapsed;
                PanelBottomTools.Visibility = Visibility.Collapsed;
                PanelVanillaInfo.Visibility = Visibility.Visible;
            }
            else
            {
                PanelModControls.Visibility = Visibility.Visible;
                PanelBottomTools.Visibility = Visibility.Visible;
                PanelVanillaInfo.Visibility = Visibility.Collapsed;
                _ = Task.Run(async () => await FetchGitHubReleasesAsync());
            }

            VerifyInstallationStatus();
        }

        private void CbThemes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbThemes == null || CbThemes.SelectedIndex < 0) return;

            switch (CbThemes.SelectedIndex)
            {
                case 0: ApplyColorTheme("#000000", "#050505", "#0D0D0D", "#171717", "#E11D48", "#F43F5E"); break;
                case 1: ApplyColorTheme("#090514", "#0F0B1E", "#17122C", "#231B42", "#8B5CF6", "#A78BFA"); break;
                case 2: ApplyColorTheme("#02120D", "#051C15", "#0B2920", "#123B2F", "#10B981", "#34D399"); break;
                case 3: ApplyColorTheme("#030712", "#0B0F19", "#111827", "#1F2937", "#3B82F6", "#60A5FA"); break;
            }
        }

        private void UpdateResourceBrush(string key, string hexColor)
        {
            Color color = (Color)ColorConverter.ConvertFromString(hexColor);
            SolidColorBrush newBrush = new SolidColorBrush(color);
            newBrush.Freeze();

            if (Resources.Contains(key)) Resources.Remove(key);
            Resources.Add(key, newBrush);
        }

        private void ApplyColorTheme(string bg, string panel, string card, string hover, string accent, string accentHover)
        {
            UpdateResourceBrush("WindowBg", bg);
            UpdateResourceBrush("PanelBg", panel);
            UpdateResourceBrush("CardBg", card);
            UpdateResourceBrush("CardHover", hover);
            UpdateResourceBrush("AccentColor", accent);
            UpdateResourceBrush("AccentHover", accentHover);
        }

        private void NavPulpit_Click(object sender, RoutedEventArgs e)
        {
            TabPulpit.Visibility = Visibility.Visible;
            TabDll.Visibility = Visibility.Collapsed;
            TabLogi.Visibility = Visibility.Collapsed;
        }

        private void NavDll_Click(object sender, RoutedEventArgs e)
        {
            TabPulpit.Visibility = Visibility.Collapsed;
            TabDll.Visibility = Visibility.Visible;
            TabLogi.Visibility = Visibility.Collapsed;
            RefreshDllStatuses();
        }

        private void NavLogi_Click(object sender, RoutedEventArgs e)
        {
            TabPulpit.Visibility = Visibility.Collapsed;
            TabDll.Visibility = Visibility.Collapsed;
            TabLogi.Visibility = Visibility.Visible;
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_isTaskCurrentlyExecuting) return;

            int selectedIndex = CbVersions.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _availableReleasesCache.Count)
            {
                MessageBox.Show("Wybierz odpowiednią wersję.", "Ostrzeżenie", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string mainGameFolder = GetSteamSourceDirectory();
            if (string.IsNullOrEmpty(mainGameFolder) || !Directory.Exists(mainGameFolder))
            {
                PromptSteamPreviousPublicInstructions();
                return;
            }

            // Pytanie tylko o instalację AUnlocker.dll
            MessageBoxResult askAUnlocker = MessageBox.Show(
                "Czy zainstalować opcjonalną wtyczkę AUnlocker.dll (odblokowuje skórki/kosmetyki)?",
                "Pobieranie wtyczek",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            bool installAUnlocker = (askAUnlocker == MessageBoxResult.Yes);

            var targetPackage = _availableReleasesCache[selectedIndex];
            _isTaskCurrentlyExecuting = true;
            BtnDownload.IsEnabled = false;

            try
            {
                string parentFolder = Directory.GetParent(mainGameFolder)?.FullName ?? mainGameFolder;

                string targetFolderName = _selectedModType == ModType.Mira ? "Among Us TOUMira" : "Among Us TOU";
                string destinationDirectory = Path.Combine(parentFolder, targetFolderName);

                AppendDiagnosticsLog($"Kopiowanie czystej gry ze Steam z {mainGameFolder} do: {destinationDirectory}");

                await Task.Run(() =>
                {
                    if (!Directory.Exists(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);

                    foreach (string dirPath in Directory.GetDirectories(mainGameFolder, "*", SearchOption.AllDirectories))
                    {
                        string relativeDir = Path.GetRelativePath(mainGameFolder, dirPath);
                        string targetDir = Path.Combine(destinationDirectory, relativeDir);
                        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                    }

                    foreach (string filePath in Directory.GetFiles(mainGameFolder, "*.*", SearchOption.AllDirectories))
                    {
                        string relativeFile = Path.GetRelativePath(mainGameFolder, filePath);
                        string targetFile = Path.Combine(destinationDirectory, relativeFile);
                        File.Copy(filePath, targetFile, true);
                    }
                });

                ProgressBarDownload.Value = 30;
                TxtDownloadStatus.Text = "Pobieranie paczki moda z GitHub...";

                byte[] fileBytes = await _sharedHttpClient.GetByteArrayAsync(targetPackage.DownloadUrl);

                ProgressBarDownload.Value = 60;
                TxtDownloadStatus.Text = "Rozpakowywanie...";

                string temporaryZipPath = Path.Combine(Path.GetTempPath(), $"Elit_{Guid.NewGuid()}.zip");
                await File.WriteAllBytesAsync(temporaryZipPath, fileBytes);

                ProgressBarDownload.Value = 80;
                TxtDownloadStatus.Text = "Wdrażanie struktur modyfikacji...";

                await Task.Run(() =>
                {
                    using (ZipArchive archive = ZipFile.OpenRead(temporaryZipPath))
                    {
                        string rootPrefix = string.Empty;
                        var topFolders = archive.Entries
                            .Select(entry => entry.FullName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                            .Where(folder => !string.IsNullOrEmpty(folder))
                            .Distinct()
                            .ToList();

                        if (topFolders.Count == 1 && archive.Entries.All(entry => entry.FullName.StartsWith(topFolders[0] + "/") || entry.FullName.StartsWith(topFolders[0] + "\\") || entry.FullName == topFolders[0]))
                        {
                            rootPrefix = topFolders[0] + "/";
                        }

                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string entryPath = entry.FullName;

                            if (!string.IsNullOrEmpty(rootPrefix) && entryPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                entryPath = entryPath.Substring(rootPrefix.Length);
                            }

                            if (string.IsNullOrEmpty(entryPath)) continue;

                            string destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entryPath));
                            if (!destinationPath.StartsWith(Path.GetFullPath(destinationDirectory), StringComparison.OrdinalIgnoreCase)) continue;

                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(destinationPath);
                            }
                            else
                            {
                                string? parentDir = Path.GetDirectoryName(destinationPath);
                                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir)) Directory.CreateDirectory(parentDir);
                                entry.ExtractToFile(destinationPath, true);
                            }
                        }
                    }

                    File.WriteAllText(Path.Combine(destinationDirectory, "steam_appid.txt"), "945360");
                });

                if (File.Exists(temporaryZipPath)) File.Delete(temporaryZipPath);

                if (installAUnlocker)
                {
                    ProgressBarDownload.Value = 90;
                    TxtDownloadStatus.Text = "Pobieranie AUnlocker.dll...";
                    await DownloadSingleDllAsync(AUnlockerUrl, "AUnlocker.dll", destinationDirectory);
                }

                ProgressBarDownload.Value = 100;
                TxtDownloadStatus.Text = "Instalacja zakończona sukcesem!";
                AppendDiagnosticsLog($"Pomyślnie zainstalowano moda w {destinationDirectory}");
                VerifyInstallationStatus();
                MessageBox.Show("Modyfikacja została pomyślnie zainstalowana!", "Elit Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd instalacji: {ex.Message}");
                TxtDownloadStatus.Text = "Błąd instalacji!";
                ProgressBarDownload.Value = 0;
                MessageBox.Show($"Wystąpił błąd: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isTaskCurrentlyExecuting = false;
                VerifyInstallationStatus();
            }
        }

        private async Task<bool> DownloadSingleDllAsync(string fileUrl, string fileName, string targetDirectory)
        {
            try
            {
                string pluginsFolder = Path.Combine(targetDirectory, "BepInEx", "plugins");
                if (!Directory.Exists(pluginsFolder)) Directory.CreateDirectory(pluginsFolder);

                string destinationPath = Path.Combine(pluginsFolder, fileName);
                
                AppendDiagnosticsLog($"Pobieranie {fileName}...");

                byte[] data = await _sharedHttpClient.GetByteArrayAsync(fileUrl);
                await File.WriteAllBytesAsync(destinationPath, data);

                string disabledPath = destinationPath + ".disabled";
                if (File.Exists(disabledPath)) File.Delete(disabledPath);

                AppendDiagnosticsLog($"Sukces! Zainstalowano: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd pobierania {fileName}: {ex.Message}");
                MessageBox.Show($"Nie udało się pobrać {fileName}:\n{ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ToggleDllState(string fileName)
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (string.IsNullOrEmpty(targetDir)) return;

            string pluginsDir = Path.Combine(targetDir, "BepInEx", "plugins");
            string normalPath = Path.Combine(pluginsDir, fileName);
            string disabledPath = Path.Combine(pluginsDir, fileName + ".disabled");

            if (File.Exists(normalPath))
            {
                File.Move(normalPath, disabledPath);
                AppendDiagnosticsLog($"Wyłączono wtyczkę {fileName}");
            }
            else if (File.Exists(disabledPath))
            {
                File.Move(disabledPath, normalPath);
                AppendDiagnosticsLog($"Włączono wtyczkę {fileName}");
            }
            RefreshDllStatuses();
        }

        private void DeleteDllFile(string fileName)
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (string.IsNullOrEmpty(targetDir)) return;

            string pluginsDir = Path.Combine(targetDir, "BepInEx", "plugins");
            string normalPath = Path.Combine(pluginsDir, fileName);
            string disabledPath = Path.Combine(pluginsDir, fileName + ".disabled");

            if (File.Exists(normalPath)) File.Delete(normalPath);
            if (File.Exists(disabledPath)) File.Delete(disabledPath);

            AppendDiagnosticsLog($"Usunięto wtyczkę {fileName}");
            RefreshDllStatuses();
        }

        private string GetCurrentModTargetDirectory()
        {
            if (string.IsNullOrEmpty(_applicationSettings.AmongUsPath)) return string.Empty;
            string mainGameFolder = GetSteamSourceDirectory();
            string parentFolder = Directory.GetParent(mainGameFolder)?.FullName ?? mainGameFolder;
            string targetFolder = _selectedModType == ModType.Mira ? "Among Us TOUMira" : "Among Us TOU";
            return Path.Combine(parentFolder, targetFolder);
        }

        private async void BtnInstallAUnlocker_Click(object sender, RoutedEventArgs e)
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (!Directory.Exists(targetDir)) { MessageBox.Show("Zainstaluj najpierw modyfikację z Pulpitu!", "Brak folderu", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            BtnInstallAUnlocker.IsEnabled = false;
            BtnInstallAUnlocker.Content = "Pobieranie...";
            await DownloadSingleDllAsync(AUnlockerUrl, "AUnlocker.dll", targetDir);
            BtnInstallAUnlocker.Content = "Pobierz";
            RefreshDllStatuses();
        }

        private void BtnToggleAUnlocker_Click(object sender, RoutedEventArgs e) => ToggleDllState("AUnlocker.dll");
        private void BtnDeleteAUnlocker_Click(object sender, RoutedEventArgs e) => DeleteDllFile("AUnlocker.dll");

        private async void BtnInstallMiraStats_Click(object sender, RoutedEventArgs e)
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (!Directory.Exists(targetDir)) { MessageBox.Show("Zainstaluj najpierw modyfikację z Pulpitu!", "Brak folderu", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            BtnInstallMiraStats.IsEnabled = false;
            BtnInstallMiraStats.Content = "Pobieranie...";
            await DownloadSingleDllAsync(MiraStatsExporterUrl, "MiraStatsExporter.dll", targetDir);
            BtnInstallMiraStats.Content = "Pobierz";
            RefreshDllStatuses();
        }

        private void BtnToggleMiraStats_Click(object sender, RoutedEventArgs e) => ToggleDllState("MiraStatsExporter.dll");
        private void BtnDeleteMiraStats_Click(object sender, RoutedEventArgs e) => DeleteDllFile("MiraStatsExporter.dll");

        private async void BtnInstallAleLudu_Click(object sender, RoutedEventArgs e)
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (!Directory.Exists(targetDir)) { MessageBox.Show("Zainstaluj najpierw modyfikację z Pulpitu!", "Brak folderu", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            BtnInstallAleLudu.IsEnabled = false;
            BtnInstallAleLudu.Content = "Pobieranie...";
            await DownloadSingleDllAsync(AleLuduModUrl, "AleLuduMod.dll", targetDir);
            BtnInstallAleLudu.Content = "Pobierz";
            RefreshDllStatuses();
        }

        private void BtnToggleAleLudu_Click(object sender, RoutedEventArgs e) => ToggleDllState("AleLuduMod.dll");
        private void BtnDeleteAleLudu_Click(object sender, RoutedEventArgs e) => DeleteDllFile("AleLuduMod.dll");

        private async void BtnRepairAllDll_Click(object sender, RoutedEventArgs e)
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                MessageBox.Show("Nie znaleziono folderu modyfikacji. Zainstaluj najpierw modyfikację.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendDiagnosticsLog("Naprawa wtyczek DLL...");
            await DownloadSingleDllAsync(AUnlockerUrl, "AUnlocker.dll", targetDir);
            await DownloadSingleDllAsync(MiraStatsExporterUrl, "MiraStatsExporter.dll", targetDir);
            await DownloadSingleDllAsync(AleLuduModUrl, "AleLuduMod.dll", targetDir);
            RefreshDllStatuses();
            MessageBox.Show("Wszystkie wtyczki DLL zostały pomyślnie pobrane i naprawione!", "Naprawa", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnRepairMods_Click(object sender, RoutedEventArgs e)
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                MessageBox.Show("Brak modyfikacji do naprawienia.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                AppendDiagnosticsLog("Rozpoczynanie naprawy pliku sterowania Steam...");
                File.WriteAllText(Path.Combine(targetDir, "steam_appid.txt"), "945360");
                RefreshDllStatuses();
                MessageBox.Show("Pliki wywołania modyfikacji zostały naprawione!", "Naprawa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd podczas naprawy: {ex.Message}");
            }
        }

        private void BtnOpenModFolder_Click(object sender, RoutedEventArgs e)
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                MessageBox.Show("Folder modyfikacji nie istnieje na dysku.", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
                AppendDiagnosticsLog($"Otwarto katalog: {targetDir}");
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd podczas otwierania folderu: {ex.Message}");
            }
        }

        private async void BtnDeleteMod_Click(object sender, RoutedEventArgs e)
        {
            string targetDir = GetCurrentModTargetDirectory();
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                MessageBox.Show("Brak instalacji tej modyfikacji do usunięcia.", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"Czy na pewno chcesz usunąć całą modyfikację i folder:\n{targetDir}?",
                "Potwierdzenie usunięcia",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                AppendDiagnosticsLog($"Usuwanie modyfikacji z: {targetDir}...");
                await Task.Run(() => Directory.Delete(targetDir, true));

                AppendDiagnosticsLog("Modyfikacja została pomyślnie usunięta.");
                VerifyInstallationStatus();
                RefreshDllStatuses();
                MessageBox.Show("Pomyślnie usunięto modyfikację z dysku.", "Elit Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd podczas usuwania: {ex.Message}");
                MessageBox.Show($"Nie udało się usunąć moda (sprawdź czy gra nie jest włączona):\n{ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnMainLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_applicationSettings.AmongUsPath)) return;

            if (_selectedModType == ModType.Vanilla)
            {
                ExecuteGameProcess(_applicationSettings.AmongUsPath, "Uruchamianie czystej gry Vanilla...");
                return;
            }

            string sourceDirectory = GetSteamSourceDirectory();
            string parentFolder = Directory.GetParent(sourceDirectory)?.FullName ?? sourceDirectory;

            string targetFolder = _selectedModType == ModType.Mira ? "Among Us TOUMira" : "Among Us TOU";
            string moddedExePath = Path.Combine(parentFolder, targetFolder, "Among Us.exe");

            if (File.Exists(moddedExePath))
            {
                ExecuteGameProcess(moddedExePath, $"Uruchamianie zmodowanej gry ({targetFolder})...");
            }
            else
            {
                MessageBox.Show("Modyfikacja nie jest zainstalowana na dysku! Zainstaluj ją, aby móc ją uruchomić.", "Elit Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExecuteGameProcess(string exePath, string logMessage)
        {
            try
            {
                if (!File.Exists(exePath))
                {
                    MessageBox.Show("Nie odnaleziono pliku wykonawczego gry.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string workingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
                File.WriteAllText(Path.Combine(workingDirectory, "steam_appid.txt"), "945360");

                AppendDiagnosticsLog(logMessage);

                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false
                };

                Process.Start(processInfo);
                AppendDiagnosticsLog("Gra została uruchomiona pomyślnie.");
            }
            catch (Exception ex)
            {
                AppendDiagnosticsLog($"Błąd uruchamiania: {ex.Message}");
                MessageBox.Show($"Błąd: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            TxtConsole.Clear();
            AppendDiagnosticsLog("Wyczyszczono logi.");
        }
    }
}