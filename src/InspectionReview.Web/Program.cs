using InspectionReview.Web.Auth;
using InspectionReview.Web.Components;
using InspectionReview.Web.Data;
using InspectionReview.Web.Domain;
using InspectionReview.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// A factory rather than a scoped DbContext, because Blazor Server circuits are long-lived.
// The connection string comes from user secrets; appsettings.json leaves it empty.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not set. Set it with: " +
        "dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"<value>\"");
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Identity stores use the scoped AppDbContext that AddDbContextFactory also registers.
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddCascadingAuthenticationState();

builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/access-denied";
});

builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<LoginService>();

builder.Services.AddScoped<AccessPolicy>();
builder.Services.AddScoped<InspectionService>();
builder.Services.AddScoped<FindingService>();

var app = builder.Build();

// Development only: apply migrations, then seed.
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await SeedData.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/auth/logout", async (LoginService loginService) =>
{
    await loginService.LogoutAsync();
    return Results.LocalRedirect("/login");
});

app.Run();
