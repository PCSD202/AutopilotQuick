using System;
using System.Windows.Input;

namespace AutopilotQuick;

public class SimpleCommand : ICommand
{
    public Predicate<object> CanExecuteDelegate { get; set; }
    public Action<object> ExecuteDelegate { get; set; }

    public bool CanExecute(object parameter)
    {
        if (CanExecuteDelegate != null)
            return CanExecuteDelegate(parameter);
        return true; // if there is no can execute default to true
    }

    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public void Execute(object parameter)
    {
        if (ExecuteDelegate != null)
            ExecuteDelegate(parameter);
    }
}


class SimpleMainWindowCommand : ICommand
{
    public event EventHandler<object> Executed;

#pragma warning disable CS8767
    public bool CanExecute(object parameter)
#pragma warning restore CS8767
    {
        return true;
    }

#pragma warning disable CS8767
    public void Execute(object parameter)
#pragma warning restore CS8767
    {
        Executed?.Invoke(this, parameter);
    }

    public event EventHandler? CanExecuteChanged;
}