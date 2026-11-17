using System.Globalization;

namespace CareNexus.Services;

// Bangladesh time helper.
// Bangladesh is always UTC+6 (no summer time), so this gives the same
// result on your PC and on the Azure server (which runs in UTC).
//
// We save dates and times in the database as simple text:
//   date      -> "2026-11-15"
//   time      -> "14:30"            (24-hour)
//   date+time -> "2026-11-15 14:30:00"
public static class Clock
{
    // current Bangladesh date and time
    public static DateTime Now()
    {
        DateTime bdTime = DateTime.UtcNow.AddHours(6);
        return DateTime.SpecifyKind(bdTime, DateTimeKind.Unspecified);
    }

    // today's date as text, e.g. "2026-11-15"
    public static string Today()
    {
        return Now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // now as text, e.g. "2026-11-15 14:30:05"
    public static string NowText()
    {
        return Now().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    // DateTime -> "2026-11-15"
    public static string DateText(DateTime date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // is this text a real date like "2026-11-15"?
    public static bool IsDate(string? text)
    {
        DateTime check;
        return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out check);
    }

    // "2026-11-15" -> DateTime
    public static DateTime ParseDate(string date)
    {
        return DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // "2026-11-15 14:30:05" -> DateTime
    public static DateTime ParseDateTime(string text)
    {
        return DateTime.ParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    // "2026-11-15" + "09:30" -> DateTime
    public static DateTime Combine(string date, string time)
    {
        return DateTime.ParseExact(date + " " + time, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    // "14:30" -> "2:30 PM"
    public static string AmPm(string time)
    {
        DateTime t = DateTime.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);
        return t.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    // DateTime -> "2:30 PM"
    public static string AmPm(DateTime time)
    {
        return time.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    // "2026-11-15" -> "Today" or "Mon, 16 Nov"
    public static string NiceDate(string date)
    {
        if (date == Today()) return "Today";
        return ParseDate(date).ToString("ddd, dd MMM", CultureInfo.InvariantCulture);
    }

    // "2026-11-15" -> "15 Nov 2026"
    public static string LongDate(string date)
    {
        return ParseDate(date).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    }

    // "2026-11-15 14:30:05" -> "15 Nov 2026, 2:30 PM"
    public static string LongDateTime(string text)
    {
        return ParseDateTime(text).ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture);
    }

    // Bangladesh time -> milliseconds since 1970 (used by the JavaScript timer)
    public static long ToUnixMs(DateTime bdTime)
    {
        return new DateTimeOffset(bdTime, TimeSpan.FromHours(6)).ToUnixTimeMilliseconds();
    }

    // 1500 -> "৳1,500"
    public static string Taka(int? amount)
    {
        if (amount == null) return "—";
        return "৳" + amount.Value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
