using System.Collections.ObjectModel;
using System.Windows;
using Azure.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzStudio.Core.KeyVault;
using AzStudio.App.Utilities;
using AzStudio.App.Views;

namespace AzStudio.App.ViewModels;

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

    /// <summary>
    /// Called once per Connect with the credential for the active connection profile.
    /// This does NOT require a vault name — the vault is chosen (and can be changed)
    /// directly on this tab via VaultName + LoadSecrets.
    /// </summary>
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

    // CommunityToolkit's [RelayCommand] only re-evaluates a button's enabled state when
    // explicitly told to (or when an [ObservableProperty] with [NotifyCanExecuteChangedFor]
    // changes). CanRunService/CanRunOnSelectedSecret below depend on plain fields
    // (_credential, _service), so every place that mutates those fields must call this
    // afterward — otherwise the Load/Refresh buttons stay stuck at whatever state they
    // started in.
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
    private void CopyErrorDetails()
    {
        if (_errorDetails is null) return;
        Clipboard.SetText(_errorDetails);
        StatusMessage = "Error details copied to clipboard — share them with your admin.";
    }

    /// <summary>
    /// Builds (or rebuilds, if the vault name field has changed) the KeyVaultService
    /// for whatever vault name is currently typed into VaultName.
    /// </summary>
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
            SecretValueWindow.Show(Application.Current.MainWindow, secretName, version, value);
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
