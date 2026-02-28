namespace ShopNGo.IntegrationTests.Infrastructure;

internal static class IntegrationPolling
{
    public static async Task WaitForHealthAsync(HttpClient client, string path, int timeoutSeconds = 30)
    {
        await WaitUntilAsync(
            async () =>
            {
                try
                {
                    var response = await client.GetAsync(path);
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            },
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, TimeSpan? poll = null)
    {
        var delay = poll ?? TimeSpan.FromMilliseconds(300);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(delay);
        }

        throw new TimeoutException("Condition was not met within the timeout.");
    }
}
