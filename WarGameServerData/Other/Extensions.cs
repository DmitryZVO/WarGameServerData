namespace WarGameServerData.Other;

public static class Extensions
{
    public static string ToStringF2(this double num) // Приведение double к виду "0.00"
    {
        return num.ToString("F2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
    }
    public static string ToStringF2(this float num) // Приведение float к виду "0.00"
    {
        return num.ToString("F2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
    }
    public static string ToStringF6(this double num) // Приведение double к виду "0.000000"
    {
        return num.ToString("F6", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
    }
    public static string ToStringF6(this float num) // Приведение float к виду "0.000000"
    {
        return num.ToString("F6", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
    }
}