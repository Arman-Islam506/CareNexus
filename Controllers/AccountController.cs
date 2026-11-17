using System.Security.Claims;
using System.Security.Cryptography;
using CareNexus.Models;
using CareNexus.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CareNexus.Controllers;

// Register (with email code), login and logout
public class AccountController : Controller
{
    private readonly Db _db;
    private readonly EmailService _email;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AccountController> _logger;

    public AccountController(Db db, EmailService email, IWebHostEnvironment env, ILogger<AccountController> logger)
    {
        _db = db;
        _email = email;
        _env = env;
        _logger = logger;
    }

    // ---------- REGISTER ----------

    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Register(RegisterViewModel model)
    {
        // 1. check the form rules (required, email format ...)
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        string email = model.Email.Trim().ToLower();

        // 2. check if the email is already used
        User? oldUser = _db.Users.Find(u => u.Email == email).FirstOrDefault();
        if (oldUser != null)
        {
            if (oldUser.IsVerified)
                ModelState.AddModelError("Email", "This email is already registered. Please log in.");
            else
                ModelState.AddModelError("Email", "This email is registered but not verified yet. Log in to get a new code.");
            return View(model);
        }

        // 3. make the new user (only patients can sign up themselves)
        User user = new User();
        user.LoginId = _db.NextLoginId(Roles.Patient);   // e.g. "104"
        user.Name = model.Name.Trim();
        user.Email = email;
        user.Phone = model.Phone.Trim();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password, 12);
        user.Role = Roles.Patient;
        user.IsVerified = false;   // becomes true after the email code is typed

        // 4. save it in MongoDB
        _db.Users.InsertOne(user);

        // 5. send the 6-digit code and go to the "enter code" page
        SendCode(user);
        return RedirectToAction("VerifyEmail", new { email = user.Email });
    }

    // ---------- EMAIL CODE (OTP) ----------

    [HttpGet]
    public IActionResult VerifyEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return RedirectToAction("Login");
        ViewBag.Email = email;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult VerifyEmail(string email, string code)
    {
        ViewBag.Email = email;
        string cleanEmail = (email ?? "").Trim().ToLower();
        User? user = _db.Users.Find(u => u.Email == cleanEmail).FirstOrDefault();

        if (user == null)
        {
            TempData["Error"] = "Account not found.";
            return RedirectToAction("Register");
        }
        if (user.IsVerified)
        {
            TempData["Success"] = "Your email is already verified. Please log in.";
            return RedirectToAction("Login");
        }

        // the code must exist and not be older than 10 minutes
        if (user.OtpHash == null || user.OtpExpiresAt == null || string.Compare(Clock.NowText(), user.OtpExpiresAt) > 0)
        {
            TempData["Error"] = "This code has expired. Click \"Send a new code\".";
            return View();
        }

        // max 5 wrong tries
        if (user.OtpAttempts >= 5)
        {
            TempData["Error"] = "Too many wrong tries. Click \"Send a new code\".";
            return View();
        }

        // wrong code
        if (string.IsNullOrWhiteSpace(code) || !BCrypt.Net.BCrypt.Verify(code.Trim(), user.OtpHash))
        {
            user.OtpAttempts = user.OtpAttempts + 1;
            _db.Users.ReplaceOne(u => u.Id == user.Id, user);
            TempData["Error"] = "Wrong code. Please try again.";
            return View();
        }

        // correct code: verified, remove the code
        user.IsVerified = true;
        user.OtpHash = null;
        user.OtpExpiresAt = null;
        user.OtpAttempts = 0;
        _db.Users.ReplaceOne(u => u.Id == user.Id, user);

        TempData["Success"] = "Email verified ✅ Your patient ID is " + user.LoginId + ". Log in with this ID (or your email) and your 4-digit password.";
        return RedirectToAction("Login");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ResendCode(string email)
    {
        string cleanEmail = (email ?? "").Trim().ToLower();
        User? user = _db.Users.Find(u => u.Email == cleanEmail).FirstOrDefault();

        if (user != null && !user.IsVerified)
        {
            // wait 1 minute between codes
            bool tooSoon = user.OtpSentAt != null
                           && Clock.ParseDateTime(user.OtpSentAt).AddMinutes(1) > Clock.Now();
            if (tooSoon)
                TempData["Error"] = "Please wait 1 minute before asking for a new code.";
            else
                SendCode(user);
        }
        return RedirectToAction("VerifyEmail", new { email = cleanEmail });
    }

    // makes a new 6-digit code, saves it (scrambled) and emails it
    private void SendCode(User user)
    {
        string code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");   // e.g. "048213"

        user.OtpHash = BCrypt.Net.BCrypt.HashPassword(code, 10);
        user.OtpExpiresAt = Clock.Now().AddMinutes(10).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        user.OtpSentAt = Clock.NowText();
        user.OtpAttempts = 0;
        _db.Users.ReplaceOne(u => u.Id == user.Id, user);

        string html =
            "<h2>" + HospitalInfo.Name + " — email verification</h2>" +
            "<p>Hi " + System.Net.WebUtility.HtmlEncode(user.Name) + ",</p>" +
            "<p>Your verification code is:</p>" +
            "<h1 style='letter-spacing:6px'>" + code + "</h1>" +
            "<p>It works for 10 minutes. If you did not create an account, ignore this email.</p>";

        bool sent = _email.Send(user.Email, "Your CareNexus verification code", html);

        if (sent)
        {
            TempData["Success"] = "We sent a 6-digit code to " + user.Email + ".";
        }
        else
        {
            TempData["Error"] = "We could not send the email. Please try \"Send a new code\" again.";

            // ONLY on your own PC: show the code in the Visual Studio Output window, for testing
            if (_env.IsDevelopment())
            {
                _logger.LogWarning("DEV ONLY - verification code for {Email}: {Code}", user.Email, code);
            }
        }
    }

    // ---------- LOGIN ----------

    [HttpGet]
    public IActionResult Login()
    {
        // already logged in? go to the dashboard
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            return GoToDashboard(User.FindFirstValue(ClaimTypes.Role));
        }
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // only allow our 4 roles
        if (model.Role != Roles.Patient && model.Role != Roles.Doctor &&
            model.Role != Roles.Assistant && model.Role != Roles.Admin)
        {
            ModelState.AddModelError("", "Please pick a role.");
            return View(model);
        }

        // find the account: has "@" -> it's an email, else it's a login ID like "101"
        string login = model.Login.Trim().ToLower();
        User? user;
        if (login.Contains("@"))
        {
            user = _db.Users.Find(u => u.Email == login).FirstOrDefault();
        }
        else
        {
            user = _db.Users.Find(u => u.LoginId == login).FirstOrDefault();
        }

        // wrong ID/email OR wrong password -> the same message (safer: attackers can't guess accounts)
        bool passwordOk = user != null
                          && user.PasswordHash != ""
                          && BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash);
        if (user == null || !passwordOk)
        {
            ModelState.AddModelError("", "Wrong ID / email or password.");
            return View(model);
        }

        // the role button must match the account's role
        if (user.Role != model.Role)
        {
            ModelState.AddModelError("", "This account is not a " + model.Role + " account. Pick the correct role.");
            return View(model);
        }

        // email not verified yet -> send a new code
        if (!user.IsVerified)
        {
            SendCode(user);
            return RedirectToAction("VerifyEmail", new { email = user.Email });
        }

        // save who the user is inside the login cookie
        List<Claim> claims = new List<Claim>();
        claims.Add(new Claim(ClaimTypes.NameIdentifier, user.Id!));
        claims.Add(new Claim(ClaimTypes.Name, user.Name));
        claims.Add(new Claim(ClaimTypes.Email, user.Email));
        claims.Add(new Claim(ClaimTypes.Role, user.Role));

        ClaimsIdentity identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                                      new ClaimsPrincipal(identity));

        return GoToDashboard(user.Role);
    }

    // ---------- LOGOUT ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    // sends each role to its own dashboard
    private IActionResult GoToDashboard(string? role)
    {
        if (role == Roles.Admin) return RedirectToAction("Index", "Admin");
        if (role == Roles.Doctor) return RedirectToAction("Index", "Doctor");
        if (role == Roles.Assistant) return RedirectToAction("Index", "Assistant");
        return RedirectToAction("Index", "Patient");
    }
}
