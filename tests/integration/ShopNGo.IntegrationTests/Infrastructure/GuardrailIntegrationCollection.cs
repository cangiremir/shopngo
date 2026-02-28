namespace ShopNGo.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class GuardrailIntegrationCollection : ICollectionFixture<GuardrailFailClosedIntegrationFixture>
{
    public const string Name = "guardrail-integration";
}
