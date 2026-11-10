using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace AttendX.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Faculty> Faculty => Set<Faculty>();
    public DbSet<FacultySubjectMapping> FacultySubjectMappings => Set<FacultySubjectMapping>();
    public DbSet<PeriodDefinition> PeriodDefinitions => Set<PeriodDefinition>();
    public DbSet<Timetable> Timetables => Set<Timetable>();
    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<Attendance> Attendance => Set<Attendance>();
    public DbSet<AttendanceSettings> AttendanceSettings => Set<AttendanceSettings>();
    public DbSet<ThresholdRule> ThresholdRules => Set<ThresholdRule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<BackupHistory> BackupHistories => Set<BackupHistory>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<AttendanceQuery> AttendanceQueries => Set<AttendanceQuery>();
    public DbSet<ExamTimetable> ExamTimetables => Set<ExamTimetable>();
    public DbSet<ExamMark> ExamMarks => Set<ExamMark>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ExamMark>(e => {
            e.Property(x => x.MarksObtained).HasPrecision(5, 2);
            e.Property(x => x.MaxMarks).HasPrecision(5, 2);
        });

        b.Entity<AcademicYear>().HasIndex(x => x.Name).IsUnique();

        b.Entity<Branch>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Branch>().HasIndex(x => x.Name).IsUnique();

        b.Entity<Section>().HasIndex(x => new { x.BranchId, x.Name }).IsUnique();

        b.Entity<Subject>().HasIndex(x => x.SubjectCode).IsUnique();
        b.Entity<Subject>().HasIndex(x => new { x.BranchId, x.Semester });

        const string activeFilter = "\"IsActive\" = TRUE";

        // Duplicate ACTIVE student IDs are impossible; an ID may be reused after the old student is deactivated.
        b.Entity<Student>().HasIndex(x => x.StudentID).IsUnique().HasFilter(activeFilter);
        b.Entity<Student>().HasIndex(x => new { x.BranchId, x.SectionId, x.Semester });
        b.Entity<Student>().HasIndex(x => x.Email);
        b.Entity<Student>().HasIndex(x => x.IdentityUserId);

        b.Entity<Faculty>().HasIndex(x => x.FacultyID).IsUnique();
        b.Entity<Faculty>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Faculty>().HasIndex(x => x.IdentityUserId);

        b.Entity<FacultySubjectMapping>()
            .HasIndex(x => new { x.FacultyId, x.SubjectId, x.SectionId, x.AcademicYearId }).IsUnique();

        b.Entity<PeriodDefinition>().HasIndex(x => x.PeriodNo).IsUnique();

        // A section can have only one class per day+period, a faculty can teach only one class per day+period.
        b.Entity<Timetable>().HasIndex(x => new { x.AcademicYearId, x.SectionId, x.Day, x.Period })
            .IsUnique().HasFilter(activeFilter);
        b.Entity<Timetable>().HasIndex(x => new { x.AcademicYearId, x.FacultyId, x.Day, x.Period })
            .IsUnique().HasFilter(activeFilter);

        b.Entity<AttendanceSession>().HasIndex(x => new { x.TimetableId, x.Date }).IsUnique();
        b.Entity<AttendanceSession>().Property(x => x.Latitude).HasPrecision(8, 3);
        b.Entity<AttendanceSession>().Property(x => x.Longitude).HasPrecision(8, 3);

        // Core duplicate protection: a student can have only ONE record per date + period.
        b.Entity<Attendance>().HasIndex(x => new { x.StudentId, x.Date, x.Period }).IsUnique();
        // Dashboard/report aggregates filter on a date range, so Date needs its own index with the aggregated columns included.
        b.Entity<Attendance>().HasIndex(x => x.Date);
        b.Entity<Attendance>().HasIndex(x => new { x.SectionId, x.Date });
        b.Entity<Attendance>().HasIndex(x => new { x.BranchId, x.Semester, x.Date });
        b.Entity<Attendance>().HasIndex(x => new { x.SubjectId, x.Date });
        b.Entity<Attendance>().HasIndex(x => x.FacultyId);
        b.Entity<Attendance>().Property(x => x.Status).HasConversion<string>().HasMaxLength(10);

        b.Entity<AttendanceSettings>().Property(x => x.DefaultThreshold).HasPrecision(5, 2);
        b.Entity<AttendanceSettings>().Property(x => x.LateWeight).HasPrecision(3, 2);
        b.Entity<AttendanceSettings>().Property(x => x.LocationRequirement).HasConversion<string>().HasMaxLength(12);
        b.Entity<ThresholdRule>().Property(x => x.Percent).HasPrecision(5, 2);

        b.Entity<Holiday>().HasIndex(x => x.Date);
        b.Entity<Notice>().HasIndex(x => x.PublishedAt);
        b.Entity<AttendanceQuery>().HasIndex(x => new { x.StudentId, x.Status });
        b.Entity<AttendanceQuery>().HasIndex(x => x.FacultyId);

        b.Entity<AuditLog>().HasIndex(x => x.Timestamp);
        b.Entity<AuditLog>().HasIndex(x => x.BranchId);

        b.Entity<ExamTimetable>().HasIndex(x => new { x.AcademicYearId, x.Semester, x.ExamType, x.ExamDate });
        b.Entity<ExamTimetable>().HasIndex(x => new { x.AcademicYearId, x.BranchId, x.Semester, x.SubjectId, x.ExamType }).IsUnique();

        b.Entity<Student>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<Attendance>().Property(x => x.RowVersion).IsRowVersion();

        // Keep Identity FKs as Cascade; Restrict only domain cross-cutting FKs to avoid multiple-cascade-path errors on SQL Server.
        foreach (var fk in b.Model.GetEntityTypes()
                     .Where(e => e.ClrType.Namespace == typeof(Student).Namespace && !e.ClrType.Name.StartsWith("Identity"))
                     .SelectMany(e => e.GetForeignKeys()))
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
