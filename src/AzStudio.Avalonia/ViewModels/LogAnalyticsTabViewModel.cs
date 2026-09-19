using System.Data;
using Azure.Core;
using Azure.Monitor.Query;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzStudio.Core.LogAnalytics;
using AzStudio.Avalonia.Utilities;

namespace AzStudio.Avalonia.ViewModels;

/// <summary>One entry in the time-range picker. A null Factory means "Custom range" —
/// RunQueryAsync builds the QueryTimeRange from CustomStart/CustomEnd instead.</summary>
public sealed class TimeRangeOption
{
    public required string Label { get; init; }
    public Func<QueryTimeRange>? Factory { get; init; }

    public override string ToString() => Label;
}

/// <summary>
/// Port of AzStudio.App's LogAnalyticsTabViewModel. The one real behavioral delta:
/// Avalonia's DatePicker.SelectedDate is DateTimeOffset?, not WPF's DateTime?, so
/// CustomStart/CustomEnd are typed accordingly here — everything else is identical.
/// </summary>
public partial class LogAnalyticsTabViewModel : ObservableObject
{
    private static readonly IReadOnlyList<TimeRangeOption> PresetTimeRanges =
    [
        new() { Label = "Last 30 minutes", Factory = () => new QueryTimeRange(TimeSpan.FromMinutes(30)) },
        new() { Label = "Last hour", Factory = () => new QueryTimeRange(TimeSpan.FromHours(1)) },
        new() { Label = "Last 4 hours", Factory = () => new QueryTimeRange(TimeSpan.FromHours(4)) },
        new() { Label = "Last 24 hours", Factory = () => new QueryTimeRange(TimeSpan.FromHours(24)) },
        new() { Label = "Last 7 days", Factory = () => new QueryTimeRange(TimeSpan.FromDays(7)) },
        new() { Label = "Last 30 days", Factory = () => new QueryTimeRange(TimeSpan.FromDays(30)) },
        new() { Label = "Custom range...", Factory = null }
    ];

    private TokenCredential? _credential;
    private LogAnalyticsService? _service;
    private string? _connectedWorkspaceId;

    [ObservableProperty]
    private string workspaceId = string.Empty;

    [ObservableProperty]
    private string kqlQuery = string.Empty;

    public IReadOnlyList<TimeRangeOption> TimeRangeOptions => PresetTimeRanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomRangeSelected))]
    private TimeRangeOption selectedTimeRange = PresetTimeRanges[3]; // "Last 24 hours"

    public bool IsCustomRangeSelected => SelectedTimeRange.Factory is null;

    [ObservableProperty]
    private DateTimeOffset? customStart = DateTimeOffset.Now.Date.AddDays(-1);

    [ObservableProperty]
    private DateTimeOffset? customEnd = DateTimeOffset.Now.Date;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunQueryCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Not connected.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyErrorDetailsCommand))]
    private bool hasError;

    private string? _errorDetails;

    [ObservableProperty]
    private DataView? results;

    public void Activate(TokenCredential credential, string defaultWorkspaceId)
    {
        _credential = credential;
        _service = null;
        _connectedWorkspaceId = null;
        WorkspaceId = defaultWorkspaceId;
        Results = null;
        StatusMessage = "Connected. Enter a workspace ID, a KQL query, and a time range.";
        ClearError();
        NotifyServiceCommands();
    }

    public void Deactivate()
    {
        _credential = null;
        _service = null;
        _connectedWorkspaceId = null;
        Results = null;
        StatusMessage = "Not connected.";
        ClearError();
        NotifyServiceCommands();
    }

    private void NotifyServiceCommands()
    {
        RunQueryCommand.NotifyCanExecuteChanged();
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

    private LogAnalyticsService? EnsureService()
    {
        if (_credential is null) return null;

        var id = WorkspaceId.Trim();
        if (string.IsNullOrEmpty(id))
        {
            StatusMessage = "Enter a workspace ID first.";
            return null;
        }

        if (_service is null || !string.Equals(_connectedWorkspaceId, id, StringComparison.OrdinalIgnoreCase))
        {
            _service = new LogAnalyticsService(id, _credential);
            _connectedWorkspaceId = id;
            NotifyServiceCommands();
        }

        return _service;
    }

    private QueryTimeRange? BuildTimeRange()
    {
        if (SelectedTimeRange.Factory is not null)
        {
            return SelectedTimeRange.Factory();
        }

        if (CustomStart is null || CustomEnd is null)
        {
            StatusMessage = "Pick a start and end date for the custom range.";
            return null;
        }

        var start = CustomStart.Value.Date;
        var end = CustomEnd.Value.Date.AddDays(1).AddTicks(-1);
        return new QueryTimeRange(new DateTimeOffset(start, CustomStart.Value.Offset), new DateTimeOffset(end, CustomEnd.Value.Offset));
    }

    [RelayCommand(CanExecute = nameof(CanRunService))]
    private async Task RunQueryAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        var query = KqlQuery.Trim();
        if (string.IsNullOrEmpty(query))
        {
            StatusMessage = "Enter a KQL query first.";
            return;
        }

        var timeRange = BuildTimeRange();
        if (timeRange is null) return;

        IsBusy = true;
        try
        {
            StatusMessage = $"Running query against '{_connectedWorkspaceId}'...";
            var result = await service.QueryAsync(query, timeRange.Value);
            Results = ToDataView(result);
            StatusMessage = $"{result.Rows.Count} row(s), {result.Columns.Count} column(s) from '{_connectedWorkspaceId}'.";
            ClearError();
        }
        catch (Exception ex)
        {
            Results = null;
            SetError("Run query", ex, $"workspace '{_connectedWorkspaceId}'");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static DataView ToDataView(LogQueryResult result)
    {
        var table = new DataTable();
        foreach (var column in result.Columns)
        {
            table.Columns.Add(column, typeof(string));
        }

        foreach (var row in result.Rows)
        {
            table.Rows.Add(row.ToArray());
        }

        return table.DefaultView;
    }
}
