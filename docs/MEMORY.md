# AttendX — AI Memory File

**Purpose:** Context for AI assistants working on this codebase. Read this first.

## Stack
- ASP.NET Core 10 MVC, EF Core 10, **Npgsql** (PostgreSQL / Neon), ASP.NET Core Identity, Serilog, Hangfire, Chart.js, Bootstrap 5.3.
- Tests: xUnit (`AttendX.Tests`).
- No frontend framework — Razor Views + jQuery + vanilla JS.

## Key conventions
- Services in `Services/` (interfaces `IXxxService`), controllers in `Controllers/`, view models in `ViewModels/`.
- `AppDbContext` in `Data/AppDbContext.cs` — all DbSets + Fluent API config.
- Seeding: `Data/DbSeeder.cs` — idempotent, reads CSVs from `Dataset/`.
- Migrations: `Data/Migrations/` — **never edit applied migrations**; add new ones via `dotnet ef migrations add`.
- Auth policies in `Program.cs` (`PolicyNames.AdminOnly`, `AdminOrHod`, `AnyStaff`, `StudentOnly`, `ParentOnly`, `StudentOrParent`).
- Layout: `Views/Shared/_Layout.cshtml` (sidebar + topbar + tab bar). Auth pages: `_AuthLayout.cshtml`.
- CSS: single `wwwroot/css/portal.css` with CSS variables. No preprocessor.
- JS: `site.js` (toast, shortcuts, BroadcastChannel), `dashboard.js` (Chart.js), `attendance.js` (mark), `qr-scanner.js`, `offline-sync.js`.

## Critical: do NOT
- Do **not** reintroduce `EnsureCreated()` — always use `Database.MigrateAsync()`.
- Do **not** add a dark-mode toggle — institution explicitly rejected it.
- Do **not** store secrets in `appsettings.json` — use env vars / user-secrets.
- Do **not** change seeded passwords without updating `demo_logins.md` + `Constants.cs` (`SeedStudentPassword`, `SeedFacultyPassword`, `SeedParentPassword`).
- Do **not** add unique indexes that prevent legitimate multi-tenancy (e.g. same faculty invigilating two subjects same day).

## Data model highlights
- `Student` ↔ `ApplicationUser` 1:1 (via `IdentityUserId`). Same for `Faculty`, `Parent` (Parent → Student FK `StudentId`).
- `ExamMark` has `LockStatus` (Draft/Submitted/Locked) + `ModificationQuery*` fields for the HOD/Admin-approved edit flow.
- `PeriodDefinition` holds 7 periods (seeded). Admin can change via Settings (`PeriodCount`).
- `Semester` entity tracks academic calendar (Aug–Dec / Jan–May). `AcademicYear` holds June–May range.
- `NotificationLog` stores all outbound email/SMS.

## Seed flow (`DbSeeder.SeedAsync`)
1. `MigrateAsync()` (or schema-ensure if `UseMigrations=false`).
2. Roles (Admin, HOD, Faculty, Student, Parent).
3. Settings, Periods, Branches, Sections (2 per branch), Admin user.
4. Subjects (from CSV).
5. Semesters (8), Academic calendar holidays.
6. Student ID auto-heal.
7. Holidays, LeaveTypes, Faculty (from CSV), Students (from CSV), **Parents** (auto-created), LeaveBalances.
8. Timetable (from CSV), Notices.
9. Exams (6: Mid-1, Mid-2, End-Sem × odd/even), ExamTimetables (round-robin faculty + room dedup per date), ExamRegistrations, ExamMarks (completed exams only).
10. Attendance history (`SeedHistoryDays`, default 105).

## Common tasks
- **Add a new module:** Models → `DbSet` in `AppDbContext` → `dbcontext` config (indexes/constraints) → migration → `Services/IXxxService` → `Controllers/XxxController` → views → register in `Program.cs` → `_Layout.cshtml` nav → seed if demo data.
- **Reset dev DB:** `dotnet ef database drop -f` then `dotnet run` (auto re-migrates + seeds).
- **Add a column to an existing table:** add property to model → `dotnet ef migrations add AddXxx` → `dotnet ef database update`.
- **Neon deploy:** set `ConnectionStrings__DefaultConnection` to Neon pooled string; `ASPNETCORE_ENVIRONMENT=Production`; `AttendX__UseMigrations=true`; `AttendX__SeedDemoData=false` for real data.

## Known gotchas
- Razor views are compiled at build time — a syntax error in `.cshtml` breaks `dotnet build`.
- `Student.Semester` is int (1–8), not enum.
- `AttendanceStatus` is stored as **string** in Postgres (`nvarchar`) — case-sensitive. Use exact names (`Present`, `Late`, `Absent`, `Leave`).
- Chart.js `#chartData` is a `<script type="application/json">` block parsed by `dashboard.js` — must be valid JSON (use `Json.Serialize`).
- Offline QR scans cannot verify the rotating token; server always re-validates.

## Files to check first
1. `Program.cs` — DI registrations, middleware pipeline.
2. `Data/DbSeeder.cs` — what data exists.
3. `Views/Shared/_Layout.cshtml` — navigation structure.
4. `wwwroot/css/portal.css` — theme variables.
5. `AttendX.csproj` — package versions.
