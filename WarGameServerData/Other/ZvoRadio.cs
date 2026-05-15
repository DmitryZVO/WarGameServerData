using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Sockets;

namespace WarGameServerData.Other;

public class ZvoRadio(string apIp, ushort apPort)
{
    public static bool PrintLog => false;

    public const byte SizeHeader = 8; // Размер заголовка (без CRC)
    public const byte SizeHeaderCrc16 = 2;  // Размер CRC16 блока заголовка
    public const byte SizeDataCrc32 = 4; // Размер CRC32 блока данных
    public const byte SizeBlocForkXor = 16; // Какими блоками кодируем XOR для восстановления
    public const int DataCrc32SizeXored = 8; // CRC32 данных кодируется каждый байт + CRC8
    public const bool PhySendPacketCrc32 = true; // Есть ли в конце пакета FCS_CRC
    public static byte BigSizeBlockXor => (SizeBlocForkXor + 1); // Общий размер блока XOR вместе с байтами CRC8
    public static int SeekStart => (SizeHeader + SizeHeaderCrc16); // Стартовое смещение от начала заголовка

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

    private readonly List<byte[]> RadioHeadersMaxRange = [];
    private readonly List<byte[]> RadioHeadersVideoEL = [];
    private readonly List<byte[]> RadioHeadersVideoL = [];
    private readonly List<byte[]> RadioHeadersVideoM = [];
    private readonly List<byte[]> RadioHeadersVideoH = [];

    private byte PacketNumber = 0; // циклический номер пакета

    private readonly ConcurrentQueue<RadioChunk> ChunksSend = new(); // Чанки для оправки
    private RadioChunk? LastValidChunk; // Последний валидный пакет
    private readonly CancellationToken _ct = new();

    public static int SizeRoundedData(int data)
    {
        return ((data / SizeBlocForkXor + (data % SizeBlocForkXor > 0 ? 1 : 0)) * SizeBlocForkXor);
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
            if (PrintLog) Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff} ZvoRadio: ForSend={ChunksSend.Count:0}, PacketsRecv={0}");
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
            var dataChunk = data[..(PhySendPacketCrc32 ? ^4 : ^0)]; // чанк ZVO
            if (data.Length > 360)
            {

            }
            var chunk = new RadioChunk(dataChunk);
            if (chunk.Check != ChunkState.OK) continue;

            LastValidChunk ??= chunk;

            if (chunk.PacketNumber == LastValidChunk.PacketNumber) // Это повтор
            {
                if (LastValidChunk.DataIsValid) continue; // Игнорируем повторы при прошлом валидном пакете 
                LastValidChunk.WriteNewXorData(chunk.GetXorData()); // Пробуем восстановить пакет
            }
            else // Пришел новый пакет, пора отправлять старый
            {
                if (LastValidChunk.DataIsValid) // Если пакет валиден - отправляем!
                {
                    await OnNewPacketAsync.Invoke(LastValidChunk.GetNormalData());
                }
                LastValidChunk = chunk;
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

    public async Task Send(byte[] data, TransferMode mode) // Отправить пакет в радиоэфир
    {
        var send = new RadioChunk
        {
            PacketNumber = PacketNumber, // Номер пакета
            DataSizeOriginal = (ushort)(data.Length), /// Полезные данные
            TransferMode = mode, // тип отправки
        };
        send.CalcAndWriteHeaderCrc16(); // Заполняем CRC16 заголовка
        send.WriteNormalData(data); // Пишем нормальные данные
        send.CalcAndWriteDataCrc32(); // Заполняем CRC32 данных

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
            /*
            ms.Write([
                0b00001000, 
                0x00, 
                0, 0, 
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // reciever
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // sender
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // filtered
                0x00, 0x00]);
            var d = ms.ToArray();
            */
            ms.Write(send.GetNormalData());
            var d = ms.ToArray();
            await udp.SendAsync(d, apIp, apPort, _ct);
        }

        //if (PrintLog) Console.WriteLine(Convert.ToHexString(send.GetArray));
        PacketNumber++;
    }

    public class RadioChunk // Минимальный пакет радио (для отправки)
    {
        public ChunkState Check { get; }
        public TransferMode TransferMode { get; set; }
        public byte PacketNumber { get { return array[4]; } set { array[4] = value; } }
        public ushort DataSizeOriginal { get { return BitConverter.ToUInt16(array, 5); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 5, 2); } }
        public bool DataIsValid => CheckXorData() & DataCrc32Check();
        public byte[] Data => GetNormalData();
        public byte[] GetArray => array[..(SeekStart + (SizeRoundedData(DataSizeOriginal) / SizeBlocForkXor) * BigSizeBlockXor + DataCrc32SizeXored)];
        public int DataSizeXored => (SizeRoundedData(DataSizeOriginal) / SizeBlocForkXor) * BigSizeBlockXor;
        public int DataAndCrc32SizeXored { get { return (DataSizeXored + DataCrc32SizeXored); } }

        private readonly byte[] array = new byte[SizeHeader + SizeHeaderCrc16 + 10000]; // Чанк данных c XOR

        public RadioChunk()
        {
            // Служебные не изменяемые байты
            array[0] = 0b11010100; // [PKT_TYPE_CTRL|PKT_SUBTYPE_CTRL_ACK]
            //array[0] = 0b11000100; // [PKT_TYPE_CTRL|PKT_SUBTYPE_CTRL_CTS]
            //array[0] = 0b10001000; // [PKT_TYPE_DATA | PKT_SUBTYPE_DATA_QoS_D] // НЕ РАБОТАЕТ
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
            if (data.Length != SeekStart + (SizeRoundedData(lenData) / SizeBlocForkXor) * BigSizeBlockXor + DataCrc32SizeXored)
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
            if (PrintLog) Console.WriteLine(" OK");
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
            for (var i = 0; i < dataRounde.Length; i += SizeBlocForkXor)
            {
                Array.Copy(dataRounde, i, array, SeekStart + n * BigSizeBlockXor, SizeBlocForkXor);
                array[SeekStart + n * BigSizeBlockXor + SizeBlocForkXor] = CRC8(dataRounde[i..(i + SizeBlocForkXor)]);
                n++;
            }
        }
        public byte[] GetNormalData()
        {
            var ret = new byte[SizeRoundedData(DataSizeOriginal)];

            var n = 0;
            for (var i = 0; i < ret.Length; i += SizeBlocForkXor)
            {
                Array.Copy(array, SeekStart + n * BigSizeBlockXor, ret, i, SizeBlocForkXor);
                n++;
            }
            return ret[..DataSizeOriginal];
        }

        public void WriteNewXorData(byte[] data)
        {
            var len = data.Length / SizeBlocForkXor + (data.Length % SizeBlocForkXor > 0 ? SizeBlocForkXor : 0);
            for (var i = 0; i < len; i += BigSizeBlockXor)
            {
                if (CRC8(data[i..(i + SizeBlocForkXor)]) == data[i + SizeBlocForkXor]) // это валидный блок данных
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
                if (CRC8(array[(SeekStart + i)..(SeekStart + i + SizeBlocForkXor)]) != (byte)(array[SeekStart + i + SizeBlocForkXor])) return false;
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
