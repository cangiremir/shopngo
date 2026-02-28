using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ShopNGo.BuildingBlocks.Core;
using ShopNGo.BuildingBlocks.Hosting;
using ShopNGo.BuildingBlocks.Messaging;
using ShopNGo.BuildingBlocks.Persistence;
using ShopNGo.BuildingBlocks.Web;
using ShopNGo.StockService.Api;
using ShopNGo.StockService.Application;
using ShopNGo.StockService.Consumers;
using ShopNGo.StockService.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddServiceObservability(builder.Configuration, "ShopNGo.StockService");

builder.Services.AddDbContext<StockDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddHealthChecks().AddDbContextCheck<StockDbContext>("postgres", tags: ["ready"]);

builder.Services.AddRabbitMqMessaging(
    builder.Configuration,
    registration =>
    {
        registration.AddConsumer<OrderCreatedConsumer>();
    },
    (rabbit, context, options) =>
    {
        rabbit.ConfigureSubscriptionEndpoint<OrderCreatedConsumer>(
            context,
            options,
            queueName: "stock.order-created",
            routingKey: ShopNGo.Contracts.EventRoutingKeys.OrderCreated);
    });
builder.Services.AddOptions<OutboxDispatcherOptions>()
    .Bind(builder.Configuration.GetSection(OutboxDispatcherOptions.SectionName));
builder.Services.AddOptions<StockConcurrencyOptions>()
    .Bind(builder.Configuration.GetSection(StockConcurrencyOptions.SectionName));
builder.Services.AddOptions<RedisGuardrailOptions>()
    .Bind(builder.Configuration.GetSection(RedisGuardrailOptions.SectionName));

builder.Services.AddScoped<InboxProcessor<StockDbContext>>();
builder.Services.AddScoped<StockApplicationService>();
builder.Services.AddScoped<IStockReservationStore, StockReservationStore>();
builder.Services.AddSingleton<IStockGuardrail, RedisStockGuardrail>();
builder.Services.AddScoped<IValidator<SeedStockRequest>, SeedStockRequestValidator>();
builder.Services.AddHostedService<StockOutboxDispatcher>();

var app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
await app.InitializeDatabaseAsync<StockDbContext>();
app.MapDefaultHealthEndpoints();

app.MapPost("/stock/seed", async Task<Results<Ok, ValidationProblem, ProblemHttpResult>> (
    HttpContext http,
    SeedStockRequest request,
    IValidator<SeedStockRequest> validator,
    StockApplicationService service,
    CancellationToken ct) =>
{
    var annotationErrors = request.ValidateWithDataAnnotations();
    if (annotationErrors is not null)
    {
        return TypedResults.ValidationProblem(annotationErrors);
    }

    var fv = await validator.ValidateAsync(request, ct);
    if (!fv.IsValid)
    {
        return TypedResults.ValidationProblem(fv.Errors.GroupBy(x => x.PropertyName).ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray()));
    }

    try
    {
        await service.SeedStockAsync(request, http.GetCorrelationId(), http.GetTraceParent(), ct);
        return TypedResults.Ok();
    }
    catch (BusinessRuleException ex)
    {
        return TypedResults.Problem(title: ex.ErrorCode, detail: ex.Message, statusCode: 400);
    }
});

app.MapGet("/stock/{productId:guid}", async (Guid productId, StockDbContext db, CancellationToken ct) =>
{
    var item = await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == productId, ct);
    return item is null ? Results.NotFound() : Results.Ok(new { item.ProductId, item.AvailableQuantity, item.UpdatedAtUtc });
});

app.Run();
