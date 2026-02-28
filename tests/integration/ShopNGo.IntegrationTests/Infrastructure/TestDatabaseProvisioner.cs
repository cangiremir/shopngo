using Npgsql;

namespace ShopNGo.IntegrationTests.Infrastructure;

internal sealed record ProvisionedServiceDatabases(
    string OrderConnectionString,
    string StockConnectionString,
    string NotificationConnectionString);

internal static class TestDatabaseProvisioner
{
    public static async Task<ProvisionedServiceDatabases> CreateServiceDatabasesAsync(string postgresConnectionString)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var orderDatabaseName = $"order_it_{suffix}";
        var stockDatabaseName = $"stock_it_{suffix}";
        var notificationDatabaseName = $"notification_it_{suffix}";

        await CreateDatabasesAsync(
            postgresConnectionString,
            [orderDatabaseName, stockDatabaseName, notificationDatabaseName]);

        return new ProvisionedServiceDatabases(
            BuildConnectionString(postgresConnectionString, orderDatabaseName),
            BuildConnectionString(postgresConnectionString, stockDatabaseName),
            BuildConnectionString(postgresConnectionString, notificationDatabaseName));
    }

    private static async Task CreateDatabasesAsync(string postgresConnectionString, IReadOnlyCollection<string> databaseNames)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(postgresConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        foreach (var databaseName in databaseNames)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""CREATE DATABASE "{databaseName}" """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string BuildConnectionString(string postgresConnectionString, string databaseName)
        => new NpgsqlConnectionStringBuilder(postgresConnectionString)
        {
            Database = databaseName
        }.ConnectionString;
}
