using System.Globalization;
using BiboWebBot.Components;
using BiboWebBot.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Google:ClientId"] ?? string.Empty;
        options.ClientSecret = builder.Configuration["Google:ClientSecret"] ?? string.Empty;
        options.SaveTokens = true;
        options.Scope.Add("https://www.googleapis.com/auth/calendar.events");
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IVoebbAutomationService, VoebbAutomationService>();
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<IMqttPublishService, MqttPublishService>();
builder.Services.AddHostedService<DailyLoanSyncHostedService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapGet("/auth/google/login", async (HttpContext context, string? returnUrl) =>
{
    var redirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    await context.ChallengeAsync(
        GoogleDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = redirectUri });
});

app.MapGet("/auth/google/logout", async (HttpContext context, string? returnUrl) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
});

app.MapGet("/api/google-calendar/sync-earliest", [Authorize] async (
    HttpContext context,
    IGoogleCalendarService googleCalendarService,
    string dueDate,
    string? accountLabel,
    CancellationToken cancellationToken) =>
{
    var deCulture = CultureInfo.GetCultureInfo("de-DE");
    if (!DateOnly.TryParseExact(dueDate, "dd.MM.yyyy", deCulture, DateTimeStyles.None, out var parsedDueDate))
    {
        return Results.BadRequest("Ungültiges Datumsformat. Erwartet: dd.MM.yyyy");
    }

    var created = await googleCalendarService.CreateEarliestLoanEventAsync(context, parsedDueDate, accountLabel, cancellationToken);
    return created ? Results.Ok() : Results.Problem("Kalendereintrag konnte nicht erstellt werden.");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
