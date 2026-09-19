using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

namespace AzStudio.Core.LogAnalytics;

/// <summary>
/// Thin wrapper around LogsQueryClient scoped to one Log Analytics workspace,
/// authenticated with whatever TokenCredential CredentialFactory produced for the
/// active connection profile. Unlike Storage/Service Bus/Key Vault, a workspace is
/// addressed by its Workspace ID (a GUID, found on the workspace's Overview page in
/// the portal) rather than a friendly name — Log Analytics' query API has no
/// name-based endpoint.
/// </summary>
public class LogAnalyticsService
{
    private readonly LogsQueryClient _client;
    private readonly string _workspaceId;

    public LogAnalyticsService(string workspaceId, TokenCredential credential)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        }

        _workspaceId = workspaceId;
        _client = new LogsQueryClient(credential);
    }

    public async Task<LogQueryResult> QueryAsync(string kqlQuery, QueryTimeRange timeRange, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(kqlQuery))
        {
            throw new ArgumentException("A KQL query is required.", nameof(kqlQuery));
        }

        var response = await _client.QueryWorkspaceAsync(_workspaceId, kqlQuery, timeRange, cancellationToken: ct);
        var result = response.Value;

        // A query can fail with a 200 response (e.g. a KQL syntax error) rather than a
        // thrown RequestFailedException, so this has to be checked explicitly.
        if (result.Status == LogsQueryResultStatus.Failure)
        {
            throw new InvalidOperationException(result.Error?.Message ?? "The query failed.");
        }

        var table = result.Table;
        var columns = table.Columns.Select(c => c.Name).ToList();

        var rows = new List<IReadOnlyList<string>>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var cells = new List<string>(columns.Count);
            for (var i = 0; i < columns.Count; i++)
            {
                // Matches how every other panel in the app renders a value into a grid
                // cell: the object's own ToString() (dates, numbers, GUIDs, bools all
                // render sensibly this way; nothing here needs per-type formatting).
                cells.Add(row[i]?.ToString() ?? string.Empty);
            }
            rows.Add(cells);
        }

        return new LogQueryResult(columns, rows);
    }
}
