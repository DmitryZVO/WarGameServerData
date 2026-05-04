using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data.SQLite;
using System.Drawing;
using System.Text;
using WarGameServerData.Controllers;
using WarGameServerData.Other;

namespace WarGameServerData.Data;

public class StaticObjects
{
    public long TimeStamp { get; set; } = DateTime.Now.Ticks;
    public List<StaticObject> Items { get; set; } = new();

    //private int _counter = 1;

    public async void StartAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
    }

    public async Task<bool> LoadAsync(CancellationToken ct = default)
    {
        var sqlite = new SQLiteConnection($@"Data Source={Directory.GetCurrentDirectory()}\DB\staticObj.db3;Version=3;");
        try
        {
            await sqlite.OpenAsync(ct);
        }
        catch // БД не существует!
        {
            WriteLog(LogLevel.Information, "Не найден staticObj.db3");
            return false;
        }

        await using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Items( " +
                          "id INTEGER NOT NULL, " +
                          "type INTEGER NOT NULL, " +
                          "coords TEXT NOT NULL, " +
                          "params TEXT NOT NULL, " +
                          "name TEXT NOT NULL); ";
        await cmd.ExecuteNonQueryAsync(ct);

        cmd.CommandText = "SELECT * FROM Items";
        var countAll = 0;
        await using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                try
                {
                    var id = (int)(long)reader["id"];
                    var type = (int)(long)reader["type"];
                    var coords = (string)reader["coords"];
                    var t = reader["params"];
                    var par = t is DBNull ? string.Empty : (string)(t);
                    var name = (string)reader["name"];
                    Items.Add(new StaticObject
                    {
                        Id = id,
                        Type = type,
                        Coords = StaticObject.StringToCoords(coords),
                        ParamsJsonString = par,
                        Name = name,
                    });
                    countAll++;
                }
                catch
                {
                    //
                }
            }
        }
        await sqlite.CloseAsync();
        TimeStamp = DateTime.Now.Ticks;
        //_counter = Items.Count>0 ? Items.Max(x => x.Id) + 1 : 1;
        WriteLog(LogLevel.Information, $"staticObj.db3 загружен! записей={countAll:0}");
        return true;
    }

    public async Task<bool> SaveAsync()
    {
        var sqlite = new SQLiteConnection($@"Data Source={Directory.GetCurrentDirectory()}\DB\staticObj.db3;Version=3;");
        try
        {
            sqlite.Open();
        }
        catch // БД не существует!
        {
            WriteLog(LogLevel.Information, "Не найден staticObj.db3");
            return false;
        }

        var transaction = sqlite.BeginTransaction();
        try
        {
            await using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS Items( " +
                              "id INTEGER NOT NULL, " +
                              "type INTEGER NOT NULL, " +
                              "coords TEXT NOT NULL, " +
                              "params TEXT NOT NULL, " +
                              "name TEXT NOT NULL); DELETE FROM Items; ";
            cmd.ExecuteNonQuery();
            foreach (var item in Items)
            {
                cmd.CommandText = "INSERT INTO Items " +
                                  "(id, type, coords, params, name) " +
                                  "VALUES (:id, :type, :coords, :params, :name); ";
                cmd.Parameters.AddWithValue("id", item.Id);
                cmd.Parameters.AddWithValue("type", item.Type);
                cmd.Parameters.AddWithValue("coords", StaticObject.CoordsToString(item.Coords));
                cmd.Parameters.AddWithValue("params", item.ParamsJsonString);
                cmd.Parameters.AddWithValue("name", item.Name);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch (Exception ex)
        {
            WriteLog(LogLevel.Error, $"Ошибка записи в SQL, ex: {ex.Message}");
            transaction.Rollback();
            sqlite.Close();
            return false;
        }
        sqlite.Close();

        WriteLog(LogLevel.Information, "staticObj.db3 обновлен");
        return true;
    }

    public static void WriteLog(LogLevel level, string e)
    {
        Core.IoC.Services.GetRequiredService<ILogger<WebControllerStaticObjects>>().Log(level, e);
    }
}

public class StaticObject
{
    // Номер объекта
    public int Id { get; set; }
    // Тип объекта:
    // 0 - город
    // 1 - точка интереса (флажок)
    // 10 - полигон (разрешенная рабочая область)
    // 11 - полигон (запрещенная рабочая область)
    public int Type { get; set; }
    // Точки объекта
    public List<PointF> Coords { get; set; } = new(); 
    //Текстовое имя
    public string Name { get; set; } = string.Empty;
    //Дополнительные параметры JSON
    public string ParamsJsonString { get; set; } = string.Empty;

    public static string CoordsToString(List<PointF> coords)
    {
        var sb = new StringBuilder();
        foreach (var c in coords)
        {
            sb.Append($"{c.X.ToStringF6()}, {c.Y.ToStringF6()}, ");
        }
        return sb.ToString();
    }
    public static List<PointF> StringToCoords(string text)
    {
        var ret = new List<PointF>();
        var ss = text.Split(", ");
        var n = 0;
        do
        {
            if (ss[n].Equals(string.Empty)) break;

            var x = 0.0f;
            var y = 0.0f;
            try
            {
                x = float.Parse(ss[n].Replace(".",","));
            }
            catch
            {
                //
            }

            try
            {
                y = float.Parse(ss[n + 1].Replace(".", ","));
            }
            catch
            {
                //
            }
            ret.Add(new PointF(x, y));
            n += 2;
        } while (n < ss.Length);
        return ret;
    }
}
