# AttendX — Product Requirements Document (PRD)

**Institution:** PVG College of Engineering & Technology (PVGCOET), Pune
**Program:** B.Tech (4 years, 8 semesters) — all 14 branches
**Academic year:** June–May. Odd semesters (1,3,5,7): Aug 1 – Dec 15 classes, End-Sem Dec 16–30. Even semesters (2,4,6,8): Jan 10 – May 15 classes, End-Sem May 16–31. Summer vacation: Jun 1–Jul 31. Odd-sem 1-week break: Dec 31–Jan 9.

## 1. Personas
| Persona | Goal | Login |
|---|---|---|
| **Admin** | Full control: students, faculty, subjects, timetable, holidays, notices, reports, analytics, audit, backup, settings, exam management, leave approvals | `/admin/login` (email + password) |
| **HOD** | Department scope: all admin features limited to own branch | `/faculty/login` (Faculty ID + email + password) |
| **Faculty** | Mark attendance (manual + QR), enter exam marks, view timetable, raise queries | `/faculty/login` |
| **Student** | View own attendance, timetable, exams, results, raise attendance query | `/student/login` (Student ID + email + password) |
| **Parent** | View ward's attendance, exam results, apply leave on ward's behalf | `/parent/login` (ward's Student ID + parent's email + password) |

## 2. Authentication
- ASP.NET Core Identity with application cookie (`AttendX.Auth`, 12 h sliding, `HttpOnly`, `SameSite=Lax`, `Always` in prod).
- Passwords seeded: Admin `Admin@123`, Faculty `Faculty@123`, Student `Student@123`, Parent `Parent@123`. Force-change on first login.
- 5 failed attempts → 10 min lockout.
- "Remember me" → persistent cookie.

## 3. Academic structure
- **4 years, 8 semesters.** Odd = Aug–Dec, Even = Jan–May.
- **14 branches:** CSE, CSE-AI, CSE-AIML, AI&DS, ECE, EEE, MECH, CIVIL, IT, Cyber Security, AIDS, AIML, IOT, DS.
- **2 sections per branch** per semester (28 sections), **30 students/section** (2,520 total).
- **896 subjects** (8 per branch per semester × 14 branches × 8 semesters).
- **126 faculty** (1 HOD + 8 faculty per branch).

## 4. Timetable rules
- **7 periods/day**, Mon–Sat.
  - P1 09:00–10:00, P2 10:00–11:00, P3 11:00–12:00, P4 12:00–13:00
  - Lunch 13:00–14:00
  - P5 14:00–15:20 (**80 min**, Lab), P6 15:20–16:20, P7 16:20–17:20
- Lecture = 60 min, Lab = 80 min.
- Rooms: Block A/B/C, floors 1–3, rooms 101–330 (270 rooms) + Lab-1..Lab-6 per floor.
- **No room/faculty/section clash** per time slot (enforced by seeder + DB unique index on `(TimetableId, RoomNo)` and room-per-day exam dedup).

## 5. Attendance rules
- Mark only inside period ± grace (10 min before, 10 min after). After window → only Admin/HOD can correct (with reason).
- Statuses: Present, Late, Absent, Leave (auto-excluded from % if approved leave).
- Late weight: 0.5 (counts as half a present). Configurable in Settings.
- **75% minimum** required for exam eligibility.
- Student raise query → faculty sees → resolves with response.
- **105 days of attendance history** seeded (from Aug 1 2026).

## 6. Exams
| Exam | When | Type |
|---|---|---|
| Mid-Sem 1 | 3rd week of September (all semesters) | Internal (30 marks) |
| Mid-Sem 2 | 3rd week of November (all semesters) | Internal (30 marks) |
| End-Semester | Dec 16–30 (odd), May 16–31 (even) | External (70 marks) |

- **Marks:** Internal (30) + External (70) = 100. Grade: O/A+/A/B+/B/C/P/F.
- **Lock workflow:** Faculty enters marks → submits → **locks** after external exam → no further edits. To modify, faculty raises a **modification query** → HOD/Admin approves + allots a time window → faculty edits only inside the window.
- **Hall ticket:** blocked if attendance < 75%.
- **Results:** visible to student + parent after evaluation.

## 7. Leave
- Types: Medical (12d), Casual (6d), On-Duty (10d), Maternity (90d), Paternity (15d).
- Student applies → HOD/Admin approves/rejects → if approved, dates auto-mark as Leave in attendance.
- Leave balance tracked per student per academic year.

## 8. Notifications
- **Email (SendGrid SMTP)** + **SMS (Twilio)**.
- Triggers: absence alert (to parent), low-attendance warning (to student), weekly deficiency report (to HOD).
- All sends logged to `NotificationLog` table (channel, to, subject, status, error).

## 9. Non-functional requirements
- Responsive: phone (< 576px), tablet (576–992px), desktop (> 992px).
- WCAG 2.1 AA (color contrast, focus rings, skip-link, keyboard shortcuts).
- Page load < 2 s on 4G. Charts render < 300 ms.
- Offline: QR scans queued in IndexedDB, sync when online.
- **Smooth animations** everywhere (cubic-bezier easing, 250–350 ms).
- No dark mode (institution requested light theme only).

## 10. Acceptance criteria
- [x] 4 logins work with seeded passwords.
- [x] 2,520 students, 126 faculty, 2,520 parents seeded.
- [x] 105 days of attendance history (Aug–Nov 2026).
- [x] Exams module with marks, lock, modification query.
- [x] Leave module with balance + approval.
- [x] Parent portal with ward view.
- [x] Neon-ready (Npgsql + migrations).
- [x] Dockerfile + docker-compose + render.yaml.
- [x] CI (GitHub Actions).
