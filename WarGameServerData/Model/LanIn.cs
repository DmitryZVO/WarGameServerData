using Microsoft.Extensions.DependencyInjection;
using System.Net.Sockets;
using WarGameServerData.Data;
using WarGameServerData.Other;
using static WarGameServerData.Data.CameraFrame;

namespace WarGameServerData.Model;

public class LanIn
{
    public readonly static int UdpPortCamera = 30000; // Штатный порт UDP для получения потока H264 от камер игровых объектов
    public readonly static int UdpPortHb = 7777; // Штатный порт UDP для получения Heartbeat от игровых объектов (с отправкой пакетов-request в ответ)

    // Структура любого правильного пакета:
    // 0x70, 0x70 - заголовок ZVO (2 байта UINT16)
    // 0xTT, 0xNN, 0xNN, 0xNN, 0xNN - уникальный тип и номер объекта (1 байт UCHAR8 + 4 байта UINT32)
    // 0xPP - Тип пакета (1 байт UCHAR8)
    // 0xLN, 0xLN - длинна полезной нагрузки (2 байта UINT16)
    // 0xNN..0xNN - тело пакета
    private readonly CancellationToken _ct = new();

    public async void LanInPortHbAsync()
    {
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
                await Core.IoC.Services.GetRequiredService<GameObjects>().ParseUdpPacketAsync(client.Address.ToString(), data);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        connect.Close();
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
