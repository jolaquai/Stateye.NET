using Microsoft.Extensions.Logging;

using Stateye;

internal static class EntryPoint
{
    private static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            using var loggerFactory = LoggerFactory.Create(static builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddProvider(new TimestampConsoleLoggerProvider());
            });

            var runtime = new StateyeRuntime(loggerFactory.CreateLogger<StateyeRuntime>());
            await runtime.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }

        void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cts.Cancel();
        }
    }
}
