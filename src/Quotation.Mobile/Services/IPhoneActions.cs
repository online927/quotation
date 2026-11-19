namespace Quotation.Mobile.Services;

/// <summary>What only the phone itself can do: open or share a PDF, call, WhatsApp, e-mail (Android: Intents).</summary>
public interface IPhoneActions
{
    /// <summary>Where PDFs are saved before they are opened or shared (the app's own cache).</summary>
    string FilesFolder { get; }
    /// <summary>Opens the PDF in the phone's PDF viewer.</summary>
    void OpenPdf(string path);
    /// <summary>Shares the PDF: straight to WhatsApp, or the phone's share list (Gmail, Drive …).</summary>
    void SharePdf(string path, string title, bool whatsApp);
    void Dial(string phone);
    /// <summary>A WhatsApp chat with this number.</summary>
    void WhatsAppChat(string phone);
    void Email(string address, string subject);

    /// <summary>
    /// A photo of an enquiry from the camera, or a picture chosen on the phone (a WhatsApp screenshot), made smaller for
    /// sending (JPEG); null when cancelled.
    /// </summary>
    Task<byte[]?> TakePhotoAsync(bool chooseOnPhone);

    /// <summary>What was said into the phone (its own speech recognition, Indian English); null when cancelled or not heard.</summary>
    Task<string?> ListenAsync();
}

/// <summary>For tests and for running the phone screens on a PC: remembers what was asked.</summary>
public sealed class RecordingPhoneActions : IPhoneActions
{
    public List<string> Done { get; } = [];
    public string FilesFolder { get; set; } = Path.Combine(Path.GetTempPath(), "tsq-phone");
    public void OpenPdf(string path) => Done.Add("open " + path);
    public void SharePdf(string path, string title, bool whatsApp) => Done.Add((whatsApp ? "whatsapp " : "share ") + path);
    public void Dial(string phone) => Done.Add("call " + phone);
    public void WhatsAppChat(string phone) => Done.Add("chat " + phone);
    public void Email(string address, string subject) => Done.Add("mail " + address);

    /// <summary>The photo the camera "takes" (null = cancelled).</summary>
    public byte[]? Photo { get; set; }

    /// <summary>What is "said", one each time the phone listens (empty = stopped).</summary>
    public Queue<string> Speech { get; } = new();

    public Task<byte[]?> TakePhotoAsync(bool chooseOnPhone)
    {
        Done.Add(chooseOnPhone ? "choose picture" : "camera");
        return Task.FromResult(Photo);
    }

    public Task<string?> ListenAsync()
    {
        Done.Add("listen");
        return Task.FromResult(Speech.TryDequeue(out var said) ? said : null);
    }
}

/// <summary>Phone numbers for WhatsApp links: digits only, India (91) added to a 10-digit mobile number.</summary>
public static class PhoneNumbers
{
    public static string ForWhatsApp(string phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '0') digits = digits[1..];
        return digits.Length == 10 ? "91" + digits : digits;
    }

    /// <summary>The first number when a Tally field holds several ("98450 12345 / 080-2222").</summary>
    public static string First(string phone)
    {
        var parts = (phone ?? "").Split(['/', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : "";
    }
}
