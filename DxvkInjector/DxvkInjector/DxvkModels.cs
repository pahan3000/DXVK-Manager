using System;

namespace DxvkInjector.Dxvk
{
    /// <summary>
    /// Which DXVK-provided DLL is targeted. Bitflags so a game can run any subset
    /// (e.g. only d3d9 for an old title, or all four for a game that touches multiple APIs).
    /// </summary>
    [Flags]
    public enum DxvkModule
    {
        None = 0,
        D3D9 = 1,
        D3D10Core = 2,
        D3D11 = 4,
        Dxgi = 8,
        All = D3D9 | D3D10Core | D3D11 | Dxgi
    }

    public enum DxvkVariant
    {
        Standard,   // doitsujin/dxvk
        GplAsync    // dxvk-gplasync fork, persisted shader cache
    }

    public enum GameArchitecture
    {
        Unknown,
        X86,
        X64
    }

    /// <summary>
    /// Direct3D API a game's executable imports, as detected by walking its PE import
    /// table. Used to decide whether (and how) to auto-inject DXVK.
    /// </summary>
    public enum DxvkApiVersion
    {
        Unknown,
        D3D9,
        D3D10,
        D3D11,
        D3D12
    }

    /// <summary>
    /// Per-game DXVK state, persisted via DxvkGameConfigStore.
    /// </summary>
    public class DxvkGameConfig
    {
        public bool Enabled { get; set; }

        /// <summary>Free-text label for what was injected — typically the DXVK folder name (e.g. "dxvk-2.4"), not a resolved version.</summary>
        public string InstalledVersion { get; set; }

        public DxvkVariant Variant { get; set; } = DxvkVariant.Standard;
        public DxvkModule ActiveModules { get; set; } = DxvkModule.All;

        /// <summary>True if this config was written by the auto-inject feature rather than a manual Inject click.</summary>
        public bool InjectedAutomatically { get; set; }

        /// <summary>
        /// User-picked exe path, set via the "Browse..." button when auto-detection guesses
        /// wrong (multi-exe titles, launcher stubs, etc.). When set, this is used instead of
        /// the Play Action / folder-scan guess for every future Detect/Inject.
        /// </summary>
        public string ManualExePath { get; set; }
    }
}
