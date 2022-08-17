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

    public bool CanExecute(object parameter)
    {
        return true;
    }

    public void Execute(object parameter)
    {
        if (Executed != null)
            Executed(this, parameter);
    }

    public event EventHandler CanExecuteChanged;
}