using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WarGameServerData.Data;
using WarGameServerData.Model;

namespace WarGameServerData.Other;

internal class Core
{
    public static IHost IoC { get; private set; } = Host.CreateDefaultBuilder(null).Build();

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
    }

    public static void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
    }

    public static void Start()
    {
        IoC = Host.CreateDefaultBuilder(null)
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<Server>();
                services.AddSingleton<StaticObjects>();
                services.AddSingleton<GameObjects>();
                services.AddSingleton<LanIn>();
                services.AddSingleton(sp => new ZvoRadio("192.168.1.51", 2222))
                ;
            })
            .ConfigureLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddProvider(new LoggerToFilesProvider());
                builder.AddSimpleConsole(config =>
                {
                    config.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
                    config.SingleLine = true;
                });
            })
            .Build();

        var radio = IoC.Services.GetRequiredService<ZvoRadio>();
        radio.AddRadioHead(ZvoRadio.TransferMode.MaxRange, 20, [0, 0, 9, 0, 4, 0, 0, 0, 2]); // ZVO_RATE_2_B_1Mbit_20MHz_CCK, SGI
        radio.StartAsync();

        IoC.Services.GetRequiredService<StaticObjects>().StartAsync();
        IoC.Services.GetRequiredService<Server>().StartAsync();
        IoC.Services.GetRequiredService<LanIn>().StartAsync();
    }
}