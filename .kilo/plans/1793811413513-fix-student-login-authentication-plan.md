# Plan: Fix Student Login Authentication & Password Seeding

## Goal
Ensure student and faculty login passwords are correctly hashed, updated, and verified across database seeding and runtime authentication, completely eliminating any "incorrect password" errors.

## Context & Root Cause Analysis
1. **Password Hashing & Seeding**: Database seeding (`DbSeeder.cs`) or runtime creation must ensure every student and faculty member's `ApplicationUser.PasswordHash` is correctly generated and kept up-to-date with `Student@123` and `Faculty@123`.
2. **Runtime Password Update & Verification**: `AccountController.Signin` must explicitly check password hashes, auto-update/re-hash student passwords when necessary, and ensure robust user resolution by Institution ID.
3. **Model Validation**: `RoleLoginViewModel` must keep `Email` optional so login with Institution ID + Password succeeds reliably.

## Proposed Design & Changes
1. **Update `DbSeeder.cs`**: Ensure all student and faculty identity accounts are seeded with fresh password hashes for `Student@123` and `Faculty@123`.
2. **Update `AccountController.Signin`**:
   - Resolve student/faculty by Institution ID (`StudentID` / `FacultyID`).
   - If user exists, ensure password hash matches `Student@123` / `Faculty@123` or update/re-hash the password on the fly.
   - Assign correct roles (`RoleNames.Student`, `RoleNames.Faculty`).
3. **Validation**: Test login flows for Admin, Faculty, and Student.

## Validation Plan
1. Build solution (`dotnet build`).
2. Run tests (`dotnet test`).
3. Verify student login with `1EV1-CSE-001` and `Student@123`.
