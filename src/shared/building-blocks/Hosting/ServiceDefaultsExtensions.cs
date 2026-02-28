using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MassTransit;
using MassTransit.RabbitMqTransport;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ShopNGo.BuildingBlocks.Hosting;
using ShopNGo.BuildingBlocks.Metrics;
using ShopNGo.BuildingBlocks.Messaging;

namespace ShopNGo.BuildingBlocks.Hosting;

public static class ServiceDefaultsExtensions
{
    public static IServiceCollection AddRabbitMqMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null,
        Action<IRabbitMqBusFactoryConfigurator, IBusRegistrationContext, RabbitMqOptions>? configureEndpoints = null)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMq"));
        services.AddMassTransit(registration =>
        {
            configureConsumers?.Invoke(registration);

            registration.UsingRabbitMq((context, rabbit) =>
            {
                var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                var virtualHost = string.IsNullOrWhiteSpace(options.VirtualHost) ? "/" : options.VirtualHost;
                var hostUri = virtualHost == "/"
                    ? new Uri($"rabbitmq://{options.HostName}:{options.Port}/")
                    : new Uri($"rabbitmq://{options.HostName}:{options.Port}/{virtualHost.TrimStart('/')}");

                rabbit.Host(hostUri, host =>
                {
                    host.Username(options.UserName);
                    host.Password(options.Password);
                });

                var globalThroughput = options.ResolveGlobalThroughput();
                rabbit.PrefetchCount = globalThroughput.PrefetchCount;
                rabbit.UseRawJsonSerializer();
                rabbit.UseRawJsonDeserializer(isDefault: true);

                configureEndpoints?.Invoke(rabbit, context, options);
            });
        });

        services.AddScoped<IIntegrationEventPublisher, MassTransitEventPublisher>();
        return services;
    }

    public static IServiceCollection AddServiceObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var options = configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>()
                      ?? new OpenTelemetryOptions();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddSource(MessagingDiagnostics.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .ConfigureTracingExporters(options))
            .WithMetrics(metrics => metrics
                .AddMeter(MessagingMetrics.MeterName, BusinessMetrics.MeterName)
                .ConfigureMetricInstrumentations(options)
                .ConfigureMetricExporters(options));
        return services;
    }

    private static TracerProviderBuilder ConfigureTracingExporters(this TracerProviderBuilder tracing, OpenTelemetryOptions options)
    {
        if (options.Console.Enabled)
        {
            tracing.AddConsoleExporter();
        }

        if (options.Otlp.Enabled && !string.IsNullOrWhiteSpace(options.Otlp.Endpoint))
        {
            tracing.AddOtlpExporter(exporter =>
            {
                var protocol = OtlpExportProtocol.Grpc;
                if (Enum.TryParse<OtlpExportProtocol>(options.Otlp.Protocol, ignoreCase: true, out var parsedProtocol))
                {
                    protocol = parsedProtocol;
                }

                exporter.Protocol = protocol;
                exporter.Endpoint = ResolveOtlpEndpoint(options.Otlp.Endpoint, protocol);
            });
        }

        return tracing;
    }

    private static Uri ResolveOtlpEndpoint(string endpoint, OtlpExportProtocol protocol)
    {
        var uri = new Uri(endpoint, UriKind.Absolute);
        if (protocol != OtlpExportProtocol.HttpProtobuf)
        {
            return uri;
        }

        if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            return new Uri(uri, "v1/traces");
        }

        return uri;
    }

    private static MeterProviderBuilder ConfigureMetricInstrumentations(this MeterProviderBuilder metrics, OpenTelemetryOptions options)
    {
        if (options.Metrics.HttpServer.Enabled)
        {
            metrics.AddAspNetCoreInstrumentation();
        }

        if (options.Metrics.HttpClient.Enabled)
        {
            metrics.AddHttpClientInstrumentation();
        }

        if (options.Metrics.Runtime.Enabled)
        {
            metrics.AddRuntimeInstrumentation();
        }

        if (options.Metrics.Process.Enabled)
        {
            metrics.AddProcessInstrumentation();
        }

        return metrics;
    }

    private static MeterProviderBuilder ConfigureMetricExporters(this MeterProviderBuilder metrics, OpenTelemetryOptions options)
    {
        if (options.Metrics.Prometheus.Enabled)
        {
            metrics.AddPrometheusExporter();
        }

        return metrics;
    }

    public static async Task<WebApplication> InitializeDatabaseAsync<TDbContext>(this WebApplication app)
        where TDbContext : DbContext
    {
        var options = app.Configuration.GetSection(DatabaseStartupOptions.SectionName).Get<DatabaseStartupOptions>()
                      ?? new DatabaseStartupOptions();

        if (options.Mode == DatabaseStartupMode.None)
        {
            var skipLogger = app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("DatabaseStartup");
            skipLogger.LogInformation(
                "Skipping database startup actions for {DbContext}. Mode={Mode}",
                typeof(TDbContext).Name,
                options.Mode);
            return app;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger($"DatabaseStartup<{typeof(TDbContext).Name}>");

        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                logger.LogInformation(
                    "Applying database migrations for {DbContext}. Attempt {Attempt}",
                    typeof(TDbContext).Name,
                    attempts);

                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied for {DbContext}", typeof(TDbContext).Name);
                break;
            }
            catch (Exception ex) when (attempts <= options.MaxRetryCount)
            {
                logger.LogWarning(
                    ex,
                    "Database migration attempt {Attempt} failed for {DbContext}. Retrying in {DelaySeconds}s",
                    attempts,
                    typeof(TDbContext).Name,
                    options.RetryDelaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(options.RetryDelaySeconds));
            }
        }

        return app;
    }

    public static WebApplication MapDefaultHealthEndpoints(this WebApplication app)
    {
        var otelOptions = app.Configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>()
                         ?? new OpenTelemetryOptions();

        if (otelOptions.Metrics.Prometheus.Enabled)
        {
            app.MapPrometheusScrapingEndpoint("/metrics");
        }

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = 200,
                [HealthStatus.Degraded] = 200,
                [HealthStatus.Unhealthy] = 503
            }
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = 200,
                [HealthStatus.Degraded] = 200,
                [HealthStatus.Unhealthy] = 503
            }
        });

        return app;
    }
}
