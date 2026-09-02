using System.Windows;
using AzStudio.Core.Models;
using AzStudio.Core.Security;

namespace AzStudio.App.Views;

public partial class ConnectionEditorWindow : Window
{
    private readonly ConnectionProfile _editing;
    private readonly bool _isNew;

    public ConnectionProfile? Result { get; private set; }

    public ConnectionEditorWindow(ConnectionProfile? existing)
    {
        InitializeComponent();

        _isNew = existing is null;
        _editing = existing ?? new ConnectionProfile();

        NameBox.Text = _editing.Name;
        TenantIdBox.Text = _editing.TenantId;
        ClientIdBox.Text = _editing.ClientId;
        StorageAccountBox.Text = _editing.StorageAccountName;
        ServiceBusNamespaceBox.Text = _editing.ServiceBusNamespace;

        if (_editing.AuthType == AuthType.ServicePrincipal)
        {
            ServicePrincipalRadio.IsChecked = true;
        }
        else
        {
            InteractiveRadio.IsChecked = true;
        }

        if (!_isNew && _editing.AuthType == AuthType.ServicePrincipal && !string.IsNullOrEmpty(_editing.ProtectedClientSecret))
        {
            SecretHintText.Visibility = Visibility.Visible;
        }

        Title = _isNew ? "New Connection" : "Edit Connection";
        UpdateAuthTypeUi();
    }

    private void AuthType_Checked(object sender, RoutedEventArgs e) => UpdateAuthTypeUi();

    private void UpdateAuthTypeUi()
    {
        if (SecretPanel is null) return;

        var isServicePrincipal = ServicePrincipalRadio.IsChecked == true;
        SecretPanel.Visibility = isServicePrincipal ? Visibility.Visible : Visibility.Collapsed;
        ClientIdLabel.Text = isServicePrincipal ? "Client (App) ID" : "Client (App) ID (optional)";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Connection name is required.");
            return;
        }

        var authType = ServicePrincipalRadio.IsChecked == true ? AuthType.ServicePrincipal : AuthType.InteractiveUser;
        var tenantId = TenantIdBox.Text.Trim();
        var clientId = ClientIdBox.Text.Trim();

        string protectedSecret = _editing.ProtectedClientSecret;

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

            var enteredSecret = ClientSecretBox.Password;
            if (!string.IsNullOrEmpty(enteredSecret))
            {
                protectedSecret = SecretProtector.Protect(enteredSecret);
            }
            else if (string.IsNullOrEmpty(protectedSecret))
            {
                ShowError("Client secret is required for Service Principal authentication.");
                return;
            }
        }
        else
        {
            protectedSecret = string.Empty;
        }

        var storageAccount = StorageAccountBox.Text.Trim();
        var serviceBusNamespace = ServiceBusNamespaceBox.Text.Trim();

        Result = new ConnectionProfile
        {
            Id = _editing.Id,
            Name = name,
            AuthType = authType,
            TenantId = tenantId,
            ClientId = clientId,
            ProtectedClientSecret = protectedSecret,
            StorageAccountName = storageAccount,
            ServiceBusNamespace = serviceBusNamespace
        };

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static ConnectionProfile? Edit(Window? owner, ConnectionProfile? existing)
    {
        var window = new ConnectionEditorWindow(existing) { Owner = owner };
        return window.ShowDialog() == true ? window.Result : null;
    }
}
