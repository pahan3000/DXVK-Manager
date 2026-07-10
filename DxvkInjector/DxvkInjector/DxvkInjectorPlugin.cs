using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using DxvkInjector.Dxvk;

namespace DxvkInjector
{
    /// <summary>
    /// The plugin Playnite loads (extension.yaml's Module: DxvkInjector.dll points here).
    /// Provides:
    ///  - A settings page under Playnite's Add-ons settings (F9).
    ///  - A topbar icon button that opens the DXVK panel for whichever game is
    ///    currently selected in the library (not the global settings page).
    ///  - A per-game right-click menu item for manual Detect/Inject/Remove.
    ///  - A main-menu "Scan library" action that checks every installed game for
    ///    DXVK compatibility in one pass and lets you bulk-inject the results.
    ///  - Optional automatic DXVK injection for DX9–DX11 games when they're added
    ///    to the library or finish installing.
    /// </summary>
    public class DxvkInjectorPlugin : GenericPlugin
    {
        public override Guid Id { get; } = Guid.Parse("629fcf75-e4c2-47cc-9280-cf339024cd46");

        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly DxvkInjectionService injection = new DxvkInjectionService();

        public DxvkInjectorSettingsViewModel Settings { get; }
        public DxvkGameConfigStore ConfigStore { get; }

        public DxvkInjectorPlugin(IPlayniteAPI api) : base(api)
        {
            Settings = new DxvkInjectorSettingsViewModel(this);
            ConfigStore = new DxvkGameConfigStore(GetPluginUserDataPath());

            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            PlayniteApi.Database.Games.ItemCollectionChanged += Games_ItemCollectionChanged;
        }

        public override ISettings GetSettings(bool firstRunSettings) => Settings;

        public override UserControl GetSettingsView(bool firstRunSettings) => new DxvkInjectorSettingsView();

        // ---------------------------------------------------------------
        // Topbar icon — now opens the per-game DXVK panel for whatever
        // game is currently selected in the library, instead of jumping to
        // the global (F9) settings page.
        // ---------------------------------------------------------------
        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            yield return new TopPanelItem
            {
                Icon = BuildTopPanelIcon(),
                Title = "DXVK Injector — manage DXVK for the selected game",
                Activated = () =>
                {
                    try
                    {
                        var game = PlayniteApi.MainView.SelectedGames?.FirstOrDefault();
                        if (game == null)
                        {
                            PlayniteApi.Dialogs.ShowMessage(
                                "Select a game in your library first, then click this icon to " +
                                "detect, inject, or remove DXVK for it.",
                                "DXVK Injector");
                            return;
                        }

                        OpenGameDxvkWindow(game);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "DXVK Injector: topbar icon action failed");
                        SafeShowError("Couldn't open the DXVK panel for the selected game.", ex);
                    }
                }
            };
        }

        /// <summary>
        /// Small self-drawn "DX / VK" badge (DirectX blue over Vulkan red) so the
        /// topbar icon reads as DXVK at a glance instead of a generic gear.
        /// </summary>
        private static FrameworkElement BuildTopPanelIcon()
        {
            var badge = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x26)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x58, 0x58, 0x60)),
                BorderThickness = new Thickness(1),
                SnapsToDevicePixels = true
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            stack.Children.Add(new TextBlock
            {
                Text = "DX",
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), // DirectX-ish blue
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, -1)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "VK",
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE4, 0x3A, 0x3A)), // Vulkan-ish red
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 1)
            });

            badge.Child = stack;
            return badge;
        }

        // ---------------------------------------------------------------
        // Per-game context menu
        // ---------------------------------------------------------------
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "Manage DXVK",
                MenuSection = "DXVK Injector",
                Action = a =>
                {
                    // Every path out of this delegate must be caught: Playnite invokes
                    // GameMenuItem.Action directly from the UI dispatcher with nothing
                    // guarding it, so an uncaught exception here (e.g. from opening the
                    // window) takes the whole app down instead of just this feature.
                    try
                    {
                        var game = a.Games?.FirstOrDefault();
                        if (game == null) return;

                        OpenGameDxvkWindow(game);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "DXVK Injector: game menu action failed");
                        SafeShowError("Couldn't open the DXVK panel for this game.", ex);
                    }
                }
            };
        }

        // ---------------------------------------------------------------
        // Main menu — library-wide scan
        // ---------------------------------------------------------------
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Scan library for DXVK-compatible games...",
                MenuSection = "@DXVK Injector",
                Action = a =>
                {
                    try
                    {
                        var vm = new DxvkScanViewModel(this);
                        var window = new DxvkScanWindow
                        {
                            DataContext = vm
                        };
                        AttachOwner(window);
                        window.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "DXVK Injector: library scan window failed to open");
                        SafeShowError("Couldn't open the DXVK library scan window.", ex);
                    }
                }
            };
        }

        /// <summary>
        /// Shared by the topbar icon and the game context menu: opens the per-game
        /// Detect/Inject/Remove window for a single game. Uses Playnite's own
        /// CreateWindow instead of a raw custom-chrome Window — it gets native title bar/theme integration for
        /// free and CreateWindow's owner handling is what Playnite itself manages, so this
        /// also sidesteps the fullscreen-mode null-owner crash the old AttachOwner() hack
        /// worked around by hand.
        /// </summary>
        private void OpenGameDxvkWindow(Game game)
        {
            var vm = new GameSettingsViewModel(this, game);
            var view = new GameSettingsView { DataContext = vm };

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false
            });

            window.Title = $"DXVK Injector — {game.Name}";
            window.Content = view;
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            try
            {
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "DXVK Injector: could not resolve current app window");
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            vm.CloseRequested = () => window.Close();

            try
            {
                vm.Detect();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DXVK Injector: Detect() failed on window open");
                vm.StatusText = $"Couldn't check DXVK status for this game: {ex.Message}";
            }

            window.ShowDialog();
        }

        /// <summary>
        /// Sets Window.Owner defensively. Still used by the library scan window
        /// (DxvkScanWindow), which is a plain Window rather than a CreateWindow-hosted
        /// UserControl. GetCurrentAppWindow() can return null in some Playnite modes
        /// (e.g. fullscreen), and a Window with WindowStartupLocation set to CenterOwner
        /// but a null Owner throws InvalidOperationException the moment ShowDialog() is
        /// called. Falling back to CenterScreen avoids it.
        /// </summary>
        private void AttachOwner(Window window)
        {
            Window owner = null;
            try
            {
                owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "DXVK Injector: could not resolve current app window");
            }

            if (owner != null)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void SafeShowError(string message, Exception ex)
        {
            try
            {
                PlayniteApi.Dialogs.ShowErrorMessage($"{message}\n\n{ex.Message}", "DXVK Injector");
            }
            catch
            {
                // If even the error dialog can't be shown, swallow it — logging above
                // already captured the details, and this must never throw further.
            }
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            if (!Settings.Settings.AutoInjectEnabled || !Settings.Settings.AutoInjectOnInstalled) return;
            if (args?.Game == null) return;

            TryAutoInject(args.Game);
        }

        private void Games_ItemCollectionChanged(object sender, ItemCollectionChangedEventArgs<Game> e)
        {
            if (!Settings.Settings.AutoInjectEnabled || !Settings.Settings.AutoInjectOnAdded) return;
            if (e?.AddedItems == null) return;

            foreach (var game in e.AddedItems)
            {
                if (game.IsInstalled)
                {
                    TryAutoInject(game);
                }
            }
        }

        /// <summary>
        /// Auto-inject entry point: validates preconditions, detects the game's DirectX
        /// API from its exe, and injects only if it's a DX9–DX11 title that isn't
        /// already handled. Every failure path just logs and returns — this must never
        /// throw out into a Playnite event handler.
        /// </summary>
        private void TryAutoInject(Game game)
        {
            try
            {
                var settings = Settings.Settings;

                if (!DxvkLocalSource.TryValidate(settings.DxvkFolderPath, out var folderError))
                {
                    logger.Warn($"DXVK auto-inject skipped for '{game.Name}': {folderError}");
                    return;
                }

                var existing = ConfigStore.Load(game.Id);
                if (existing != null && existing.Enabled)
                {
                    return; // already handled, manually or automatically — don't touch it again
                }

                if (string.IsNullOrEmpty(game.InstallDirectory) || !Directory.Exists(game.InstallDirectory))
                {
                    return;
                }

                string exePath;
                try
                {
                    exePath = GameSettingsViewModel.ResolveExePath(game);
                }
                catch
                {
                    return; // no play action yet, nothing to inspect
                }

                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return;
                }

                var arch = injection.DetectArchitecture(exePath);
                if (arch == GameArchitecture.Unknown)
                {
                    return;
                }

                var api = injection.DetectDirectXVersion(exePath);
                var modules = ModulesForApi(api) & settings.GetEnabledModuleMask();

                if (modules == DxvkModule.None)
                {
                    logger.Info($"DXVK auto-inject skipped for '{game.Name}': detected API {api}, not a DX9-DX11 title (or its module is disabled in settings).");
                    return;
                }

                var label = DxvkLocalSource.GetDisplayLabel(settings.DxvkFolderPath);
                var config = injection.Inject(game.InstallDirectory, settings.DxvkFolderPath, arch, modules, DxvkVariant.Standard, label);
                config.InjectedAutomatically = true;
                ConfigStore.Save(game.Id, config);

                logger.Info($"DXVK auto-injected into '{game.Name}' ({api}, {arch}, modules: {modules}).");

                if (settings.ShowNotifications)
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        $"dxvk-autoinject-{game.Id}",
                        $"DXVK auto-injected into \"{game.Name}\" ({api}, {arch}).",
                        NotificationType.Info));
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"DXVK auto-inject failed for '{game?.Name}'");
            }
        }

        internal static DxvkModule ModulesForApi(DxvkApiVersion api)
        {
            switch (api)
            {
                case DxvkApiVersion.D3D9: return DxvkModule.D3D9;
                case DxvkApiVersion.D3D10: return DxvkModule.D3D10Core | DxvkModule.Dxgi;
                case DxvkApiVersion.D3D11: return DxvkModule.D3D11 | DxvkModule.Dxgi;
                default: return DxvkModule.None; // D3D12 or Unknown — leave it alone
            }
        }
    }
}
