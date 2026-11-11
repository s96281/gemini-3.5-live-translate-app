using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GeminiLiveTranslate.App.ViewModels;

/// <summary>
/// Synchronous ICommand implementation for MVVM data binding.
/// Pure .NET BCL implementation compatible with both net9.0 and net9.0-windows.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        if (execute == null) throw new ArgumentNullException(nameof(execute));
        _execute = _ => execute();
        if (canExecute != null)
        {
            _canExecute = _ => canExecute();
        }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute(parameter);
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Asynchronous ICommand implementation with re-entrancy guard for MVVM data binding.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Func<object?, bool>? _canExecute;
    private int _isExecuting;

    public bool IsExecuting => Interlocked.CompareExchange(ref _isExecuting, 0, 0) == 1;

    public event EventHandler? CanExecuteChanged;

    public AsyncRelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        if (executeAsync == null) throw new ArgumentNullException(nameof(executeAsync));
        _executeAsync = _ => executeAsync();
        if (canExecute != null)
        {
            _canExecute = _ => canExecute();
        }
    }

    public bool CanExecute(object? parameter)
    {
        return !IsExecuting && (_canExecute?.Invoke(parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter).ConfigureAwait(false);
    }

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            return;
        }

        RaiseCanExecuteChanged();
        try
        {
            await _executeAsync(parameter).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
