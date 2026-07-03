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
        // "offline" + "consent" sorgen dafür, dass Google einen refresh_token ausstellt,
        // damit der Access Token nach Ablauf (~1 Stunde) automatisch erneuert werden kann.
        options.AccessType = "offline";
        options.AdditionalAuthorizationParameters["prompt"] = "consent";
        options.Scope.Add("https://www.googleapis.com/auth/calendar.events");
        // Zusätzlicher Lese-Scope wird benötigt, damit CalendarList.List() (Kalenderliste
        // für das Dropdown) funktioniert - "calendar.events" allein reicht dafür nicht aus.
        options.Scope.Add("https://www.googleapis.com/auth/calendar.readonly");
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
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
    string? calendarId,
    string? eventName,
    CancellationToken cancellationToken) =>
{
    var deCulture = CultureInfo.GetCultureInfo("de-DE");
    if (!DateOnly.TryParseExact(dueDate, "dd.MM.yyyy", deCulture, DateTimeStyles.None, out var parsedDueDate))
    {
        return Results.BadRequest("Ungültiges Datumsformat. Erwartet: dd.MM.yyyy");
    }

    var created = await googleCalendarService.CreateEarliestLoanEventAsync(context, parsedDueDate, accountLabel, calendarId, eventName, cancellationToken);
    return created ? Results.Ok() : Results.Problem("Kalendereintrag konnte nicht erstellt werden.");
});

app.MapGet("/api/google-calendar/calendars", [Authorize] async (
    HttpContext context,
    IGoogleCalendarService googleCalendarService,
    CancellationToken cancellationToken) =>
{
    var result = await googleCalendarService.GetAvailableCalendarsAsync(context, cancellationToken);
    return Results.Ok(result);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
