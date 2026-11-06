using Microsoft.EntityFrameworkCore;
using SeedPlanner.Infrastructure;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SeedPlanner.Infrastructure.ICurrentUser, SeedPlanner.Api.Security.CurrentUser>();

builder.Services
    .AddIdentity<Microsoft.AspNetCore.Identity.IdentityUser, Microsoft.AspNetCore.Identity.IdentityRole>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<SeedPlanner.Infrastructure.SeedPlannerDbContext>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Login";
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddRazorPages(o => o.Conventions.AuthorizeFolder("/Plants"))
    .AddViewLocalization();
builder.Services.AddDbContext<SeedPlannerDbContext>(options =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .UseSnakeCaseNamingConvention());

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SeedPlanner.Infrastructure.SeedPlannerDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

var supportedCultures = new[] { "de", "en", "ru" };

app.UseRequestLocalization(o => o
    .SetDefaultCulture("en")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

// Language switcher: stores the choice in a cookie, then goes back to the page
app.MapGet("/culture", (string c, string? returnUrl, HttpContext ctx) =>
{
    if (!supportedCultures.Contains(c)) return Results.BadRequest();

    ctx.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(c)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

    return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
});

app.Run();