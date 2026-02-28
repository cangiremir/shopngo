namespace ShopNGo.StockService.Application;

public enum StockConcurrencyMode
{
    Pessimistic,
    Optimistic,
    Hybrid
}

public sealed class StockConcurrencyOptions
{
    public const string SectionName = "StockConcurrency";

    public string Mode { get; set; } = nameof(StockConcurrencyMode.Pessimistic);
    public HybridConcurrencyOptions Hybrid { get; set; } = new();
}

public sealed class HybridConcurrencyOptions
{
    public bool Enabled { get; set; } = true;
    public int CanaryPercent { get; set; } = 15;
    public int LowStockThreshold { get; set; } = 10;
    public bool ConservativeOnGuardrailFailure { get; set; } = true;
}
