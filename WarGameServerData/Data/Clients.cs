namespace WarGameServerData.Data;

public class Clients
{
    public List<Client> Items { get; set; } = new();
}

public class Client
{
    public bool Alive => (DateTime.Now - LastTime).TotalMilliseconds > 1000;
    public string Name { get; set; } = string.Empty;
    public float[] Channels { get; set; } = new float[16];
    public DateTime LastTime = DateTime.MinValue;
}