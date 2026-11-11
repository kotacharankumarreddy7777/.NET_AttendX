-- ============================================================
-- AttendX Neon Data Reset (no backup needed)
-- Run in Neon Console -> SQL Editor, then redeploy on Render
-- Keeps __EFMigrationsHistory so EF migrations stay intact
-- ============================================================

-- 1. Transactional data (children first)
DELETE FROM "Attendance";
DELETE FROM "AttendanceSessions";
DELETE FROM "ExamMarks";
DELETE FROM "ExamTimetables";
DELETE FROM "FacultySubjectMappings";
DELETE FROM "Timetables";
DELETE FROM "AttendanceQueries";

-- 2. People (clears Students/Faculty, then their Identity users)
DELETE FROM "Students";
DELETE FROM "Faculty";

-- 3. Master data
DELETE FROM "Subjects";
DELETE FROM "Sections";
DELETE FROM "ThresholdRules";
DELETE FROM "Notices";
DELETE FROM "Holidays";
DELETE FROM "PeriodDefinitions";
DELETE FROM "AcademicYears";
DELETE FROM "Branches";

-- 4. Settings & operational
DELETE FROM "AttendanceSettings";
DELETE FROM "AuditLogs";
DELETE FROM "BackupHistories";
DELETE FROM "UserPreferences";
DELETE FROM "InstitutionBranding";

-- 5. Identity (clean up remaining users/roles)
DELETE FROM "AspNetUserTokens";
DELETE FROM "AspNetUserRoles";
DELETE FROM "AspNetUserLogins";
DELETE FROM "AspNetUserClaims";
DELETE FROM "AspNetRoleClaims";
DELETE FROM "AspNetUsers";
DELETE FROM "AspNetRoles";

-- 6. Reset identity columns (sequences)
SELECT setval(pg_get_serial_sequence('"Attendance"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"AttendanceSessions"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"ExamMarks"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"ExamTimetables"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"FacultySubjectMappings"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"Timetables"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"AttendanceQueries"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"Students"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"Faculty"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"Subjects"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"Sections"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"ThresholdRules"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"Notices"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"Holidays"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"PeriodDefinitions"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"AcademicYears"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"Branches"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"AttendanceSettings"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"AuditLogs"', 'Id'), 1, false);
SELECT setval(pg_get_serial_sequence('"BackupHistories"', 'Id'), 1, false);

SELECT 'Neon data reset complete. All tables emptied; identities reset.' AS status;
