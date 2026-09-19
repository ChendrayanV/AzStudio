using System.Data;
using System.Windows;
using Azure.Core;
using Azure.Monitor.Query;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzStudio.Core.LogAnalytics;
using AzStudio.App.Utilities;

namespace AzStudio.App.ViewModels;

/// <summary>One entry in the time-range picker. A null Factory means "Custom range" —
/// RunQueryAsync builds the QueryTimeRange from CustomStart/CustomEnd instead.</summary>
public sealed class TimeRangeOption
{
    public required string Label { get; init; }
    public Func<QueryTimeRange>? Factory { get; init; }

    public override string ToString() => Label;
}

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
    private DateTime? customStart = DateTime.Today.AddDays(-1);

    [ObservableProperty]
    private DateTime? customEnd = DateTime.Today;

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

    /// <summary>
    /// Called once per Connect with the credential for the active connection profile.
    /// This does NOT require a workspace ID — the workspace is chosen (and can be
    /// changed) directly on this tab via WorkspaceId + Run Query.
    /// </summary>
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

    // CommunityToolkit's [RelayCommand] only re-evaluates a button's enabled state when
    // explicitly told to (or when an [ObservableProperty] with [NotifyCanExecuteChangedFor]
    // changes). CanRunService depends on plain fields (_credential), so every place that
    // mutates it must call this afterward — otherwise the Run Query button stays stuck
    // disabled even after a successful connect.
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
    private void CopyErrorDetails()
    {
        if (_errorDetails is null) return;
        Clipboard.SetText(_errorDetails);
        StatusMessage = "Error details copied to clipboard — share them with your admin.";
    }

    /// <summary>
    /// Builds (or rebuilds, if the workspace ID field has changed) the LogAnalyticsService
    /// for whatever workspace ID is currently typed into WorkspaceId.
    /// </summary>
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

        // End date is inclusive of the whole day, matching how a human picks "through
        // this date" rather than "up to midnight at its start". DateTimeOffset(DateTime)
        // uses the machine's local offset, which is what an analyst reading logs on
        // their own workstation expects "today" to mean.
        var start = new DateTimeOffset(CustomStart.Value.Date);
        var end = new DateTimeOffset(CustomEnd.Value.Date.AddDays(1).AddTicks(-1));
        return new QueryTimeRange(start, end);
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
