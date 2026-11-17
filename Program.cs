using CareNexus.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// ---------- PART 1: add the tools the app needs ----------

// MVC (controllers + views)
builder.Services.AddControllersWithViews();

// MongoDB: one connection for the whole app (reads from User Secrets)
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    MongoClientSettings settings = MongoClientSettings.FromConnectionString(builder.Configuration["MongoDb:ConnectionString"]);

    // secure connection: allow TLS 1.2 and 1.3 (fixes "The Local Security Authority cannot be contacted" on some PCs)
    settings.SslSettings = new SslSettings
    {
        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
        CheckCertificateRevocation = false
    };

    // give up after 10 seconds instead of 30 if the database can't be reached
    settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);

    return new MongoClient(settings);
});

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>()
      .GetDatabase(builder.Configuration["MongoDb:DatabaseName"]));

// our own classes (one copy shared by the whole app)
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<QueueService>();
builder.Services.AddSingleton<BookingService>();
builder.Services.AddSingleton<EmailService>();

// Login with cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";               // not logged in -> go here
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied"; // wrong role -> go here
        options.ExpireTimeSpan = TimeSpan.FromDays(7);     // stay logged in for 8 hours
        options.SlidingExpiration = true;
        options.Cookie.Name = "CareNexus.Auth";
        options.Cookie.HttpOnly = true;                     // JavaScript can't read the cookie
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // only sent over https
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// the hospital's name, address and phone (from appsettings.json)
HospitalInfo.Load(app.Configuration);

// Email index + starting data (departments, doctors, chambers, assistants)
try
{
    DbSetup.Run(app.Services, app.Configuration);
}
catch (TimeoutException)
{
    // the database could not be reached: show a clear message instead of crashing
    Console.WriteLine("");
    Console.WriteLine("**************************************************************");
    Console.WriteLine(" CANNOT REACH THE DATABASE (MongoDB Atlas).");
    Console.WriteLine(" 1) Check your internet.");
    Console.WriteLine(" 2) Atlas -> Security -> Network Access: add your current IP.");
    Console.WriteLine(" 3) Then stop and run the app again.");
    Console.WriteLine("**************************************************************");
    Console.WriteLine("");
}

// ---------- PART 2: rules for every request ----------

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// if a page can't reach the database, show a friendly message instead of an error page
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (TimeoutException)
    {
        context.Response.StatusCode = 503;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            "<div style='font-family:Segoe UI,Arial;max-width:560px;margin:80px auto;text-align:center'>" +
            "<h2 style='color:#0c9791'>Cannot reach the database</h2>" +
            "<p>Please check your internet connection.<br/>If you are the developer: add your current IP in " +
            "MongoDB Atlas → Security → Network Access, then refresh this page.</p></div>");
    }
});

app.UseHttpsRedirection();
app.UseStaticFiles();      // serves files added while the app runs (uploaded photos)
app.UseRouting();
app.UseAuthentication();   // who are you? (reads the login cookie)
app.UseAuthorization();    // are you allowed? (checks the role)

app.MapStaticAssets();

// ---------- PART 3: routes and start ----------

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();