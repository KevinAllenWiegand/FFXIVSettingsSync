using System;
using System.Windows;
using System.Windows.Input;
using FFXIVSettingsSync.Commands;

namespace FFXIVSettingsSync.ViewModels
{
    public class MainViewModel : ViewModelBase, IToggleVisibility
    {
        public State State => SettingsWatcher.Instance.State;

        public string ToggleStateMenuDisplayString
        {
            get { return Application.Current.MainWindow.Visibility == Visibility.Visible ? "Hide" : "Show"; }
        }

        public string TrayIconSource => "Resources/Moogle.ico";

        public ICommand ToggleApplicationVisibilityCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand ResumeCommand { get; }
        public ICommand SynchronizeCommand { get; }

        public MainViewModel()
        {
            SettingsWatcher.Instance.StateChanged += OnStateChanged;

            ToggleApplicationVisibilityCommand = new ToggleApplicationVisibilityCommand();
            ExitCommand = new ExitApplicationCommand();
            StartCommand = new RelayCommand(StartCommandImpl, parameter => StartCommandCanExecute());
            StopCommand = new RelayCommand(StopCommandImpl, parameter => StopCommandCanExecute());
            PauseCommand = new RelayCommand(PauseCommandImpl, parameter => PauseCommandCanExecute());
            ResumeCommand = new RelayCommand(ResumeCommandImpl, parameter => ResumeCommandCanExecute());
            SynchronizeCommand = new RelayCommand(SynchronizeCommandImpl, parameter => SynchronizeCommandCanExecute());
        }

        private void StartCommandImpl(object parameter)
        {
            SettingsWatcher.Instance.Start();
        }

        private void StopCommandImpl(object parameter)
        {
            SettingsWatcher.Instance.Stop();
        }

        private void PauseCommandImpl(object parameter)
        {
            SettingsWatcher.Instance.Pause();
        }

        private void ResumeCommandImpl(object parameter)
        {
            SettingsWatcher.Instance.Resume();
        }

        private void SynchronizeCommandImpl(object parameter)
        {
            SettingsWatcher.Instance.SynchronizeFiles();
        }

        private bool StartCommandCanExecute()
        {
            return State == State.Stopped;
        }

        private bool StopCommandCanExecute()
        {
            return State == State.Running || State == State.Paused;
        }

        private bool PauseCommandCanExecute()
        {
            return State == State.Running;
        }

        private bool ResumeCommandCanExecute()
        {
            return State == State.Paused;
        }

        private bool SynchronizeCommandCanExecute()
        {
            return State == State.Stopped;
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            NotifyPropertyChanged(nameof(State));
        }

        void IToggleVisibility.OnVisibilityChanged()
        {
            NotifyPropertyChanged(nameof(ToggleStateMenuDisplayString));
        }
    }
}
