using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WarGameServerData.Data;
using WarGameServerData.Other;

namespace WarGameServerData.Controllers;

public class WebControllerStaticObjects : ControllerBase
{
    [Route("GetStaticObjects")]
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
}
