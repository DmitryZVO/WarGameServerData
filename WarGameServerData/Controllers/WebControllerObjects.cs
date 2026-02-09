using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarGameServerData.Data;
using WarGameServerData.Other;

namespace WarGameServerData.Controllers;

public class WebControllerObjects : ControllerBase
{
    [Route("SetObjectValues")]
    public IActionResult SetObjectValues(string name, [FromBody] JsonObject json)
    {
        try
        {
            var objV = JsonSerializer.Deserialize<ObjectsList.ObjectList>(json.ToJsonString());
            if (objV == null) return NotFound();

            var objs = Core.IoC.Services.GetRequiredService<Objects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Name.Equals(name));
                if (obj == null)
                {
                    obj = new Data.Object();
                    Core.IoC.Services.GetRequiredService<Objects>().Items.Add(obj);
                }
                obj.LastTime = DateTime.Now;
                obj.Name = objV.Name;
                obj.Z = objV.Z;
                obj.Angle = objV.Angle;
                obj.LatY = objV.LatY;
                obj.LonX = objV.LonX;
                obj.Type = objV.Type;
            }

            return Ok();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }

    [Route("SetObjectTelem")]
    public IActionResult SetObjectTelem(string name, [FromBody] JsonObject json)
    {
        try
        {
            var telem = JsonSerializer.Deserialize<Telem>(json.ToJsonString());
            if (telem == null) return NotFound();

            var objs = Core.IoC.Services.GetRequiredService<Objects>().Items;
            lock (objs)
            {
                var obj = objs.Find(x => x.Name.Equals(name));
                if (obj == null)
                {
                    obj = new Data.Object {LastTime = DateTime.Now, Name = name};
                    Core.IoC.Services.GetRequiredService<Objects>().Items.Add(obj);
                }
                obj.Telem = telem;
            }

            return Ok();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }

    [Route("GetObjectTelem")]
    public IActionResult GetObjectTelem(string name)
    {
        try
        {
            var items = Core.IoC.Services.GetRequiredService<Objects>().Items;
            lock (items)
            {
                var item = items.Find(x => x.Name.Equals(name));
                if (item == null) return NotFound();
                return Ok(JsonSerializer.Serialize(item.Telem));
            }
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }

    }

    [Route("GetObjectsList")]
    public IActionResult GetObjectsList()
    {
        try
        {
            var objs = Core.IoC.Services.GetRequiredService<Objects>().Items;
            lock (objs)
            {
                var ret = new ObjectsList {TimeStamp = Core.IoC.Services.GetRequiredService<Objects>().TimeStamp};
                foreach (var obj in objs)
                {
                    ret.Items.Add(new ObjectsList.ObjectList
                    {
                        Name = obj.Name, 
                        Alive = obj.Alive, 
                        Type = obj.Type, 
                        Angle = obj.Angle, 
                        LatY = obj.LatY,
                        LonX = obj.LonX, 
                        Z = obj.Z
                    });
                }

                return Ok(JsonSerializer.Serialize(ret));
            }
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerObjects>>().Log(LogLevel.Error, e.ToString());
            return NotFound();
        }
    }

    public class ObjectsList
    {
        public long TimeStamp { get; set; } = DateTime.Now.Ticks;
        public List<ObjectList> Items { get; set; } = new();

        public class ObjectList
        {
            public string Name { get; set; } = string.Empty; // Имя объекта
            public bool Alive { get; set; } // Жив или нет
            public int Type { get; set; } // Тип объекта
            public float LonX { get; set; } // Позиция по X
            public float LatY { get; set; } // Позиция по Y
            public float Z { get; set; } // Позиция по Z
            public float Angle { get; set; } // Угол поворота
        }
    }

    [Route("GetCamera")]
    public IActionResult GetCamera(int number)
    {
        /*
        try
        {
            var bmp = Array.Empty<byte>();
            switch (number)
            {
                default:
                case 0:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera0)
                    {
                        if (Core.IoC.Services.GetRequiredService<Objects>().Camera0.Empty()) break;
                        bmp = Core.IoC.Services.GetRequiredService<Objects>().Camera0.ToBytes(".webp");
                    }
                    break;
                case 1:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera1)
                    {
                        if (Core.IoC.Services.GetRequiredService<Objects>().Camera1.Empty()) break;
                        bmp = Core.IoC.Services.GetRequiredService<Objects>().Camera1.ToBytes(".webp");
                    }
                    break;
                case 2:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera2)
                    {
                        if (Core.IoC.Services.GetRequiredService<Objects>().Camera2.Empty()) break;
                        bmp = Core.IoC.Services.GetRequiredService<Objects>().Camera2.ToBytes(".webp");
                    }
                    break;
                case 3:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera3)
                    {
                        if (Core.IoC.Services.GetRequiredService<Objects>().Camera3.Empty()) break;
                        bmp = Core.IoC.Services.GetRequiredService<Objects>().Camera3.ToBytes(".webp");
                    }
                    break;
                case 4:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera4)
                    {
                        if (Core.IoC.Services.GetRequiredService<Objects>().Camera4.Empty()) break;
                        bmp = Core.IoC.Services.GetRequiredService<Objects>().Camera4.ToBytes(".webp");
                    }
                    break;
            }
            return bmp.Length > 0 ? Ok(Convert.ToBase64String(bmp)) : NotFound();
        }
        catch (Exception e)
        {
            //Core.IoC.Services.GetRequiredService<ILogger<WebControllerTiles>>().Log(Microsoft.Extensions.Logging.LogLevel.Error, e.ToString());
        }
        */
        return NotFound();
    }

    [Route("PutCamera")]
    public IActionResult PutCamera(int number, [FromBody] JsonObject json)
    {
        /*
        try
        {
            var video = JsonSerializer.Deserialize<CameraVideo>(json.ToJsonString());
            if (video == null) return NotFound();

            switch (number)
            {
                default:
                case 0:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera0)
                    {
                        Core.IoC.Services.GetRequiredService<Objects>().Camera0.Dispose();
                        Core.IoC.Services.GetRequiredService<Objects>().Camera0 =
                            Cv2.ImDecode(Convert.FromBase64String(video.FileBase64), ImreadModes.Unchanged);
                    }
                    break;
                case 1:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera1)
                    {
                        Core.IoC.Services.GetRequiredService<Objects>().Camera1.Dispose();
                        Core.IoC.Services.GetRequiredService<Objects>().Camera1 =
                            Cv2.ImDecode(Convert.FromBase64String(video.FileBase64), ImreadModes.Unchanged);
                    }
                    break;
                case 2:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera2)
                    {
                        Core.IoC.Services.GetRequiredService<Objects>().Camera2.Dispose();
                        Core.IoC.Services.GetRequiredService<Objects>().Camera2 =
                            Cv2.ImDecode(Convert.FromBase64String(video.FileBase64), ImreadModes.Unchanged);
                    }
                    break;
                case 3:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera3)
                    {
                        Core.IoC.Services.GetRequiredService<Objects>().Camera3.Dispose();
                        Core.IoC.Services.GetRequiredService<Objects>().Camera3 =
                            Cv2.ImDecode(Convert.FromBase64String(video.FileBase64), ImreadModes.Unchanged);
                    }
                    break;
                case 4:
                    lock (Core.IoC.Services.GetRequiredService<Objects>().Camera4)
                    {
                        Core.IoC.Services.GetRequiredService<Objects>().Camera4.Dispose();
                        Core.IoC.Services.GetRequiredService<Objects>().Camera4 =
                            Cv2.ImDecode(Convert.FromBase64String(video.FileBase64), ImreadModes.Unchanged);
                    }

                    break;
            }

            return Ok();
        }
        catch (Exception e)
        {
            return NotFound();
        }
        */
        return NotFound();
    }
}

public class CameraVideo
{
    public string FileBase64 { get; set; } = string.Empty;
}

