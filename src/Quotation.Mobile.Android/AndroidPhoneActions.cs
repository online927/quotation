using Android.App;
using Android.Content;
using Bitmap = Android.Graphics.Bitmap;
using BitmapFactory = Android.Graphics.BitmapFactory;
using Matrix = Android.Graphics.Matrix;
using Android.Provider;
using Android.Speech;
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

    // ------------------------------------------------------------------ photo of an enquiry, speaking items

    private const int PhotoRequest = 7101;
    private const int PictureRequest = 7102;
    private const int SpeechRequest = 7103;
    /// <summary>Longest side of the photo sent to the server (enough for Claude to read handwriting; small to send).</summary>
    private const int MaxSide = 1600;

    private static TaskCompletionSource<(Result Result, Intent? Data)>? _waiting;
    private static int _waitingFor;

    /// <summary>The camera, the picture chooser or the speech recognizer finished (MainActivity passes it on).</summary>
    public static void OnActivityResult(int requestCode, Result result, Intent? data)
    {
        if (requestCode != _waitingFor) return;
        _waiting?.TrySetResult((result, data));
    }

    private static Task<(Result Result, Intent? Data)> StartForResultAsync(Intent intent, int request)
    {
        var activity = MainActivity.Current ?? throw new InvalidOperationException("The app is not open.");
        _waiting?.TrySetResult((Result.Canceled, null));
        var waiting = new TaskCompletionSource<(Result, Intent?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting = waiting;
        _waitingFor = request;
        try
        {
            activity.StartActivityForResult(intent, request);
        }
        catch (ActivityNotFoundException)
        {
            waiting.TrySetResult((Result.Canceled, null));
        }
        return waiting.Task;
    }

    public async Task<byte[]?> TakePhotoAsync(bool chooseOnPhone)
    {
        if (!chooseOnPhone)
        {
            var folder = System.IO.Path.Combine(Ctx.CacheDir!.AbsolutePath, "photos");
            Directory.CreateDirectory(folder);
            var file = System.IO.Path.Combine(folder, "enquiry.jpg");
            if (File.Exists(file)) File.Delete(file);
            var camera = new Intent(MediaStore.ActionImageCapture);
            var uri = UriOf(file);
            camera.PutExtra(MediaStore.ExtraOutput, uri);
            camera.ClipData = ClipData.NewRawUri("enquiry", uri);
            camera.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
            if (camera.ResolveActivity(Ctx.PackageManager!) is not null)
            {
                var (result, _) = await StartForResultAsync(camera, PhotoRequest);
                if (result != Result.Ok || !File.Exists(file) || new FileInfo(file).Length == 0) return null;
                return Smaller(await File.ReadAllBytesAsync(file));
            }
            Say("No camera app: choose the picture instead.");
        }
        var pick = new Intent(Intent.ActionGetContent);
        pick.SetType("image/*");
        pick.AddCategory(Intent.CategoryOpenable);
        var (picked, data) = await StartForResultAsync(Intent.CreateChooser(pick, "Choose the enquiry picture")!, PictureRequest);
        if (picked != Result.Ok || data?.Data is not { } chosen) return null;
        await using var stream = Ctx.ContentResolver!.OpenInputStream(chosen);
        if (stream is null) return null;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return Smaller(memory.ToArray());
    }

    /// <summary>The photo turned the right way up, at most <see cref="MaxSide"/> pixels on its longest side, as JPEG.</summary>
    private static byte[] Smaller(byte[] photo)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(photo, 0, photo.Length, bounds);
        var longest = Math.Max(bounds.OutWidth, bounds.OutHeight);
        if (longest <= 0) return photo;
        var sample = 1;
        while (longest / (sample * 2) >= MaxSide) sample *= 2;
        using var decoded = BitmapFactory.DecodeByteArray(photo, 0, photo.Length, new BitmapFactory.Options { InSampleSize = sample });
        if (decoded is null) return photo;
        var scale = Math.Min(1f, (float)MaxSide / Math.Max(decoded.Width, decoded.Height));
        var matrix = new Matrix();
        matrix.PostScale(scale, scale);
        var turn = Turn(photo);
        if (turn != 0) matrix.PostRotate(turn);
        using var ready = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
        using var output = new MemoryStream();
        ready!.Compress(Bitmap.CompressFormat.Jpeg!, 85, output);
        return output.ToArray();
    }

    /// <summary>How far the camera says the photo must be turned (degrees).</summary>
    private static int Turn(byte[] photo)
    {
        try
        {
            using var stream = new MemoryStream(photo);
            var exif = new Android.Media.ExifInterface(stream);
            return exif.GetAttributeInt(Android.Media.ExifInterface.TagOrientation, 1) switch
            {
                6 => 90,
                3 => 180,
                8 => 270,
                _ => 0,
            };
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public async Task<string?> ListenAsync()
    {
        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, "en-IN");
        intent.PutExtra(RecognizerIntent.ExtraPrompt, "Say the item, how many, and the rate");
        intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
        if (intent.ResolveActivity(Ctx.PackageManager!) is null)
        {
            Say("Speaking is not available on this phone (it needs the Google app).");
            return null;
        }
        var (result, data) = await StartForResultAsync(intent, SpeechRequest);
        if (result != Result.Ok) return null;
        return data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults)?.FirstOrDefault();
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
