using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AzStudio.Core.ServiceBus;
using AzStudio.Avalonia.ViewModels;
using AzStudio.Avalonia.Views;

namespace AzStudio.Avalonia;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel { OwnerWindow = this };
        DataContext = vm;
    }

    private async void NewConnection_Click(object? sender, RoutedEventArgs e)
    {
        var result = await ConnectionEditorWindow.EditAsync(this, null);
        if (result is not null)
        {
            ViewModel.AddOrUpdate(result);
            ConnectionsList.SelectedItem = result;
        }
    }

    private async void EditConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedConnection is null) return;

        var result = await ConnectionEditorWindow.EditAsync(this, ViewModel.SelectedConnection);
        if (result is not null)
        {
            ViewModel.AddOrUpdate(result);
        }
    }

    private void DeleteConnection_Click(object? sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedConnection;
        if (selected is null) return;
        ViewModel.Remove(selected);
    }

    private void StorageNav_Checked(object? sender, RoutedEventArgs e)
    {
        if (StorageNavRadio.IsChecked != true) return;
        ViewModel.IsStorageSelected = true;
        ViewModel.IsServiceBusSelected = false;
        ViewModel.IsKeyVaultSelected = false;
        ViewModel.IsLogAnalyticsSelected = false;
    }

    private void ServiceBusNav_Checked(object? sender, RoutedEventArgs e)
    {
        if (ServiceBusNavRadio.IsChecked != true) return;
        ViewModel.IsServiceBusSelected = true;
        ViewModel.IsStorageSelected = false;
        ViewModel.IsKeyVaultSelected = false;
        ViewModel.IsLogAnalyticsSelected = false;
    }

    private void KeyVaultNav_Checked(object? sender, RoutedEventArgs e)
    {
        if (KeyVaultNavRadio.IsChecked != true) return;
        ViewModel.IsKeyVaultSelected = true;
        ViewModel.IsStorageSelected = false;
        ViewModel.IsServiceBusSelected = false;
        ViewModel.IsLogAnalyticsSelected = false;
    }

    private void LogAnalyticsNav_Checked(object? sender, RoutedEventArgs e)
    {
        if (LogAnalyticsNavRadio.IsChecked != true) return;
        ViewModel.IsLogAnalyticsSelected = true;
        ViewModel.IsStorageSelected = false;
        ViewModel.IsServiceBusSelected = false;
        ViewModel.IsKeyVaultSelected = false;
    }

    private void PeekedMessagesGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: PeekedMessageInfo message })
        {
            MessageDetailWindow.Show(this, message, ViewModel.ServiceBus.PeekedMessagesSource);
        }
    }
}
