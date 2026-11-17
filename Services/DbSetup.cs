using CareNexus.Models;
using MongoDB.Driver;

namespace CareNexus.Services;

// Runs once when the app starts. It never makes duplicates (it checks first).
//
// Login IDs:  Patients 101, 102 ...   Doctors 201, 202 ...
//             Assistants 301, 302 ... Admins 901, 902 ...
// Demo password for every demo account: 1234
public static class DbSetup
{
    public static void Run(IServiceProvider services, IConfiguration config)
    {
        Db db = services.GetRequiredService<Db>();

        // 1) two accounts can't use the same email
        db.Users.Indexes.CreateOne(new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Email),
            new CreateIndexOptions { Unique = true }));

        // 2) starting data (departments, doctors, chambers, assistants) when the database is empty
        if (db.Departments.CountDocuments(d => true) == 0)
        {
            AddStartingData(db);
        }

        // 3) demo admin + demo patients
        AddUserIfMissing(db, "901", "Hospital Admin", "admin901@carenexus.com", "01900000001", Roles.Admin);
        AddUserIfMissing(db, "101", "Arman Islam", "patient101@carenexus.com", "01711111111", Roles.Patient);
        AddUserIfMissing(db, "102", "Sadia Akter", "patient102@carenexus.com", "01722222222", Roles.Patient);
        AddUserIfMissing(db, "103", "Rafiq Hasan", "patient103@carenexus.com", "01733333333", Roles.Patient);

        // 4) old accounts without a password get "1234"
        string demoHash = BCrypt.Net.BCrypt.HashPassword("1234", 12);
        db.Users.UpdateMany(u => u.PasswordHash == "" || u.PasswordHash == null,
                            Builders<User>.Update.Set(u => u.PasswordHash, demoHash));

        // 5) old accounts without a login ID get the next free one (e.g. 104, 206 ...)
        GiveMissingLoginIds(db);

        // 6) two accounts can't use the same login ID
        db.Users.Indexes.CreateOne(new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.LoginId),
            new CreateIndexOptions { Unique = true }));

        // 7) sample past visits, only when there are no serials yet
        if (db.Serials.CountDocuments(s => true) == 0)
        {
            AddSampleVisits(db);
        }

        // 8) admin account from User Secrets (optional)
        string? adminEmail = config["Admin:Email"];
        string? adminPassword = config["Admin:Password"];
        if (!string.IsNullOrEmpty(adminEmail) && !string.IsNullOrEmpty(adminPassword))
        {
            adminEmail = adminEmail.Trim().ToLower();
            bool exists = db.Users.CountDocuments(u => u.Email == adminEmail) > 0;
            if (!exists)
            {
                User admin = new User();
                admin.LoginId = db.NextLoginId(Roles.Admin);
                admin.Name = "Admin";
                admin.Email = adminEmail;
                admin.Role = Roles.Admin;
                admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, 12);
                db.Users.InsertOne(admin);
            }
        }
    }

    // adds a demo account if its login ID and email are both new (password: 1234)
    private static void AddUserIfMissing(Db db, string loginId, string name, string email, string phone, string role)
    {
        bool exists = db.Users.CountDocuments(u => u.LoginId == loginId || u.Email == email) > 0;
        if (exists) return;

        User user = new User();
        user.LoginId = loginId;
        user.Name = name;
        user.Email = email;
        user.Phone = phone;
        user.Role = role;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("1234", 12);

        if (role == Roles.Patient)
        {
            user.Age = 25;
            user.Gender = name.StartsWith("Sadia") ? "Female" : "Male";
            user.BloodGroup = "O+";
            user.City = "Dhaka";
        }
        db.Users.InsertOne(user);
    }

    // every account without a login ID gets the next free one for its role (oldest first)
    private static void GiveMissingLoginIds(Db db)
    {
        List<User> noId = db.Users.Find(u => u.LoginId == "" || u.LoginId == null).ToList();
        noId = noId.OrderBy(u => u.CreatedAt).ToList();

        foreach (User user in noId)
        {
            string newId = db.NextLoginId(user.Role);
            db.Users.UpdateOne(u => u.Id == user.Id, Builders<User>.Update.Set(u => u.LoginId, newId));
        }
    }

    // a few finished visits in the past (with fee + prescription) for the demo patients
    private static void AddSampleVisits(Db db)
    {
        AddPastVisit(db, "101", "201", 3, "Chest pain while walking",
                     "Stable angina", "Tab. Ecosprin 75 mg — 0+1+0, after meal, 30 days\nTab. Nitrocontin 2.6 mg — 1+0+1, 30 days", "Avoid heavy work. Do ECG and lipid profile.");
        AddPastVisit(db, "101", "202", 1, "Fever for 3 days",
                     "Viral fever", "Tab. Napa 500 mg — 1+1+1, after meal, 5 days\nSyp. Histacin — 2 tsp at night, 5 days", "Rest and drink plenty of water.");
        AddPastVisit(db, "102", "205", 2, "Skin rash on arms",
                     "Allergic dermatitis", "Tab. Fexo 120 mg — 0+0+1, 7 days\nOint. Betameson — apply twice daily, 7 days", "Avoid new soap or detergent.");
        AddPastVisit(db, "103", "204", 4, "Knee pain",
                     "Osteoarthritis (knee)", "Tab. Naprosyn 500 mg — 1+0+1, after meal, 7 days\nCap. Omep 20 mg — 1+0+1, before meal, 7 days", "Light exercise. Do knee X-ray.");
    }

    // one finished visit, some days ago, with a fee and a prescription
    private static void AddPastVisit(Db db, string patientLoginId, string doctorLoginId, int daysAgo,
                                     string problem, string diagnosis, string medicines, string advice)
    {
        User? patient = db.Users.Find(u => u.LoginId == patientLoginId).FirstOrDefault();
        User? doctor = db.Users.Find(u => u.LoginId == doctorLoginId).FirstOrDefault();
        if (patient == null || doctor == null) return;

        string doctorId = doctor.Id!;
        Chamber? chamber = db.Chambers.Find(c => c.DoctorId == doctorId).FirstOrDefault();
        if (chamber == null) return;

        string date = Clock.DateText(Clock.Now().Date.AddDays(-daysAgo));
        DateTime start = Clock.Combine(date, chamber.StartTime);
        string format = "yyyy-MM-dd HH:mm:ss";
        System.Globalization.CultureInfo english = System.Globalization.CultureInfo.InvariantCulture;

        Serial serial = new Serial();
        serial.PatientId = patient.Id!;
        serial.DoctorId = doctorId;
        serial.ChamberId = chamber.Id!;
        serial.Date = date;
        serial.SerialNo = 1;
        serial.QueueOrder = 1;
        serial.SlotTime = chamber.StartTime;
        serial.Problem = problem;
        serial.Status = SerialStatus.Done;
        serial.StartedAt = start.ToString(format, english);
        serial.EndedAt = start.AddMinutes(12).ToString(format, english);
        serial.Fee = doctor.Fee;
        db.Serials.InsertOne(serial);

        Prescription prescription = new Prescription();
        prescription.SerialId = serial.Id!;
        prescription.PatientId = patient.Id!;
        prescription.DoctorId = doctorId;
        prescription.CreatedAt = start.AddMinutes(10).ToString(format, english);
        prescription.Diagnosis = diagnosis;
        prescription.Medicines = medicines;
        prescription.Advice = advice;
        prescription.FollowUp = "After 2 weeks";
        db.Prescriptions.InsertOne(prescription);
    }

    private static void AddStartingData(Db db)
    {
        // remove the old empty demo accounts made by v5 (they had no chambers)
        db.Users.DeleteMany(u => u.Email == "demo.doctor@carenexus.com" || u.Email == "demo.assistant@carenexus.com");

        // ---- departments ----
        string[] departmentNames = { "Cardiology", "Medicine", "Pediatrics", "Orthopedics", "Dermatology" };
        foreach (string name in departmentNames)
        {
            db.Departments.InsertOne(new Department { Name = name });
        }

        // ---- doctors (IDs 201-205) + their assistants (IDs 301-305) ----
        // number, name, department, degree, years, fee, chamber start, chamber end
        AddDoctor(db, 1, "Dr. Rahman Khan", "Cardiology", "MBBS, FCPS (Cardiology)", 15, 1000, "16:00", "22:00");
        AddDoctor(db, 2, "Dr. Nusrat Jahan", "Medicine", "MBBS, MD (Medicine)", 10, 800, "09:00", "14:00");
        AddDoctor(db, 3, "Dr. Tanvir Ahmed", "Pediatrics", "MBBS, DCH (Pediatrics)", 8, 700, "15:00", "21:00");
        AddDoctor(db, 4, "Dr. Kamrul Hasan", "Orthopedics", "MBBS, MS (Orthopedics)", 12, 1000, "10:00", "16:00");
        AddDoctor(db, 5, "Dr. Farhana Islam", "Dermatology", "MBBS, DDV (Dermatology)", 7, 900, "17:00", "22:00");
    }

    // adds one doctor + his chamber (every day, so you can test any day) + one assistant
    private static void AddDoctor(Db db, int number, string name, string departmentName, string degree,
                                  int years, int fee, string startTime, string endTime)
    {
        Department dept = db.Departments.Find(d => d.Name == departmentName).First();
        string doctorLoginId = "20" + number;       // 201, 202 ...
        string assistantLoginId = "30" + number;    // 301, 302 ...

        // skip if this ID is already used
        if (db.Users.CountDocuments(u => u.LoginId == doctorLoginId) > 0) return;

        User doctor = new User();
        doctor.LoginId = doctorLoginId;
        doctor.Name = name;
        doctor.Email = "doctor" + doctorLoginId + "@carenexus.com";
        doctor.Phone = "0170000000" + number;
        doctor.Role = Roles.Doctor;
        doctor.PasswordHash = BCrypt.Net.BCrypt.HashPassword("1234", 12);
        doctor.DepartmentId = dept.Id!;
        doctor.Degree = degree;
        doctor.ExperienceYears = years;
        doctor.Fee = fee;
        db.Users.InsertOne(doctor);   // after this, doctor.Id is filled by MongoDB

        Chamber chamber = new Chamber();
        chamber.DoctorId = doctor.Id!;
        chamber.Room = "Room " + (200 + number);
        chamber.Days = new List<string>(Chamber.AllDays);
        chamber.StartTime = startTime;
        chamber.EndTime = endTime;
        db.Chambers.InsertOne(chamber);

        // assistant who controls this doctor's serials
        if (db.Users.CountDocuments(u => u.LoginId == assistantLoginId) > 0) return;

        User assistant = new User();
        assistant.LoginId = assistantLoginId;
        assistant.Name = "Assistant of " + name.Replace("Dr. ", "");
        assistant.Email = "assistant" + assistantLoginId + "@carenexus.com";
        assistant.Phone = "0180000000" + number;
        assistant.Role = Roles.Assistant;
        assistant.PasswordHash = BCrypt.Net.BCrypt.HashPassword("1234", 12);
        assistant.DoctorId = doctor.Id!;
        db.Users.InsertOne(assistant);
    }
}
