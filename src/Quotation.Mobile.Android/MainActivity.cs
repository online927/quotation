using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using Quotation.Mobile;

namespace Quotation.PhoneApp;

[Activity(Label = "Quotations", Theme = "@style/MyTheme.NoActionBar", Icon = "@drawable/icon", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : AvaloniaMainActivity<App>
{
    /// <summary>The open screen (the camera, picture chooser and speech recognizer answer to it).</summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        AndroidPhoneActions.OnActivityResult(requestCode, resultCode, data);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.Phone = new AndroidPhoneActions();
        App.DeviceName = "Phone " + (global::Android.OS.Build.Model ?? "");
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
