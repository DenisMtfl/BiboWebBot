using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BiboWebBot.Services;

public sealed class GoogleCalendarService(IConfiguration configuration, ILogger<GoogleCalendarService> logger) : IGoogleCalendarService
{
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.events";

    public async Task<bool> CreateEarliestLoanEventAsync(HttpContext context, DateOnly dueDate, string? accountLabel, CancellationToken cancellationToken = default)
    {
        var authResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var accessToken = authResult.Properties?.GetTokenValue("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("Google access token fehlt. Kalendereintrag wird übersprungen.");
            return false;
        }

        var credential = GoogleCredential.FromAccessToken(accessToken).CreateScoped(CalendarScope);
        using var calendarService = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "BiboWebBot"
        });

        var calendarEvent = BuildCalendarEvent(dueDate, accountLabel);

        try
        {
            var request = calendarService.Events.Insert(calendarEvent, "primary");
            await request.ExecuteAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google-Kalendereintrag konnte nicht erstellt werden.");
            return false;
        }
    }

    public async Task<bool> CreateEarliestLoanEventByServiceAccountAsync(DateOnly dueDate, string? accountLabel, CancellationToken cancellationToken = default)
    {
        var serviceAccountFile = configuration["Google:ServiceAccountJsonPath"];
        var calendarId = configuration["Google:CalendarId"];
        if (string.IsNullOrWhiteSpace(serviceAccountFile) || string.IsNullOrWhiteSpace(calendarId))
        {
            return false;
        }

        if (!File.Exists(serviceAccountFile))
        {
            logger.LogWarning("Google Service Account Datei nicht gefunden: {ServiceAccountFile}", serviceAccountFile);
            return false;
        }

        try
        {
            var credential = GoogleCredential.FromFile(serviceAccountFile).CreateScoped(CalendarScope);
            using var calendarService = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "BiboWebBot"
            });

            var request = calendarService.Events.Insert(BuildCalendarEvent(dueDate, accountLabel), calendarId);
            await request.ExecuteAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google-Kalendereintrag via Service Account konnte nicht erstellt werden.");
            return false;
        }
    }

    private static Event BuildCalendarEvent(DateOnly dueDate, string? accountLabel)
    {
        var dateText = dueDate.ToString("dd.MM.yyyy");
        var summary = string.IsNullOrWhiteSpace(accountLabel)
            ? $"VÖBB: Früheste Rückgabe ({dateText})"
            : $"VÖBB: Früheste Rückgabe {accountLabel} ({dateText})";

        return new Event
        {
            Summary = summary,
            Description = "Automatisch aus BiboWebBot erzeugt.",
            Start = new EventDateTime
            {
                Date = dueDate.ToString("yyyy-MM-dd")
            },
            End = new EventDateTime
            {
                Date = dueDate.AddDays(1).ToString("yyyy-MM-dd")
            }
        };
    }
}
