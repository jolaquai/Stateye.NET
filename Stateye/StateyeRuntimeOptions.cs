using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Stateye;

public sealed class StateyeRuntimeOptions
{
    public static StateyeRuntimeOptions Default { get; } = FromConfigFilePath(static () => Path.Combine(Environment.CurrentDirectory, AppConstants.ConfigFileName));

    public Func<ILogger, CancellationToken, ValueTask<Config>> LoadConfigAsync { get; set; } = static (logger, cancellationToken) => LoadConfigFromPathAsync(static () => Path.Combine(Environment.CurrentDirectory, AppConstants.ConfigFileName), logger, cancellationToken);
    public Func<Config, CancellationToken, ValueTask<string>> WriteSampleConfigAsync { get; set; } = static (config, cancellationToken) => WriteSampleConfigToPathAsync(static () => Path.Combine(Environment.CurrentDirectory, AppConstants.ConfigFileName), config, cancellationToken);

    public static StateyeRuntimeOptions FromConfigFilePath(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        return FromConfigFilePath(() => configPath);
    }

    public static StateyeRuntimeOptions FromConfigFilePath(Func<string> configPathProvider)
    {
        ArgumentNullException.ThrowIfNull(configPathProvider);

        return new StateyeRuntimeOptions()
        {
            LoadConfigAsync = (logger, cancellationToken) => LoadConfigFromPathAsync(configPathProvider, logger, cancellationToken),
            WriteSampleConfigAsync = (config, cancellationToken) => WriteSampleConfigToPathAsync(configPathProvider, config, cancellationToken),
        };
    }

    public static StateyeRuntimeOptions FromConfig(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return FromConfig(() => config);
    }

    public static StateyeRuntimeOptions FromConfig(Func<Config> configProvider)
    {
        ArgumentNullException.ThrowIfNull(configProvider);

        return new StateyeRuntimeOptions()
        {
            LoadConfigAsync = (_, _) => ValueTask.FromResult(configProvider()),
            WriteSampleConfigAsync = static (_, _) => ValueTask.FromResult<string>(null),
        };
    }

    public static StateyeRuntimeOptions FromOptions(IOptions<Config> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return FromConfig(() => options.Value);
    }

    private static ValueTask<Config> LoadConfigFromPathAsync(Func<string> configPathProvider, ILogger logger, CancellationToken cancellationToken)
        => new(Config.LoadAsync(GetConfigPath(configPathProvider), logger, cancellationToken));

    private static async ValueTask<string> WriteSampleConfigToPathAsync(Func<string> configPathProvider, Config config, CancellationToken cancellationToken)
    {
        var configPath = GetConfigPath(configPathProvider);
        await Config.WriteAsync(configPath, config, cancellationToken);
        return configPath;
    }

    private static string GetConfigPath(Func<string> configPathProvider)
    {
        ArgumentNullException.ThrowIfNull(configPathProvider);
        var configPath = configPathProvider();
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        return configPath;
    }
}
