using CareNexus.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CareNexus.Models;

// The steps of one serial, written once so we never misspell them
public static class SerialStatus
{
    public const string Waiting = "Waiting";   // booked, not at the hospital yet
    public const string Present = "Present";   // assistant marked "Present"
    public const string InRoom = "InRoom";     // timer running, patient is with the doctor
    public const string Done = "Done";         // timer stopped, visit finished
    public const string Absent = "Absent";     // absent 2 times, out of the queue
}

// One booked serial (appointment)
[BsonIgnoreExtraElements]
public class Serial
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string PatientId { get; set; } = "";
    public string DoctorId { get; set; } = "";
    public string ChamberId { get; set; } = "";
    public string Date { get; set; } = "";          // "2026-11-15"

    public int SerialNo { get; set; }               // #1, #2, #3 ...
    public string SlotTime { get; set; } = "";      // booked time, e.g. "09:30"
    public string Problem { get; set; } = "";

    public string Status { get; set; } = SerialStatus.Waiting;
    public int QueueOrder { get; set; }             // place in the line (changes when absent)
    public int AbsentCount { get; set; }

    public string? StartedAt { get; set; }          // timer started, e.g. "2026-11-15 09:31:10"
    public string? EndedAt { get; set; }            // timer stopped
    public int? Fee { get; set; }                   // money received (Taka)

    // text shown to the user
    public string Label()
    {
        if (Status == SerialStatus.Present) return "Present";
        if (Status == SerialStatus.InRoom) return "In room";
        if (Status == SerialStatus.Done) return "Done";
        if (Status == SerialStatus.Absent) return "Absent";
        return "Waiting";
    }

    // colour of the status badge (classes are in healthcare.css)
    public string CssClass()
    {
        if (Status == SerialStatus.Present) return "status status-checkedin";
        if (Status == SerialStatus.InRoom) return "status status-inconsultation";
        if (Status == SerialStatus.Done) return "status status-completed";
        if (Status == SerialStatus.Absent) return "status status-absent";
        return "status";
    }

    // how long the visit took, in minutes (null if not finished)
    public int? MinutesTaken()
    {
        if (StartedAt == null || EndedAt == null) return null;

        TimeSpan time = Clock.ParseDateTime(EndedAt) - Clock.ParseDateTime(StartedAt);
        int minutes = (int)Math.Round(time.TotalMinutes);
        if (minutes < 1) minutes = 1;
        return minutes;
    }
}
