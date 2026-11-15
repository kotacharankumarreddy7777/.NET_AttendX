# AttendX demo logins (development seed passwords - change for real use)

| Role | Login screen | Institution ID | Email | Password |
|---|---|---|---|---|
| Admin | /admin/login | - | admin@attendx.edu.in | Admin@123 |
| HOD (CSE) | /faculty/login | FAC-CSE-001 | ishaan.chauhan@attendx.edu.in | Faculty@123 |
| Faculty | /faculty/login | FAC-CSE-002 | khushi.desai@attendx.edu.in | Faculty@123 |
| Student | /student/login | 1EV1-CSE-001 | <Name>.<StudentID>@attendx.edu.in | Student@123 |
| Parent | /parent/login | - | parent.<StudentID>@attendx.edu.in | Parent@123 |

Every faculty (FAC-<BRANCH>-001..009; 001 = HOD) and every student (see students.csv) can log in with their own Institution ID + Email + the password above.

Student email is auto-generated as `<lowercase-fullname-dot-separated>.<StudentID>@attendx.edu.in` (e.g. `tanvi.dodiya.1EV1-CSE-001@attendx.edu.in`).
Parent email is auto-generated as `parent.<StudentID>@attendx.edu.in` (e.g. `parent.1EV1-CSE-001@attendx.edu.in`).
