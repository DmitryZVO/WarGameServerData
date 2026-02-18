using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarGameServerData.Data;
using WarGameServerData.Other;

namespace WarGameServerData.Controllers;

public class WebControllerGameObjects : ControllerBase
{
    [Route("SetGameObjectRcChannels")]
    public IActionResult SetGameObjectRcChannels(string name, [FromBody] JsonObject json)
    {
        try
        {
            var rcChannels = JsonSerializer.Deserialize<RcChannelsForWrite>(json.ToJsonString());
            if (rcChannels == null) return NotFound();

            var objs = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Name.Equals(name));
                if (obj == null)
                {
                    obj = new GameObject { LastTime = DateTime.Now, Name = name };
                    Core.IoC.Services.GetRequiredService<GameObjects>().Items.Add(obj);
                }
                obj.RcForWrite = rcChannels;
                obj.Requests.RcRewriteLastTime = DateTime.Now;
            }

            return Ok();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }

    [Route("GetGameObjectTelem")]
    public IActionResult GetGameObjectTelem(string name)
    {
        try
        {
            var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (items)
            {
                var item = items.Find(x => x.Name.Equals(name));
                if (item == null) return NotFound();
                item.Requests.TelemLastTime = DateTime.Now;
                var jsonStr = JsonSerializer.Serialize(item.Telem);
                return Ok(jsonStr);
            }
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }

    }

    [Route("GetGameObjectsList")]
    public IActionResult GetGameObjectsList()
    {
        try
        {
            var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (items)
            {
                return Ok(JsonSerializer.Serialize(items));
            }
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, e.ToString());
        }
        return NotFound();
    }

    [Route("GetCamera")]
    public IActionResult GetCamera(int number)
    {
        return NotFound();
    }
}

