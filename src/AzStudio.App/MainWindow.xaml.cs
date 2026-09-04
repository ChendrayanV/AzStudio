using System.Windows;
using System.Windows.Input;
using AzStudio.App.ViewModels;
using AzStudio.App.Views;
using AzStudio.Core.ServiceBus;

namespace AzStudio.App;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void NewConnection_Click(object sender, RoutedEventArgs e)
    {
        var result = ConnectionEditorWindow.Edit(this, null);
        if (result is not null)
        {
            ViewModel.AddOrUpdate(result);
            ConnectionsList.SelectedItem = result;
        }
    }

    private void EditConnection_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedConnection is null)
        {
            MessageBox.Show(this, "Select a connection to edit.", "AzStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = ConnectionEditorWindow.Edit(this, ViewModel.SelectedConnection);
        if (result is not null)
        {
            ViewModel.AddOrUpdate(result);
        }
    }

    private void DeleteConnection_Click(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedConnection;
        if (selected is null)
        {
            MessageBox.Show(this, "Select a connection to delete.", "AzStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this, $"Delete connection '{selected.Name}'?", "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.Yes)
        {
            ViewModel.Remove(selected);
        }
    }

    private void PeekedMessagesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PeekedMessagesListView.SelectedItem is PeekedMessageInfo message)
        {
            MessageDetailWindow.Show(this, message, ViewModel.ServiceBus.PeekedMessagesSource);
        }
    }

    private void StorageNav_Checked(object sender, RoutedEventArgs e)
    {
        // XAML sets StorageNavRadio's IsChecked="True" as its initial state, which fires this
        // handler during InitializeComponent() — before DataContext is assigned in the
        // constructor below — so guard rather than assume ViewModel is available yet.
        if (DataContext is not MainViewModel vm) return;
        vm.IsStorageSelected = true;
        vm.IsServiceBusSelected = false;
        vm.IsKeyVaultSelected = false;
    }

    private void ServiceBusNav_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.IsServiceBusSelected = true;
        vm.IsStorageSelected = false;
        vm.IsKeyVaultSelected = false;
    }

    private void KeyVaultNav_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.IsKeyVaultSelected = true;
        vm.IsStorageSelected = false;
        vm.IsServiceBusSelected = false;
    }
}
