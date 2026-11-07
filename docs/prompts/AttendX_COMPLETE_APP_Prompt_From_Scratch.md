# AttendX - COMPLETE APP PROMPT FROM SCRATCH
(Attendance Management System: ASP.NET Core MVC + SQL Server, Admin / HOD / Faculty / Student portals, flat institutional interface, fully responsive)

How to use: open an empty folder in VS Code or Visual Studio 2026 with an AI coding agent that can RUN TERMINAL COMMANDS (Kilo Code, Cursor, Claude Code). Paste **PROMPT A** (the whole app).
If the agent runs out of context, paste the **phase prompts** in section 16 one at a time. Requires the .NET 10 SDK (comes with Visual Studio 2026). With .NET 8 replace `net10.0`/`10.0.x` by `net8.0`/`8.0.x`.

---

# PROMPT A

## 1. ROLE AND GOAL
You are a senior full-stack .NET engineer and UI designer. Build **AttendX - Attendance Management System** completely from an empty folder: ASP.NET Core MVC (.NET 10), EF Core, SQL Server LocalDB, ASP.NET Core Identity.
Four roles (Admin, HOD, Faculty, Student) with email + ID + password login, fully seeded realistic data (30 students per class), manual and QR attendance with strict server-side rules, offline sync, reports, summaries, notices, holidays, audit log, backup and an Excel master-data export.
The interface is a **plain, professional institutional portal** (not a flashy app) and **works perfectly on phones, tablets and desktops**.
The solution must build with **0 errors and 0 warnings** and run with **F5 in Visual Studio 2026** after unzipping, with no manual database steps.

## 2. ENGINEERING RULES (this is how we avoid errors)
1. Solution `AttendX.sln` with projects `AttendX` (web, net10.0) and `AttendX.Tests` (xUnit). `Nullable` and `ImplicitUsings` enabled. A `GlobalUsings.cs` for common namespaces.
2. **Build gate:** after every phase run `dotnet build` (0 errors, 0 warnings) and `dotnet test`. Never leave TODOs, stubs or pseudo-code. Fix root causes, not symptoms.
3. NuGet (verify versions exist with `dotnet add package`): Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.*, Microsoft.EntityFrameworkCore.SqlServer 10.0.*, Microsoft.EntityFrameworkCore.Tools 10.0.*, QRCoder 1.6.*, ClosedXML 0.104.*, QuestPDF 2025.* (Community licence); tests: xunit, Microsoft.NET.Test.Sdk, xunit.runner.visualstudio, Microsoft.EntityFrameworkCore.InMemory 10.0.*. No other packages. Front-end libraries from CDN only: Bootstrap 5.3 (base), Bootstrap Icons, Chart.js 4, DataTables 1.13, html5-qrcode 2.3.8, Google Font Inter.
4. Database `(localdb)\MSSQLLocalDB`, name `AttendXDb`, connection string in appsettings.json. Use `EnsureCreated()` on first run (config flag `UseMigrations` switches to `Migrate()`).
5. **SQL Server cascade rule:** every FK of our own entities uses `DeleteBehavior.Restrict` (soft delete with `IsActive`). Use filtered unique indexes where specified.
6. **Razor rules:** escape literal `@` as `@@`; every view a controller returns must exist; tag-helper forms for all POST (antiforgery); no `<text>` SVG tags inside code blocks; avoid apostrophes inside single-quoted tag-helper attributes; lambda parameter names must not shadow locals or `foreach` variables.
7. **Seeding performance rules:** hash each demo password ONCE with `PasswordHasher` and reuse the hash string; insert Identity users/roles directly (`Users`, `UserRoles`) in batches of 500 with `AutoDetectChangesEnabled=false` and `ChangeTracker.Clear()`; insert attendance history with `SqlBulkCopy`; never write thousands of image files (QR codes are rendered on demand). First-run seeding < 3 minutes, later starts < 3 seconds. Seeding is idempotent and must never crash startup (try/catch + clear log messages).
8. Business rules live in services (never in controllers or JavaScript). Inject `IClock`; **server time is authoritative**.
9. Final proof required: `dotnet build`, `dotnet test`, and a smoke test (section 14) pasted in the final report.

## 3. ROLES AND AUTHORIZATION
Roles: `Admin`, `HOD`, `Faculty`, `Student`. HOD accounts also have the Faculty role. Policies: AdminOnly, AdminOrHod, AnyStaff (Admin/HOD/Faculty), StudentOnly.
- Admin: everything. HOD: own department only (students, faculty, reports, summary, audit, notices). Faculty: own classes only. Student: own data only, read-only (classmates as a **count only**).
- Every dashboard resolves the person from the logged-in identity (`IdentityUserId`) - **never from an id in the URL** (no IDOR). Admin/HOD may open read-only detail pages.
- Cookie auth 8 h sliding, secure cookie, CSRF on every POST (JSON calls send the token header), lockout 5 failures / 10 min, password policy (8+, upper, lower, digit, symbol), security headers, idle sign-out after 30 min (browser timer with a warning).

## 4. LOGIN (three entrances)
Role-selection page `/` and three screens: `/admin/login` (**Email + Password**), `/faculty/login` and `/student/login` (**Faculty ID / Student ID + Email + Password**). POST `/account/signin`.
Flow: normalise inputs (trim, upper-case ID, lower-case email) -> find the Identity user by email -> if locked out show the lockout message -> verify the **ID belongs to the same account**, the role matches the screen, the person is active, and the password is correct. **Any mismatch -> the same generic message "Institution ID, email or password is incorrect."** and `AccessFailedAsync` (counts towards lockout). Only after the password is proven correct may the app say "This account is not a <role> account. Use the correct login." Audit successes and failures (never passwords); store `LastLoginAt`; show last login and the last 5 sign-ins on the dashboards. Forgot password = "Contact the administrator" + Admin "Reset password" action (one-time temporary password, forced change at next login). Change-password page for everyone. Landing redirect: Student -> `/Student`, others -> `/Dashboard`.
**Seeded logins (development only; documented in README and `Dataset/demo_logins.md`; NOT shown in the UI):**
| Role | ID | Email | Password |
|---|---|---|---|
| Admin | - | `admin@attendx.edu.in` | `Admin@123` |
| HOD / Faculty | `FAC-CSE-001` (HOD), `FAC-CSE-002` ... | `<firstname>.<lastname>@attendx.edu.in` | `Faculty@123` |
| Student | `1EV1-CSE-001` ... | `<StudentID>@attendx.edu.in` | `Student@123` |
Config flag `ForceAdminPasswordChange` (default false).

## 5. ACADEMIC STRUCTURE
- **14 branches** (name -> code): CSE->CSE, CSE-AI->CSEAI, CSE-AIML->CSEAIML, AI&DS->ADS, ECE->ECE, EEE->EEE, MECH->MECH, CIVIL->CIVIL, IT->IT, Cyber Security->CYBER, AIDS->AIDS, AIML->AIML, IOT->IOT, DS->DS.
- **102 sections:** CSE EV1-EV6; CSE-AI EN1-EN24; CSE-AIML EN1-EN24; AI&DS EU1-EU6; ECE EC1-EC6; EEE EE1-EE4; MECH ME1-ME4; CIVIL CE1-CE4; IT IT1-IT6; Cyber CS1-CS6; AIDS AD1-AD4; AIML AM1-AM4; IOT IO1-IO4; DS DS1-DS4.
- A **class** = Branch + Section + Semester (one section hosts several semester cohorts). Year is ALWAYS calculated from semester (1-2=1, 3-4=2, 5-6=3, 7-8=4).
- **Student ID** = `[Semester][Section]-[BranchCode]-[001]`, e.g. `1EV1-CSE-001`, `3EC2-ECE-014`; numbering per class; unique among active students (filtered unique index); an ID may be reused after deactivation.
- Academic year `2026-27` (current). Periods (admin editable): P1 09:00-09:55, P2 10:00-10:55, P3 11:00-11:55, P4 12:00-12:55, (lunch), P5 14:00-14:55, P6 15:00-15:55, P7 16:00-16:55; Mon-Sat; Sundays are non-working automatically.

## 6. DATA MODEL
AcademicYear, Branch, Section, Subject (8 per semester per branch, semesters 1-8), Student (+`IdentityUserId`), Faculty (+`IdentityUserId`), FacultySubjectMapping, PeriodDefinition, Timetable, AttendanceSession, Attendance (long Id), AttendanceSettings (1 row), ThresholdRule, AuditLog, BackupHistory, Holiday (Date, Name, Type = Holiday/Event/Exam), Notice (Title, Body, Audience = All/Faculty/Student/Branch/Section, BranchId?, SectionId?, PublishedAt, ExpiresAt?, IsPinned, CreatedBy), AttendanceQuery (StudentId, SubjectId, FacultyId?, BranchId, RecordDate, Message, Status Open/Resolved/Rejected, Response, ResponderName, timestamps), `ApplicationUser : IdentityUser` (FullName, MustChangePassword, LastLoginAt).
Key constraints: unique active StudentID; **unique (StudentId, Date, Period)** in Attendance; Timetable unique active (AcademicYear, Section, **Semester**, Day, Period) and (AcademicYear, Faculty, Day, Period); unique (TimetableId, Date) in AttendanceSession; unique Branch.Code/Name, Section (Branch, Name), Subject.Code, Faculty.Email/FacultyID, Mapping (Faculty, Subject, Section, Year). Attendance copies BranchId and Semester at marking time. `Status` stored as text (Present/Absent/Late). Concurrency tokens (`RowVersion`) on Student and Attendance.

## 7. SEED DATA (CSV-driven, verified by script)
Create a `Dataset` folder (copied to output) and read it at first run: `subjects.csv`, `faculty.csv`, `students.csv`, `timetable.csv`, `holidays.csv`; also ship `AttendX_Master_Data.xlsx` and `demo_logins.md` generated from the same data by a Python script (included in `docs/` for regeneration).
- **Students:** exactly **30 per class** for 14 branches x first 2 sections x semesters 1, 3, 5 = **84 classes = 2,520 students**; unique real-looking Indian names within a class; Student ID as above; email `<StudentID>@attendx.edu.in`; phone 10 digits starting 6-9; joining date by semester (sem 1 -> 2026-08-01, 3 -> 2025-08-01, 5 -> 2024-08-01); Identity user with role Student for each (QRPath = "on-demand").
- **Faculty:** 9 per branch (`FAC-<BRANCH>-001..009`, 001 = HOD, Professor) = **126 faculty**; unique `first.last@attendx.edu.in`; Identity users with roles Faculty (+HOD for 001). **Each teaching faculty (002-009) teaches exactly 3 subjects**: subject number *i* of semesters 1, 3 and 5, to both sections.
- **Timetable (conflict-free):** per class 8 subjects x 5 periods = 40 of 42 slots. Construction: for class index c (0-5 per branch), faculty index f (0-7), repetition r (0-4): `slot = (8*r + f + 7*c) mod 42`, day = slot // 7 + 1, period = slot % 7 + 1 (this is a latin-rectangle: no class and no faculty clash). Rooms `Block A-101`/`Lab-2`. **A Python verification script must assert:** 0 class clashes, 0 faculty clashes, each faculty <= 3 subjects, every class has exactly 30 students. FacultySubjectMapping rows are derived from the timetable.
- **Attendance history:** last 14 days (config), skipping Sundays and holidays, deterministic `Random(2026)`: each student gets a propensity (20% low 55-72%, 60% mid 78-90%, 20% high 92-98%, ~8% declining recently), 6% of presents are Late. About 200,000 rows inserted with SqlBulkCopy (sessions via EF in batches of 1,000).
- **Holidays:** fixed-date sample holidays (Independence Day, Gandhi Jayanti, Christmas, Uttarayan, Republic Day) plus an exam and an event. **Notices:** three sample notices (all, students, faculty).
- **Excel master file:** sheets README, Admin, Faculty, Students, Subjects, Timetable, Holidays, Logins. The app also writes `App_Data/Exports/AttendX_Master_Data.xlsx` after seeding and Admin -> Backup and Export -> "Download Master Data (Excel)" regenerates it (passwords are never stored readable; the column shows the default seed password).
- Self-check logged after seeding: classes without exactly 30 students (must be 0), students without login (0), counts.
- Photos: initials avatars (no external face services); Admin can upload real photos (validated).

## 8. BUSINESS RULES (services)
- **Manual attendance:** only the assigned faculty, the same calendar day, inside `period start - grace` .. `period end + grace` (grace default 10 min, server clock); students must belong to the class; save all or fail; duplicates impossible (unique index + checks + concurrency catch); Admin/HOD may correct after the window **with a mandatory reason**; every change writes an audit row (old/new value, actor, reason) in the same transaction.
- **Sunday / holiday:** `ICalendarService` returns the reason; attendance is blocked and "today's classes" is empty with a clear message.
- **QR attendance:** student QR payload `AX1.{id}.{studentCode}.{signature}` (HMAC-SHA256 with server secret). Faculty scans on laptop/phone camera. The scan request carries a **rotating session token** (default 30 s window, current + previous accepted). Result codes: Marked, AlreadyMarked, InvalidQr, WrongClass, OutsideTime, Unauthorized, SessionExpired, LocationRequired. Scan after (period start + LateAfterMinutes) is stored as Late.
- **Offline:** browser queue in localStorage; on reconnect `/Attendance/Sync` re-validates every record (faculty, class, captured time inside the window, sync within the allowed delay, student, duplicates).
- **Thresholds:** default 75% plus rules by branch/semester/section (most specific wins). Percentage = (Present + Late x LateWeight) / Total. Risk: Safe / Watch / At Risk / Critical. Classes required = ceil((T x total - 100 x effective present) / (100 - T)).
- **Timetable rules:** no faculty or section double-booking (section check includes semester), subject must match branch + semester, faculty must be mapped (auto-map option), times come from period settings.
- **Attendance queries:** student raises a query (last 30 days, subject in own timetable); faculty/HOD/admin answer (Resolve/Reject with response); audited.
- **Student creation** (Admin) creates the Identity login too (default password, must change at first login); deactivating a student/faculty locks the login; email changes sync to the login.
- Reports: section, subject, student (by subject), faculty activity, daily, monthly, date-range, low attendance, defaulters - one `ReportTable` feeds the page, Excel (ClosedXML), PDF (QuestPDF) and CSV. Attendance summary: low-attendance list with trend text, subject summary, section comparison (z-score), weekly chart (statistics only, no external services).
- Backup: SQL `BACKUP DATABASE ... WITH CHECKSUM`; restore does `RESTORE VERIFYONLY`, then single-user restore with typed confirmation; only `.bak` files from the backup folder (no path traversal).

## 9. PAGES BY ROLE
- **Admin/HOD:** Dashboard, Today's Classes, Corrections, Timetable (grid + editor), Student Queries, Reports, Attendance Summary, Notices (board + manage), Students (list with class strength, details, create/edit/deactivate, photo/webcam, QR card, CSV import), Faculty (list/form/CSV import in one transaction), Faculty - Subject, Subjects, Branches, Sections, Holidays, Audit Log, Backup and Export, Settings (rules, periods, thresholds, academic years), Change Password.
- **Faculty:** Dashboard (details, current/next class with Mark Attendance and Scan QR, tiles, today's schedule, classes taught, low attendance list, notices, weekly timetable), Today's Classes, Mark Attendance (all 30 students), Scan QR, Timetable, Reports/Summary (own classes), Student Queries, Notices.
- **Student:** one Dashboard: student details + QR, attendance summary tiles, subject-wise table, today's classes, 5-week calendar, weekly timetable, recent attendance, my faculty, notices, holidays, attendance query, PDF report, phone update, recent sign-ins; Notices; Change Password.

## 10. INTERFACE - INSTITUTIONAL PORTAL DESIGN SYSTEM
Flat, structured, data-first. **No gradients, glass, glow, floating shapes, per-role colour themes, dark mode, emojis, count-up or page animations.**
**Colours (CSS variables in one file `wwwroot/css/portal.css`):** navy `#14305A` (header, sidebar, table headers, primary buttons), navy-dark `#0E2442`, blue `#1F5FA8` (links/focus), accent gold `#B8892D` (3px line under the header only), background `#F2F4F7`, surface `#FFFFFF`, border `#D5DBE3`, text `#1B2430`, muted `#5E6A7A`; status: success `#1E7F4F`/`#E7F4EC`, warning `#9A6B00`/`#FDF3DC`, danger `#B42318`/`#FCE9E7`, info `#1D5FA8`/`#E6F0FB`. Same palette for all roles.
**Type:** Inter 400/500/600 only (fallback Segoe UI, Arial), tabular numbers. Base 14px; table body 13px; table header 12px uppercase +0.04em/600; small 12px; page title 22px/600; panel title 15-16px/600; KPI number 26px/600. Radius 4px; 1px borders; shadow only `0 1px 2px rgba(16,24,40,.06)`; controls 36px high (44px on phones). Motion: 150 ms colour transitions only; respect `prefers-reduced-motion`.
**Shell:** fixed navy header (56px: menu button, logo mark, institution name + "Attendance Management System", notification bell with count, user name + role, Sign out) with a 3px gold line; 240px navy sidebar with group labels (Attendance, Reports, Academic Data, System) and a gold left bar on the active item; context bar (breadcrumb "Home / Page" on the left, "Academic Year 2026-27" and the date on the right); content max-width 1480px; footer "(c) <year> <Institution Name>. All rights reserved." Institution name/short name/system name come from `appsettings.json` -> `Institution`.
**Login:** `/` shows three plain bordered tiles (Administrator, Faculty / HOD, Student) under a header strip. Login screens = one centred 400px white panel (title, fields, Show/Hide password, Remember me, Login button, "Forgot password?" one-liner, text links to the other logins). No illustrations, taglines, feature lists or demo buttons.
**Components:** panels with a header row; KPI tiles (label above number, no icons); definition lists for details; tables with sticky navy header, hover tint, right-aligned numbers, status chips (text + muted tint), thin progress bars in % cells; filter bar above lists; pagination; flat Chart.js charts (navy/blue/grey, dashed red threshold line, no animation); toasts bottom-right; confirm via native confirm; calendar grid for the student's last 5 weeks; week timetable grid with today's column highlighted.
**Empty states:** one reusable partial with a 40px line icon, a one-line title and one line of text (Sunday: "No classes today / Today is Sunday."; holiday: "Holiday: <name>."; no attendance: "No attendance recorded / Records appear after the first class."; no timetable; no students; no notices; no queries; no results; no students below minimum; offline; camera blocked).
**Copy rules:** short, neutral, institutional; Title Case for titles and buttons; dates `dd MMM yyyy`, times 24 h. **Banned:** slogans, "smart", "AI", "insights", "powered by", "welcome back", exclamation marks, emojis, motivational sentences, feature bullets on login pages, demo/sample credentials in the UI, long explanations under titles.
**Pages follow patterns:** list pages (title + action, filter bar, table, pagination, class strength line for student lists), form pages (single panel, labels above, two columns on desktop, action row at the bottom), detail pages (definition list + tables), Mark Attendance (header panel, toolbar with All Present / All Absent / Reset and live counters, table with a 3-option Present / Absent / Late control, sticky bottom Save bar), QR Scan (camera panel with Start/Stop + result line + scan log), reports (filter panel, table with totals, Excel / PDF / CSV / Print buttons).

## 11. RESPONSIVE AND ACCESSIBILITY (must be perfect on every device)
- Breakpoints 360 / 576 / 768 / 992 / 1200. Desktop: fixed sidebar (collapsible). Below 992px: sidebar becomes an off-canvas drawer with backdrop, plus a fixed bottom bar (Home, Today, Timetable, Menu for staff; Home, Notices, Report, Menu for students) respecting the iOS safe area.
- Below 768px: controls 44px high, inputs 16px (no iOS zoom), definition lists collapse to a single column, tables scroll horizontally inside their panel, KPI tiles 2 per row, the sticky Save bar's button is full width, role tiles stack.
- No horizontal page scroll at 360px; test at 360x640, 390x844, 768x1024 and 1440x900. Printing: reports and ID card print cleanly (hide header, sidebar, filters).
- WCAG AA contrast, visible focus outline, labels on every input, `aria-live` for toasts, skip-to-content link, status never by colour alone, tap targets >= 44px on phones.

## 12. SECURITY
Identity password hashing, lockout, antiforgery, role + resource-level authorization (department / own classes / own data), EF parameterised queries, Razor encoding, upload validation (extension, content type, magic bytes, size, random file name), no secrets or passwords in logs, audit trail for every sensitive action, security headers, `QrSecret` in config (documented as "change in production"), HTTPS redirection.

## 13. TESTS (xUnit, must pass)
Year mapping, percentage / risk / classes-required maths, threshold resolution, time window (open, grace, closed, other day), Student ID sequence, QR tamper + replay + token window, CSV parser, calendar service (Sunday, holiday, event), attendance service (assigned faculty marks, other faculty refused, outside window refused, holiday refused, admin correction needs reason and is audited, duplicate scan, wrong class, expired token, late scan, offline re-validation).

## 14. VERIFICATION (include the output in the final report)
`dotnet build` (0/0), `dotnet test`, then run the app and script a smoke test: for admin, one faculty and one student - GET login page (200), POST login with the antiforgery token (302), GET the dashboard (200), GET one data page (200); confirm a student is refused on `/Students` and `/Reports`; confirm a wrong Faculty ID is rejected. Report seed counts (students, faculty, classes, timetable rows, attendance rows) and the first-run seeding time. Run the Python dataset verification (0 clashes, 30 per class).

## 15. DELIVERABLES
Zip with: solution, `AttendX` web project, `AttendX.Tests`, `Dataset` (CSV + xlsx + demo_logins.md), README (install steps for Visual Studio 2026 workloads "ASP.NET and web development" + "Data storage and processing" (LocalDB), run steps, logins table, 10-minute demo script, troubleshooting, configuration, honest limitations), `docs/` (project documentation, viva questions, presentation outline, UI guide, dataset generator script).

## 16. PHASE PROMPTS (paste one at a time if needed)
**Phase 1 - Foundation:** "Create the solution, projects, packages, GlobalUsings, entities, AppDbContext (restrict deletes, filtered unique indexes, Attendance status as text), Identity with the four roles, Program.cs, appsettings.json (AttendX and Institution sections), policies. Build gate."
**Phase 2 - Services and rules:** "Implement IClock, AttendanceMath, ThresholdResolver, AttendanceWindow, StudentIdService, QrSigner/QrService, PhotoService, Settings/Audit/Calendar/Notice services, StudentService, FacultyService, TimetableService, AttendanceService (manual, QR, offline sync, corrections), ReportService, ExportService, AnalyticsService, DashboardService, StudentPortalService, BackupService, MasterDataExportService, with xUnit tests. Build + test gate."
**Phase 3 - Dataset and seeder:** "Write the Python generator and verifier (84 classes x 30 students, 126 faculty, latin-rectangle timetable, holidays, xlsx, demo_logins.md), then DbSeeder with fast paths (single password hash, batched Identity inserts, SqlBulkCopy history), self-check logging. Run the app and show the seed counts and timing."
**Phase 4 - Login and controllers:** "Implement the three login screens' controllers (`/admin/login`, `/faculty/login`, `/student/login`, `/account/signin` with the ID + email + password rule, generic errors, lockout, audit), landing, change password, and all controllers for every module and role with correct authorization scoping. Build gate."
**Phase 5 - Portal interface:** "Create `portal.css` and the layouts (header, sidebar, context bar, footer, bottom bar), role-selection and login views, empty-state partial, and the Admin/HOD, Faculty and Student dashboards following section 10. Build gate."
**Phase 6 - Data pages and attendance screens:** "Create all list/form/detail views, the timetable grid, Mark Attendance, QR Scan (with the JavaScript for session token rotation, scan log, offline queue), reports, attendance summary, notices, holidays, queries, audit, backup, settings, and error pages in the same design. Keep every JS hook working."
**Phase 7 - Responsive and copy sweep:** "Test and fix layouts at 360, 390, 768, 1024 and 1440 px; verify the drawer, bottom bar, tables, forms, Mark Attendance and QR screens on phone sizes; search the codebase and remove banned words, emojis, gradients and animations; accessibility pass."
**Phase 8 - Docs and delivery:** "Write README, documentation, viva questions, UI guide; run the full verification (section 14); create the zip; report build/test/seed/smoke results and honest limitations."

START NOW. Work phase by phase, show the gate output after each phase, and do not stop until the final verification passes.
