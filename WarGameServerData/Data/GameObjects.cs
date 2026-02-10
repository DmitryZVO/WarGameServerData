using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

namespace WarGameServerData.Data;

public class GameObjects
{
    public long TimeStamp { get; set; } = DateTime.Now.Ticks;
    public List<GameObject> Items { get; set; } = new();
}

public class GameObject
{
    public bool Alive => (DateTime.Now - LastTime).TotalMilliseconds > 5000; // Проверка на статус жив/мертв
    public string Name { get; set; } = string.Empty; // Имя объекта
    public int Type { get; set; } // Тип объекта 0-тестовый ровер, 1-борщевик, 2-БЭК 
    public float LonX { get; set; } // Позиция по X
    public float LatY { get; set; } // Позиция по Y
    public float Z { get; set; } // Позиция по Z
    public float Angle { get; set; } // Угол поворота
    public DateTime LastTime = DateTime.MinValue; // Время последнего пакета
    [JsonIgnore] public GameObjectTelem Telem { get; set; } = new(); // Телеметрия объекта
    [JsonIgnore] public PoolRequests Requests { get; set; } = new(); // Запросы данных с объекта
    [JsonIgnore] public RcChannelsForWrite RcForWrite { get; set; } = new(); // Значение пультов для ручного управления
}
public class GameObjectTelem // Параметры телеметрии
{
    public float[] Servos { get; set; } = new float[8]; // Значения сервоприводов
    public float[] RcChannels { get; set; } = new float[16]; // Значения каналов управления
}
public class RcChannelsForWrite
{
    public float[] Values { get; set; } = new float[16]; // Значения каналов управления
}
public class PoolRequests // Список запросов данных с объекта
{
    [JsonIgnore] public DateTime TelemLastTime { get; set; } = DateTime.MinValue; // Время последнего запроса телеметрии
    [JsonIgnore] public DateTime RcRewriteLastTime { get; set; } = DateTime.MinValue; // Время последнего запроса перезаписи пультов

    [JsonIgnore]
    public DateTime[] CamerasLastTime { get; set; } =
    {
        DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue,
        DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue
    }; // Время последнего запроса телеметрии

    public byte RcRewrite => (byte)((DateTime.Now - RcRewriteLastTime).TotalMilliseconds< 1000 ? 1 : 0); // Время последнего запроса перезаписи пультов
    public byte Telem => (byte)((DateTime.Now - TelemLastTime).TotalMilliseconds < 3000 ? 1 : 0); // Есть ли запрос телеметрии
    public byte[] Cameras
    {
        get
        {
            var ret = new byte[10];
            var time = DateTime.Now;
            for (var i =0;i< ret.Length;i++)
            {
                ret[i] = (byte)((time - CamerasLastTime[i]).TotalMilliseconds < 3000 ? 1 : 0);
            }
            return ret;
        }
    } // Есть ли запрос изображений с камер
}