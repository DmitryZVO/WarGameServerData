using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarGameServerData.Data;
using WarGameServerData.Model;
using WarGameServerData.Other;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace WarGameServerData.Controllers;

public class WebControllerGameObjects : ControllerBase
{
    [Route("GameObjectCommand")]
    public IActionResult GameObjectCommand(int id, uint command)
    {
        try
        {

            var objs = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Id.Equals(id));
                if (obj == null)
                {
                    obj = new GameObject { LastTime = DateTime.Now, Id = id, Name = GameObjects.IdToName(id) };
                    Core.IoC.Services.GetRequiredService<GameObjects>().Items.Add(obj);
                }
                obj.Requests.Commands.Enqueue(command); // Добавить команду к исполнению в список команд
                /////
            }

            return Ok();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, "{e}", e.ToString());
            return NotFound();
        }
    }

    [Route("SetGameObjectRcChannels")]
    public IActionResult SetGameObjectRcChannels(int id, int number, [FromBody] JsonObject json)
    {
        try
        {
            var rcChannels = JsonSerializer.Deserialize<RcChannelsForWrite>(json.ToJsonString());
            if (rcChannels == null) return NotFound();

            var objs = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Id.Equals(id));
                if (obj == null)
                {
                    obj = new GameObject { LastTime = DateTime.Now, Id = id, Name = GameObjects.IdToName(id) };
                    Core.IoC.Services.GetRequiredService<GameObjects>().Items.Add(obj);
                }
                if (obj.RcForWrite.Length <= number) return NotFound();
                obj.RcForWrite[number] = rcChannels;
                obj.Requests.RcRewriteLastTime[number] = DateTime.Now;
            }

            return Ok();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, "{e}", e.ToString());
            return NotFound();
        }
    }

    [Route("GetGameObjectTelem")]
    public IActionResult GetGameObjectTelem(int id)
    {
        try
        {
            var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            var lan = Core.IoC.Services.GetRequiredService<LanIn>();
            var zvo = Core.IoC.Services.GetRequiredService<ZvoRadio>();
            lock (items)
            {
                var item = items.Find(x => x.Id.Equals(id));
                if (item == null) return NotFound();
                item.Telem.CommandCount = (byte)Math.Min(255, item.Requests.Commands.Count); // Добавляем количество команд на исполнение
                item.Telem.QualityMeshWaterToGround = lan.GetCounterMeshHB(); // Качество связи меш до сервера
                item.Telem.QualityMeshGroundToWater = (DateTime.Now - item.LastTime).TotalMilliseconds > 1000 ? 0.0f : item.Telem.QualityMeshGroundToWater;
                item.Telem.QualityZvoWaterToGround = lan.GetCounterZvoHB(); // Качество связи меш до сервера
                item.Telem.QualityZvoGroundToWater = (DateTime.Now - item.LastTime).TotalMilliseconds > 1000 ? 0.0f : item.Telem.QualityZvoGroundToWater;
                item.Telem.QueueZvoGroundToWaterSend = (byte)Math.Min(zvo.ApRadioSendQueue, 255);
                item.Telem.MbitsZvoGroundToWaterSend = zvo.ApRadioBytesSend * 8.0f / 1_000_000.0f;
                item.Telem.MbitsZvoGroundToWaterRecv = zvo.ApRadioBytesRecv * 8.0f / 1_000_000.0f;
                var jsonStr = JsonSerializer.Serialize(item.Telem);
                return Ok(jsonStr);
            }
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, "{e}", e.ToString());
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
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, "{e}", e.ToString());
        }
        return NotFound();
    }

    [Route("GetCamera")]
    public IActionResult GetCamera(int id, int number)
    {
        try
        {
            var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (items)
            {
                var item = items.Find(x => x.Id == id);
                if (item == null) return NotFound();
                item.Requests.CamerasLastTime[number] = DateTime.Now;
                lock (item.CamFrames[number].FrameToSend)
                {
                    return Ok(item.CamFrames[number].FrameToSend);
                }
            }
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, "{e}", e.ToString());
            return NotFound();
        }
    }
}

