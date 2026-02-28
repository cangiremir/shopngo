using MassTransit;
using Microsoft.EntityFrameworkCore;
using ShopNGo.BuildingBlocks.Hosting;
using ShopNGo.BuildingBlocks.Messaging;
using ShopNGo.BuildingBlocks.Persistence;
using ShopNGo.BuildingBlocks.Web;
using ShopNGo.NotificationService.Application;
using ShopNGo.NotificationService.Consumers;
using ShopNGo.NotificationService.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddServiceObservability(builder.Configuration, "ShopNGo.NotificationService");

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddHealthChecks().AddDbContextCheck<NotificationDbContext>("postgres", tags: ["ready"]);

builder.Services.AddRabbitMqMessaging(
    builder.Configuration,
    registration =>
    {
        registration.AddConsumer<OrderConfirmedNotificationConsumer>();
        registration.AddConsumer<OrderRejectedNotificationConsumer>();
    },
    (rabbit, context, options) =>
    {
        rabbit.ConfigureSubscriptionEndpoint<OrderConfirmedNotificationConsumer>(
            context,
            options,
            queueName: "notification.order-confirmed",
            routingKey: ShopNGo.Contracts.EventRoutingKeys.OrderConfirmed);

        rabbit.ConfigureSubscriptionEndpoint<OrderRejectedNotificationConsumer>(
            context,
            options,
            queueName: "notification.order-rejected",
            routingKey: ShopNGo.Contracts.EventRoutingKeys.OrderRejected);
    });

builder.Services.AddScoped<NotificationDispatchService>();
builder.Services.AddScoped<INotificationChannelHandler, EmailNotificationChannelHandler>();
builder.Services.AddScoped<INotificationChannelHandler, SmsNotificationChannelHandler>();
builder.Services.AddScoped<InboxProcessor<NotificationDbContext>>();

var app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
await app.InitializeDatabaseAsync<NotificationDbContext>();
app.MapDefaultHealthEndpoints();

app.MapGet("/notifications", async (NotificationDbContext db, CancellationToken ct) =>
{
    var rows = await db.NotificationLogs
        .AsNoTracking()
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(100)
        .ToListAsync(ct);

    return Results.Ok(rows.Select(x => new
    {
        x.Id,
        x.OrderId,
        x.Target,
        x.Channel,
        x.Template,
        x.Status,
        x.ErrorCode,
        x.CreatedAtUtc
    }));
});

app.Run();
