# AttendX — Attendance & Academic Management System

ASP.NET Core 10 MVC + EF Core + Npgsql (Neon PostgreSQL) + ASP.NET Core Identity + Hangfire + Serilog.
Built for **PVG College of Engineering & Technology (PVGCOET), Pune** — a **4-year B.Tech** program (8 semesters).

## Features
- 4 role logins: **Admin** (`/admin/login`), **HOD / Faculty** (`/faculty/login`), **Student** (`/student/login`), **Parent** (`/parent/login`).
- Institution crest header, clean sidebar, responsive for phone/tablet/desktop.
- Manual + QR attendance (rotating token), offline sync (IndexedDB), corrections workflow (Admin/HOD only).
- **Exams module**: Mid-Sem 1 (3rd week Sep), Mid-Sem 2 (3rd week Nov), End-Semester. Faculty enter marks (Internal 30 + External 70), **lock** after external exam; modification requires HOD/Admin approval + allotted window.
- **Leave module**: Medical / Casual / On-Duty / Maternity / Paternity, balance tracking, HOD/Admin approval.
- **Parent portal**: view ward's attendance, exam results, apply leave on ward's behalf.
- **Notifications**: absence alerts (email/SMS), low-attendance warnings (SendGrid + Twilio).
- **7 periods/day**, Lecture = 60 min, Lab = 80 min (1 h 20 m), 09:00–17:20 with lunch break.
- **Conflict-free timetable** for both semesters (Aug–Dec odd, Jan–May even); summer vacation Jun–Jul; 1-week odd-sem break.
- **105 days of attendance history** seeded (from Aug 1 2026), ~60,000 exam marks for completed exams.
- Reports (Excel/PDF/CSV), Analytics, Audit log, Backup/Restore, Attendance summary.

## Demo logins (development passwords — change in production)
| Role | Institution ID | Email | Password |
|---|---|---|---|
| Admin | - | `admin@attendx.edu.in` | `Admin@123` |
| HOD (CSE) | `FAC-CSE-001` | `ishaan.chauhan@attendx.edu.in` | `Faculty@123` |
| Faculty | `FAC-CSE-002` | `khushi.desai@attendx.edu.in` | `Faculty@123` |
| Student | `1EV1-CSE-001` | `tanvi.dodiya.1EV1-CSE-001@attendx.edu.in` | `Student@123` |
| Parent | `1EV1-CSE-001` (ward's ID) | `parent.1EV1-CSE-001@attendx.edu.in` | `Parent@123` |

See `Dataset/demo_logins.md` for the full list.

## Quick start (local)
```bash
# 1. PostgreSQL running on localhost:5432 (or use docker compose)
docker compose up -d db

# 2. Restore + build
dotnet restore AttendX.sln
dotnet build AttendX.sln

# 3. Apply migrations and seed (first run takes ~4 min)
dotnet run --project AttendX/AttendX.csproj
```
First run drops/creates `AttendXDb` and seeds: 14 branches, 28 sections (2 per branch), 896 subjects, 126 faculty, 2,520 students, 2,520 parents, ~2,000 exam timetable slots, ~60,000 exam marks, 105 days of attendance.

## Neon (cloud PostgreSQL) deployment
1. Create a Neon project → copy the pooled connection string.
2. Set `ConnectionStrings__DefaultConnection` to the Neon string (no `SSL Mode=Require` needed — Npgsql handles it via `SSL Mode`).
3. `ASPNETCORE_ENVIRONMENT=Production` and `AttendX__UseMigrations=true`.
4. Deploy to Render (see `render.yaml`) — it auto-runs `dotnet ef database update` via `UseMigrations` on startup.

## Build / test
```bash
dotnet build
dotnet test
```

## Project structure
```
AttendX_Solution/
  AttendX/            Models/ Data/ (AppDbContext, DbSeeder, Migrations/) Services/ Controllers/ ViewModels/ Views/ wwwroot/ Dataset/
  AttendX.Tests/      xUnit tests
  docs/               PRD.md, MEMORY.md (AI context), attendance-data-request.md
  .github/workflows/  ci.yml
  Dockerfile          multi-stage build
  docker-compose.yml  Postgres + app for local dev
  render.yaml         Render deployment descriptor
```

## Environment variables
| Variable | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | Neon / Postgres connection string |
| `AttendX__UseMigrations` | `true` to apply EF migrations on startup (default in prod) |
| `AttendX__SeedDemoData` | `true` to seed demo data (default in dev) |
| `AttendX__SeedHistoryDays` | Days of attendance to seed (default 105) |
| `Institution__Name` / `ShortName` / `SystemName` | Institution branding |
| `Auth__SeedAdminPassword` / `SeedFacultyPassword` / `SeedStudentPassword` / `SeedParentPassword` | Seed credentials |
