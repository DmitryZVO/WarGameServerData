using H264Sharp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarGameServerData.Data;
using WarGameServerData.Other;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

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
                return Ok(Convert.ToBase64String(item.CamFrames[number].Frame.ToBytes(".webp")));
            }
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }
    [Route("SetCamera")]
    public IActionResult SetCamera(int id, int number, [FromBody] JsonObject json)
    {
        var w = 640;
        var h = 480;
        try
        {
            var frame = Convert.FromBase64String(JsonSerializer.Deserialize<CameraVideo>(json.ToJsonString())!.FileBase64);//Convert.FromBase64String(json.ToJsonString());
            if (frame.Length <= 0) return NotFound();

            var objs = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Id.Equals(id));
                if (obj == null)
                {
                    return NotFound();
                }

                obj.Telem.MBitServerInBytesCounter += frame.Length; // Обновляем счетчик принятых байт на сервер от объекта
                var rgb = new RgbImage(ImageFormat.Rgb, w, h);
                var s = obj.CamFrames[number].H264Decoder.Decode(frame, 0, frame.Length, true, out var state, ref rgb);
                if (state != DecodingState.dsErrorFree)
                {
                    Console.WriteLine($"{s}: {state}, len={frame.Length:0}");
                }

                obj.CamFrames[number].Frame.Dispose();
                var data = rgb.GetBytes();
                using var mOrig = Mat.FromPixelData(rgb.Height, rgb.Width, MatType.CV_8UC3, data);
                obj.CamFrames[number].Frame = mOrig.Resize(new Size(CameraFrame.Width, CameraFrame.Height));
                rgb.Dispose();
            }

            return Ok();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }

    private class CameraVideo
    {
        public string FileBase64 { get; set; } = string.Empty;
    }
}

