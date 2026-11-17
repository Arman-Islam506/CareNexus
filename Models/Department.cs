using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CareNexus.Models;

// A hospital department, e.g. Cardiology
[BsonIgnoreExtraElements]
public class Department
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Name { get; set; } = "";
}
