#!/usr/bin/env python3
"""Generate complete SQL seed script from CSV files for Supabase."""

import csv
import os
import uuid
from datetime import datetime

DATASET_DIR = "AttendX/Dataset"
OUTPUT = "seed_all_data.sql"

def esc(s):
    return str(s).replace("'", "''")

def read_csv(filename):
    rows = []
    with open(os.path.join(DATASET_DIR, filename), newline='', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            rows.append(row)
    return rows

branches_data = [
    ("CSE", "CSE"), ("CSE-AI", "CSEAI"), ("CSE-AIML", "CSEAIML"), ("AI&DS", "ADS"),
    ("ECE", "ECE"), ("EEE", "EEE"), ("MECH", "MECH"), ("CIVIL", "CIVIL"),
    ("IT", "IT"), ("Cyber Security", "CYBER"), ("AIDS", "AIDS"), ("AIML", "AIML"),
    ("IOT", "IOT"), ("DS", "DS")
]

section_rules = {
    "CSE": ("EV", 6), "CSEAI": ("EN", 24), "CSEAIML": ("EN", 24), "ADS": ("EU", 6),
    "ECE": ("EC", 6), "EEE": ("EE", 4), "MECH": ("ME", 4), "CIVIL": ("CE", 4),
    "IT": ("IT", 6), "CYBER": ("CS", 6), "AIDS": ("AD", 4), "AIML": ("AM", 4),
    "IOT": ("IO", 4), "DS": ("DS", 4)
}

students = read_csv("students.csv")
faculty = read_csv("faculty.csv")
subjects = read_csv("subjects.csv")
timetable = read_csv("timetable.csv")
holidays = read_csv("holidays.csv")

# Plain text passwords — stored as NULL initially, populated on first login
# No ASP.NET Identity hashing for this college project
ADMIN_PASSWORD_HASH = "NULL"
FACULTY_PASSWORD_HASH = "NULL"
STUDENT_PASSWORD_HASH = "NULL"

sql = []
sql.append("-- ============================================================")
sql.append("-- AttendX Complete Seed Script for Supabase")
sql.append("-- Run this in Supabase SQL Editor after schema_final.sql")
sql.append("-- Admin: kota@attendx.edu.in / kotareddy")
sql.append("-- Faculty: FacultyID / Faculty@123")
sql.append("-- Student: StudentID / Student@123")
sql.append("-- ============================================================")
sql.append("")

branch_inserts = []
branch_id_map = {}
for i, (name, code) in enumerate(branches_data, 1):
    branch_inserts.append(f"({i}, '{esc(name)}', '{esc(code)}', true)")
    branch_id_map[code] = i
sql.append("INSERT INTO \"Branches\" (\"Id\", \"Name\", \"Code\", \"IsActive\") VALUES")
sql.append(",\n".join(branch_inserts) + ";")
sql.append("")

section_inserts = []
section_id_map = {}
sec_id = 1
for (name, code) in branches_data:
    if code not in section_rules:
        continue
    prefix, count = section_rules[code]
    for i in range(1, count + 1):
        sec_name = f"{prefix}{i}"
        section_inserts.append(f"({sec_id}, '{esc(sec_name)}', {branch_id_map[code]}, true)")
        section_id_map[(code, sec_name)] = sec_id
        sec_id += 1
sql.append("INSERT INTO \"Sections\" (\"Id\", \"Name\", \"BranchId\", \"IsActive\") VALUES")
sql.append(",\n".join(section_inserts) + ";")
sql.append("")

period_times = [
    (1, "09:00:00", "09:55:00"), (2, "10:00:00", "10:55:00"),
    (3, "11:00:00", "11:55:00"), (4, "12:00:00", "12:55:00"),
    (5, "14:00:00", "14:55:00"), (6, "15:00:00", "15:55:00"),
    (7, "16:00:00", "16:55:00")
]
period_inserts = [f"({i}, {pno}, '{start}', '{end}')" for i, (pno, start, end) in enumerate(period_times, 1)]
sql.append("INSERT INTO \"PeriodDefinitions\" (\"Id\", \"PeriodNo\", \"StartTime\", \"EndTime\") VALUES")
sql.append(",\n".join(period_inserts) + ";")
sql.append("")

sql.append("INSERT INTO \"AcademicYears\" (\"Id\", \"Name\", \"StartDate\", \"EndDate\", \"IsCurrent\") VALUES")
sql.append("(1, '2026-27', '2026-08-01 00:00:00+00', '2027-05-31 23:59:59+00', true);")
sql.append("")

subject_inserts = []
subject_id_map = {}
subj_id = 1
for row in subjects:
    code = row["BranchCode"]
    sem = int(row["Semester"])
    subj_code = row["SubjectCode"]
    subj_name = esc(row["SubjectName"])
    credits = int(row["Credits"])
    subject_inserts.append(f"({subj_id}, '{subj_name}', '{esc(subj_code)}', {branch_id_map[code]}, {sem}, {credits}, true)")
    subject_id_map[subj_code] = subj_id
    subj_id += 1
sql.append("INSERT INTO \"Subjects\" (\"Id\", \"SubjectName\", \"SubjectCode\", \"BranchId\", \"Semester\", \"Credits\", \"IsActive\") VALUES")
sql.append(",\n".join(subject_inserts) + ";")
sql.append("")

# Faculty + Users
faculty_inserts = []
user_inserts = []
faculty_id_map = {}
fac_id = 1
for row in faculty:
    fid = row["FacultyID"]
    name = esc(row["FullName"])
    email = row["Email"].strip().lower()
    phone = row["Phone"]
    dept = row["Department"]
    designation = esc(row["Designation"])
    is_hod = row["IsHOD"].lower() == "true"
    branch_code = row["Department"]
    bid = branch_id_map.get(branch_code)
    
    uid = str(uuid.uuid4())
    faculty_inserts.append(
        f"({fac_id}, '{esc(fid)}', '{name}', '{email}', '{phone}', {bid}, '{esc(dept)}', '{designation}', {is_hod}, true, '{uid}', "
        f"'2026-01-01 00:00:00+00', '2026-01-01 00:00:00+00')"
    )
    faculty_id_map[fid] = fac_id
    fac_id += 1
    
    user_inserts.append(
        f"('{uid}', '{esc(name)}', false, '2026-01-01 00:00:00+00', NULL, '{email}', '{email.upper()}', "
        f"'{email}', '{email.upper()}', true, {FACULTY_PASSWORD_HASH}, '{uuid.uuid4()}', '{uuid.uuid4()}', NULL, "
        f"false, false, NULL, true, 0)"
    )

sql.append("INSERT INTO \"Faculty\" (\"Id\", \"FacultyID\", \"FullName\", \"Email\", \"Phone\", \"BranchId\", \"Department\", \"Designation\", \"IsHOD\", \"IsActive\", \"IdentityUserId\", \"CreatedAt\", \"UpdatedAt\") VALUES")
sql.append(",\n".join(faculty_inserts) + ";")
sql.append("")

sql.append("INSERT INTO \"AspNetUsers\" (\"Id\", \"FullName\", \"MustChangePassword\", \"CreatedAt\", \"LastLoginAt\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumber\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnd\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES")
sql.append(",\n".join(user_inserts) + ";")
sql.append("")

# Students + Users
student_inserts = []
student_user_inserts = []
student_id_map = {}
stud_id = 1
for row in students:
    sid = row["StudentID"]
    name = esc(row["FullName"])
    email = row["Email"].strip().lower()
    phone = row["Phone"]
    branch_code = row["BranchCode"]
    section_name = row["Section"]
    sem = int(row["Semester"])
    joining_date = row["JoiningDate"]
    year = (sem + 1) // 2
    
    bid = branch_id_map.get(branch_code)
    sec_key = (branch_code, section_name)
    sec_id_val = section_id_map.get(sec_key)
    
    uid = str(uuid.uuid4())
    student_inserts.append(
        f"({stud_id}, '{esc(sid)}', '{name}', '{email}', '{phone}', {bid}, {sec_id_val}, {sem}, {year}, "
        f"NULL, 'on-demand', '{uid}', '{joining_date} 00:00:00+00', true, '2026-01-01 00:00:00+00', '2026-01-01 00:00:00+00', '\\x00')"
    )
    student_id_map[sid] = stud_id
    stud_id += 1
    
    student_user_inserts.append(
        f"('{uid}', '{name}', false, '2026-01-01 00:00:00+00', NULL, '{email}', '{email.upper()}', "
        f"'{email}', '{email.upper()}', true, {STUDENT_PASSWORD_HASH}, '{uuid.uuid4()}', '{uuid.uuid4()}', NULL, "
        f"false, false, NULL, true, 0)"
    )

sql.append("INSERT INTO \"Students\" (\"Id\", \"StudentID\", \"FullName\", \"Email\", \"Phone\", \"BranchId\", \"SectionId\", \"Semester\", \"Year\", \"PhotoPath\", \"QRPath\", \"IdentityUserId\", \"JoiningDate\", \"IsActive\", \"CreatedAt\", \"UpdatedAt\", \"RowVersion\") VALUES")
sql.append(",\n".join(student_inserts) + ";")
sql.append("")

sql.append("INSERT INTO \"AspNetUsers\" (\"Id\", \"FullName\", \"MustChangePassword\", \"CreatedAt\", \"LastLoginAt\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumber\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnd\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES")
sql.append(",\n".join(student_user_inserts) + ";")
sql.append("")

# Admin user
admin_uid = str(uuid.uuid4())
admin_email = "kota@attendx.edu.in"
admin_password_hash = "NULL"
sql.append("INSERT INTO \"AspNetUsers\" (\"Id\", \"FullName\", \"MustChangePassword\", \"CreatedAt\", \"LastLoginAt\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumber\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnd\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES")
sql.append(f"('{admin_uid}', 'Kota Reddy', false, '2026-01-01 00:00:00+00', NULL, '{admin_email}', 'KOTA@ATTENDX.EDU.IN', '{admin_email}', 'KOTA@ATTENDX.EDU.IN', true, {admin_password_hash}, '{uuid.uuid4()}', '{uuid.uuid4()}', NULL, false, false, NULL, true, 0);")
sql.append("")

# Roles
role_admin_id = str(uuid.uuid4())
role_faculty_id = str(uuid.uuid4())
role_student_id = str(uuid.uuid4())
role_hod_id = str(uuid.uuid4())

sql.append("INSERT INTO \"AspNetRoles\" (\"Id\", \"Name\", \"NormalizedName\", \"ConcurrencyStamp\") VALUES")
sql.append(f"('{role_admin_id}', 'Admin', 'ADMIN', '{uuid.uuid4()}'),")
sql.append(f"('{role_faculty_id}', 'Faculty', 'FACULTY', '{uuid.uuid4()}'),")
sql.append(f"('{role_student_id}', 'Student', 'STUDENT', '{uuid.uuid4()}'),")
sql.append(f"('{role_hod_id}', 'Hod', 'HOD', '{uuid.uuid4()}');")
sql.append("")

# Admin role
sql.append("INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") VALUES")
sql.append(f"('{admin_uid}', '{role_admin_id}');")
sql.append("")

# Faculty user roles via subquery
faculty_role_sql = []
for row in faculty:
    fid = row["FacultyID"]
    is_hod = row["IsHOD"].lower() == "true"
    faculty_role_sql.append(
        f"INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") "
        f"SELECT \"IdentityUserId\", '{role_faculty_id}' FROM \"Faculty\" WHERE \"FacultyID\" = '{esc(fid)}';"
    )
    if is_hod:
        faculty_role_sql.append(
            f"INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") "
            f"SELECT \"IdentityUserId\", '{role_hod_id}' FROM \"Faculty\" WHERE \"FacultyID\" = '{esc(fid)}';"
        )
sql.extend(faculty_role_sql)
sql.append("")

# Student user roles via subquery
for row in students:
    sid = row["StudentID"]
    sql.append(
        f"INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") "
        f"SELECT \"IdentityUserId\", '{role_student_id}' FROM \"Students\" WHERE \"StudentID\" = '{esc(sid)}';"
    )
sql.append("")

# Timetables
tt_inserts = []
fsm_inserts = []
tt_id = 1
fsm_id = 1
academic_year_id = 1
seen_mappings = set()
seen_slots = set()

for row in timetable:
    branch_code = row["BranchCode"]
    section_name = row["Section"]
    sem = int(row["Semester"])
    day = int(row["Day"])
    period = int(row["Period"])
    subj_code = row["SubjectCode"]
    fac_id_str = row["FacultyID"]
    room = esc(row["Room"])
    
    bid = branch_id_map.get(branch_code)
    sec_key = (branch_code, section_name)
    sec_id_val = section_id_map.get(sec_key)
    subj_id_val = subject_id_map.get(subj_code)
    fac_db_id = faculty_id_map.get(fac_id_str)
    
    if not all([bid, sec_id_val, subj_id_val, fac_db_id]):
        continue
    
    slot_key = (sec_id_val, day, period)
    if slot_key in seen_slots:
        continue
    seen_slots.add(slot_key)
    
    class_type = "Lecture"
    if "Lab" in room:
        class_type = "Lab"
    elif "Workshop" in room:
        class_type = "Workshop"
    
    period_def = period_times[period - 1] if period <= len(period_times) else period_times[0]
    
    tt_inserts.append(
        f"({tt_id}, {bid}, {sec_id_val}, {sem}, {day}, {period}, {subj_id_val}, {fac_db_id}, "
        f"'{room}', '{class_type}', '{period_def[1]}', '{period_def[2]}', true, {academic_year_id})"
    )
    
    mapping_key = (fac_db_id, subj_id_val, sec_id_val, academic_year_id)
    if mapping_key not in seen_mappings:
        seen_mappings.add(mapping_key)
        fsm_inserts.append(
            f"({fsm_id}, {fac_db_id}, {subj_id_val}, {bid}, {sec_id_val}, {sem}, {academic_year_id}, true)"
        )
        fsm_id += 1
    
    tt_id += 1

sql.append("INSERT INTO \"Timetables\" (\"Id\", \"BranchId\", \"SectionId\", \"Semester\", \"Day\", \"Period\", \"SubjectId\", \"FacultyId\", \"RoomNo\", \"ClassType\", \"StartTime\", \"EndTime\", \"IsActive\", \"AcademicYearId\") VALUES")
sql.append(",\n".join(tt_inserts) + ";")
sql.append("")

sql.append("INSERT INTO \"FacultySubjectMappings\" (\"Id\", \"FacultyId\", \"SubjectId\", \"BranchId\", \"SectionId\", \"Semester\", \"AcademicYearId\", \"Active\") VALUES")
sql.append(",\n".join(fsm_inserts) + ";")
sql.append("")

# Holidays
holiday_inserts = []
for i, row in enumerate(holidays, 1):
    h_date = row["Date"]
    h_name = esc(row["Name"])
    h_type = esc(row["Type"])
    holiday_inserts.append(f"({i}, '{h_date} 00:00:00+00', '{h_name}', '{h_type}')")
sql.append("INSERT INTO \"Holidays\" (\"Id\", \"Date\", \"Name\", \"Type\") VALUES")
sql.append(",\n".join(holiday_inserts) + ";")
sql.append("")

# Notices
now = datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S+00")
notice_inserts = [
    f"(1, 'Welcome to AttendX', 'Attendance is now digital.', 'All', NULL, NULL, '{now}', NULL, true, 'System Administrator')",
    f"(2, '75% attendance required', 'Students below 75% may not sit exams.', 'Student', NULL, NULL, '{now}', NULL, false, 'System Administrator')",
    f"(3, 'Mark attendance in period window', 'Attendance can be marked 10 min before to 10 min after period.', 'Faculty', NULL, NULL, '{now}', NULL, false, 'System Administrator')"
]
sql.append("INSERT INTO \"Notices\" (\"Id\", \"Title\", \"Body\", \"Audience\", \"BranchId\", \"SectionId\", \"PublishedAt\", \"ExpiresAt\", \"IsPinned\", \"CreatedBy\") VALUES")
sql.append(",\n".join(notice_inserts) + ";")
sql.append("")

# AttendanceSettings
sql.append("INSERT INTO \"AttendanceSettings\" (\"Id\", \"GraceMinutes\", \"DefaultThreshold\", \"QrExpirySeconds\", \"LocationRequirement\", \"LateWeight\", \"LateAfterMinutes\", \"OfflineSyncEnabled\", \"OfflineMaxDelayMinutes\", \"PeriodCount\", \"UpdatedAt\") VALUES")
sql.append(f"(1, 10, 75.00, 30, 'Optional', 0.00, 15, true, 120, 7, '{now}');")
sql.append("")

# Verification
sql.append("-- ============================================================")
sql.append("-- Verification queries")
sql.append("-- ============================================================")
sql.append("SELECT count(*) AS branches_count FROM \"Branches\";")
sql.append("SELECT count(*) AS sections_count FROM \"Sections\";")
sql.append("SELECT count(*) AS subjects_count FROM \"Subjects\";")
sql.append("SELECT count(*) AS faculty_count FROM \"Faculty\";")
sql.append("SELECT count(*) AS students_count FROM \"Students\";")
sql.append("SELECT count(*) AS timetables_count FROM \"Timetables\";")
sql.append("SELECT count(*) AS users_count FROM \"AspNetUsers\";")
sql.append("SELECT count(*) AS roles_count FROM \"AspNetRoles\";")
sql.append("SELECT count(*) AS user_roles_count FROM \"AspNetUserRoles\";")

with open(OUTPUT, "w", encoding="utf-8") as f:
    f.write("\n".join(sql))

print(f"Seed script written to {OUTPUT}")
print(f"Branches: {len(branches_data)}")
print(f"Sections: {len(section_inserts)}")
print(f"Subjects: {len(subject_inserts)}")
print(f"Faculty: {len(faculty_inserts)}")
print(f"Students: {len(student_inserts)}")
print(f"Timetables: {len(tt_inserts)}")
print(f"FacultySubjectMappings: {len(fsm_inserts)}")
print(f"Holidays: {len(holiday_inserts)}")
print(f"Notices: {len(notice_inserts)}")
print(f"Admin: {admin_email} / kotareddy")
