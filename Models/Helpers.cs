namespace CareNexus.Models;

// Small helper classes used by the pages.

// One row of the live queue table
public class QueueRow
{
    public Serial Serial { get; set; } = new Serial();
    public string PatientName { get; set; } = "";
    public string PatientLoginId { get; set; } = "";
    public string PatientPhone { get; set; } = "";
    public DateTime? Estimated { get; set; }      // estimated time to go in
    public int PeopleAhead { get; set; }
    public int DelayMinutes { get; set; }         // + late, - early, compared to the booked slot
    public bool HasPrescription { get; set; }
}

// One choice in a dropdown list
public class Option
{
    public string Value { get; set; } = "";
    public string Text { get; set; } = "";
}

// Answer from a service method: did it work + a message to show
public class Result
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";

    public static Result Success(string message)
    {
        return new Result { Ok = true, Message = message };
    }

    public static Result Fail(string message)
    {
        return new Result { Ok = false, Message = message };
    }
}
