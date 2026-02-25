using System.Data.Common;
using BlazorAgentChat.Abstractions;

namespace BlazorAgentChat.Infrastructure.Database;

/// <summary>
/// Placeholder factory registered by default. Throws at runtime with a clear message
/// so the app starts successfully but fails loudly when a DB agent is actually invoked.
///
/// To activate database agents, replace this registration in Program.cs with a real
/// implementation. Example using Microsoft.Data.SqlClient:
///
///   Add NuGet: Microsoft.Data.SqlClient
///
///   public sealed class SqlServerConnectionFactory : IDbConnectionFactory
///   {
///       public async Task&lt;DbConnection&gt; OpenConnectionAsync(string connectionString, CancellationToken ct)
///       {
///           var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
///           await conn.OpenAsync(ct);
///           return conn;
///       }
///   }
///
///   Then in Program.cs swap:
///   builder.Services.AddSingleton&lt;IDbConnectionFactory, SqlServerConnectionFactory&gt;();
///
/// For PostgreSQL use Npgsql.NpgsqlConnection in the same pattern.
/// </summary>
public sealed class NoopDbConnectionFactory : IDbConnectionFactory
{
    public Task<DbConnection> OpenConnectionAsync(string connectionString, CancellationToken ct = default)
        => throw new NotImplementedException(
            "No IDbConnectionFactory has been configured. " +
            "Register a real implementation in Program.cs — see NoopDbConnectionFactory.cs for examples.");
}
