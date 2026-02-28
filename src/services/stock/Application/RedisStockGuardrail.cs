using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Threading;

namespace ShopNGo.StockService.Application;

public interface IStockGuardrail
{
    ValueTask<StockGuardrailLease> AcquireAsync(Guid productId, CancellationToken ct);
}

public sealed class StockGuardrailLease : IAsyncDisposable
{
    private readonly Func<ValueTask>? _releaseAsync;
    private int _disposed;

    private StockGuardrailLease(bool allowed, bool isHotSku, bool measurementAvailable, string? reason, Func<ValueTask>? releaseAsync)
    {
        Allowed = allowed;
        IsHotSku = isHotSku;
        MeasurementAvailable = measurementAvailable;
        Reason = reason;
        _releaseAsync = releaseAsync;
    }

    public bool Allowed { get; }
    public bool IsHotSku { get; }
    public bool MeasurementAvailable { get; }
    public string? Reason { get; }

    public static StockGuardrailLease Allow(
        bool isHotSku = false,
        bool measurementAvailable = true,
        Func<ValueTask>? releaseAsync = null)
        => new(true, isHotSku, measurementAvailable, reason: null, releaseAsync);

    public static StockGuardrailLease Deny(string reason)
        => new(false, isHotSku: false, measurementAvailable: true, reason, releaseAsync: null);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_releaseAsync is null)
        {
            return;
        }

        await _releaseAsync();
    }
}

public sealed class RedisStockGuardrail(
    IOptions<RedisGuardrailOptions> options,
    ILogger<RedisStockGuardrail> logger) : IStockGuardrail, IDisposable
{
    private readonly RedisGuardrailOptions _options = options.Value;
    private readonly Lazy<ConnectionMultiplexer> _multiplexer = new(() => ConnectionMultiplexer.Connect(options.Value.Configuration));

    public async ValueTask<StockGuardrailLease> AcquireAsync(Guid productId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return StockGuardrailLease.Allow();
        }

        var db = TryGetDatabase(logger);
        if (db is null)
        {
            return ResolveUnavailableLease();
        }

        var settings = ResolveRuntimeSettings();
        var keys = BuildKeys(productId, settings.KeyPrefix);

        try
        {
            return await AcquireLeaseFromRedisAsync(db, keys, settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis guardrail operation failed for {ProductId}", productId);
            return ResolveRuntimeErrorLease();
        }
    }

    private async Task<StockGuardrailLease> AcquireLeaseFromRedisAsync(
        IDatabase db,
        RedisGuardrailKeys keys,
        RedisGuardrailRuntimeSettings settings)
    {
        var currentInFlight = await IncrementInFlightAsync(db, keys.InflightKey, settings.InFlightTtl).ConfigureAwait(false);
        if (currentInFlight > settings.MaxInFlightPerSku)
        {
            await ReleaseInFlightAsync(db, keys.InflightKey).ConfigureAwait(false);
            return StockGuardrailLease.Deny("admission_limited");
        }

        try
        {
            var windowCount = await IncrementHotWindowAsync(db, keys.HotWindowKey, settings.HotWindowTtl).ConfigureAwait(false);
            var isHotSku = await ResolveHotSkuStateAsync(db, keys.HotStateKey, settings, windowCount).ConfigureAwait(false);

            return StockGuardrailLease.Allow(
                isHotSku: isHotSku,
                measurementAvailable: true,
                releaseAsync: async () => await ReleaseInFlightAsync(db, keys.InflightKey).ConfigureAwait(false));
        }
        catch
        {
            await TryReleaseInFlightAsync(db, keys.InflightKey).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<long> IncrementInFlightAsync(IDatabase db, RedisKey inflightKey, TimeSpan inFlightTtl)
    {
        var currentInFlight = await db.StringIncrementAsync(inflightKey).ConfigureAwait(false);
        if (currentInFlight == 1)
        {
            await db.KeyExpireAsync(inflightKey, inFlightTtl).ConfigureAwait(false);
        }

        return currentInFlight;
    }

    private static async Task<long> IncrementHotWindowAsync(IDatabase db, RedisKey hotWindowKey, TimeSpan hotWindowTtl)
    {
        var windowCount = await db.StringIncrementAsync(hotWindowKey).ConfigureAwait(false);
        if (windowCount == 1)
        {
            await db.KeyExpireAsync(hotWindowKey, hotWindowTtl).ConfigureAwait(false);
        }

        return windowCount;
    }

    private static async Task<bool> ResolveHotSkuStateAsync(
        IDatabase db,
        RedisKey hotStateKey,
        RedisGuardrailRuntimeSettings settings,
        long windowCount)
    {
        if (windowCount >= settings.HotSkuEnterThreshold)
        {
            await db.StringSetAsync(hotStateKey, "1", settings.HotStateTtl).ConfigureAwait(false);
            return true;
        }

        var hasHotState = await db.KeyExistsAsync(hotStateKey).ConfigureAwait(false);
        if (!hasHotState)
        {
            return false;
        }

        if (windowCount <= settings.HotSkuExitThreshold)
        {
            await db.KeyDeleteAsync(hotStateKey).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private async Task TryReleaseInFlightAsync(IDatabase db, RedisKey inflightKey)
    {
        try
        {
            await ReleaseInFlightAsync(db, inflightKey).ConfigureAwait(false);
        }
        catch
        {
            // Best effort rollback for inflight increment.
        }
    }

    private StockGuardrailLease ResolveUnavailableLease()
        => _options.FailOpen
            ? StockGuardrailLease.Allow(measurementAvailable: false)
            : StockGuardrailLease.Deny("redis_unavailable");

    private StockGuardrailLease ResolveRuntimeErrorLease()
        => _options.FailOpen
            ? StockGuardrailLease.Allow(measurementAvailable: false)
            : StockGuardrailLease.Deny("redis_error");

    private RedisGuardrailRuntimeSettings ResolveRuntimeSettings()
    {
        var keyPrefix = string.IsNullOrWhiteSpace(_options.KeyPrefix) ? "shopngo:stock" : _options.KeyPrefix.Trim();
        var inFlightTtl = TimeSpan.FromSeconds(_options.InFlightTtlSeconds <= 0 ? 30 : _options.InFlightTtlSeconds);
        var maxInFlight = _options.MaxInFlightPerSku <= 0 ? 32 : _options.MaxInFlightPerSku;
        var hotWindowTtl = TimeSpan.FromSeconds(_options.HotSkuWindowSeconds <= 0 ? 60 : _options.HotSkuWindowSeconds);
        var hotStateTtl = TimeSpan.FromSeconds(_options.HotSkuTtlSeconds <= 0 ? 300 : _options.HotSkuTtlSeconds);
        var hotEnter = _options.HotSkuEnterThreshold <= 0 ? 100 : _options.HotSkuEnterThreshold;
        var hotExit = Math.Clamp(_options.HotSkuExitThreshold <= 0 ? 60 : _options.HotSkuExitThreshold, 1, hotEnter);

        return new RedisGuardrailRuntimeSettings(
            keyPrefix,
            inFlightTtl,
            maxInFlight,
            hotWindowTtl,
            hotStateTtl,
            hotEnter,
            hotExit);
    }

    private static RedisGuardrailKeys BuildKeys(Guid productId, string keyPrefix)
    {
        var sku = productId.ToString("N");
        return new RedisGuardrailKeys(
            $"{keyPrefix}:inflight:{sku}",
            $"{keyPrefix}:hot:window:{sku}",
            $"{keyPrefix}:hot:state:{sku}");
    }

    private static async Task ReleaseInFlightAsync(IDatabase db, RedisKey inflightKey)
    {
        var remaining = await db.StringDecrementAsync(inflightKey).ConfigureAwait(false);
        if (remaining <= 0)
        {
            await db.KeyDeleteAsync(inflightKey).ConfigureAwait(false);
        }
    }

    private IDatabase? TryGetDatabase(ILogger logger)
    {
        try
        {
            return _multiplexer.Value.GetDatabase();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to connect Redis guardrail to {Configuration}", _options.Configuration);
            return null;
        }
    }

    public void Dispose()
    {
        if (_multiplexer.IsValueCreated)
        {
            _multiplexer.Value.Dispose();
        }
    }

    private sealed record RedisGuardrailKeys(
        RedisKey InflightKey,
        RedisKey HotWindowKey,
        RedisKey HotStateKey);

    private sealed record RedisGuardrailRuntimeSettings(
        string KeyPrefix,
        TimeSpan InFlightTtl,
        int MaxInFlightPerSku,
        TimeSpan HotWindowTtl,
        TimeSpan HotStateTtl,
        int HotSkuEnterThreshold,
        int HotSkuExitThreshold);
}
