# AttendX Complete Overhaul Plan

## Current State (verified)
- ASP.NET Core 10 MVC + EF Core 10 + Npgsql (Neon-ready). Identity auth with cookie.
- Migrations exist (InitialCreate, ExamModule, LeaveParentModule) — UseMigrations=true in appsettings.
- Hangfire, Serilog, HealthChecks, Swagger-less (no Swagger yet), NotificationService (stub), LeaveService, ExamService, ParentController already added.
- Dataset: 2,520 students, 126 faculty, 896 subjects, 3,360 timetable rows, 22 rooms, 14 branches, 28 sections (2 per branch, 5-6 periods).
- Login: 3 role screens (admin/faculty/student). Passwords: `Admin@123`, `Faculty@123`, `Student@1234`. User wants `Faculty@123` and `Student@123` (note: user wrote `Faulty@123` and `Student@123`; plan uses `Faculty@123` and `Student@123`).
- Dashboard has 5 charts (branch/group, semester, daily, monthly, subject) — some flagged as unwanted by user.
- Frontend: portal.css (~495 lines) has dark mode, toasts, skeletons, animations. _Layout.cshtml has theme toggle button (user wants NO dark mode toggle).
- No API controllers, no SignalR, no Leave/Parent views, no exam timetable in student/faculty portals, no class-by-period cards in admin.

## Goals (from user message)
1. Make all logins work perfectly (admin/faculty/student) with auto-generated email + passwords `Faculty@123` / `Student@123`.
2. Add cookies (remember me) — cookie already configured; verify and enhance.
3. Add memory files / PRD file (docs) to the codebase.
4. Keep existing real-world data; modify/add as needed (don't strip).
5. Exam timetable, completed exams, upcoming exams, exam results for students and faculty.
6. Faculty can mark marks and modify after locking by raising query to HOD/Admin.
7. B.Tech 4 years (8 sem) + 3-year for some others (real Indian scenario). User says "all branches 4 years" for this college — plan uses 4 years for all 14 branches, 8 semesters.
8. Frontend keep-data/update-data plan for all above.
9. Database ready for Neon seeding (already Npgsql + migrations + DbSeeder).
10. Deploy later to GitHub/Render — build now.
11. Add exam timetable + completed + upcoming + results for students and faculty.
12. Admin "Today's Classes" page: 7 period cards, each card shows all classes of that period (no overlap), faculty name + room number, rooms don't overlap. Min 5-6 periods per section, 14 branches × 5-6 sections (random), each section 25-30 students (random).
13. College Aug–May. Odd sem Aug–Dec, even sem Jan–May. After odd sem 1-week holiday. Full academic timetable.
14. Attendance from college start. Mid-1 completed 3rd week of Sep (all semesters). Mid-2 3rd week of Nov. End sem end of Dec. Even sem starts Jan.
15. Exam data + marks for completed exams for all.
16. Admin can access anything. Faculty can give/modify marks (with lock + query flow). Student sees upcoming timetable + results.
17. Smooth, responsive, no complaints across devices.
18. Wow UX: toast system, skeleton, dark mode (user says remove toggle — so NO dark mode), micro-interactions, keyboard shortcuts, empty states, animated charts, QR scanner UX, swipe-to-mark, data tables, file uploads, print styling, offline indicators, parent portal, leave mgmt, notifications, exam gate, REST API + Swagger, geo-fencing, scheduled jobs, EF migrations, Serilog, health checks, Testcontainers, CI/CD.
19. Sidebar & navbar perfect (institution style), remove clutter. Top navbar with institution crest, live notification dropdown, NO dark mode toggle.
20. Background animations, smooth animations everywhere.
21. Graphs: semester-wise, remove unwanted. Admin can search specific student/faculty by ID + auto-generated email. Passwords `Faculty@123` / `Student@123`. Changes appear in all portals.
22. Admin dashboard: remove unwanted graphs, add animated pie/bar/doughnut, branch-wise, best colors, real-world data.
23. B.Tech 4 years = 8 sem (4 odd + 4 even). All branches 4 years.
24. Classrooms: no overlapping at same time. Exam centers too.
25. College Aug–May. Odd sem Aug–Dec, even sem Jan–May. After odd sem 1-week holiday. June–July summer vacation. Full academic timetable.
26. Timetable: lecture 1 hour, lab 1h20m.
27. Admin Today's Classes: 7 cards (periods), admin can change period count. Period 1 card shows all period-1 classes. No overlap. Faculty name + room + room number. Rooms don't overlap. Min 5-6 periods per section. 14 branches × 5-6 sections (random). Each section 25-30 students (random).
28. College Aug–May full year. Odd Aug–Dec, even Jan–May. After odd sem 1-week holiday. Full academic timetable all the app.
29. Attendance from college start. Mid-1 3rd week Sep (all sem). Mid-2 3rd week Nov. End sem end Dec. Even sem starts Jan. Data for all. Marks for completed exams.
30. Admin portal: access anything. Faculty: give marks after exam; modify after lock via query to HOD/Admin (any can allot time). Student: upcoming exams timetable + results.
31. App extreme smooth, highly responsive all devices.

## Plan Phases

### Phase 1 — Database & Data (Neon-ready)
- Verify migrations cover all entities (Exam, Leave, Parent, ExamMark). Add missing `ExamMark.LockStatus`, `ExamMark.LockQuery` fields for faculty lock/query flow.
- Update `DbSeeder`:
  - Seed `LeaveType` (Medical, Casual, On-Duty, Maternity).
  - Seed `Parent` for every student (auto-generate parent email `parent.<studentid>@attendx.edu.in`, password `Parent@123`).
  - Seed `Exam` records for all 8 semesters: Mid-Sem 1 (3rd week Sep 2026), Mid-Sem 2 (3rd week Nov 2026), End-Sem (Dec 2026), and even-sem Mid-1 (3rd week Feb 2027), Mid-2 (3rd week Mar 2027), End-Sem (May 2027).
  - Seed `ExamTimetable` for each exam/subject/section with faculty + room + date/time (no faculty/room clash).
  - Seed `ExamRegistration` for all students (eligibility based on attendance %).
  - Seed `ExamMark` for completed exams (Mid-1, Mid-2, End-Sem of odd sem 2026) with realistic marks (Internal 30, External 70, Grade).
  - Seed `LeaveBalance` per student per academic year.
  - Seed 8 semesters of students (already have 2520; verify 4-year mapping).
  - Seed timetable for even sem (Jan–May 2027) and odd sem (Aug–Dec 2026) with proper room allocation (no overlap per time slot, Block A/B/C 101-330 + Labs).
  - Seed holidays: Aug 15, Oct 2, Nov 1 (Diwali), Dec 25, Jan 14, Jan 26, plus 1-week odd-sem break (Dec 16–31 2026), summer Jun 1–Jul 31 2027.
- Keep `SeedStudentPassword` = `Student@123` (update from `Student@1234`), `SeedFacultyPassword` = `Faculty@123`.
- Update `demo_logins.md` accordingly.

### Phase 2 — Auth & Login
- Fix `AccountController.Signin` to accept `Student@123` (not `Student@1234`).
- Update `Lookup` to return auto-generated email for student/faculty.
- Add "Remember me" cookie persistence (already there; verify `RememberMe` honored).
- Add `Parent` login role (parent portal) — add `Parent` role to `RoleNames`, `PolicyNames.ParentOnly`, login screen `/parent/login`.
- Auto-generate parent email + password `Parent@123`.
- Ensure `MustChangePassword` flow works for all roles.
- Add password strength meter on `RoleLogin.cshtml`.

### Phase 3 — Academic Calendar & Timetable Engine
- Update `CalendarService` to handle semester date ranges:
  - Odd sem: Aug 1 – Dec 15 (classes), Dec 16–31 (end sem + break).
  - Even sem: Jan 10 – May 15 (classes), May 16–31 (end sem).
  - Summer: Jun 1 – Jul 31.
- Add `Semester` to `AcademicYear` or create `Semester` entity (start/end dates per semester).
- Update `PeriodDefinition` seed: 7 periods, Lecture 60 min, Lab 80 min (1h20m). Use admin-configurable `PeriodCount`.
- Update `TimetableService` to allocate rooms with no overlap:
  - Block A/B/C, floors 1-3, rooms 101-330 (30 rooms/floor × 3 floors × 3 blocks = 270 rooms).
  - Lab-1..Lab-6 per floor per block.
  - Validate room + faculty + section clash.
- Update `DbSeeder.SeedTimetableAsync` to generate conflict-free timetable for both semesters.

### Phase 4 — Exam Module (full)
- Add to `ExamMark`: `LockStatus` (Draft/Submitted/Locked), `LockQueryStatus` (None/Pending/Approved/Rejected), `LockQueryReason`, `LockQueryBy`, `LockQueryAt`, `LockApprovedBy`, `LockApprovedAt`, `ExternalMarksWindow` (start/end for external exam marks).
- Add `ExamService`:
  - `LockMarksAsync(examTimetableId)` — faculty locks marks after external exam.
  - `RaiseLockQueryAsync(examTimetableId, reason)` — faculty raises query to HOD/Admin.
  - `ReviewLockQueryAsync(queryId, approve, response)` — HOD/Admin approves/rejects, allots time window.
  - `ModifyMarksAsync(examTimetableId, studentId, internal, external, remarks)` — only within allotted window or by HOD/Admin.
  - `GetStudentExamResultsAsync(studentId)` — for student portal.
  - `GetExamTimetableForStudentAsync(studentId)` — upcoming exams.
  - `GetExamTimetableForFacultyAsync(facultyId)` — exam invigilator duties.
- Add `ExamController` actions: `StudentResults`, `StudentTimetable`, `FacultyTimetable`, `LockMarks`, `RaiseLockQuery`, `ReviewLockQuery`.
- Add views: `Exam/StudentTimetable.cshtml`, `Exam/StudentResults.cshtml`, `Exam/FacultyTimetable.cshtml`, `Exam/LockMarks.cshtml`, `Exam/LockQuery.cshtml`.
- Add `ExamTimetable` for each exam: date, session (FN/AN), room, invigilator. No room/faculty overlap.
- Seed completed exam marks for Mid-1, Mid-2, End-Sem (odd sem 2026) for all students.

### Phase 5 — Leave Module (full)
- Seed `LeaveType`: Medical (12 days), Casual (6), On-Duty (10), Maternity (90).
- Seed `LeaveBalance` for all students (current academic year).
- Add `LeaveController` actions for student apply, HOD/Admin review, balance view.
- Add views: `Leave/Index.cshtml` (student + admin), `Leave/Apply.cshtml`, `Leave/Review.cshtml`, `Leave/Balance.cshtml`.
- Add to student/faculty sidebar nav.

### Phase 6 — Parent Portal (full)
- Add `Parent` role, login screen `/parent/login`.
- Add `ParentController` with: dashboard (child attendance, %, leave, results), apply leave on behalf.
- Add views: `Parent/Index.cshtml`, `Parent/NoParent.cshtml`.
- Seed parent for every student.

### Phase 7 — Notification Service (real)
- Integrate SMTP (SendGrid) for email, Twilio for SMS.
- Add `NotificationService` triggers in `AttendanceService.SaveManualAsync` (absence alert) and `AnalyticsService` (low attendance).
- Add notification log table `NotificationLog` (to, type, subject, body, status, sentAt).
- Add admin view to see notification history.

### Phase 8 — REST API + Swagger
- Add `Microsoft.AspNetCore.Swagger` + `Swashbuckle` NuGet.
- Add `ApiController` base, `[ApiController]`, `[Route("api/[controller]")]`.
- Add `AttendanceApiController`, `StudentsApiController`, `ExamsApiController`, `AnalyticsApiController`.
- Add JWT or API key auth (reuse Identity cookie for now, add API key later).
- Add Swagger UI at `/swagger`.

### Phase 9 — SignalR Real-Time
- Add `Microsoft.AspNetCore.SignalR` (already in SDK).
- Add `AttendanceHub` (push live attendance to faculty dashboards).
- Add `NotificationHub` (push notifications to admin/HOD).
- Update `AttendanceService.SaveManualAsync` to notify hub.
- Add JS client to mark page + admin today's classes.

### Phase 10 — Frontend Wow UX (no dark mode)
- Remove dark mode toggle from `_Layout.cshtml`, `site.js`, `portal.css` (keep CSS vars but remove toggle).
- Update `_Layout.cshtml`: institution crest, live notification bell dropdown (SignalR), user avatar dropdown, clean sidebar.
- Update `portal.css`: modern toast system (already there), skeleton loaders (already there), micro-interactions (already there), print styles, modern form controls, animated charts styles.
- Update `dashboard.js`: animated charts (enable Chart.js animations), gradient fills, tooltips, brand colors.
- Update `qr-scanner.js`: camera permission UI, flash/torch toggle, front/rear camera switch, scan success sound/visual, scan history carousel.
- Update `attendance.js`: swipe-to-mark, bulk-select, undo, real-time sync (BroadcastChannel), visual confidence.
- Update `students.js`: drag-and-drop photo upload, preview, crop/rotate, compression.
- Update `site.js`: toast system (already there), keyboard shortcuts (already there), BroadcastChannel sync.
- Update `Dashboard/Admin.cshtml`: 7 period cards for Today's Classes, each card shows all classes of that period (no overlap), faculty name + room, animated pie/bar/doughnut charts (branch-wise, semester-wise, remove unwanted).
- Update `Dashboard/Faculty.cshtml`: animated charts, clean layout.
- Update `Student/Index.cshtml`: upcoming exams, results, animated charts, clean layout.
- Update `RoleLogin.cshtml`: password strength meter, social-login style buttons, background animation.
- Add background canvas animation on auth screens.
- Add `_EmptyState.cshtml` SVG illustrations per state.
- Responsive: test all breakpoints (mobile, tablet, desktop, 4K).

### Phase 11 — Admin Today's Classes (period-wise cards)
- Update `AttendanceService.GetSlotsAsync` to return slots grouped by period.
- Update `DashboardController` / `DashboardService` to provide `TodayClassesByPeriod` (7 periods, each with list of classes).
- Update `Views/Dashboard/Admin.cshtml`: 7 cards (one per period), each card lists all classes in that period (subject, faculty, room, section, branch). No overlap. Admin can change period count in Settings.
- Seed timetable to ensure no room/faculty/section overlap per time slot.
- Seed 14 branches × 5-6 sections (random) × 25-30 students (random).

### Phase 12 — Production Hardening
- Verify EF migrations (already there). Add `ExamMark.LockStatus` migration.
- Verify Serilog (already there).
- Verify HealthChecks (already there).
- Add Testcontainers integration tests (AttendanceService, ExamService).
- Add GitHub Actions CI (build, test, docker build).
- Add Docker Compose (SQL Server or Postgres for local).
- Add `Dockerfile` (already there), `docker-compose.yml` (already there).
- Add `render.yaml` for Render deployment (later).
- Add `README.md` with setup, Neon seeding, deployment instructions.
- Add `docs/PRD.md` (Product Requirements Document).
- Add `docs/MEMORY.md` (memory file for AI context).

### Phase 13 — Seed & Deploy Prep
- Update `DbSeeder` to seed everything (students, faculty, subjects, timetable, attendance, exams, marks, leaves, parents, holidays, notices).
- Verify `SeedAsync` runs cleanly on empty Neon DB.
- Add `appsettings.Production.json` template (Neon connection string placeholder).
- Test full seed locally before any deploy.

## Critical Files to Modify/Create
- Models: `Exams.cs` (add lock fields), `Leave.cs` (add leave balance), `People.cs` (parent).
- Services: `ExamService.cs`, `LeaveService.cs`, `NotificationService.cs`, `DashboardService.cs`, `AttendanceService.cs`, `TimetableService.cs`, `CalendarService.cs`, `StudentPortalService.cs`.
- Controllers: `AccountController.cs`, `ExamController.cs`, `LeaveController.cs`, `ParentController.cs`, `DashboardController.cs`, `AttendanceController.cs`, `StudentsController.cs`, `FacultyController.cs`.
- Views: `Shared/_Layout.cshtml`, `Dashboard/Admin.cshtml`, `Dashboard/Faculty.cshtml`, `Student/Index.cshtml`, `Exam/*.cshtml`, `Leave/*.cshtml`, `Parent/*.cshtml`, `Account/RoleLogin.cshtml`.
- JS: `site.js`, `dashboard.js`, `qr-scanner.js`, `attendance.js`, `students.js`.
- CSS: `portal.css`.
- Data: `DbSeeder.cs`, `AppDbContext.cs`, migrations.
- Config: `appsettings.json`, `Program.cs`.
- Docs: `docs/PRD.md`, `docs/MEMORY.md`, `README.md`.
- CI/CD: `.github/workflows/ci.yml`, `Dockerfile`, `docker-compose.yml`, `render.yaml`.

## Open Questions (to confirm with user)
1. Parent password: confirm `Parent@123` or another?
2. REST API auth: cookie or API key?
3. SMS provider: Twilio or AWS SNS? Email: SendGrid or SMTP?
4. Render deployment: web service + Neon Postgres?
5. Keep `Student@123` (not `Student@1234`)?

## Risk & Mitigation
- Large seed on Neon free tier: batch inserts, use `ChangeTracker.AutoDetectChangesEnabled = false`.
- Migration drift: use `dotnet ef migrations add` for new fields, test on empty DB.
- Frontend responsiveness: test on real devices (mobile, tablet).
- Exam marks lock flow: ensure HOD/Admin override works.

## Done Criteria
- All logins work (admin/faculty/student/parent) with correct passwords.
- All portals show exam timetable, results, attendance, leaves.
- Admin Today's Classes: 7 period cards, no overlap, faculty + room.
- Animated charts (branch-wise, semester-wise, pie/bar/doughnut).
- Smooth animations, responsive, no dark mode toggle.
- Neon-ready seed, migrations, CI/CD.
- PRD + memory files in repo.