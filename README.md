# AttendX - Attendance Management System (v3: portal interface)

ASP.NET Core MVC (.NET 10) + Entity Framework Core + SQL Server LocalDB + ASP.NET Core Identity.
Plain institutional portal interface (flat navy theme, fully responsive: phone, tablet, desktop). Three separate login screens, a faculty dashboard, a student dashboard,
manual + QR attendance, offline sync, reports (Excel / PDF / CSV), attendance summary, notices, holidays, student attendance queries, audit log, backup/restore and a master-data Excel export.
The institution name shown in the header is set in `appsettings.json` -> `Institution` (`Name`, `ShortName`, `SystemName`).

> **Honest status:** this project was written without access to a .NET SDK or the internet, so it has **not been compiled or run by me**.
> It was checked with scripts only (bracket balance, controller/view matching, JavaScript syntax) and the timetable/dataset were verified by script
> (84 classes x exactly 30 students, 126 faculty, 0 timetable clashes). Expect that the first build may show a few small errors -
> send me the first error (file, line, message) and I will fix it. See section 8.

---

## 1. Install (one time) - you have Visual Studio 2026
Visual Studio Installer -> Modify -> tick: **ASP.NET and web development** and **Data storage and processing** (SQL Server Express **LocalDB**).
Nothing else. Use Chrome or Edge. Internet is needed while running (Bootstrap, Chart.js, Inter font, QR scanner load from CDNs).
Check LocalDB: Command Prompt -> `sqllocaldb info` shows `MSSQLLocalDB`.

## 2. Run
1. Unzip `AttendX_Solution.zip` (short path, e.g. `C:\Projects\AttendX_Solution`).
2. Double-click **AttendX.sln** -> press **F5**.
3. First run creates the database and seeds all data: **wait 1-3 minutes** (2,520 students, 126 faculty, 3,360 timetable rows, ~200,000 attendance records). Next runs start in seconds.

## 3. The three login screens
| Screen | URL | You enter |
|---|---|---|
| Admin | `/admin/login` | Email + Password |
| Faculty / HOD | `/faculty/login` | **Institution ID (Faculty ID) + Email + Password** |
| Student | `/student/login` | **Institution ID (Student ID) + Email + Password** |
The landing page `/` shows the three role cards. All three values must belong to the same account, otherwise the same generic error appears (and 5 failures lock the account for 10 minutes).

## 4. Demo logins (development passwords)
| Role | Institution ID | Email | Password |
|---|---|---|---|
| Admin | - | `admin@attendx.edu.in` | `Admin@123` |
| HOD (CSE) | `FAC-CSE-001` | `ishaan.chauhan@attendx.edu.in` | `Faculty@123` |
| Faculty (CSE) | `FAC-CSE-002` | `khushi.desai@attendx.edu.in` | `Faculty@123` |
| Student | `1EV1-CSE-001` | `1EV1-CSE-001@attendx.edu.in` | `Student@123` |
**Every** faculty (`FAC-<BRANCH>-001..009`, 001 = HOD) and **every** student (`Dataset/students.csv`, e.g. `3EV2-CSE-017`) can log in with their own Institution ID + email + the role password above.
Full list: `Dataset/demo_logins.md` and the **Logins** sheet of `Dataset/AttendX_Master_Data.xlsx`.

## 5. Seeded data (all automatic, from the `Dataset` folder)
* 14 branches, 102 sections, 896 subjects (8 per semester, semesters 1-8).
* **30 students per class** = 14 branches x first 2 sections x semesters 1, 3, 5 -> **2,520 students**, real-looking Indian names, email `<StudentID>@attendx.edu.in`, Student ID like `3EV1-CSE-001`.
* **126 faculty** (1 HOD + 8 faculty per branch), each teaching exactly 3 subjects (one per semester 1/3/5, to both sections). Faculty-subject mappings are created from the timetable.
* **Conflict-free timetable** Mon-Sat, 7 periods (09:00-16:55, 55-minute periods), rooms like `Block A-101`, `Lab-2`.
* 14 days of realistic attendance history, holidays (Sundays are automatic), notices, academic year 2026-27.
* **Excel master file:** `Dataset/AttendX_Master_Data.xlsx` (sheets: Admin, Faculty, Students, Subjects, Timetable, Holidays, Logins). The app also writes a fresh copy to `AttendX/App_Data/Exports/` after seeding, and Admin -> *Backup / Excel* -> **Download master data (Excel)** regenerates it any time.
  (Passwords are never stored in readable form; the Excel shows the default seed password.)
* Photos: initials avatars (no external face services, nothing downloaded). Admin can upload real photos.
Change seed size in `appsettings.json` -> `AttendX` (`SeedDemoData`, `SeedHistoryDays`) or edit the CSV files.

## 6. What each role sees
* **Admin / HOD dashboard** - KPIs, charts, low-attendance alerts, missing attendance, analytics flags; students, faculty, subjects, mappings, timetable, **holidays**, **notices**, corrections, reports, analytics, audit, backup, settings. HOD sees only own department.
* **Faculty dashboard** (one screen, loaded from the logged-in Faculty ID): now/next class with *Mark attendance* and *Scan QR*, today's timeline, "You teach N classes" chips with strength and average %, students below threshold, missing attendance, notices, weekly timetable, open student queries.
* **Student dashboard** (one screen, read-only, own data only): ID card + QR, overall attendance donut, classes needed, 4-week projection, subject-wise bars, 5-week heat map, today's classes, weekly timetable, recent records, my faculty, notices, holidays, **attendance queries**, PDF report, phone update, recent sign-ins. Classmates are shown as a **count only**.
* **Empty states** (small icon + one line): Sunday ("No classes today. Today is Sunday."), holiday, no classes, no attendance recorded, no timetable, no students, no notices, no queries, no results, 404/403/500.

## 7. Demo script
1. `/admin/login` -> admin demo -> look at the dashboard -> Backup / Excel -> download master data.
2. Settings -> Period timings: move Period 1 so the **current time** is inside it (attendance is allowed only inside the period +/- grace, server clock).
3. `/faculty/login` -> faculty demo (CSE faculty teach CSE classes) -> Mark attendance -> try a closed period (refused).
4. `/student/login` -> student demo -> see the dashboard; raise a query; open the QR card.
5. Faculty -> Scan QR (use the student's QR on another screen). Scan twice -> "already marked".
6. Admin -> Holidays: add today as a Holiday -> faculty and student dashboards show the holiday screen; on a Sunday they show the Sunday screen.
7. Admin -> Corrections (reason required) -> Audit log shows old/new values.

## 8. If the build shows errors
1. View -> Error List. Common causes: a NuGet version missing (edit `AttendX/AttendX.csproj`; QuestPDF uses `2025.*`), or a typo I could not catch without a compiler.
2. Send me the first error (file, line, message). 3. Tests: Test -> Run All Tests.
4. LocalDB: `sqllocaldb start MSSQLLocalDB`. HTTPS certificate: `dotnet dev-certs https --trust`.
5. Start from scratch: stop the app, delete database `AttendXDb` in SQL Server Object Explorer, F5 again.
6. Using .NET 8 instead of 10? Replace `net10.0` and the `10.0.0` package versions with `net8.0` / `8.0.x`.

## 9. Structure
```
AttendX_Solution/ AttendX.sln  README.md  docs/ (documentation, viva questions, presentation, prompts/)
  AttendX/        Models/ Data/ (DbContext, DbSeeder) Services/ Controllers/ ViewModels/ Views/ Infrastructure/ wwwroot/ Dataset/ (CSV + xlsx)
  AttendX.Tests/  xUnit tests
```

## 10. Known limitations
* Not compiled/run in my environment. * Internet needed for CDN libraries. * Offline QR scans cannot check the rotating token (server still verifies signature, class, window, duplicates).
* Student photos are generated avatars (upload real ones in Admin). * Timetable/students are seeded for semesters 1, 3, 5 in the first two sections of each branch; admin builds the rest.
* Idle sign-out (30 min) is enforced in the browser. * Backup/restore uses SQL Server native BACKUP/RESTORE (folder must be writable by the SQL service/LocalDB user).
