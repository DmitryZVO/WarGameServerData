using H264Sharp;
using OpenCvSharp;
using System.Text.Json.Serialization;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
                if (dataLen != 5 * 4) return retEmpty; // 4 байта float с позицией, углом и высотой
                var seek = 10;
                obj.LonX = BitConverter.ToSingle(data, seek); seek += 4; // LonX
                obj.LatY = BitConverter.ToSingle(data, seek); seek += 4; // LatY
                obj.Angle = BitConverter.ToSingle(data, seek); seek += 4; // Угол поворота
                obj.Z = BitConverter.ToSingle(data, seek); seek += 4; // Высота
                obj.Telem.PingToServer = BitConverter.ToSingle(data, seek); seek += 4; // Пинг до сервера

                var ret = new byte[14]; // Ответ с таблицей запросов (requests)
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
                ret[12] = obj.Requests.RelayRewrite; // Запрос перезаписи каналов реле
                ret[13] = obj.Requests.Ptz; // Управление PTZ
                return ret;
            }
            // Это Борщелодка, пакет телеметрии
            case 1 when packType == 0x01:
            {
                if (dataLen != (8 * 4) + (16 * 4) + (4) + (8 * 4)) return retEmpty; // 8 серво, 16 каналов, входящий поток в мбит, каналы реле
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
                obj.Telem.Relay[0] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Relay[1] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Relay[2] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Relay[3] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Relay[4] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Relay[5] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Relay[6] = BitConverter.ToSingle(data, seek); seek += 4;
                obj.Telem.Relay[7] = BitConverter.ToSingle(data, seek); seek += 4;
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
            // Это Борщелодка, пакет запроса перезаписи каналов реле
            case 1 when packType == 0x03:
                {
                    if (dataLen != 0) return retEmpty;

                    var ret = new byte[8 * 4]; // Ответ с таблицей значений (requests)
                    var seek = 0; // Смещение в пакете
                    Array.Copy(BitConverter.GetBytes(obj.RelayValsForWrite.Values[0]), 0, ret, seek, 4); seek += 4;
                    Array.Copy(BitConverter.GetBytes(obj.RelayValsForWrite.Values[1]), 0, ret, seek, 4); seek += 4;
                    Array.Copy(BitConverter.GetBytes(obj.RelayValsForWrite.Values[2]), 0, ret, seek, 4); seek += 4;
                    Array.Copy(BitConverter.GetBytes(obj.RelayValsForWrite.Values[3]), 0, ret, seek, 4); seek += 4;
                    Array.Copy(BitConverter.GetBytes(obj.RelayValsForWrite.Values[4]), 0, ret, seek, 4); seek += 4;
                    Array.Copy(BitConverter.GetBytes(obj.RelayValsForWrite.Values[5]), 0, ret, seek, 4); seek += 4;
                    Array.Copy(BitConverter.GetBytes(obj.RelayValsForWrite.Values[6]), 0, ret, seek, 4); seek += 4;
                    Array.Copy(BitConverter.GetBytes(obj.RelayValsForWrite.Values[7]), 0, ret, seek, 4); seek += 4;
                    return ret;
                }

            default:
                return retEmpty;
        }
    }
    public byte[] ParseUdpCameraPacket(byte[] data)
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
        if (data.Length != dataLen + 10)
        {
            //Console.WriteLine($"Слипшийся пакет! Всего {data.Length:0}, пакет соло {dataLen + 10}");
            return retEmpty; // Динна пакета не совпадает
        }

        if (dataLen <= (1+8+4+4)) return retEmpty; // Полезные данные отсутствуют

        // Находим или создаем новый игровой объект
        GameObject? obj;
        var time = DateTime.Now;
        lock (Items)
        {
            obj = Items.Find(x => x.Id == id);
            if (obj == null)
            {
                obj = new GameObject { Id = id, Type = type, Name = IdToName(id) };
                Items.Add(obj);
            }
        }

        // Обновляем телеметрические данные
        obj.LastTime = time;
        obj.Telem.MBitServerInBytesCounter += data.Length; // Обновляем счетчик принятых байт на сервер от объекта

        switch (type)
        {
            // Разбираем входящий пакет
            // Это Борщелодка, пакет с камерой
            case 1 when packType == 0x11:
                {
                    var seek = 10; // смещение от начала заголовка
                    var cam = data[seek]; seek += 1; // номер камеры
                    var frameNumber = BitConverter.ToInt64(data, seek); seek += 8; // Номер кадра
                    var frameCut = BitConverter.ToUInt32(data, seek); seek += 4; // Номер куска
                    var frameCutAll = BitConverter.ToUInt32(data, seek); seek += 4; // Всего кусков
                    //Console.WriteLine($"{frameNumber:0}: {frameCut:0}/{frameCutAll}, len {dataLen}");
                    if (obj.CamFrames[cam].UdpFrameNumber != frameNumber && obj.CamFrames[cam].UdpFrame.Length > 0) // Новый кадр, пора пересоздавать матрицу кадра
                    {
                        obj.CamFrames[cam].DecodeFrame(cam);
                    }
                    obj.CamFrames[cam].UdpFrameNumber = frameNumber;
                    obj.CamFrames[cam].UdpFrame.Write(data, seek, dataLen - (1 + 8 + 4 + 4)); // Записываем кусок данных
                    if (frameCut == frameCutAll) // Это финальный кусок, пора пересоздавать матрицу кадра
                    {
                        obj.CamFrames[cam].DecodeFrame(cam);
                    }
                    return Array.Empty<byte>();
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
    [JsonIgnore] public RelayForWrite RelayValsForWrite { get; set; } = new(); // Значение реле для ручного управления
    [JsonIgnore] public CameraFrame[] CamFrames { get; set; } = {new(), new(), new(), new(), new(), new(), new(), new(), new(), new()}; // Кадры с камер изображения [10]
}

public class CameraFrame
{
    public const int Width = 800;
    public const int Height = 600;
    public H264Decoder H264Decoder;
    public Mat Frame { get; set; }
    public long UdpFrameNumber { get; set; } // Текущий номер кадра (для сборки)
    public MemoryStream UdpFrame { get; set; } // Поток кадра из udp собранный из кусков

    public CameraFrame()
    {
        UdpFrame = new MemoryStream();
        var decParam = new TagSVCDecodingParam
        {
            uiTargetDqLayer = 0xFF,
            eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_DISABLE,
            bParseOnly = false,
        };
        decParam.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_DEFAULT;
        H264Decoder = new H264Decoder();
        H264Decoder.Initialize(decParam);
        Frame = new Mat(new Size(Width, Height), MatType.CV_8UC3, Scalar.Black);
    }

    public void DecodeFrame(int camNumber)
    {
        var frame = UdpFrame.ToArray();
        var rgb = new RgbImage(ImageFormat.Rgb, CameraFrame.Width, CameraFrame.Height);
        var s = H264Decoder.Decode(frame, 0, frame.Length, true, out var state, ref rgb);
        if (state != DecodingState.dsErrorFree)
        {
            Console.WriteLine($"{s}: {state}, len={frame.Length:0}");
        }

        Frame.Dispose();
        var data = rgb.GetBytes();
        using var mOrig = Mat.FromPixelData(rgb.Height, rgb.Width, MatType.CV_8UC3, data);
        using var mat4 = mOrig.Resize(new Size(CameraFrame.Width, CameraFrame.Height));

        if (camNumber == 4 | camNumber == 5) // Камеры с круговым обзором, нужна коррекция
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
            Frame = mat44.Clone();
        }
        else
        {
            Frame = mat4.Clone();
        }
        rgb.Dispose();
        UdpFrame = new MemoryStream();
    }
}

public class GameObjectTelem // Параметры телеметрии
{
    public float[] Servos { get; set; } = new float[8]; // Значения сервоприводов
    public float[] RcChannels { get; set; } = new float[16]; // Значения каналов управления
    public float[] Relay { get; set; } = new float[8]; // Значения реле
    public float MBitObjectIn { get; set; } // Прием данных от сервера в мегабитах (на объекте)
    public float PingToServer { get; set; } // Пинг до сервера и обратно
    public float MBitServerIn { get; set; } // Прием данных на сервер в мегабитах (на сервере)
    public int MBitServerInBytesCounter { get; set; } // Счетчик приема данных в байтах
}
public class RcChannelsForWrite
{
    public float[] Values { get; set; } = new float[16]; // Значения каналов управления
}
public class RelayForWrite
{
    public float[] Values { get; set; } = new float[8]; // Значения реле для перезаписи
}
public class PoolRequests // Список запросов данных с объекта
{
    [JsonIgnore] public DateTime TelemLastTime { get; set; } = DateTime.MinValue; // Время последнего запроса телеметрии
    [JsonIgnore] public DateTime RcRewriteLastTime { get; set; } = DateTime.MinValue; // Время последнего запроса перезаписи пультов
    [JsonIgnore] public DateTime RelayRewriteLastTime { get; set; } = DateTime.MinValue; // Время последнего запроса перезаписи реле

    [JsonIgnore]
    public DateTime[] CamerasLastTime { get; set; } =
    {
        DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue,
        DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue
    }; // Время последнего запроса телеметрии

    public byte RcRewrite => (byte)((DateTime.Now - RcRewriteLastTime).TotalMilliseconds< 1000 ? 1 : 0); // Время последнего запроса перезаписи пультов
    public byte Telem => (byte)((DateTime.Now - TelemLastTime).TotalMilliseconds < 3000 ? 1 : 0); // Есть ли запрос телеметрии
    public byte RelayRewrite => (byte)((DateTime.Now - RelayRewriteLastTime).TotalMilliseconds < 1000 ? 1 : 0); // Время последнего запроса перезаписи реле
    public byte Ptz { get; set; } // Управление PTZ
    public byte[] Cameras
    {
        get
        {
            var ret = new byte[10];
            var time = DateTime.Now;
            for (var i =0;i< ret.Length;i++)
            {
                ret[i] = (byte)((time - CamerasLastTime[i]).TotalMilliseconds < 1000 ? 1 : 0);
            }
            return ret;
        }
    } // Есть ли запрос изображений с камер
}