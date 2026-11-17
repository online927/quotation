using Android.Content;
using Android.Widget;
using AndroidX.Core.Content;
using Quotation.Mobile.Services;
using AndroidApp = Android.App.Application;
using AndroidUri = Android.Net.Uri;

namespace Quotation.PhoneApp;

/// <summary>Opening and sharing PDFs, calling, WhatsApp and e-mail, with Android's own apps (Intents).</summary>
public sealed class AndroidPhoneActions : IPhoneActions
{
    private const string Authority = "com.tsquotation.phone.fileprovider";
    private static readonly string[] WhatsAppApps = ["com.whatsapp", "com.whatsapp.w4b"];

    private static Context Ctx => AndroidApp.Context;

    /// <summary>The app's cache (shared with other apps through the FileProvider, Resources/xml/file_paths.xml).</summary>
    public string FilesFolder => Path.Combine(Ctx.CacheDir!.AbsolutePath, "pdf");

    private static AndroidUri UriOf(string path) => FileProvider.GetUriForFile(Ctx, Authority, new Java.IO.File(path))!;

    public void OpenPdf(string path)
    {
        var uri = UriOf(path);
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/pdf");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        if (!TryStart(intent))
        {
            Say("No PDF viewer on this phone: choose an app to open it.");
            SharePdf(path, Path.GetFileNameWithoutExtension(path), whatsApp: false);
        }
    }

    public void SharePdf(string path, string title, bool whatsApp)
    {
        var uri = UriOf(path);
        Intent Send()
        {
            var send = new Intent(Intent.ActionSend);
            send.SetType("application/pdf");
            send.PutExtra(Intent.ExtraStream, uri);
            send.PutExtra(Intent.ExtraSubject, title);
            send.ClipData = ClipData.NewRawUri(title, uri);
            send.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            return send;
        }
        if (whatsApp)
        {
            // WhatsApp or WhatsApp Business, whichever is on the phone (both: the phone asks which).
            var installed = WhatsAppApps.Where(IsInstalled).ToList();
            if (installed.Count == 1)
            {
                var direct = Send();
                direct.SetPackage(installed[0]);
                if (TryStart(direct)) return;
            }
            if (installed.Count == 0) Say("WhatsApp is not on this phone: choose another app.");
        }
        var chooser = Intent.CreateChooser(Send(), "Share " + title)!;
        chooser.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        TryStart(chooser);
    }

    public void Dial(string phone)
    {
        var digits = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        var intent = new Intent(Intent.ActionDial, AndroidUri.Parse("tel:" + digits));
        intent.AddFlags(ActivityFlags.NewTask);
        if (!TryStart(intent)) Say("This phone cannot make calls.");
    }

    public void WhatsAppChat(string phone)
    {
        var intent = new Intent(Intent.ActionView, AndroidUri.Parse("https://wa.me/" + phone));
        intent.AddFlags(ActivityFlags.NewTask);
        if (!TryStart(intent)) Say("WhatsApp is not on this phone.");
    }

    public void Email(string address, string subject)
    {
        var intent = new Intent(Intent.ActionSendto, AndroidUri.Parse("mailto:" + address));
        if (subject.Length > 0) intent.PutExtra(Intent.ExtraSubject, subject);
        intent.AddFlags(ActivityFlags.NewTask);
        if (!TryStart(intent)) Say("No e-mail app on this phone.");
    }

    private static bool IsInstalled(string package)
    {
        try { return Ctx.PackageManager?.GetLaunchIntentForPackage(package) is not null; }
        catch (Java.Lang.Exception) { return false; }
    }

    private static bool TryStart(Intent intent)
    {
        try
        {
            Ctx.StartActivity(intent);
            return true;
        }
        catch (ActivityNotFoundException) { return false; }
    }

    private static void Say(string text) => Toast.MakeText(Ctx, text, ToastLength.Long)?.Show();
}
