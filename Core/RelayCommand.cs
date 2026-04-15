using System.Windows.Input;


namespace HydroExplorer.Core
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object>? _execute = null;
        private readonly Action? _executeSimple = null;
        private readonly Func<object, bool>? _canExecute = null;

        public RelayCommand(object currentView)
        {
            CurrentView = currentView;
        }

        public RelayCommand(Action<object> execute, Func<object, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<object, bool>? canExecute = null)
        {
            _executeSimple = execute;
            _canExecute = canExecute;
        }

        public object CurrentView { get; }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute?.Invoke(parameter);
            _executeSimple?.Invoke();
        }
    }
}
