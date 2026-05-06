using H264Sharp;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using System.Text.Json.Serialization;
using WarGameServerData.Other;

namespace WarGameServerData.Data;

public class GameObjects
{
    public static string IdToName(int id)
    {
        return id switch
        {
            0x00000000 => "Server",
            0x00000001 => "Ship1",
            _ => $"?0x{id:X8}"
        };
    }

    public long TimeStamp { get; set; } = DateTime.Now.Ticks;
    public List<GameObject> Items { get; set; } = [];

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
            // Это Борщелодка, пакет HeartBeat + Telem
            case 1 when packType == 0x00:
                {
                    if (dataLen != ((4 * 2) + (2 * 3) + (1 * 8) + (2 * 3) + (1 * 2) + 5 + 2 + 1 + 1 + (8 * 2))) return retEmpty; // не верный размер пакета
                    var seek = 10;
                    obj.LonX = BitConverter.ToSingle(data, seek); seek += 4; // LonX
                    obj.LatY = BitConverter.ToSingle(data, seek); seek += 4; // LatY
                    obj.Angle = BitConverter.ToUInt16(data, seek) / 100.0f; seek += 2; // Угол поворота Yaw
                    obj.Telem.YawGrad = obj.Angle;
                    obj.Telem.RollGrad = BitConverter.ToInt16(data, seek) / 100.0f; seek += 2; // Угол поворота Roll
                    obj.Telem.PitchGrad = BitConverter.ToInt16(data, seek) / 100.0f; seek += 2; // Угол поворота Pitch
                    obj.Telem.RcChannels[0] = (ushort)(data[seek] * 5 + 1000); seek += 1;
                    obj.Telem.RcChannels[1] = (ushort)(data[seek] * 5 + 1000); seek += 1;
                    obj.Telem.RcChannels[2] = (ushort)(data[seek] * 5 + 1000); seek += 1;
                    obj.Telem.RcChannels[3] = (ushort)(data[seek] * 5 + 1000); seek += 1;
                    obj.Telem.RcChannels[4] = (ushort)(data[seek] * 5 + 1000); seek += 1;
                    obj.Telem.RcChannels[5] = (ushort)(data[seek] * 5 + 1000); seek += 1;
                    obj.Telem.RcChannels[6] = (ushort)(data[seek] * 5 + 1000); seek += 1;
                    obj.Telem.RcChannels[7] = (ushort)(data[seek] * 5 + 1000); seek += 1;
                    obj.Telem.MBitObjectIn = BitConverter.ToUInt16(data, seek) / 1000.0f; seek += 2;
                    obj.Telem.MBitObjectOut = BitConverter.ToUInt16(data, seek) / 1000.0f; seek += 2;
                    obj.Telem.PingToServer = BitConverter.ToUInt16(data, seek); seek += 2; // Пинг до сервера
                    obj.Telem.VideoFps = data[seek]; seek += 1;
                    obj.Telem.VideoQuality = data[seek]; seek += 1;
                    Array.Copy(data, seek, obj.Telem.CanEngineBits, 0, 5); seek += 5;
                    obj.Telem.FuelVcurr = BitConverter.ToUInt16(data, seek); seek += 2; // Объем топлива в баке
                    obj.Telem.FuelLcurr = data[seek]; seek += 1; // Уровень топлива в баке (0..255)
                    obj.Telem.FuelTemp = (sbyte)data[seek]; seek += 1; // Температура в баке
                    obj.Telem.AliveCheck = BitConverter.ToUInt64(data, seek); seek += 8;
                    obj.Telem.EnableCheck = BitConverter.ToUInt64(data, seek); seek += 8;


                    var ret = new byte[4]; // Ответ с таблицей запросов (requests)
                    uint req = 0;
                    req += (uint)(obj.Requests.Cameras[0] ?   0b00000000000000000000000000000001 : 0);
                    req += (uint)(obj.Requests.Cameras[1] ?   0b00000000000000000000000000000010 : 0);
                    req += (uint)(obj.Requests.Cameras[2] ?   0b00000000000000000000000000000100 : 0);
                    req += (uint)(obj.Requests.Cameras[3] ?   0b00000000000000000000000000001000 : 0);
                    req += (uint)(obj.Requests.Cameras[4] ?   0b00000000000000000000000000010000 : 0);
                    req += (uint)(obj.Requests.Cameras[5] ?   0b00000000000000000000000000100000 : 0);
                    req += (uint)(obj.Requests.Cameras[6] ?   0b00000000000000000000000001000000 : 0);
                    req += (uint)(obj.Requests.Cameras[7] ?   0b00000000000000000000000010000000 : 0);
                    req += (uint)(obj.Requests.Cameras[8] ?   0b00000000000000000000000100000000 : 0);
                    req += (uint)(obj.Requests.Cameras[9] ?   0b00000000000000000000001000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[0] ? 0b00000000000000000000010000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[1] ? 0b00000000000000000000100000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[2] ? 0b00000000000000000001000000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[3] ? 0b00000000000000000010000000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[4] ? 0b00000000000000000100000000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[5] ? 0b00000000000000001000000000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[6] ? 0b00000000000000010000000000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[7] ? 0b00000000000000100000000000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[8] ? 0b00000000000001000000000000000000 : 0);
                    req += (uint)(obj.Requests.RcRewrite[9] ? 0b00000000000010000000000000000000 : 0);
                    req += (uint)(obj.Requests.Command ?      0b00000000000100000000000000000000 : 0);
                    Array.Copy(BitConverter.GetBytes(req), 0, ret, 0, 4);
                    return ret;
                }
            // Это Борщелодка, пакет запроса перезаписи RC каналов
            case 1 when packType == 0x02:
                {
                    if (dataLen != 1) return retEmpty;

                    var ret = new byte[1 + 8]; // Ответ с таблицей запросов (requests)
                    var number = data[10];
                    if (obj.RcForWrite.Length <= number) return retEmpty;
                    var seek = 0;
                    ret[seek] = number; seek += 1; // номер пульта
                    ret[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[0])) * 500 + 500) / 5); seek += 1;
                    ret[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[1])) * 500 + 500) / 5); seek += 1;
                    ret[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[2])) * 500 + 500) / 5); seek += 1;
                    ret[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[3])) * 500 + 500) / 5); seek += 1;
                    ret[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[4])) * 500 + 500) / 5); seek += 1;
                    ret[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[5])) * 500 + 500) / 5); seek += 1;
                    ret[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[6])) * 500 + 500) / 5); seek += 1;
                    ret[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[7])) * 500 + 500) / 5); seek += 1;
                    return ret;
                }
            // Это Борщелодка, пакет запроса команды на исполнение
            case 1 when packType == 0x04:
                {
                    if (dataLen != 0) return retEmpty;

                    var ret = new byte[4]; // Ответ с таблицей значений (requests)
                    var seek = 0; // Смещение в пакете
                    Array.Copy(BitConverter.GetBytes(obj.Requests.Commands.Count > 0 ? obj.Requests.Commands.Dequeue() : 0), 0, ret, seek, 4); seek += 4;
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

        if (dataLen <= (1 + 8 + 4 + 4)) return retEmpty; // Полезные данные отсутствуют

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
                    return [];
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
    [JsonIgnore] public RcChannelsForWrite[] RcForWrite { get; set; } = [ new(), new(), new(), new(), new(), new(), new(), new(), new(), new() ]; // Кадры с пультов [10]
    [JsonIgnore] public CameraFrame[] CamFrames { get; set; } = [new(), new(), new(), new(), new(), new(), new(), new(), new(), new()]; // Кадры с камер изображения [10]
}

public class CameraFrame
{
    public static readonly Size DefFrameSizeH = new(1280, 640); // Максимальный размер фрейма (High)
    public static readonly Size DefFrameSizeM = new(640, 320); // Максимальный размер фрейма (Medium)
    public static readonly Size DefFrameSizeL = new(320, 160); // Максимальный размер фрейма (Low)
    public static readonly Size DefFrameSizeExL = new(160, 80); // Максимальный размер фрейма (ExtraLow)
    public Mat Frame { get; set; } // Текущий собраный кадр (для отправки клиентам)
    public int Fps { get; set; } // Частота входящих успешнодекодированных кадров

    //public const int Width = 960;
    //public const int Height = 540;
    public List<H264Decoder> H264Decoders = [];
    public long UdpFrameNumber { get; set; } // Текущий номер кадра (для сборки)
    public MemoryStream UdpFrame { get; set; } // Поток кадра из udp собранный из кусков
    private int _counter = 0;
    private DateTime _last = DateTime.Now;

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
        Frame = new Mat(new Size(0, 0), MatType.CV_8UC3, Scalar.Black);
    }

    public void DecodeFrame(int camNumber)
    {
        var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
        lock (items)
        {
            var dataArr = UdpFrame.ToArray();
            if (dataArr.Length <= 8)
            {
                UdpFrame = new MemoryStream();
                return;
            }
            var width = BitConverter.ToInt32(dataArr, 0);
            var height = BitConverter.ToInt32(dataArr, 4);
            var frame = dataArr[8..];
            var rgb = new RgbImage(ImageFormat.Rgb, width, height);
            var s = H264Decoder.Decode(frame, 0, frame.Length, true, out var state, ref rgb);
            if (s == false)
            {
                rgb.Dispose();
                UdpFrame = new MemoryStream();
                return;
            }

            using var mOrig = Mat.FromPixelData(rgb.Height, rgb.Width, MatType.CV_8UC3, rgb.GetBytes());
            using var mat4 = mOrig.Resize(DefFrameSizeH);

            _counter++;
            var time = DateTime.Now;
            if ((time - _last).TotalMilliseconds > 1000)
            {
                Fps = _counter;
                _last = time;
                _counter = 0;
                Console.WriteLine($"CAM_FPS={Fps:0}");
            }

            if (camNumber == 4 | camNumber == 5) // Камеры с круговым обзором, нужна коррекция
            {
                const float xmin = 0.25f;
                const float xmax = 0.75f;
                const float ymin = 0.20f;
                const float ymax = 0.80f;
                var srcPoints4 = new List<Point2f>
                    {
                        new(DefFrameSizeH.Width * xmin, DefFrameSizeH.Height * ymin),
                        new(DefFrameSizeH.Width * xmax, DefFrameSizeH.Height * ymin),
                        new(DefFrameSizeH.Width * xmin, DefFrameSizeH.Height * ymax),
                        new(DefFrameSizeH.Width * xmax, DefFrameSizeH.Height * ymax)
                    };
                var dstPoints4 = new List<Point2f>
                    {
                        new(0, 0),
                        new(DefFrameSizeH.Width, 0),
                        new(0, DefFrameSizeH.Height),
                        new(DefFrameSizeH.Width, DefFrameSizeH.Height)
                    };

                using var mat44 = new Mat();
                Cv2.WarpPerspective(mat4, mat44, Cv2.GetPerspectiveTransform(srcPoints4, dstPoints4), DefFrameSizeH);
                if (!Frame.IsDisposed) Frame.Dispose();
                Frame = mat44.Clone();
            }
            else
            {
                if (!Frame.IsDisposed) Frame.Dispose();
                Frame = mat4.Clone();
            }
            rgb.Dispose();
            UdpFrame = new MemoryStream();
        }
    }
}

public class GameObjectTelem // Параметры телеметрии
{
    public ushort[] RcChannels { get; set; } = new ushort[8]; // Значения каналов управления
    public float MBitObjectIn { get; set; } // Прием данных от сервера в мегабитах (на объекте)
    public float MBitServerIn { get; set; } // Прием данных на сервер в мегабитах (на сервере)
    public float MBitObjectOut { get; set; } // Передача данных от объекта в мегабитах (на объекте)
    public float MBitServerOut { get; set; } // Передача данных от сервера в мегабитах (на сервере)
    public float PingToServer { get; set; } // Пинг до сервера и обратно
    public float RollGrad { get; set; } // Угол наклона
    public float PitchGrad { get; set; } // Угол наклона
    public float YawGrad { get; set; } // Угол наклона
    public sbyte FuelTemp { get; set; } // Температура в баке
    public byte FuelLcurr { get; set; } // Уровень топлива (0..255)
    public ushort FuelVcurr { get; set; } // Объем топлива в литрах
    public byte CommandCount { get; set; } // Количество команд под исполнение
    public byte VideoFps { get; set; } // FPS видео с камер (0->5)
    public byte VideoQuality { get; set; } // Качество видео с камер (0->5)
    public byte[] CanEngineBits { get; set; } = new byte[5]; // статусы движка
    public ulong AliveCheck { get; set; } // статусы компонентов устройства
    public ulong EnableCheck { get; set; } // статусы использования/включения устройства
    [JsonIgnore] public int MBitServerInBytesCounter { get; set; } // Счетчик приема данных в байтах
    [JsonIgnore] public int MBitServerOutBytesCounter { get; set; } // Счетчик передачи данных в байтах
}
public class RcChannelsForWrite
{
    public float[] Values { get; set; } = new float[8]; // Значения каналов управления
}
public class PoolRequests // Список запросов данных с объекта
{
    [JsonIgnore] public DateTime[] RcRewriteLastTime { get; set; } =
    [
        DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue,
        DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue
    ]; // Время последнего запроса перезаписи пультов
    [JsonIgnore] public Queue<uint> Commands { get; set; } = new Queue<uint>(); // Список команд для изделия

    [JsonIgnore]
    public DateTime[] CamerasLastTime { get; set; } =
    [
        DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue,
        DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue
    ]; // Время последнего запроса телеметрии

    public bool Command => Commands.Count > 0; // Команды для изделия
    public bool[] RcRewrite // Время последнего запроса перезаписи пультов
    {
        get
        {
            var ret = new bool[10];
            var time = DateTime.Now;
            for (var i = 0; i < ret.Length; i++)
            {
                ret[i] = (time - RcRewriteLastTime[i]).TotalMilliseconds < 1000;
            }
            return ret;
        }
    }
    
    public bool[] Cameras
    {
        get
        {
            var ret = new bool[10];
            var time = DateTime.Now;
            for (var i =0;i< ret.Length;i++)
            {
                ret[i] = (time - CamerasLastTime[i]).TotalMilliseconds < 1000;
            }
            return ret;
        }
    } // Есть ли запрос изображений с камер
}