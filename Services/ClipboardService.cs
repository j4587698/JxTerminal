using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace JxTerminal.Services;

public static class ClipboardService
{
    public static async Task SetTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(text);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set clipboard text: {ex.Message}");
        }
    }
}
