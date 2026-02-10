using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarGameServerData.Data;
using WarGameServerData.Other;

namespace WarGameServerData.Controllers;

public class WebControllerClients : ControllerBase
{

    public class JoyChannels
    {
        public float[] Channels { get; set; } = new float[16];
    }
}
