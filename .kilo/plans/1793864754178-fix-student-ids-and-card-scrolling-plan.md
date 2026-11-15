# Plan: Fix Malformed Student IDs in Database & Card Scrolling UI Issues

## Objectives
1. **Student ID Correction**: Identify and fix malformed student IDs in the database (e.g. where semester/section prefix got duplicated like `33EV1-CSE-008` instead of `3EV1-CSE-008`) via a migration/fix script or seeder adjustment.
2. **Card Scrolling & Layout**: Fix scrolling and overflow issues in `.ax-card` components and container cards across the application (`portal.css` and views) so content overflows cleanly with proper scrollbars or responsive containment.

## Affected Boundaries
- **Database / Data Integrity**: `Students` table `StudentID` column, audit logs, attendance records referencing student IDs or foreign keys.
- **UI / CSS**: `AttendX/wwwroot/css/portal.css` (specifically `.ax-card`, `.card-b`, `.table-responsive`, and card container styles).
- **Services / Seeders**: `StudentIdService.cs`, `DbSeeder.cs`, CSV datasets if any malformed IDs exist there.

## Implementation Steps

### 1. Database Student ID Cleanup
- Create a safe SQL migration / database update snippet or startup routine in `DbSeeder.cs` (or migration) that queries students where `StudentID LIKE '33%'` or similar malformed patterns (e.g. starting with `33`, `55`, `77`, `11` where semester prefix is doubled) and corrects them (e.g. replacing leading double semester digit `33` with single digit `3`, or using a regex/string manipulation).
- Ensure attendance history and related foreign keys remain intact (since foreign key in `Attendance` links to `StudentId` (integer PK), updating `StudentID` string does not break FK relationships).

### 2. Card Scrolling & Layout Fixes (`portal.css`)
- Review `.ax-card` in `AttendX/wwwroot/css/portal.css`.
- Add robust overflow and scrolling handling to `.ax-card` and `.card-b` (e.g. `overflow-x: auto;` or `overflow-y: auto;` or wrapper `.table-responsive` improvements) so cards do not clip content or break layout when content exceeds dimensions.
- Ensure max-height or proper flex/grid layout constraints on cards where vertical or horizontal scrolling is needed.

### 3. Validation
- Run unit tests (`dotnet test`).
- Verify database student IDs via query/seeding checks.
- Verify UI rendering and scroll behavior.

## Risks & Mitigations
- **Foreign Key integrity**: Since Entity Framework Core uses integer `Id` as primary key for `Student` and references `StudentId` (int) in `Attendance`, updating the string `StudentID` property will not violate relational integrity.
- **CSS side-effects**: Keep card scrolling scoped to tables and overflow containers (`.table-responsive`, `.ax-card .card-b`) to prevent unintended scrollbars on normal viewports.
