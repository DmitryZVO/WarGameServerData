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
    [Route("SetGameObjectRelay")]
    public IActionResult SetGameObjectRelay(string name, [FromBody] JsonObject json)
    {
        try
        {
            var relays = JsonSerializer.Deserialize<RelayForWrite>(json.ToJsonString());
            if (relays == null) return NotFound();

            var objs = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Name.Equals(name));
                if (obj == null)
                {
                    obj = new GameObject { LastTime = DateTime.Now, Name = name };
                    Core.IoC.Services.GetRequiredService<GameObjects>().Items.Add(obj);
                }
                obj.RelayValsForWrite = relays;
                obj.Requests.RelayRewriteLastTime = DateTime.Now;
            }

            return Ok();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerGameObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }
    [Route("SetGameObjectPtz")]
    public IActionResult SetGameObjectPtz(string name, byte ptz)
    {
        try
        {
            var objs = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Name.Equals(name));
                if (obj == null)
                {
                    obj = new GameObject { LastTime = DateTime.Now, Name = name };
                    Core.IoC.Services.GetRequiredService<GameObjects>().Items.Add(obj);
                }

                obj.Requests.Ptz = ptz;
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

                obj.Telem.MBitServerInBytesCounter +=
                    frame.Length; // Обновляем счетчик принятых байт на сервер от объекта
                var rgb = new RgbImage(ImageFormat.Rgb, CameraFrame.Width, CameraFrame.Height);
                var s = obj.CamFrames[number].H264Decoder.Decode(frame, 0, frame.Length, true, out var state, ref rgb);
                if (state != DecodingState.dsErrorFree)
                {
                    Console.WriteLine($"{s}: {state}, len={frame.Length:0}");
                }

                obj.CamFrames[number].Frame.Dispose();
                var data = rgb.GetBytes();
                using var mOrig = Mat.FromPixelData(rgb.Height, rgb.Width, MatType.CV_8UC3, data);
                using var mat4 = mOrig.Resize(new Size(CameraFrame.Width, CameraFrame.Height));

                if (number == 4 | number == 5) // Камеры с круговым обзором, нужна коррекция
                {
                    const float xmin = 0.25f;
                    const float xmax = 0.75f;
                    const float ymin = 0.20f;
                    const float ymax = 0.80f;
                    var srcPoints4 = new List<Point2f>
                    {
                        new(CameraFrame.Width * xmin, CameraFrame.Height * ymin),
                        new(CameraFrame.Width * xmax, CameraFrame.Height * ymin),
                        new(CameraFrame.Width * xmin, CameraFrame.Height * ymax),
                        new(CameraFrame.Width * xmax, CameraFrame.Height * ymax)
                    };
                    var dstPoints4 = new List<Point2f>
                    {
                        new(0, 0),
                        new(CameraFrame.Width, 0),
                        new(0, CameraFrame.Height),
                        new(CameraFrame.Width, CameraFrame.Height)
                    };

                    using var mat44 = new Mat();
                    Cv2.WarpPerspective(mat4, mat44, Cv2.GetPerspectiveTransform(srcPoints4, dstPoints4), new Size(CameraFrame.Width, CameraFrame.Height));
                    obj.CamFrames[number].Frame = mat44.Clone();
                }
                else
                {
                    obj.CamFrames[number].Frame = mat4.Clone();
                }
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

