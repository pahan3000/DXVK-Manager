using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Models;
using DxvkInjector.Dxvk;

namespace DxvkInjector
{
    /// <summary>
    /// Scans every installed game in the library, detects its DirectX API + architecture
    /// from its exe, and reports whether DXVK looks applicable — without touching anything
    /// until the user picks rows and clicks "Inject selected". Backs DxvkScanWindow.
    /// </summary>
    public class DxvkScanViewModel : ObservableObject
    {
        private readonly DxvkInjectorPlugin plugin;
        private readonly DxvkInjectionService injection = new DxvkInjectionService();

        public IPlayniteAPI PlayniteApi => plugin.PlayniteApi;

        public ObservableCollection<DxvkGameScanResult> Results { get; } = new ObservableCollection<DxvkGameScanResult>();

        private bool busy;
        public bool Busy { get => busy; set => SetValue(ref busy, value); }

        private string summaryText = "Scanning...";
        public string SummaryText { get => summaryText; set => SetValue(ref summaryText, value); }

        public DxvkScanViewModel(DxvkInjectorPlugin plugin)
        {
            this.plugin = plugin;
        }

        public ICommand ScanCommand => new RelayCommand(async () => await ScanAsync());
        public ICommand SelectAllEligibleCommand => new RelayCommand(SelectAllEligible);
        public ICommand SelectNoneCommand => new RelayCommand(SelectNone);
        public ICommand InjectSelectedCommand => new RelayCommand(async () => await InjectSelectedAsync());

        public async Task ScanAsync()
        {
            if (Busy) return;

            Busy = true;
            SummaryText = "Scanning installed games...";
            Application.Current?.Dispatcher.Invoke(() => Results.Clear());

            var settings = plugin.Settings.Settings;
            var folderOk = DxvkLocalSource.TryValidate(settings.DxvkFolderPath, out var folderError);

            var installedGames = PlayniteApi.Database.Games
                .Where(g => g.IsInstalled && !string.IsNullOrEmpty(g.InstallDirectory))
                .ToList();

            int eligibleCount = 0;
            int alreadyInjectedCount = 0;

            await Task.Run(() =>
            {
                foreach (var game in installedGames)
                {
                    DxvkGameScanResult row;
                    try
                    {
                        row = ScanGame(game, settings, folderOk);
                    }
                    catch (Exception ex)
                    {
                        row = new DxvkGameScanResult
                        {
                            GameId = game.Id,
                            GameName = game.Name,
                            InstallDirectory = game.InstallDirectory,
                            Eligible = false,
                            StatusText = $"Scan failed: {ex.Message}"
                        };
                    }

                    if (row.AlreadyInjected) System.Threading.Interlocked.Increment(ref alreadyInjectedCount);
                    if (row.Eligible && !row.AlreadyInjected) System.Threading.Interlocked.Increment(ref eligibleCount);

                    Application.Current?.Dispatcher.Invoke(() => Results.Add(row));
                }
            });

            if (!folderOk)
            {
                SummaryText = $"{folderError} (scan still ran, but nothing can be injected until this is fixed.)";
            }
            else
            {
                SummaryText = $"Scanned {installedGames.Count} installed game(s): " +
                               $"{eligibleCount} eligible for DXVK, {alreadyInjectedCount} already injected.";
            }

            Busy = false;
        }

        private DxvkGameScanResult ScanGame(Game game, DxvkInjectorSettings settings, bool folderOk)
        {
            var row = new DxvkGameScanResult
            {
                GameId = game.Id,
                GameName = game.Name,
                InstallDirectory = game.InstallDirectory
            };

            var existing = plugin.ConfigStore.Load(game.Id);
            row.AlreadyInjected = existing != null && existing.Enabled;

            if (!Directory.Exists(game.InstallDirectory))
            {
                row.StatusText = "Install directory not found.";
                return row;
            }

            string exePath;
            try
            {
                exePath = GameSettingsViewModel.ResolveExePath(game);
            }
            catch (Exception ex)
            {
                row.StatusText = $"No play action: {ex.Message}";
                return row;
            }

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                row.StatusText = "Executable not found.";
                return row;
            }

            row.Architecture = injection.DetectArchitecture(exePath);
            row.DetectedApi = injection.DetectDirectXVersion(exePath);
            row.SuggestedModules = DxvkInjectorPlugin.ModulesForApi(row.DetectedApi) & settings.GetEnabledModuleMask();

            if (row.AlreadyInjected)
            {
                row.StatusText = $"DXVK already injected ({existing.InstalledVersion}).";
                return row;
            }

            if (row.Architecture == GameArchitecture.Unknown)
            {
                row.StatusText = "Could not determine architecture from exe.";
                return row;
            }

            if (row.SuggestedModules == DxvkModule.None)
            {
                row.StatusText = row.DetectedApi == DxvkApiVersion.Unknown
                    ? "Direct3D version not detected (dynamically loaded, or non-D3D game)."
                    : $"Detected {row.DetectedApi} — not a DX9-DX11 title, or its modules are disabled in settings.";
                return row;
            }

            row.Eligible = true;
            row.StatusText = folderOk
                ? $"Eligible ({row.DetectedApi}, {row.Architecture})."
                : $"Eligible ({row.DetectedApi}, {row.Architecture}) — set a DXVK folder in settings first.";
            return row;
        }

        private void SelectAllEligible()
        {
            foreach (var row in Results)
            {
                row.Selected = row.Eligible && !row.AlreadyInjected;
            }
        }

        private void SelectNone()
        {
            foreach (var row in Results)
            {
                row.Selected = false;
            }
        }

        public async Task InjectSelectedAsync()
        {
            if (Busy) return;

            var settings = plugin.Settings.Settings;
            if (!DxvkLocalSource.TryValidate(settings.DxvkFolderPath, out var folderError))
            {
                PlayniteApi.Dialogs.ShowMessage(folderError, "DXVK Injector");
                return;
            }

            var targets = Results.Where(r => r.Selected && r.Eligible && !r.AlreadyInjected).ToList();
            if (targets.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "No eligible games selected. Tick the games you want, or use \"Select all eligible\".",
                    "DXVK Injector");
                return;
            }

            Busy = true;
            var label = DxvkLocalSource.GetDisplayLabel(settings.DxvkFolderPath);
            int successCount = 0, failCount = 0;

            foreach (var row in targets)
            {
                row.StatusText = "Injecting...";
                try
                {
                    var config = await Task.Run(() => injection.Inject(
                        row.InstallDirectory, settings.DxvkFolderPath, row.Architecture,
                        row.SuggestedModules, DxvkVariant.Standard, label));

                    plugin.ConfigStore.Save(row.GameId, config);
                    row.AlreadyInjected = true;
                    row.Selected = false;
                    row.StatusText = $"DXVK ({label}) injected ({row.Architecture}).";
                    successCount++;
                }
                catch (Exception ex)
                {
                    row.StatusText = $"Injection failed: {ex.Message}";
                    failCount++;
                }
            }

            SummaryText = $"Bulk inject finished: {successCount} succeeded, {failCount} failed.";
            Busy = false;
        }
    }
}
