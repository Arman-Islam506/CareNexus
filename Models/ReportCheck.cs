using CareNexus.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CareNexus.Models;

// One saved report check (the "My Reports" history).
// We save ONLY the values and the explanation — never the uploaded file.
[BsonIgnoreExtraElements]
public class ReportCheck
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string PatientId { get; set; } = "";
    public string CreatedAt { get; set; } = "";      // "2026-11-17 12:40:00"

    public bool ReadByAi { get; set; }               // true = uploaded file, false = typed values
    public string FileName { get; set; } = "";       // only the name, to remember which report it was
    public string Summary { get; set; } = "";
    public string Explanation { get; set; } = "";    // "In simple words" from the AI
    public bool IsCritical { get; set; }             // some value very far from normal

    public List<ReportFinding> Findings { get; set; } = new List<ReportFinding>();
    public List<string> NotRecognised { get; set; } = new List<string>();

    // how many values are High or Low
    public int NotNormalCount()
    {
        return Findings.Count(f => f.Level == "High" || f.Level == "Low");
    }
}
