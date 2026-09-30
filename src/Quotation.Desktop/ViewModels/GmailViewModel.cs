using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed class EmailRow(EmailMessageDto m)
{
    public EmailMessageDto Message { get; } = m;
    public string Received => Formatting.LocalDateTime(m.ReceivedUtc) ?? "";
    public string From => m.From;
    public string Subject => m.Subject.Length == 0 ? "(fetching…)" : m.Subject;
    public string Status => m.Status.ToString();
    public string Result => m.ProcessingResult + (m.LastError is null ? "" : $" ({m.LastError})");
}

public sealed partial class GmailViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly INavigator _navigator;

    [ObservableProperty] private GmailStatusDto? _status;
    public ObservableCollection<EmailRow> Messages { get; } = [];
    public bool IsAdmin => _session.IsAdmin;

    public GmailViewModel(AppSession session, INavigator navigator)
    {
        _session = session;
        _navigator = navigator;
        _ = LoadAsync();
    }

    public string ConnectionText => Status is null ? "" : Status.Connected ? $"CONNECTED — {Status.Account}" : "NOT CONNECTED";
    public string CountsText => Status is null || Status.Counts.Count == 0 ? "No e-mails processed yet." :
        string.Join("   ", Status.Counts.Select(kv => $"{kv.Key}: {kv.Value}"));
    public string LastPollText => Formatting.LocalDateTime(Status?.LastPollUtc) ?? "never";

    partial void OnStatusChanged(GmailStatusDto? value)
    {
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(CountsText));
        OnPropertyChanged(nameof(LastPollText));
    }

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        Status = await _session.Api.GmailStatusAsync();
        Messages.Clear();
        foreach (var m in await _session.Api.GmailMessagesAsync(100)) Messages.Add(new EmailRow(m));
    });

    [RelayCommand]
    private Task PollAsync() => RunAsync(async () =>
    {
        var r = await _session.Api.GmailPollAsync();
        InfoMessage = r.Error ?? $"Checked {r.Found} e-mail(s): {r.New} new, {r.Processed} processed, {r.Failed} failed.";
        Status = await _session.Api.GmailStatusAsync();
        Messages.Clear();
        foreach (var m in await _session.Api.GmailMessagesAsync(100)) Messages.Add(new EmailRow(m));
    });

    [RelayCommand]
    private Task ConnectAsync() => RunAsync(async () =>
    {
        using var receiver = new LoopbackOAuthReceiver();
        var start = await _session.Api.GmailConnectStartAsync(receiver.RedirectUri);
        InfoMessage = "Complete the sign-in in your browser (Google's page). Waiting…";
        _session.Documents.Open(start.AuthorizationUrl);
        var (code, state, error) = await receiver.WaitAsync(TimeSpan.FromMinutes(5));
        if (error is not null || code is null)
        {
            ErrorMessage = "Gmail was not connected: " + (error ?? "no authorization code received.");
            InfoMessage = null;
            return;
        }
        await _session.Api.GmailConnectCompleteAsync(new GmailConnectCompleteRequest(code, state ?? "", receiver.RedirectUri));
        Status = await _session.Api.GmailStatusAsync();
        InfoMessage = $"Connected to {Status.Account}.";
    });

    [RelayCommand]
    private Task DisconnectAsync() => RunAsync(async () =>
    {
        await _session.Api.GmailDisconnectAsync();
        Status = await _session.Api.GmailStatusAsync();
        InfoMessage = "Gmail disconnected. The stored token was deleted.";
    });

    [RelayCommand]
    private void OpenInbox() => _navigator.Navigate("inbox");
}
