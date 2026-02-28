using System.Diagnostics.Metrics;

namespace ShopNGo.BuildingBlocks.Metrics;

public static class BusinessMetrics
{
    public const string MeterName = "ShopNGo.Business";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> OrdersCreatedCounter =
        Meter.CreateCounter<long>("shopngo_orders_created_total", description: "Orders created in pending stock state.");

    private static readonly Counter<long> OrdersFinalizedCounter =
        Meter.CreateCounter<long>("shopngo_orders_finalized_total", description: "Orders finalized by status and error code.");

    private static readonly Counter<long> StockReservationResultCounter =
        Meter.CreateCounter<long>("shopngo_stock_reservation_result_total", description: "Stock reservation results.");

    private static readonly Histogram<double> StockReservationDurationMsHistogram =
        Meter.CreateHistogram<double>("shopngo_stock_reservation_duration_ms", unit: "ms", description: "Stock reservation processing duration.");

    public static void RecordOrderCreated(string service)
    {
        OrdersCreatedCounter.Add(1, new KeyValuePair<string, object?>("service", service));
    }

    public static void RecordOrderFinalized(string service, string status, string? errorCode)
    {
        OrdersFinalizedCounter.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("error_code", string.IsNullOrWhiteSpace(errorCode) ? "none" : errorCode));
    }

    public static void RecordStockReservationResult(string service, string result, string? errorCode, double durationMs)
    {
        StockReservationResultCounter.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("result", result),
            new KeyValuePair<string, object?>("error_code", string.IsNullOrWhiteSpace(errorCode) ? "none" : errorCode));

        StockReservationDurationMsHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("result", result),
            new KeyValuePair<string, object?>("error_code", string.IsNullOrWhiteSpace(errorCode) ? "none" : errorCode));
    }
}
