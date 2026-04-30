using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting.WindowsServices;

using Stateye;
using Microsoft.Extensions.DependencyInjection;

internal static class EntryPoint
{
    private static async Task Main(string[] args)
    {
        var host = Host.CreateApplicationBuilder(args);

        host.Services.AddWindowsService(static o => o.ServiceName = "Stateye");

        host.Services.AddSingleton<StateyeHost>();
        host.Services.AddHostedService(static sp => sp.GetRequiredService<StateyeHost>());

        var app = host.Build();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += OnCancelKeyPress;

        void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cts.Cancel();
        }

        await app.StartAsync(cts.Token);
        await app.WaitForShutdownAsync(cts.Token);
    }
}

internal sealed class StateyeHost : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var loggerFactory = LoggerFactory.Create(static builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new TimestampConsoleLoggerProvider());
        });

        try
        {
            var runtime = new StateyeRuntime(loggerFactory.CreateLogger<StateyeRuntime>());
            await runtime.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}