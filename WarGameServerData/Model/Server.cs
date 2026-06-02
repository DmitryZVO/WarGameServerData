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

    public static readonly DnnSuperResImpl sr2 = new();
    public static readonly DnnSuperResImpl sr3 = new();
    public static readonly DnnSuperResImpl sr4 = new();
    public static readonly DnnSuperResImpl sr8 = new();

    public async void StartAsync(CancellationToken ct = default)
    {
        Core.IoC.Services.GetRequiredService<GameObjects>().SendRequestsAsync(ct);

        sr2.ReadModel(@"dnn\x2.pb");
        sr2.SetModel("fsrcnn", 2);
        sr3.ReadModel(@"dnn\x3.pb");
        sr3.SetModel("fsrcnn", 3);
        sr4.ReadModel(@"dnn\x4.pb");
        sr4.SetModel("fsrcnn", 4);
        sr8.ReadModel(@"dnn\x8.pb");
        sr8.SetModel("lapsrn", 8);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            TimeStamp = DateTime.Now;
        }
    }
}