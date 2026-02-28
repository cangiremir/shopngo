namespace ShopNGo.BuildingBlocks.Messaging;

public sealed class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string EventsExchange { get; set; } = "ecommerce.events";
    public string RetryExchange { get; set; } = "ecommerce.retry";
    public string DlqExchange { get; set; } = "ecommerce.dlq";
    public ushort PrefetchCount { get; set; } = 8;
    public int RetryDelayMs { get; set; } = 5000;
    public int MaxRetries { get; set; } = 3;
    public bool PublisherConfirmsEnabled { get; set; } = true;
    public int PublisherConfirmTimeoutMs { get; set; } = 5000;
    public RabbitMqThroughputOptions Throughput { get; set; } = new();
}

public sealed class RabbitMqThroughputOptions
{
    public string? Level { get; set; }
    public int? PrefetchOverride { get; set; }
    public int? ConcurrentMessageLimit { get; set; }
    public Dictionary<string, RabbitMqEndpointThroughputOptions> Endpoints { get; set; } = new();
}

public sealed class RabbitMqEndpointThroughputOptions
{
    public string? Level { get; set; }
    public int? PrefetchOverride { get; set; }
    public int? ConcurrentMessageLimit { get; set; }
}
