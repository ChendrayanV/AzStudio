using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzStudio.Core.Auth;
using AzStudio.Core.Models;
using AzStudio.Core.Profiles;

namespace AzStudio.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore = new();

    public ObservableCollection<ConnectionProfile> Connections { get; } = new();

    public BlobStorageTabViewModel BlobStorage { get; } = new();

    public ServiceBusTabViewModel ServiceBus { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private ConnectionProfile? selectedConnection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isConnecting;

    [ObservableProperty]
    private string statusMessage = "Ready.";

    [ObservableProperty]
    private string? connectedConnectionName;

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
        }

        Persist();
    }

    public void Remove(ConnectionProfile profile)
    {
        Connections.Remove(profile);
        Persist();
    }

    private void Persist() => _profileStore.Save(Connections);

    private bool CanConnect() => SelectedConnection is not null && !IsConnecting;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedConnection is null) return;
        var profile = SelectedConnection;

        IsConnecting = true;
        StatusMessage = "Connected. Sign-in happens per service when you first load data.";
        try
        {
            var credential = CredentialFactory.Create(profile);

            // Deliberately NOT forcing a token fetch here. Storage and Service Bus are
            // different Azure resource audiences, so each authenticates separately on its
            // own first use regardless — pre-fetching a token for an unrelated resource
            // (e.g. Azure Resource Manager, which this app never calls) just adds a second,
            // wasted sign-in round and can trigger an extra interactive prompt in tenants
            // with strict per-resource conditional access. Each tab's Load command already
            // surfaces auth failures clearly via its own try/catch.

            // Both tabs are activated with the shared credential regardless of whether a
            // default account/namespace was saved on this connection — the account/namespace
            // name can always be typed (or changed) directly on each tab.
            BlobStorage.Activate(credential, profile.StorageAccountName);
            ServiceBus.Activate(credential, profile.ServiceBusNamespace);

            IsConnected = true;
            ConnectedConnectionName = profile.Name;
            StatusMessage = $"Connected as '{profile.Name}'.";

            if (!string.IsNullOrWhiteSpace(profile.StorageAccountName))
            {
                _ = BlobStorage.LoadContainersCommand.ExecuteAsync(null);
            }

            if (!string.IsNullOrWhiteSpace(profile.ServiceBusNamespace))
            {
                _ = ServiceBus.LoadTopicsAndQueuesCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            BlobStorage.Deactivate();
            ServiceBus.Deactivate();
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
        IsConnected = false;
        ConnectedConnectionName = null;
        StatusMessage = "Disconnected.";
    }
}
