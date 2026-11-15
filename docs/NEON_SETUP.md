# AttendX — Neon PostgreSQL Setup Guide

## 1. Create the Neon project
1. Sign in at <https://neon.tech> → **New Project** → name it `AttendX`.
2. Copy the **pooled** connection string (looks like `postgres://user:pass@ep-xxx-pooler.region.aws.neon.tech/AttendXDb`).
   - Use the **pooled** endpoint for the app; the **direct** endpoint only for `dotnet ef` from your machine.
3. In Neon console → **Connection Details**, note the role password.

## 2. Local migrations (run once)
```bash
# from the repo root
dotnet tool restore
dotnet ef database update --project AttendX/AttendX.csproj --startup-project AttendX/AttendX.csproj \
  --connection "Host=ep-xxx-pooler.region.aws.neon.tech;Database=AttendXDb;Username=user;Password=pass;SSL Mode=Require"
```
This creates all tables + applies the 8 migrations (InitialCreate, ExamModule, LeaveParentModule, ExamMarkLockAndSemestersAndNotificationLog, ExamUniqueIndexFix, ExamTimetableIndexFix, ExamTimetableRoomIndexNonUnique, TimetableUniqueIndexSemester).

## 3. First seed (demo data)
For a first demo deploy, seed the demo dataset:
```bash
AttendX__SeedDemoData=true
AttendX__SeedHistoryDays=30
```
Run the app once (locally against Neon, or via Render shell) so `DbSeeder.SeedAsync` runs. It takes ~4–5 minutes for 6,720 students. Then set `AttendX__SeedDemoData=false` for subsequent deploys so the seed never re-runs.

## 4. Render environment variables
In the Render dashboard → Service → Environment, set:
| Key | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | Neon pooled string (no `SSL Mode` needed; Npgsql handles it) |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `AttendX__UseMigrations` | `true` |
| `AttendX__SeedDemoData` | `false` (set `true` only for the very first deploy) |
| `AttendX__SeedHistoryDays` | `30` |
| `Auth__SeedAdminPassword` | strong admin password |
| `Auth__SeedFacultyPassword` | strong faculty password |
| `Auth__SeedStudentPassword` | strong student password |
| `Auth__SeedParentPassword` | strong parent password |
| `Institution__Name` | `PVG College of Engineering & Technology` |
| `Institution__ShortName` | `PVGCOET` |
| `Institution__Latitude` / `Institution__Longitude` | campus GPS (default 18.5204, 73.8567) |
| `Notification__Provider` | `Smtp` or `SendGrid` (leave `Log` for no email) |
| `Notification__SendGrid__ApiKey` | SendGrid key (optional) |
| `Notification__Twilio__AccountSid` / `AuthToken` / `FromNumber` | Twilio (optional) |

## 5. Health check
Render health check → path `/healthz` (already registered via `MapHealthChecks`). It returns 200 when the DB is reachable.

## 6. Branching / restore
Neon branches are full DB copies. To test a schema change: create a branch, point a local appsettings at it, run `dotnet ef database update`, verify, then merge the migration into main and re-deploy.

## 7. Backups
- Neon auto-branches + point-in-time restore (console → Branches → Restore).
- In-app: Admin → Settings → Backup. On Neon, `pg_dump` is usually unavailable, so the service falls back to a JSON export of master data (see `BackupService.CreateAsync`).
