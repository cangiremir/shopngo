using System.Net;
using System.Net.Http.Json;
using ShopNGo.BuildingBlocks.Core;
using ShopNGo.IntegrationTests.Infrastructure;

namespace ShopNGo.IntegrationTests;

[Collection(GuardrailIntegrationCollection.Name)]
public sealed class GuardrailIntegrationTests(GuardrailFailClosedIntegrationFixture fixture)
{
    [Fact]
    public async Task GuardrailDeny_IsHandledAsBusinessRejection_WithoutErrorQueueGrowth()
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
            customerEmail = "guardrail@example.com",
            items = new[] { new { productId, quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderView>();
        Assert.NotNull(createdOrder);

        var finalOrder = await fixture.WaitForOrderStatusAsync(createdOrder!.Id, "Rejected");
        Assert.Equal("Rejected", finalOrder.Status);
        Assert.Equal(ErrorCodes.StockGuardrailUnavailable, finalOrder.RejectionReasonCode);

        await Task.Delay(500);
        var finalErrorQueue = await fixture.GetQueueMessageCountAsync(errorQueueName);
        Assert.Equal(baselineErrorQueue, finalErrorQueue);
    }
}
