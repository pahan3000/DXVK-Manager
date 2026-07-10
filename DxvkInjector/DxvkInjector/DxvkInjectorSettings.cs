using DxvkInjector.Dxvk;

namespace DxvkInjector
{
    /// <summary>
    /// The actual persisted settings, saved/loaded via Plugin.SavePluginSettings /
    /// LoadPluginSettings. This is what shows up under Playnite's Add-ons settings (F9).
    /// </summary>
    public class DxvkInjectorSettings : ObservableObject
    {
        private string dxvkFolderPath;
        /// <summary>Folder the user extracted a DXVK release into — expected to contain x32/ and x64/.</summary>
        public string DxvkFolderPath
        {
            get => dxvkFolderPath;
            set => SetValue(ref dxvkFolderPath, value);
        }

        private bool injectD3D9 = true;
        public bool InjectD3D9 { get => injectD3D9; set => SetValue(ref injectD3D9, value); }

        private bool injectD3D10Core = true;
        public bool InjectD3D10Core { get => injectD3D10Core; set => SetValue(ref injectD3D10Core, value); }

        private bool injectD3D11 = true;
        public bool InjectD3D11 { get => injectD3D11; set => SetValue(ref injectD3D11, value); }

        private bool injectDxgi = true;
        public bool InjectDxgi { get => injectDxgi; set => SetValue(ref injectDxgi, value); }

        private bool autoInjectEnabled;
        /// <summary>Master switch — inject DXVK automatically for DX9–DX11 games instead of requiring a manual per-game click.</summary>
        public bool AutoInjectEnabled
        {
            get => autoInjectEnabled;
            set => SetValue(ref autoInjectEnabled, value);
        }

        private bool autoInjectOnAdded = true;
        /// <summary>Run auto-inject when a new game is added to the library (and is already installed).</summary>
        public bool AutoInjectOnAdded { get => autoInjectOnAdded; set => SetValue(ref autoInjectOnAdded, value); }

        private bool autoInjectOnInstalled = true;
        /// <summary>Run auto-inject right after Playnite finishes installing a game.</summary>
        public bool AutoInjectOnInstalled { get => autoInjectOnInstalled; set => SetValue(ref autoInjectOnInstalled, value); }

        private bool showNotifications = true;
        public bool ShowNotifications { get => showNotifications; set => SetValue(ref showNotifications, value); }

        /// <summary>Combines the four per-module toggles into the flag mask used by Inject().</summary>
        public DxvkModule GetEnabledModuleMask()
        {
            var mask = DxvkModule.None;
            if (InjectD3D9) mask |= DxvkModule.D3D9;
            if (InjectD3D10Core) mask |= DxvkModule.D3D10Core;
            if (InjectD3D11) mask |= DxvkModule.D3D11;
            if (InjectDxgi) mask |= DxvkModule.Dxgi;
            return mask;
        }

        public DxvkInjectorSettings Clone()
        {
            return new DxvkInjectorSettings
            {
                DxvkFolderPath = DxvkFolderPath,
                InjectD3D9 = InjectD3D9,
                InjectD3D10Core = InjectD3D10Core,
                InjectD3D11 = InjectD3D11,
                InjectDxgi = InjectDxgi,
                AutoInjectEnabled = AutoInjectEnabled,
                AutoInjectOnAdded = AutoInjectOnAdded,
                AutoInjectOnInstalled = AutoInjectOnInstalled,
                ShowNotifications = ShowNotifications
            };
        }

        public void CopyFrom(DxvkInjectorSettings other)
        {
            DxvkFolderPath = other.DxvkFolderPath;
            InjectD3D9 = other.InjectD3D9;
            InjectD3D10Core = other.InjectD3D10Core;
            InjectD3D11 = other.InjectD3D11;
            InjectDxgi = other.InjectDxgi;
            AutoInjectEnabled = other.AutoInjectEnabled;
            AutoInjectOnAdded = other.AutoInjectOnAdded;
            AutoInjectOnInstalled = other.AutoInjectOnInstalled;
            ShowNotifications = other.ShowNotifications;
        }
    }
}
