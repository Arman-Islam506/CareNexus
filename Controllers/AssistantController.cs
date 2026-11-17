using System.Security.Claims;
using CareNexus.Models;
using CareNexus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CareNexus.Controllers;

// Only logged-in assistants can open these pages
[Authorize(Roles = Roles.Assistant)]
public class AssistantController : Controller
{
    private readonly Db _db;
    private readonly QueueService _queue;
    private readonly BookingService _booking;

    public AssistantController(Db db, QueueService queue, BookingService booking)
    {
        _db = db;
        _queue = queue;
        _booking = booking;
    }

    // the doctor this assistant works for (null if the admin didn't set one)
    private User? MyDoctor()
    {
        string myId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        User? me = _db.GetUser(myId);
        if (me == null) return null;
        return _db.GetUser(me.DoctorId);
    }

    // ===================== SERIAL QUEUE =====================

    // /Assistant  or  /Assistant?date=2026-11-16&chamberId=...
    public IActionResult Index(string? chamberId, string? date)
    {
        User? doctor = MyDoctor();
        if (doctor == null)
        {
            return View("NoDoctor");
        }

        // which day to show (today if no date or a wrong date)
        string day = Clock.Today();
        if (Clock.IsDate(date)) day = date!;

        Chamber? chamber = _queue.PickChamber(doctor.Id!, chamberId, day);
        ViewBag.Doctor = doctor;
        ViewBag.Chamber = chamber;
        ViewBag.Chambers = _queue.ChambersOn(doctor.Id!, day);
        ViewBag.Date = day;
        ViewBag.Upcoming = _queue.UpcomingDays(doctor.Id!);

        List<QueueRow> rows = new List<QueueRow>();
        if (chamber != null)
        {
            rows = _queue.BuildQueue(chamber, day);
        }
        return View(rows);
    }

    // The buttons on the queue page. Each one does its job, shows a message,
    // and goes back to the same chamber.

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Present(string serialId, string? chamberId, string? date)
    {
        User? doctor = MyDoctor();
        if (doctor == null) return RedirectToAction("Index");

        ShowMessage(_queue.MarkPresent(serialId, doctor.Id!));
        return RedirectToAction("Index", new { chamberId, date });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Absent(string serialId, string? chamberId, string? date)
    {
        User? doctor = MyDoctor();
        if (doctor == null) return RedirectToAction("Index");

        ShowMessage(_queue.MarkAbsent(serialId, doctor.Id!));
        return RedirectToAction("Index", new { chamberId, date });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult StartTimer(string serialId, string? chamberId, string? date)
    {
        User? doctor = MyDoctor();
        if (doctor == null) return RedirectToAction("Index");

        ShowMessage(_queue.StartTimer(serialId, doctor.Id!));
        return RedirectToAction("Index", new { chamberId, date });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult StopTimer(string serialId, string? chamberId, string? date)
    {
        User? doctor = MyDoctor();
        if (doctor == null) return RedirectToAction("Index");

        ShowMessage(_queue.StopTimer(serialId, doctor.Id!));
        // the "Print Rx" button shows in the queue row once the doctor saves the prescription
        return RedirectToAction("Index", new { chamberId, date });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SaveFee(string serialId, int? amount, string? chamberId, string? date)
    {
        User? doctor = MyDoctor();
        if (doctor == null) return RedirectToAction("Index");

        if (amount == null)
        {
            TempData["Error"] = "Please type the amount first.";
            return RedirectToAction("Index", new { chamberId, date });
        }

        ShowMessage(_queue.SaveFee(serialId, doctor.Id!, amount.Value));
        return RedirectToAction("Index", new { chamberId, date });
    }

    // green message if it worked, red if not
    private void ShowMessage(Result result)
    {
        if (result.Ok) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;
    }

    // ===================== ADD SERIAL (walk-in or phone call) =====================

    [HttpGet]
    public IActionResult AddSerial(string? date, string? chamberId)
    {
        User? doctor = MyDoctor();
        if (doctor == null) return View("NoDoctor");

        string today = Clock.Today();
        string lastDay = Clock.DateText(Clock.Now().Date.AddDays(BookingService.DaysAhead - 1));

        // the day (today if empty or outside today .. 7 days ahead)
        string day = today;
        if (Clock.IsDate(date) && string.Compare(date, today) >= 0 && string.Compare(date, lastDay) <= 0)
        {
            day = date!;
        }

        List<Chamber> chambers = _queue.ChambersOn(doctor.Id!, day);
        Chamber? chamber = chambers.FirstOrDefault(c => c.Id == chamberId);
        if (chamber == null) chamber = chambers.FirstOrDefault();

        List<string> freeSlots = new List<string>();
        if (chamber != null)
        {
            freeSlots = _booking.FreeSlots(chamber, day);
        }

        ViewBag.Doctor = doctor;
        ViewBag.Date = day;
        ViewBag.MinDate = today;
        ViewBag.MaxDate = lastDay;
        ViewBag.Chambers = chambers;
        ViewBag.Chamber = chamber;
        ViewBag.FreeSlots = freeSlots;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddSerial(string? patientSearch, string? newName, string? newPhone,
                                   string date, string chamberId, string slot, string problem)
    {
        User? doctor = MyDoctor();
        if (doctor == null) return RedirectToAction("Index");

        // 1. find the patient: by ID (e.g. 104) or by phone number
        User? patient = null;
        if (!string.IsNullOrWhiteSpace(patientSearch))
        {
            string text = patientSearch.Trim();
            patient = _db.Users.Find(u => u.Role == Roles.Patient && (u.LoginId == text || u.Phone == text)).FirstOrDefault();

            if (patient == null)
            {
                TempData["Error"] = "No patient found with ID or phone \"" + text + "\". Fill the New patient part instead.";
                return RedirectToAction("AddSerial", new { date, chamberId });
            }
        }

        // 2. not found -> make a new patient account
        bool isNewPatient = false;
        if (patient == null)
        {
            if (string.IsNullOrWhiteSpace(newName) || string.IsNullOrWhiteSpace(newPhone))
            {
                TempData["Error"] = "Type an existing patient's ID / phone, or a new patient's name and phone.";
                return RedirectToAction("AddSerial", new { date, chamberId });
            }

            patient = new User();
            patient.LoginId = _db.NextLoginId(Roles.Patient);
            patient.Name = newName.Trim();
            patient.Phone = newPhone.Trim();
            patient.Email = "patient" + patient.LoginId + "@carenexus.com";   // simple email, can be changed later
            patient.Role = Roles.Patient;
            patient.PasswordHash = BCrypt.Net.BCrypt.HashPassword("1234", 12);
            _db.Users.InsertOne(patient);
            isNewPatient = true;
        }

        // 3. book the serial (same rules as when a patient books online)
        Result result = _booking.Book(patient.Id!, doctor.Id!, chamberId, date ?? "", slot ?? "", problem ?? "");

        if (!result.Ok)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction("AddSerial", new { date, chamberId });
        }

        string message = result.Message + " Patient: " + patient.Name + " (ID " + patient.LoginId + ").";
        if (isNewPatient)
        {
            message = message + " New account made — tell the patient: ID " + patient.LoginId + ", password 1234.";
        }
        TempData["Success"] = message;
        return RedirectToAction("Index", new { date, chamberId });
    }

    // ===================== PRINT PRESCRIPTION =====================

    // the assistant prints the prescription for a patient who wants a paper copy
    public IActionResult PrintPrescription(string serialId)
    {
        User? doctor = MyDoctor();
        Serial? serial = _db.GetSerial(serialId);

        // only serials of my doctor
        if (doctor == null || serial == null || serial.DoctorId != doctor.Id)
        {
            TempData["Error"] = "Serial not found.";
            return RedirectToAction("Index");
        }

        Prescription? prescription = _db.Prescriptions.Find(p => p.SerialId == serial.Id).FirstOrDefault();
        if (prescription == null)
        {
            TempData["Error"] = "The doctor has not written the prescription for serial #" + serial.SerialNo + " yet.";
            return RedirectToAction("Index", new { date = serial.Date, chamberId = serial.ChamberId });
        }

        ViewBag.Serial = serial;
        return View(prescription);
    }

    // ===================== COLLECTIONS =====================

    // money received today (all chambers of the doctor)
    public IActionResult Collections()
    {
        User? doctor = MyDoctor();
        if (doctor == null)
        {
            return View("NoDoctor");
        }

        string doctorId = doctor.Id!;
        string today = Clock.Today();
        List<Serial> paid = _db.Serials.Find(s => s.DoctorId == doctorId && s.Date == today && s.Fee != null).ToList();
        paid = paid.OrderBy(s => s.SerialNo).ToList();

        ViewBag.Doctor = doctor;
        ViewBag.Total = _queue.CollectionFor(doctorId, today);
        return View(paid);
    }
}
