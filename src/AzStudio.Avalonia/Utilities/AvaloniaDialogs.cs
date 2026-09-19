using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using AzStudio.Avalonia.Views;

namespace AzStudio.Avalonia.Utilities;

/// <summary>
/// Avalonia equivalents of the WPF-only calls the ViewModels used directly (static
/// Clipboard, MessageBox, Microsoft.Win32 file dialogs, Application.Current.MainWindow).
/// Avalonia has no static Clipboard/MessageBox — everything hangs off a TopLevel (a
/// Window, here always the main window) and is async, unlike WPF's synchronous
/// ShowDialog(). This is the actual porting cost that spike was meant to surface: every
/// one of these call sites in every ViewModel needs this kind of change, not just the
/// XAML.
/// </summary>
public static class AvaloniaDialogs
{
    private static Window? MainWindow =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

    public static async Task CopyToClipboardAsync(string text)
    {
        var clipboard = MainWindow?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public static Task<bool> ConfirmAsync(string message, string title) =>
        ConfirmWindow.AskAsync(MainWindow, message, title);

    public static Task<string?> PromptTextAsync(string title, string prompt) =>
        TextInputWindow.PromptAsync(MainWindow, title, prompt);

    public static Task<(string Subject, string Body)?> PromptSendMessageAsync() =>
        SendMessageWindow.PromptAsync(MainWindow);

    public static Window? Owner => MainWindow;

    public static async Task<string?> OpenFileAsync(string title)
    {
        var provider = MainWindow?.StorageProvider;
        if (provider is null) return null;

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> SaveFileAsync(string title, string suggestedName)
    {
        var provider = MainWindow?.StorageProvider;
        if (provider is null) return null;

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName });
        return file?.TryGetLocalPath();
    }
}
