namespace ABXConsoleClient;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateApplicationBuilder(args);
        var services = host.Services;
        services.AddSingleton<Worker>();
        services.AddLogging();

        var sp = services.BuildServiceProvider();
        var worker = sp.GetService<Worker>();
        worker.doWork();
    }
}
