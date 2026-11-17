using CareNexus.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CareNexus.Models;

// A chamber = when and in which room a doctor sits in our hospital.
// Example: Dr. Rahman sits in Room 201 on Sat, Mon, Wed from 09:00 to 12:00.
// A doctor can have more than one chamber (e.g. morning and evening).
// (Chambers in many hospitals is future work.)
[BsonIgnoreExtraElements]
public class Chamber
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string DoctorId { get; set; } = "";
    public string Room { get; set; } = "";
    public List<string> Days { get; set; } = new List<string>();   // "Sat", "Sun", "Mon" ...
    public string StartTime { get; set; } = "09:00";               // 24-hour text
    public string EndTime { get; set; } = "12:00";

    // all 7 day names, in order (used for checkboxes)
    public static readonly string[] AllDays = { "Sat", "Sun", "Mon", "Tue", "Wed", "Thu", "Fri" };

    // does this chamber run on this date?
    public bool RunsOn(DateTime date)
    {
        string dayName = date.DayOfWeek.ToString().Substring(0, 3);   // "Saturday" -> "Sat"
        return Days.Contains(dayName);
    }

    // "Room 201 · 9:00 AM – 12:00 PM"
    public string Title()
    {
        string room = Room == "" ? "Chamber" : Room;
        return room + " · " + TimeText();
    }

    // "9:00 AM – 12:00 PM"
    public string TimeText()
    {
        return Clock.AmPm(StartTime) + " – " + Clock.AmPm(EndTime);
    }

    // "Sat, Mon, Wed"
    public string DaysText()
    {
        return string.Join(", ", Days);
    }

    // how many 10-minute serials fit in one session
    public int SlotCount()
    {
        DateTime start = Clock.Combine("2000-01-01", StartTime);
        DateTime end = Clock.Combine("2000-01-01", EndTime);
        return (int)((end - start).TotalMinutes / QueueService.SlotMinutes);
    }
}
