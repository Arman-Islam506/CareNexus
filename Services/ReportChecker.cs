using System.Globalization;
using System.Text.RegularExpressions;

namespace CareNexus.Services;

// One checked value from the report
public class ReportFinding
{
    public string Test { get; set; } = "";
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public string NormalRange { get; set; } = "";
    public string Level { get; set; } = "";      // Low / Normal / High
    public string Note { get; set; } = "";
}

// The full answer of the report checker
public class ReportResult
{
    public List<ReportFinding> Findings { get; set; } = new List<ReportFinding>();
    public List<string> NotRecognised { get; set; } = new List<string>();
    public string Summary { get; set; } = "";
}

// One test we know: its normal adult range and what low / high can mean
public class TestRule
{
    public string Name = "";
    public string Unit = "";
    public double Min;
    public double Max;
    public string LowNote = "";
    public string HighNote = "";
    public string[] Names = new string[0];   // other names people write on reports

    public TestRule(string name, string unit, double min, double max, string lowNote, string highNote, string[] names)
    {
        Name = name; Unit = unit; Min = min; Max = max; LowNote = lowNote; HighNote = highNote; Names = names;
    }
}

// Simple report checker (no AI yet).
// It reads lines like "Hemoglobin 10.5" and compares them with normal adult ranges.
// Later we will add the Gemini AI to read the PDF / image automatically.
public static class ReportChecker
{
    private static readonly List<TestRule> Rules = new List<TestRule>
    {
        new TestRule("Hemoglobin", "g/dL", 12, 17.5, "May point to anaemia (low blood).", "Can happen with dehydration or other conditions.", new[] { "hemoglobin", "haemoglobin", "hb", "hgb" }),
        new TestRule("WBC", "×10⁹/L", 4, 11, "Low white cells can lower immunity.", "Often seen with infection or inflammation.", new[] { "wbc", "white blood cell", "tc", "total count" }),
        new TestRule("Platelets", "×10⁹/L", 150, 450, "Low platelets can cause easy bleeding (check for dengue if fever).", "Can rise with infection or inflammation.", new[] { "platelet", "platelets", "plt" }),
        new TestRule("Fasting blood sugar", "mg/dL", 70, 100, "Low sugar — eat something and inform the doctor.", "Higher than normal — may suggest prediabetes or diabetes.", new[] { "fbs", "fasting sugar", "fasting glucose", "fasting blood sugar", "glucose", "sugar" }),
        new TestRule("HbA1c", "%", 4, 5.6, "", "Average sugar of the last 3 months is high.", new[] { "hba1c", "a1c" }),
        new TestRule("Total cholesterol", "mg/dL", 0, 200, "", "High cholesterol raises heart risk.", new[] { "cholesterol", "total cholesterol" }),
        new TestRule("Triglycerides", "mg/dL", 0, 150, "", "High — diet and exercise matter.", new[] { "triglyceride", "triglycerides", "tg" }),
        new TestRule("Creatinine", "mg/dL", 0.6, 1.3, "Usually not a concern.", "May point to reduced kidney function.", new[] { "creatinine", "s. creatinine", "serum creatinine" }),
        new TestRule("SGPT (ALT)", "U/L", 7, 56, "", "Liver may be stressed or inflamed.", new[] { "sgpt", "alt" }),
        new TestRule("TSH", "mIU/L", 0.4, 4.0, "May point to an overactive thyroid.", "May point to an underactive thyroid.", new[] { "tsh" }),
        new TestRule("Uric acid", "mg/dL", 3.5, 7.2, "", "High uric acid can cause gout.", new[] { "uric acid", "uric" }),
    };

    // a test name, then a space or : = -, then a number. Example: "Hemoglobin: 10.5"
    private static readonly Regex LinePattern = new Regex(@"^\s*([A-Za-z][A-Za-z0-9 .()]*?)\s*(?:[:=\-]\s*|\s+)(\d+(?:\.\d+)?)");

    public static ReportResult Check(string reportText)
    {
        ReportResult result = new ReportResult();
        string[] lines = (reportText ?? "").Split('\n');

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line == "") continue;

            Match match = LinePattern.Match(line);
            if (!match.Success)
            {
                result.NotRecognised.Add(line);
                continue;
            }

            string name = match.Groups[1].Value.Trim().ToLower();
            double value = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

            TestRule? rule = FindRule(name);
            if (rule == null)
            {
                result.NotRecognised.Add(line);
                continue;
            }

            ReportFinding finding = new ReportFinding();
            finding.Test = rule.Name;
            finding.Value = value;
            finding.Unit = rule.Unit;
            finding.NormalRange = rule.Min == 0 ? "below " + rule.Max : rule.Min + " – " + rule.Max;

            if (value < rule.Min)
            {
                finding.Level = "Low";
                finding.Note = rule.LowNote != "" ? rule.LowNote : "Lower than usual.";
            }
            else if (value > rule.Max)
            {
                finding.Level = "High";
                finding.Note = rule.HighNote;
            }
            else
            {
                finding.Level = "Normal";
                finding.Note = "Within the normal range.";
            }

            result.Findings.Add(finding);
        }

        // short summary at the top
        int notNormal = result.Findings.Count(f => f.Level != "Normal");

        if (result.Findings.Count == 0)
            result.Summary = "No known test values were found. Type values like \"Hemoglobin 11.2\", one per line.";
        else if (notNormal == 0)
            result.Summary = "All " + result.Findings.Count + " checked values are within the normal range.";
        else
            result.Summary = notNormal + " of " + result.Findings.Count + " values are outside the normal range. Show this report to your doctor.";

        return result;
    }

    private static TestRule? FindRule(string name)
    {
        // exact name first
        foreach (TestRule rule in Rules)
        {
            if (rule.Names.Contains(name)) return rule;
        }
        // then "starts with", e.g. "hemoglobin (hb)"
        foreach (TestRule rule in Rules)
        {
            foreach (string n in rule.Names)
            {
                if (name.StartsWith(n)) return rule;
            }
        }
        return null;
    }
}
