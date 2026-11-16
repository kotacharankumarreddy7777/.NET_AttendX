using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AttendX.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Semester> Semesters => Set<Semester>();
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
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamTimetable> ExamTimetables => Set<ExamTimetable>();
    public DbSet<ExamRegistration> ExamRegistrations => Set<ExamRegistration>();
    public DbSet<ExamMark> ExamMarks => Set<ExamMark>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AcademicYear>().HasIndex(x => x.Name).IsUnique();

        b.Entity<Branch>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Branch>().HasIndex(x => x.Name).IsUnique();

        b.Entity<Section>().HasIndex(x => new { x.BranchId, x.Name }).IsUnique();

        b.Entity<Subject>().HasIndex(x => x.SubjectCode).IsUnique();
        b.Entity<Subject>().HasIndex(x => new { x.BranchId, x.Semester });

        // Duplicate ACTIVE student IDs are impossible; an ID may be reused after the old student is deactivated.
        b.Entity<Student>().HasIndex(x => x.StudentID).IsUnique();
        b.Entity<Student>().HasIndex(x => new { x.BranchId, x.SectionId, x.Semester });
        b.Entity<Student>().HasIndex(x => x.Email);
        b.Entity<Student>().HasIndex(x => x.IdentityUserId);

        b.Entity<Faculty>().HasIndex(x => x.FacultyID).IsUnique();
        b.Entity<Faculty>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Faculty>().HasIndex(x => x.IdentityUserId);

        b.Entity<FacultySubjectMapping>()
            .HasIndex(x => new { x.FacultyId, x.SubjectId, x.SectionId, x.AcademicYearId }).IsUnique();

        b.Entity<PeriodDefinition>().HasIndex(x => x.PeriodNo).IsUnique();

        // A section can have only one class per day+period PER SEMESTER, a faculty can teach only one class per day+period PER SEMESTER.
        // (Odd and even semesters of the same section run at different times of the year, so their slots may coincide.)
        b.Entity<Timetable>().HasIndex(x => new { x.AcademicYearId, x.SectionId, x.Day, x.Period, x.Semester })
            .IsUnique();
        b.Entity<Timetable>().HasIndex(x => new { x.AcademicYearId, x.FacultyId, x.Day, x.Period, x.Semester })
            .IsUnique();
        // One room cannot host two different sections at the same day+period+semester.
        b.Entity<Timetable>().HasIndex(x => new { x.AcademicYearId, x.RoomNo, x.Day, x.Period, x.Semester })
            .IsUnique();

        b.Entity<AttendanceSession>().HasIndex(x => new { x.TimetableId, x.Date }).IsUnique();
        b.Entity<AttendanceSession>().Property(x => x.Latitude).HasPrecision(8, 3);
        b.Entity<AttendanceSession>().Property(x => x.Longitude).HasPrecision(8, 3);

        // Core duplicate protection: a student can have only ONE record per date + period.
        b.Entity<Attendance>().HasIndex(x => new { x.StudentId, x.Date, x.Period }).IsUnique();
        // Dashboard/report aggregates filter on a date range, so Date needs its own index with the aggregated columns included.
        b.Entity<Attendance>().HasIndex(x => x.Date)
            .IncludeProperties(nameof(Models.Attendance.Status), nameof(Models.Attendance.BranchId), nameof(Models.Attendance.Semester), nameof(Models.Attendance.SectionId),
                nameof(Models.Attendance.SubjectId), nameof(Models.Attendance.FacultyId), nameof(Models.Attendance.AttendanceSessionId), nameof(Models.Attendance.MarkedAt));
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

        // Exam entities configuration
        b.Entity<Exam>().HasIndex(x => new { x.AcademicYearId, x.Semester, x.Name }).IsUnique();
        // A section sits only one paper per subject per exam date (no double-booking of the same paper).
        b.Entity<ExamTimetable>().HasIndex(x => new { x.ExamId, x.SubjectId, x.SectionId, x.ExamDate }).IsUnique();
        // Exam centers: room clash prevented at DB level — same room cannot host two exams at the same time on the same date.
        b.Entity<ExamTimetable>().HasIndex(x => new { x.RoomNo, x.ExamDate, x.StartTime }).IsUnique();
        b.Entity<ExamRegistration>().HasIndex(x => new { x.ExamId, x.StudentId }).IsUnique();
        b.Entity<ExamMark>().HasIndex(x => new { x.ExamTimetableId, x.StudentId }).IsUnique();
        b.Entity<ExamMark>().Property(x => x.InternalMark).HasPrecision(5, 2);
        b.Entity<ExamMark>().Property(x => x.ExternalMark).HasPrecision(5, 2);

        // Leave entities configuration
        b.Entity<LeaveType>().HasIndex(x => x.Name).IsUnique();
        b.Entity<LeaveBalance>().HasIndex(x => new { x.StudentId, x.AcademicYearId }).IsUnique();
        b.Entity<LeaveApplication>().HasIndex(x => x.StudentId);
        b.Entity<LeaveApplication>().HasIndex(x => x.Status);
        b.Entity<Parent>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Parent>().HasIndex(x => x.StudentId).IsUnique();

        // Semester entity
        b.Entity<Semester>().HasIndex(x => new { x.AcademicYearId, x.Number }).IsUnique();

        // Notification log
        b.Entity<NotificationLog>().HasIndex(x => x.CreatedAt);
        b.Entity<NotificationLog>().HasIndex(x => new { x.Channel, x.Status });

        b.Entity<AuditLog>().HasIndex(x => x.Timestamp);
        b.Entity<AuditLog>().HasIndex(x => x.BranchId);

        // SQL Server rejects "multiple cascade paths", so every relationship of OUR tables is Restrict (soft-delete is used anyway).
        foreach (var fk in b.Model.GetEntityTypes()
                     .Where(e => e.ClrType.Namespace == typeof(Student).Namespace)
                     .SelectMany(e => e.GetForeignKeys()))
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }

        // PostgreSQL requires DateTimeKind.Utc for timestamp with time zone columns.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            foreach (var prop in entityType.GetProperties().Where(p => p.ClrType == typeof(DateTime)))
            {
                prop.SetValueConverter(new ValueConverter<DateTime, DateTime>(
                    v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                    v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            }
            foreach (var prop in entityType.GetProperties().Where(p => p.ClrType == typeof(DateTime?)))
            {
                prop.SetValueConverter(new ValueConverter<DateTime?, DateTime?>(
                    v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v,
                    v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v));
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            foreach (var prop in entry.Properties)
            {
                if (prop.CurrentValue is DateTime dt && dt.Kind != DateTimeKind.Utc)
                    prop.CurrentValue = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                if (prop.OriginalValue is DateTime odt && odt.Kind != DateTimeKind.Utc)
                    prop.OriginalValue = DateTime.SpecifyKind(odt, DateTimeKind.Utc);
            }
        }
        return base.SaveChangesAsync(ct);
    }
}
