namespace ShopNGo.StockService.Application;

public sealed class RedisGuardrailOptions
{
    public const string SectionName = "StockGuardrail";

    public bool Enabled { get; set; }
    public bool FailOpen { get; set; } = true;
    public string Configuration { get; set; } = "localhost:6379";
    public string KeyPrefix { get; set; } = "shopngo:stock";

    public int MaxInFlightPerSku { get; set; } = 32;
    public int InFlightTtlSeconds { get; set; } = 30;

    public int HotSkuWindowSeconds { get; set; } = 60;
    public int HotSkuEnterThreshold { get; set; } = 100;
    public int HotSkuExitThreshold { get; set; } = 60;
    public int HotSkuTtlSeconds { get; set; } = 300;
}

