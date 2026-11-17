using CareNexus.Models;
using MongoDB.Driver;

namespace CareNexus.Services;

// Taking a serial: which chamber dates a doctor has,
// which 10-minute slots are free, and saving the booking.
public class BookingService
{
    public const int DaysAhead = 7;    // patients can book up to 7 days ahead (today + 6 more days)

    private readonly Db _db;

    public BookingService(Db db)
    {
        _db = db;
    }

    // free 10-minute slots of a chamber on a date, e.g. ["09:00", "09:10", ...]
    // (slots already booked or already in the past are skipped)
    public List<string> FreeSlots(Chamber chamber, string date)
    {
        // the chamber must run on that day
        if (!chamber.RunsOn(Clock.ParseDate(date))) return new List<string>();

        List<Serial> booked = _db.Serials.Find(s => s.ChamberId == chamber.Id && s.Date == date).ToList();
        List<string> takenTimes = booked.Select(s => s.SlotTime).ToList();

        DateTime now = Clock.Now();
        DateTime time = Clock.Combine(date, chamber.StartTime);
        DateTime end = Clock.Combine(date, chamber.EndTime);

        List<string> free = new List<string>();
        while (time < end)
        {
            string text = time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            if (!takenTimes.Contains(text) && time > now)
            {
                free.Add(text);
            }
            time = time.AddMinutes(QueueService.SlotMinutes);
        }
        return free;
    }

    // saves a new serial
    public Result Book(string patientId, string doctorId, string chamberId, string date, string slot, string problem)
    {
        Chamber? chamber = _db.GetChamber(chamberId);
        if (chamber == null || chamber.DoctorId != doctorId)
        {
            return Result.Fail("Please choose a valid chamber.");
        }

        // only today up to 7 days ahead
        string today = Clock.Today();
        string lastDay = Clock.DateText(Clock.Now().Date.AddDays(DaysAhead - 1));
        if (string.Compare(date, today) < 0 || string.Compare(date, lastDay) > 0)
        {
            return Result.Fail("You can book from today up to " + DaysAhead + " days ahead.");
        }

        if (string.IsNullOrWhiteSpace(problem))
        {
            return Result.Fail("Please write your problem in short.");
        }

        // one serial per doctor per day
        bool alreadyBooked = _db.Serials.CountDocuments(s => s.PatientId == patientId && s.DoctorId == doctorId
                                                          && s.Date == date && s.Status != SerialStatus.Absent) > 0;
        if (alreadyBooked)
        {
            return Result.Fail("You already have a serial with this doctor on that day.");
        }

        if (!FreeSlots(chamber, date).Contains(slot))
        {
            return Result.Fail("That time slot was just taken. Please pick another one.");
        }

        // serial number from the slot: 09:00 -> #1, 09:10 -> #2 ...
        double minutesFromStart = (Clock.Combine(date, slot) - Clock.Combine(date, chamber.StartTime)).TotalMinutes;
        int serialNo = (int)(minutesFromStart / QueueService.SlotMinutes) + 1;

        Serial serial = new Serial();
        serial.PatientId = patientId;
        serial.DoctorId = doctorId;
        serial.ChamberId = chamberId;
        serial.Date = date;
        serial.SlotTime = slot;
        serial.SerialNo = serialNo;
        serial.QueueOrder = serialNo;
        serial.Problem = problem.Trim();

        _db.Serials.InsertOne(serial);

        return Result.Success("Serial #" + serialNo + " confirmed for " + Clock.NiceDate(date) + " at " + Clock.AmPm(slot) + ".");
    }
}
