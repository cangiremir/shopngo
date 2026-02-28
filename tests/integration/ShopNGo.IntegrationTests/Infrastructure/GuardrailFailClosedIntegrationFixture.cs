namespace ShopNGo.IntegrationTests.Infrastructure;

public sealed class GuardrailFailClosedIntegrationFixture : IntegrationTestFixture
{
    protected override Dictionary<string, string?> BuildCommonSettings()
    {
        var settings = base.BuildCommonSettings();

        settings["StockGuardrail:Enabled"] = "true";
        settings["StockGuardrail:FailOpen"] = "false";
        // Unreachable Redis endpoint for deterministic guardrail deny path.
        settings["StockGuardrail:Configuration"] = "127.0.0.1:1";

        return settings;
    }
}
