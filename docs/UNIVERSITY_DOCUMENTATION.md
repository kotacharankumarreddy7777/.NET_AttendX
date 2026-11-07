# AttendX - Project Documentation (Marwadi University, B.Tech CE/CSE)

## 1. Abstract
AttendX is a web-based attendance management system for a B.Tech institute. It replaces paper registers with role-based digital attendance
(Admin, HOD, Faculty), supports manual and QR-based marking, enforces time-window and duplicate rules on the server,
works with an offline queue, produces Excel/PDF/CSV reports, and gives early-warning analytics for low-attendance students.

## 2. Problem statement
Paper attendance is slow, easy to forge (proxy attendance), hard to audit and impossible to analyse in time. Institutions also need
department-wise control (HOD), thresholds (e.g. 75%), and evidence of who changed what.

## 3. Objectives
1. Secure role-based login (Admin, HOD, Faculty). 2. Master data: branches, sections, subjects, faculty, students, timetable.
3. Attendance only for the right faculty, class and time. 4. Signed QR identity cards. 5. Audit trail for every sensitive action.
6. Reports/exports. 7. Analytics (risk, trend, projection, anomalies). 8. Backup/restore. 9. Responsive UI.

## 4. Technology
ASP.NET Core MVC (.NET 10), C#, Entity Framework Core 10, SQL Server LocalDB, ASP.NET Core Identity, Bootstrap 5, Chart.js, DataTables,
html5-qrcode, QRCoder, ClosedXML (Excel), QuestPDF (PDF), xUnit.

## 5. Architecture
Layered MVC: **Controllers** (HTTP only) -> **Services** (all business rules; reusable by a future REST API) -> **EF Core DbContext** -> SQL Server.
ViewModels separate UI from entities. Cross-cutting: Identity cookie auth, authorization policies (AdminOnly, AdminOrHod, AnyStaff),
global antiforgery filter, force-password-change filter, audit service, injectable clock (server time).

## 6. Database (main tables)
AspNetUsers/Roles (Identity) - AcademicYears - Branches - Sections - Subjects - Students - Faculty - FacultySubjectMappings -
PeriodDefinitions - Timetables - AttendanceSessions - Attendance - AttendanceSettings - ThresholdRules - AuditLogs - BackupHistories.
Key constraints: unique active StudentID; unique (Student, Date, Period) in Attendance; unique (Section|Faculty, Day, Period) per academic year in Timetable;
unique (Timetable, Date) in AttendanceSession; all relationships `Restrict` (soft-delete used, history never lost).

## 7. Modules
Authentication - Master data - Student (ID generation, photo, QR) - Faculty (identity creation, CSV import) - Timetable (conflict validation) -
Attendance (manual, QR, correction, offline sync) - Reports - Analytics - Audit - Settings - Backup.

## 8. Key algorithms
* **Student ID**: prefix `[Sem][Section]-[Branch]-`, next = highest active sequence + 1, retry on unique-index conflict.
* **Attendance %** = (Present + Late x LateWeight) / Total x 100. **Risk** compares % with the resolved threshold (Safe / Watch / At Risk / Critical).
* **Classes needed**: x >= (T x total - 100 x present) / (100 - T).
* **QR security**: HMAC-SHA256 signature of the student payload; session token = HMAC(sessionId, time-window), valid for the current and previous window only.
* **Trend**: least-squares slope of weekly attendance; 4-week projection; section anomalies via z-score and 2-week drop.

## 9. Security
Password hashing (Identity), lockout after 5 failures, CSRF tokens, role + resource-level authorization, parameterised EF queries (no SQL injection),
HTML encoding by Razor (XSS), upload validation (extension, content type, magic bytes, size, random file name), no secrets in logs, audit log without passwords, security headers.

## 10. Testing
xUnit: year mapping, percentage/risk maths, thresholds, time window, Student ID sequence, QR tamper/replay, CSV parser, and service-level scenarios
(allowed/forbidden marking, duplicate scan, wrong class, expired token, correction with reason, offline re-validation) using the EF InMemory provider.
Manual test checklist: see README demo script.

## 11. Limitations and future scope
Mobile app / REST API reuse of the service layer, student self-service portal, SMS/e-mail alerts for defaulters, face/geo-fence verification,
real ML models trained on multi-year data, EF migrations pipeline, Docker deployment.

## 12. Conclusion
AttendX shows a complete, secure, auditable attendance workflow with real-time rule enforcement and actionable analytics, built with mainstream .NET technology.
