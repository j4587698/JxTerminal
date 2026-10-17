using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using LuminaUI.Controls;

namespace JxTerminal.Services.Dialog;

/// <summary>
/// Default implementation of IDialogService that renders modal dialogs using LuminaDialog.
/// </summary>
public class DialogService : IDialogService
{
    private static IDialogService? _instance;
    public static IDialogService Instance => _instance ??= new DialogService();

    private Action<Control, string?>? _showDialogAction;
    private Action? _closeDialogAction;

    public void RegisterDialogHost(Action<Control, string?> showDialogAction, Action closeDialogAction)
    {
        _showDialogAction = showDialogAction;
        _closeDialogAction = closeDialogAction;
    }

    public void ShowDialog(Control content, string? title = null)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowDialog(content, title));
            return;
        }

        if (_showDialogAction != null)
        {
            _showDialogAction(content, title);
        }
        else
        {
            ViewModels.MainWindowViewModel.Instance?.ShowDialog(content, title);
        }
    }

    public void CloseDialog()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(CloseDialog);
            return;
        }

        if (_closeDialogAction != null)
        {
            _closeDialogAction();
        }
        else
        {
            ViewModels.MainWindowViewModel.Instance?.CloseDialog();
        }
    }

    public void ShowMessage(string title, string message)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowMessage(title, message));
            return;
        }

        var panel = new StackPanel
        {
            Spacing = 16,
            MinWidth = 300,
            MaxWidth = 440
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            LineHeight = 20
        });
        var confirm = new Button
        {
            Content = LocalizationService.Instance["Confirm"],
            Classes = { "Primary" },
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };
        confirm.Click += (_, _) => CloseDialog();
        panel.Children.Add(confirm);

        ShowDialog(panel, title);
    }

    public Task<bool> ShowConfirmAsync(string title, string message, string? confirmText = null, bool danger = false)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ShowConfirmAsync(title, message, confirmText, danger));
        }

        var tcs = new TaskCompletionSource<bool>();
        var l = LocalizationService.Instance;
        var btnText = string.IsNullOrWhiteSpace(confirmText) ? l["Confirm"] : confirmText;

        var confirm = new Button
        {
            Content = btnText,
            Classes = { danger ? "Danger" : "Primary" }
        };
        var cancel = new Button
        {
            Content = l["Cancel"],
            Classes = { "Ghost" }
        };

        confirm.Click += (_, _) => { tcs.TrySetResult(true); CloseDialog(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(false); CloseDialog(); };

        var panel = new StackPanel
        {
            Spacing = 16,
            MinWidth = 320,
            MaxWidth = 460
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            LineHeight = 20
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);

        ShowDialog(panel, title);
        return tcs.Task;
    }

    public Task<string?> ShowInputAsync(string title, string message, bool isPassword = false, string? defaultValue = null)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ShowInputAsync(title, message, isPassword, defaultValue));
        }

        var tcs = new TaskCompletionSource<string?>();
        var l = LocalizationService.Instance;

        var input = new TextBox
        {
            PlaceholderText = message,
            Text = defaultValue ?? string.Empty
        };
        if (isPassword) input.PasswordChar = '\u25cf';

        void Complete(string? result)
        {
            tcs.TrySetResult(result);
            CloseDialog();
        }

        var confirm = new Button
        {
            Content = l["Confirm"],
            Classes = { "Primary" },
            IsDefault = true
        };
        confirm.Click += (_, _) => Complete(input.Text ?? string.Empty);

        var cancel = new Button
        {
            Content = l["Cancel"],
            Classes = { "Ghost" },
            IsCancel = true
        };
        cancel.Click += (_, _) => Complete(null);

        var panel = new StackPanel
        {
            Spacing = 16,
            MinWidth = 320,
            MaxWidth = 460
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            LineHeight = 20
        });
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);

        ShowDialog(panel, title);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => input.Focus(), Avalonia.Threading.DispatcherPriority.Input);
        return tcs.Task;
    }

    public Task<string?> ShowPasswordAsync(string title, string message)
        => ShowInputAsync(title, message, isPassword: true);
}
