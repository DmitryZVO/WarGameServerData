using System.Buffers.Binary;
using System.Net.Sockets;

namespace WarGameServerData.Other;

public class ZvoRadio(string apIp, int apPort)
{
    public static bool PrintLog => true;

    public const int SizeCrc32 = 4; // Размер CRC32 блока данных
    public const int SizeHeaderStatic = 4; // Размер заголовка (не изменяемая часть)
    public const int SizeHeaderDynamic = 4; // Размер заголовка (изменяемая часть)
    public const int SizeMacroBlock = 200; // Размер макроблока БЕЗ CRC32
    public const int MacroBlocksCount = 3; // Кол-во макро блоков A[0] + B[1] + XOR(A^B)[2] = 3

    public const int SizeHeaderDynamicAndCrc32 = SizeHeaderDynamic + SizeCrc32;  // Размер динамической части заголовка + CRC32
    public const int SizeMacroBlockAndCrc32 = SizeMacroBlock + SizeCrc32;  // Размер динамической части заголовка + CRC32
    public const int SizeHeaderAndMacroBlockAndCrc32 = SizeHeaderDynamicAndCrc32 + SizeMacroBlockAndCrc32;  // Размер заголовка + CRC32 + макроблок + CRC32
    public const int SizeFull = SizeHeaderStatic + SizeHeaderAndMacroBlockAndCrc32 * MacroBlocksCount;  // ПОЛНЫЙ РАЗМЕР ПАКЕТА
    public const bool PhySendPacketCrc32 = true; // Есть ли в конце пакета FCS_CRC
    public List<RadioChunk> PacketsVideoRecv = [];

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
    public RadioRepeater Repeater = new();

    private int recvGood, recvBad, recvHeadBad, recvAll, recvRest, sendAll;
    //private RadioChunk? LastValidChunk; // Последний успешно принятый чанк

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

            //if (!sender.Address.ToString().Equals(apIp)) continue; // Пакет не от точки связи
            var dataChunk = data[..(PhySendPacketCrc32 ? ^4 : ^0)]; // чанк ZVO

            if (data[0] == 0b11010100 && data[1] == 0x71) // это пакет от ретранслятора
            {
                Repeater.UpdateValues(dataChunk); /// Обновляем данные
                continue;
            }

            if (data.Length < SizeFull)
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

            //Console.WriteLine($"recv, packNumer={chunk.PacketNumber:0}, good={(chunk.PacketIsValid ? "YES":"NO")}");

            //LastValidChunk ??= chunk;

            // Пакеты с видео требуют последовательной обработки
            if (packType == 0x18)
            {
                if (PacketsVideoRecv.Any(x => x.PacketNumber == chunk.PacketNumber)) continue; // Такой пакет уже был
                PacketsVideoRecv.Add(chunk);
                Console.WriteLine($"recv video packet {chunk.PacketNumber:0}"); // это пакеты с видео
            }
            else // для не пакетов с видео обрабатывается ВСЕ (т.к. это телеметрия) и повторы и дубли
            {
                _ = OnNewPacketAsync.Invoke(chunk.GetNormalData());
            }

            /*
            // Остальные пакеты шлем как придут вместе с повторами
            if (chunk.PacketNumber == LastValidChunk.PacketNumber) // Это повтор
            {
                if (LastValidChunk.PacketIsValid) continue; // Игнорируем повторы при прошлом валидном пакете 
                if (chunk.PacketIsValid)
                {
                    LastValidChunk = chunk;
                    recvRest++; // Если восстановили - увечиливаем счетчик
                }
                else
                {
                    //Console.WriteLine("");
                }
            }
            else // Пришел новый пакет, пора отправлять старый
            {
                if (LastValidChunk.PacketIsValid) // Если пакет валиден - отправляем!
                {
                    if (LastValidChunk.PacketNumber >= chunk.PacketNumber) Console.WriteLine($"recv non video packet {LastValidChunk.PacketNumber:0}"); // это пакеты не с видео
                    _ = OnNewPacketAsync.Invoke(LastValidChunk.GetNormalData());
                    if (PacetRecvs.Count > 10) PacetRecvs.Remove(PacetRecvs.First()); // Удаляем отосланный пакет из списка
                }
                LastValidChunk = chunk;
            }
            */
        }
    }

    public async void ThreadRecvApAsync()
    {
        var connect = new UdpClient(apPort + 1);
        while (!_ct.IsCancellationRequested)
        {
            var result = await connect.ReceiveAsync(_ct);
            //var sender = result.RemoteEndPoint;
            var data = result.Buffer;

            //if (!sender.Address.ToString().Equals(apIp)) continue; // Пакет не от точки связи
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
            Console.WriteLine($"data ZVO size packet is BIG! len={data.Length:0}, max={SizeMacroBlock * 2:0}");
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

        //}
        //if (PrintLog) Console.WriteLine(Convert.ToHexString(send.GetArray));
        PacketNumber++;
    }

    public class RadioChunk // Минимальный пакет радио (для отправки)
    {
        public ChunkState Check { get; }
        public TransferMode TransferMode { get; set; }

        private readonly byte[] array = new byte[10000]; // Чанк данных c XOR

        public ushort PacketNumber { get { return BitConverter.ToUInt16(array, 4); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 4, 2); } }
        public ushort DataSizeOriginal { get { return BitConverter.ToUInt16(array, 6); } set { Array.Copy(BitConverter.GetBytes(value), 0, array, 6, 2); } }
        public bool PacketIsValid { get { if (!DynamicHeaderIsValid(0)) return false; if (!MacroBlockIsValid(0)) return false; if (!MacroBlockIsValid(1)) return false; return true; } }
        public byte[] Data => GetNormalData();
        public byte[] GetArray => array[..SizeFull];
        public bool SetNormalData(byte[] data) // +
        {
            if (data.Length > SizeMacroBlock * 2) return false; // Размер данных слишком большой для пакета

            Array.Copy(CRC32(array[SizeHeaderStatic..(SizeHeaderStatic + SizeHeaderDynamic)]), 0, array, SizeHeaderStatic + SizeHeaderDynamic, SizeCrc32); // записываем CRC32 в заголовок 0
            var header = array[SizeHeaderStatic..(SizeHeaderStatic + SizeHeaderDynamicAndCrc32)];
            Array.Copy(header, 0, array, SizeHeaderStatic + SizeHeaderAndMacroBlockAndCrc32, header.Length); // Копируем заголовок во второй блок данных
            Array.Copy(header, 0, array, SizeHeaderStatic + SizeHeaderAndMacroBlockAndCrc32 * 2, header.Length); // Копируем заголовок во блок данных XOR

            // Заполняем блок данных 0
            var lenBlockData0 = Math.Min(data.Length, SizeMacroBlock);
            if (lenBlockData0 > 0) Array.Copy(data, 0, array, SizeHeaderStatic + SizeHeaderDynamicAndCrc32, lenBlockData0);
            var block0crc32 = CRC32(array[(SizeHeaderStatic + SizeHeaderDynamicAndCrc32)..(SizeHeaderStatic + SizeHeaderDynamicAndCrc32 + SizeMacroBlock)]);
            Array.Copy(block0crc32, 0, array, SizeHeaderStatic + SizeHeaderAndMacroBlockAndCrc32 - SizeCrc32, block0crc32.Length);

            // Заполняем блок данных 1
            var lenBlockData1 = Math.Min(data.Length - SizeMacroBlock, SizeMacroBlock);
            if (lenBlockData1 > 0) Array.Copy(data, SizeMacroBlock, array, SizeHeaderStatic + SizeHeaderDynamicAndCrc32 + SizeHeaderAndMacroBlockAndCrc32, lenBlockData1);
            var block1crc32 = CRC32(array[(SizeHeaderStatic + SizeHeaderDynamicAndCrc32 + SizeHeaderAndMacroBlockAndCrc32)..(SizeHeaderStatic + SizeHeaderDynamicAndCrc32 + SizeHeaderAndMacroBlockAndCrc32 + SizeMacroBlock)]);
            Array.Copy(block1crc32, 0, array, SizeHeaderStatic + (SizeHeaderAndMacroBlockAndCrc32 * 2) - SizeCrc32, block1crc32.Length);

            // Заполняем блок XOR
            var data0 = array[(SizeHeaderStatic + SizeHeaderDynamicAndCrc32)..(SizeHeaderStatic + SizeHeaderAndMacroBlockAndCrc32 - SizeCrc32)];
            var data1 = array[(SizeHeaderStatic + SizeHeaderDynamicAndCrc32 + SizeHeaderAndMacroBlockAndCrc32)..(SizeHeaderStatic + SizeHeaderAndMacroBlockAndCrc32 * 2 - SizeCrc32)];
            for (var i = 0; i < SizeMacroBlock; i++)
            {
                array[SizeHeaderStatic + SizeHeaderDynamicAndCrc32 + SizeHeaderAndMacroBlockAndCrc32 * 2 + i] = (byte)(data0[i] ^ data1[i]);
            }
            var xorData = CRC32(array[(SizeHeaderStatic + SizeHeaderDynamicAndCrc32 + SizeHeaderAndMacroBlockAndCrc32 * 2)..(SizeHeaderStatic + SizeHeaderDynamicAndCrc32 + SizeHeaderAndMacroBlockAndCrc32 * 2 + SizeMacroBlock)]);
            Array.Copy(xorData, 0, array, SizeHeaderStatic + SizeHeaderAndMacroBlockAndCrc32 * 3 - SizeCrc32, xorData.Length);
            return true;
        }

        public byte[] GetNormalData()
        {
            var data = new byte[SizeMacroBlock * 2];
            Array.Copy(array, SizeHeaderStatic + SizeHeaderDynamicAndCrc32, data, 0, SizeMacroBlock);
            Array.Copy(array, SizeHeaderStatic + SizeHeaderAndMacroBlockAndCrc32 + SizeHeaderDynamicAndCrc32, data, SizeMacroBlock, SizeMacroBlock);
            return data[..DataSizeOriginal];
        }
        public void CheckAndRestorePacket() // Проверка и восстановление пакета (по необходимости)
        {
            RestoreDynamicHeader();
            RestoreMacroBlock0();
            RestoreMacroBlock1();
        }
        public bool DynamicHeaderIsValid(int block)
        {
            var arr = GetDynamicHeaderAndCrc32(block);
            return CRC32(arr[..^SizeCrc32]).SequenceEqual(arr[SizeHeaderDynamic..]);
        }
        public bool MacroBlockIsValid(int block)
        {
            var arr = GetMacroBlockAndCrc32(block);
            return CRC32(arr[..^SizeCrc32]).SequenceEqual(arr[SizeMacroBlock..]);
        }
        private byte[] GetDynamicHeaderAndCrc32(int block)
        {
            var start = SizeHeaderStatic + block * (SizeHeaderAndMacroBlockAndCrc32);
            var end = start + SizeHeaderDynamicAndCrc32;
            return array[start..end];
        }

        private byte[] GetMacroBlockAndCrc32(int block)
        {
            var start = SizeHeaderStatic + block * (SizeHeaderDynamicAndCrc32 + SizeMacroBlockAndCrc32) + SizeHeaderDynamicAndCrc32;
            var end = start + SizeMacroBlockAndCrc32;
            return array[start..end];
        }
        private void RestoreDynamicHeader()
        {
            if (DynamicHeaderIsValid(0)) return; // нулевой блок динамического заголовка валиден, незачем восстанавливать
            for (var i = 1; i < MacroBlocksCount; i++)
            {
                if (DynamicHeaderIsValid(i)) // первый повтор динамического блока заголовка валиден, восстанавливаем из него
                {
                    Array.Copy(GetDynamicHeaderAndCrc32(i), 0, array, SizeHeaderStatic, SizeHeaderDynamicAndCrc32);
                    break;
                }
            }
            //if (DynamicHeaderIsValid(0)) Console.WriteLine($"packet {PacketNumber:0} Restore HEADER");// else Console.WriteLine($"packet {PacketNumber:0} NO RESTORE HEADER");
        }
        private void RestoreMacroBlock0()
        {
            if (MacroBlockIsValid(0)) return; // первый блок данных валиден, незачем восстанавливать
            if (!MacroBlockIsValid(2)) return; // не возможно восстановить макроблок, т.к. XOR часть повреждена
            if (!MacroBlockIsValid(1)) return; // не возможно восстановить макроблок, т.к. второй блок поврежден
            var xor = GetMacroBlockAndCrc32(2); // данные XOR
            var block1 = GetMacroBlockAndCrc32(1); // данные второго блока
            var block0 = new byte[xor.Length]; // первый блок (восстановленный)
            for (var i = 0; i < xor.Length - SizeCrc32; i++)
            {
                block0[i] = (byte)(xor[i] ^ block1[i]);
            }
            Array.Copy(CRC32(block0[..^SizeCrc32]), 0, block0, SizeMacroBlock, SizeCrc32);
            Array.Copy(block0, 0, array, SizeHeaderStatic + SizeHeaderDynamicAndCrc32, block0.Length);
            //if (MacroBlockIsValid(0)) Console.WriteLine($"packet {PacketNumber:0} Restore BLOCK0");// else Console.WriteLine($"packet {PacketNumber:0} NO RESTORE BLOCK0");
        }
        private void RestoreMacroBlock1()
        {
            if (MacroBlockIsValid(1)) return; // второй блок данных валиден, незачем восстанавливать
            if (!MacroBlockIsValid(2)) return; // не возможно восстановить макроблок, т.к. XOR часть повреждена
            if (!MacroBlockIsValid(0)) return; // не возможно восстановить макроблок, т.к. первый блок поврежден
            var xor = GetMacroBlockAndCrc32(2); // данные XOR
            var block0 = GetMacroBlockAndCrc32(0); // данные первого блока
            var block1 = new byte[xor.Length]; // второй блок (восстановленный)
            for (var i = 0; i < xor.Length - SizeCrc32; i++)
            {
                block1[i] = (byte)(xor[i] ^ block0[i]);
            }
            Array.Copy(CRC32(block1[..^SizeCrc32]), 0, block1, SizeMacroBlock, SizeCrc32);
            Array.Copy(block1, 0, array, SizeHeaderStatic + SizeHeaderDynamicAndCrc32 + SizeHeaderAndMacroBlockAndCrc32, block1.Length);
            //if (MacroBlockIsValid(1)) Console.WriteLine($"packet {PacketNumber:0} Restore BLOCK1");// else Console.WriteLine($"packet {PacketNumber:0} NO RESTORE BLOCK1");
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
            if (data.Length < SizeFull)
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

            CheckAndRestorePacket();

            if (!DynamicHeaderIsValid(0))
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)}, HEADER_CRC_ERROR\n");
                Check = ChunkState.ErrorHeaderCrc;
                return;
            }

            var lenData = BitConverter.ToUInt16(array, 6);
            if (lenData > SizeMacroBlock * 2) // Размер пакета не может быть более двух макроблоков
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)} LEN_DATA_ERROR, len={lenData}>{SizeMacroBlock * 2}\n");
                Check = ChunkState.ErrorSize;
                return;
            }

            /*
            if (!MacroBlockIsValid(0))
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)}, BLOCK_0_CRC_ERROR\n");
                Check = ChunkState.ErrorDataCrc;
                return;
            }
            if (!MacroBlockIsValid(1))
            {
                //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)}, BLOCK_1_CRC_ERROR\n");
                Check = ChunkState.ErrorDataCrc;
                return;
            }
            */

            //if (PrintLog) Console.WriteLine($"{Convert.ToHexString(data)}, OK");
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
