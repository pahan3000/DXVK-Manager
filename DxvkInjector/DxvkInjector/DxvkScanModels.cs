using System;
using DxvkInjector.Dxvk;

namespace DxvkInjector
{
    /// <summary>
    /// One row in the library scan results list. Bindable so the DataGrid's checkbox
    /// column and live status text update in place as a bulk inject runs.
    /// </summary>
    public class DxvkGameScanResult : ObservableObject
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; }
        public string InstallDirectory { get; set; }

        public DxvkApiVersion DetectedApi { get; set; } = DxvkApiVersion.Unknown;
        public GameArchitecture Architecture { get; set; } = GameArchitecture.Unknown;
        public DxvkModule SuggestedModules { get; set; } = DxvkModule.None;

        /// <summary>True if this title looks like a DX9–DX11 game DXVK can be injected into.</summary>
        public bool Eligible { get; set; }

        private bool alreadyInjected;
        public bool AlreadyInjected
        {
            get => alreadyInjected;
            set => SetValue(ref alreadyInjected, value);
        }

        private bool selected;
        /// <summary>Bound to the row checkbox; only meaningful for eligible, not-yet-injected rows.</summary>
        public bool Selected
        {
            get => selected;
            set => SetValue(ref selected, value);
        }

        private string statusText;
        public string StatusText
        {
            get => statusText;
            set => SetValue(ref statusText, value);
        }

        public string ApiLabel => DetectedApi == DxvkApiVersion.Unknown ? "Unknown" : DetectedApi.ToString();
        public string ArchLabel => Architecture == GameArchitecture.Unknown ? "?" : Architecture.ToString();
    }
}
