using System.Security.Claims;
using CareNexus.Models;
using CareNexus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CareNexus.Controllers;

// Only logged-in doctors can open these pages
[Authorize(Roles = Roles.Doctor)]
public class DoctorController : Controller
{
    private readonly Db _db;
    private readonly QueueService _queue;

    public DoctorController(Db db, QueueService queue)
    {
        _db = db;
        _queue = queue;
    }

    // the logged-in doctor's Id
    private string MyId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    }

    // ===================== TODAY'S PATIENTS =====================

    // /Doctor  or  /Doctor?date=2026-11-16&chamberId=...
    public IActionResult Index(string? chamberId, string? date)
    {
        string myId = MyId();
        string today = Clock.Today();

        // which day to show (today if no date or a wrong date)
        string day = today;
        if (Clock.IsDate(date)) day = date!;

        Chamber? chamber = _queue.PickChamber(myId, chamberId, day);
        ViewBag.Chamber = chamber;
        ViewBag.Chambers = _queue.ChambersOn(myId, day);
        ViewBag.Date = day;
        ViewBag.Upcoming = _queue.UpcomingDays(myId);
        ViewBag.TodayTotal = _queue.CollectionFor(myId, today);

        List<QueueRow> rows = new List<QueueRow>();
        if (chamber != null)
        {
            rows = _queue.BuildQueue(chamber, day);
        }
        return View(rows);
    }

    // ===================== PRESCRIPTION =====================

    [HttpGet]
    public IActionResult Prescription(string serialId)
    {
        Serial? serial = _db.GetSerial(serialId);
        if (serial == null || serial.DoctorId != MyId())
        {
            TempData["Error"] = "Serial not found.";
            return RedirectToAction("Index");
        }

        // load the old prescription if it exists, else start an empty one
        Prescription? prescription = _db.Prescriptions.Find(p => p.SerialId == serial.Id).FirstOrDefault();
        if (prescription == null)
        {
            prescription = new Prescription();
            prescription.SerialId = serial.Id!;
        }

        string patientId = serial.PatientId;
        string myId = MyId();

        ViewBag.Serial = serial;
        ViewBag.Patient = _db.GetUser(patientId);
        ViewBag.PreviousVisits = _db.Serials.CountDocuments(s => s.PatientId == patientId && s.DoctorId == myId
                                                              && s.Status == SerialStatus.Done);
        ViewBag.LatestReport = _db.LatestReportOf(patientId);   // the patient's last report check (or null)
        return View(prescription);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Prescription(string serialId, string diagnosis, string medicines, string? advice, string? followUp)
    {
        Serial? serial = _db.GetSerial(serialId);
        if (serial == null || serial.DoctorId != MyId())
        {
            TempData["Error"] = "Serial not found.";
            return RedirectToAction("Index");
        }

        if (string.IsNullOrWhiteSpace(diagnosis) || string.IsNullOrWhiteSpace(medicines))
        {
            TempData["Error"] = "Diagnosis and medicines are required.";
            return RedirectToAction("Prescription", new { serialId });
        }

        Prescription? prescription = _db.Prescriptions.Find(p => p.SerialId == serial.Id).FirstOrDefault();
        bool isNew = prescription == null;

        if (prescription == null)
        {
            prescription = new Prescription();
            prescription.SerialId = serial.Id!;
            prescription.PatientId = serial.PatientId;
            prescription.DoctorId = serial.DoctorId;
            prescription.CreatedAt = Clock.NowText();
        }

        prescription.Diagnosis = diagnosis.Trim();
        prescription.Medicines = medicines.Trim();
        prescription.Advice = (advice ?? "").Trim();
        prescription.FollowUp = (followUp ?? "").Trim();

        if (isNew)
        {
            _db.Prescriptions.InsertOne(prescription);
        }
        else
        {
            _db.Prescriptions.ReplaceOne(p => p.Id == prescription.Id, prescription);
        }

        TempData["Success"] = "Prescription saved. The patient can see it now.";
        return RedirectToAction("Index", new { chamberId = serial.ChamberId, date = serial.Date });
    }

    // ===================== PATIENT HISTORY =====================

    // all serials with this doctor, search by patient ID, name, phone or email
    public IActionResult PatientHistory(string? search)
    {
        string myId = MyId();
        List<Serial> serials = _db.Serials.Find(s => s.DoctorId == myId).ToList();
        serials = serials.OrderByDescending(s => s.Date).ThenBy(s => s.SerialNo).ToList();

        List<QueueRow> rows = new List<QueueRow>();
        foreach (Serial s in serials)
        {
            User? patient = _db.GetUser(s.PatientId);
            if (patient == null) continue;

            // search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                string text = search.Trim().ToLower();
                bool match = patient.LoginId == text
                          || patient.Name.ToLower().Contains(text)
                          || patient.Phone.Contains(text)
                          || patient.Email.ToLower().Contains(text);
                if (!match) continue;
            }

            QueueRow row = new QueueRow();
            row.Serial = s;
            row.PatientName = patient.Name;
            row.PatientLoginId = patient.LoginId;
            row.PatientPhone = patient.Phone;
            row.HasPrescription = _db.Prescriptions.CountDocuments(p => p.SerialId == s.Id) > 0;
            rows.Add(row);
        }

        ViewBag.Search = search;
        return View(rows);
    }

    // ===================== CHAMBER SCHEDULE =====================

    public IActionResult Schedule()
    {
        List<Chamber> chambers = _db.ChambersOf(MyId());
        return View(chambers);
    }

    // ===================== PATIENT'S REPORT CHECK =====================

    // the doctor can open a saved report check of HIS OWN patient (read-only, printable)
    public IActionResult PatientReport(string id)
    {
        ReportCheck? check = _db.GetReportCheck(id);
        if (check == null)
        {
            TempData["Error"] = "Report not found.";
            return RedirectToAction("Index");
        }

        // only if this patient has at least one serial with me
        string myId = MyId();
        string patientId = check.PatientId;
        bool isMyPatient = _db.Serials.CountDocuments(s => s.PatientId == patientId && s.DoctorId == myId) > 0;
        if (!isMyPatient)
        {
            TempData["Error"] = "This is not your patient.";
            return RedirectToAction("Index");
        }

        return View(check);
    }
}
