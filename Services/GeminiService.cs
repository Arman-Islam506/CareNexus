using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CareNexus.Services;

// Talks to Google's Gemini AI.
// Needs in User Secrets:
//   "Gemini": { "ApiKey": "your key from aistudio.google.com" }
// Optional in appsettings.json (to change the models later):
//   "Gemini": { "Model": "gemini-3.8-flash",
//               "BackupModels": "gemini-3.5-flash,gemini-2.5-flash,gemini-3.1-flash-lite" }
//
// Safe design (AI only reads and explains, OUR CODE decides High / Low):
//   1. AI reads the PDF / photo -> test name, value, unit, printed normal range
//   2. our code cleans the numbers and flags High / Low
//   3. AI writes a short, simple explanation of OUR flags
public class GeminiService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiService> _logger;

    // the last error message (shown on the page while testing)
    public string LastError = "";

    public GeminiService(HttpClient http, IConfiguration config, ILogger<GeminiService> logger)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(60);
        _config = config;
        _logger = logger;
    }

    public bool IsSetUp()
    {
        return !string.IsNullOrWhiteSpace(_config["Gemini:ApiKey"]);
    }

    // ---------- STEP 1: read the report file ----------
    // returns the tests found in the file, or null if the AI call failed
    public async Task<List<ReportFinding>?> ReadReport(byte[] fileBytes, string mimeType)
    {
        string prompt =
            "You read medical laboratory reports. Find every test result in this file. " +
            "Return ONLY JSON in this exact shape: " +
            "{\"tests\":[{\"name\":\"\",\"value\":\"\",\"unit\":\"\",\"rangeLow\":\"\",\"rangeHigh\":\"\"}]}. " +
            "Copy numbers exactly as printed. If the normal range is printed like '13 - 17', rangeLow is '13' and rangeHigh is '17'. " +
            "If it is printed like '< 200', rangeLow is '' and rangeHigh is '200'. If no range is printed, leave both empty. " +
            "Do NOT include the patient's name, ID, phone, address or any personal details. " +
            "If this is not a lab report, return {\"tests\":[]}.";

        // the request: the file (as base64) + our instructions, and "answer in JSON"
        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = mimeType, data = Convert.ToBase64String(fileBytes) } },
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new { responseMimeType = "application/json", temperature = 0 }
        };

        string? answer = await CallGemini(body);
        if (answer == null) return null;

        // sometimes the AI wraps the JSON in ```json ... ``` -> remove that
        answer = answer.Trim();
        if (answer.StartsWith("```"))
        {
            answer = answer.Replace("```json", "").Replace("```", "").Trim();
        }

        // turn the JSON answer into a list of findings
        List<ReportFinding> findings = new List<ReportFinding>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(answer);
            foreach (JsonElement t in doc.RootElement.GetProperty("tests").EnumerateArray())
            {
                string name = GetText(t, "name");
                double? value = CleanNumber(GetText(t, "value"));
                if (name == "" || value == null) continue;          // skip anything we can't read as a number

                ReportFinding f = new ReportFinding();
                f.Test = name;
                f.Value = value.Value;
                f.Unit = GetText(t, "unit");
                double? low = CleanNumber(GetText(t, "rangeLow"));
                double? high = CleanNumber(GetText(t, "rangeHigh"));
                FlagFinding(f, low, high);                          // OUR code decides High / Low
                findings.Add(f);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read Gemini's answer: {Answer}", answer);
            LastError = "Could not read the AI answer: " + answer;
            return null;
        }
        return findings;
    }

    // ---------- STEP 2: our code decides High / Low ----------
    private static void FlagFinding(ReportFinding f, double? low, double? high)
    {
        // no range on the report -> use our table of normal adult ranges (if we know the test)
        TestRule? rule = ReportChecker.FindRule(f.Test.ToLower());
        if (low == null && high == null && rule != null)
        {
            low = rule.Min;
            high = rule.Max;
            if (f.Unit == "") f.Unit = rule.Unit;
        }

        if (low == null && high == null)
        {
            f.Level = "Unknown";
            f.NormalRange = "not printed";
            f.Note = "No normal range found — ask your doctor about this value.";
            return;
        }

        if (low != null && high != null) f.NormalRange = low + " – " + high;
        else if (high != null) f.NormalRange = "below " + high;
        else f.NormalRange = "above " + low;

        if (low != null && f.Value < low)
        {
            f.Level = "Low";
            f.Note = rule != null && rule.LowNote != "" ? rule.LowNote : "Lower than the normal range.";
        }
        else if (high != null && f.Value > high)
        {
            f.Level = "High";
            f.Note = rule != null && rule.HighNote != "" ? rule.HighNote : "Higher than the normal range.";
        }
        else
        {
            f.Level = "Normal";
            f.Note = "Within the normal range.";
        }

        // very far from normal -> mark as urgent
        if ((high != null && high > 0 && f.Value > high * 2) || (low != null && low > 0 && f.Value < low * 0.5))
        {
            f.Note = "Very far from normal — please contact your doctor soon.";
            f.IsCritical = true;
        }
    }

    // ---------- STEP 3: a short, simple explanation ----------
    public async Task<string?> Explain(List<ReportFinding> findings)
    {
        StringBuilder list = new StringBuilder();
        foreach (ReportFinding f in findings)
        {
            list.AppendLine(f.Test + ": " + f.Value.ToString(CultureInfo.InvariantCulture) + " " + f.Unit +
                            " (normal " + f.NormalRange + ") = " + f.Level);
        }

        string prompt =
            "You help patients in Bangladesh understand their lab report before they see the doctor. " +
            "Here are the results, already checked by our system (do not change the High/Low labels):\n" + list +
            "\nWrite 4 to 6 short, simple sentences in plain English for a patient. Say which values are outside the normal range " +
            "and what that can commonly mean, in a calm way. Do NOT give a diagnosis, do NOT suggest medicines or doses. " +
            "End by telling them to discuss the report with their doctor.";

        var body = new
        {
            contents = new[] { new { parts = new object[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.2 }
        };

        return await CallGemini(body);
    }

    // ---------- the actual web call to Gemini ----------
    // returns the AI's text answer, or null if something went wrong.
    // If one model's free limit is used up (429), it tries the backup models.
    private async Task<string?> CallGemini(object body)
    {
        string? apiKey = _config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LastError = "Gemini API key is missing.";
            return null;
        }

        // main model first, then the backup models
        List<string> models = new List<string>();
        models.Add(_config["Gemini:Model"] ?? "gemini-3.8-flash");
        string backups = _config["Gemini:BackupModels"] ?? "gemini-3.5-flash,gemini-2.5-flash,gemini-3.1-flash-lite";
        foreach (string name in backups.Split(','))
        {
            if (name.Trim() != "") models.Add(name.Trim());
        }

        string bodyJson = JsonSerializer.Serialize(body);

        foreach (string model in models)
        {
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" + model + ":generateContent";

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Add("x-goog-api-key", apiKey);
                    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await _http.SendAsync(request);
                    string json = await response.Content.ReadAsStringAsync();
                    int code = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                    {
                        // the answer text is in: candidates[0].content.parts[0].text
                        using JsonDocument doc = JsonDocument.Parse(json);
                        JsonElement parts = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
                        StringBuilder text = new StringBuilder();
                        foreach (JsonElement p in parts.EnumerateArray())
                        {
                            if (p.TryGetProperty("text", out JsonElement t)) text.Append(t.GetString());
                        }
                        return text.ToString();
                    }

                    LastError = "Gemini error " + code + " (model: " + model + "): " + json;
                    _logger.LogWarning("Gemini error {Code} with {Model}", code, model);

                    // daily limit used up -> no point waiting, go to the next model
                    if (code == 429 && json.Contains("per day")) break;

                    // busy for a moment -> wait 2 s and try this model once more
                    if ((code == 429 || code == 500 || code == 503) && attempt < 2)
                    {
                        await Task.Delay(2000);
                        continue;
                    }

                    // model not found (404) or other problem -> next model
                    break;
                }
                catch (Exception ex)
                {
                    LastError = "Gemini call failed (model: " + model + "): " + ex.Message;
                    _logger.LogError(ex, "Gemini call failed");
                    if (attempt < 2) await Task.Delay(2000);
                }
            }
        }

        return null;   // every model failed
    }

    // ---------- small helpers ----------

    private static string GetText(JsonElement e, string property)
    {
        if (!e.TryGetProperty(property, out JsonElement v)) return "";
        if (v.ValueKind == JsonValueKind.String) return (v.GetString() ?? "").Trim();
        if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
        return "";
    }

    // "2,50,000" -> 250000,  "10.5 g/dL" -> 10.5,  "abc" -> null
    public static double? CleanNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string clean = text.Replace(",", "").Replace(" ", "");   // remove lakh / thousand commas

        // keep only the first number (digits, one dot, minus sign)
        StringBuilder number = new StringBuilder();
        foreach (char c in clean)
        {
            if (char.IsDigit(c) || c == '.' || (c == '-' && number.Length == 0)) number.Append(c);
            else if (number.Length > 0) break;
        }

        double value;
        if (!double.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return null;
        if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) return null;   // impossible values
        return value;
    }
}