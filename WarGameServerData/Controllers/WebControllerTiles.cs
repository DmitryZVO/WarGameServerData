using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Data.SQLite;
using Microsoft.Extensions.DependencyInjection;
using WarGameServerData.Other;

namespace WarGameServerData.Controllers;

public class WebControllerTiles : ControllerBase
{
    [Route("GetTile")]
    public async Task<IActionResult> GetTile(int x, int y, int z)
    {
        try
        {
            var files = Directory.GetFiles($"{AppDomain.CurrentDomain.BaseDirectory}Maps");

            var bmp = Array.Empty<byte>();

            foreach (var f in files)
            {
                var sqlite = new SQLiteConnection($"Data Source={f};Version=3;");
                await sqlite.OpenAsync();

                var cmd = sqlite.CreateCommand();
                cmd.CommandText = "SELECT * FROM Tiles WHERE zoom=@zoom AND x=@x AND y=@y AND type=@type";
                cmd.Parameters.AddWithValue("@zoom", z);
                cmd.Parameters.AddWithValue("@x", x);
                cmd.Parameters.AddWithValue("@y", y);
                cmd.Parameters.AddWithValue("@type", 0);
                var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    bmp = (byte[])reader["blob"];
                }
                await reader.DisposeAsync();
                await cmd.DisposeAsync();
                await sqlite.DisposeAsync();
                if (bmp.Length > 0) break;
            }
            return bmp.Length > 0 ? Ok(Convert.ToBase64String(bmp)) : NotFound();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerTiles>>().Log(LogLevel.Error, e.ToString());
        }
        return NotFound();
    }
}
