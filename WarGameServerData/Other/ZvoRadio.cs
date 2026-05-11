using System.Collections.Concurrent;
using System.Net.Sockets;

namespace WarGameServerData.Other;

public class ZvoRadio
{
#pragma warning disable CA2211 // Поля, не являющиеся константами, не должны быть видимыми
    public static byte SizeData = 16;
#pragma warning restore CA2211 // Поля, не являющиеся константами, не должны быть видимыми

    public static readonly byte SizeHeader = 9;
    public static readonly byte SizeCrc = 4;

    public Func<byte[], Task> OnGetPacketAsync { get; set; } = async delegate { }; // делегат при получении нового пакета

    private readonly List<byte[]> RadioHeaders = [];
    private byte PacketNumber = 0; // циклический номер пакета
    private readonly string ApIp; // адрес точки пересылки
    private readonly ushort ApPort; // порт точки пересылки

    private readonly ConcurrentQueue<RadioChunk> ChunksSend = new(); // Чанки для оправки
    private readonly List<RecvPacket> PacketsRecv = []; // Собраные пакеты
    private readonly CancellationToken _ct = new();

    private float CounterZvoNoise;
    private int CounterZvoNoiseBytesInternal;
    public float GetCounterZvoNoise => CounterZvoNoise;
    private float CounterZvoCrcErrors;
    private int CounterZvoCrcErrorsInternal;
    public float GetCounterZvoCrcErrors => CounterZvoCrcErrors;

    public ZvoRadio(string apIp, ushort apPort, byte sizeData)
    {
        ApIp = apIp;
        ApPort = apPort;
        SizeData = sizeData;
    }

    public void AddRadioHead(byte[] radiohead)
    {
        lock (RadioHeaders)
        {
            RadioHeaders.Add(radiohead);
        }
    }

    public void Send(byte[] data) // Отправить пакет в радиоэфир
    {
        var seek = 0;
        ushort cut = 0;
        var cuts = (ushort)(data.Length / SizeData + ((data.Length % SizeData > 0) ? 1 : 0));
        do
        {
            var len = Math.Min(SizeData, data.Length - seek);
            var chunk = new RadioChunk
            {
                PacketNumber = PacketNumber, // Номер пакета
                CutNumber = cut, // Номер куска
                CutsAll = cuts, // Всего кусков
                Data = data[seek..(seek + len)] // Полезные анные
            };
            chunk.CalcAndWriteCrc32(); // Заполняем CRC32

            ChunksSend.Enqueue(chunk);

            cut++;
            seek += len;
        }
        while (seek < data.Length);
        PacketNumber++;
    }

    public async void StartAsync()
    {
        ThreadSendAsync();
        ThreadRecvAsync();
        ThreadRecvActionAsync();

        while (!_ct.IsCancellationRequested)
        {
            await Task.Delay(1000, _ct);
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff} ZvoRadio CHUNK={SizeData:0}: ForSend={ChunksSend.Count:0}, PacketsRecv={PacketsRecv.Count:0}");

            CounterZvoNoise = (CounterZvoNoiseBytesInternal * 8) / 1_000_000.0f;
            CounterZvoCrcErrors = CounterZvoCrcErrorsInternal;
            CounterZvoNoiseBytesInternal = 0;
            CounterZvoCrcErrorsInternal = 0;
        }
    }

    public async void StopAsync()
    {
        _ct.ThrowIfCancellationRequested();
        await Task.Delay(100);
    }

    public async void ThreadRecvAsync()
    {
        var noise = 0;
        var connect = new UdpClient(ApPort);
        while (!_ct.IsCancellationRequested)
        {
            if (noise > 0) // Прошлый пакет был шумом
            {
                CounterZvoNoiseBytesInternal += noise;
            }
            // Получение данных
            var result = await connect.ReceiveAsync(_ct);
            var sender = result.RemoteEndPoint;
            var data = result.Buffer;
            noise = data.Length;

            if (!sender.Address.ToString().Equals(ApIp)) continue; // Пакет не от точки связи
            if (data.Length <= 3) continue; // Огрызок пакета
            var dataChunk = data[data[2]..^4]; // чанк ZVO
            var chunk = new RadioChunk(dataChunk);
            if (chunk.Check != ChunkState.OK)
            {
                if (chunk.Check == ChunkState.ErrorCrc)
                {
                    CounterZvoCrcErrorsInternal++;
                    noise = 0;
                    continue;
                }
                if (chunk.Check == ChunkState.ErrorZvo)
                {
                    continue;
                }
                continue;
            }

            lock (PacketsRecv)
            {
                var packet = PacketsRecv.Find(x => x.Number == chunk.PacketNumber); // Ищем пакеты с таким номером
                if (packet == null)
                {
                    PacketsRecv.Add(new RecvPacket(chunk));
                }
                else
                {
                    packet.AddChunk(chunk);
                }
            }

            noise = 0;
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
                removed = PacketsRecv.RemoveAll(x => (DateTime.Now - x.LastUpdate).TotalMilliseconds > 300);
            }

            if (removed == 0) await Task.Delay(10, _ct);
        }
    }

    public class RecvPacket(RadioChunk chunk)
    {
        public DateTime LastUpdate = DateTime.Now;
        public bool OK => CutsAll != 0 && CutsAll == Cuts.Count;
        public byte Number { get; set; } = chunk.PacketNumber;

        private ushort CutsAll { get; } = chunk.CutsAll;
        private List<Cut> Cuts { get; } = [new Cut(chunk.CutNumber, chunk.Data)];

        public void AddChunk(RadioChunk chunk)
        {
            LastUpdate = DateTime.Now;
            lock (Cuts)
            {
                if (Cuts.Any(x => x.Number == chunk.CutNumber)) return;
                Cuts.Add(new Cut(chunk.CutNumber, chunk.Data));
            }
        }

        public class Cut
        {
            public byte[] Data;
            public ushort Number;

            public Cut(ushort number, byte[] data)
            {
                Number = number;
                Data = new byte[data.Length];
                Array.Copy(data, 0, Data, 0, data.Length);
            }
        }

        public byte[] GetPacket()
        {
            var ret = new byte[CutsAll * SizeData];
            lock (Cuts)
            {
                foreach (var c in Cuts)
                {
                    Array.Copy(c.Data, 0, ret, c.Number * SizeData, c.Data.Length);
                }
            }
            LastUpdate = DateTime.MinValue;
            return ret;
        }
    }

    public enum ChunkState
    {
        OK = 0,
        ErrorZvo = 1,
        ErrorCrc = 2,
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
            for (var i = 0; i < RadioHeaders.Count; i++)
            {
                using var ms = new MemoryStream();
                lock (RadioHeaders)
                {
                    ms.Write(RadioHeaders[i]);
                }
                ms.Write(send.GetArray);
                await udp.SendAsync(ms.ToArray(), ApIp, ApPort, _ct);
            }
        }
    }

    public class RadioChunk // Минимальный пакет радио (для отправки)
    {
        public ChunkState Check { get; } = ChunkState.OK;
        public byte PacketNumber { get { return array[4]; } set { array[4] = value; } }
        public ushort CutNumber { get { return BitConverter.ToUInt16(array, 5); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 5, 2); } }
        public ushort CutsAll { get { return BitConverter.ToUInt16(array, 7); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 7, 2); } }
        public byte[] Data { get { return array[SizeHeader..^SizeCrc]; } set { Array.Copy(value, 0, array, SizeHeader, value.Length); } }
        public byte[] GetArray => array;

        private readonly byte[] array = new byte[SizeHeader + SizeData + SizeCrc]; // Чанк данных (размером с WifiHeader

        public RadioChunk(byte[] data)
        {
            if (data.Length != SizeHeader + SizeData + SizeCrc)
            {
                Check = ChunkState.ErrorSize;
                return;
            }
            else
            {
                Array.Copy(data, array, data.Length);
            }
            if (data[1] != 0x70)
            {
                Check = ChunkState.ErrorZvo;
                return;
            }
            if (System.IO.Hashing.Crc32.Hash(data[4..(data.Length - SizeCrc)]).SequenceEqual(data[^4..]) == false) Check = ChunkState.ErrorCrc;
        }

        public RadioChunk()
        {
            // Служебные не изменяемые байты
            array[0] = 0b11010100; // [PKT_TYPE_CTRL|PKT_SUBTYPE_CTRL_ACK]
            array[1] = 0x70; // Идентификатор ZVO пакета (для идентификации ZVO пакетов)
            array[2] = 0x00; // Duraton/ID (использовать нельзя, меняется при пересылке)
            array[3] = 0x00; // Duraton/ID (использовать нельзя, меняется при пересылке)
            array[4] = 0; // Номер пакета UInt8
            array[5] = 0; array[6] = 0; // Номер куска (UInt16)
            array[7] = 0; array[8] = 0; // Всего кусков (UInt16)

            // Тело полезной нагрузки SizeData = [SizeHeader..(SizeHeader+SizeData)]

            // CRC32 пакета SizeCrc, считаются по телу [0..(SizeHeader+SizeData)]
            array[^4] = 0; array[^3] = 0; array[^2] = 0; array[^1] = 0;
        }

        public void CalcAndWriteCrc32()
        {
            // CRC считается исключительно с 4го байта, т.к. байты 2 и 3 меняются при пересылке
            Array.Copy(System.IO.Hashing.Crc32.Hash(array[4..(array.Length - SizeCrc)]), 0, array, array.Length - SizeCrc, SizeCrc);
        }
    }
}
