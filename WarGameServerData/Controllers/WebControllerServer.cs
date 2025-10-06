using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WarGameServerData.Model;
using WarGameServerData.Other;

namespace WarGameServerData.Controllers;

public class WebControllerServer : ControllerBase
{
    [Route("ServerCheck")]
    public IActionResult ServerCheck()
    {
        try
        {
            var ret = new ServerCheck();
            return Ok(JsonSerializer.Serialize(ret));
        }
        catch (Exception e)
        {
            Core.IoC.Services.GetRequiredService<ILogger<WebControllerServer>>().Log(LogLevel.Error, e.ToString());
        }
        return NotFound();
    }
}

public class ServerCheck
{
    public long Time { get; set; } = Core.IoC.Services.GetRequiredService<Server>().TimeStamp.Ticks;
}