using Microsoft.AspNetCore.Identity;
using Npgsql.EntityFrameworkCore.PostgreSQL;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    var urls = $"http://0.0.0.0:{port}";
    builder.WebHost.UseUrls(urls);
}

builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; });

var rawCs = Environment.GetEnvironmentVariable("DATABASE_URL")
          ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
          ?? builder.Configuration.GetConnectionString("DefaultConnection")
          ?? "";

if (string.IsNullOrWhiteSpace(rawCs))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' or environment variable DATABASE_URL is missing or empty.");
}

var cs = ConnectionHelper.ConvertToNpgsqlConnectionString(rawCs);

var usePostgres = rawCs.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
              || rawCs.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

var qrSecret = Environment.GetEnvironmentVariable("QR_SECRET");
if (!string.IsNullOrWhiteSpace(qrSecret))
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AttendX:QrSecret"] = qrSecret });
}
if (string.IsNullOrWhiteSpace(builder.Configuration["AttendX:QrSecret"]) || builder.Configuration["AttendX:QrSecret"]!.Length < 16)
{
    throw new InvalidOperationException("AttendX:QrSecret is required (min 16 chars) via environment variable QR_SECRET or appsettings. Refusing to start with unset/default secret.");
}
builder.Services.Configure<AttendXOptions>(builder.Configuration.GetSection("AttendX"));
builder.Services.Configure<InstitutionOptions>(builder.Configuration.GetSection("Institution"));

builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (usePostgres)
    {
        o.UseNpgsql(cs, npgsql =>
        {
            npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
            npgsql.CommandTimeout(120);
        });
    }
    else
    {
        o.UseSqlServer(cs, sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            sql.CommandTimeout(120);
        });
    }
    o.ConfigureWarnings(wb => wb.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddDataProtection();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 5;
        o.Password.RequireDigit = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.AdminOnly, p => p.RequireRole(RoleNames.Admin))
    .AddPolicy(PolicyNames.AdminOrHod, p => p.RequireRole(RoleNames.Admin, RoleNames.Hod))
    .AddPolicy(PolicyNames.AnyStaff, p => p.RequireRole(RoleNames.Admin, RoleNames.Hod, RoleNames.Faculty))
    .AddPolicy(PolicyNames.StudentOnly, p => p.RequireRole(RoleNames.Student));

builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.Filters.AddService<ForcePasswordChangeFilter>();
});

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ForcePasswordChangeFilter>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IStudentIdService, StudentIdService>();
builder.Services.AddScoped<IQrService, QrService>();
builder.Services.AddScoped<IPhotoService, PhotoService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<IFacultyService, FacultyService>();
builder.Services.AddScoped<ITimetableService, TimetableService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<INoticeService, NoticeService>();
builder.Services.AddScoped<IStudentPortalService, StudentPortalService>();
builder.Services.AddScoped<IMasterDataExportService, MasterDataExportService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<DbSeeder>();

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.UseDeveloperExceptionPage();
else { app.UseExceptionHandler("/Error"); }

app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers.XFrameOptions = "SAMEORIGIN";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(self), geolocation=(self)";
    await next();
});

if (!app.Environment.IsDevelopment() && string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"), "https", StringComparison.OrdinalIgnoreCase) == false)
{
    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HTTPS_REDIRECT"))) app.UseHttpsRedirection();
}
app.UseResponseCompression();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

// Seed BEFORE opening the port so no request hits an empty DB mid-reseed.
// Render port check retries, so blocking here is safe; total seed budget = 25 min.
await app.StartAsync();
{
    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    var log = loggerFactory.CreateLogger("Program");
    try
    {
        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(25));
        await seeder.SeedAsync(cts.Token);
        log.LogInformation("Database seeding completed.");
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Database seeding failed — continuing to serve requests.");
    }
}
await app.WaitForShutdownAsync();
