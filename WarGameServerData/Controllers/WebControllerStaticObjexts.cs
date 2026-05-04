using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarGameServerData.Data;
using WarGameServerData.Model;
using WarGameServerData.Other;

namespace WarGameServerData.Controllers;

public class WebControllerStaticObjects : ControllerBase
{
    [Route("GetStaticObjects")] // Запрос объектов
    public IActionResult GetStaticObjects()
    {
        try
        {
            var items = Core.IoC.Services.GetRequiredService<StaticObjects>().Items;
            lock (items)
            {
                return Ok(JsonSerializer.Serialize(items));
            }
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerStaticObjects>>().Log(LogLevel.Error, e.ToString());
        }
        return NotFound();
    }

    [Route("SetStaticObjects")] // Запись объектов
    public async Task<IActionResult> SetStaticObjects([FromBody] JsonObject json)
    {
        var ret = false;
        try
        {
            var objects = Core.IoC.Services.GetRequiredService<StaticObjects>();
            var itemsNew = JsonSerializer.Deserialize<StaticObjects>(json.ToJsonString());
            if (itemsNew == null) return BadRequest();
            lock (objects.Items)
            {
                objects.Items = itemsNew.Items;
                objects.TimeStamp = DateTime.Now.Ticks;
                ret = true;
            }
            await Core.IoC.Services.GetRequiredService<StaticObjects>().SaveAsync();
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerStaticObjects>>().Log(LogLevel.Error, e.ToString());
        }
        return ret ? Ok() : NotFound();
    }
}
