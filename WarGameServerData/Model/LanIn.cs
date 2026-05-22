using Microsoft.Extensions.DependencyInjection;
using System.Net.Sockets;
using WarGameServerData.Data;
using WarGameServerData.Other;

namespace WarGameServerData.Model;

public class LanIn
{
    public const int UdpPortHb = 8000; // Штатный порт UDP для получения Heartbeat от игровых объектов (с отправкой пакетов-request в ответ)
    public const int UdpPortZvo = 2222; // Штатный порт UDP для получения пакетов от радио ZVO

    // Структура любого правильного пакета:
    // 0x70, 0x70 - заголовок ZVO (2 байта UINT16)
    // 0xTT, 0xNN, 0xNN, 0xNN, 0xNN - уникальный тип и номер объекта (1 байт UCHAR8 + 4 байта UINT32)
    // 0xPP - Тип пакета (1 байт UCHAR8)
    // 0xLN, 0xLN - длинна полезной нагрузки (2 байта UINT16)
    // 0xNN..0xNN - тело пакета
    private readonly CancellationToken _ct = new();

    private readonly List<float> CounterMeshHBlist = [0.0f, 0.0f, 0.0f, 0.0f, 0.0f];
    private int CounterMeshHB;

    private readonly List<float> CounterZvoHBlist = [0.0f, 0.0f, 0.0f, 0.0f, 0.0f];
    private int CounterZvoHB;
    public float GetCounterMeshHB()
    {
        lock (CounterMeshHBlist)
        {
            return CounterMeshHBlist.Sum() / CounterMeshHBlist.Count;
        }
    }
    public float GetCounterZvoHB()
    {
        lock (CounterZvoHBlist)
        {
            return CounterZvoHBlist.Sum() / CounterZvoHBlist.Count;
        }
    }

    public async void CheckAsyncHB(CancellationToken ct = default)
    {
        while (!_ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);

            lock (CounterMeshHBlist)
            {
                CounterMeshHBlist.Add(CounterMeshHB);
                CounterMeshHBlist.RemoveAt(0);
            }
            lock (CounterZvoHBlist)
            {
                CounterZvoHBlist.Add(CounterZvoHB);
                CounterZvoHBlist.RemoveAt(0);
            }

            CounterMeshHB = 0;
            CounterZvoHB = 0;
        }
    }

    public async void LanInPortHbAsync()
    {
        Core.IoC.Services.GetRequiredService<ZvoRadio>().OnNewPacketAsync += RecvZvoPacket;

        CheckAsyncHB();

        var connect = new UdpClient(UdpPortHb);
        while (!_ct.IsCancellationRequested)
        {
            try
            {
                // Получение данных
                var result = await connect.ReceiveAsync(_ct);
                var client = result.RemoteEndPoint;
                var data = result.Buffer;
                // Парсинг входящего пакета
                await Core.IoC.Services.GetRequiredService<GameObjects>().ParseUdpPacketAsync("192.168.1.241", data); // ZVO+MESH
                if ((data[0] & 0b01111111) == 0x10)
                {
                    if (!client.Address.ToString().Equals("127.0.0.1")) CounterMeshHB++;
                    if (client.Address.ToString().Equals("127.0.0.1")) CounterZvoHB++;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        connect.Close();
    }

    public static async Task RecvZvoPacket(byte[] data)
    {
        await new UdpClient().SendAsync(data, "127.0.0.1", UdpPortHb);
    }

    public async void StartAsync()
    {
        var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
        LanInPortHbAsync();

        while (!_ct.IsCancellationRequested)
        {
            await Task.Delay(1000, _ct);
            lock (items)
            {
                items.ForEach(x => x.Telem.MBitServerIn = (float)Math.Round(x.Telem.MBitServerInBytesCounter * 8.0f / 1000000.0f, 3));
                items.ForEach(x => x.Telem.MBitServerInBytesCounter = 0);
                items.ForEach(x => x.Telem.MBitServerOut = (float)Math.Round(x.Telem.MBitServerOutBytesCounter * 8.0f / 1000000.0f, 3));
                items.ForEach(x => x.Telem.MBitServerOutBytesCounter = 0);
            }
        }
    }
}
