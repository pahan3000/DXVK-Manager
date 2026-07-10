using System;
using System.Windows;
using Playnite.SDK;

namespace DxvkInjector
{
    public partial class DxvkScanWindow : Window
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public DxvkScanWindow()
        {
            InitializeComponent();
        }

        // Per-game failures inside ScanAsync are already caught, but anything that
        // throws outside that loop (e.g. reading the game list itself) would
        // otherwise crash Playnite the same way an unguarded Detect() would.
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is DxvkScanViewModel vm)
            {
                try
                {
                    await vm.ScanAsync();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "DXVK Injector: library scan failed");
                    vm.SummaryText = $"Scan failed: {ex.Message}";
                }
            }
        }
    }
}
