using System;
using System.Windows.Input;
using Playnite.SDK;

namespace DxvkInjector
{
    /// <summary>
    /// Minimal ICommand implementation for binding buttons in the settings/game
    /// windows. Wraps an Action (and optionally a CanExecute check) so XAML can
    /// bind to it directly.
    ///
    /// Execute() is wrapped in a try/catch: an exception thrown from a Command
    /// handler runs on the UI dispatcher, and an unhandled exception there takes
    /// the whole Playnite process down with it — not just this plugin's window.
    /// Individual view models should still catch and surface their own errors
    /// (via StatusText etc.) wherever possible; this is the last line of defense
    /// for anything that slips through.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly Action execute;
        private readonly Func<bool> canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => canExecute?.Invoke() ?? true;

        public void Execute(object parameter)
        {
            try
            {
                execute();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DXVK Injector: unhandled exception from a command");
            }
        }
    }
}
