namespace ABXConsoleClient;

public class Program
{
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
        var worker = sp.GetService<Worker>();
        worker.doWorkV2();
    }
}
