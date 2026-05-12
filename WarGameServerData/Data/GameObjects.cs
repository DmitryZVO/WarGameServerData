using H264Sharp;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using WarGameServerData.Model;
using WarGameServerData.Other;

namespace WarGameServerData.Data;

public class GameObjects
{
    public readonly static int PortServerRequests = 8000; // Входящий порт от сервера для статуса запросов
    public readonly static int PortServerRcRewrite = 8001; // Входящий порт от сервера для перезаписи пульта
    public readonly static int PortServerGetCommand = 8002; // Входящий порт от сервера для запроса команд на исполнение

    public async void SendRequestsAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(50, ct); // 20гц

                foreach (var item in Items)
                {
                    await SendRequestsAsync(item);
                }
            }
            catch
            {
                //
            }
        }
    }

    public static async Task SendRequestsAsync(GameObject obj)
    {
        if (obj.Ip.Equals(string.Empty)) return;

        var data = new byte[4]; // Ответ с таблицей запросов (requests)
        uint req = 0;
        req += (uint)(obj.Requests.Cameras[0] ? 0b00000000000000000000000000000001 : 0);
        req += (uint)(obj.Requests.Cameras[1] ? 0b00000000000000000000000000000010 : 0);
        req += (uint)(obj.Requests.Cameras[2] ? 0b00000000000000000000000000000100 : 0);
        req += (uint)(obj.Requests.Cameras[3] ? 0b00000000000000000000000000001000 : 0);
        req += (uint)(obj.Requests.Cameras[4] ? 0b00000000000000000000000000010000 : 0);
        req += (uint)(obj.Requests.Cameras[5] ? 0b00000000000000000000000000100000 : 0);
        req += (uint)(obj.Requests.Cameras[6] ? 0b00000000000000000000000001000000 : 0);
        req += (uint)(obj.Requests.Cameras[7] ? 0b00000000000000000000000010000000 : 0);
        req += (uint)(obj.Requests.Cameras[8] ? 0b00000000000000000000000100000000 : 0);
        req += (uint)(obj.Requests.Cameras[9] ? 0b00000000000000000000001000000000 : 0);
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
        req += (uint)(obj.Requests.Command ? 0b00000000000100000000000000000000 : 0);
        Array.Copy(BitConverter.GetBytes(req), 0, data, 0, 4);

        await new UdpClient().SendAsync(data, obj.Ip, PortServerRequests, new CancellationTokenSource(100).Token);
        Core.IoC.Services.GetRequiredService<ZvoRadio>().Send(data, ZvoRadio.TransferMode.MaxRange);
        obj.Telem.MBitServerOutBytesCounter += data.Length;
    }
    public static async Task SendCommandAsync(GameObject obj)
    {
        if (obj.Ip.Equals(string.Empty)) return;

        var data = new byte[4]; // Ответ с таблицей значений (requests)
        var seek = 0; // Смещение в пакете
        Array.Copy(BitConverter.GetBytes(obj.Requests.Commands.Count > 0 ? obj.Requests.Commands.Dequeue() : 0), 0, data, seek, 4);

        await new UdpClient().SendAsync(data, obj.Ip, PortServerGetCommand, new CancellationTokenSource(100).Token);
        Core.IoC.Services.GetRequiredService<ZvoRadio>().Send(data, ZvoRadio.TransferMode.MaxRange);
        obj.Telem.MBitServerOutBytesCounter += data.Length;
    }

    public static async Task SendRcRewriteAsync(GameObject obj, byte number)
    {
        if (obj.Ip.Equals(string.Empty)) return;

        var data = new byte[1 + 8]; // Ответ с таблицей запросов (requests)
        if (obj.RcForWrite.Length <= number) return;
        var seek = 0;
        data[seek] = number; seek += 1; // номер пульта
        data[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[0])) * 500 + 500) / 5); seek += 1;
        data[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[1])) * 500 + 500) / 5); seek += 1;
        data[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[2])) * 500 + 500) / 5); seek += 1;
        data[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[3])) * 500 + 500) / 5); seek += 1;
        data[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[4])) * 500 + 500) / 5); seek += 1;
        data[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[5])) * 500 + 500) / 5); seek += 1;
        data[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[6])) * 500 + 500) / 5); seek += 1;
        data[seek] = (byte)((Math.Min(1.0f, Math.Max(-1.0f, obj.RcForWrite[number].Values[7])) * 500 + 500) / 5);

        var send = await new UdpClient().SendAsync(data, obj.Ip, PortServerRcRewrite, new CancellationTokenSource(100).Token);
        Core.IoC.Services.GetRequiredService<ZvoRadio>().Send(data, ZvoRadio.TransferMode.MaxRange);
        obj.Telem.MBitServerOutBytesCounter += send;
    }
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

    public async Task ParseUdpPacketAsync(string sender, byte[] data)
    {
        var retEmpty = Array.Empty<byte>();

        // Проверка на пакет ZVO
        if (data.Length < 10) return; // ZVO пакет не может быть менее 10 байт
        if (data[0] != 0x70) return; // это не ZVO пакет
        if (data[1] != 0x70) return; // это не ZVO пакет
        var type = (int)data[2]; // Тип объекта
        var id = (int)BitConverter.ToUInt32(data, 3); // ID объекта
        var packType = (int)data[7]; // тип входящего пакета
        var dataLen = (int)BitConverter.ToUInt16(data, 8); ; // длинна полезных данных
        if (data.Length < dataLen + 10) return; // Динна пакета не совпадает

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

        if (obj.Ip.Equals(sender) == false) obj.Ip = sender;

        switch (type)
        {
            // Разбираем входящий пакет
            // Это Борщелодка, пакет HeartBeat + Telem
            case 1 when packType == 0x00:
                {
                    if (dataLen < ((4 * 2) + (2 * 3) + (1 * 8) + (2 * 3) + (1 * 2) + 5 + 2 + 1 + 1 + (8 * 2) + (4 * 2) + 1 + (2 * 2))) return; // не верный размер пакета
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
                    obj.Telem.QualityMeshGroundToWater = BitConverter.ToSingle(data, seek); seek += 4;
                    obj.Telem.QualityZvoGroundToWater = BitConverter.ToSingle(data, seek); seek += 4;
                    obj.Telem.QueueZvoWaterToGroundSend = data[seek]; seek += 1;
                    obj.Telem.MbitsZvoWaterToGroundSend = BitConverter.ToUInt16(data, seek) / (float)ushort.MaxValue; seek += 2;
                    obj.Telem.MbitsZvoWaterToGroundRecv = BitConverter.ToUInt16(data, seek) / (float)ushort.MaxValue; seek += 2;
                    return;
                }
            // Это Борщелодка, пакет запроса перезаписи RC каналов
            case 1 when packType == 0x02:
                {
                    if (dataLen < 1) return;
                    await SendRcRewriteAsync(obj, data[10]);
                    return;
                }
            // Это Борщелодка, пакет запроса команды на исполнение
            case 1 when packType == 0x04:
                {
                    if (dataLen < 0) return;
                    await SendCommandAsync(obj);
                    return;
                }

            default:
                return;
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
    [JsonIgnore] public string Ip { get; set; } = string.Empty; // IP адрес устройства
    [JsonIgnore] public GameObjectTelem Telem { get; set; } = new(); // Телеметрия объекта
    [JsonIgnore] public PoolRequests Requests { get; set; } = new(); // Запросы данных с объекта
    [JsonIgnore] public RcChannelsForWrite[] RcForWrite { get; set; } // Кадры с пультов [10]
    [JsonIgnore] public CameraFrame[] CamFrames { get; set; } // Кадры с камер изображения [10]
    public GameObject()
    {
        RcForWrite = new RcChannelsForWrite[10];
        for (var i = 0; i < RcForWrite.Length; i++)
        {
            RcForWrite[i] = new();
        }

        CamFrames = new CameraFrame[10];
        for (var i = 0; i < CamFrames.Length; i++) 
        {
            CamFrames[i] = new(this, i);
        }
    }
}

public class CameraFrame
{
    public static readonly Size DefFrameToSend = new(1920, 1080); // Размер кадря для пересылки на клиента
    //public static readonly Size DefFrameToSend = new(960, 540); // Размер кадря для пересылки на клиента
    public static int MaxChunks => (DefFrameSizeH.Width / H264ChunkDecoder.BlockSize.Width) * (DefFrameSizeH.Height / H264ChunkDecoder.BlockSize.Height);
    public static readonly Size DefFrameSizeH = new(1920, 1440); // Максимальный размер фрейма (High)
    public static readonly Size DefFrameSizeM = new(1280, 720); // Максимальный размер фрейма (Medium)
    public static readonly Size DefFrameSizeL = new(640, 480); // Максимальный размер фрейма (Low)
    public static readonly Size DefFrameSizeExL = new(640, 320); // Максимальный размер фрейма (ExtraLow)
    public int Fps { get; set; } // Частота входящих успешнодекодированных кадров

    public byte[] FrameToSend { get; set; } // Текущий собраный кадр (для отправки клиентам)

    public List<H264ChunkDecoder> H264ChunkDecoders = [];
    public readonly int Number; // Номер камеры
    public readonly GameObject Object; // Игровой объект

    private Mat Frame { get; set; } // Текущий собраный кадр (для отправки клиентам)

    public CameraFrame(GameObject obj, int number)
    {
        FrameToSend = [];

        Number = number;
        Object = obj;
        Frame = new Mat();

        for (var i = 0; i < MaxChunks; i++)
        {
            H264ChunkDecoders.Add(new(this, i));
        }
        H264ChunkDecoders.ForEach(x => x.StartAsync());
        
        UpdateCutFrameAsync();
    }

    public async void UpdateCutFrameAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(30, ct); // 30 кадров секунду

            if (H264ChunkDecoders.Any(x=>x.IsUpdate == true)) UpdateFrame();
        }
    }

    private void UpdateFrame()
    {
        lock (Frame)
        {
            var sizeFrame = Object.Telem.VideoQuality switch
            {
                3 => DefFrameSizeH,
                2 => DefFrameSizeM,
                1 => DefFrameSizeL,
                _ => DefFrameSizeExL,
            };
            if (!Frame.Size().Equals(sizeFrame))
            {
                if (!Frame.IsDisposed) Frame.Dispose();
                Frame = new Mat(sizeFrame, MatType.CV_8UC3, Scalar.Black);
            }

            int i = 0;
            for (var sy = 0; sy < sizeFrame.Height / H264ChunkDecoder.BlockSize.Height; sy++) 
            {
                for (var sx = 0; sx < sizeFrame.Width / H264ChunkDecoder.BlockSize.Width; sx++)
                {
                    var chunk = H264ChunkDecoders[i].FrameChunk;
                    i++;

                    lock (chunk)
                    {
                        if (chunk.IsDisposed || chunk.Empty()) continue;
                        Cv2.CopyTo(chunk, Frame.SubMat(new Rect(sx * H264ChunkDecoder.BlockSize.Width, sy * H264ChunkDecoder.BlockSize.Height, H264ChunkDecoder.BlockSize.Width, H264ChunkDecoder.BlockSize.Height)));
                    }
                }
            }
            lock (FrameToSend)
            {
                var res = new Mat();
                var sizeFrameSend = Object.Telem.VideoQuality switch
                {
                    3 => DefFrameToSend,
                    2 => DefFrameSizeM,
                    1 => DefFrameSizeL,
                    _ => DefFrameSizeExL,
                };
                Cv2.Resize(Frame, res, sizeFrameSend);
                FrameToSend = res.ToBytes(".jpeg");
                res.Dispose();
            }
        }
    }

    /*
    var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
    lock (items)
    {
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
    */
}

public class H264ChunkDecoder
{
    public Mat FrameChunk = new();
    public readonly static Size BlockSize = new(640, 16); // Должно быть кратно 16x16 (это минимальный блок кодирования h264 по умолчанию) 160x160 = 10x10 блоков, 80x80 = 5x5 блоков, 64x64 = 4x4 блока, 32x32 = 2x2 блока

    public int Number { get; } // Номер чанка
    public long UdpFrameNumber { get; set; } // Текущий номер кадра (для сборки)
    public MemoryStream UdpFrame { get; set; } // Поток кадра из udp собранный из кусков
    public bool IsUpdate => (DateTime.Now - LastUpdate).TotalMilliseconds <= 30; // Проверка на обновление чанка

    private readonly H264Decoder _decoder;
    private readonly CameraFrame _camera;
    private DateTime LastUpdate { get; set; } = DateTime.MinValue;


    public H264ChunkDecoder(CameraFrame camera, int number)
    {
        Number = number;
        UdpFrame = new MemoryStream();

        _camera = camera;
        _decoder = new();

        // Инициализация декодера
        var decParam = new TagSVCDecodingParam
        {
            uiTargetDqLayer = 0xFF,
            eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_DISABLE,
            bParseOnly = false,
        };
        decParam.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_DEFAULT;
        _decoder.Initialize(decParam);
    }

    public async void StartAsync(CancellationToken ct = default)
    {
        // Читаем порт для чанков
        var connect = new UdpClient(LanIn.UdpPortCamera + _camera.Number * 1000 + Number); // Конкретный порт под конкретный чанк
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Получение данных
                var result = await connect.ReceiveAsync(ct);
                var client = result.RemoteEndPoint;
                var data = result.Buffer;
                // Парсинг входящего пакета
                ParseUdpChunkPacket(data);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        connect.Close();
    }

    public void ParseUdpChunkPacket(byte[] data)
    {
        var retEmpty = Array.Empty<byte>();

        // Проверка на пакет ZVO
        const int headLen = 6;
        if (data.Length < headLen) return; // ZVO пакет не может быть менее 6 байт
        var seek = 0;
        var id = data[seek]; seek += 1; // ID объекта
        var dataLen = (int)BitConverter.ToUInt16(data, seek); seek += 2; // длинна полезных данных
        if (data.Length != dataLen + headLen)
        {
            return; // Динна пакета не совпадает
        }

        if (dataLen <= 0) return; // Полезные данные отсутствуют

        // Находим или создаем новый игровой объект
        GameObject? obj;
        var time = DateTime.Now;
        var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
        lock (items)
        {
            obj = items.Find(x => x.Id == id);
        }
        if (obj == null) return; // Такого объекта у нас нет
                                 // Обновляем телеметрические данные
        obj.LastTime = time;
        obj.Telem.MBitServerInBytesCounter += data.Length; // Обновляем счетчик принятых байт на сервер от объекта

        var frameNumber = data[seek]; seek += 1; // Номер кадра
        var frameCut = data[seek]; seek += 1; // Номер куска
        var frameCutAll = data[seek]; seek += 1; // Всего кусков

        if (UdpFrameNumber != frameNumber && UdpFrame.Length > 0) // Новый кадр, пора пересоздавать матрицу кадра
        {
            DecodeChunk();
        }
        UdpFrameNumber = frameNumber;
        UdpFrame.Write(data, seek, dataLen); // Записываем кусок данных
        if (frameCut == frameCutAll) // Это финальный кусок, пора пересоздавать матрицу кадра
        {
            DecodeChunk();
        }
    }

    public void DecodeChunk()
    {
        var dataArr = UdpFrame.ToArray();
        if (dataArr.Length <= 0)
        {
            UdpFrame = new MemoryStream();
            return;
        }
        var rgb = new RgbImage(ImageFormat.Rgb, BlockSize.Width, BlockSize.Height);
        if (_decoder.Decode(dataArr, 0, dataArr.Length, true, out var _, ref rgb) != false)
        {
            lock (FrameChunk)
            {
                FrameChunk.Dispose();
                FrameChunk = Mat.FromPixelData(rgb.Height, rgb.Width, MatType.CV_8UC3, rgb.GetBytes());
                LastUpdate = DateTime.Now;
            }

        }
        rgb.Dispose();
        UdpFrame = new MemoryStream();
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
    public float QualityMeshWaterToGround { get; set; }  // Качество связи через МЭШ с воды до сервера
    public float QualityMeshGroundToWater { get; set; }  // Качество связи через МЭШ с сервера до воды
    public float QualityZvoWaterToGround { get; set; }  // Качество связи через ZVO с воды до сервера
    public float QualityZvoGroundToWater { get; set; }  // Качество связи через ZVO с сервера до воды
    public byte QueueZvoWaterToGroundSend { get; set; } // Очередь отправки пакетов с воды до сервера
    public float MbitsZvoWaterToGroundSend { get; set; } // Отправка с воды до сервера в мегабитах
    public float MbitsZvoWaterToGroundRecv { get; set; } // Прием с воды до сервера в мегабитах
    public byte QueueZvoGroundToWaterSend { get; set; } // Очередь отправки пакетов с сервера до воды
    public float MbitsZvoGroundToWaterSend { get; set; } // Отправка с сервера до воды в мегабитах
    public float MbitsZvoGroundToWaterRecv { get; set; } // Прием с сервера до воды в мегабитах
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