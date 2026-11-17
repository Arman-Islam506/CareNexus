using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CareNexus.Models;

// The 4 role names, written once so we never misspell them
public static class Roles
{
    public const string Patient = "Patient";
    public const string Doctor = "Doctor";
    public const string Assistant = "Assistant";
    public const string Admin = "Admin";
}

// One account in the "Users" collection.
// All 4 roles use this same class. Some fields are only used by one role.
[BsonIgnoreExtraElements]   // ignore old fields in the database that we don't use anymore
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    // for everyone
    public string LoginId { get; set; } = "";        // short ID to log in: patient 101, doctor 201, assistant 301, admin 901
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PasswordHash { get; set; } = "";   // scrambled password, never the real one
    public string Role { get; set; } = Roles.Patient;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string PhotoUrl { get; set; } = "";       // e.g. "/uploads/avatars/abc123.jpg" (empty = no photo)

    // email verification (only for patients who sign up on the website)
    // old accounts in the database don't have this field, so it starts as true for them
    public bool IsVerified { get; set; } = true;
    public string? OtpHash { get; set; }             // the 6-digit code, scrambled with BCrypt
    public string? OtpExpiresAt { get; set; }        // e.g. "2026-11-15 16:10:00" (10 minutes)
    public string? OtpSentAt { get; set; }           // used for the 1-minute wait before "send again"
    public int OtpAttempts { get; set; }             // wrong tries (max 5)

    // patient details (filled on the profile page)
    public int? Age { get; set; }
    public string Gender { get; set; } = "";
    public string BloodGroup { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";

    // doctor details (filled by the admin)
    public string DepartmentId { get; set; } = "";
    public string Degree { get; set; } = "";
    public int ExperienceYears { get; set; }
    public int Fee { get; set; }                     // consultation fee in Taka

    // assistant details: the doctor he/she works for
    public string DoctorId { get; set; } = "";

    // first letter of the name, shown in the round avatar
    public string FirstLetter()
    {
        string clean = Name.Replace("Dr. ", "").Trim();
        if (clean.Length == 0) return "?";
        return clean.Substring(0, 1).ToUpper();
    }
}
