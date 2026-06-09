using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp.DnnSuperres;
using WarGameServerData.Data;
using WarGameServerData.Other;

namespace WarGameServerData.Model;

public class Server
{
    public float Version { get; set; } = 1.06f;
    public string VersionString { get; set; } = "STABLE 2026-05-13";
    public DateTime TimeStamp { get; set; } = DateTime.Now;
    public string CurrentWebState { get; set; } = string.Empty;

    public async void StartAsync(CancellationToken ct = default)
    {
        Core.IoC.Services.GetRequiredService<GameObjects>().SendRequestsAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            TimeStamp = DateTime.Now;
        }
    }
}