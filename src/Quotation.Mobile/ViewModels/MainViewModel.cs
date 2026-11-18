using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;
using Quotation.Mobile.Services;

namespace Quotation.Mobile.ViewModels;

/// <summary>
/// The whole phone app: the sign-in screen, or a page with the bottom menu (Home, Quotations, Products, Customers, More).
/// Pages opened from a list (a quotation, a customer) go on a stack; the phone's Back button returns.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly Stack<PageViewModel> _stack = new();
    private readonly Dictionary<string, PageViewModel> _tabs = [];

    public MainViewModel(PhoneSession session)
    {
        Session = session;
        Login = new LoginViewModel(session, OnSignedIn);
        ThemeService.Apply(AppTheme.Normalize(session.Settings.Theme));
    }

    public PhoneSession Session { get; }
    public LoginViewModel Login { get; }

    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private PageViewModel? _page;
    [ObservableProperty] private string _tab = Tabs.Home;

    public bool CanGoBack => _stack.Count > 0;

    /// <summary>A Yes / No question over the page (the Tally theme asks "Approve? Yes or No").</summary>
    [ObservableProperty] private PhoneQuestion? _question;

    public async Task<bool> AskAsync(string title, string message)
    {
        var q = new PhoneQuestion(title, message);
        Question = q;
        try { return await q.Answer; }
        finally { if (Question == q) Question = null; }
    }

    [RelayCommand]
    private void Answer(string? yes) => Question?.Choose(yes == "yes");

    /// <summary>Opening the app: straight in with the saved sign-in while it is valid.</summary>
    public async Task StartAsync()
    {
        if (await Session.ResumeAsync()) await OnSignedIn();
    }

    private async Task OnSignedIn()
    {
        ThemeService.Apply(AppTheme.Normalize(Session.User?.Theme ?? Session.Settings.Theme));
        _tabs.Clear();
        _stack.Clear();
        IsSignedIn = true;
        await ShowTabAsync(Tabs.Home);
    }

    [RelayCommand]
    public async Task ShowTabAsync(string? tab)
    {
        tab ??= Tabs.Home;
        _stack.Clear();
        Tab = tab;
        if (!_tabs.TryGetValue(tab, out var page))
        {
            page = tab switch
            {
                Tabs.Quotations => new QuotationsViewModel(this),
                Tabs.Products => new ProductsViewModel(this),
                Tabs.Customers => new CustomersViewModel(this),
                Tabs.More => new MoreViewModel(this),
                _ => new HomeViewModel(this),
            };
            _tabs[tab] = page;
        }
        await ShowAsync(page);
    }

    /// <summary>The Quotations page on one of its lists (Home's number boxes).</summary>
    public async Task ShowQuotationsAsync(string list)
    {
        await ShowTabAsync(Tabs.Quotations);
        if (Page is QuotationsViewModel q) await q.ChooseAsync(list);
    }

    /// <summary>A page opened from another one (Back returns).</summary>
    public async Task PushAsync(PageViewModel page)
    {
        if (Page is not null) _stack.Push(Page);
        await ShowAsync(page);
    }

    /// <summary>The phone's Back button or the arrow: the page before. False on a bottom-menu page (the phone may close the app).</summary>
    public bool GoBack()
    {
        if (!IsSignedIn || _stack.Count == 0)
        {
            if (IsSignedIn && Tab != Tabs.Home) { _ = ShowTabAsync(Tabs.Home); return true; }
            return false;
        }
        _ = ShowAsync(_stack.Pop());
        return true;
    }

    [RelayCommand]
    private void Back() => GoBack();

    private async Task ShowAsync(PageViewModel page)
    {
        Page = page;
        OnPropertyChanged(nameof(CanGoBack));
        await page.LoadAsync();
    }

    public async Task SignOutAsync()
    {
        await Session.SignOutAsync();
        IsSignedIn = false;
        Page = null;
        _tabs.Clear();
        _stack.Clear();
        Login.Password = "";
        Login.Refresh();
    }
}

public sealed class PhoneQuestion(string title, string message)
{
    private readonly TaskCompletionSource<bool> _answer = new();
    public string Title { get; } = title;
    public string Message { get; } = message;
    public Task<bool> Answer => _answer.Task;
    public void Choose(bool yes) => _answer.TrySetResult(yes);
}

public static class Tabs
{
    public const string Home = "home", Quotations = "quotations", Products = "products", Customers = "customers", More = "more";
}
