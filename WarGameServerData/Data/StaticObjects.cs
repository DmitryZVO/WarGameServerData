using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data.SQLite;
using WarGameServerData.Controllers;
using WarGameServerData.Other;

namespace WarGameServerData.Data;

public class StaticObjects
{
    public DateTime TimeStamp { get; set; } = DateTime.Now;
    public List<StaticObject> Items { get; set; } = new();

    private int _counter = 1;

    public async void StartAsync(CancellationToken ct = default)
    {
        await LoadAsync();
    }

    public async Task<bool> LoadAsync()
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

        await using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Items( " +
                          "id INTEGER NOT NULL, " +
                          "type INTEGER NOT NULL, " +
                          "lonX REAL NOT NULL, " +
                          "latY REAL NOT NULL, " +
                          "visible INTEGER NOT NULL, " +
                          "name TEXT NOT NULL); ";
        cmd.ExecuteNonQuery();

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
                    var lonX = (double)reader["lonX"];
                    var latY = (double)reader["latY"];
                    var visible = (long)reader["visible"] == 1;
                    var name = (string)reader["name"];
                    Items.Add(new StaticObject()
                    {
                        Id = id,
                        Type = type,
                        LonX = lonX,
                        LatY = latY,
                        Visible = visible,
                        Name = name,
                    });
                    countAll++;
                }
                catch
                {
                    continue;
                }
            }
        }
        sqlite.Close();
        TimeStamp = DateTime.Now;
        _counter = Items.Count>0 ? Items.Max(x => x.Id) + 1 : 1;
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
                              "lonX REAL NOT NULL, " +
                              "latY REAL NOT NULL, " +
                              "visible INTEGER NOT NULL, " +
                              "name TEXT NOT NULL); DELETE FROM Items; ";
            cmd.ExecuteNonQuery();
            foreach (var item in Items)
            {
                cmd.CommandText = "INSERT INTO Items " +
                                  "(id, type, lonX, latY, visible, name) " +
                                  "VALUES (:id, :type, :lonX, :latY, :visible, :name); ";
                cmd.Parameters.AddWithValue("id", item.Id);
                cmd.Parameters.AddWithValue("type", item.Type);
                cmd.Parameters.AddWithValue("lonX", item.LonX);
                cmd.Parameters.AddWithValue("latY", item.LatY);
                cmd.Parameters.AddWithValue("visible", item.Visible ? 1 : 0);
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

    public void WriteLog(LogLevel level, string e)
    {
        Core.IoC.Services.GetRequiredService<ILogger<WebControllerStaticObjects>>().Log(level, e);
    }
}

public class StaticObject
{
    public int Id { get; set; } // Номер объекта
    public int Type { get; set; } // Тип объекта
    public double LonX { get; set; }
    public double LatY { get; set; }
    public bool Visible { get; set; }
    public string Name { get; set; } = string.Empty;
}
