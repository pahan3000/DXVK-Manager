using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DxvkInjector
{
    /// <summary>
    /// Standard INotifyPropertyChanged base used across Playnite plugin templates.
    /// The Playnite SDK does not provide this itself — every generated plugin project
    /// carries its own copy of essentially this class. If your real project already
    /// has one (it almost certainly does), delete this file and use yours instead.
    /// </summary>
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetValue<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
