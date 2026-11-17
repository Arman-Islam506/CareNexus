# CareNexus

**Smart Healthcare Management Platform with AI-Based Medical Report Analysis**

A doctor-chamber serial and live-queue system for Bangladesh, with an AI lab-report checker.
IUB CSE internship / final year project.

## Tech stack
- ASP.NET Core MVC (C#, .NET 10), Razor views + Bootstrap 5
- MongoDB Atlas (`MongoDB.Driver`)
- Cookie login with roles, BCrypt password hashing
- Gmail SMTP email sender (MailKit), ready for later use

## Features (v6)
- **Register:** patients sign up and verify their email with a 6-digit code (Gmail SMTP, MailKit)
- **Login:** pick a role (Patient, Doctor, Assistant, Admin) + ID or email + 4-digit password (BCrypt)
- **Patient:** dashboard, doctor list by department, take serial (department → doctor → date on a calendar → free 10-min slot → problem), My Serial with live "arrive by" time, cancel, prescriptions (view + print), report checker
- **Assistant:** add serial for walk-in / phone patients (new account made if needed), print prescription after the visit, live serial queue, Present / Absent (absent once → end of queue, twice → removed), consultation timer, fee entry, today's collections
- **Doctor:** today's patients with live times and collection, prescription notepad, patient history search, chamber schedule
- **Admin:** dashboard by department, users (add doctor / assistant), departments, chamber schedules
- Starting data (5 departments, 5 doctors with 2 chambers each, 5 assistants) is added automatically on first run

## Demo accounts (password for all: `1234`)
Log in with the **ID** or the **email**. Added automatically when the app starts.
IDs: patients 1xx, doctors 2xx, assistants 3xx, admins 9xx. Every password is 4 digits.

| ID | Role | Name | Email |
|---|---|---|---|
| 901 | Admin | Hospital Admin | admin901@carenexus.com |
| 101 | Patient | Arman Islam | patient101@carenexus.com |
| 102 | Patient | Sadia Akter | patient102@carenexus.com |
| 103 | Patient | Rafiq Hasan | patient103@carenexus.com |
| 201 | Doctor | Dr. Rahman Khan (Cardiology) | doctor201@carenexus.com |
| 202 | Doctor | Dr. Nusrat Jahan (Medicine) | doctor202@carenexus.com |
| 203 | Doctor | Dr. Tanvir Ahmed (Pediatrics) | doctor203@carenexus.com |
| 204 | Doctor | Dr. Kamrul Hasan (Orthopedics) | doctor204@carenexus.com |
| 205 | Doctor | Dr. Farhana Islam (Dermatology) | doctor205@carenexus.com |
| 301 | Assistant | Assistant of Rahman Khan | assistant301@carenexus.com |
| 302 | Assistant | Assistant of Nusrat Jahan | assistant302@carenexus.com |
| 303 | Assistant | Assistant of Tanvir Ahmed | assistant303@carenexus.com |
| 304 | Assistant | Assistant of Kamrul Hasan | assistant304@carenexus.com |
| 305 | Assistant | Assistant of Farhana Islam | assistant305@carenexus.com |

New patients who register get the next ID (104, 105 ...). Doctors and assistants added by the admin get 206 / 306 ...

Remove the demo box on the login page before giving the app to a real hospital.

## One hospital per copy
CareNexus is sold to one hospital at a time. Change the hospital's name, address and phone in
`appsettings.json` → `"Hospital"` (no code changes). Many hospitals in one system is future work.

## Still to do
- AI report checker with Gemini (PDF / image)
- Notifications, Azure deploy

## Run it locally
1. Open `CareNexus.slnx` in Visual Studio.
2. Right-click the project → **Manage User Secrets**, and add:
   ```json
   {
     "MongoDb": { "ConnectionString": "<atlas connection string>", "DatabaseName": "CareNexusDb" },
     "Email":   { "From": "<gmail address>", "AppPassword": "<16-letter gmail app password>" },
     "Admin":   { "Email": "<admin email>", "Password": "<admin password>" }
   }
   ```
3. Press **F5**.

Secrets are kept in User Secrets and are never committed to GitHub.
