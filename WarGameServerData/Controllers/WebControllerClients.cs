using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarGameServerData.Data;
using WarGameServerData.Other;

namespace WarGameServerData.Controllers;

public class WebControllerClients : ControllerBase
{
    [Route("SetClientJoyChannels")]
    public IActionResult SetClientJoyChannels(string name, [FromBody] JsonObject json)
    {
        try
        {
            var cl = Core.IoC.Services.GetRequiredService<Clients>().Items.Find(x => x.Name.Equals(name));
            if (cl == null)
            {
                cl = new Client {Channels = new float[16], LastTime = DateTime.Now, Name = name};
                Core.IoC.Services.GetRequiredService<Clients>().Items.Add(cl);
            }
            var cns = JsonSerializer.Deserialize<JoyChannels>(json.ToJsonString());
            if (cns == null) return NotFound();
            cl.Channels = cns.Channels;
            return Ok();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerClients>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }

    public class JoyChannels
    {
        public float[] Channels { get; set; } = new float[16];
    }
}
