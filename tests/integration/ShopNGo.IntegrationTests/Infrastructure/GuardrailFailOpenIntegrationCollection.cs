namespace ShopNGo.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class GuardrailFailOpenIntegrationCollection : ICollectionFixture<GuardrailFailOpenIntegrationFixture>
{
    public const string Name = "guardrail-fail-open-integration";
}
