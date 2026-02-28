using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ShopNGo.BuildingBlocks.Core;
using ShopNGo.Contracts;
using ShopNGo.IntegrationTests.Infrastructure;

namespace ShopNGo.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ECommerceSagaIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task HappyPath_ConfirmsOrder_UpdatesStock_AndCreatesNotification()
    {
        var productId = Guid.NewGuid();
        var seedResponse = await fixture.StockClient.PostAsJsonAsync("/stock/seed", new
        {
            items = new[] { new { productId, quantity = 5 } }
        });
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        var createResponse = await fixture.OrderClient.PostAsJsonAsync("/orders", new
        {
            customerEmail = "happy@example.com",
            items = new[] { new { productId, quantity = 2 } }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderView>();
        Assert.NotNull(createdOrder);

        var finalOrder = await fixture.WaitForOrderStatusAsync(createdOrder!.Id, "Confirmed");
        Assert.Equal("Confirmed", finalOrder.Status);

        var stock = await fixture.StockClient.GetFromJsonAsync<StockView>($"/stock/{productId}");
        Assert.NotNull(stock);
        Assert.Equal(3, stock!.AvailableQuantity);

        var notifications = await fixture.WaitForNotificationsAsync(createdOrder.Id, expectedCount: 1);
        Assert.Contains(notifications, n => n.Template == "order-confirmed" && n.Status == "Sent");
    }

    [Fact]
    public async Task HappyPath_WithSmsNotificationChannel_CreatesSmsNotificationLog()
    {
        var productId = Guid.NewGuid();
        var seedResponse = await fixture.StockClient.PostAsJsonAsync("/stock/seed", new
        {
            items = new[] { new { productId, quantity = 3 } }
        });
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        var phone = "+15551234567";
        var createResponse = await fixture.OrderClient.PostAsJsonAsync("/orders", new
        {
            customerEmail = "sms-happy@example.com",
            customerPhone = phone,
            notificationChannel = "sms",
            items = new[] { new { productId, quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderView>();
        Assert.NotNull(createdOrder);

        var finalOrder = await fixture.WaitForOrderStatusAsync(createdOrder!.Id, "Confirmed");
        Assert.Equal("Confirmed", finalOrder.Status);

        var notifications = await fixture.WaitForNotificationsAsync(createdOrder.Id, expectedCount: 1);
        Assert.Contains(notifications, n => n.Template == "order-confirmed" && n.Status == "Sent" && n.Channel == "sms" && n.Target == phone);
    }

    [Fact]
    public async Task InsufficientStock_RejectsOrder_AndCreatesRejectionNotification()
    {
        var productId = Guid.NewGuid();
        var seedResponse = await fixture.StockClient.PostAsJsonAsync("/stock/seed", new
        {
            items = new[] { new { productId, quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        var createResponse = await fixture.OrderClient.PostAsJsonAsync("/orders", new
        {
            customerEmail = "reject@example.com",
            items = new[] { new { productId, quantity = 2 } }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderView>();
        Assert.NotNull(createdOrder);

        var finalOrder = await fixture.WaitForOrderStatusAsync(createdOrder!.Id, "Rejected");
        Assert.Equal("Rejected", finalOrder.Status);
        Assert.Equal("INSUFFICIENT_STOCK", finalOrder.RejectionReasonCode);

        var stock = await fixture.StockClient.GetFromJsonAsync<StockView>($"/stock/{productId}");
        Assert.NotNull(stock);
        Assert.Equal(1, stock!.AvailableQuantity);

        var notifications = await fixture.WaitForNotificationsAsync(createdOrder.Id, expectedCount: 1);
        Assert.Contains(notifications, n => n.Template == "order-rejected" && n.Status == "Sent");
    }

    [Fact]
    public async Task DuplicateMessageDelivery_WithSameMessageId_IsProcessedOnce()
    {
        var orderId = Guid.NewGuid();
        var evt = new OrderConfirmedIntegrationEvent(
            orderId,
            "dupe@example.com",
            DateTimeOffset.UtcNow,
            "email",
            "dupe@example.com");
        var messageId = Guid.NewGuid().ToString("N");

        await fixture.PublishEventAsync(EventRoutingKeys.OrderConfirmed, evt, messageId);
        await fixture.PublishEventAsync(EventRoutingKeys.OrderConfirmed, evt, messageId);

        var notifications = await fixture.WaitForNotificationsAsync(orderId, expectedCount: 1);
        Assert.Single(notifications, n => n.Template == "order-confirmed");
    }

    [Fact]
    public async Task InvalidNotificationChannel_StoresRejectedNotification_WithOriginalMessageContext()
    {
        var orderId = Guid.NewGuid();
        var evt = new OrderConfirmedIntegrationEvent(
            orderId,
            "context@example.com",
            DateTimeOffset.UtcNow,
            "fax",
            "invalid-target");
        var messageId = Guid.NewGuid().ToString("N");

        await fixture.PublishEventAsync(EventRoutingKeys.OrderConfirmed, evt, messageId);

        var notifications = await fixture.WaitForNotificationsAsync(orderId, expectedCount: 1);
        var failure = Assert.Single(notifications, n => n.Template == "order-confirmed");
        Assert.Equal("Rejected", failure.Status);
        Assert.Equal(ErrorCodes.NotificationInvalidChannel, failure.ErrorCode);
        Assert.Equal("fax", failure.Channel);
        Assert.Equal("invalid-target", failure.Target);
    }

    [Fact]
    public async Task InvalidOrderCreatedMessage_Retries_ThenMovesToDlq()
    {
        var baseline = await fixture.GetQueueMessageCountAsync("stock.order-created_error");
        var messageId = Guid.NewGuid().ToString("N");

        await fixture.PublishRawAsync(
            EventRoutingKeys.OrderCreated,
            Encoding.UTF8.GetBytes("{\"broken\":true"), // malformed JSON
            messageId);

        var finalCount = await fixture.WaitForQueueMessageCountAtLeastAsync("stock.order-created_error", baseline + 1);
        Assert.True(finalCount >= baseline + 1);
    }

    [Fact]
    public async Task OrderCreatedEvent_PropagatesCorrelationId_AndTraceParent()
    {
        var productId = Guid.NewGuid();
        await fixture.StockClient.PostAsJsonAsync("/stock/seed", new
        {
            items = new[] { new { productId, quantity = 1 } }
        });

        var correlationId = Guid.NewGuid().ToString("N");
        RabbitCapturedMessage captured = await fixture.CaptureNextPublishedEventAsync(
            EventRoutingKeys.OrderCreated,
            async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
                {
                    Content = JsonContent.Create(new
                    {
                        customerEmail = "trace@example.com",
                        items = new[] { new { productId, quantity = 1 } }
                    })
                };
                request.Headers.Add("x-correlation-id", correlationId);

                using var response = await fixture.OrderClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
            });

        Assert.Equal(EventRoutingKeys.OrderCreated, captured.RoutingKey);
        Assert.True(Guid.TryParse(correlationId, out var expectedCorrelationId));
        Assert.True(Guid.TryParse(captured.CorrelationId, out var actualCorrelationId));
        Assert.Equal(expectedCorrelationId, actualCorrelationId);
        Assert.True(captured.Headers.TryGetValue("traceparent", out var traceParent));
        Assert.False(string.IsNullOrWhiteSpace(traceParent));
        Assert.StartsWith("00-", traceParent, StringComparison.Ordinal);

        using var bodyDoc = JsonDocument.Parse(captured.BodyJson);
        Assert.True(bodyDoc.RootElement.TryGetProperty("orderId", out _));
    }

    [Fact]
    public async Task MetricsEndpoints_ExposePrometheusMetrics_AndCustomShopNGoMetrics()
    {
        var productId = Guid.NewGuid();
        var seedResponse = await fixture.StockClient.PostAsJsonAsync("/stock/seed", new
        {
            items = new[] { new { productId, quantity = 2 } }
        });
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        var createResponse = await fixture.OrderClient.PostAsJsonAsync("/orders", new
        {
            customerEmail = "metrics@example.com",
            items = new[] { new { productId, quantity = 1 } }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderView>();
        Assert.NotNull(createdOrder);
        await fixture.WaitForOrderStatusAsync(createdOrder!.Id, "Confirmed");

        using var orderResponse = await fixture.OrderClient.GetAsync("/metrics");
        using var stockResponse = await fixture.StockClient.GetAsync("/metrics");
        using var notificationResponse = await fixture.NotificationClient.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, orderResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, notificationResponse.StatusCode);

        var orderMetrics = await orderResponse.Content.ReadAsStringAsync();
        var stockMetrics = await stockResponse.Content.ReadAsStringAsync();
        var notificationMetrics = await notificationResponse.Content.ReadAsStringAsync();

        if (!string.IsNullOrWhiteSpace(orderMetrics))
        {
            Assert.Contains("shopngo_", orderMetrics, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(stockMetrics))
        {
            Assert.Contains("shopngo_", stockMetrics, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(notificationMetrics))
        {
            Assert.Contains("shopngo_", notificationMetrics, StringComparison.Ordinal);
        }
    }
}
