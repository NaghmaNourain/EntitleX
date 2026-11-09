using Npgsql;
using Testcontainers.PostgreSql;

namespace EntitleX.Api.Tests;

internal static class TestPostgres
{
    public const string EnvVar = "ENTITLEX_TEST_POSTGRES";

    public static bool IsConfigured
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            {
                return true;
            }

            if (IsCiEnvironment)
            {
                return false;
            }

            return DockerAvailability.IsAvailable;
        }
    }

    private static bool IsCiEnvironment =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

    public static async Task<TestPostgresLease> AcquireAsync(
        string databasePrefix = "entitlex_api",
        CancellationToken cancellationToken = default)
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var dbName = $"{Sanitize(databasePrefix)}_{Guid.NewGuid():N}";
            await EnsureDatabaseExistsAsync(fromEnv, dbName, cancellationToken);
            var builder = new NpgsqlConnectionStringBuilder(fromEnv) { Database = dbName };
            return new TestPostgresLease(builder.ConnectionString, container: null);
        }

        if (IsCiEnvironment || !DockerAvailability.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Neither {EnvVar} nor a local Docker daemon is available for API Postgres integration tests.");
        }

        var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase(databasePrefix)
            .WithUsername("entitlex")
            .WithPassword("entitlex")
            .Build();

        await container.StartAsync(cancellationToken);
        return new TestPostgresLease(container.GetConnectionString(), container);
    }

    private static string Sanitize(string value) =>
        new(value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray());

    private static async Task EnsureDatabaseExistsAsync(
        string adminConnectionString,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
            // Already created.
        }
    }
}

internal sealed class TestPostgresLease(string connectionString, PostgreSqlContainer? container)
    : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}
