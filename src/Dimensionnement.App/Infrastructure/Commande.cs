using System.Windows.Input;

namespace Dimensionnement.App.Infrastructure;

public class Commande : ICommand
{
    readonly Action<object?> _action;
    readonly Func<object?, bool>? _possible;

    public Commande(Action<object?> action, Func<object?, bool>? possible = null)
    {
        _action = action;
        _possible = possible;
    }

    public Commande(Action action, Func<bool>? possible = null)
        : this(_ => action(), possible is null ? null : new Func<object?, bool>(_ => possible()))
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parametre) => _possible?.Invoke(parametre) ?? true;
    public void Execute(object? parametre) => _action(parametre);
}
