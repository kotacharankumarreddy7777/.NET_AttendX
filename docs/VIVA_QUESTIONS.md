# Viva questions and short answers

1. **Why MVC?** Separation of concerns: models (data), views (UI), controllers (request handling); business rules live in services.
2. **Why do business rules live in services, not controllers/JS?** JavaScript can be bypassed; services are testable and reusable by an API/mobile app.
3. **How do you stop duplicate attendance?** Unique index (StudentId, Date, Period) + existence check + catching `DbUpdateException` for concurrent requests.
4. **Which clock is used?** The server clock (`IClock`). The browser clock is display-only.
5. **How does the QR stay secure?** HMAC-SHA256 signature with a server secret; tampered or old QR (after ID change) is rejected; scans need a rotating session token.
6. **What is replay protection here?** Session tokens expire after ~30-60 s and are bound to one session; a duplicate scan returns "already marked".
7. **Difference between authentication and authorization?** Login identity vs permitted actions (policies/roles + resource checks like "own class only").
8. **How is HOD limited to a department?** `ICurrentUser.GetScopeBranchIdAsync()` is applied in every query (students, reports, audit, analytics).
9. **Why soft delete?** Attendance history must survive student/faculty deactivation; all FKs are `Restrict`.
10. **How is the Student ID generated?** Semester + section + branch code + next 3-digit sequence among active students; retry on conflict.
11. **Year of a student?** Calculated from semester: (sem+1)/2; never entered manually.
12. **What does offline sync do?** Browser queues records in localStorage; on reconnect the server re-validates faculty, class, window (captured time + max delay), student, duplicates.
13. **How are percentages calculated?** (Present + Late x weight)/Total; the late weight is configurable.
14. **How is risk explained?** Compared with the resolved threshold: Safe/Watch/At Risk/Critical, plus classes needed to recover and a 4-week projection.
15. **Is the analytics "AI"?** It is statistics/rule-based (regression slope, z-score), no external AI service; each result is explainable.
16. **How are passwords stored?** Salted PBKDF2 hashes via ASP.NET Core Identity; faculty creation does not store a second password.
17. **CSRF/XSS/SQL injection protection?** Antiforgery tokens, Razor encoding, EF parameterised queries.
18. **How does backup work?** SQL Server `BACKUP DATABASE ... WITH CHECKSUM`; restore runs `RESTORE VERIFYONLY` first, then restores in single-user mode.
19. **What are the limitations?** See README section 10 (not compiled in the authoring environment, CDN dependency, no student login, etc.).
20. **How would you scale it?** Move to full SQL Server, add caching and background jobs, expose the services as REST API, containerise.
