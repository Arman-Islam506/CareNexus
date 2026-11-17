using System.Security.Claims;
using CareNexus.Models;
using CareNexus.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CareNexus.Controllers;

// "My Profile" page — every logged-in user can see and edit his own profile
[Authorize]
public class ProfileController : Controller
{
    private readonly Db _db;
    private readonly IWebHostEnvironment _env;   // tells us where the wwwroot folder is

    public ProfileController(Db db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    private User? Me()
    {
        string myId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        return _db.GetUser(myId);
    }

    public IActionResult Index()
    {
        User? me = Me();
        if (me == null) return RedirectToAction("Login", "Account");
        return View(me);
    }

    [HttpGet]
    public IActionResult Edit()
    {
        User? me = Me();
        if (me == null) return RedirectToAction("Login", "Account");
        return View(me);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(5 * 1024 * 1024)]   // max 5 MB for the whole form
    public IActionResult Edit(string name, string? phone, int? age, string? gender, string? bloodGroup, string? city, string? address,
                              IFormFile? photo, bool removePhoto)
    {
        User? me = Me();
        if (me == null) return RedirectToAction("Login", "Account");

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction("Edit");
        }

        // change only the fields from the form, keep everything else
        me.Name = name.Trim();
        me.Phone = (phone ?? "").Trim();
        me.Age = age;
        me.Gender = gender ?? "";
        me.BloodGroup = bloodGroup ?? "";
        me.City = (city ?? "").Trim();
        me.Address = (address ?? "").Trim();

        // remove the old photo if the box was ticked
        if (removePhoto)
        {
            DeleteOldPhoto(me.PhotoUrl);
            me.PhotoUrl = "";
        }

        // save a new photo if one was chosen
        if (photo != null && photo.Length > 0)
        {
            string extension = Path.GetExtension(photo.FileName).ToLower();
            string[] allowed = { ".jpg", ".jpeg", ".png", ".webp" };

            if (!allowed.Contains(extension))
            {
                TempData["Error"] = "Photo must be a JPG, PNG or WEBP image.";
                return RedirectToAction("Edit");
            }
            if (photo.Length > 2 * 1024 * 1024)
            {
                TempData["Error"] = "Photo must be smaller than 2 MB.";
                return RedirectToAction("Edit");
            }

            // folder: wwwroot/uploads/avatars  (made if missing)
            string folder = Path.Combine(_env.WebRootPath, "uploads", "avatars");
            Directory.CreateDirectory(folder);

            // a new random file name, so the browser never shows an old cached photo
            string fileName = me.Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + extension;
            string fullPath = Path.Combine(folder, fileName);

            using (FileStream stream = new FileStream(fullPath, FileMode.Create))
            {
                photo.CopyTo(stream);
            }

            DeleteOldPhoto(me.PhotoUrl);
            me.PhotoUrl = "/uploads/avatars/" + fileName;
        }

        _db.Users.ReplaceOne(u => u.Id == me.Id, me);

        TempData["Success"] = "Profile saved.";
        return RedirectToAction("Index");
    }

    // deletes an old photo file from wwwroot/uploads/avatars
    private void DeleteOldPhoto(string photoUrl)
    {
        if (!photoUrl.StartsWith("/uploads/avatars/")) return;   // only our own photo folder

        string fileName = Path.GetFileName(photoUrl);
        string fullPath = Path.Combine(_env.WebRootPath, "uploads", "avatars", fileName);

        if (System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
    }
}
