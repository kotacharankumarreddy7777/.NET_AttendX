using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Npgsql.EntityFrameworkCore.PostgreSQL;

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

// Npgsql uses the 'Host' keyword; SQL Server uses 'Server'. URL schemes are also accepted.
var usePostgres = rawCs.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
              || rawCs.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
              || cs.Contains("Host=", StringComparison.OrdinalIgnoreCase);

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
            npgsql.MaxBatchSize(500);
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
});

var dataProtectionKeys = Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS")
    ?? (builder.Environment.IsDevelopment() ? Path.Combine(builder.Environment.ContentRootPath, ".keys") : "/tmp/attendx-keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeys))
    .SetApplicationName("AttendX");

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireLowercase = true;
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
builder.Services.AddSingleton<ReadinessState>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });
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
builder.Services.AddScoped<IAppearanceService, AppearanceService>();
builder.Services.AddScoped<DbSeeder>();
builder.Services.AddHostedService<DatabaseInitializationService>();

var app = builder.Build();

var forwardedHeaders = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);
app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation("AttendX started. Environment={Environment}", app.Environment.EnvironmentName));

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
app.UseMiddleware<AppearanceMiddleware>();

// Liveness: process is up. No dependency checks.
app.MapGet("/health/live", (ReadinessState state) => Results.Json(new { status = "live" }));
// Readiness: process is up AND initialization + database connectivity are verified.
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var status = report.Status == HealthStatus.Healthy ? "ready" : "unhealthy";
        var state = ctx.RequestServices.GetRequiredService<ReadinessState>();
        await ctx.Response.WriteAsJsonAsync(new
        {
            status,
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
            failure = state.Failure
        });
    }
});

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

await app.RunAsync();
