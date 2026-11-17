using CareNexus.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CareNexus.Services;

// All our MongoDB collections ("tables") in one place,
// plus small helpers to find one item by its Id.
public class Db
{
    public IMongoCollection<User> Users;
    public IMongoCollection<Department> Departments;
    public IMongoCollection<Chamber> Chambers;
    public IMongoCollection<Serial> Serials;
    public IMongoCollection<Prescription> Prescriptions;
    public IMongoCollection<ReportCheck> ReportChecks;      // "My Reports" history

    public Db(IMongoDatabase database)
    {
        Users = database.GetCollection<User>("Users");
        Departments = database.GetCollection<Department>("Departments");
        Chambers = database.GetCollection<Chamber>("Chambers");
        Serials = database.GetCollection<Serial>("Serials");
        Prescriptions = database.GetCollection<Prescription>("Prescriptions");
        ReportChecks = database.GetCollection<ReportCheck>("ReportChecks");
    }

    // MongoDB Ids look like "6735f1c2a4b5c6d7e8f90123".
    // A wrong-looking Id would crash the query, so we check it first.
    public static bool IsValidId(string? id)
    {
        return !string.IsNullOrEmpty(id) && ObjectId.TryParse(id, out _);
    }

    public User? GetUser(string? id)
    {
        if (!IsValidId(id)) return null;
        return Users.Find(u => u.Id == id).FirstOrDefault();
    }

    public Chamber? GetChamber(string? id)
    {
        if (!IsValidId(id)) return null;
        return Chambers.Find(c => c.Id == id).FirstOrDefault();
    }

    public Serial? GetSerial(string? id)
    {
        if (!IsValidId(id)) return null;
        return Serials.Find(s => s.Id == id).FirstOrDefault();
    }

    public Prescription? GetPrescription(string? id)
    {
        if (!IsValidId(id)) return null;
        return Prescriptions.Find(p => p.Id == id).FirstOrDefault();
    }

    public ReportCheck? GetReportCheck(string? id)
    {
        if (!IsValidId(id)) return null;
        return ReportChecks.Find(r => r.Id == id).FirstOrDefault();
    }

    // the newest saved report check of one patient (or null)
    public ReportCheck? LatestReportOf(string patientId)
    {
        return ReportChecks.Find(r => r.PatientId == patientId)
                           .SortByDescending(r => r.CreatedAt)
                           .FirstOrDefault();
    }

    public string UserName(string? id)
    {
        User? user = GetUser(id);
        if (user == null) return "—";
        return user.Name;
    }

    public string DepartmentName(string? id)
    {
        if (!IsValidId(id)) return "—";
        Department? dept = Departments.Find(d => d.Id == id).FirstOrDefault();
        if (dept == null) return "—";
        return dept.Name;
    }

    // all chambers of one doctor, sorted by start time
    public List<Chamber> ChambersOf(string doctorId)
    {
        List<Chamber> list = Chambers.Find(c => c.DoctorId == doctorId).ToList();
        return list.OrderBy(c => c.StartTime).ToList();
    }

    // next free login ID for a role:
    //   Patient 101, 102 ...  Doctor 201 ...  Assistant 301 ...  Admin 901 ...
    // (after 99 people it simply goes on: 1100, 1101 ... so it never runs out)
    public string NextLoginId(string role)
    {
        string prefix = "1";
        if (role == Roles.Doctor) prefix = "2";
        if (role == Roles.Assistant) prefix = "3";
        if (role == Roles.Admin) prefix = "9";

        // find the biggest number already used by this role
        List<User> sameRole = Users.Find(u => u.Role == role).ToList();
        int biggest = 0;
        foreach (User u in sameRole)
        {
            if (u.LoginId != null && u.LoginId.Length > 1 && u.LoginId.StartsWith(prefix))
            {
                int number;
                if (int.TryParse(u.LoginId.Substring(1), out number) && number > biggest)
                {
                    biggest = number;
                }
            }
        }

        int next = biggest + 1;
        return prefix + next.ToString("00");   // 1 -> "101", 2 -> "102", 100 -> "1100"
    }

    public List<Department> AllDepartments()
    {
        return Departments.Find(d => true).ToList().OrderBy(d => d.Name).ToList();
    }

    public List<User> AllDoctors()
    {
        return Users.Find(u => u.Role == Roles.Doctor).ToList().OrderBy(u => u.Name).ToList();
    }
}
