namespace ShopNGo.IntegrationTests.Infrastructure;

public sealed class GuardrailFailOpenIntegrationFixture : IntegrationTestFixture
{
    protected override Dictionary<string, string?> BuildCommonSettings()
    {
        var settings = base.BuildCommonSettings();

        settings["StockGuardrail:Enabled"] = "true";
        settings["StockGuardrail:FailOpen"] = "true";
        // Unreachable Redis endpoint for deterministic guardrail unavailable path.
        settings["StockGuardrail:Configuration"] = "127.0.0.1:1";

        return settings;
    }
}
