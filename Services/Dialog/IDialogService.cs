using System.Threading.Tasks;
using Avalonia.Controls;

namespace JxTerminal.Services.Dialog;

/// <summary>
/// Abstraction for presenting modal dialogs, confirmations, and prompts to the user.
/// </summary>
public interface IDialogService
{
    void ShowDialog(Control content, string? title = null);
    void CloseDialog();
    void ShowMessage(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message, string? confirmText = null, bool danger = false);
    Task<string?> ShowInputAsync(string title, string message, bool isPassword = false, string? defaultValue = null);
    Task<string?> ShowPasswordAsync(string title, string message);
}
