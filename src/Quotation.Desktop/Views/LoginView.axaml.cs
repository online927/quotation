using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Quotation.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        var target = string.IsNullOrEmpty(this.FindControl<TextBox>("UserBox")?.Text) ? "UserBox" : "PasswordBox";
        this.FindControl<TextBox>(target)?.Focus();
    }
}
