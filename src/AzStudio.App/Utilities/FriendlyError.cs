using System.Net.Sockets;
using System.Text;
using Azure;

namespace AzStudio.App.Utilities;

/// <summary>
/// Turns exceptions from Azure SDK calls into short, human-readable status messages,
/// plus a verbose block (exception chain + timestamp) suitable for copying into a
/// support request to a subscription/tenant admin.
/// </summary>
public static class FriendlyError
{
    public static string Summarize(Exception ex, string resourceLabel)
    {
        var socket = UnwrapSocketException(ex);
        if (socket is not null)
        {
            return socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain
                ? $"Couldn't find '{resourceLabel}'. Check the name and that the resource exists."
                : $"Couldn't reach '{resourceLabel}'. Check your network connection and try again.";
        }

        if (ex is RequestFailedException rfe)
        {
            if (rfe.ErrorCode == "AuthorizationPermissionMismatch")
            {
                return $"Access denied to '{resourceLabel}'. Your Azure AD account has no data-plane role here — " +
                       "seeing it in the Portal doesn't count, that often uses the storage account key instead. " +
                       "Ask an admin for the 'Storage Blob Data Reader' (or Contributor) role on this storage account.";
            }

            return rfe.Status switch
            {
                401 or 403 => $"Access denied to '{resourceLabel}'. Ask an admin to grant you permission.",
                404 => $"'{resourceLabel}' was not found.",
                429 => "Too many requests right now. Wait a moment and try again.",
                >= 500 => $"'{resourceLabel}' is temporarily unavailable. Try again shortly.",
                _ => $"'{resourceLabel}' returned an error ({rfe.Status}). See details for more info."
            };
        }

        return $"Something went wrong with '{resourceLabel}'. See details for more info.";
    }

    public static string BuildDetails(string operation, Exception ex, string? resourceLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("AzStudio error report");
        sb.AppendLine($"Time: {DateTimeOffset.Now:u}");
        sb.AppendLine($"Operation: {operation}");
        if (!string.IsNullOrEmpty(resourceLabel))
        {
            sb.AppendLine($"Resource: {resourceLabel}");
        }
        sb.AppendLine();
        sb.Append(ex);
        return sb.ToString();
    }

    private static SocketException? UnwrapSocketException(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is SocketException socketException)
            {
                return socketException;
            }
            ex = ex.InnerException;
        }
        return null;
    }
}
