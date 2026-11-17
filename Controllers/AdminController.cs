using CareNexus.Models;
using CareNexus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CareNexus.Controllers;

// Only logged-in admins can open these pages
[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly Db _db;
    private readonly QueueService _queue;

    public AdminController(Db db, QueueService queue)
    {
        _db = db;
        _queue = queue;
    }

    // ===================== DASHBOARD =====================

    public IActionResult Index()
    {
        string today = Clock.Today();

        // numbers for the boxes at the top
        ViewBag.Patients = _db.Users.CountDocuments(u => u.Role == Roles.Patient);
        ViewBag.Doctors = _db.Users.CountDocuments(u => u.Role == Roles.Doctor);
        ViewBag.Assistants = _db.Users.CountDocuments(u => u.Role == Roles.Assistant);
        ViewBag.SerialsToday = _db.Serials.CountDocuments(s => s.Date == today);

        // total money today (all doctors)
        int collection = 0;
        foreach (User doctor in _db.AllDoctors())
        {
            collection = collection + _queue.CollectionFor(doctor.Id!, today);
        }
        ViewBag.CollectionToday = collection;

        return View(_db.AllDepartments());
    }

    // ===================== USERS =====================

    // /Admin/Users or /Admin/Users?role=Doctor
    public IActionResult Users(string? role)
    {
        List<User> users = _db.Users.Find(u => true).ToList();

        if (!string.IsNullOrEmpty(role))
        {
            users = users.Where(u => u.Role == role).ToList();
        }

        ViewBag.Role = role;
        return View(users.OrderBy(u => u.Role).ThenBy(u => u.Name).ToList());
    }

    [HttpGet]
    public IActionResult AddUser()
    {
        ViewBag.Departments = _db.AllDepartments();
        ViewBag.Doctors = _db.AllDoctors();
        return View();
    }

    // adds a doctor or an assistant
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddUser(string role, string name, string email, string? phone, string? password,
                                 string? departmentId, string? degree, int experienceYears, int fee, string? doctorId)
    {
        // simple checks
        if (role != Roles.Doctor && role != Roles.Assistant)
        {
            TempData["Error"] = "Role must be Doctor or Assistant.";
            return RedirectToAction("AddUser");
        }
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "Name and email are required.";
            return RedirectToAction("AddUser");
        }

        string cleanEmail = email.Trim().ToLower();
        if (_db.Users.CountDocuments(u => u.Email == cleanEmail) > 0)
        {
            TempData["Error"] = "This email is already used.";
            return RedirectToAction("AddUser");
        }

        if (role == Roles.Doctor && string.IsNullOrEmpty(departmentId))
        {
            TempData["Error"] = "Please choose a department for the doctor.";
            return RedirectToAction("AddUser");
        }
        if (role == Roles.Assistant && string.IsNullOrEmpty(doctorId))
        {
            TempData["Error"] = "Please choose which doctor the assistant works for.";
            return RedirectToAction("AddUser");
        }

        // password: 4 digits (empty -> "1234")
        string realPassword = string.IsNullOrWhiteSpace(password) ? "1234" : password.Trim();
        if (realPassword.Length != 4 || !realPassword.All(char.IsDigit))
        {
            TempData["Error"] = "Password must be exactly 4 digits.";
            return RedirectToAction("AddUser");
        }

        User user = new User();
        user.LoginId = _db.NextLoginId(role);   // doctor 2xx, assistant 3xx
        user.Role = role;
        user.Name = name.Trim();
        user.Email = cleanEmail;
        user.Phone = (phone ?? "").Trim();

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(realPassword, 12);

        if (role == Roles.Doctor)
        {
            user.DepartmentId = departmentId ?? "";
            user.Degree = (degree ?? "").Trim();
            user.ExperienceYears = experienceYears;
            user.Fee = fee;
        }
        else
        {
            user.DoctorId = doctorId ?? "";
        }

        _db.Users.InsertOne(user);

        TempData["Success"] = role + " " + user.Name + " added. Login ID: " + user.LoginId + ".";
        return RedirectToAction("Users", new { role });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteUser(string id)
    {
        User? user = _db.GetUser(id);

        if (user == null || user.Role == Roles.Admin)
        {
            TempData["Error"] = "This user can't be deleted.";
        }
        else
        {
            _db.Users.DeleteOne(u => u.Id == user.Id);
            if (user.Role == Roles.Doctor)
            {
                _db.Chambers.DeleteMany(c => c.DoctorId == user.Id);   // his chambers go too
            }
            TempData["Success"] = user.Name + " deleted.";
        }
        return RedirectToAction("Users");
    }

    // ===================== DEPARTMENTS =====================

    public IActionResult Departments()
    {
        return View(_db.AllDepartments());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddDepartment(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Please type a department name.";
            return RedirectToAction("Departments");
        }

        string cleanName = name.Trim();
        if (_db.Departments.CountDocuments(d => d.Name == cleanName) > 0)
        {
            TempData["Error"] = "This department already exists.";
            return RedirectToAction("Departments");
        }

        _db.Departments.InsertOne(new Department { Name = cleanName });
        TempData["Success"] = "Department " + cleanName + " added.";
        return RedirectToAction("Departments");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteDepartment(string id)
    {
        bool hasDoctors = _db.Users.CountDocuments(u => u.Role == Roles.Doctor && u.DepartmentId == id) > 0;
        if (hasDoctors)
        {
            TempData["Error"] = "This department still has doctors. Move or delete them first.";
        }
        else if (Db.IsValidId(id))
        {
            _db.Departments.DeleteOne(d => d.Id == id);
            TempData["Success"] = "Department deleted.";
        }
        return RedirectToAction("Departments");
    }

    // ===================== CHAMBER SCHEDULES =====================

    public IActionResult Schedules()
    {
        List<Chamber> chambers = _db.Chambers.Find(c => true).ToList();
        ViewBag.Doctors = _db.AllDoctors();
        return View(chambers.OrderBy(c => c.DoctorId).ThenBy(c => c.StartTime).ToList());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddChamber(string doctorId, string? room, List<string> days, string startTime, string endTime)
    {
        if (_db.GetUser(doctorId) == null)
        {
            TempData["Error"] = "Please choose a doctor.";
            return RedirectToAction("Schedules");
        }
        if (days == null || days.Count == 0)
        {
            TempData["Error"] = "Please tick at least one day.";
            return RedirectToAction("Schedules");
        }
        if (string.IsNullOrEmpty(startTime) || string.IsNullOrEmpty(endTime) || string.Compare(startTime, endTime) >= 0)
        {
            TempData["Error"] = "End time must be after start time.";
            return RedirectToAction("Schedules");
        }

        Chamber chamber = new Chamber();
        chamber.DoctorId = doctorId;
        chamber.Room = (room ?? "").Trim();
        chamber.Days = days;
        chamber.StartTime = startTime;   // the time box sends "HH:mm"
        chamber.EndTime = endTime;

        _db.Chambers.InsertOne(chamber);
        TempData["Success"] = "Chamber added for " + _db.UserName(doctorId) + ".";
        return RedirectToAction("Schedules");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteChamber(string id)
    {
        if (Db.IsValidId(id))
        {
            _db.Chambers.DeleteOne(c => c.Id == id);
            TempData["Success"] = "Chamber deleted.";
        }
        return RedirectToAction("Schedules");
    }
}
