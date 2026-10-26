using Microsoft.AspNetCore.Identity;

try
{
    foreach (var proc in System.Diagnostics.Process.GetProcessesByName("AttendX"))
    {
        if (proc.Id != Environment.ProcessId) proc.Kill();
    }
}
catch { }

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AttendXOptions>(builder.Configuration.GetSection("AttendX"));
builder.Services.Configure<InstitutionOptions>(builder.Configuration.GetSection("Institution"));

var cs = builder.Configuration.GetConnectionString("DefaultConnection")
         ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing in appsettings.json");
builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; });
builder.Services.AddDbContextPool<AppDbContext>(o => o.UseSqlServer(cs, sql =>
{
    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
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
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());   // CSRF protection on every POST
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
builder.Services.AddScoped<DbSeeder>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try { await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync(); }
    catch (Exception ex)
    {
        log.LogCritical(ex, "Database creation/seeding failed. Check the connection string and that SQL Server LocalDB is installed.");
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
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
