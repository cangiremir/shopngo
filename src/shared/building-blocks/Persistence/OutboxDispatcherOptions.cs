namespace ShopNGo.BuildingBlocks.Persistence;

public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "OutboxDispatcher";

    public int PollIntervalMs { get; set; } = 2000;
    public int BatchSize { get; set; } = 50;
}
