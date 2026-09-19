namespace AzStudio.Core.LogAnalytics;

/// <summary>
/// The result of one KQL query, in a shape that doesn't depend on the Azure Monitor
/// Query SDK's own result types (so the App layer can render it — e.g. into a
/// DataTable for a DataGrid — without depending on Azure.Monitor.Query directly).
/// Every cell is already rendered to its display string, since a log table's column
/// set (and each column's type) is different for every query and there's no fixed
/// schema to bind against otherwise.
/// </summary>
public record LogQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);
