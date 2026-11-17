using System.Security.Claims;
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

// Plant types are shared by all users. Only the administrators listed in the setting
// Admin:Email (one or more addresses separated by ; or ,) may change them.
// Without the setting nobody is an administrator.
var adminEmails = (builder.Configuration["Admin:Email"] ?? "")
    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("PlantTypeAdmin", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            ctx.User.FindFirst(ClaimTypes.Email)?.Value is { } email &&
            adminEmails.Contains(email, StringComparer.OrdinalIgnoreCase)));

builder.Services.AddRazorPages(o =>
    {
        o.Conventions.AuthorizeFolder("/Plants");
        o.Conventions.AuthorizeFolder("/Calendar");
        o.Conventions.AuthorizeFolder("/Pots");
        o.Conventions.AuthorizeFolder("/Saucers");
        o.Conventions.AuthorizeFolder("/RaisedBeds");
        o.Conventions.AuthorizeFolder("/Roles");
        o.Conventions.AuthorizeFolder("/Garden");
        o.Conventions.AuthorizeFolder("/Demand");
        o.Conventions.AuthorizeFolder("/Plans");
    })
    .AddMvcOptions(o => o.MaxModelBindingCollectionSize = 5000)
    .AddViewLocalization();
// The plan page posts a few values for every crop and variety: more than the default 1024 values of a form
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.ValueCountLimit = 20000);

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
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
