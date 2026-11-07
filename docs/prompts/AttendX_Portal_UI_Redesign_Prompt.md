# AttendX - NEW PORTAL INTERFACE (UI-only redesign, backend unchanged)

Paste everything below the line into an AI coding agent that can run terminal commands (Kilo Code, Cursor, Claude Code) with the AttendX_Solution folder open.

---

## 1. TASK
Replace the entire user interface of the existing ASP.NET Core MVC app **AttendX** with a **new, professional institutional portal interface**, like the student/faculty ERP portals used by real universities.
**The backend stays exactly the same** (models, database, services, controllers' logic, routes, form field names, seeding, login rules).
Only change: `Views/**`, `wwwroot/css/**`, `wwwroot/js/**` (presentation), and user-facing message strings.

The current interface (gradient colours, glass cards, floating shapes, illustrations everywhere, marketing text, theme colours per role) is rejected. The new one must look like a **serious, plain, trustworthy college portal**: flat, structured, data-first, clean and neat.

## 2. BACKEND CONTRACT (do not break)
- **Do not change** controller actions' logic, service classes, entities, DbContext, seeder, `Program.cs`, appsettings keys, routes or policies.
- Keep every `asp-controller/asp-action` target, **form field `name=`/`asp-for` values**, hidden fields and antiforgery tokens.
- Keep these URLs working: `/` (role selection), `/admin/login`, `/faculty/login`, `/student/login` (POST `/account/signin`), `/Dashboard`, `/Student`, `/Attendance/*` (Mark, Scan, Corrections, SessionToken, ScanPost, Sync), `/Students/*`, `/Faculty/*`, `/FacultySubject`, `/Subjects`, `/Branches`, `/Sections` (+ `ByBranch`, `SubjectsFor` JSON), `/Timetable`, `/Reports` (+ Export), `/Analytics`, `/Audit`, `/Backup` (+ ExportExcel), `/Settings`, `/Holidays`, `/Notices` (+ Board), `/Queries`, `/Account/ChangePassword`, `/Error/{code}`.
- JavaScript that talks to the backend must keep working. Preserve these element IDs/attributes and the scripts that use them (restyle freely, do not rename):
  `markForm, allP, allA, resetBtn, cP, cA, cL, cU, DeviceId, Latitude, Longitude, countdown, scanRoot, reader, startScan, stopScan, scanResult, scanLog, scanCount, chartData, weeklyData, chGroup, chSem, chDaily, chMonthly, chSubject, chWeekly, BranchId, SectionId, Semester, SubjectId, camBtn, camVideo, snapBtn, WebcamPhoto, photoPreview, yearView, Photo, offlineBadge, toastArea, logoutForm, InstitutionId, Password, loginForm` and attributes `data-confirm, data-once, data-tt, data-date, data-end-ms, data-now-ms, data-location, data-offline, data-expiry`, class `dt`, radio names `Status[<id>]`, `<meta name="csrf">`.
- Keep `site.js` helpers (`AttendX.csrf/postJson/loadSections/loadSubjects/deviceId/toast`), `offline-sync.js`, `attendance.js`, `qr-scanner.js`, `students.js`, `timetable.js`, `reports.js`, `dashboard.js` (restyle charts only).
- You may edit **user-facing text strings** in controllers/services (TempData, ModelState messages) to match section 9. No logic changes.
- Libraries stay CDN-only: Bootstrap 5.3 (as a base, heavily themed), Bootstrap Icons, Chart.js, DataTables, html5-qrcode. No new frameworks, no npm build.

## 3. DESIGN LANGUAGE - "INSTITUTIONAL PORTAL"
Flat, high-contrast, structured. No gradients, no glass/blur, no glow, no floating or animated decoration, no per-role colour themes, no dark mode (light only), no emojis.

**Colour tokens (CSS variables in a new `wwwroot/css/portal.css`)**
| Token | Value | Use |
|---|---|---|
| `--navy` | `#14305A` | header, sidebar, table headers, primary buttons |
| `--navy-dark` | `#0E2442` | sidebar hover/active, footer |
| `--blue` | `#1F5FA8` | links, focus, selected states |
| `--accent` | `#B8892D` | thin accent line under header, small highlights only |
| `--bg` | `#F2F4F7` | page background |
| `--surface` | `#FFFFFF` | panels, tables, forms |
| `--border` | `#D5DBE3` | all borders (1px) |
| `--text` | `#1B2430` | body text |
| `--muted` | `#5E6A7A` | helper text, labels |
| `--success` | `#1E7F4F` / bg `#E7F4EC` | present, good attendance |
| `--warning` | `#9A6B00` / bg `#FDF3DC` | late, watch |
| `--danger` | `#B42318` / bg `#FCE9E7` | absent, below threshold |
| `--info` | `#1D5FA8` / bg `#E6F0FB` | information |
Same palette for **every role** (Admin, Faculty, Student). The role is shown as text in the header, not by colour.

**Typography**
- Font: `Inter` (Google Fonts 400/500/600) with fallback `"Segoe UI", Arial, sans-serif`. No other fonts. Numbers use `font-variant-numeric: tabular-nums`.
- Sizes (px): base **14**, table body **13**, table header **12 uppercase letter-spacing .04em, 600**, small/help **12**, page title **22/600**, panel title **16/600**, KPI number **26/600**, login title **20/600**. Line height 1.5. No text smaller than 12px.
- Weight: 400 body, 500 labels, 600 headings. No 800 weights, no huge display text.

**Shape and spacing**
- Radius **4px** everywhere (inputs, buttons, panels). Pills only for status chips (999px, 12px text).
- Shadows: none, except 1px `0 1px 2px rgba(16,24,40,.06)` on panels. Borders do the separation.
- Spacing scale 4/8/12/16/24/32. Panel padding 16. Page gutter 24 (mobile 12). Content max-width 1400px.
- Controls: height **36px** (mobile 44px), 1px border, focus = 2px `--blue` outline (no glow). Buttons: primary solid navy, secondary white with border, danger solid red; no gradients; hover = slightly darker; active = darker again. Disabled = 50% opacity.

**Motion**: functional only. 150ms colour/background transition on hover and focus, 200ms collapse/drawer. No page-load animation, no stagger, no count-up, no floating, no parallax, no shake. Respect `prefers-reduced-motion`.

## 4. PORTAL SHELL (all signed-in pages)
- **Top header (56px, navy):** left - small square logo mark (configurable image, default simple "A" monogram) + institution name (`Institution:Name` setting, default "Institution Name") + system name "Attendance Management System" in smaller muted-white text. Right - notification bell with count, user name + role text ("Administrator", "Head of Department", "Faculty", "Student"), "Sign out". A 3px `--accent` line under the header.
- **Context bar under the header (white, 40px):** breadcrumb (Home / Section / Page) on the left; on the right the academic year and today's date (and semester for students).
- **Left sidebar (240px, navy, collapsible to icons):** grouped menu with 12px uppercase group labels, 14px items, 1px icons (Bootstrap Icons, outline style), active item = `--navy-dark` background + 3px accent bar on the left. Mobile: off-canvas drawer opened from a menu button; plus a simple bottom bar (Home, Today, Timetable, Menu) for Faculty and (Home, Notices, Report, Menu) for Students.
- **Page title area:** H1 (22px) on the left, primary actions on the right, one line of plain helper text only if really needed.
- **Footer:** one line - "(c) <year> <Institution Name>. All rights reserved." and version. Nothing else.
- Menu per role (same items as today; labels in section 9). Student menu: Dashboard, Notices, Attendance Report, Change Password.

## 5. LOGIN SCREENS (three entrances, plain and official)
- **`/` Role selection:** white header strip with logo + institution name + system name. Centered title "Sign in". Three equal bordered tiles in a row (stack on mobile): **Administrator**, **Faculty / HOD**, **Student**. Each tile: simple 32px outline icon, title, one short line ("Administrators", "Faculty and department heads", "Enrolled students"), and the whole tile is a link. No animation except border colour on hover. Footer line as above.
- **Login pages (`/admin/login`, `/faculty/login`, `/student/login`):** same header strip; light-grey page; **one centered white panel (width 400px)**, 1px border, 24px padding. Inside: title ("Administrator Login", "Faculty Login", "Student Login"), form, then a plain text row of the other two login links ("Faculty login | Student login"). **No illustrations, no split layout, no marketing text, no feature lists, no demo buttons.**
  - Admin fields: Email, Password.
  - Faculty/Student fields: **Faculty ID / Student ID**, Email, Password.
  - Labels above inputs (500 weight), 36px inputs, show/hide password text button ("Show"/"Hide"), "Remember me" checkbox, primary full-width button "Login", link "Forgot password?" that expands one line: "Contact the administrator to reset your password."
  - Validation: inline red 12px text under the field; form-level error in a red-bordered alert row. Messages from section 9 only.
  - Institution ID is upper-cased as typed; password field shows a small "Caps Lock is on" text when relevant. Nothing else is animated.
- **Change password:** same panel style with Current / New / Confirm and one line of rules ("At least 8 characters with upper case, lower case, number and symbol.").

## 6. DASHBOARDS (flat tiles + tables, no hero banners)
**General pattern:** a row of KPI tiles (white panel, 1px border; label 12px muted on top, number 26px below; no icons, no gradients); then two-column panels with simple tables and charts. Charts: Chart.js, flat, palette navy `#14305A`, blue `#1F5FA8`, grey `#9AA5B4`, threshold line dashed `--danger`, no gradients, no animation, grid light grey, legend only when more than one series.

**Administrator / HOD dashboard:** tiles - Students, Faculty, Classes Today, Sessions Marked Today, Average Attendance (30 days), Below Threshold. Panels - Attendance by Branch (or Section for HOD) bar chart, Daily Attendance (14 days) line, Low Attendance List (table: name, ID, class, %, classes required), Attendance Not Marked Today (table), Faculty Activity (7 days), Notices.

**Faculty dashboard (one screen, own data):** header panel with a plain 2-column definition list (Faculty ID, Name, Department, Designation, Email, Phone, Last Login). Panel "Current Class" (subject, class, period, time remaining, buttons **Mark Attendance**, **Scan QR**) or "Next class at HH:MM". Tiles - Classes Today, Classes per Week, Students Taught, Average Attendance, Below Threshold, Open Queries. Panels - Today's Schedule (table: period, time, subject, class, room, status, action), Classes Taught (table: class code, subject, strength, average %), Low Attendance List, Notices, Weekly Timetable grid.

**Student dashboard (one screen, read-only, own data):**
1. **Student details panel:** definition list in two columns - Name, Student ID, Branch, Section, Semester, Year, Email, Phone, Date of Joining, Class Strength ("30 students"), Last Login; QR code on the right with "Download" and "Print" links.
2. **Attendance summary:** tiles - Overall Attendance (%), Required (%), Classes Held, Present, Late, Absent, Classes Required to Reach Minimum. If below minimum show one red-bordered notice row: "Your attendance is 68.2%. Minimum required is 75%. Attend the next 9 classes to reach it."
3. **Subject-wise Attendance:** table (Subject code, Subject, Faculty, Held, Present, Late, Absent, %, status text) with a thin 6px progress bar in the % cell.
4. **Today's Classes** table and **Weekly Timetable** grid.
5. **Attendance Calendar (last 5 weeks):** simple 7-column grid of small squares with legend (Present, Late/Partial, Absent, No Class, Holiday/Sunday), tooltips with the date.
6. **Recent Attendance** table with filters (subject, month, status).
7. **My Faculty** table, **Notices** list, **Holidays and Events** list.
8. **Attendance Query:** small form (date, subject, remarks) + table of my queries with status chips (Open, Resolved, Rejected) and the response text.
9. **Downloads and Profile:** "Download Attendance Report (PDF)", phone update field, recent sign-ins table.

## 7. DATA PAGES (one consistent pattern)
- **List pages (Students, Faculty, Subjects, Faculty-Subject, Branches, Sections, Holidays, Notices, Audit, Queries):** page title + primary action right; **filter bar** (single row panel: search + selects + Search/Clear buttons); table panel with sticky navy header, 13px rows (36px high), right-aligned numbers, status chips, row action links ("View", "Edit", "Deactivate") as text buttons; pagination bar below (showing "1-25 of 2,520", Previous / Next). Class strength shown above Students list when a class is filtered ("Class strength: 30").
- **Form pages:** single panel, labels above fields, two-column grid on desktop, required marker `*`, sticky action row at the bottom of the panel (Save / Cancel). Photo upload with small preview square; webcam capture as a secondary button.
- **Detail pages:** definition list panel + related tables.
- **Timetable:** filter bar, then a bordered week grid (periods as rows, Mon-Sat columns), cells with subject code, faculty/class and room in 12px text; today's column has a light-blue header. Mobile: day selector.
- **Mark Attendance:** header panel (subject, class, period, time, remaining time); toolbar (All Present, All Absent, Reset, counters "Present 28 | Absent 2 | Late 0 | Unmarked 0"); table with Roll No, Student ID, Name, and a 3-option segmented control Present / Absent / Late (44px tall on mobile); sticky bottom action bar with "Save Attendance". Row background tints (very light green/red/amber) only after a choice.
- **QR Scan:** two panels - camera (Start / Stop buttons) and scan log table; result line under the camera with a green, amber or red left border and plain text ("Marked: Name (ID) - Present", "Already marked", "Not in this class", "Invalid QR code").
- **Reports / Summary:** filter panel (report type, branch, section, semester, subject, dates), result table with totals row, export buttons "Excel", "PDF", "CSV", "Print". Print stylesheet hides header, sidebar, filters.
- **Attendance Summary (formerly Analytics):** tables only (low attendance list with trend text, subject summary, section comparison) plus one weekly line chart.
- **Settings, Backup, Audit:** tabbed or stacked panels; Backup has "Create Backup", "Download Master Data (Excel)" and a restore panel with a bordered warning row and typed confirmation field.
- **Errors (404/403/500):** header strip, centered panel with the code, one line of text, "Back to Home" button.

## 8. EMPTY STATES (plain, small, useful)
Use one reusable partial `_EmptyState` (small **monochrome navy line icon or simple outline illustration, max 96px**, one-line title, one-line explanation, optional action). Colours: navy and grey only. No animation.
| Situation | Title | Text |
|---|---|---|
| Sunday | No classes today | Today is Sunday. |
| Holiday | No classes today | Holiday: <name>. |
| No periods | No classes scheduled | There are no periods today. |
| All periods over | Classes completed | All periods for today are over. |
| No attendance yet | No attendance recorded | Records appear after the first class. |
| No timetable | Timetable not available | The timetable has not been published. |
| No students | No students | There are no students in this class. |
| No notices | No notices | There are no notices. |
| No queries | No queries | There are no attendance queries. |
| No results | No results found | Change the filters and try again. |
| Nobody below minimum | No students below minimum | No student is below the required attendance. |
| Offline | Offline | Records are saved on this device and will sync when the connection returns. |
| Camera blocked | Camera access blocked | Allow camera access in the browser to scan QR codes. |
Replace the old illustration set; delete unused SVG/partials and unused CSS/JS (theme.css, ui.js animations, landing blobs, tilt, count-up, reveal).

## 9. COPY RULES - REMOVE UNWANTED / AI-STYLE TEXT
**Banned:** marketing slogans and taglines, words like "smart", "AI", "intelligent", "insights", "powered by", "seamless", "unlock", "supercharge", "magic", "journey", "welcome back!", "great job", "enjoy your day off", exclamation marks, emojis, motivational or chatty sentences, feature bullet lists on login pages, "demo account" buttons or sample credentials anywhere in the UI, developer hints, long explanatory paragraphs under titles.
**Style:** short, neutral, institutional; sentence case for sentences, Title Case for page titles and buttons; verbs on buttons; dates `dd MMM yyyy`, times 24 h.
| Old | New |
|---|---|
| "Smart BTech Attendance Ecosystem" | "Attendance Management System" |
| Landing hero text, feature chips | (remove) |
| "Use demo account", demo credential text | (remove) |
| "Good morning, <name>" hero | Page title "Dashboard" and a plain details list |
| "Needs attention" | "Low Attendance List" |
| "Analytics" / "Academic attendance analytics" | "Attendance Summary" |
| "You teach N classes" chips | "Classes Taught" table |
| "Your class has 30 students" chip | "Class Strength: 30" |
| "Attendance queries - Something wrong in a record? Ask your faculty." | "Attendance Query" |
| "All good / You are all caught up" | "No notices" / "No students below minimum" |
| "Institution ID, email or password is incorrect." | keep (security message) |
| Toasts like "Attendance saved - 28 present, 2 absent." | "Attendance saved. Present 28, Absent 2, Late 0." |
| "Student created with ID X and a signed QR code." | "Student created. Student ID: X." |
Review every Razor view, partial, JS toast and controller/service message and apply these rules.

## 10. RESPONSIVE AND ACCESSIBILITY
- Breakpoints 360, 576, 768, 992, 1200. Test at **390x844** (phone), 768 (tablet), 1440 (desktop). No horizontal page scroll; wide tables scroll inside their panel.
- WCAG AA contrast, visible focus outline, labels on every field, `aria-live` on toasts and scan results, tables with `scope`, skip-to-content link, touch targets >= 44px on phones, never colour-only meaning (status chips include text).
- Print styles for reports, student attendance report page and QR ID card.
- Performance: one CSS file (`portal.css`, < 30 KB), one small UI script; fonts `display=swap`; no layout shift.

## 11. FILE PLAN
Create: `wwwroot/css/portal.css` (tokens, shell, components, tables, forms, print). Rewrite: `Views/Shared/_Layout.cshtml`, `_AuthLayout.cshtml`, `_EmptyState.cshtml`, `_Pager.cshtml`, `_WeekGrid.cshtml`, and every view listed in sections 5-7. Simplify `site.js` (keep helpers) and remove animation code. Delete obsolete files (`theme.css`, `ui.js`, `_Illustration.cshtml` and old illustrations, unused CSS). Add `docs/UI_GUIDE.md` (tokens, components, copy rules).
Add settings `Institution:Name` (default "Institution Name") and `Institution:ShortName` to `appsettings.json`, bound to a small options class injected into the layouts (UI only).

## 12. WORKING METHOD AND ACCEPTANCE
Work in phases and run `dotnet build` (0 errors, 0 warnings) and `dotnet test` after each: (1) tokens + shell + auth layout, (2) login/role selection, (3) dashboards, (4) data pages, (5) attendance screens, (6) reports/summary/settings, (7) empty states + copy sweep + cleanup.
**Done when:** every page renders with the new interface; all three logins, marking, QR scan, offline queue, exports, queries, notices and holidays work as before; no banned words or emojis appear anywhere (search the codebase); no gradient, blur, animation or dark-mode code remains; 390px and 1440px layouts are clean; Lighthouse Accessibility >= 95; the README screenshots/notes are updated. Report what changed per phase.
