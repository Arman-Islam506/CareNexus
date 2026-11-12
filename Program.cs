using MongoDB.Bson;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// MVC (controllers + views)
builder.Services.AddControllersWithViews();

// MongoDB: one connection for the whole app (reads from User Secrets)
builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(builder.Configuration["MongoDb:ConnectionString"]));

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>()
      .GetDatabase(builder.Configuration["MongoDb:DatabaseName"]));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Test page: checks the database connection
app.MapGet("/db-test", async (IMongoDatabase db) =>
{
    await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
    return "MongoDB connected ✅";
});

app.Run();