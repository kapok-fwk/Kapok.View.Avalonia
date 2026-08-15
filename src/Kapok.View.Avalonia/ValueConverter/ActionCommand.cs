using System.Windows.Input;

namespace Kapok.View.Avalonia.ValueConverter;

/// <summary>
/// Wraps a Kapok <see cref="IAction"/> (or <see cref="IAction{T}"/>) as a System.Windows.Input.ICommand
/// so it can be bound to Avalonia's Button.Command etc. (Avalonia reuses the same BCL ICommand
/// interface WPF does, so this needs no framework-specific type - unlike WPF's
/// IActionToICommandConverter, which existed mainly to bridge XAML's Converter= syntax; the actual
/// wrapping logic is identical.)
/// </summary>
public class ActionCommand : ICommand
{
    private readonly IAction? _action;
    private readonly object? _typedAction; // IAction<T> boxed, invoked via reflection-free delegates below
    private readonly Func<object?, bool>? _canExecuteTyped;
    private readonly Action<object?>? _executeTyped;

    public ActionCommand(IAction action)
    {
        _action = action;
        action.CanExecuteChanged += (_, _) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private ActionCommand(Func<object?, bool> canExecute, Action<object?> execute, Action<EventHandler> subscribeCanExecuteChanged)
    {
        _canExecuteTyped = canExecute;
        _executeTyped = execute;
        subscribeCanExecuteChanged(delegate { CanExecuteChanged?.Invoke(this, EventArgs.Empty); });
    }

    /// <summary>
    /// Wraps a generic IAction&lt;T&gt; where T is only known at runtime (e.g. IDataSetSelectionAction&lt;TEntry&gt;
    /// bound via a CommandParameter carrying the selection list).
    /// </summary>
    public static ActionCommand ForGeneric<T>(IAction<T> action)
    {
        return new ActionCommand(
            canExecute: arg => action.CanExecute((T?)arg),
            execute: arg => action.Execute((T?)arg),
            subscribeCanExecuteChanged: handler => action.CanExecuteChanged += handler);
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (_action != null) return _action.CanExecute();
        return _canExecuteTyped!(parameter);
    }

    public void Execute(object? parameter)
    {
        if (_action != null)
        {
            _action.Execute();
            return;
        }

        _executeTyped!(parameter);
    }
}
