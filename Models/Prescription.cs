using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CareNexus.Models;

// A prescription written by the doctor for one serial.
// The patient can see and print it.
[BsonIgnoreExtraElements]
public class Prescription
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string SerialId { get; set; } = "";
    public string PatientId { get; set; } = "";
    public string DoctorId { get; set; } = "";
    public string CreatedAt { get; set; } = "";      // "2026-11-15 10:05:00"

    public string Diagnosis { get; set; } = "";
    public string Medicines { get; set; } = "";      // one medicine per line
    public string Advice { get; set; } = "";
    public string FollowUp { get; set; } = "";
}
