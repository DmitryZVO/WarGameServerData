using System.Text.Json.Serialization;

namespace WarGameServerData.Data;

public class GameObjects
{
    public static string IdToName(int id)
    {
        return id switch
        {
            0x00000000 => "Server",
            0x00000001 => "Tank1",
            _ => $"?0x{id:X8}"
        };
    }

    public long TimeStamp { get; set; } = DateTime.Now.Ticks;
    public List<GameObject> Items { get; set; } = new();

    public byte[] ParseUdpPacket(byte[] data)
    {
        var retEmpty = Array.Empty<byte>();

        // Проверка на пакет ZVO
        if (data.Length < 10) return retEmpty; // ZVO пакет не может быть менее 10 байт
        if (data[0] != 0x70) return retEmpty; // это не ZVO пакет
        if (data[1] != 0x70) return retEmpty; // это не ZVO пакет
        var type = (int)data[2]; // Тип объекта
        var id = (int)BitConverter.ToUInt32(data, 3); // ID объекта
        var packType = (int)data[7]; // тип входящего пакета
        var dataLen = (int)BitConverter.ToUInt16(data, 8); ; // длинна полезных данных
        if (data.Length != dataLen + 10) return retEmpty; // Динна пакета не совпадает

        // Находим или создаем новый игровой объект
        GameObject? obj;
        var time = DateTime.Now;
        lock (Items)
        {
            obj = Items.Find(x => x.Id == id);
            if (obj == null)
            {
                obj = new GameObject {Id = id, Type = type, Name = IdToName(id)};
                Items.Add(obj);
            }
        }

        // Обновляем телеметрические данные
        obj.LastTime = time;
        obj.Telem.MBitServerInBytesCounter += data.Length; // Обновляем счетчик принятых байт на сервер от объекта

        switch (type)
        {
            // Разбираем входящий пакет
            // Это Борщелодка, пакет HeartBeat
            case 1 when packType == 0x00:
            {
                if (dataLen != 4 * 4) return retEmpty; // 4 байта float с позицией, углом и высотой
                var seek = 10;
                obj.LonX = BitConverter.ToSingle(data, seek); seek += 4; // LonX
                obj.LatY = BitConverter.ToSingle(data, seek); seek += 4; // LatY
                obj.Angle = BitConverter.ToSingle(data, seek); seek += 4; // Угол поворота
                obj.Z = BitConverter.ToSingle(data, seek); seek += 4; // Высота

                var ret = new byte[12]; // Ответ с таблицей запросов (requests)
                ret[0] = obj.Requests.Telem; // Запрос телеметрии
                ret[1] = obj.Requests.RcRewrite; // Запрос перезаписи RC каналов
                ret[2] = obj.Requests.Cameras[0]; // Запрос кадра с камеры 0
                ret[3] = obj.Requests.Cameras[1]; // Запрос кадра с камеры 1
                ret[4] = obj.Requests.Cameras[2]; // Запрос кадра с камеры 2
                ret[5] = obj.Requests.Cameras[3]; // Запрос кадра с камеры 3
                ret[6] = obj.Requests.Cameras[4]; // Запрос кадра с камеры 4
                ret[7] = obj.Requests.Cameras[5]; // Запрос кадра с камеры 5
                ret[8] = obj.Requests.Cameras[6]; // Запрос кадра с камеры 6
                ret[9] = obj.Requests.Cameras[7]; // Запрос кадра с камеры 7
                ret[10] = obj.Requests.Cameras[8]; // Запрос кадра с камеры 8
                ret[11] = obj.Requests.Cameras[9]; // Запрос кадра с камеры 9
                return ret;
            }
            // Это Борщелодка, пакет телеметрии
            case 1 when packType == 0x01:
            {
                if (dataLen != 8 * 4 + 16 * 4 + 4) return retEmpty; // 8 серво, 16 каналов, входящий поток в мбит
                var seek = 10;
                obj.Telem.Servos[0] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Servos[1] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Servos[2] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Servos[3] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Servos[4] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Servos[5] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Servos[6] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Servos[7] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[0] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[1] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[2] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[3] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[4] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[5] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[6] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[7] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[8] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[9] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[10] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[11] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[12] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[13] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[14] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.RcChannels[15] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.MBitObjectIn = BitConverter.ToSingle(data, seek); seek += 4;
                return retEmpty;
            }
            // Это Борщелодка, пакет запроса перезаписи RC каналов
            case 1 when packType == 0x02:
            {
                if (dataLen != 0) return retEmpty;

                var ret = new byte[16 * 4]; // Ответ с таблицей запросов (requests)
                var seek = 0; // Смещение в пакете
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[0]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[1]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[2]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[3]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[4]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[5]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[6]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[7]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[8]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[9]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[10]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[11]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[12]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[13]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[14]), 0, ret, seek, 4); seek += 4;
                Array.Copy(BitConverter.GetBytes(obj.RcForWrite.Values[15]), 0, ret, seek, 4); seek += 4;
                return ret;
            }

            default:
                return retEmpty;
        }
    }
}

public class GameObject
{
    public bool Alive => (DateTime.Now - LastTime).TotalMilliseconds > 5000; // Проверка на статус жив/мертв
    public int Id { get; set; } // Уникальный номер объекта (4 байта)
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
    public float MBitObjectIn { get; set; } // Прием данных от сервера в мегабитах (на объекте)
    public float MBitServerIn { get; set; } // Прием данных на сервер в мегабитах (на сервере)
    public int MBitServerInBytesCounter { get; set; } // Счетчик приема данных в байтах
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