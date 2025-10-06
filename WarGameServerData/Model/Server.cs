namespace WarGameServerData.Model;

public class Server
{
    public float Version { get; set; } = 1.01f;
    public string VersionString { get; set; } = "STABLE 2025-10-06";
    public DateTime TimeStamp { get; set; } = DateTime.Now;
    public string CurrentWebState { get; set; } = string.Empty;


    public async void StartAsync(CancellationToken ct = default)
    {
        //var startTime = DateTime.Now;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            TimeStamp = DateTime.Now;
        }
    }
}