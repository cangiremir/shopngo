namespace ShopNGo.BuildingBlocks.Hosting;

public enum DatabaseStartupMode
{
    None = 0,
    Migrate = 1
}

public sealed class DatabaseStartupOptions
{
    public const string SectionName = "DatabaseStartup";

    public DatabaseStartupMode Mode { get; set; } = DatabaseStartupMode.None;
    public int MaxRetryCount { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 2;
}
