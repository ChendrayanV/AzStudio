using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Azure.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzStudio.Core.Storage;
using AzStudio.App.Utilities;
using AzStudio.App.Views;
using Microsoft.Win32;

namespace AzStudio.App.ViewModels;

public partial class BlobStorageTabViewModel : ObservableObject
{
    private TokenCredential? _credential;
    private BlobStorageService? _service;
    private string? _connectedAccountName;

    [ObservableProperty]
    private string storageAccountName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadContainersCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadBlobsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateContainerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteContainerCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteBlobCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Not connected.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyErrorDetailsCommand))]
    private bool hasError;

    private string? _errorDetails;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadBlobsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteContainerCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand))]
    private BlobContainerInfo? selectedContainer;

    public ObservableCollection<BlobContainerInfo> Containers { get; } = new();

    public ObservableCollection<BlobItemInfo> Blobs { get; } = new();

    /// <summary>
    /// Called once per Connect with the credential for the active connection profile.
    /// This does NOT require a storage account name — the account is chosen (and can be
    /// changed) directly on this tab via StorageAccountName + LoadContainers.
    /// </summary>
    public void Activate(TokenCredential credential, string defaultAccountName)
    {
        _credential = credential;
        _service = null;
        _connectedAccountName = null;
        StorageAccountName = defaultAccountName;
        Containers.Clear();
        Blobs.Clear();
        StatusMessage = "Connected. Enter a storage account name and load containers.";
        ClearError();
        NotifyServiceCommands();
    }

    public void Deactivate()
    {
        _credential = null;
        _service = null;
        _connectedAccountName = null;
        Containers.Clear();
        Blobs.Clear();
        StatusMessage = "Not connected.";
        ClearError();
        NotifyServiceCommands();
    }

    // CommunityToolkit's [RelayCommand] only re-evaluates a button's enabled state when
    // explicitly told to (or when an [ObservableProperty] with [NotifyCanExecuteChangedFor]
    // changes). CanRunService/CanRunOnSelected* below depend on plain fields (_credential,
    // _service), so every place that mutates those fields must call this afterward —
    // otherwise the Load/Upload/Delete buttons stay stuck at whatever state they started in.
    private void NotifyServiceCommands()
    {
        LoadContainersCommand.NotifyCanExecuteChanged();
        CreateContainerCommand.NotifyCanExecuteChanged();
        LoadBlobsCommand.NotifyCanExecuteChanged();
        DeleteContainerCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        DeleteBlobCommand.NotifyCanExecuteChanged();
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
    /// Builds (or rebuilds, if the account name field has changed) the BlobStorageService
    /// for whatever account name is currently typed into StorageAccountName.
    /// </summary>
    private BlobStorageService? EnsureService()
    {
        if (_credential is null) return null;

        var name = StorageAccountName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            StatusMessage = "Enter a storage account name first.";
            return null;
        }

        if (_service is null || !string.Equals(_connectedAccountName, name, StringComparison.OrdinalIgnoreCase))
        {
            _service = new BlobStorageService(name, _credential);
            _connectedAccountName = name;
            NotifyServiceCommands();
        }

        return _service;
    }

    [RelayCommand(CanExecute = nameof(CanRunService))]
    private async Task LoadContainersAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        IsBusy = true;
        try
        {
            StatusMessage = $"Loading containers in '{_connectedAccountName}'...";
            var containers = await service.ListContainersAsync();
            Containers.Clear();
            foreach (var c in containers) Containers.Add(c);
            Blobs.Clear();
            StatusMessage = $"{Containers.Count} container(s) loaded from '{_connectedAccountName}'.";
            ClearError();
        }
        catch (Exception ex)
        {
            SetError("Load containers", ex, $"storage account '{_connectedAccountName}'");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedContainerChanged(BlobContainerInfo? value)
    {
        if (value is not null)
        {
            _ = LoadBlobsAsync();
        }
    }

    private bool CanRunOnSelectedContainer() => _service is not null && !IsBusy && SelectedContainer is not null;

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedContainer))]
    private async Task LoadBlobsAsync()
    {
        if (_service is null || SelectedContainer is null) return;
        IsBusy = true;
        try
        {
            StatusMessage = $"Loading blobs in '{SelectedContainer.Name}'...";
            var blobs = await _service.ListBlobsAsync(SelectedContainer.Name);
            Blobs.Clear();
            foreach (var b in blobs) Blobs.Add(b);
            StatusMessage = $"{Blobs.Count} blob(s) loaded.";
            ClearError();
        }
        catch (Exception ex)
        {
            SetError("Load blobs", ex, $"container '{SelectedContainer.Name}'");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunService))]
    private async Task CreateContainerAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        var name = TextInputWindow.Prompt(Application.Current.MainWindow, "New Container", "Container name (lowercase, digits, hyphens):");
        if (string.IsNullOrWhiteSpace(name)) return;

        IsBusy = true;
        try
        {
            await service.CreateContainerAsync(name.Trim());
            StatusMessage = $"Container '{name}' created.";
            await LoadContainersAsync();
        }
        catch (Exception ex)
        {
            SetError("Create container", ex, $"container '{name}'");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedContainer))]
    private async Task DeleteContainerAsync()
    {
        if (_service is null || SelectedContainer is null) return;
        var confirm = MessageBox.Show(
            $"Delete container '{SelectedContainer.Name}' and all of its blobs? This cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var containerName = SelectedContainer.Name;
            await _service.DeleteContainerAsync(containerName);
            StatusMessage = $"Container '{containerName}' deleted.";
            await LoadContainersAsync();
        }
        catch (Exception ex)
        {
            SetError("Delete container", ex, $"container '{SelectedContainer.Name}'");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedContainer))]
    private async Task UploadAsync()
    {
        if (_service is null || SelectedContainer is null) return;
        var dialog = new OpenFileDialog { Title = "Select file to upload", Multiselect = false };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var blobName = Path.GetFileName(dialog.FileName);
            await using var stream = File.OpenRead(dialog.FileName);
            await _service.UploadAsync(SelectedContainer.Name, blobName, stream);
            StatusMessage = $"Uploaded '{blobName}'.";
            await LoadBlobsAsync();
        }
        catch (Exception ex)
        {
            SetError("Upload blob", ex, $"blob '{Path.GetFileName(dialog.FileName)}'");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRunOnSelectedBlob() => _service is not null && !IsBusy && SelectedBlob is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteBlobCommand))]
    private BlobItemInfo? selectedBlob;

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedBlob))]
    private async Task DownloadAsync()
    {
        if (_service is null || SelectedContainer is null || SelectedBlob is null) return;
        var dialog = new SaveFileDialog { Title = "Save blob as", FileName = SelectedBlob.Name };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            await using var stream = File.Create(dialog.FileName);
            await _service.DownloadAsync(SelectedContainer.Name, SelectedBlob.Name, stream);
            StatusMessage = $"Downloaded '{SelectedBlob.Name}' to {dialog.FileName}.";
            ClearError();
        }
        catch (Exception ex)
        {
            SetError("Download blob", ex, $"blob '{SelectedBlob.Name}'");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedBlob))]
    private async Task DeleteBlobAsync()
    {
        if (_service is null || SelectedContainer is null || SelectedBlob is null) return;
        var confirm = MessageBox.Show($"Delete blob '{SelectedBlob.Name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var blobName = SelectedBlob.Name;
            await _service.DeleteBlobAsync(SelectedContainer.Name, blobName);
            StatusMessage = $"Deleted '{blobName}'.";
            await LoadBlobsAsync();
        }
        catch (Exception ex)
        {
            SetError("Delete blob", ex, $"blob '{SelectedBlob.Name}'");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
