using H264Sharp;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using WarGameServerData.Model;
using WarGameServerData.Other;

namespace WarGameServerData.Data;

public class ZvoRadio(string apIp, ushort apPort)
{
    public static bool PrintLog => false;
    public static int TimeOutCreateMs => 300;
    public static int SeekStart => (SizeHeader + SizeCrc);

    public static readonly byte SizeHeader = 8;
    public static readonly byte SizeCrc = 4;

    private static readonly byte XorByte = 0b01010101;
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

    public Func<byte[], Task> OnGetPacketAsync { get; set; } = async delegate { }; // делегат при получении нового пакета

    private readonly List<byte[]> RadioHeadersMaxRange = [];
    private readonly List<byte[]> RadioHeadersVideoEL = [];
    private readonly List<byte[]> RadioHeadersVideoL = [];
    private readonly List<byte[]> RadioHeadersVideoM = [];
    private readonly List<byte[]> RadioHeadersVideoH = [];

    private byte PacketNumber = 0; // циклический номер пакета

    private readonly ConcurrentQueue<RadioChunk> ChunksSend = new(); // Чанки для оправки
    private readonly List<RecvPacket> PacketsRecv = []; // Собраные пакеты
    private readonly CancellationToken _ct = new();

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
            DataSize = (ushort)(data.Length + SizeCrc), /// Полезные данные
            TransferMode = mode, // тип отправки
        };
        chunk.CalcAndWriteHeaderCrc32(); // Заполняем CRC32
        chunk.WriteNormalData(data); // Пишем нормальные данные
        chunk.CalcAndWriteDataCrc32(); // Заполняем CRC32 данных
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
            if (data.Length < SizeHeader + SizeCrc + 4) continue; // Огрызок пакета
            var dataChunk = data[..^4]; // чанк ZVO
            var chunk = new RadioChunk(dataChunk);
            if (chunk.Check != ChunkState.OK) continue;

            lock (PacketsRecv)
            {
                var packet = PacketsRecv.Find(x => x.Number == chunk.PacketNumber); // Ищем пакеты с таким номером
                if (packet == null)
                {
                    PacketsRecv.Add(new RecvPacket(chunk));
                }
                else
                {
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
            if (data.Length < SizeHeader + SizeCrc + 4) continue; // Огрызок пакета

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
            ApLanSendQueue = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(data, seek)); seek += 8;
        }
    }

    public async void ThreadRecvActionAsync()
    {
        while (!_ct.IsCancellationRequested)
        {
            var removed = 0;
            RecvPacket? packet;

            lock (PacketsRecv)
            {
                packet = PacketsRecv.Find(x => x.OK);
            }

            if (packet != null)
            {
                await OnGetPacketAsync.Invoke(packet.GetPacket());
            }

            lock (PacketsRecv)
            {
                if (packet != null)
                {
                    PacketsRecv.Remove(packet);
                }
                removed = PacketsRecv.RemoveAll(x => (DateTime.Now - x.LastUpdate).TotalMilliseconds >= TimeOutCreateMs);
            }

            if (removed == 0) await Task.Delay(10, _ct);
        }
    }

    public class RecvPacket(RadioChunk chunk)
    {
        public DateTime LastUpdate = DateTime.Now;
        public bool OK => Chunk.DataIsValid;
        public byte Number { get; set; } = chunk.PacketNumber;
        private RadioChunk Chunk { get; } = chunk;

        public void AddRepeat(RadioChunk chunk)
        {
            LastUpdate = DateTime.Now;
            Chunk.WriteNewXorData(chunk.GetXorData());
        }

        public byte[] GetPacket()
        {
            return Chunk.GetNormalData()[..^SizeCrc];
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
        public ChunkState Check { get; } = ChunkState.OK;
        public TransferMode TransferMode { get; set; } = TransferMode.None;
        public byte PacketNumber { get { return array[4]; } set { array[4] = value; } }
        public ushort DataSize { get { return BitConverter.ToUInt16(array, 5); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 5, 2); } }
        public bool DataIsValid => CheckXorData() & DataCrcCheck();
        public byte[] Data => GetNormalData();
        public byte[] GetArray => array[..(SeekStart + DataSize * 2)];

        private readonly byte[] array = new byte[SizeHeader + SizeCrc + 10000]; // Чанк данных c XOR

        public bool DataCrcCheck()
        {
            var crc32 = System.IO.Hashing.Crc32.Hash(array[SeekStart..(SeekStart + DataSize * 2 - SizeCrc * 2)]);
            for (var i = 0; i < crc32.Length; i++)
            {
                if (array[SeekStart + DataSize * 2 - SizeCrc * 2 + i * 2 + 0] != crc32[i]) return false;
            }
            return true;
        }

        public void WriteNormalData(byte[] data)
        {
            if (data.Length > array.Length) return; // Размер данных для записи не совпадает
            for (var i = 0; i < data.Length; i++)
            {
                array[SeekStart + i * 2 + 0] = data[i];
                array[SeekStart + i * 2 + 1] = (byte)(data[i] ^ XorByte);
            }
        }
        public byte[] GetNormalData()
        {
            var ret = new byte[DataSize];
            for (var i = 0; i < DataSize; i++)
            {
                ret[i] = array[SeekStart + i * 2 + 0];
            }
            return ret;
        }

        public void WriteNewXorData(byte[] data)
        {
            for (var i = 0; i < data.Length / 2; i++)
            {
                if (data.Length > array.Length) break;

                if ((data[i * 2 + 0] == (byte)(data[i * 2 + 1] ^ XorByte))) // верный кусок данных
                {
                    array[SeekStart + i * 2 + 0] = data[i * 2 + 0]; // Обновляем данные
                    array[SeekStart + i * 2 + 1] = data[i * 2 + 1]; // Обновляем данные
                }
            }
        }

        public byte[] GetXorData()
        {
            return array[SeekStart..(SeekStart + DataSize * 2)];
        }

        public bool CheckXorData()
        {
            for (var i = 0; i < DataSize; i++)
            {
                if (array[SeekStart + i * 2 + 0] != (byte)(array[SeekStart + i * 2 + 1] ^ XorByte)) return false;
            }
            return true;
        }

        public RadioChunk(byte[] data)
        {
            if (data.Length < SeekStart)
            {
                Check = ChunkState.ErrorSize;
                return;
            }

            var lenData = BitConverter.ToUInt16(data, 5);
            if (data.Length != SeekStart + lenData * 2)
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
            if (!System.IO.Hashing.Crc32.Hash(data[..SizeHeader]).SequenceEqual(data[SizeHeader..SeekStart]))
            {
                if (PrintLog) Console.WriteLine(" CRC_ERROR");
                Check = ChunkState.ErrorHeaderCrc;
                return;
            }
            if (PrintLog) Console.WriteLine("");
        }

        public RadioChunk()
        {
            // Служебные не изменяемые байты
            array[0] = 0b11010100; // [PKT_TYPE_CTRL|PKT_SUBTYPE_CTRL_ACK]
            array[1] = 0x70; // Идентификатор ZVO пакета (для идентификации ZVO пакетов)
            array[2] = 0x00; // Duraton/ID (использовать нельзя, меняется при пересылке)
            array[3] = 0x00; // Duraton/ID (использовать нельзя, меняется при пересылке)
            array[4] = 0; array[5] = 0; // Номер пакета (UInt16)
            array[6] = 0; array[7] = 0; // Длина данных (UInt16) (всегда кратна 2м, т.к. XOR)
            array[8] = 0; array[9] = 0; array[10] = 0; array[11] = 0; // CRC32 заголовка [4..8]
            // Тело полезной нагрузки 
        }

        public void CalcAndWriteHeaderCrc32()
        {
            Array.Copy(System.IO.Hashing.Crc32.Hash(array[..SizeHeader]), 0, array, SizeHeader, SizeCrc);
        }
        public void CalcAndWriteDataCrc32()
        {
            var crc32 = System.IO.Hashing.Crc32.Hash(array[SeekStart..(SeekStart + DataSize * 2 - SizeCrc * 2)]);
            for (var i = 0; i < crc32.Length; i++)
            {
                array[SeekStart + DataSize * 2 - SizeCrc * 2 + i * 2 + 0] = crc32[i];
                array[SeekStart + DataSize * 2 - SizeCrc * 2 + i * 2 + 1] = (byte)(crc32[i] ^ XorByte);
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
        using var msIn = new MemoryStream(dataZip);
        using var msOut = new MemoryStream();
        using (var ds = new DeflateStream(msIn, CompressionMode.Decompress))
        {
            ds.CopyTo(msOut);
        }
        return msOut.ToArray();
    }
}