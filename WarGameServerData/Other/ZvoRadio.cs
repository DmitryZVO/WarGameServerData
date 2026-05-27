using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Sockets;

namespace WarGameServerData.Other;

public class ZvoRadio(string apIp, int apPort)
{
    public static bool PrintLog => true;

    public const int SizeCrc32 = 4; // Размер CRC32 блока данных
    public const int SizeHeaderStatic = 4; // Размер заголовка (не изменяемая часть)
    public const int SizeHeaderDynamic = 4; // Размер заголовка (изменяемая часть)
    public const int SizeMinimal = SizeHeaderStatic + SizeHeaderDynamic + SizeCrc32; // Размер заголовка (минимальный)

    public const int SizeHeaderDynamicAndCrc32 = SizeHeaderDynamic + SizeCrc32;  // Размер динамической части заголовка + CRC32
    public const bool PhySendPacketCrc32 = true; // Есть ли в конце пакета FCS_CRC

    public Func<byte[], Task> OnNewPacketAsync { get; set; } = async delegate { }; // делегат при получении нового пакета
    public ulong ApLanPacketsSend { get; private set; }
    public ulong ApLanPacketsRecv { get; private set; }
    public ulong ApRadioPacketsSend { get; private set; }
    public ulong ApRadioPacketsRecv { get; private set; }
    public ulong ApLanBytesSend { get; private set; }
    public ulong ApLanBytesRecv { get; private set; }
    public ulong ApRadioBytesSend { get; private set; }
    public ulong ApRadioBytesRecv { get; private set; }
    public ulong ApLanSendQueue { get; private set; }
    public ulong ApRadioSendQueue { get; private set; }
    public readonly RadioRepeater Repeater = new(); // ретранслятор

    private int recvGood, recvBad, recvHeadBad, recvAll, recvRest, sendAll;
    private readonly List<RadioChunk> PacketsVideoRecv = [];

    private readonly List<byte[]> RadioHeadersMaxRange = [];
    private readonly List<byte[]> RadioHeadersVideoEL = [];
    private readonly List<byte[]> RadioHeadersVideoL = [];
    private readonly List<byte[]> RadioHeadersVideoM = [];
    private readonly List<byte[]> RadioHeadersVideoH = [];

    private ushort PacketNumber = 0; // циклический номер пакета
    private readonly CancellationToken _ct = new();

    public class RadioRepeater
    {
        public DateTime _lastUpdate = DateTime.MinValue;
        public bool Alive => (DateTime.Now - _lastUpdate).TotalMilliseconds < 3000;
        public ulong WtoGbytesRecv { get; private set; }
        public ulong GtoWbytesRecv { get; private set; }
        public ulong WtoGpacketsRecv { get; private set; }
        public ulong GtoWpacketsRecv { get; private set; }
        public ulong SendQueue { get; private set; }

        public void UpdateValues(byte[] dataHb)
        {
            if (dataHb.Length < 44) return;

            var seek = 4;
            WtoGpacketsRecv = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(dataHb, seek)); seek += 8;
            GtoWpacketsRecv = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(dataHb, seek)); seek += 8;
            WtoGbytesRecv = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(dataHb, seek)); seek += 8;
            GtoWbytesRecv = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(dataHb, seek)); seek += 8;
            SendQueue = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(dataHb, seek)); //seek += 8;
            _lastUpdate = DateTime.Now;
        }
    }

    public void AddRadioHead(TransferMode mode, int repeats, byte[] radiohead)
    {
        switch (mode)
        {
            case TransferMode.MaxRange:
            default:
                lock (RadioHeadersMaxRange)
                {
                    for (var i = 0; i < repeats; i++)
                    {
                        RadioHeadersMaxRange.Add(radiohead);
                    }
                }
                break;
            case TransferMode.VideoEL:
                lock (RadioHeadersVideoEL)
                {
                    for (var i = 0; i < repeats; i++)
                    {
                        RadioHeadersVideoEL.Add(radiohead);
                    }
                }
                break;
            case TransferMode.VideoL:
                lock (RadioHeadersVideoL)
                {
                    for (var i = 0; i < repeats; i++)
                    {
                        RadioHeadersVideoL.Add(radiohead);
                    }
                }
                break;
            case TransferMode.VideoM:
                lock (RadioHeadersVideoM)
                {
                    for (var i = 0; i < repeats; i++)
                    {
                        RadioHeadersVideoM.Add(radiohead);
                    }
                }
                break;
            case TransferMode.VideoH:
                lock (RadioHeadersVideoH)
                {
                    for (var i = 0; i < repeats; i++)
                    {
                        RadioHeadersVideoH.Add(radiohead);
                    }
                }
                break;
        }
    }

    public async void StartAsync()
    {
        ThreadRecvAsync();
        ThreadRecvApAsync();

        while (!_ct.IsCancellationRequested)
        {
            await Task.Delay(1000, _ct);
            if (PrintLog) Console.WriteLine($"[v26-05-26] {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff} ZvoRadio: PacketsSend: [{sendAll:0}], AP_SEND={ApRadioPacketsSend:0} | PacketsRecv all/badH: [{recvAll:0}/{recvHeadBad:0}], AP_RECV={ApRadioPacketsRecv:0} | good/badData: [{recvGood:0}/{recvBad:0}] ({(1.0f - recvBad / (float)recvAll) * 100.0:0.00}%), recvRest={recvRest:0} (+{recvRest / (float)recvAll * 100.0:0.00}%)");
            recvAll = 0;
            recvHeadBad = 0;
            recvBad = 0;
            recvGood = 0;
            sendAll = 0;
            recvRest = 0;
        }
    }

    public async void StopAsync()
    {
        _ct.ThrowIfCancellationRequested();
        await Task.Delay(100);
    }

    public async void ThreadRecvAsync()
    {
        var _lastVideoPacket = DateTime.MinValue;

        var connect = new UdpClient(apPort);
        while (!_ct.IsCancellationRequested)
        {
            var result = await connect.ReceiveAsync(_ct);
            var sender = result.RemoteEndPoint;
            var data = result.Buffer;
            recvAll++;

            //if (!sender.Address.ToString().Equals(apIp)) continue; // Пакет не от точки связи
            var dataChunk = data[..(PhySendPacketCrc32 ? ^4 : ^0)]; // чанк ZVO

            if (data[0] == 0b11010100 && data[1] == 0x71) // это пакет от ретранслятора
            {
                Repeater.UpdateValues(dataChunk); /// Обновляем данные
                continue;
            }

            if (data.Length < SizeMinimal)
            {
                recvHeadBad++;
                continue; // Огрызок пакета
            }

            var chunk = new RadioChunk(dataChunk);

            if (chunk.Check != ChunkState.OK)
            {
                recvHeadBad++;
                continue;
            }

            var packType = chunk.GetNormalData()[0] & 0b01111111;
            if (packType < 0x10) continue; // это пакет от сервера, игнорируем

            if (chunk.PacketIsValid == false)
            {
                recvBad++;
                continue;
            }

            recvGood++;

            // Пакеты с видео требуют последовательной обработки
            if (packType == 0x18)
            {
                if (PacketsVideoRecv.Any(x => x.PacketNumber == chunk.PacketNumber)) continue; // Такой пакет уже был
                PacketsVideoRecv.Add(chunk);
                PacketsVideoRecv.Sort((a, b) => a.PacketNumber - b.PacketNumber);
                if (PacketsVideoRecv.Count > 10) // более 10 пакетов - уже начинаем отправку
                {
                    var pack = PacketsVideoRecv.First();
                    PacketsVideoRecv.RemoveAll(x => x.PacketNumber == pack.PacketNumber); // удаляем все дубли
                    await OnNewPacketAsync.Invoke(pack.GetNormalData()); // отправляем пакет на исполнение
                    PacketsVideoRecv.RemoveAll(x => x.PacketNumber > 65000); // удаляем все старые пакеты на переходе более 65000
                    //Console.WriteLine($"recv video packet {pack.PacketNumber:0}, Q={PacketsVideoRecv.Count:0}"); // это пакеты с видео
                }
                _lastVideoPacket = DateTime.Now;
            }
            else // для пакетов не с видео обрабатывается ВСЕ (т.к. это телеметрия) и повторы и дубли
            {
                if ((DateTime.Now - _lastVideoPacket).TotalMilliseconds > 3000)
                {
                    PacketsVideoRecv.Clear();
                }
                await OnNewPacketAsync.Invoke(chunk.GetNormalData());
            }
        }
    }

    public async void ThreadRecvApAsync()
    {
        var connect = new UdpClient(apPort + 1);
        while (!_ct.IsCancellationRequested)
        {
            var result = await connect.ReceiveAsync(_ct);
            var sender = result.RemoteEndPoint;
            var data = result.Buffer;

            if (!sender.Address.ToString().Equals(apIp)) continue; // Пакет не от точки связи
            if (data.Length < 82) continue; // Огрызок пакета

            var seek = 2;
            ApLanPacketsSend = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
            ApLanPacketsRecv = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
            ApLanBytesSend = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
            ApLanBytesRecv = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
            ApRadioPacketsSend = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
            ApRadioPacketsRecv = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
            ApRadioBytesSend = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
            ApRadioBytesRecv = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
            ApRadioSendQueue = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
            ApLanSendQueue = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek));
        }
    }

    public enum TransferMode
    {
        None = 0,
        MaxRange = 1,
        VideoEL = 2,
        VideoL = 3,
        VideoM = 4,
        VideoH = 5,
    }

    public enum ChunkState
    {
        OK = 0,
        ErrorZvo = 1,
        ErrorHeaderCrc = 2,
        ErrorSize = 3,
        ErrorDataCrc = 4,
    }

    public async Task Send(byte[] data, TransferMode mode) // Отправить пакет в радиоэфир
    {
        var send = new RadioChunk
        {
            PacketNumber = PacketNumber, // Номер пакета
            DataSizeOriginal = (ushort)data.Length, /// Полезные данные
            TransferMode = mode, // тип отправки
        };
        if (!send.SetNormalData(data)) // Пишем нормальные данные
        {
            //Console.WriteLine($"data ZVO size packet is BIG! len={data.Length:0}, max={SizeMacroBlock * 2:0}");
        }

        using var udp = new UdpClient();
        var radio = RadioHeadersMaxRange;

        switch (send.TransferMode)
        {
            default:
            case TransferMode.None:
                return;
            case TransferMode.MaxRange:
                radio = RadioHeadersMaxRange;
                break;
            case TransferMode.VideoEL:
                radio = RadioHeadersVideoEL;
                break;
            case TransferMode.VideoL:
                radio = RadioHeadersVideoL;
                break;
            case TransferMode.VideoM:
                radio = RadioHeadersVideoM;
                break;
            case TransferMode.VideoH:
                radio = RadioHeadersVideoH;
                break;
        }

        for (var i = 0; i < radio.Count; i++)
        {
            using var ms = new MemoryStream();
            lock (radio)
            {
                ms.Write(radio[0]); // Записываем заголовок Radiotap для инжектирования
            }
            ms.Write(send.GetArray); // Записываем оставшийся блок данных и CRC
            var d = ms.ToArray();
            await udp.SendAsync(d, apIp, apPort, _ct);
            sendAll++;
        }

        PacketNumber++;
    }

    public class RadioChunk // Минимальный пакет радио (для отправки)
    {
        public ChunkState Check { get; }
        public TransferMode TransferMode { get; set; }

        private readonly byte[] array = new byte[10000]; // Чанк данных c XOR

        public ushort PacketNumber { get { return BitConverter.ToUInt16(array, 4); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 4, 2); } }
        public ushort DataSizeOriginal { get { return BitConverter.ToUInt16(array, 6); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 6, 2); } }
        public bool PacketIsValid { get { if (!DynamicHeaderIsValid()) return false; if (!DataIsValid()) return false; return true; } }
        public byte[] Data => GetNormalData();
        public byte[] GetArray => array[..(SizeMinimal + DataSizeOriginal + SizeCrc32)];
        public bool SetNormalData(byte[] data) // +
        {
            Array.Copy(CRC32(array[SizeHeaderStatic..(SizeHeaderStatic + SizeHeaderDynamic)]), 0, array, SizeHeaderStatic + SizeHeaderDynamic, SizeCrc32); // записываем CRC32 в заголовок 0
            Array.Copy(data, 0, array, SizeMinimal, data.Length);
            Array.Copy(CRC32(data), 0, array, SizeMinimal + data.Length, SizeCrc32);
            return true;
        }

        public byte[] GetNormalData()
        {
            return array[SizeMinimal..(SizeMinimal + DataSizeOriginal)];
        }
        public bool DynamicHeaderIsValid()
        {
            var arr = GetDynamicHeaderAndCrc32();
            return CRC32(arr[..SizeHeaderDynamic]).SequenceEqual(arr[SizeHeaderDynamic..]);
        }
        private byte[] GetDynamicHeaderAndCrc32()
        {
            var start = SizeHeaderStatic;
            var end = start + SizeHeaderDynamicAndCrc32;
            return array[start..end];
        }
        public bool DataIsValid()
        {
            var arr = GetDataAndCrc32();
            return CRC32(arr[..DataSizeOriginal]).SequenceEqual(arr[DataSizeOriginal..]);
        }

        private byte[] GetDataAndCrc32()
        {
            var start = SizeMinimal;
            var end = start + DataSizeOriginal + SizeCrc32;
            return array[start..end];
        }

        public RadioChunk()
        {
            // Служебные не изменяемые байты (статическая часть) = 4 байта
            array[0] = 0b11010100; // [PKT_TYPE_CTRL|PKT_SUBTYPE_CTRL_ACK]
            array[1] = 0x70; // Идентификатор ZVO пакета (для идентификации ZVO пакетов)
            array[2] = 0x00; // Duraton/ID (использовать нельзя, меняется при пересылке)
            array[3] = 0x00; // Duraton/ID (использовать нельзя, меняется при пересылке)

            // Служебный заголовок (изменяемая часть) = 8 байт с CRC
            array[4] = 0; array[5] = 0; // Номер пакета (UInt16)
            array[6] = 0; array[7] = 0; // Длина полезные данных (UInt16) (чистых байт, без XOR)
            array[8] = 0; array[9] = 0; array[10] = 0; array[11] = 0; // CRC32 заголовка [8..11]
            // Тело полезной нагрузки 
        }

        public RadioChunk(byte[] data)
        {
            if (data.Length < SizeMinimal)
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)} LEN_PACKET_ERROR, len={data.Length}!={SizeFull}\n");
                Check = ChunkState.ErrorSize;
                return;
            }

            Array.Copy(data, array, data.Length); // Копируем данные из пакета
            array[2] = 0x00; // обязательная перезапись, т.к. меняется при пересылке
            array[3] = 0x00; // обязательная перезапись, т.к. меняется при пересылке

            if (array[1] != 0x70)
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)}, ZVO_ERROR\n");
                Check = ChunkState.ErrorZvo;
                return;
            }

            if (DynamicHeaderIsValid() == false)
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)}, HEADER_CRC_ERROR\n");
                Check = ChunkState.ErrorHeaderCrc;
                return;
            }

            var lenData = BitConverter.ToUInt16(array, 6);
            if (lenData != data.Length - SizeCrc32 - SizeMinimal)
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)} LEN_DATA_ERROR, len={lenData}>{SizeMacroBlock * 2}\n");
                Check = ChunkState.ErrorSize;
                return;
            }

            //if (DataIsValid() == false)
            //{
            //    Check = ChunkState.ErrorDataCrc;
            //    return;
            //}

            Check = ChunkState.OK;
        }
    }

    public static byte[] CRC32(IEnumerable<byte> bytes)
    {
        var crcTable = new uint[256];
        uint crc;

        for (uint i = 0; i < 256; i++)
        {
            crc = i;
            for (uint j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;

            crcTable[i] = crc;
        }

        crc = bytes.Aggregate(0xFFFFFFFF, (current, s) => crcTable[(current ^ s) & 0xFF] ^ (current >> 8));

        crc ^= 0xFFFFFFFF;
        return BitConverter.GetBytes(crc);
    }
    public static bool CompressZipIfSmall(byte[] data, out byte[] smaller)
    {
        using var msIn = new MemoryStream(data);
        using var msOut = new MemoryStream();
        using (var ds = new DeflateStream(msOut, CompressionLevel.SmallestSize))
        {
            msIn.CopyTo(ds);
        }
        var zip = msOut.ToArray();
        if (zip.Length < data.Length)
        {
            smaller = zip;
            return true;
        }
        smaller = data;
        return false;
    }

    public static byte[] CompressZip(byte[] data)
    {
        using var msIn = new MemoryStream(data);
        using var msOut = new MemoryStream();
        using (var ds = new DeflateStream(msOut, CompressionLevel.SmallestSize))
        {
            msIn.CopyTo(ds);
        }
        return msOut.ToArray();
    }

    public static byte[] DecompressZip(byte[] dataZip)
    {
        try
        {
            using var msIn = new MemoryStream(dataZip);
            using var msOut = new MemoryStream();
            using (var ds = new DeflateStream(msIn, CompressionMode.Decompress))
            {
                ds.CopyTo(msOut);
            }
            return msOut.ToArray();
        }
        catch
        {
            return [];
        }
    }
}
