using System.Collections.ObjectModel;
using Azure.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzStudio.Core.KeyVault;
using AzStudio.Avalonia.Utilities;
using AzStudio.Avalonia.Views;

namespace AzStudio.Avalonia.ViewModels;

/// <summary>
/// Port of AzStudio.App's KeyVaultTabViewModel — the only changes are Clipboard.SetText
/// and SecretValueWindow.Show(owner, ...) becoming the async AvaloniaDialogs equivalents.
/// </summary>
public partial class KeyVaultTabViewModel : ObservableObject
{
    private TokenCredential? _credential;
    private KeyVaultService? _service;
    private string? _connectedVaultName;

    [ObservableProperty]
    private string vaultName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadSecretsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSecretVersionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ViewSecretValueCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Not connected.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyErrorDetailsCommand))]
    private bool hasError;

    private string? _errorDetails;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshSecretVersionsCommand))]
    private SecretSummaryInfo? selectedSecret;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ViewSecretValueCommand))]
    private SecretVersionInfo? selectedVersion;

    public ObservableCollection<SecretSummaryInfo> Secrets { get; } = new();

    public ObservableCollection<SecretVersionInfo> SecretVersions { get; } = new();

    public void Activate(TokenCredential credential, string defaultVaultName)
    {
        _credential = credential;
        _service = null;
        _connectedVaultName = null;
        VaultName = defaultVaultName;
        Secrets.Clear();
        SecretVersions.Clear();
        SelectedVersion = null;
        StatusMessage = "Connected. Enter a key vault name and load secrets.";
        ClearError();
        NotifyServiceCommands();
    }

    public void Deactivate()
    {
        _credential = null;
        _service = null;
        _connectedVaultName = null;
        Secrets.Clear();
        SecretVersions.Clear();
        SelectedVersion = null;
        StatusMessage = "Not connected.";
        ClearError();
        NotifyServiceCommands();
    }

    private void NotifyServiceCommands()
    {
        LoadSecretsCommand.NotifyCanExecuteChanged();
        RefreshSecretVersionsCommand.NotifyCanExecuteChanged();
        ViewSecretValueCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunService() => _credential is not null && !IsBusy;

    private void SetError(string operation, Exception ex, string resourceLabel)
    {
        StatusMessage = FriendlyError.Summarize(ex, resourceLabel);
        _errorDetails = FriendlyError.BuildDetails(operation, ex, resourceLabel);
        HasError = true;
    }

    private void ClearError()
    {
        HasError = false;
        _errorDetails = null;
    }

    private bool CanCopyErrorDetails() => HasError && _errorDetails is not null;

    [RelayCommand(CanExecute = nameof(CanCopyErrorDetails))]
    private async Task CopyErrorDetailsAsync()
    {
        if (_errorDetails is null) return;
        await AvaloniaDialogs.CopyToClipboardAsync(_errorDetails);
        StatusMessage = "Error details copied to clipboard — share them with your admin.";
    }

    private KeyVaultService? EnsureService()
    {
        if (_credential is null) return null;

        var name = VaultName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            StatusMessage = "Enter a key vault name first.";
            return null;
        }

        if (_service is null || !string.Equals(_connectedVaultName, name, StringComparison.OrdinalIgnoreCase))
        {
            _service = new KeyVaultService(name, _credential);
            _connectedVaultName = name;
            NotifyServiceCommands();
        }

        return _service;
    }

    [RelayCommand(CanExecute = nameof(CanRunService))]
    private async Task LoadSecretsAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        IsBusy = true;
        try
        {
            StatusMessage = $"Loading secrets in '{_connectedVaultName}'...";
            var secrets = await service.ListSecretsAsync();
            Secrets.Clear();
            foreach (var s in secrets) Secrets.Add(s);
            SecretVersions.Clear();
            SelectedVersion = null;
            StatusMessage = $"{Secrets.Count} secret(s) loaded from '{_connectedVaultName}'.";
            ClearError();
        }
        catch (Exception ex)
        {
            SetError("Load secrets", ex, $"key vault '{_connectedVaultName}'");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedSecretChanged(SecretSummaryInfo? value)
    {
        if (value is not null)
        {
            _ = LoadSecretVersionsAsync();
        }
    }

    private bool CanRunOnSelectedSecret() => _service is not null && !IsBusy && SelectedSecret is not null;

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedSecret))]
    private async Task RefreshSecretVersionsAsync() => await LoadSecretVersionsAsync();

    private async Task LoadSecretVersionsAsync()
    {
        if (_service is null || SelectedSecret is null) return;
        IsBusy = true;
        try
        {
            StatusMessage = $"Loading versions of '{SelectedSecret.Name}'...";
            var versions = await _service.ListSecretVersionsAsync(SelectedSecret.Name);
            SecretVersions.Clear();
            SelectedVersion = null;
            foreach (var v in versions) SecretVersions.Add(v);
            StatusMessage = $"{SecretVersions.Count} version(s) loaded for '{SelectedSecret.Name}'.";
            ClearError();
        }
        catch (Exception ex)
        {
            SetError("Load secret versions", ex, $"secret '{SelectedSecret.Name}'");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanViewSecretValue() => _service is not null && !IsBusy && SelectedSecret is not null && SelectedVersion is not null;

    [RelayCommand(CanExecute = nameof(CanViewSecretValue))]
    private async Task ViewSecretValueAsync()
    {
        if (_service is null || SelectedSecret is null || SelectedVersion is null) return;

        var secretName = SelectedSecret.Name;
        var version = SelectedVersion.Version;

        IsBusy = true;
        try
        {
            StatusMessage = $"Fetching value of '{secretName}' (version {version})...";
            var value = await _service.GetSecretValueAsync(secretName, version);
            StatusMessage = $"Fetched value of '{secretName}' (version {version}). It is not shown in the status log.";
            ClearError();
            SecretValueWindow.Show(AvaloniaDialogs.Owner, secretName, version, value);
        }
        catch (Exception ex)
        {
            SetError("View secret value", ex, $"secret '{secretName}' (version {version})");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
