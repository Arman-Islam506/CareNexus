namespace CareNexus.Services;

// The hospital that uses this copy of CareNexus.
// CareNexus is a product: each hospital gets its own copy, so we only
// change these values in appsettings.json (no code changes needed):
//
//   "Hospital": {
//     "Name": "Popular Medical Centre",
//     "Address": "Dhanmondi, Dhaka",
//     "Phone": "09610-000000"
//   }
//
// (Many hospitals in one system is future work.)
public static class HospitalInfo
{
    public static string Name = "CareNexus General Hospital";
    public static string Address = "Dhaka, Bangladesh";
    public static string Phone = "";

    // called once in Program.cs when the app starts
    public static void Load(IConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config["Hospital:Name"])) Name = config["Hospital:Name"]!;
        if (!string.IsNullOrWhiteSpace(config["Hospital:Address"])) Address = config["Hospital:Address"]!;
        if (!string.IsNullOrWhiteSpace(config["Hospital:Phone"])) Phone = config["Hospital:Phone"]!;
    }
}
