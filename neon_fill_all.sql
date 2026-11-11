-- ============================================================
-- Supplemental seed: fill ThresholdRules + InstitutionBranding
-- Run AFTER the app has reseeded from CSV on Render
-- ============================================================

-- InstitutionBranding (EF HasData won't re-insert after delete)
INSERT INTO "InstitutionBranding" ("Id", "Name", "LogoPath", "PrimaryColor", "SecondaryColor")
VALUES (1, 'NAVEEN INSTITUTION OF TECHNOLOGY', NULL, '#1e3a8a', '#3b82f6')
ON CONFLICT ("Id") DO NOTHING;

-- ThresholdRules: one rule per branch x semester for seeded semesters (1, 3, 5)
-- Fills the table completely for all seeded classes
INSERT INTO "ThresholdRules" ("BranchId", "Semester", "SectionId", "Percent", "Note")
SELECT b.Id, s.Semester, NULL, 75.00, 'Default 75% threshold'
FROM "Branches" b
CROSS JOIN (VALUES (1),(3),(5)) AS s(Semester)
WHERE b.Code IN ('CSE','CSEAI','CSEAIML','ADS','ECE','EEE','MECH','CIVIL','IT','CYBER','AIDS','AIML','IOT','DS')
ON CONFLICT DO NOTHING;

SELECT 'Supplemental seed complete: ThresholdRules + InstitutionBranding filled.' AS status;
