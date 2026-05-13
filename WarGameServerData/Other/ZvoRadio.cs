using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Sockets;

namespace WarGameServerData.Other;

public class ZvoRadio(string apIp, ushort apPort)
{
    public static bool PrintLog => false;
    public static int TimeOutCreateMs => 200;

    public const byte SizeHeader = 8; // Размер заголовка (без CRC)
    public const byte SizeHeaderCrc16 = 2;  // Размер CRC16 блока заголовка
    public const byte SizeDataCrc32 = 4; // Размер CRC32 блока данных
    public const byte SizeBlockXor = 3; // Какими блоками кодируем XOR для восстановления
    public const int DataCrc32SizeXored = 8; // CRC32 данных кодируется каждый байт + CRC8
    public static byte BigSizeBlockXor => (SizeBlockXor + 1); // Общий размер блока XOR вместе с байтами CRC8
    public static int SeekStart => (SizeHeader + SizeHeaderCrc16); // Стартовое смещение от начала заголовка

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

    public Action<byte[]> OnGetPacketAsync { get; set; } = async delegate { }; // делегат при получении нового пакета

    private readonly List<byte[]> RadioHeadersMaxRange = [];
    private readonly List<byte[]> RadioHeadersVideoEL = [];
    private readonly List<byte[]> RadioHeadersVideoL = [];
    private readonly List<byte[]> RadioHeadersVideoM = [];
    private readonly List<byte[]> RadioHeadersVideoH = [];

    private byte PacketNumber = 0; // циклический номер пакета

    private readonly ConcurrentQueue<RadioChunk> ChunksSend = new(); // Чанки для оправки
    private readonly List<RecvPacket> PacketsRecv = []; // Собраные пакеты
    private readonly CancellationToken _ct = new();

    public static int SizeRoundedData(int data)
    {
        return ((data / SizeBlockXor + (data % SizeBlockXor > 0 ? 1 : 0)) * SizeBlockXor);
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

    public void Send(byte[] data, TransferMode mode) // Отправить пакет в радиоэфир
    {
        var chunk = new RadioChunk
        {
            PacketNumber = PacketNumber, // Номер пакета
            DataSizeOriginal = (ushort)(data.Length), /// Полезные данные
            TransferMode = mode, // тип отправки
        };
        chunk.CalcAndWriteHeaderCrc16(); // Заполняем CRC16 заголовка
        chunk.WriteNormalData(data); // Пишем нормальные данные
        chunk.CalcAndWriteDataCrc32(); // Заполняем CRC32 данных
        var a = chunk.DataIsValid;
        var b = chunk.Data;
        var c = chunk.GetNormalData();
        var d = chunk.Check;
        var e = chunk.DataSizeXored;
        var f = chunk.GetArray;
        ChunksSend.Enqueue(chunk);

        if (PrintLog) Console.WriteLine(Convert.ToHexString(chunk.GetArray));
        PacketNumber++;
    }

    public async void StartAsync()
    {
        ThreadSendAsync();
        ThreadRecvAsync();
        ThreadRecvApAsync();
        ThreadRecvActionAsync();

        while (!_ct.IsCancellationRequested)
        {
            await Task.Delay(1000, _ct);
            if (PrintLog) Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff} ZvoRadio: ForSend={ChunksSend.Count:0}, PacketsRecv={PacketsRecv.Count:0}");
        }
    }

    public async void StopAsync()
    {
        _ct.ThrowIfCancellationRequested();
        await Task.Delay(100);
    }

    public async void ThreadRecvAsync()
    {
        var connect = new UdpClient(apPort);
        while (!_ct.IsCancellationRequested)
        {
            var result = await connect.ReceiveAsync(_ct);
            var sender = result.RemoteEndPoint;
            var data = result.Buffer;

            if (!sender.Address.ToString().Equals(apIp)) continue; // Пакет не от точки связи
            if (data.Length < SeekStart + SizeDataCrc32) continue; // Огрызок пакета
            var dataChunk = data[..^4]; // чанк ZVO
            var chunk = new RadioChunk(dataChunk);
            if (chunk.Check != ChunkState.OK) continue;

            lock (PacketsRecv)
            {
                var packet = PacketsRecv.Find(x => x.Number == chunk.PacketNumber); // Ищем пакеты с таким номером
                if (packet == null)
                {
                    //Console.WriteLine($"{DateTime.Now:yyyy-mm-dd HH:mm:ss.ffff} add new packet {chunk.PacketNumber}, valid={chunk.DataIsValid}");
                    PacketsRecv.Add(new RecvPacket(chunk));
                }
                else
                {
                    //var thisDouble = packet.Chunk.GetXorData().SequenceEqual(chunk.GetXorData());
                    //Console.WriteLine($"{DateTime.Now:yyyy-mm-dd HH:mm:ss.ffff} add repeat packet {chunk.PacketNumber}, valid={chunk.DataIsValid}, double={thisDouble}");
                    packet.AddRepeat(chunk);
                }
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
            if (data.Length < SeekStart + SizeDataCrc32) continue; // Огрызок пакета

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

    public async void ThreadRecvActionAsync()
    {
        while (!_ct.IsCancellationRequested)
        {
            var removed = 0;
            List<RecvPacket> packets;

            lock (PacketsRecv)
            {
                packets = PacketsRecv.FindAll(x => x.OK);
            }

            foreach (var i in packets)
            {
                OnGetPacketAsync.Invoke(i.GetPacket());
            }

            lock (PacketsRecv)
            {
                foreach (var i in packets)
                {
                    removed += PacketsRecv.Remove(i) ? 1 : 0;
                }
                removed += PacketsRecv.RemoveAll(x => (DateTime.Now - x.LastUpdate).TotalMilliseconds >= TimeOutCreateMs);
            }

            if (removed == 0) await Task.Delay(10, _ct);
        }
    }

    public class RecvPacket(RadioChunk chunk)
    {
        public DateTime LastUpdate = DateTime.Now;
        public bool OK => Chunk.DataIsValid;
        public byte Number { get; set; } = chunk.PacketNumber;
        public RadioChunk Chunk { get; set; } = chunk;

        public void AddRepeat(RadioChunk repeat)
        {
            LastUpdate = DateTime.Now;

            if (Chunk.DataIsValid) return;

            if (repeat.DataIsValid)
            {
                Chunk = repeat;
                return;
            }

            //var v = Chunk.DataIsValid;
            Chunk.WriteNewXorData(repeat.GetXorData());
            //Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff} old={v}, rep={repeat.DataIsValid}, new={Chunk.DataIsValid}");
        }

        public byte[] GetPacket()
        {
            return Chunk.GetNormalData()[..];
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
    }

    public async void ThreadSendAsync()
    {
        while (!_ct.IsCancellationRequested)
        {

            ChunksSend.TryDequeue(out RadioChunk? send);

            if (send == null)
            {
                await Task.Delay(10, _ct);
                continue;
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
                    ms.Write(radio[i]);
                }
                ms.Write(send.GetArray);
                await udp.SendAsync(ms.ToArray(), apIp, apPort, _ct);
            }
        }
    }

    public class RadioChunk // Минимальный пакет радио (для отправки)
    {
        public ChunkState Check { get; }
        public TransferMode TransferMode { get; set; }
        public byte PacketNumber { get { return array[4]; } set { array[4] = value; } }
        public ushort DataSizeOriginal { get { return BitConverter.ToUInt16(array, 5); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 5, 2); } }
        public bool DataIsValid => CheckXorData() & DataCrc32Check();
        public byte[] Data => GetNormalData();
        public byte[] GetArray => array[..(SeekStart + (SizeRoundedData(DataSizeOriginal) / SizeBlockXor) * BigSizeBlockXor + DataCrc32SizeXored)];
        public int DataSizeXored => (SizeRoundedData(DataSizeOriginal) / SizeBlockXor) * BigSizeBlockXor;
        public int DataAndCrc32SizeXored { get { return (DataSizeXored + DataCrc32SizeXored); } }

        private readonly byte[] array = new byte[SizeHeader + SizeHeaderCrc16 + 10000]; // Чанк данных c XOR

        public RadioChunk()
        {
            // Служебные не изменяемые байты
            array[0] = 0b11010100; // [PKT_TYPE_CTRL|PKT_SUBTYPE_CTRL_ACK]
            array[1] = 0x70; // Идентификатор ZVO пакета (для идентификации ZVO пакетов)
            array[2] = 0x00; // Duraton/ID (использовать нельзя, меняется при пересылке)
            array[3] = 0x00; // Duraton/ID (использовать нельзя, меняется при пересылке)
            array[4] = 0; array[5] = 0; // Номер пакета (UInt16)
            array[6] = 0; array[7] = 0; // Длина полезные данных (UInt16) (чистых байт, без XOR)
            array[8] = 0; array[9] = 0; // CRC16 заголовка [0..8]
            // Тело полезной нагрузки 
        }

        public RadioChunk(byte[] data)
        {
            if (data.Length < SeekStart)
            {
                Check = ChunkState.ErrorSize;
                return;
            }

            var lenData = BitConverter.ToUInt16(data, 5);
            if (data.Length != SeekStart + (SizeRoundedData(lenData) / SizeBlockXor) * BigSizeBlockXor + DataCrc32SizeXored)
            {
                if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)} LEN_ERROR, len={data.Length}!={SeekStart + lenData * 2}");
                Check = ChunkState.ErrorSize;
                return;
            }
            else
            {
                Array.Copy(data, array, data.Length);
                data[2] = 0x00; // обязательная перезапись, т.к. меняется при пересылке
                data[3] = 0x00; // обязательная перезапись, т.к. меняется при пересылке
            }
            if (PrintLog) Console.Write(Convert.ToHexString(data));
            if (data[1] != 0x70)
            {
                if (PrintLog) Console.WriteLine(" ZVO_ERROR");
                Check = ChunkState.ErrorZvo;
                return;
            }
            if (!CRC16(data[..SizeHeader]).SequenceEqual(data[SizeHeader..SeekStart]))
            {
                if (PrintLog) Console.WriteLine(" CRC_ERROR");
                Check = ChunkState.ErrorHeaderCrc;
                return;
            }
            if (PrintLog) Console.WriteLine("");
        }

        public bool DataCrc32Check() // +
        {
            var crc32 = CRC32(array[SeekStart..(SeekStart + DataSizeXored)]);
            for (var i = 0; i < crc32.Length; i++)
            {
                if (array[SeekStart + DataSizeXored + i * 2 + 0] != crc32[i]) return false;
            }
            return true;
        }

        public void WriteNormalData(byte[] data) // +
        {
            // Создаем массив кратный данным для кратности блокам XOR
            var dataRounde = new byte[SizeRoundedData(data.Length)];
            Array.Copy(data, 0, dataRounde, 0, data.Length);

            // Заполняем array блоками данных SizeBlockXor, с шагами по SizeBlockXor
            var n = 0;
            for (var i = 0; i < dataRounde.Length; i += SizeBlockXor)
            {
                Array.Copy(dataRounde, i, array, SeekStart + n * BigSizeBlockXor, SizeBlockXor);
                array[SeekStart + n * BigSizeBlockXor + SizeBlockXor] = CRC8(dataRounde[i..(i + SizeBlockXor)]);
                n++;
            }
        }
        public byte[] GetNormalData()
        {
            var ret = new byte[SizeRoundedData(DataSizeOriginal)];

            var n = 0;
            for (var i = 0; i < ret.Length; i += SizeBlockXor)
            {
                Array.Copy(array, SeekStart + n * BigSizeBlockXor, ret, i, SizeBlockXor);
                n++;
            }
            return ret[..DataSizeOriginal];
        }

        public void WriteNewXorData(byte[] data)
        {
            var len = data.Length / SizeBlockXor + (data.Length % SizeBlockXor > 0 ? SizeBlockXor : 0);
            for (var i = 0; i < len; i += BigSizeBlockXor)
            {
                if (CRC8(data[i..(i + SizeBlockXor)]) == data[i + SizeBlockXor]) // это валидный блок данных
                {
                    Array.Copy(data, i, array, SeekStart + i, BigSizeBlockXor);
                }
            }
        }

        public byte[] GetXorData()
        {
            return array[SeekStart..(SeekStart + DataSizeOriginal * BigSizeBlockXor)];
        }

        public bool CheckXorData()
        {
            for (var i = 0; i < DataSizeXored; i += BigSizeBlockXor)
            {
                if (CRC8(array[(SeekStart + i)..(SeekStart + i + SizeBlockXor)]) != (byte)(array[SeekStart + i + SizeBlockXor])) return false;
            }
            return true;
        }

        public void CalcAndWriteHeaderCrc16()
        {
            Array.Copy(CRC16(array[..SizeHeader]), 0, array, SizeHeader, SizeHeaderCrc16);
        }

        public void CalcAndWriteDataCrc32()
        {
            var crc32 = CRC32(array[SeekStart..(SeekStart + DataSizeXored)]);
            for (var i = 0; i < crc32.Length; i++)
            {
                array[SeekStart + DataSizeXored + i * 2 + 0] = crc32[i];
                array[SeekStart + DataSizeXored + i * 2 + 1] = CRC8(crc32[i]);
            }
        }
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

    public static byte CRC8(byte data)
    {
        return CRC8([data]);
    }

    public static byte CRC8(byte[] data)
    {
        byte crc = 0x00;
        for (int i = 0; i < data.Length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x80) != 0)
                    crc = (byte)((crc << 1) ^ 0x07); // Polynomial
                else
                    crc <<= 1;
            }
        }
        return crc;
    }

    public static byte[] CRC16(byte[] data)
    {
        ushort crc = 0xFFFF; // Начальное значение

        for (int i = 0; i < data.Length; i++)
        {
            crc ^= (ushort)data[i];
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                    crc = (ushort)((crc >> 1) ^ 0xA001); // Полином
                else
                    crc >>= 1;
            }
        }
        return BitConverter.GetBytes(crc);
    }

    public static byte[] CRC32(byte[] data)
    {
        return System.IO.Hashing.Crc32.Hash(data);
    }
}
