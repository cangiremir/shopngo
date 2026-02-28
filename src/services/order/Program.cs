using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopNGo.BuildingBlocks.Core;
using ShopNGo.BuildingBlocks.Hosting;
using ShopNGo.BuildingBlocks.Messaging;
using ShopNGo.BuildingBlocks.Persistence;
using ShopNGo.BuildingBlocks.Web;
using ShopNGo.OrderService.Api;
using ShopNGo.OrderService.Application;
using ShopNGo.OrderService.Consumers;
using ShopNGo.OrderService.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddServiceObservability(builder.Configuration, "ShopNGo.OrderService");

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddHealthChecks().AddDbContextCheck<OrderDbContext>("postgres", tags: ["ready"]);

builder.Services.AddRabbitMqMessaging(
    builder.Configuration,
    registration =>
    {
        registration.AddConsumer<StockReservedConsumer>();
        registration.AddConsumer<StockRejectedConsumer>();
    },
    (rabbit, context, options) =>
    {
        rabbit.ConfigureSubscriptionEndpoint<StockReservedConsumer>(
            context,
            options,
            queueName: "order.stock-reserved",
            routingKey: ShopNGo.Contracts.EventRoutingKeys.StockReserved);

        rabbit.ConfigureSubscriptionEndpoint<StockRejectedConsumer>(
            context,
            options,
            queueName: "order.stock-rejected",
            routingKey: ShopNGo.Contracts.EventRoutingKeys.StockRejected);
    });
builder.Services.AddOptions<OutboxDispatcherOptions>()
    .Bind(builder.Configuration.GetSection(OutboxDispatcherOptions.SectionName));

builder.Services.AddScoped<OrderApplicationService>();
builder.Services.AddScoped<InboxProcessor<OrderDbContext>>();
builder.Services.AddScoped<IValidator<CreateOrderRequest>, CreateOrderRequestValidator>();
builder.Services.AddHostedService<OrderOutboxDispatcher>();

var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();

await app.InitializeDatabaseAsync<OrderDbContext>();
app.MapDefaultHealthEndpoints();

app.MapPost("/orders", async (
    HttpContext http,
    CreateOrderRequest request,
    IValidator<CreateOrderRequest> validator,
    OrderApplicationService service,
    CancellationToken ct) =>
{
    var annotationErrors = request.ValidateWithDataAnnotations();
    if (annotationErrors is not null)
    {
        return Results.ValidationProblem(annotationErrors);
    }

    var fluentResult = await validator.ValidateAsync(request, ct);
    if (!fluentResult.IsValid)
    {
        var errors = fluentResult.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray());
        return Results.ValidationProblem(errors);
    }

    try
    {
        var order = await service.CreateOrderAsync(request, http.GetCorrelationId(), http.GetTraceParent(), ct);
        return Results.Created($"/orders/{order.Id}", OrderDto.From(order));
    }
    catch (BusinessRuleException ex)
    {
        return Results.Problem(statusCode: 400, title: ex.ErrorCode, detail: ex.Message);
    }
});

app.MapGet("/orders/{id:guid}", async (Guid id, OrderDbContext db, CancellationToken ct) =>
{
    var order = await db.Orders
        .Include(x => x.Items)
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == id, ct);

    return order is null
        ? Results.NotFound()
        : Results.Ok(OrderDto.From(order));
});

app.Run();
