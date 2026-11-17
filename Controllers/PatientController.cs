using System.Security.Claims;
using CareNexus.Models;
using CareNexus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CareNexus.Controllers;

// Only logged-in patients can open these pages
[Authorize(Roles = Roles.Patient)]
public class PatientController : Controller
{
    private readonly Db _db;
    private readonly QueueService _queue;
    private readonly BookingService _booking;
    private readonly GeminiService _gemini;
    private readonly IWebHostEnvironment _env;

    public PatientController(Db db, QueueService queue, BookingService booking, GeminiService gemini, IWebHostEnvironment env)
    {
        _db = db;
        _queue = queue;
        _booking = booking;
        _gemini = gemini;
        _env = env;
    }

    // the logged-in patient's Id (saved in the login cookie)
    private string MyId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    }

    // my serials from today onwards that are not finished, sorted by date and time
    private List<Serial> MyUpcomingSerials()
    {
        string myId = MyId();
        string today = Clock.Today();

        List<Serial> all = _db.Serials.Find(s => s.PatientId == myId).ToList();
        List<Serial> upcoming = new List<Serial>();

        foreach (Serial s in all)
        {
            bool notPast = string.Compare(s.Date, today) >= 0;
            bool active = s.Status == SerialStatus.Waiting || s.Status == SerialStatus.Present || s.Status == SerialStatus.InRoom;
            if (notPast && active) upcoming.Add(s);
        }

        return upcoming.OrderBy(s => s.Date).ThenBy(s => s.SlotTime).ToList();
    }

    // ===================== DASHBOARD =====================

    // /Patient
    public IActionResult Index()
    {
        User? me = _db.GetUser(MyId());
        if (me == null) return RedirectToAction("Login", "Account");

        string myId = MyId();
        List<Serial> upcoming = MyUpcomingSerials();

        ViewBag.NextSerial = upcoming.FirstOrDefault();
        ViewBag.PrescriptionCount = _db.Prescriptions.CountDocuments(p => p.PatientId == myId);
        ViewBag.VisitCount = _db.Serials.CountDocuments(s => s.PatientId == myId && s.Status == SerialStatus.Done);

        return View(me);
    }

    // ===================== DOCTORS LIST =====================

    // /Patient/Doctors?search=rahman&departmentId=...
    public IActionResult Doctors(string? search, string? departmentId)
    {
        List<User> doctors = _db.AllDoctors();

        // filter by department
        if (!string.IsNullOrEmpty(departmentId))
        {
            doctors = doctors.Where(d => d.DepartmentId == departmentId).ToList();
        }

        // filter by name
        if (!string.IsNullOrWhiteSpace(search))
        {
            string text = search.Trim().ToLower();
            doctors = doctors.Where(d => d.Name.ToLower().Contains(text)).ToList();
        }

        ViewBag.Departments = _db.AllDepartments();
        ViewBag.Search = search;
        ViewBag.DepartmentId = departmentId;
        return View(doctors);
    }

    // ===================== TAKE SERIAL =====================
    // Step by step: department -> doctor -> date (calendar) -> chamber -> free slot -> problem

    [HttpGet]
    public IActionResult TakeSerial(string? departmentId, string? doctorId, string? date, string? chamberId)
    {
        string today = Clock.Today();
        string lastDay = Clock.DateText(Clock.Now().Date.AddDays(BookingService.DaysAhead - 1));

        ViewBag.Departments = _db.AllDepartments();
        ViewBag.DepartmentId = departmentId;
        ViewBag.DoctorId = doctorId;
        ViewBag.MinDate = today;      // calendar starts today
        ViewBag.MaxDate = lastDay;    // and ends 7 days later

        // step 2: doctors of the chosen department
        List<User> doctors = new List<User>();
        if (!string.IsNullOrEmpty(departmentId))
        {
            doctors = _db.AllDoctors().Where(d => d.DepartmentId == departmentId).ToList();
        }
        ViewBag.Doctors = doctors;

        User? doctor = doctors.FirstOrDefault(d => d.Id == doctorId);
        ViewBag.SelectedDoctor = doctor;

        // step 3: the date from the calendar (only today .. 7 days ahead)
        bool dateOk = Clock.IsDate(date)
                      && string.Compare(date, today) >= 0
                      && string.Compare(date, lastDay) <= 0;
        ViewBag.Date = dateOk ? date : null;

        // step 4: the doctor's chambers on that date
        List<Chamber> dayChambers = new List<Chamber>();
        if (doctor != null && dateOk)
        {
            DateTime day = Clock.ParseDate(date!);
            foreach (Chamber c in _db.ChambersOf(doctor.Id!))
            {
                if (c.RunsOn(day)) dayChambers.Add(c);
            }
        }
        ViewBag.DayChambers = dayChambers;

        // chosen chamber, or the first one of that day
        Chamber? chamber = dayChambers.FirstOrDefault(c => c.Id == chamberId);
        if (chamber == null) chamber = dayChambers.FirstOrDefault();
        ViewBag.Chamber = chamber;

        // step 5: free slots of that chamber on that date
        List<string> freeSlots = new List<string>();
        if (chamber != null)
        {
            freeSlots = _booking.FreeSlots(chamber, date!);
        }
        ViewBag.FreeSlots = freeSlots;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult TakeSerial(string departmentId, string doctorId, string date, string chamberId, string slot, string problem)
    {
        if (!Clock.IsDate(date))
        {
            TempData["Error"] = "Please pick a date.";
            return RedirectToAction("TakeSerial", new { departmentId, doctorId });
        }

        Result result = _booking.Book(MyId(), doctorId, chamberId, date, slot ?? "", problem ?? "");

        if (!result.Ok)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction("TakeSerial", new { departmentId, doctorId, date, chamberId });
        }

        TempData["Success"] = result.Message;
        return RedirectToAction("MySerial");
    }

    // ===================== MY SERIAL (live) =====================

    // /Patient/MySerial or /Patient/MySerial/<serialId>
    public IActionResult MySerial(string? id)
    {
        List<Serial> upcoming = MyUpcomingSerials();
        ViewBag.Upcoming = upcoming;

        // which serial to show
        Serial? serial = upcoming.FirstOrDefault(s => s.Id == id);
        if (serial == null) serial = upcoming.FirstOrDefault();

        if (serial == null)
        {
            return View();   // page shows "No upcoming serial"
        }

        Chamber? chamber = _db.GetChamber(serial.ChamberId);
        ViewBag.Chamber = chamber;
        ViewBag.Doctor = _db.GetUser(serial.DoctorId);

        // find my row in the live queue
        QueueRow? myRow = null;
        int? nowServing = null;

        if (chamber != null)
        {
            List<QueueRow> rows = _queue.BuildQueue(chamber, serial.Date);
            myRow = rows.FirstOrDefault(r => r.Serial.Id == serial.Id);

            QueueRow? inRoom = rows.FirstOrDefault(r => r.Serial.Status == SerialStatus.InRoom);
            if (inRoom != null) nowServing = inRoom.Serial.SerialNo;
        }

        ViewBag.MyRow = myRow;
        ViewBag.NowServing = nowServing;
        return View(serial);
    }

    // cancel a serial that has not started yet
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(string id)
    {
        Serial? serial = _db.GetSerial(id);

        if (serial == null || serial.PatientId != MyId())
        {
            TempData["Error"] = "Serial not found.";
        }
        else if (serial.Status != SerialStatus.Waiting)
        {
            TempData["Error"] = "This serial can't be cancelled now.";
        }
        else
        {
            _db.Serials.DeleteOne(s => s.Id == serial.Id);
            TempData["Success"] = "Serial #" + serial.SerialNo + " cancelled.";
        }

        return RedirectToAction("MySerial");
    }

    // ===================== PRESCRIPTIONS =====================

    public IActionResult Prescriptions()
    {
        string myId = MyId();
        List<Prescription> list = _db.Prescriptions.Find(p => p.PatientId == myId).ToList();
        list = list.OrderByDescending(p => p.CreatedAt).ToList();
        return View(list);
    }

    // view and print one prescription
    public IActionResult PrescriptionView(string id)
    {
        Prescription? prescription = _db.GetPrescription(id);

        // a patient can only see his own prescriptions
        if (prescription == null || prescription.PatientId != MyId())
        {
            TempData["Error"] = "Prescription not found.";
            return RedirectToAction("Prescriptions");
        }

        return View(prescription);
    }

    // ===================== REPORT CHECKER =====================

    [HttpGet]
    public IActionResult ReportChecker()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(12 * 1024 * 1024)]   // max 12 MB for the whole form
    public async Task<IActionResult> ReportChecker(string? reportText, IFormFile? reportFile)
    {
        ViewBag.ReportText = reportText;

        // ---------- A) a file was uploaded -> the AI reads it ----------
        if (reportFile != null && reportFile.Length > 0)
        {
            ViewBag.FileName = reportFile.FileName;

            if (!_gemini.IsSetUp())
            {
                TempData["Error"] = "The AI checker is not set up yet (Gemini API key missing). Type the values instead.";
                return View();
            }

            // only PDF and photos, max 10 MB
            string extension = Path.GetExtension(reportFile.FileName).ToLower();
            string mimeType;
            if (extension == ".pdf") mimeType = "application/pdf";
            else if (extension == ".jpg" || extension == ".jpeg") mimeType = "image/jpeg";
            else if (extension == ".png") mimeType = "image/png";
            else if (extension == ".webp") mimeType = "image/webp";
            else
            {
                TempData["Error"] = "Please upload a PDF, JPG, PNG or WEBP file.";
                return View();
            }
            if (reportFile.Length > 10 * 1024 * 1024)
            {
                TempData["Error"] = "The file must be smaller than 10 MB.";
                return View();
            }

            // read the file into memory (we don't save it anywhere)
            byte[] bytes;
            using (MemoryStream memory = new MemoryStream())
            {
                await reportFile.CopyToAsync(memory);
                bytes = memory.ToArray();
            }

            // step 1 + 2: AI reads the values, our code flags High / Low
            List<ReportFinding>? findings = await _gemini.ReadReport(bytes, mimeType);
            if (findings == null)
            {
                string message = "The AI could not read this file right now. Please try again, or type the values below.";
                // while testing on our own PC, also show the real error
                if (_env.IsDevelopment()) message = message + " (" + _gemini.LastError + ")";
                TempData["Error"] = message;
                return View();
            }
            if (findings.Count == 0)
            {
                TempData["Error"] = "No test values were found. Is this a lab report? You can also type the values below.";
                return View();
            }

            ReportResult result = new ReportResult();
            result.Findings = findings;
            int notNormal = findings.Count(f => f.Level == "High" || f.Level == "Low");
            result.Summary = notNormal == 0
                ? "All " + findings.Count + " values the AI found are within the normal range."
                : notNormal + " of " + findings.Count + " values are outside the normal range. Show this report to your doctor.";

            ViewBag.Result = result;
            ViewBag.ReadByAi = true;
            ViewBag.Critical = findings.Any(f => f.IsCritical);

            // step 3: a short explanation in simple words
            string? explanation = await _gemini.Explain(findings);
            ViewBag.Explanation = explanation;

            // save it in "My Reports" (values only, not the file)
            ViewBag.SavedId = SaveReportCheck(result, true, reportFile.FileName, explanation ?? "");
            return View();
        }

        // ---------- B) no file -> check the typed values (no AI) ----------
        if (string.IsNullOrWhiteSpace(reportText))
        {
            TempData["Error"] = "Upload your report file, or type the test values.";
            return View();
        }

        ReportResult typedResult = Services.ReportChecker.Check(reportText);
        ViewBag.Result = typedResult;

        // save it too (only if at least one value was understood)
        if (typedResult.Findings.Count > 0)
        {
            ViewBag.SavedId = SaveReportCheck(typedResult, false, "", "");
        }
        return View();
    }

    // saves one checked report in the database and returns its Id
    private string? SaveReportCheck(ReportResult result, bool readByAi, string fileName, string explanation)
    {
        ReportCheck check = new ReportCheck();
        check.PatientId = MyId();
        check.CreatedAt = Clock.NowText();
        check.ReadByAi = readByAi;
        check.FileName = fileName;
        check.Summary = result.Summary;
        check.Explanation = explanation;
        check.Findings = result.Findings;
        check.NotRecognised = result.NotRecognised;
        check.IsCritical = result.Findings.Any(f => f.IsCritical);

        _db.ReportChecks.InsertOne(check);
        return check.Id;
    }

    // ===================== MY REPORTS (history) =====================

    // list of all my saved report checks, newest first
    public IActionResult MyReports()
    {
        string myId = MyId();
        List<ReportCheck> list = _db.ReportChecks.Find(r => r.PatientId == myId).ToList();
        list = list.OrderByDescending(r => r.CreatedAt).ToList();
        return View(list);
    }

    // one saved report check, with a Print button
    public IActionResult ReportView(string id)
    {
        ReportCheck? check = _db.GetReportCheck(id);

        // a patient can only see his own reports
        if (check == null || check.PatientId != MyId())
        {
            TempData["Error"] = "Report not found.";
            return RedirectToAction("MyReports");
        }

        ViewBag.Patient = _db.GetUser(MyId());
        return View(check);
    }

    // delete one saved report check (the patient's right to delete his data)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteReport(string id)
    {
        ReportCheck? check = _db.GetReportCheck(id);

        if (check == null || check.PatientId != MyId())
        {
            TempData["Error"] = "Report not found.";
        }
        else
        {
            _db.ReportChecks.DeleteOne(r => r.Id == check.Id);
            TempData["Success"] = "Report deleted.";
        }

        return RedirectToAction("MyReports");
    }
}
