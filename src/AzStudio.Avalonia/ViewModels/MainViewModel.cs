using System.Collections.ObjectModel;
using Avalonia.Controls;
using Azure.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzStudio.Core.Auth;
using AzStudio.Core.Models;
using AzStudio.Core.Profiles;
using AzStudio.Avalonia.Views;

namespace AzStudio.Avalonia.ViewModels;

/// <summary>
/// Port of AzStudio.App's MainViewModel — all four service modules wired, same as the
/// WPF version.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore = new();
    private readonly Dictionary<string, TokenCredential> _credentialCache = new();

    public ObservableCollection<ConnectionProfile> Connections { get; } = new();

    public BlobStorageTabViewModel BlobStorage { get; } = new();

    public ServiceBusTabViewModel ServiceBus { get; } = new();

    public KeyVaultTabViewModel KeyVault { get; } = new();

    public LogAnalyticsTabViewModel LogAnalytics { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private ConnectionProfile? selectedConnection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private bool isConnected;

    public bool IsDisconnected => !IsConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isConnecting;

    [ObservableProperty]
    private string statusMessage = "Ready.";

    [ObservableProperty]
    private string? connectedConnectionName;

    [ObservableProperty]
    private bool isStorageSelected = true;

    [ObservableProperty]
    private bool isServiceBusSelected;

    [ObservableProperty]
    private bool isKeyVaultSelected;

    [ObservableProperty]
    private bool isLogAnalyticsSelected;

    public MainViewModel()
    {
        foreach (var profile in _profileStore.Load())
        {
            Connections.Add(profile);
        }
    }

    public void AddOrUpdate(ConnectionProfile profile)
    {
        var existing = Connections.FirstOrDefault(c => c.Id == profile.Id);
        if (existing is null)
        {
            Connections.Add(profile);
        }
        else
        {
            var index = Connections.IndexOf(existing);
            Connections[index] = profile;
            _credentialCache.Remove(profile.Id);
        }

        Persist();
    }

    public void Remove(ConnectionProfile profile)
    {
        Connections.Remove(profile);
        _credentialCache.Remove(profile.Id);
        Persist();
    }

    private void Persist() => _profileStore.Save(Connections);

    private bool CanConnect() => SelectedConnection is not null && !IsConnecting;

    public Window? OwnerWindow { get; set; }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedConnection is null) return;
        var profile = SelectedConnection;

        var hasCachedCredential = _credentialCache.TryGetValue(profile.Id, out var cachedCredential);

        string? servicePrincipalSecret = null;
        if (!hasCachedCredential && profile.AuthType == AuthType.ServicePrincipal)
        {
            servicePrincipalSecret = await ClientSecretPromptWindow.PromptAsync(OwnerWindow, profile.Name);
            if (string.IsNullOrEmpty(servicePrincipalSecret))
            {
                StatusMessage = "Connection cancelled: client secret is required.";
                return;
            }
        }

        IsConnecting = true;
        StatusMessage = hasCachedCredential
            ? "Reconnecting..."
            : profile.AuthType == AuthType.InteractiveUser
                ? "Signing in (check for a browser window)..."
                : "Authenticating...";
        try
        {
            var credential = hasCachedCredential
                ? cachedCredential!
                : await CredentialFactory.CreateAsync(profile, servicePrincipalSecret);
            _credentialCache[profile.Id] = credential;

            BlobStorage.Activate(credential, profile.StorageAccountName);
            ServiceBus.Activate(credential, profile.ServiceBusNamespace);
            KeyVault.Activate(credential, profile.KeyVaultName);
            LogAnalytics.Activate(credential, profile.LogAnalyticsWorkspaceId);

            IsConnected = true;
            ConnectedConnectionName = profile.Name;
            StatusMessage = $"Connected as '{profile.Name}'.";

            if (!string.IsNullOrWhiteSpace(profile.StorageAccountName))
            {
                _ = BlobStorage.LoadContainersCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _credentialCache.Remove(profile.Id);
            BlobStorage.Deactivate();
            ServiceBus.Deactivate();
            KeyVault.Deactivate();
            LogAnalytics.Deactivate();
            StatusMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanDisconnect() => IsConnected;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        BlobStorage.Deactivate();
        ServiceBus.Deactivate();
        KeyVault.Deactivate();
        LogAnalytics.Deactivate();
        IsConnected = false;
        ConnectedConnectionName = null;
        StatusMessage = "Disconnected.";
    }
}
