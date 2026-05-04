using Microsoft.AspNetCore.Mvc;

namespace WarGameServerData.Controllers;

public class WebControllerFiles : ControllerBase
{
    [Route("GetFile")]
    public IActionResult GetFile(string type, string name)
    {
        var path = AppDomain.CurrentDomain.BaseDirectory + $"{type}\\";
        if (!Directory.Exists(path)) return NotFound();
        var file = path + $"\\{name}";
        if (!System.IO.File.Exists(file)) return NotFound();
        return Ok(Convert.ToBase64String(System.IO.File.ReadAllBytes(file)));
    }
}
