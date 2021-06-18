using System;
using System.Windows;
using System.Windows.Input;

namespace FFXIVSettingsSync.Commands
{
    public class ExitApplicationCommand : ICommand
    {
        event EventHandler ICommand.CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        bool ICommand.CanExecute(object parameter)
        {
            return true;
        }

        void ICommand.Execute(object parameter)
        {
            Application.Current.Shutdown();
        }
    }
}
