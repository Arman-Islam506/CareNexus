using CareNexus.Models;
using MongoDB.Driver;

namespace CareNexus.Services;

// The live serial queue: present / absent, the consultation timer,
// fees, and the estimated time for every patient.
//
// How the estimated time works:
//   1. Every patient books a fixed 10-minute slot (09:00, 09:10, 09:20 ...).
//   2. During the chamber, the times move with the real visits:
//      - a finished visit: the next patient can go in when it ended
//      - a running visit: expected to end 10 min after it started
//        (or "now", if it is already taking longer)
//      - every waiting patient after that gets +10 min
//   3. A patient's time is never earlier than his booked slot.
//   So if a visit takes 15 min, everyone after moves 5 min later.
public class QueueService
{
    public const int SlotMinutes = 10;

    private readonly Db _db;

    public QueueService(Db db)
    {
        _db = db;
    }

    // ===================== READING =====================

    // the doctor's chambers that run on a date ("2026-11-15")
    public List<Chamber> ChambersOn(string doctorId, string date)
    {
        DateTime day = Clock.ParseDate(date);
        List<Chamber> result = new List<Chamber>();

        foreach (Chamber c in _db.ChambersOf(doctorId))
        {
            if (c.RunsOn(day)) result.Add(c);
        }
        return result;
    }

    // which chamber to show: the one picked in the dropdown,
    // else (today) the one running now / next, else the last one; (other days) the first one
    public Chamber? PickChamber(string doctorId, string? chamberId, string date)
    {
        List<Chamber> chambers = ChambersOn(doctorId, date);
        if (chambers.Count == 0) return null;

        foreach (Chamber c in chambers)
        {
            if (c.Id == chamberId) return c;
        }

        if (date != Clock.Today()) return chambers[0];

        string nowTime = Clock.Now().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        foreach (Chamber c in chambers)
        {
            if (string.Compare(nowTime, c.EndTime) < 0) return c;   // not finished yet
        }
        return chambers[chambers.Count - 1];
    }

    // booked serials from today onwards, grouped by date and chamber.
    // Example text: "Mon, 16 Nov · Lakeview Medical Centre · 3 serials"
    // Value = "<chamberId>|2026-11-16" (used to open that day's queue)
    public List<Option> UpcomingDays(string doctorId)
    {
        string today = Clock.Today();
        List<Serial> serials = _db.Serials.Find(s => s.DoctorId == doctorId).ToList();

        // keep only today and later, not finished
        List<Serial> upcoming = new List<Serial>();
        foreach (Serial s in serials)
        {
            bool notPast = string.Compare(s.Date, today) >= 0;
            bool active = s.Status == SerialStatus.Waiting || s.Status == SerialStatus.Present || s.Status == SerialStatus.InRoom;
            if (notPast && active) upcoming.Add(s);
        }

        // group: one line per (date, chamber)
        List<Option> result = new List<Option>();
        var groups = upcoming.GroupBy(s => s.Date + "|" + s.ChamberId).OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            Serial first = group.First();
            Chamber? chamber = _db.GetChamber(first.ChamberId);
            string place = chamber != null ? chamber.Title() : "Unknown chamber";

            Option option = new Option();
            option.Value = first.ChamberId + "|" + first.Date;
            option.Text = Clock.NiceDate(first.Date) + " · " + place + " · " + group.Count() + " serial(s)";
            result.Add(option);
        }
        return result;
    }

    // builds the queue of one chamber on one date, with estimated times
    public List<QueueRow> BuildQueue(Chamber chamber, string date)
    {
        List<Serial> serials = _db.Serials.Find(s => s.ChamberId == chamber.Id && s.Date == date).ToList();

        // split the serials into groups
        List<Serial> done = serials.Where(s => s.Status == SerialStatus.Done).OrderBy(s => s.EndedAt).ToList();
        Serial? inRoom = serials.FirstOrDefault(s => s.Status == SerialStatus.InRoom);
        List<Serial> waiting = serials.Where(s => s.Status == SerialStatus.Waiting || s.Status == SerialStatus.Present)
                                      .OrderBy(s => s.QueueOrder).ToList();
        List<Serial> absent = serials.Where(s => s.Status == SerialStatus.Absent).OrderBy(s => s.SerialNo).ToList();

        List<QueueRow> rows = new List<QueueRow>();
        DateTime now = Clock.Now();

        // "cursor" = the time when the next patient can go in
        DateTime cursor = Clock.Combine(date, chamber.StartTime);

        // 1. finished visits
        foreach (Serial s in done)
        {
            DateTime ended = Clock.ParseDateTime(s.EndedAt!);
            if (ended > cursor) cursor = ended;
            rows.Add(MakeRow(s, null));
        }

        // 2. the patient in the room now
        if (inRoom != null)
        {
            DateTime started = Clock.ParseDateTime(inRoom.StartedAt!);
            DateTime expectedEnd = started.AddMinutes(SlotMinutes);
            if (now > expectedEnd) expectedEnd = now;          // running late
            if (expectedEnd > cursor) cursor = expectedEnd;
            rows.Add(MakeRow(inRoom, started));
        }
        else if (date == Clock.Today() && now > cursor)
        {
            cursor = now;                                       // room is free right now
        }

        // 3. waiting patients get +10 minutes each
        int ahead = 0;
        if (inRoom != null) ahead = 1;

        foreach (Serial s in waiting)
        {
            DateTime booked = Clock.Combine(date, s.SlotTime);
            DateTime estimate = cursor;
            if (estimate < booked) estimate = booked;           // never earlier than the booked slot

            QueueRow row = MakeRow(s, estimate);
            row.PeopleAhead = ahead;
            row.DelayMinutes = (int)Math.Round((estimate - booked).TotalMinutes);
            rows.Add(row);

            ahead = ahead + 1;
            cursor = estimate.AddMinutes(SlotMinutes);
        }

        // 4. absent patients at the end
        foreach (Serial s in absent)
        {
            rows.Add(MakeRow(s, null));
        }

        return rows;
    }

    private QueueRow MakeRow(Serial s, DateTime? estimate)
    {
        User? patient = _db.GetUser(s.PatientId);

        QueueRow row = new QueueRow();
        row.Serial = s;
        row.PatientName = patient != null ? patient.Name : "Unknown";
        row.PatientLoginId = patient != null ? patient.LoginId : "";
        row.PatientPhone = patient != null ? patient.Phone : "";
        row.Estimated = estimate;
        row.HasPrescription = _db.Prescriptions.CountDocuments(p => p.SerialId == s.Id) > 0;
        return row;
    }

    // total money collected for a doctor on one date (all chambers)
    public int CollectionFor(string doctorId, string date)
    {
        List<Serial> serials = _db.Serials.Find(s => s.DoctorId == doctorId && s.Date == date).ToList();
        int total = 0;
        foreach (Serial s in serials)
        {
            if (s.Fee != null) total = total + s.Fee.Value;
        }
        return total;
    }

    // ===================== ASSISTANT ACTIONS =====================
    // Each action checks that the serial belongs to this doctor (security).

    // patient is at the hospital (also works for an absent patient who came late)
    public Result MarkPresent(string serialId, string doctorId)
    {
        Serial? s = _db.GetSerial(serialId);
        if (s == null || s.DoctorId != doctorId) return Result.Fail("Serial not found.");

        if (s.Status == SerialStatus.Absent)
        {
            s.QueueOrder = NextLateOrder(s);   // came late: goes to the end of the line
        }
        else if (s.Status != SerialStatus.Waiting)
        {
            return Result.Fail("Serial #" + s.SerialNo + " is already " + s.Label() + ".");
        }

        s.Status = SerialStatus.Present;
        _db.Serials.ReplaceOne(x => x.Id == s.Id, s);
        return Result.Success("Serial #" + s.SerialNo + " marked present.");
    }

    // patient not here when called:
    // 1st time -> moves to the end of the line, 2nd time -> out of the queue
    public Result MarkAbsent(string serialId, string doctorId)
    {
        Serial? s = _db.GetSerial(serialId);
        if (s == null || s.DoctorId != doctorId) return Result.Fail("Serial not found.");

        if (s.Status != SerialStatus.Waiting && s.Status != SerialStatus.Present)
        {
            return Result.Fail("Serial #" + s.SerialNo + " cannot be marked absent now.");
        }

        s.AbsentCount = s.AbsentCount + 1;
        string message;

        if (s.AbsentCount >= 2)
        {
            s.Status = SerialStatus.Absent;
            message = "Serial #" + s.SerialNo + " absent again — removed from the queue.";
        }
        else
        {
            s.Status = SerialStatus.Waiting;
            s.QueueOrder = NextLateOrder(s);
            message = "Serial #" + s.SerialNo + " not present — moved to the end of the queue.";
        }

        _db.Serials.ReplaceOne(x => x.Id == s.Id, s);
        return Result.Success(message);
    }

    // patient goes into the doctor's room: start the timer
    public Result StartTimer(string serialId, string doctorId)
    {
        Serial? s = _db.GetSerial(serialId);
        if (s == null || s.DoctorId != doctorId) return Result.Fail("Serial not found.");

        if (s.Status != SerialStatus.Present)
        {
            return Result.Fail("Mark the patient present before starting the timer.");
        }

        bool roomBusy = _db.Serials.CountDocuments(x => x.ChamberId == s.ChamberId && x.Date == s.Date
                                                     && x.Status == SerialStatus.InRoom) > 0;
        if (roomBusy)
        {
            return Result.Fail("Another patient is still in the room. Stop that timer first.");
        }

        s.Status = SerialStatus.InRoom;
        s.StartedAt = Clock.NowText();
        _db.Serials.ReplaceOne(x => x.Id == s.Id, s);
        return Result.Success("Timer started for serial #" + s.SerialNo + ".");
    }

    // patient comes out of the room: stop the timer
    public Result StopTimer(string serialId, string doctorId)
    {
        Serial? s = _db.GetSerial(serialId);
        if (s == null || s.DoctorId != doctorId) return Result.Fail("Serial not found.");

        if (s.Status != SerialStatus.InRoom)
        {
            return Result.Fail("This timer is not running.");
        }

        s.Status = SerialStatus.Done;
        s.EndedAt = Clock.NowText();
        _db.Serials.ReplaceOne(x => x.Id == s.Id, s);
        return Result.Success("Serial #" + s.SerialNo + " finished in " + s.MinutesTaken() + " min.");
    }

    // save the money received from the patient
    public Result SaveFee(string serialId, string doctorId, int amount)
    {
        if (amount < 0) return Result.Fail("Amount cannot be negative.");

        Serial? s = _db.GetSerial(serialId);
        if (s == null || s.DoctorId != doctorId) return Result.Fail("Serial not found.");

        s.Fee = amount;
        _db.Serials.ReplaceOne(x => x.Id == s.Id, s);
        return Result.Success(Clock.Taka(amount) + " saved for serial #" + s.SerialNo + ".");
    }

    // late patients get 1000+ so they always stay after the normal serials
    private int NextLateOrder(Serial s)
    {
        List<Serial> sameDay = _db.Serials.Find(x => x.ChamberId == s.ChamberId && x.Date == s.Date).ToList();

        int max = 1000;
        foreach (Serial x in sameDay)
        {
            if (x.QueueOrder > max) max = x.QueueOrder;
        }
        return max + 1;
    }
}
