namespace ABXConsoleClient;

public class Program
{
    private static ILogger<Program> _logger;
    public static void Main(string[] args)
    {

        var host = Host.CreateApplicationBuilder(args);
        var services = host.Services;
        services.AddSingleton<Worker>();
        services.AddSingleton<IABXExchangeServerClient, ExchangeClientV2>();
        services.AddLogging();
        var config = host.Configuration;

        services.Configure<ExchangeServerConnectionConfig>(config.GetSection(ExchangeServerConnectionConfig.ConfigKey));
        var sp = services.BuildServiceProvider();
        _logger = sp.GetService<ILogger<Program>>();
        var worker = sp.GetService<Worker>();
        var result = worker.doWorkV2();
        if (result.Err != null)
        {
            _logger.LogError("got the error, shutting down unsuccessfully");
            return;
        }
        _logger.LogInformation("Program shutting down successfully");
    }
}
