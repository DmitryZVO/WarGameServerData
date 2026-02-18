using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using WarGameServerData.Data;
using WarGameServerData.Other;

namespace WarGameServerData.Model;

public class LanIn
{
    private const int UdpPortHb = 7777; // Штатный порт UDP для получения Heartbeat от игровых объектов (с отправкой пакетов-request в ответ)
    private const int UdpPortCamera = 7778; // Штатный порт UDP для получения потока H264 от камер игровых объектов

    // Структура любого правильного пакета:
    // 0x70, 0x70 - заголовок ZVO (2 байта UINT16)
    // 0xTT, 0xNN, 0xNN, 0xNN, 0xNN - уникальный тип и номер объекта (1 байт UCHAR8 + 4 байта UINT32)
    // 0xPP - Тип пакета (1 байт UCHAR8)
    // 0xLN, 0xLN - длинна полезной нагрузки (2 байта UINT16)
    // 0xNN..0xNN - тело пакета

    public async void LanInPortHbAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var connect = new UdpClient(UdpPortHb);
            try
            {
                // Получение данных
                var result = await connect.ReceiveAsync(ct);
                var client = result.RemoteEndPoint;
                var data = result.Buffer;
                // Парсинг входящего пакета
                var toSend = Core.IoC.Services.GetRequiredService<GameObjects>().ParseUdpPacket(data);

                // Оправляем данные в ответ на входящий пакет (если есть что отправить)
                if (toSend.Length > 0)
                {
                    const int timeOutMs = 100;
                    var udpTimeOut = new CancellationTokenSource();
                    udpTimeOut.CancelAfter(timeOutMs);
                    await connect.SendAsync(toSend, client, udpTimeOut.Token);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            connect.Close();
        }
    }

    public async void StartAsync(CancellationToken ct = default)
    {
        LanInPortHbAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            var items = Core.IoC.Services.GetRequiredService<GameObjects>().Items;
            lock (items)
            {
                items.ForEach(x => x.Telem.MBitServerIn = x.Telem.MBitServerInBytesCounter * 8.0f / 1000000.0f);
                items.ForEach(x => x.Telem.MBitServerInBytesCounter = 0);
            }
        }
    }
}
