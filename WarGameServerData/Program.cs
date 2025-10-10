using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WarGameServerData.Data;
using WarGameServerData.Model;
using WarGameServerData.Other;

using var mutex = new Mutex(true, "WGS_DATA", out var createdNew);
if (!createdNew)
{
    Console.WriteLine("Запущен другой экземпляр программы! Продолженние не возможно....");
    Console.ReadLine();
    return;
}

Core.Start();

var host = new WebHostBuilder().UseKestrel(options => { options.Limits.MaxRequestBodySize = null; })
    .UseUrls("http://*:1111").UseStartup<Core>().Build();

var serv = Core.IoC.Services.GetRequiredService<Server>();

Core.IoC.Services.GetRequiredService<ILogger<Core>>().Log(LogLevel.Information, $"WarGame Server Data v{serv.Version.ToStringF2()} [{serv.VersionString}] START!");
host.Run();

await Core.IoC.Services.GetRequiredService<StaticObjects>().SaveAsync();
Core.IoC.Services.GetRequiredService<ILogger<Core>>().Log(LogLevel.Information, $"WarGame Server Data v{serv.Version.ToStringF2()} [{serv.VersionString}] STOP!");
