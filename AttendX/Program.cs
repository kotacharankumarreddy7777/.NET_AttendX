using Microsoft.AspNetCore.Identity;
using Serilog;
using Serilog.Events;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Dashboard;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;
using AttendX.Hubs;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/attendx-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

// Neon: .env.local / .env hold DATABASE_URL pulled by `neon deploy`. Process env wins over the file.
foreach (var f in new[] { ".env.local", ".env" })
{
    if (File.Exists(f))
    {
        DotNetEnv.Env.Load(f, new DotNetEnv.LoadOptions(true, false, false));
        Log.Logger.Information("Loaded Neon env from {File}", f);
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.Configure<AttendXOptions>(builder.Configuration.GetSection("AttendX"));
builder.Services.Configure<InstitutionOptions>(builder.Configuration.GetSection("Institution"));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notification"));

// Neon writes DATABASE_URL (pooled) / DATABASE_URL_UNPOOLED (direct) into .env.local; appsettings.json calls it DefaultConnection.
if (builder.Configuration.GetConnectionString("DefaultConnection") is null)
{
    var neonCs = builder.Configuration["DATABASE_URL"] ?? builder.Configuration["ConnectionStrings:DATABASE_URL"];
    if (neonCs is not null) builder.Configuration.GetSection("ConnectionStrings")["DefaultConnection"] = neonCs;
}

var cs = builder.Configuration.GetConnectionString("DefaultConnection")
         ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing in appsettings.json");

// Npgsql doesn't understand channel_binding=require from Neon's pooled URL; drop unsupported params.
var withoutBinding = cs;
var qIdx = withoutBinding.IndexOf('?');
if (qIdx >= 0)
{
    var query = withoutBinding[(qIdx + 1)..];
    var kept = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Where(p => !p.StartsWith("channel_binding="))
        .ToList();
    withoutBinding = withoutBinding[..qIdx] + (kept.Count > 0 ? "?" + string.Join("&", kept) : "");
}

builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; });
builder.Services.AddDbContextPool<AppDbContext>(o => o.UseNpgsql(withoutBinding, npgsql =>
{
    npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null!);
}));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireNonAlphanumeric = true;
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
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.Cookie.Name = "AttendX.Auth";
    o.ExpireTimeSpan = TimeSpan.FromHours(12);     // 12-hour session for active users
    o.SlidingExpiration = true;
    // RememberMe: when checked, Identity issues a persistent cookie (no session-only limit)
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.AdminOnly, p => p.RequireRole(RoleNames.Admin))
    .AddPolicy(PolicyNames.AdminOrHod, p => p.RequireRole(RoleNames.Admin, RoleNames.Hod))
    .AddPolicy(PolicyNames.AnyStaff, p => p.RequireRole(RoleNames.Admin, RoleNames.Hod, RoleNames.Faculty))
    .AddPolicy(PolicyNames.StudentOnly, p => p.RequireRole(RoleNames.Student))
    .AddPolicy(PolicyNames.ParentOnly, p => p.RequireRole(RoleNames.Parent))
    .AddPolicy(PolicyNames.StudentOrParent, p => p.RequireRole(RoleNames.Student, RoleNames.Parent));

builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
builder.Services.AddHttpContextAccessor();

// REST API + Swagger UI at /swagger
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.Filters.AddService<ForcePasswordChangeFilter>();
}).AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "AttendX REST API",
        Version = "v1",
        Description = "Attendance, exam, leave and student APIs for mobile/3rd-party integrations. Authenticate with the same application cookie as the web UI (log in at /admin/login, /faculty/login, /student/login or /parent/login) and pass the RequestVerificationToken header for POST/PUT/DELETE.",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact { Name = "AttendX" }
    });
    o.AddSecurityDefinition("cookie", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Cookie,
        Name = "AttendX.Auth",
        Description = "Session cookie issued by the web login."
    });
    o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "cookie" }
            },
            Array.Empty<string>()
        }
    });
    var xmlFile = System.IO.Path.ChangeExtension(typeof(Program).Assembly.Location, ".xml");
    if (System.IO.File.Exists(xmlFile)) o.IncludeXmlComments(xmlFile);
    // Only document API controllers ([ApiController]) — not MVC view controllers
    o.DocInclusionPredicate((docName, apiDesc) => apiDesc.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor cad && (cad.ControllerTypeInfo?.GetCustomAttributes(typeof(ApiControllerAttribute), true)?.Any() ?? false));
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
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ILeaveService, LeaveService>();
builder.Services.AddScoped<DbSeeder>();

// Health Checks
builder.Services.AddHealthChecks();

// SignalR real-time hubs
builder.Services.AddSignalR();

// Hangfire background jobs
#pragma warning disable CS0618
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(withoutBinding));
#pragma warning restore CS0618
builder.Services.AddHangfireServer();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try { await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync(); }
    catch (Exception ex)
    {
        log.LogCritical(ex, "Database creation/seeding failed. Check the connection string and that PostgreSQL is installed.");
        throw;
    }
}

if (app.Environment.IsDevelopment()) app.UseDeveloperExceptionPage();
else { app.UseExceptionHandler("/Error"); app.UseHsts(); }
app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers.XFrameOptions = "SAMEORIGIN";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(self), geolocation=(self)";
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz");

// REST API + Swagger UI (cookie auth)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AttendX API v1");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "AttendX REST API";
});

// SignalR real-time hubs
app.MapHub<AttendanceHub>("/hubs/attendance");
app.MapHub<NotificationHub>("/hubs/notifications");

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthFilter() }
});

RecurringJob.AddOrUpdate<IAttendanceService>("auto-mark-absent", s => s.MarkAbsentAfterGracePeriodAsync(), "0 23 * * *");
RecurringJob.AddOrUpdate<INotificationService>("daily-deficiency-sweep", n => n.SendLowAttendanceSweepAsync(), "0 8 * * *");

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapControllers();   // /api/* attribute-routed API controllers
app.Run();

public class HangfireAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        return context.GetHttpContext().User.IsInRole(RoleNames.Admin);
    }
}
