using System.Data.Common;

namespace BlazorAgentChat.Abstractions;

/// <summary>
/// Abstracts database connection creation so DbAgentRunner stays provider-agnostic.
/// Implement this with your preferred driver (Microsoft.Data.SqlClient, Npgsql, etc.)
/// and register it in Program.cs to replace NoopDbConnectionFactory.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>Returns an open connection for the given connection string.</summary>
    Task<DbConnection> OpenConnectionAsync(string connectionString, CancellationToken ct = default);
}
