using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using BiboWebBot.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace BiboWebBot.Services;

public sealed class GoogleCalendarService(IConfiguration configuration, ILogger<GoogleCalendarService> logger, IHttpClientFactory httpClientFactory, IMemoryCache cache) : IGoogleCalendarService
{
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.events";
    private const string DefaultEventSummaryTemplate = "VÖBB: Früheste Rückgabe {Konto} ({Datum})";
    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly TimeSpan CalendarListCacheDuration = TimeSpan.FromMinutes(3);

    public async Task<bool> CreateEarliestLoanEventAsync(HttpContext context, DateOnly dueDate, string? accountLabel, string? calendarId = null, string? eventSummaryTemplate = null, CancellationToken cancellationToken = default)
    {
        var accessToken = await GetValidAccessTokenAsync(context, cancellationToken);
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

        var targetCalendarId = string.IsNullOrWhiteSpace(calendarId)
            ? configuration["Google:CalendarId"]
            : calendarId;
        if (string.IsNullOrWhiteSpace(targetCalendarId))
        {
            targetCalendarId = "primary";
        }

        var calendarEvent = BuildCalendarEvent(dueDate, accountLabel, eventSummaryTemplate);

        try
        {
            var request = calendarService.Events.Insert(calendarEvent, targetCalendarId);
            await request.ExecuteAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google-Kalendereintrag konnte nicht erstellt werden.");
            return false;
        }
    }

    public async Task<bool> CreateEarliestLoanEventByServiceAccountAsync(DateOnly dueDate, string? accountLabel, string? eventSummaryTemplate = null, CancellationToken cancellationToken = default)
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

            var request = calendarService.Events.Insert(BuildCalendarEvent(dueDate, accountLabel, eventSummaryTemplate), calendarId);
            await request.ExecuteAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google-Kalendereintrag via Service Account konnte nicht erstellt werden.");
            return false;
        }
    }

    public async Task<GoogleCalendarListResult> GetAvailableCalendarsAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
        var cacheKey = $"google-calendars:{userId}";

        // Kurzzeitiges Caching verhindert, dass jeder Seiten-Reload bzw. Blazor-Server-Reconnect
        // (z. B. beim Pausieren/Fortsetzen im Debugger) erneut die Google API anfragt und so das
        // Kontingent "Queries per minute per user" ausschöpft.
        if (cache.TryGetValue(cacheKey, out List<GoogleCalendarInfo>? cachedCalendars) && cachedCalendars is not null)
        {
            return new GoogleCalendarListResult { IsSuccess = true, Calendars = cachedCalendars };
        }

        var accessToken = await GetValidAccessTokenAsync(context, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("Google access token fehlt. Kalenderliste kann nicht geladen werden.");
            return new GoogleCalendarListResult
            {
                IsSuccess = false,
                ErrorMessage = "Keine gültige Google-Anmeldung gefunden. Bitte per Google Login anmelden."
            };
        }

        var credential = GoogleCredential.FromAccessToken(accessToken).CreateScoped(CalendarScope);
        using var calendarService = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "BiboWebBot"
        });

        try
        {
            var calendars = new List<GoogleCalendarInfo>();
            var request = calendarService.CalendarList.List();
            string? pageToken = null;

            do
            {
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync(cancellationToken);
                if (response.Items is not null)
                {
                    calendars.AddRange(response.Items.Select(item => new GoogleCalendarInfo
                    {
                        Id = item.Id,
                        Summary = string.IsNullOrWhiteSpace(item.Summary) ? item.Id : item.Summary,
                        IsPrimary = item.Primary ?? false
                    }));
                }

                pageToken = response.NextPageToken;
            } while (!string.IsNullOrWhiteSpace(pageToken));

            var ordered = calendars
                .OrderByDescending(c => c.IsPrimary)
                .ThenBy(c => c.Summary, StringComparer.OrdinalIgnoreCase)
                .ToList();

            cache.Set(cacheKey, ordered, CalendarListCacheDuration);

            return new GoogleCalendarListResult { IsSuccess = true, Calendars = ordered };
        }
        catch (Google.GoogleApiException ex)
        {
            logger.LogError(ex, "Google-Kalenderliste konnte nicht geladen werden.");
            return new GoogleCalendarListResult
            {
                IsSuccess = false,
                ErrorMessage = BuildFriendlyErrorMessage(ex)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google-Kalenderliste konnte nicht geladen werden.");
            return new GoogleCalendarListResult
            {
                IsSuccess = false,
                ErrorMessage = "Kalenderliste konnte nicht geladen werden. Bitte später erneut versuchen."
            };
        }
    }

    /// <summary>
    /// Wandelt eine Google-API-Fehlermeldung in einen für Endanwender verständlichen Text um,
    /// z. B. für ausgeschöpfte Kontingente (Rate Limit) oder fehlende Berechtigungen (Scopes).
    /// </summary>
    private static string BuildFriendlyErrorMessage(Google.GoogleApiException ex)
    {
        var reason = ex.Error?.Errors?.FirstOrDefault()?.Reason;
        var message = ex.Error?.Message ?? ex.Message;

        if (string.Equals(reason, "rateLimitExceeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reason, "userRateLimitExceeded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Quota exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return "Google-Kontingent kurzzeitig überschritten (zu viele Anfragen pro Minute). Bitte 1-2 Minuten warten und erneut versuchen.";
        }

        if (ex.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            return "Fehlende Kalender-Berechtigung. Bitte per Google Logout abmelden und erneut anmelden.";
        }

        if (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            return "Google-Anmeldung ist abgelaufen. Bitte per Google Logout abmelden und erneut anmelden.";
        }

        return $"Google-Kalenderliste konnte nicht geladen werden ({(int)ex.HttpStatusCode} {ex.HttpStatusCode}).";
    }

    /// <summary>
    /// Liefert einen gültigen Google Access Token aus dem Auth-Cookie. Ist der Token abgelaufen
    /// (Google Access Tokens sind nur ca. 1 Stunde gültig) und liegt ein refresh_token vor,
    /// wird automatisch ein neuer Access Token angefordert und im Auth-Cookie aktualisiert.
    /// </summary>
    private async Task<string?> GetValidAccessTokenAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var authResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!authResult.Succeeded || authResult.Properties is null)
        {
            return null;
        }

        var accessToken = authResult.Properties.GetTokenValue("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var expiresAtRaw = authResult.Properties.GetTokenValue("expires_at");
        if (DateTimeOffset.TryParse(expiresAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresAt)
            && expiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            // Access Token ist noch ausreichend lange gültig.
            return accessToken;
        }

        var refreshToken = authResult.Properties.GetTokenValue("refresh_token");
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            logger.LogWarning("Google Access Token ist abgelaufen und es liegt kein refresh_token vor. Bitte erneut per Google Login anmelden.");
            return accessToken;
        }

        var refreshed = await RefreshAccessTokenAsync(refreshToken, cancellationToken);
        if (refreshed is null)
        {
            return accessToken;
        }

        var tokens = authResult.Properties.GetTokens()
            .Where(t => t.Name is not ("access_token" or "expires_at"))
            .ToList();
        tokens.Add(new AuthenticationToken { Name = "access_token", Value = refreshed.Value.AccessToken });
        tokens.Add(new AuthenticationToken { Name = "expires_at", Value = refreshed.Value.ExpiresAt.ToString("o", CultureInfo.InvariantCulture) });
        authResult.Properties.StoreTokens(tokens);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, authResult.Principal, authResult.Properties);

        return refreshed.Value.AccessToken;
    }

    private async Task<(string AccessToken, DateTimeOffset ExpiresAt)?> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var clientId = configuration["Google:ClientId"];
        var clientSecret = configuration["Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            using var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            });

            using var response = await httpClient.PostAsync(GoogleTokenEndpoint, requestContent, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Google Access Token Refresh fehlgeschlagen: {StatusCode} {Payload}", response.StatusCode, payload);
                return null;
            }

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var newAccessToken = root.TryGetProperty("access_token", out var accessTokenElement) ? accessTokenElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(newAccessToken))
            {
                return null;
            }

            var expiresIn = root.TryGetProperty("expires_in", out var expiresInElement) ? expiresInElement.GetInt32() : 3600;
            return (newAccessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google Access Token Refresh fehlgeschlagen.");
            return null;
        }
    }

    private static Event BuildCalendarEvent(DateOnly dueDate, string? accountLabel, string? eventSummaryTemplate)
    {
        var dateText = dueDate.ToString("dd.MM.yyyy");
        var accountText = string.IsNullOrWhiteSpace(accountLabel) ? string.Empty : accountLabel;
        var template = string.IsNullOrWhiteSpace(eventSummaryTemplate) ? DefaultEventSummaryTemplate : eventSummaryTemplate;

        var summary = template
            .Replace("{Konto}", accountText, StringComparison.OrdinalIgnoreCase)
            .Replace("{Datum}", dateText, StringComparison.OrdinalIgnoreCase);

        // Doppelte Leerzeichen entfernen, falls {Konto} leer war.
        summary = string.Join(' ', summary.Split(' ', StringSplitOptions.RemoveEmptyEntries));

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
