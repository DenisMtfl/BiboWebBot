using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace BiboWebBot.VoebbParsing;

public sealed class VoebbAutomationService : IVoebbAutomationService
{
    private const string StartUrl = "https://www.voebb.de/aDISWeb/app/prod00";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<VoebbOperationResult> LoadLoansAsync(VoebbCredentials credentials, CancellationToken cancellationToken = default)
        => ExecuteHttpAsync(credentials, cancellationToken);

    public Task<VoebbOperationResult> RenewLoanAsync(VoebbCredentials credentials, int renewIndex, CancellationToken cancellationToken = default)
        => ExecuteHttpAsync(credentials, cancellationToken);

    public Task<VoebbOperationResult> RenewAllAsync(VoebbCredentials credentials, CancellationToken cancellationToken = default)
        => ExecuteHttpAsync(credentials, cancellationToken);

    private static async Task<VoebbOperationResult> ExecuteHttpAsync(VoebbCredentials credentials, CancellationToken cancellationToken)
    {
        var logs = new List<string>();

        void Trace(string message)
        {
            logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        if (string.IsNullOrWhiteSpace(credentials.CardId) || string.IsNullOrWhiteSpace(credentials.Password))
        {
            Trace("Abbruch: Zugangsdaten unvollständig.");
            return new VoebbOperationResult { Success = false, Message = "Bibliotheksausweis und Passwort sind erforderlich.", Logs = logs };
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(3));

            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(2)
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 BiboWebBot/1.0");

            Trace("Lade Startseite (HTTP). ");
            var currentUri = new Uri(StartUrl, UriKind.Absolute);
            var html = await client.GetStringAsync(currentUri, cts.Token);

            if (!ContainsLoginFields(html))
            {
                Trace("Loginformular nicht direkt sichtbar, versuche Login-Link.");
                var loginUri = FindFirstLinkByText(currentUri, html, ["Anmelden", "Mein Konto", "Login"]);
                if (loginUri is not null)
                {
                    Trace($"Login-Link gefunden: {loginUri}");
                    currentUri = loginUri;
                    html = await client.GetStringAsync(currentUri, cts.Token);
                }
                else
                {
                    Trace("Kein Login-Link gefunden.");
                }
            }

            if (!ContainsLoginFields(html))
            {
                Trace("Loginformular nicht als Link gefunden, versuche HTML-Form-Trigger.");
                if (TryBuildTriggerFormRequest(currentUri, html, out var triggerUri, out var triggerMethod, out var triggerValues, out var triggerDescription))
                {
                    Trace($"Sende Trigger-Form: {triggerDescription} | Methode={triggerMethod.Method} | Ziel={triggerUri}");
                    var triggerResult = await SendFormRequestAsync(client, triggerUri, triggerMethod, triggerValues, cts.Token);
                    currentUri = triggerResult.Uri;
                    html = triggerResult.Html;
                    Trace($"Trigger-Form abgeschlossen. Finale URL: {currentUri}");
                }
                else
                {
                    Trace("Kein passender HTML-Form-Trigger für Login gefunden.");
                }
            }

            if (!ContainsLoginFields(html))
            {
                Trace("Loginformular noch nicht gefunden, versuche aDIS-Fallback-Aktionen.");
                var fallbackSteps = BuildAdisFallbackActions(currentUri, html);
                foreach (var step in fallbackSteps)
                {
                    Trace($"Fallback-Request: {step.Description} | Methode={step.Method.Method} | Ziel={step.Uri}");
                    var response = await SendFormRequestAsync(client, step.Uri, step.Method, step.Values, cts.Token);
                    currentUri = response.Uri;
                    html = response.Html;
                    Trace($"Fallback abgeschlossen. Finale URL: {currentUri}");

                    if (ContainsLoginFields(html))
                    {
                        Trace("Loginformular nach Fallback gefunden.");
                        break;
                    }
                }
            }

            if (!ContainsLoginFields(html))
            {
                Trace("Loginformular via HTTP nicht gefunden.");
                return new VoebbOperationResult
                {
                    Success = false,
                    Message = "Loginformular konnte nicht gefunden werden.",
                    Logs = logs
                };
            }

            Trace("Sende Loginformular (HTTP). ");
            var formAction = FindLoginFormAction(currentUri, html) ?? currentUri;
            var formValues = ExtractHiddenInputs(html);
            formValues["L#AUSW"] = credentials.CardId;
            formValues["LPASSW"] = credentials.Password;
            formValues["LLOGIN"] = "Anmelden";

            using (var requestContent = new FormUrlEncodedContent(formValues))
            using (var response = await client.PostAsync(formAction, requestContent, cts.Token))
            {
                response.EnsureSuccessStatusCode();
                currentUri = response.RequestMessage?.RequestUri ?? formAction;
                html = await response.Content.ReadAsStringAsync(cts.Token);
            }

            if (ContainsLoginFields(html))
            {
                Trace("Login fehlgeschlagen: Loginformular weiterhin vorhanden.");
                return new VoebbOperationResult
                {
                    Success = false,
                    Message = "Login fehlgeschlagen. Bitte Zugangsdaten prüfen.",
                    Logs = logs
                };
            }

            Trace("Login erfolgreich, navigiere zur Ausleihansicht.");
            html = await NavigateToLoansHtmlAsync(client, currentUri, html, cts.Token, Trace);

            var loans = ParseLoansFromHtml(html);
            Trace($"Ausleihen aus HTML-Tabelle gelesen: {loans.Count}.");

            if (loans.Count == 0)
            {
                var fallbackLoans = ParseLoansFromTextFallback(html);
                Trace($"Ausleihen aus Text-Fallback gelesen: {fallbackLoans.Count}.");
                loans = fallbackLoans;
            }

            Trace($"Ausleihen gesamt gelesen: {loans.Count}.");

            return new VoebbOperationResult
            {
                Success = true,
                Message = loans.Count == 0 ? "Es wurden keine Ausleihen gefunden." : $"Ausleihen geladen: {loans.Count}.",
                Loans = loans,
                Logs = logs
            };
        }
        catch (TaskCanceledException ex)
        {
            Trace($"Timeout (HTTP): {ex.Message}");
            return new VoebbOperationResult { Success = false, Message = "Zeitüberschreitung bei der Kommunikation mit VÖBB.", Logs = logs };
        }
        catch (Exception ex)
        {
            Trace($"Unerwarteter Fehler (HTTP): {ex.Message}");
            return new VoebbOperationResult { Success = false, Message = $"Unerwarteter Fehler: {ex.Message}", Logs = logs };
        }
    }

    private static bool ContainsLoginFields(string html)
    {
        var hasCardField = Regex.IsMatch(
            html,
            "name=['\"]L(?:#|%23|&#35;|\\\\#)AUSW['\"]",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var hasPasswordField = Regex.IsMatch(
            html,
            "name=['\"]LPASSW['\"]|type=['\"]password['\"]",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return hasCardField && hasPasswordField;
    }

    private static Uri? FindFirstLinkByText(Uri baseUri, string html, IReadOnlyList<string> keywords)
    {
        var linkMatches = Regex.Matches(html, "<a[^>]*href=['\"](?<href>[^'\"]+)['\"][^>]*>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in linkMatches)
        {
            var text = WebUtility.HtmlDecode(Regex.Replace(match.Groups["text"].Value, "<.*?>", string.Empty));
            if (!keywords.Any(key => text.Contains(key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (Uri.TryCreate(baseUri, href, out var resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static Uri? FindLoginFormAction(Uri baseUri, string html)
    {
        var formMatch = Regex.Match(html, "<form[^>]*action=['\"](?<action>[^'\"]+)['\"][^>]*>.*?L#AUSW.*?</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!formMatch.Success)
        {
            return null;
        }

        var action = WebUtility.HtmlDecode(formMatch.Groups["action"].Value);
        return Uri.TryCreate(baseUri, action, out var resolved) ? resolved : null;
    }

    private static Dictionary<string, string> ExtractHiddenInputs(string html)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inputMatches = Regex.Matches(html, "<input[^>]*type=['\"]hidden['\"][^>]*>", RegexOptions.IgnoreCase);
        foreach (Match input in inputMatches)
        {
            var name = Regex.Match(input.Value, "name=['\"](?<name>[^'\"]+)['\"]", RegexOptions.IgnoreCase).Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var value = Regex.Match(input.Value, "value=['\"](?<value>[^'\"]*)['\"]", RegexOptions.IgnoreCase).Groups["value"].Value;
            values[name] = WebUtility.HtmlDecode(value);
        }

        return values;
    }

    private static bool TryBuildTriggerFormRequest(
        Uri baseUri,
        string html,
        out Uri actionUri,
        out HttpMethod method,
        out Dictionary<string, string> formValues,
        out string triggerDescription)
    {
        actionUri = baseUri;
        method = HttpMethod.Post;
        formValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        triggerDescription = string.Empty;

        var formMatches = Regex.Matches(html, "<form(?<attrs>[^>]*)>(?<content>.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match formMatch in formMatches)
        {
            var attrs = formMatch.Groups["attrs"].Value;
            var content = formMatch.Groups["content"].Value;

            var action = ExtractAttribute(attrs, "action");
            if (!string.IsNullOrWhiteSpace(action) && Uri.TryCreate(baseUri, WebUtility.HtmlDecode(action), out var resolvedAction))
            {
                actionUri = resolvedAction;
            }

            var methodAttr = ExtractAttribute(attrs, "method");
            method = string.Equals(methodAttr, "get", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Get : HttpMethod.Post;

            formValues = ExtractHiddenInputs(content);

            var inputMatches = Regex.Matches(content, "<input(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match inputMatch in inputMatches)
            {
                var inputAttrs = inputMatch.Groups["attrs"].Value;
                var name = ExtractAttribute(inputAttrs, "name");
                var type = ExtractAttribute(inputAttrs, "type");
                var value = WebUtility.HtmlDecode(ExtractAttribute(inputAttrs, "value") ?? string.Empty);

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var isSubmitLike = string.Equals(type, "submit", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "button", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "image", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("AUTHFU", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("LLOGIN", StringComparison.OrdinalIgnoreCase);

                var matchesTrigger = IsTriggerKeyword(name) || IsTriggerKeyword(value);
                if (!isSubmitLike && !matchesTrigger)
                {
                    continue;
                }

                if (name.Contains("AUTHFU", StringComparison.OrdinalIgnoreCase))
                {
                    formValues["$ScriptButton"] = "pressed";
                    triggerDescription = $"input[name={name}] => $ScriptButton=pressed";
                    return true;
                }

                formValues[name] = string.IsNullOrWhiteSpace(value) ? "pressed" : value;
                triggerDescription = $"input[name={name}]";
                return true;
            }

            var buttonMatches = Regex.Matches(content, "<button(?<attrs>[^>]*)>(?<text>.*?)</button>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match buttonMatch in buttonMatches)
            {
                var buttonAttrs = buttonMatch.Groups["attrs"].Value;
                var text = WebUtility.HtmlDecode(Regex.Replace(buttonMatch.Groups["text"].Value, "<.*?>", string.Empty));
                var name = ExtractAttribute(buttonAttrs, "name");
                var value = WebUtility.HtmlDecode(ExtractAttribute(buttonAttrs, "value") ?? string.Empty);

                if (!IsTriggerKeyword(text) && !IsTriggerKeyword(value) && !IsTriggerKeyword(name))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    formValues[name] = string.IsNullOrWhiteSpace(value) ? text.Trim() : value;
                }

                triggerDescription = string.IsNullOrWhiteSpace(name) ? "button[text]" : $"button[name={name}]";
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<AdisFallbackAction> BuildAdisFallbackActions(Uri baseUri, string html)
    {
        var result = new List<AdisFallbackAction>();
        if (!TryGetPrimaryFormContext(baseUri, html, out var actionUri, out var method, out var baseValues))
        {
            return result;
        }

        var internalLinkId = FindInternalLinkIdByText(html, "Mein Konto");
        if (!string.IsNullOrWhiteSpace(internalLinkId))
        {
            var values = Clone(baseValues);
            values[internalLinkId] = "pressed";
            result.Add(new AdisFallbackAction(actionUri, method, values, $"{internalLinkId}=pressed"));
        }

        if (html.Contains("htmlOnLink(\"*SBK\")", StringComparison.OrdinalIgnoreCase)
            || html.Contains("htmlOnLink('*SBK')", StringComparison.OrdinalIgnoreCase))
        {
            var values = Clone(baseValues);
            values["selected"] = "ZTEXT       *SBK";
            values["keyCode"] = "0";
            result.Add(new AdisFallbackAction(actionUri, method, values, "selected=ZTEXT       *SBK"));
        }

        var scriptButtonInputExists = Regex.IsMatch(
            html,
            "name=['\"]SUO\\d+_AUTHFU_\\d+['\"]",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (scriptButtonInputExists)
        {
            var values = Clone(baseValues);
            values["$ScriptButton"] = "pressed";
            result.Add(new AdisFallbackAction(actionUri, method, values, "$ScriptButton=pressed"));
        }

        return result;

        static Dictionary<string, string> Clone(Dictionary<string, string> source)
            => new(source, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetPrimaryFormContext(
        Uri baseUri,
        string html,
        out Uri actionUri,
        out HttpMethod method,
        out Dictionary<string, string> values)
    {
        actionUri = baseUri;
        method = HttpMethod.Post;
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var formMatch = Regex.Match(html, "<form(?<attrs>[^>]*)>(?<content>.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!formMatch.Success)
        {
            return false;
        }

        var attrs = formMatch.Groups["attrs"].Value;
        var content = formMatch.Groups["content"].Value;

        var action = ExtractAttribute(attrs, "action");
        if (!string.IsNullOrWhiteSpace(action) && Uri.TryCreate(baseUri, WebUtility.HtmlDecode(action), out var resolvedAction))
        {
            actionUri = resolvedAction;
        }

        var methodAttr = ExtractAttribute(attrs, "method");
        method = string.Equals(methodAttr, "get", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Get : HttpMethod.Post;
        values = ExtractHiddenInputs(content);
        return true;
    }

    private static string? FindInternalLinkIdByText(string html, string text)
    {
        var linkMatches = Regex.Matches(html, "<a(?<attrs>[^>]*)>(?<inner>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in linkMatches)
        {
            var innerText = WebUtility.HtmlDecode(Regex.Replace(match.Groups["inner"].Value, "<.*?>", string.Empty));
            if (!innerText.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = ExtractAttribute(match.Groups["attrs"].Value, "id");
            if (!string.IsNullOrWhiteSpace(id) && id.StartsWith("$InternalLink", StringComparison.Ordinal))
            {
                return id;
            }
        }

        return null;
    }

    private sealed record AdisFallbackAction(Uri Uri, HttpMethod Method, Dictionary<string, string> Values, string Description);

    private static async Task<(Uri Uri, string Html)> SendFormRequestAsync(
        HttpClient client,
        Uri actionUri,
        HttpMethod method,
        IReadOnlyDictionary<string, string> values,
        CancellationToken token)
    {
        var submitValues = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
        {
            ["scriptEnabled"] = "true",
            ["keyCode"] = "0"
        };

        if (method == HttpMethod.Get)
        {
            using var queryContent = new FormUrlEncodedContent(submitValues);
            var query = await queryContent.ReadAsStringAsync(token);
            var separator = actionUri.Query.Length == 0 ? "?" : "&";
            var requestUri = new Uri(actionUri + separator + query, UriKind.Absolute);
            var html = await client.GetStringAsync(requestUri, token);
            return (requestUri, html);
        }

        using var requestContent = new FormUrlEncodedContent(submitValues);
        using var response = await client.PostAsync(actionUri, requestContent, token);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri ?? actionUri;
        var content = await response.Content.ReadAsStringAsync(token);
        return (finalUri, content);
    }

    private static bool IsTriggerKeyword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("anmelden", StringComparison.OrdinalIgnoreCase)
            || text.Contains("mein konto", StringComparison.OrdinalIgnoreCase)
            || text.Contains("login", StringComparison.OrdinalIgnoreCase)
            || text.Contains("authfu", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractAttribute(string attrs, string attributeName)
    {
        var match = Regex.Match(
            attrs,
            $"\\b{Regex.Escape(attributeName)}\\s*=\\s*(['\"])(?<value>.*?)\\1",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (match.Success)
        {
            return match.Groups["value"].Value;
        }

        var unquotedMatch = Regex.Match(
            attrs,
            $"\\b{Regex.Escape(attributeName)}\\s*=\\s*(?<value>[^\\s>]+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return unquotedMatch.Success ? unquotedMatch.Groups["value"].Value : null;
    }

    private static async Task<string> NavigateToLoansHtmlAsync(HttpClient client, Uri currentUri, string html, CancellationToken token, Action<string> trace)
    {
        var accountAction = BuildAnchorActionByText(currentUri, html, ["Mein Konto", "Konto", "My account", "Benutzerkonto"]);
        if (accountAction is not null)
        {
            trace($"Öffne Kontoansicht (HTTP): {accountAction.Description}");
            var result = await ExecuteAnchorActionAsync(client, accountAction, token);
            html = result.Html;
            currentUri = result.Uri;
        }

        var loansActions = BuildAnchorActionsByText(currentUri, html, ["Ausleih", "Entliehen", "Loan", "Checkouts"]);
        if (loansActions.Count > 0)
        {
            foreach (var loansAction in loansActions)
            {
                trace($"Öffne Ausleihansicht (HTTP): {loansAction.Description}");
                var result = await ExecuteAnchorActionAsync(client, loansAction, token);
                html = result.Html;
                currentUri = result.Uri;

                var tableLoans = ParseLoansFromHtml(html);
                if (tableLoans.Count > 0)
                {
                    trace($"Ausleihansicht bestätigt nach Aktion '{loansAction.Description}' (Tabellen-Treffer: {tableLoans.Count}).");
                    break;
                }

                var textLoans = ParseLoansFromTextFallback(html);
                if (textLoans.Count > 0)
                {
                    trace($"Ausleihansicht bestätigt nach Aktion '{loansAction.Description}' (Text-Treffer: {textLoans.Count}).");
                    break;
                }

                trace($"Keine Ausleihen nach Aktion '{loansAction.Description}', versuche nächste Aktion.");
            }
        }
        else
        {
            trace("Keine passende Ausleih-Aktion gefunden.");
        }

        return html;
    }

    private static AdisFallbackAction? BuildAnchorActionByText(Uri baseUri, string html, IReadOnlyList<string> keywords)
        => BuildAnchorActionsByText(baseUri, html, keywords).FirstOrDefault();

    private static IReadOnlyList<AdisFallbackAction> BuildAnchorActionsByText(Uri baseUri, string html, IReadOnlyList<string> keywords)
    {
        var actions = new List<(int Score, AdisFallbackAction Action)>();
        var seenDescriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetPrimaryFormContext(baseUri, html, out var actionUri, out var method, out var baseValues))
        {
            return [];
        }

        var linkMatches = Regex.Matches(html, "<a(?<attrs>[^>]*)>(?<inner>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in linkMatches)
        {
            var attrs = match.Groups["attrs"].Value;
            var text = WebUtility.HtmlDecode(Regex.Replace(match.Groups["inner"].Value, "<.*?>", string.Empty)).Trim();
            if (!keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)) || text.Contains("Fernleihe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var score = ScoreKeywordText(text);
            var href = WebUtility.HtmlDecode(ExtractAttribute(attrs, "href") ?? string.Empty);
            var id = ExtractAttribute(attrs, "id");

            if (!string.IsNullOrWhiteSpace(href)
                && href != "#"
                && !href.StartsWith("javascript", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(baseUri, href, out var resolvedUri))
            {
                var action = new AdisFallbackAction(
                    resolvedUri,
                    HttpMethod.Get,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    $"GET-Link '{text}'");

                AddAction(score, action);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) && id.StartsWith("$InternalLink", StringComparison.Ordinal))
            {
                var values = Clone(baseValues);
                values[id] = "pressed";
                AddAction(score, new AdisFallbackAction(actionUri, method, values, $"Form-Link {id} für '{text}'"));
            }
        }

        // Also consider submit controls; these are often the real actions for Ausleihen/Entliehen.
        var submitInputs = Regex.Matches(html, "<input(?<attrs>[^>]*type=['\"]submit['\"][^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in submitInputs)
        {
            var attrs = match.Groups["attrs"].Value;
            var name = ExtractAttribute(attrs, "name");
            var value = WebUtility.HtmlDecode(ExtractAttribute(attrs, "value") ?? string.Empty).Trim();
            var dataFld = ExtractAttribute(attrs, "data-fld") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name)
                || !keywords.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase))
                || value.Contains("Fernleihe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = Clone(baseValues);
            values[name] = value;
            values["source"] = "$B";
            if (!string.IsNullOrWhiteSpace(dataFld))
            {
                values["focus"] = dataFld;
            }

            AddAction(ScoreKeywordText(value) + 15, new AdisFallbackAction(actionUri, method, values, $"Submit-Button {name}='{value}'"));
        }

        var buttonMatches = Regex.Matches(html, "<button(?<attrs>[^>]*)>(?<inner>.*?)</button>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in buttonMatches)
        {
            var attrs = match.Groups["attrs"].Value;
            var name = ExtractAttribute(attrs, "name");
            var value = WebUtility.HtmlDecode(ExtractAttribute(attrs, "value") ?? string.Empty).Trim();
            var text = WebUtility.HtmlDecode(Regex.Replace(match.Groups["inner"].Value, "<.*?>", string.Empty)).Trim();
            var label = string.IsNullOrWhiteSpace(value) ? text : value;

            if (string.IsNullOrWhiteSpace(name)
                || !keywords.Any(k => label.Contains(k, StringComparison.OrdinalIgnoreCase))
                || label.Contains("Fernleihe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = Clone(baseValues);
            values[name] = string.IsNullOrWhiteSpace(value) ? "pressed" : value;
            AddAction(ScoreKeywordText(label) + 10, new AdisFallbackAction(actionUri, method, values, $"Button {name}='{label}'"));
        }

        // Some service-area tiles trigger htmlOnLink via JS functions (idfnX -> fnX).
        foreach (var htmlOnLinkAction in BuildHtmlOnLinkActionsByText(baseUri, html, keywords))
        {
            AddAction(htmlOnLinkAction.Score, htmlOnLinkAction.Action);
        }

        return actions
            .OrderByDescending(x => x.Score)
            .Select(x => x.Action)
            .ToList();

        void AddAction(int score, AdisFallbackAction action)
        {
            if (!seenDescriptions.Add(action.Description))
            {
                return;
            }

            actions.Add((score, action));
        }

        static Dictionary<string, string> Clone(Dictionary<string, string> source)
            => new(source, StringComparer.OrdinalIgnoreCase);

        static int ScoreKeywordText(string text)
        {
            var score = 0;
            if (text.Contains("Ausleih", StringComparison.OrdinalIgnoreCase)) score += 120;
            if (text.Contains("Entliehen", StringComparison.OrdinalIgnoreCase)) score += 110;
            if (text.Contains("Checkouts", StringComparison.OrdinalIgnoreCase) || text.Contains("Loan", StringComparison.OrdinalIgnoreCase)) score += 90;
            return score;
        }
    }

    private static IReadOnlyList<(int Score, AdisFallbackAction Action)> BuildHtmlOnLinkActionsByText(Uri baseUri, string html, IReadOnlyList<string> keywords)
    {
        var result = new List<(int Score, AdisFallbackAction Action)>();
        if (!TryGetPrimaryFormContext(baseUri, html, out var actionUri, out var method, out var baseValues))
        {
            return result;
        }

        var fnCodeMatches = Regex.Matches(
            html,
            "function\\s+fn(?<num>\\d+)\\(e\\)\\{[^}]*htmlOnLink\\(\"(?<code>[^\"]+)\"\\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var codeById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in fnCodeMatches)
        {
            var number = m.Groups["num"].Value;
            var code = m.Groups["code"].Value;
            if (!string.IsNullOrWhiteSpace(number) && !string.IsNullOrWhiteSpace(code))
            {
                codeById[$"idfn{number}"] = code;
            }
        }

        var elementMatches = Regex.Matches(
            html,
            "<(?<tag>a|div|button|span)[^>]*id=['\"](?<id>idfn\\d+)['\"][^>]*>(?<inner>.*?)</\\k<tag>>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in elementMatches)
        {
            var id = m.Groups["id"].Value;
            if (!codeById.TryGetValue(id, out var code))
            {
                continue;
            }

            var text = WebUtility.HtmlDecode(Regex.Replace(m.Groups["inner"].Value, "<.*?>", string.Empty)).Trim();
            if (!keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase))
                || text.Contains("Fernleihe", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Fristverlängerung", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = new Dictionary<string, string>(baseValues, StringComparer.OrdinalIgnoreCase)
            {
                ["selected"] = $"ZTEXT       {code}",
                ["keyCode"] = "0"
            };

            var score = 100;
            if (text.Contains("Ausleih", StringComparison.OrdinalIgnoreCase)) score += 120;
            if (text.Contains("Entliehen", StringComparison.OrdinalIgnoreCase)) score += 110;
            if (text.Contains("Loan", StringComparison.OrdinalIgnoreCase) || text.Contains("Checkouts", StringComparison.OrdinalIgnoreCase)) score += 90;

            result.Add((score, new AdisFallbackAction(actionUri, method, values, $"htmlOnLink('{code}') für '{text}' via {id}")));
        }

        return result;
    }

    private static Task<(Uri Uri, string Html)> ExecuteAnchorActionAsync(HttpClient client, AdisFallbackAction action, CancellationToken token)
    {
        if (action.Method == HttpMethod.Get)
        {
            return ExecuteGetActionAsync(client, action.Uri, token);
        }

        return SendFormRequestAsync(client, action.Uri, action.Method, action.Values, token);
    }

    private static async Task<(Uri Uri, string Html)> ExecuteGetActionAsync(HttpClient client, Uri uri, CancellationToken token)
    {
        var html = await client.GetStringAsync(uri, token);
        return (uri, html);
    }

    private static IReadOnlyList<VoebbLoanItem> ParseLoansFromHtml(string html)
        => VoebbLoanParser.ParseLoansFromHtml(html);

    private static IReadOnlyList<VoebbLoanItem> ParseLoansFromTextFallback(string html)
        => VoebbLoanParser.ParseLoansFromTextFallback(html);
}
