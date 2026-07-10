using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Models;
using DxvkInjector.Dxvk;

namespace DxvkInjector
{
    /// <summary>
    /// Backs the per-game DXVK window opened from the game right-click menu.
    /// Reads the DXVK folder from the plugin's global settings (F9) — there's no
    /// per-game download/version picker anymore, just Detect / Inject / Remove
    /// against whatever folder is configured globally.
    /// </summary>
    public class GameSettingsViewModel : ObservableObject
    {
        private readonly DxvkInjectorPlugin plugin;
        private readonly DxvkInjectionService injection = new DxvkInjectionService();

        public IPlayniteAPI PlayniteApi => plugin.PlayniteApi;
        public Game SelectedGame { get; }

        private DxvkGameConfig dxvkConfig;
        public DxvkGameConfig DxvkConfig
        {
            get => dxvkConfig;
            set
            {
                if (SetValue(ref dxvkConfig, value))
                {
                    OnPropertyChanged(nameof(DxvkD3D9Enabled));
                    OnPropertyChanged(nameof(DxvkD3D10CoreEnabled));
                    OnPropertyChanged(nameof(DxvkD3D11Enabled));
                    OnPropertyChanged(nameof(DxvkDxgiEnabled));
                }
            }
        }

        private bool busy;
        public bool Busy { get => busy; set => SetValue(ref busy, value); }

        private string statusText;
        public string StatusText { get => statusText; set => SetValue(ref statusText, value); }

        private string detectedApiText = "Not detected yet.";
        public string DetectedApiText { get => detectedApiText; set => SetValue(ref detectedApiText, value); }

        private string detectedArchitectureText = "Not detected yet.";
        public string DetectedArchitectureText { get => detectedArchitectureText; set => SetValue(ref detectedArchitectureText, value); }

        public string DxvkFolderPath => plugin.Settings.Settings.DxvkFolderPath;

        public bool DxvkD3D9Enabled
        {
            get => DxvkConfig != null && (DxvkConfig.ActiveModules & DxvkModule.D3D9) != 0;
            set => ToggleModule(DxvkModule.D3D9, value);
        }

        public bool DxvkD3D10CoreEnabled
        {
            get => DxvkConfig != null && (DxvkConfig.ActiveModules & DxvkModule.D3D10Core) != 0;
            set => ToggleModule(DxvkModule.D3D10Core, value);
        }

        public bool DxvkD3D11Enabled
        {
            get => DxvkConfig != null && (DxvkConfig.ActiveModules & DxvkModule.D3D11) != 0;
            set => ToggleModule(DxvkModule.D3D11, value);
        }

        public bool DxvkDxgiEnabled
        {
            get => DxvkConfig != null && (DxvkConfig.ActiveModules & DxvkModule.Dxgi) != 0;
            set => ToggleModule(DxvkModule.Dxgi, value);
        }

        public GameSettingsViewModel(DxvkInjectorPlugin plugin, Game game)
        {
            this.plugin = plugin;
            SelectedGame = game;
        }

        private void ToggleModule(DxvkModule module, bool enabled)
        {
            if (DxvkConfig == null) return;

            DxvkConfig.ActiveModules = enabled
                ? DxvkConfig.ActiveModules | module
                : DxvkConfig.ActiveModules & ~module;

            OnPropertyChanged(nameof(DxvkD3D9Enabled));
            OnPropertyChanged(nameof(DxvkD3D10CoreEnabled));
            OnPropertyChanged(nameof(DxvkD3D11Enabled));
            OnPropertyChanged(nameof(DxvkDxgiEnabled));

            // Already injected — re-inject immediately so flipping a checkbox takes
            // effect without a separate apply step.
            if (DxvkConfig.Enabled)
            {
                _ = InjectAsync();
            }
        }

        public ICommand InjectCommand => new RelayCommand(async () => await InjectAsync());
        public ICommand RemoveCommand => new RelayCommand(Remove);
        public ICommand DetectCommand => new RelayCommand(Detect);
        public ICommand BrowseExeCommand => new RelayCommand(BrowseExe);
        public ICommand OpenGlobalSettingsCommand => new RelayCommand(
            () => PlayniteApi.MainView.OpenPluginSettings(plugin.Id));
        public ICommand CloseCommand => new RelayCommand(() => CloseRequested?.Invoke());

        /// <summary>
        /// Set by whoever hosts this view model's window (OpenGameDxvkWindow) so the
        /// Close button — a plain UserControl, with no Window of its own — can ask its
        /// host window to close itself.
        /// </summary>
        public Action CloseRequested { get; set; }

        /// <summary>
        /// Lets the user point Detect/Inject at the correct exe when auto-detection guessed
        /// wrong — common for multi-exe titles (separate DX9/DX11/DX12 builds, a launcher plus
        /// the real game exe, etc.) where the folder scan can't tell which one Playnite actually
        /// runs. The choice is remembered per game via DxvkGameConfig.ManualExePath.
        /// </summary>
        private void BrowseExe()
        {
            if (SelectedGame == null) return;

            string picked;
            try
            {
                picked = PlayniteApi.Dialogs.SelectFile("Executable|*.exe");
            }
            catch (Exception ex)
            {
                StatusText = $"Couldn't open the file picker: {ex.Message}";
                return;
            }

            if (string.IsNullOrEmpty(picked)) return; // user cancelled

            DxvkConfig = DxvkConfig ?? new DxvkGameConfig
            {
                ActiveModules = plugin.Settings.Settings.GetEnabledModuleMask()
            };
            DxvkConfig.ManualExePath = picked;

            try
            {
                plugin.ConfigStore.Save(SelectedGame.Id, DxvkConfig);
            }
            catch (Exception ex)
            {
                StatusText = $"Couldn't save the manual exe choice: {ex.Message}";
            }

            Detect();
        }

        public void Detect()
        {
            if (SelectedGame == null) return;

            var gameDir = SelectedGame.InstallDirectory;
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                StatusText = "Game install directory not found.";
                DetectedApiText = "Unknown (install directory not found).";
                DetectedArchitectureText = "Unknown.";
                return;
            }

            DxvkGameConfig existingConfig;
            try
            {
                existingConfig = plugin.ConfigStore.Load(SelectedGame.Id);
            }
            catch (Exception ex)
            {
                // Never let a config-store failure (corrupt file, missing dependency,
                // permissions, etc.) escape uncaught here — an exception thrown from a
                // Command handler propagates through WPF's dispatcher and crashes the
                // whole Playnite process, not just this window.
                StatusText = $"Couldn't check DXVK status for this game: {ex.Message}";
                DetectedApiText = "Unknown (couldn't read saved config).";
                DetectedArchitectureText = "Unknown.";
                existingConfig = null;
            }

            DxvkConfig = existingConfig ?? new DxvkGameConfig
            {
                ActiveModules = plugin.Settings.Settings.GetEnabledModuleMask()
            };

            string exePath = null;
            bool exeFromPlayAction = false;
            bool exeFromManualOverride = !string.IsNullOrEmpty(DxvkConfig?.ManualExePath) && File.Exists(DxvkConfig.ManualExePath);

            if (exeFromManualOverride)
            {
                exePath = DxvkConfig.ManualExePath;
            }
            else
            {
                try
                {
                    exePath = ResolveExePath(SelectedGame, out exeFromPlayAction);
                }
                catch (Exception ex)
                {
                    DetectedApiText = $"Unknown ({ex.Message})";
                    DetectedArchitectureText = "Unknown.";
                }
            }

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    var api = injection.DetectDirectXVersion(exePath);
                    var arch = injection.DetectArchitecture(exePath);
                    var fromImportTable = injection.DetectedFromImportTable(exePath);

                    string guessNote;
                    if (exeFromManualOverride)
                    {
                        guessNote = " [manually selected exe]";
                    }
                    else if (!exeFromPlayAction)
                    {
                        // No real Play Action to trust (common for Steam imports, whose Play
                        // Action is a "steam://rungameid/..." link, not a file) — say so, since
                        // the exe was guessed from the install folder and could be wrong for
                        // titles with multiple executables. Use "Browse..." to fix it.
                        guessNote = $" [guessed exe: {Path.GetFileName(exePath)} — no usable play action, use Browse... if this is wrong]";
                    }
                    else if (api != DxvkApiVersion.Unknown && !fromImportTable)
                    {
                        // The exe's import table gave us nothing usable (dynamic loading, an
                        // anti-tamper wrapper, etc.) — this API came from the exe's filename
                        // instead, which is a much weaker signal.
                        guessNote = " [guessed from filename, not the exe's import table — verify this is right]";
                    }
                    else
                    {
                        guessNote = "";
                    }

                    DetectedApiText = DescribeApi(api) + guessNote;
                    DetectedArchitectureText = arch == GameArchitecture.Unknown
                        ? "Unknown (couldn't read the exe's PE header)."
                        : (arch == GameArchitecture.X64 ? "64-bit" : "32-bit");

                    // Re-derive modules from the detected API every time, as long as DXVK
                    // isn't actually injected yet for this game. A DxvkGameConfig can exist
                    // here without ever having been injected (e.g. Browse for exe... creates
                    // one up front with the default "all modules" mask before Detect ever
                    // runs) — in that case existingConfig != null but the modules still need
                    // narrowing to whatever this specific exe's API calls for. Once it's
                    // actually Enabled we stop, so we don't stomp on toggles made afterward.
                    if (!DxvkConfig.Enabled && api != DxvkApiVersion.Unknown)
                    {
                        DxvkConfig.ActiveModules = DxvkInjectorPlugin.ModulesForApi(api)
                            & plugin.Settings.Settings.GetEnabledModuleMask();
                        OnPropertyChanged(nameof(DxvkD3D9Enabled));
                        OnPropertyChanged(nameof(DxvkD3D10CoreEnabled));
                        OnPropertyChanged(nameof(DxvkD3D11Enabled));
                        OnPropertyChanged(nameof(DxvkDxgiEnabled));
                    }
                }
                catch (Exception ex)
                {
                    DetectedApiText = $"Detection failed: {ex.Message}";
                    DetectedArchitectureText = "Unknown.";
                }
            }
            else if (string.IsNullOrEmpty(DetectedApiText) || DetectedApiText == "Not detected yet.")
            {
                DetectedApiText = "Executable not found — set a play action for this game first.";
                DetectedArchitectureText = "Unknown.";
            }

            StatusText = DxvkConfig.Enabled
                ? $"DXVK ({DxvkConfig.InstalledVersion}) active — modules: {DescribeModules(DxvkConfig.ActiveModules)}."
                : "DXVK not installed for this game.";
        }

        private static string DescribeApi(DxvkApiVersion api)
        {
            switch (api)
            {
                case DxvkApiVersion.D3D9: return "Direct3D 9";
                case DxvkApiVersion.D3D10: return "Direct3D 10 / 10.1";
                case DxvkApiVersion.D3D11: return "Direct3D 11";
                case DxvkApiVersion.D3D12: return "Direct3D 12 — DXVK doesn't apply to this game.";
                default: return "Not detected (dynamically loaded, or a non-Direct3D game).";
            }
        }

        private static string DescribeModules(DxvkModule modules)
        {
            if (modules == DxvkModule.None) return "none";
            var names = new System.Collections.Generic.List<string>();
            if ((modules & DxvkModule.D3D9) != 0) names.Add("d3d9");
            if ((modules & DxvkModule.D3D10Core) != 0) names.Add("d3d10core");
            if ((modules & DxvkModule.D3D11) != 0) names.Add("d3d11");
            if ((modules & DxvkModule.Dxgi) != 0) names.Add("dxgi");
            return string.Join(", ", names);
        }

        public async Task InjectAsync()
        {
            if (SelectedGame == null) return;

            var settings = plugin.Settings.Settings;
            if (!DxvkLocalSource.TryValidate(settings.DxvkFolderPath, out var folderError))
            {
                StatusText = folderError;
                return;
            }

            var gameDir = SelectedGame.InstallDirectory;
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                StatusText = "Game install directory not found.";
                return;
            }

            string exePath;
            if (!string.IsNullOrEmpty(DxvkConfig?.ManualExePath) && File.Exists(DxvkConfig.ManualExePath))
            {
                exePath = DxvkConfig.ManualExePath;
            }
            else
            {
                try
                {
                    exePath = ResolveExePath(SelectedGame);
                }
                catch (Exception ex)
                {
                    StatusText = $"Couldn't resolve the game's executable: {ex.Message}";
                    return;
                }
            }

            var modules = DxvkConfig?.ActiveModules ?? settings.GetEnabledModuleMask();

            Busy = true;
            StatusText = "Installing DXVK...";

            try
            {
                var arch = await Task.Run(() => injection.DetectArchitecture(exePath));
                if (arch == GameArchitecture.Unknown)
                {
                    StatusText = "Could not determine game architecture from exe.";
                    return;
                }

                var label = DxvkLocalSource.GetDisplayLabel(settings.DxvkFolderPath);

                DxvkConfig = await Task.Run(() => injection.Inject(
                    gameDir, settings.DxvkFolderPath, arch, modules, DxvkVariant.Standard, label));

                plugin.ConfigStore.Save(SelectedGame.Id, DxvkConfig);

                StatusText = $"DXVK ({label}) injected ({arch}).";
            }
            catch (Exception ex)
            {
                StatusText = $"Injection failed: {ex.Message}";
            }
            finally
            {
                Busy = false;
            }
        }

        public void Remove()
        {
            if (SelectedGame == null || DxvkConfig == null || !DxvkConfig.Enabled) return;

            var gameDir = SelectedGame.InstallDirectory;
            if (string.IsNullOrEmpty(gameDir)) return;

            try
            {
                injection.Remove(gameDir, DxvkConfig);
                plugin.ConfigStore.Save(SelectedGame.Id, DxvkConfig);
                StatusText = "DXVK removed.";
            }
            catch (Exception ex)
            {
                StatusText = $"Removal failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Resolves a game's exe: prefer its Play Action, but fall back to scanning the
        /// install folder when the Play Action is missing or isn't a real file path.
        /// Shared with the plugin's auto-inject path and the library scan.
        /// </summary>
        /// <remarks>
        /// Library plugins commonly hand Playnite a launcher URI as the Play Action
        /// instead of a real exe path — Steam's own plugin does this for every game
        /// (Path = "steam://rungameid/&lt;appid&gt;"), so without this fallback almost no
        /// Steam-imported title would ever resolve here without a manual Play Action edit.
        /// </remarks>
        public static string ResolveExePath(Game game)
        {
            return ResolveExePath(game, out _);
        }

        /// <summary>Same as <see cref="ResolveExePath(Game)"/> but also reports whether the
        /// result came from the game's own Play Action (true) or was guessed by scanning the
        /// install folder (false) — callers can use this to warn the user it might be wrong.</summary>
        public static string ResolveExePath(Game game, out bool fromPlayAction)
        {
            fromPlayAction = false;

            var playAction = game?.GameActions?.FirstOrDefault(a => a.IsPlayAction);
            if (playAction != null && !string.IsNullOrEmpty(playAction.Path) && IsFilePath(playAction.Path))
            {
                var path = playAction.Path;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(game.InstallDirectory ?? "", path);

                if (File.Exists(path))
                {
                    fromPlayAction = true;
                    return path;
                }
            }

            // No usable Play Action (missing, empty, a launcher URI like steam://, or pointing
            // at a file that isn't actually there) — guess from the install folder instead.
            var guessed = TryAutoDetectExe(game);
            if (!string.IsNullOrEmpty(guessed))
                return guessed;

            if (playAction == null || string.IsNullOrEmpty(playAction.Path))
                throw new InvalidOperationException(
                    "No play action found for this game, and no exe could be found in its install folder.");

            throw new InvalidOperationException(
                "This game's play action doesn't point to a real exe (likely a launcher link, e.g. Steam), " +
                "and no exe could be found in its install folder.");
        }

        /// <summary>True for an ordinary file path; false for launcher URIs like
        /// "steam://rungameid/12345" or "com.epicgames.launcher://apps/...".</summary>
        private static bool IsFilePath(string path) => !path.Contains("://");

        private static readonly string[] ExeNameBlacklist =
        {
            "unins", "redist", "vcredist", "vc_redist", "dotnetfx", "directx", "dxsetup",
            "dxwebsetup", "setup", "crashpad", "crashhandler", "crashreporter",
            "easyanticheat", "eac_launcher", "battleye", "oalinst", "vulkanrt",
            "prereq", "helper", "updater", "installer", "benchmark",
        };

        private static readonly string[] DirBlacklist =
        {
            "_commonredist", "commonredist", "redist", "easyanticheat", "battleye",
            "__installer", "miles", "support",
        };

        /// <summary>
        /// Best-effort guess at a game's main exe when there's no usable Play Action.
        /// Not perfect for multi-exe titles, but gets most single-exe (and most Steam)
        /// games working without any manual configuration.
        /// </summary>
        public static string TryAutoDetectExe(Game game)
        {
            var dir = game?.InstallDirectory;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return null;

            List<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories)
                    .Where(p => !IsBlacklisted(p))
                    .ToList();
            }
            catch (Exception)
            {
                // Locked folder, permissions, reparse point loop, etc. — just give up
                // gracefully, the caller already has a clear error message for this case.
                return null;
            }

            if (candidates.Count == 0)
                return null;

            // Prefer an exe whose filename loosely matches the game's title in Playnite.
            // Titles that ship separate DX9/DX10/DX11/DX12 builds (e.g. "*_DX9.exe",
            // "*_DX11.exe") commonly have more than one exe that matches by name — among
            // those, prefer whichever one actually reports a recognizable DirectX version
            // from its own PE headers instead of just picking the shallowest/largest, since
            // a name match alone doesn't tell us which build Playnite is actually meant to run.
            var normalizedName = Normalize(game.Name);
            List<string> nameMatches = null;
            if (!string.IsNullOrEmpty(normalizedName))
            {
                nameMatches = candidates
                    .Where(p =>
                    {
                        var n = Normalize(Path.GetFileNameWithoutExtension(p));
                        return n.Length > 0 && (n.Contains(normalizedName) || normalizedName.Contains(n));
                    })
                    .OrderBy(p => RelativeDepth(p, dir))
                    .ToList();
            }

            var pool = nameMatches != null && nameMatches.Count > 0 ? nameMatches : candidates;

            if (pool.Count > 1)
            {
                var withDetectedApi = pool.FirstOrDefault(HasDetectableDirectXVersion);
                if (withDetectedApi != null)
                    return withDetectedApi;
            }

            if (nameMatches != null && nameMatches.Count > 0)
                return nameMatches[0];

            // Otherwise: shallowest path first (top-level exes are more likely to be the
            // real game than something nested in a bin/tools/plugins subfolder), then
            // largest file (launcher stubs tend to be small).
            return candidates
                .OrderBy(p => RelativeDepth(p, dir))
                .ThenByDescending(p =>
                {
                    try { return new FileInfo(p).Length; } catch { return 0L; }
                })
                .FirstOrDefault();
        }

        private static bool HasDetectableDirectXVersion(string exePath)
        {
            try
            {
                return new DxvkInjectionService().DetectedFromImportTable(exePath);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsBlacklisted(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
            if (ExeNameBlacklist.Any(fileName.Contains))
                return true;

            var dirName = (Path.GetDirectoryName(filePath) ?? "").ToLowerInvariant();
            return DirBlacklist.Any(dirName.Contains);
        }

        private static int RelativeDepth(string filePath, string rootDir)
        {
            var rel = filePath.Length > rootDir.Length ? filePath.Substring(rootDir.Length) : "";
            return rel.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
        }

        private static string Normalize(string s) =>
            new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
