# Plan: Fix Student Login Authentication

## Goal
Diagnose and fix why student login fails in `AccountController.Signin`.

## Context & Root Cause Analysis
1. In `AccountController.Signin`, when `role == "student"`, student is resolved by `StudentID`. However, `m.Email` passed from form might be empty or mismatched if JS lookup hasn't completed, causing authentication/validation confusion.
2. `SignInManager` or `UserManager` checks.

## Proposed Design & Changes
1. Ensure `AccountController.Signin` robustly handles student sign-in regardless of whether `m.Email` is populated by the client form.
2. Add comprehensive tests for student login in `AttendX.Tests`.

## Validation Plan
1. `dotnet build`
2. `dotnet test`
