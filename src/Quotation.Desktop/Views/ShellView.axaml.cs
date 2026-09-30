using Avalonia.Controls;

namespace Quotation.Desktop.Views;

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
        KeyBindings.Add(new Avalonia.Input.KeyBinding
        {
            Gesture = Avalonia.Input.KeyGesture.Parse("Ctrl+F"),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => GlobalSearchBox.Focus()),
        });
    }
}
