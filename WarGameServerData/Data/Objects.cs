namespace WarGameServerData.Data;

public class Objects
{
    public long TimeStamp { get; set; } = DateTime.Now.Ticks;
    public List<Object> Items { get; set; } = new();
}

public class Object
{
    public bool Alive => (DateTime.Now - LastTime).TotalMilliseconds > 5000; // Проверка на статус жив/мертв
    public string Name { get; set; } = string.Empty; // Имя объекта
    public int Type { get; set; } // Тип объекта 0-тестовый ровер, 1-борщевик, 2-БЭК 
    public float LonX { get; set; } // Позиция по X
    public float LatY { get; set; } // Позиция по Y
    public float Z { get; set; } // Позиция по Z
    public float Angle { get; set; } // Угол поворота

    public Telem Telem { get; set; } = new(); // Телеметрия объекта
    public DateTime LastTime = DateTime.MinValue; // Время последнего пакета
}

public class Telem
{
    public float[] Servos { get; set; } = new float[8]; // Сервоприводы
}