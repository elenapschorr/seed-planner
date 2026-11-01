using Microsoft.EntityFrameworkCore;
using SeedPlanner.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddDbContext<SeedPlannerDbContext>(options =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .UseSnakeCaseNamingConvention());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();