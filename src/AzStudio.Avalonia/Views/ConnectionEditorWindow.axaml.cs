using Avalonia.Controls;
using Avalonia.Interactivity;
using AzStudio.Core.Models;

namespace AzStudio.Avalonia.Views;

public partial class ConnectionEditorWindow : Window
{
    private readonly ConnectionProfile _editing;

    public ConnectionProfile? Result { get; private set; }

    public ConnectionEditorWindow()
    {
        InitializeComponent();
        _editing = new ConnectionProfile();
    }

    private ConnectionEditorWindow(ConnectionProfile? existing) : this()
    {
        _editing = existing ?? new ConnectionProfile();

        NameBox.Text = _editing.Name;
        TenantIdBox.Text = _editing.TenantId;
        ClientIdBox.Text = _editing.ClientId;
        StorageAccountBox.Text = _editing.StorageAccountName;
        ServiceBusNamespaceBox.Text = _editing.ServiceBusNamespace;
        KeyVaultNameBox.Text = _editing.KeyVaultName;
        LogAnalyticsWorkspaceIdBox.Text = _editing.LogAnalyticsWorkspaceId;

        if (_editing.AuthType == AuthType.ServicePrincipal)
        {
            ServicePrincipalRadio.IsChecked = true;
        }
        else
        {
            InteractiveRadio.IsChecked = true;
        }

        Title = existing is null ? "New Connection" : "Edit Connection";
        UpdateAuthTypeUi();
    }

    private void AuthType_Checked(object? sender, RoutedEventArgs e) => UpdateAuthTypeUi();

    private void UpdateAuthTypeUi()
    {
        if (SecretNoticeText is null) return;

        var isServicePrincipal = ServicePrincipalRadio.IsChecked == true;
        SecretNoticeText.IsVisible = isServicePrincipal;
        ClientIdLabel.IsVisible = isServicePrincipal;
        ClientIdBox.IsVisible = isServicePrincipal;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;

        var name = NameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Connection name is required.");
            return;
        }

        var authType = ServicePrincipalRadio.IsChecked == true ? AuthType.ServicePrincipal : AuthType.InteractiveUser;
        var tenantId = TenantIdBox.Text?.Trim() ?? string.Empty;
        var clientId = ClientIdBox.Text?.Trim() ?? string.Empty;

        if (authType == AuthType.ServicePrincipal)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                ShowError("Tenant ID is required for Service Principal authentication.");
                return;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                ShowError("Client ID is required for Service Principal authentication.");
                return;
            }
        }

        Result = new ConnectionProfile
        {
            Id = _editing.Id,
            Name = name,
            AuthType = authType,
            TenantId = tenantId,
            ClientId = clientId,
            StorageAccountName = StorageAccountBox.Text?.Trim() ?? string.Empty,
            ServiceBusNamespace = ServiceBusNamespaceBox.Text?.Trim() ?? string.Empty,
            KeyVaultName = KeyVaultNameBox.Text?.Trim() ?? string.Empty,
            LogAnalyticsWorkspaceId = LogAnalyticsWorkspaceIdBox.Text?.Trim() ?? string.Empty
        };

        Close(true);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    public static async Task<ConnectionProfile?> EditAsync(Window? owner, ConnectionProfile? existing)
    {
        var window = new ConnectionEditorWindow(existing);
        if (owner is null) return null;
        var saved = await window.ShowDialog<bool>(owner);
        return saved ? window.Result : null;
    }
}
