using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quotation.Contracts;

namespace Quotation.ApiClient;

/// <summary>Error returned by the Quotation Server (validation, conflict, auth …).</summary>
public sealed class ApiException(HttpStatusCode status, string message, IReadOnlyList<string>? details = null, string? code = null)
    : Exception(message)
{
    public string? Code { get; } = code;
    public HttpStatusCode Status { get; } = status;
    public IReadOnlyList<string> Details { get; } = details ?? [];
    public bool IsUnauthorized => Status == HttpStatusCode.Unauthorized;
    public bool IsConnectionFailure => Status == 0;

    public string FullMessage => Details.Count == 0 ? Message : Message + Environment.NewLine + string.Join(Environment.NewLine, Details.Select(d => "• " + d));
}

/// <summary>Typed HTTP client for the Quotation Server REST API.</summary>
public partial class QuotationApiClient
{
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly HttpClient _http;

    public QuotationApiClient(HttpClient http, string? clientMachine = null)
    {
        _http = http;
        _http.DefaultRequestHeaders.Remove("X-Client-Machine");
        _http.DefaultRequestHeaders.Add("X-Client-Machine", clientMachine ?? Environment.MachineName);
    }

    public static QuotationApiClient Create(string baseUrl, TimeSpan? timeout = null) =>
        new(new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = timeout ?? TimeSpan.FromSeconds(60) });

    public Uri? BaseAddress => _http.BaseAddress;
    public string? Token { get; private set; }
    public UserDto? CurrentUser { get; private set; }

    /// <summary>Raised when the server rejects the session (expired/revoked) so the UI can return to login.</summary>
    public event EventHandler? SessionExpired;

    public void SetToken(string? token)
    {
        Token = token;
        _http.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    // ---- System ----
    public Task<HealthDto> HealthAsync(CancellationToken ct = default) => GetAsync<HealthDto>("api/health", ct);

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var result = await PostAsync<LoginResponse>("api/auth/login", new LoginRequest(username, password, Environment.MachineName), ct);
        SetToken(result.Token);
        CurrentUser = result.User;
        return result;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try { await SendAsync(HttpMethod.Post, "api/auth/logout", null, ct); }
        catch (ApiException) { /* best effort */ }
        SetToken(null);
        CurrentUser = null;
    }

    public Task ChangePasswordAsync(string current, string next, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/auth/change-password", new ChangePasswordRequest(current, next), ct);

    public Task<UserDto> MeAsync(CancellationToken ct = default) => GetAsync<UserDto>("api/auth/me", ct);
    public Task<List<UserDto>> UsersAsync(CancellationToken ct = default) => GetAsync<List<UserDto>>("api/users", ct);
    public Task<UserDto> CreateUserAsync(CreateUserRequest req, CancellationToken ct = default) => PostAsync<UserDto>("api/users", req, ct);
    public Task<UserDto> SetUserActiveAsync(int id, bool active, CancellationToken ct = default) =>
        PostAsync<UserDto>($"api/users/{id}/active/{active.ToString().ToLowerInvariant()}", null, ct);
    public Task ResetPasswordAsync(int id, string password, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/users/{id}/reset-password", new CreateUserRequest("", "", password, default), ct);

    public Task<DashboardDto> DashboardAsync(CancellationToken ct = default) => GetAsync<DashboardDto>("api/dashboard", ct);
    public Task<SystemStatusDto> StatusAsync(CancellationToken ct = default) => GetAsync<SystemStatusDto>("api/status", ct);
    public Task<AllSettingsDto> SettingsAsync(CancellationToken ct = default) => GetAsync<AllSettingsDto>("api/settings", ct);
    public Task<AllSettingsDto> SaveSettingsAsync(AllSettingsDto dto, CancellationToken ct = default) =>
        SendAsync<AllSettingsDto>(HttpMethod.Put, "api/settings", dto, ct);
    public Task<List<AuditEntryDto>> AuditAsync(string? entityType = null, string? entityId = null, int take = 200, CancellationToken ct = default) =>
        GetAsync<List<AuditEntryDto>>($"api/audit?take={take}{Q("entityType", entityType)}{Q("entityId", entityId)}", ct);

    // ---- plumbing ----
    protected static string Q(string name, string? value) =>
        string.IsNullOrEmpty(value) ? "" : $"&{name}={Uri.EscapeDataString(value)}";

    protected Task<T> GetAsync<T>(string url, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, url, null, ct);
    protected Task<T> PostAsync<T>(string url, object? body, CancellationToken ct) => SendAsync<T>(HttpMethod.Post, url, body, ct);

    protected async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, url, body, ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    protected async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, url, body, ct);
    }

    protected async Task<byte[]> GetBytesAsync(string url, CancellationToken ct)
    {
        using var response = await SendRawAsync(HttpMethod.Get, url, null, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    protected async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(0, $"Cannot reach the Quotation Server at {_http.BaseAddress}. {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(0, $"The Quotation Server at {_http.BaseAddress} did not respond in time.");
        }

        if (response.IsSuccessStatusCode) return response;

        using (response)
        {
            ApiError? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, ct); }
            catch { /* non-JSON error body */ }
            if (response.StatusCode == HttpStatusCode.Unauthorized && url != "api/auth/login")
            {
                SessionExpired?.Invoke(this, EventArgs.Empty);
            }
            var message = error?.Error ?? response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Your session has expired. Please log in again.",
                HttpStatusCode.Forbidden => "You do not have permission for this action.",
                HttpStatusCode.NotFound => "Not found.",
                _ => $"Server error ({(int)response.StatusCode}).",
            };
            throw new ApiException(response.StatusCode, message, error?.Details, error?.Code);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}

public partial class QuotationApiClient
{
    public Task<TallyTestResultDto> TestTallyAsync(CancellationToken ct = default) => PostAsync<TallyTestResultDto>("api/tally/test", null, ct);
    public Task<List<string>> TallyCompaniesAsync(CancellationToken ct = default) => GetAsync<List<string>>("api/tally/companies", ct);
}

public partial class QuotationApiClient
{
    /// <summary>Starts a sync. With wait=true the call returns when the sync has finished.</summary>
    public async Task<SyncRunDto?> StartSyncAsync(Quotation.Core.Domain.SyncKind kind, bool wait = false, CancellationToken ct = default)
    {
        var url = $"api/sync/{kind.ToString().ToLowerInvariant()}{(wait ? "?wait=true" : "")}";
        if (wait) return await PostAsync<SyncRunDto>(url, null, ct);
        await SendAsync(HttpMethod.Post, url, null, ct);
        return null;
    }

    public Task<SyncStateDto> SyncStateAsync(CancellationToken ct = default) => GetAsync<SyncStateDto>("api/sync/state", ct);
    public Task<List<SyncRunDto>> SyncRunsAsync(int take = 50, CancellationToken ct = default) => GetAsync<List<SyncRunDto>>($"api/sync/runs?take={take}", ct);

    public async Task<string> TallySampleAsync(string type, string name, CancellationToken ct = default)
    {
        using var r = await SendRawAsync(HttpMethod.Get, $"api/sync/sample?type={Uri.EscapeDataString(type)}&name={Uri.EscapeDataString(name)}", null, ct);
        return await r.Content.ReadAsStringAsync(ct);
    }
}

public partial class QuotationApiClient
{
    public Task<List<ProductSearchHitDto>> SearchProductsAsync(string query, int limit = 20, CancellationToken ct = default) =>
        GetAsync<List<ProductSearchHitDto>>($"api/products/search?limit={limit}{Q("q", query)}", ct);

    public Task<ProductDetailDto> ProductAsync(int id, CancellationToken ct = default) => GetAsync<ProductDetailDto>($"api/products/{id}", ct);

    public Task<List<CustomerSearchHitDto>> SearchCustomersAsync(string query, int limit = 20, CancellationToken ct = default) =>
        GetAsync<List<CustomerSearchHitDto>>($"api/customers/search?limit={limit}{Q("q", query)}", ct);

    public Task<CustomerDetailDto> CustomerAsync(int id, CancellationToken ct = default) => GetAsync<CustomerDetailDto>($"api/customers/{id}", ct);
}

public partial class QuotationApiClient
{
    public Task<QuotationDto> CreateQuotationAsync(SaveQuotationRequest r, CancellationToken ct = default) => PostAsync<QuotationDto>("api/quotations", r, ct);
    public Task<QuotationDto> UpdateQuotationAsync(Guid id, SaveQuotationRequest r, CancellationToken ct = default) =>
        SendAsync<QuotationDto>(HttpMethod.Put, $"api/quotations/{id}", r, ct);
    public Task<QuotationDto> QuotationAsync(Guid id, CancellationToken ct = default) => GetAsync<QuotationDto>($"api/quotations/{id}", ct);
    public Task<QuotationDto> ApproveQuotationAsync(Guid id, bool overrideStaleData = false, int? revision = null, CancellationToken ct = default) =>
        PostAsync<QuotationDto>($"api/quotations/{id}/approve", new ApproveRequest(overrideStaleData, revision), ct);
    public Task<QuotationDto> CancelQuotationAsync(Guid id, CancellationToken ct = default) => PostAsync<QuotationDto>($"api/quotations/{id}/cancel", null, ct);
    public Task<QuotationDto> DuplicateQuotationAsync(Guid id, CancellationToken ct = default) => PostAsync<QuotationDto>($"api/quotations/{id}/duplicate", null, ct);
    public Task<QuotationDto> RegeneratePdfAsync(Guid id, CancellationToken ct = default) => PostAsync<QuotationDto>($"api/quotations/{id}/regenerate-pdf", null, ct);
    public Task<byte[]> QuotationPdfAsync(Guid id, CancellationToken ct = default) => GetBytesAsync($"api/quotations/{id}/pdf", ct);
    public Task<List<ValidationErrorDto>> ValidateQuotationAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<List<ValidationErrorDto>>($"api/quotations/{id}/validate", ct);
    public Task<NextNumberDto> NextNumberAsync(DateOnly? date = null, CancellationToken ct = default) =>
        GetAsync<NextNumberDto>("api/quotations/next-number" + (date is null ? "" : $"?date={date:yyyy-MM-dd}"), ct);

    public Task<List<QuotationSummaryDto>> QuotationsAsync(string? q = null, Quotation.Core.Domain.QuotationStatus? status = null,
        DateOnly? from = null, DateOnly? to = null, int? customerId = null, int take = 100, Quotation.Core.Domain.QuotationSource? source = null,
        CancellationToken ct = default) =>
        GetAsync<List<QuotationSummaryDto>>($"api/quotations?take={take}{Q("q", q)}{Q("status", status?.ToString())}{Q("source", source?.ToString())}" +
                                            $"{Q("from", from?.ToString("yyyy-MM-dd"))}{Q("to", to?.ToString("yyyy-MM-dd"))}{Q("customerId", customerId?.ToString())}", ct);

    public Task<NumberSeriesDto> NumberSeriesAsync(int? financialYearStart = null, CancellationToken ct = default) =>
        GetAsync<NumberSeriesDto>("api/numbering" + (financialYearStart is null ? "" : $"?financialYearStart={financialYearStart}"), ct);
    public Task<NumberSeriesDto> SetNextSequenceAsync(int financialYearStart, int next, CancellationToken ct = default) =>
        SendAsync<NumberSeriesDto>(HttpMethod.Put, "api/numbering", new SetNextSequenceRequest(financialYearStart, next), ct);
}

public partial class QuotationApiClient
{
    public Task<byte[]> PreviewPdfAsync(Guid id, CancellationToken ct = default) => GetBytesAsync($"api/quotations/{id}/preview", ct);
}

public partial class QuotationApiClient
{
    public Task<AiRequestDto> AnalyzeAsync(string text, CancellationToken ct = default) => PostAsync<AiRequestDto>("api/ai/analyze", new AnalyzeRequest(text), ct);
    public Task<List<AiRequestDto>> AiRequestsAsync(Quotation.Core.Domain.AiRequestStatus? status = null, int take = 100, CancellationToken ct = default) =>
        GetAsync<List<AiRequestDto>>($"api/ai/requests?take={take}{Q("status", status?.ToString())}", ct);
    public Task<AiRequestDto> AiRequestAsync(int id, CancellationToken ct = default) => GetAsync<AiRequestDto>($"api/ai/requests/{id}", ct);
    public Task<AiRequestDto> CreateDraftFromAiAsync(int id, CreateDraftFromAiRequest r, CancellationToken ct = default) =>
        PostAsync<AiRequestDto>($"api/ai/requests/{id}/create-draft", r, ct);
    public Task<AiRequestDto> DismissAiRequestAsync(int id, CancellationToken ct = default) => PostAsync<AiRequestDto>($"api/ai/requests/{id}/dismiss", null, ct);
}

public partial class QuotationApiClient
{
    public Task<GmailStatusDto> GmailStatusAsync(CancellationToken ct = default) => GetAsync<GmailStatusDto>("api/gmail/status", ct);
    public Task<List<EmailMessageDto>> GmailMessagesAsync(int take = 100, CancellationToken ct = default) => GetAsync<List<EmailMessageDto>>($"api/gmail/messages?take={take}", ct);
    public Task<GmailPollResultDto> GmailPollAsync(CancellationToken ct = default) => PostAsync<GmailPollResultDto>("api/gmail/poll", null, ct);
    public Task<GmailConnectStartResponse> GmailConnectStartAsync(string redirectUri, CancellationToken ct = default) =>
        PostAsync<GmailConnectStartResponse>("api/gmail/connect/start", new GmailConnectStartRequest(redirectUri), ct);
    public Task GmailConnectCompleteAsync(GmailConnectCompleteRequest r, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/gmail/connect/complete", r, ct);
    public Task GmailDisconnectAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Post, "api/gmail/disconnect", null, ct);
}

public partial class QuotationApiClient
{
    public Task<RecentItemsDto> RecentItemsAsync(CancellationToken ct = default) => GetAsync<RecentItemsDto>("api/quotations/recent-items", ct);
    public Task<DiagnosticsDto> DiagnosticsAsync(CancellationToken ct = default) => GetAsync<DiagnosticsDto>("api/diagnostics", ct);

    public async Task<string> ServerLogAsync(int lines = 300, CancellationToken ct = default)
    {
        using var r = await SendRawAsync(HttpMethod.Get, $"api/diagnostics/log?lines={lines}", null, ct);
        return await r.Content.ReadAsStringAsync(ct);
    }
}

public partial class QuotationApiClient
{
    public Task<List<AuditEntryDto>> QuotationAuditAsync(Guid id, CancellationToken ct = default) => GetAsync<List<AuditEntryDto>>($"api/quotations/{id}/audit", ct);
}

public partial class QuotationApiClient
{
    public async Task<string> BackupNowAsync(CancellationToken ct = default)
    {
        using var r = await SendRawAsync(HttpMethod.Post, "api/diagnostics/backup", null, ct);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("path").GetString() ?? "";
    }
}
