using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarGameServerData.Data;
using WarGameServerData.Other;
using static WarGameServerData.Controllers.WebControllerClients;

namespace WarGameServerData.Controllers;

public class WebControllerGameObjects : ControllerBase
{
    [Route("SetGameObjectValues")]
    public IActionResult SetGameObjectValues(string name, [FromBody] JsonObject json)
    {
        try
        {
            var objV = JsonSerializer.Deserialize<GameObject>(json.ToJsonString());
            if (objV == null) return NotFound();

            var objs = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Name.Equals(name));
                if (obj == null)
                {
                    obj = new GameObject();
                    Core.IoC.Services.GetRequiredService<GameObjects>().Items.Add(obj);
                }
                obj.LastTime = DateTime.Now;
                obj.Name = objV.Name;
                obj.Z = objV.Z;
                obj.Angle = objV.Angle;
                obj.LatY = objV.LatY;
                obj.LonX = objV.LonX;
                obj.Type = objV.Type;
                return Ok(JsonSerializer.Serialize(obj.Requests));
            }
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }

    [Route("SetGameObjectTelem")]
    public IActionResult SetGameObjectTelem(string name, [FromBody] JsonObject json)
    {
        try
        {
            var telem = JsonSerializer.Deserialize<GameObjectTelem>(json.ToJsonString());
            if (telem == null) return NotFound();

            var objs = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Name.Equals(name));
                if (obj == null)
                {
                    obj = new GameObject {LastTime = DateTime.Now, Name = name};
                    Core.IoC.Services.GetRequiredService<GameObjects>().Items.Add(obj);
                }
                obj.Telem = telem;
            }

            return Ok();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }

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

    [Route("GetRcChannels")]
    public IActionResult GetRcChannels(string name)
    {
        try
        {
            var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (items)
            {
                var item = items.Find(x => x.Name.Equals(name));
                if (item == null) return NotFound();
                item.Requests.TelemLastTime = DateTime.Now;
                var jsonStr = JsonSerializer.Serialize(item.RcForWrite);
                return Ok(jsonStr);
            }
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

    [Route("PutCamera")]
    public IActionResult PutCamera(int number, [FromBody] JsonObject json)
    {
        return NotFound();
    }
}

