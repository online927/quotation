using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Mobile.ViewModels;

/// <summary>More: new quotation from a message, the theme (same list as the PC), the server and signing out.</summary>
public sealed partial class MoreViewModel(MainViewModel main) : PageViewModel
{
    public override string Title => "More";

    public string Initial => (main.Session.User?.DisplayName is { Length: > 0 } n ? n[..1] : "?").ToUpperInvariant();
    public string UserName => main.Session.User?.DisplayName ?? "";
    public string RoleName => main.Session.User?.RoleName ?? "";
    public string ServerText => LoginViewModel.ShortServer(main.Session.Settings.ServerUrl);
    public string Version => "Version " + (typeof(MoreViewModel).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "");
    public bool MakesDocuments => main.Session.User?.MakesDocuments == true;

    /// <summary>The themes, as on the PC.</summary>
    public IReadOnlyList<ThemeChoice> Themes { get; } = ThemeNames.All
        .Select(t => new ThemeChoice(t.Theme, t.Name)).ToList();

    [ObservableProperty] private ThemeChoice? _theme;

    private bool _loading;

    public override Task LoadAsync()
    {
        _loading = true;
        Theme = Themes.FirstOrDefault(t => t.Key == AppTheme.Normalize(main.Session.User?.Theme ?? main.Session.Settings.Theme)) ?? Themes[0];
        _loading = false;
        return Task.CompletedTask;
    }

    /// <summary>A theme chosen: on the screens at once and saved for this user (the PC follows at the next sign-in).</summary>
    partial void OnThemeChanged(ThemeChoice? value)
    {
        if (value is null || _loading) return;
        ThemeService.Apply(value.Key);
        main.Session.Settings.Theme = value.Key;
        main.Session.Settings.Save();
        _ = RunAsync(async () => main.Session.Api.UpdateCurrentUser(await main.Session.Api.SetMyPreferencesAsync(new MyPreferencesDto(Theme: value.Key))));
    }

    [RelayCommand]
    private Task NewFromMessageAsync() => main.PushAsync(new MessageViewModel(main));

    [RelayCommand]
    private Task SignOutAsync() => main.SignOutAsync();
}

public sealed record ThemeChoice(string Key, string Name)
{
    public override string ToString() => Name;
}
