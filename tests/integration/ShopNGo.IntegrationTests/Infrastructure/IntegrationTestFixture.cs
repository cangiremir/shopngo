using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ShopNGo.BuildingBlocks.Serialization;
using ShopNGo.Contracts;
using ShopNGo.NotificationService;
using ShopNGo.OrderService;
using ShopNGo.StockService;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace ShopNGo.IntegrationTests.Infrastructure;

public class IntegrationTestFixture : IAsyncLifetime, IAsyncDisposable
{
    private const string EventsExchange = "ecommerce.events";

    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:3.13-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private ConfiguredWebApplicationFactory<OrderServiceAssemblyMarker>? _orderFactory;
    private ConfiguredWebApplicationFactory<StockServiceAssemblyMarker>? _stockFactory;
    private ConfiguredWebApplicationFactory<NotificationServiceAssemblyMarker>? _notificationFactory;
    private RabbitMqTestClient _rabbitClient = null!;

    public HttpClient OrderClient { get; private set; } = null!;
    public HttpClient StockClient { get; private set; } = null!;
    public HttpClient NotificationClient { get; private set; } = null!;

    public string RabbitHost { get; private set; } = string.Empty;
    public int RabbitPort { get; private set; }
    public string RabbitUserName { get; private set; } = string.Empty;
    public string RabbitPassword { get; private set; } = string.Empty;

    public string OrderDbConnectionString { get; private set; } = string.Empty;
    public string StockDbConnectionString { get; private set; } = string.Empty;
    public string NotificationDbConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgresContainer.StartAsync(), _rabbitMqContainer.StartAsync());

        ConfigureRabbitSettings();
        _rabbitClient = CreateRabbitClient();
        await CreateServiceDatabasesAsync();
        StartServices();

        await Task.WhenAll(
            IntegrationPolling.WaitForHealthAsync(OrderClient, "/health/ready"),
            IntegrationPolling.WaitForHealthAsync(StockClient, "/health/ready"),
            IntegrationPolling.WaitForHealthAsync(NotificationClient, "/health/ready"));
    }

    public async Task DisposeAsync()
    {
        OrderClient?.Dispose();
        StockClient?.Dispose();
        NotificationClient?.Dispose();
        _orderFactory?.Dispose();
        _stockFactory?.Dispose();
        _notificationFactory?.Dispose();

        await _rabbitMqContainer.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync() => await DisposeAsync();

    private void ConfigureRabbitSettings()
    {
        var rabbitUri = new Uri(_rabbitMqContainer.GetConnectionString());
        RabbitHost = rabbitUri.Host;
        RabbitPort = rabbitUri.Port;

        var userInfo = rabbitUri.UserInfo.Split(':', 2);
        RabbitUserName = Uri.UnescapeDataString(userInfo[0]);
        RabbitPassword = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
    }

    private async Task CreateServiceDatabasesAsync()
    {
        var databases = await TestDatabaseProvisioner.CreateServiceDatabasesAsync(_postgresContainer.GetConnectionString());
        OrderDbConnectionString = databases.OrderConnectionString;
        StockDbConnectionString = databases.StockConnectionString;
        NotificationDbConnectionString = databases.NotificationConnectionString;
    }

    private void StartServices()
    {
        var commonRabbit = BuildCommonSettings();

        _orderFactory = new ConfiguredWebApplicationFactory<OrderServiceAssemblyMarker>(new Dictionary<string, string?>(commonRabbit)
        {
            ["ConnectionStrings:Postgres"] = OrderDbConnectionString
        });
        _stockFactory = new ConfiguredWebApplicationFactory<StockServiceAssemblyMarker>(new Dictionary<string, string?>(commonRabbit)
        {
            ["ConnectionStrings:Postgres"] = StockDbConnectionString
        });
        _notificationFactory = new ConfiguredWebApplicationFactory<NotificationServiceAssemblyMarker>(new Dictionary<string, string?>(commonRabbit)
        {
            ["ConnectionStrings:Postgres"] = NotificationDbConnectionString
        });

        OrderClient = _orderFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        StockClient = _stockFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        NotificationClient = _notificationFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    protected virtual Dictionary<string, string?> BuildCommonSettings()
    {
        return new Dictionary<string, string?>
        {
            ["RabbitMq:HostName"] = RabbitHost,
            ["RabbitMq:Port"] = RabbitPort.ToString(),
            ["RabbitMq:UserName"] = RabbitUserName,
            ["RabbitMq:Password"] = RabbitPassword,
            ["RabbitMq:VirtualHost"] = "/",
            ["RabbitMq:EventsExchange"] = EventsExchange,
            ["RabbitMq:RetryExchange"] = "ecommerce.retry",
            ["RabbitMq:DlqExchange"] = "ecommerce.dlq",
            // Speed up retry/DLQ tests.
            ["RabbitMq:RetryDelayMs"] = "200",
            ["RabbitMq:MaxRetries"] = "1",
            ["OpenTelemetry:Console:Enabled"] = "false",
            ["OpenTelemetry:Metrics:Prometheus:Enabled"] = "true",
            ["Logging:LogLevel:Default"] = "Warning"
        };
    }

    private RabbitMqTestClient CreateRabbitClient()
        => new(
            RabbitHost,
            RabbitPort,
            RabbitUserName,
            RabbitPassword,
            virtualHost: "/",
            eventsExchange: EventsExchange);

    public async Task WaitForHealthAsync(HttpClient client, string path, int timeoutSeconds = 30)
    {
        await IntegrationPolling.WaitForHealthAsync(client, path, timeoutSeconds);
    }

    public async Task<OrderView> WaitForOrderStatusAsync(Guid orderId, string expectedStatus, int timeoutSeconds = 30)
    {
        OrderView? last = null;
        await IntegrationPolling.WaitUntilAsync(
            async () =>
            {
                last = await OrderClient.GetFromJsonAsync<OrderView>($"/orders/{orderId}");
                return last is not null && string.Equals(last.Status, expectedStatus, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(timeoutSeconds));

        return last ?? throw new InvalidOperationException($"Order {orderId} not found.");
    }

    public async Task<List<NotificationRow>> WaitForNotificationsAsync(Guid orderId, int expectedCount, int timeoutSeconds = 30)
    {
        List<NotificationRow> found = [];
        await IntegrationPolling.WaitUntilAsync(
            async () =>
            {
                var rows = await NotificationClient.GetFromJsonAsync<List<NotificationRow>>("/notifications") ?? [];
                found = rows.Where(x => x.OrderId == orderId).ToList();
                return found.Count >= expectedCount;
            },
            TimeSpan.FromSeconds(timeoutSeconds));
        return found;
    }

    public async Task PublishEventAsync<T>(string routingKey, T payload, string messageId, string? correlationId = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonDefaults.Options);
        await PublishRawAsync(routingKey, body, messageId, correlationId);
    }

    public Task PublishRawAsync(string routingKey, byte[] body, string messageId, string? correlationId = null)
        => _rabbitClient.PublishRawAsync(routingKey, body, messageId, correlationId);

    public Task<uint> GetQueueMessageCountAsync(string queueName)
        => _rabbitClient.GetQueueMessageCountAsync(queueName);

    public async Task<uint> WaitForQueueMessageCountAtLeastAsync(string queueName, uint expectedMinimum, int timeoutSeconds = 20)
    {
        uint count = 0;
        await IntegrationPolling.WaitUntilAsync(
            async () =>
            {
                count = await GetQueueMessageCountAsync(queueName);
                return count >= expectedMinimum;
            },
            TimeSpan.FromSeconds(timeoutSeconds));

        return count;
    }

    public async Task<RabbitCapturedMessage> CaptureNextPublishedEventAsync(string routingKey, Func<Task> trigger, int timeoutSeconds = 15)
        => await _rabbitClient.CaptureNextPublishedEventAsync(routingKey, trigger, timeoutSeconds);
}

public sealed record OrderView(
    Guid Id,
    string CustomerEmail,
    string Status,
    string? RejectionReasonCode,
    string? RejectionReason,
    IReadOnlyCollection<OrderItemView> Items,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OrderItemView(Guid ProductId, int Quantity);

public sealed record NotificationRow(
    Guid Id,
    Guid OrderId,
    string Target,
    string Channel,
    string Template,
    string Status,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc);

public sealed record StockView(Guid ProductId, int AvailableQuantity, DateTimeOffset UpdatedAtUtc);

public sealed record RabbitCapturedMessage(
    string RoutingKey,
    string? MessageId,
    string? CorrelationId,
    IReadOnlyDictionary<string, string> Headers,
    string BodyJson);
