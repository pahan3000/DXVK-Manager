using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using Playnite.SDK;
using DxvkInjector.Dxvk;

namespace DxvkInjector
{
    /// <summary>
    /// Wires DxvkInjectorSettings into Playnite's Add-ons settings dialog (F9).
    /// Playnite calls BeginEdit when the tab opens, then either EndEdit (Save) or
    /// CancelEdit (Cancel / close without saving).
    /// </summary>
    public class DxvkInjectorSettingsViewModel : ObservableObject, ISettings
    {
        private readonly DxvkInjectorPlugin plugin;
        private DxvkInjectorSettings editingBackup;

        private DxvkInjectorSettings settings;
        public DxvkInjectorSettings Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        private string folderStatusText;
        public string FolderStatusText
        {
            get => folderStatusText;
            set => SetValue(ref folderStatusText, value);
        }

        public DxvkInjectorSettingsViewModel(DxvkInjectorPlugin plugin)
        {
            this.plugin = plugin;

            var saved = plugin.LoadPluginSettings<DxvkInjectorSettings>();
            Settings = saved ?? new DxvkInjectorSettings();
            RefreshFolderStatus();
        }

        public ICommand BrowseFolderCommand => new RelayCommand(BrowseFolder);

        private void BrowseFolder()
        {
            var selected = plugin.PlayniteApi.Dialogs.SelectFolder();
            if (string.IsNullOrEmpty(selected)) return;

            Settings.DxvkFolderPath = selected;
            RefreshFolderStatus();
        }

        private void RefreshFolderStatus()
        {
            if (string.IsNullOrEmpty(Settings.DxvkFolderPath))
            {
                FolderStatusText = "No folder selected yet.";
                return;
            }

            FolderStatusText = DxvkLocalSource.TryValidate(Settings.DxvkFolderPath, out var error)
                ? "Looks good — found an x32/x64 DXVK build in this folder."
                : error;
        }

        public void BeginEdit()
        {
            editingBackup = Settings.Clone();
        }

        public void CancelEdit()
        {
            if (editingBackup != null)
            {
                Settings.CopyFrom(editingBackup);
                RefreshFolderStatus();
            }
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (!string.IsNullOrEmpty(Settings.DxvkFolderPath) &&
                !DxvkLocalSource.TryValidate(Settings.DxvkFolderPath, out var error))
            {
                errors.Add(error);
            }

            if (Settings.AutoInjectEnabled && string.IsNullOrEmpty(Settings.DxvkFolderPath))
            {
                errors.Add("Automatic injection is on, but no DXVK folder is set — pick one first.");
            }

            RefreshFolderStatus();
            return errors.Count == 0;
        }
    }
}
