using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using PaymentGateway.Application.Locking;

namespace PaymentGateway.Infrastructure.Locking;

public sealed class PostgresAdvisoryLockProvider(
    IConfiguration configuration,
    ILogger<PostgresAdvisoryLockProvider> logger) : IDistributedLockProvider
{
    private readonly ConcurrentDictionary<string, byte> _localLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDistributedLock?> TryAcquireLockAsync(string resourceName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var connectionString = configuration.GetConnectionString("PaymentDb");
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return TryAcquireLocalLock(resourceName);
        }

        try
        {
            var key = ComputeAdvisoryLockKey(resourceName);
            var lockConnectionString = BuildLockConnectionString(connectionString);
            var connection = new NpgsqlConnection(lockConnectionString);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@key);";
            cmd.Parameters.AddWithValue("key", key);

            var result = (bool?)await cmd.ExecuteScalarAsync(ct);
            if (result == true)
            {
                logger.LogDebug("Acquired PostgreSQL advisory lock for {ResourceName} (key {Key})", resourceName, key);
                return new PostgresAdvisoryLock(resourceName, key, connection, logger);
            }

            await connection.DisposeAsync();
            logger.LogDebug("Could not acquire PostgreSQL advisory lock for {ResourceName}; already held by another session", resourceName);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to acquire advisory lock for {ResourceName} via PostgreSQL; falling back to local lock", resourceName);
            return TryAcquireLocalLock(resourceName);
        }
    }

    private IDistributedLock? TryAcquireLocalLock(string resourceName)
    {
        if (_localLocks.TryAdd(resourceName, 0))
        {
            return new LocalAdvisoryLock(resourceName, () => _localLocks.TryRemove(resourceName, out _));
        }

        return null;
    }

    internal static long ComputeAdvisoryLockKey(string resourceName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resourceName));
        return BitConverter.ToInt64(hash, 0);
    }

    internal static string BuildLockConnectionString(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                ApplicationName = "pg_advisory_locks",
                Pooling = true,
                MaxPoolSize = 10,
                MinPoolSize = 0,
                ConnectionIdleLifetime = 15
            };
            return builder.ConnectionString;
        }
        catch
        {
            return connectionString;
        }
    }

    private sealed class PostgresAdvisoryLock(
        string resourceName,
        long key,
        NpgsqlConnection connection,
        ILogger logger) : IDistributedLock
    {
        private int _disposed;

        public string ResourceName => resourceName;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    await using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT pg_advisory_unlock(@key);";
                    cmd.Parameters.AddWithValue("key", key);
                    await cmd.ExecuteScalarAsync();
                    logger.LogDebug("Released PostgreSQL advisory lock for {ResourceName} (key {Key})", resourceName, key);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error releasing PostgreSQL advisory lock for {ResourceName}", resourceName);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    private sealed class LocalAdvisoryLock(string resourceName, Action releaseAction) : IDistributedLock
    {
        private int _disposed;

        public string ResourceName => resourceName;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                releaseAction();
            }

            return ValueTask.CompletedTask;
        }
    }
}
