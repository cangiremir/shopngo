using System.Net;
using System.Net.Http.Json;
using ShopNGo.IntegrationTests.Infrastructure;

namespace ShopNGo.IntegrationTests;

[Collection(GuardrailFailOpenIntegrationCollection.Name)]
public sealed class GuardrailFailOpenIntegrationTests(GuardrailFailOpenIntegrationFixture fixture)
{
    [Fact]
    public async Task GuardrailUnavailable_WithFailOpen_ContinuesWithStockFlow()
    {
        var productId = Guid.NewGuid();
        var errorQueueName = "stock.order-created_error";
        var baselineErrorQueue = await fixture.GetQueueMessageCountAsync(errorQueueName);

        var seedResponse = await fixture.StockClient.PostAsJsonAsync("/stock/seed", new
        {
            items = new[] { new { productId, quantity = 5 } }
        });
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        var createResponse = await fixture.OrderClient.PostAsJsonAsync("/orders", new
        {
            customerEmail = "guardrail-failopen@example.com",
            items = new[] { new { productId, quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderView>();
        Assert.NotNull(createdOrder);

        var finalOrder = await fixture.WaitForOrderStatusAsync(createdOrder!.Id, "Confirmed");
        Assert.Equal("Confirmed", finalOrder.Status);
        Assert.Null(finalOrder.RejectionReasonCode);

        await Task.Delay(500);
        var finalErrorQueue = await fixture.GetQueueMessageCountAsync(errorQueueName);
        Assert.Equal(baselineErrorQueue, finalErrorQueue);
    }
}
