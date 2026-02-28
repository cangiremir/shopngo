namespace ShopNGo.BuildingBlocks.Hosting;

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public ConsoleExporterOptions Console { get; set; } = new();
    public OtlpExporterOptions Otlp { get; set; } = new();
    public MetricsOptions Metrics { get; set; } = new();

    public sealed class ConsoleExporterOptions
    {
        public bool Enabled { get; set; } = true;
    }

    public sealed class OtlpExporterOptions
    {
        public bool Enabled { get; set; }
        public string? Endpoint { get; set; }
        public string? Protocol { get; set; }
    }

    public sealed class MetricsOptions
    {
        public PrometheusOptions Prometheus { get; set; } = new();
        public ToggleOptions Runtime { get; set; } = new() { Enabled = true };
        public ToggleOptions Process { get; set; } = new() { Enabled = true };
        public ToggleOptions HttpServer { get; set; } = new() { Enabled = true };
        public ToggleOptions HttpClient { get; set; } = new() { Enabled = true };
    }

    public sealed class PrometheusOptions
    {
        public bool Enabled { get; set; }
    }

    public sealed class ToggleOptions
    {
        public bool Enabled { get; set; } = true;
    }
}
