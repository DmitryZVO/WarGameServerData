using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Sockets;

namespace WarGameServerData.Other;

public class ZvoRadio (string apIp, int apPort)
{
    public static bool PrintLog => true;

    public const byte SizeBlocForkXor = 64; // Какими блоками кодируем XOR для восстановления (лучшие результаты)

    public const byte SizeHeader = 8; // Размер заголовка (без CRC)
    public const byte SizeHeaderCrc16 = 2;  // Размер CRC16 блока заголовка
    public const byte SizeDataCrc32 = 4; // Размер CRC32 блока данных

    public const bool PhySendPacketCrc32 = true; // Есть ли в конце пакета FCS_CRC
    public static byte DataCrc32SizeXored => (SizeDataCrc32 + 4); // CRC32 блока данных + CRC32 самого CRC32
    public static byte BigSizeBlockXor => (SizeBlocForkXor + 4); // Общий размер блока XOR вместе с байтами CRC32
    public static int SeekStart => (SizeHeader + SizeHeaderCrc16); // Стартовое смещение от начала заголовка
    private int recvGood, recvBad, recvHeadBad, recvAll, recvRest, sendAll;

    private RadioChunk? LastValidChunk; // Последний успешно принятый чанк

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

    private ushort PacketNumber = 0; // циклический номер пакета
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
            if (PrintLog) Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff} ZvoRadio: PacketsSend: [{sendAll:0}], AP_SEND={ApRadioPacketsSend:0} | PacketsRecv all/badH: [{recvAll:0}/{recvHeadBad:0}], AP_RECV={ApRadioPacketsRecv:0} | good/badData: [{recvGood:0}/{recvBad:0}] ({(1.0f - recvBad / (float)recvAll) * 100.0:0.00}%), recvRest={recvRest:0} (+{recvRest / (float)recvAll * 100.0:0.00}%)");
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
        var connect = new UdpClient(apPort);
        while (!_ct.IsCancellationRequested)
        {
            var result = await connect.ReceiveAsync(_ct);
            var sender = result.RemoteEndPoint;
            var data = result.Buffer;
            recvAll++;

            if (!sender.Address.ToString().Equals(apIp)) continue; // Пакет не от точки связи
            if (data.Length < SeekStart + SizeDataCrc32)
            {
                recvHeadBad++;
                continue; // Огрызок пакета
            }
            var dataChunk = data[..(PhySendPacketCrc32 ? ^4 : ^0)]; // чанк ZVO
            var chunk = new RadioChunk(dataChunk);
            if (chunk.Check != ChunkState.OK)
            {
                recvHeadBad++;
                continue;
            }
            if (chunk.DataIsValid) recvGood++; else recvBad++;

            LastValidChunk ??= chunk;

            if (chunk.PacketNumber == LastValidChunk.PacketNumber) // Это повтор
            {
                if (LastValidChunk.DataIsValid) continue; // Игнорируем повторы при прошлом валидном пакете 
                LastValidChunk.RestorePacket(chunk); // Пробуем восстановить пакет
                //Console.Write($"packet {chunk.PacketNumber:0}, restore {rest:0} blocks");
                if (LastValidChunk.DataIsValid)
                {
                    //Console.WriteLine(", packet RESTORED!");
                    recvRest++; // Если восстановили - увечиливаем счетчик
                }
                else
                {
                    //Console.WriteLine("");
                }
            }
            else // Пришел новый пакет, пора отправлять старый
            {
                if (LastValidChunk.DataIsValid) // Если пакет валиден - отправляем!
                {
                    _ = OnNewPacketAsync.Invoke(LastValidChunk.GetNormalData());
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
                ms.Write(radio[0]); // Записываем заголовок Radiotap для инжектирования
            }
            ms.Write(send.GetArray); // Записываем оставшийся блок данных и CRC
            var d = ms.ToArray();
            await udp.SendAsync(d, apIp, apPort, _ct);
            sendAll++;
        }

        //}
        //if (PrintLog) Console.WriteLine(Convert.ToHexString(send.GetArray));
        PacketNumber++;
    }

    public class RadioChunk // Минимальный пакет радио (для отправки)
    {
        public ChunkState Check { get; }
        public TransferMode TransferMode { get; set; }
        public ushort PacketNumber { get { return BitConverter.ToUInt16(array, 4); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 4, 2); } }
        public ushort DataSizeOriginal { get { return BitConverter.ToUInt16(array, 6); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 6, 2); } }
        public bool DataIsValid { get { if (!CheckCrc32Block()) return false; if (!CheckXorData()) return false; if (!DataCrc32Check()) return false; return true; } }
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
            //array[0] = 0b10001000; // [PKT_TYPE_DATA | PKT_SUBTYPE_DATA_QoS_D]
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

            var lenData = BitConverter.ToUInt16(data, 6);
            if (data.Length != SeekStart + (SizeRoundedData(lenData) / SizeBlocForkXor) * BigSizeBlockXor + DataCrc32SizeXored)
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)} LEN_ERROR, len={data.Length}!={SeekStart + lenData * 2}");
                Check = ChunkState.ErrorSize;
                return;
            }
            else
            {
                Array.Copy(data, array, data.Length);
                array[2] = 0x00; // обязательная перезапись, т.к. меняется при пересылке
                array[3] = 0x00; // обязательная перезапись, т.к. меняется при пересылке
            }

            if (array[1] != 0x70)
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)}, ZVO_ERROR");
                Check = ChunkState.ErrorZvo;
                return;
            }
            if (!CRC16(array[..SizeHeader]).SequenceEqual(array[SizeHeader..SeekStart]))
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)}, CRC_ERROR");
                Check = ChunkState.ErrorHeaderCrc;
                return;
            }
            //if (PrintLog) Console.WriteLine(" OK");
        }

        public bool DataCrc32Check() // +
        {
            var crc32 = CRC32(array[SeekStart..(SeekStart + DataSizeXored)]);
            for (var i = 0; i < crc32.Length; i++)
            {
                if (array[SeekStart + DataSizeXored + i] != crc32[i]) return false;
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
                Array.Copy(CRC32(dataRounde[i..(i + SizeBlocForkXor)]), 0, array, SeekStart + n * BigSizeBlockXor + SizeBlocForkXor, 4);
                //array[SeekStart + n * BigSizeBlockXor + SizeBlocForkXor] = CRC8(dataRounde[i..(i + SizeBlocForkXor)]);
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

        public int RestorePacket(RadioChunk chunk)
        {
            var blocks = 0;
            var nc = chunk.CheckCrc32Block();
            var c = CheckCrc32Block();
            var nxd = chunk.GetXorData();
            var xd = GetXorData();
            if (nxd.Length != xd.Length) return 0;

            // Восстанавливаем CRC32
            if (nc && !c)
            {
                Array.Copy(chunk.array, SeekStart + DataSizeXored, array, SeekStart + DataSizeXored, DataCrc32SizeXored);
                blocks++;
            }

            int n = 0;
            do
            {
                var nbd = nxd[n..(n + BigSizeBlockXor)];
                var bd = xd[n..(n + BigSizeBlockXor)];
                var nbdOk = CRC32(nbd[..^4]).SequenceEqual(nbd[^4..]);
                var bdOk = CRC32(bd[..^4]).SequenceEqual(bd[^4..]);
                //var nbdOk = CRC8(nbd[..^1]) == nbd[^1];
                //var bdOk = CRC8(bd[..^1]) == bd[^1];

                // Восстанавливаем блок данных
                if (nbdOk && !bdOk)
                {
                    if (nc && !c) Array.Copy(nbd, 0, array, SeekStart + n, nbd.Length);
                    blocks++;
                }
                n += BigSizeBlockXor;
            }
            while (nxd.Length > n && xd.Length > n);

            return blocks;
        }

        public byte[] GetXorData()
        {
            return array[SeekStart..(SeekStart + DataSizeXored)];
        }

        public bool CheckXorData()
        {
            for (var i = 0; i < DataSizeXored; i += BigSizeBlockXor)
            {
                if (!CRC32(array[(SeekStart + i)..(SeekStart + i + SizeBlocForkXor)]).SequenceEqual(array[(SeekStart + i + SizeBlocForkXor)..(SeekStart + i + SizeBlocForkXor + 4)])) return false;
                //if (CRC8(array[(SeekStart + i)..(SeekStart + i + SizeBlocForkXor)]) != (byte)(array[SeekStart + i + SizeBlocForkXor])) return false;
            }
            return true;
        }

        public bool CheckCrc32Block()
        {
            if (!CRC32(array[(SeekStart + DataSizeXored)..(SeekStart + DataSizeXored + SizeDataCrc32)]).SequenceEqual(array[(SeekStart + DataSizeXored + SizeDataCrc32)..(SeekStart + DataSizeXored + SizeDataCrc32 + 4)])) return false;
            //if (CRC8(array[(SeekStart + DataSizeXored)..(SeekStart + DataSizeXored + SizeDataCrc32)]) != array[SeekStart + DataSizeXored + SizeDataCrc32]) return false;
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
                array[SeekStart + DataSizeXored + i] = crc32[i];
            }
            Array.Copy(CRC32(crc32), 0, array, SeekStart + DataSizeXored + 4, 4);
            //array[SeekStart + DataSizeXored + crc32.Length] = CRC8(crc32);
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
                    //crc = (byte)((crc << 1) ^ 0x07); // Polynomial normal
                    crc = (byte)((crc << 1) ^ 0xd5); // Polynomial NEW
                else
                    crc <<= 1;
            }
        }
        return crc;
    }

    public static byte[] CRC16(byte[] data)
    {
        ushort[] _table = new ushort[256];
        //var polynomial = (ushort)0xA001; // стандарт
        var polynomial = (ushort)0x8408; // CcitKermit
        for (ushort i = 0; i < _table.Length; ++i)
        {
            ushort value = 0;
            var temp = i;
            for (byte j = 0; j < 8; ++j)
            {
                if (((value ^ temp) & 0x0001) != 0)
                    value = (ushort)((value >> 1) ^ polynomial);
                else
                    value >>= 1;
                temp >>= 1;
            }

            _table[i] = value;
        }
        return BitConverter.GetBytes(ComputeChecksum(data));

        ushort ComputeChecksum(params byte[] bytes)
        {
            return bytes.Aggregate<byte, ushort>(0, (current, t) => (ushort)((current >> 8) ^ _table[(byte)(current ^ t)]));
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
}
