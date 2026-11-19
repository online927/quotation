using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Quotation.Gmail;

/// <summary>
/// The office Google Drive, through the same Google sign-in as the Gmail connection. The app can only see and change the
/// files it put there itself (Google's "drive.file" permission): nothing else in the Drive is read or touched.
/// </summary>
public interface IDriveClient
{
    /// <summary>The id of the folder with this name inside <paramref name="parentId"/> (null = the top of the Drive); made when missing.</summary>
    Task<string> FolderAsync(string name, string? parentId, CancellationToken ct);

    /// <summary>Puts a file in the folder (a new file), or replaces the content of <paramref name="existingId"/>; returns the file id.</summary>
    Task<string> UploadAsync(string folderId, string name, string localPath, string mimeType, string? existingId, CancellationToken ct);
}

public sealed class GoogleDriveClient : IDriveClient, IDisposable
{
    private const string FolderType = "application/vnd.google-apps.folder";
    private readonly DriveService _service;

    public GoogleDriveClient(string clientSecretJson, string refreshToken)
    {
        var flow = GmailOAuth.CreateFlow(clientSecretJson);
        var credential = new UserCredential(flow, "gmail", new TokenResponse { RefreshToken = refreshToken });
        _service = new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "Quotations" });
    }

    public async Task<string> FolderAsync(string name, string? parentId, CancellationToken ct)
    {
        var list = _service.Files.List();
        list.Q = $"mimeType = '{FolderType}' and trashed = false and name = '{name.Replace("\\", "\\\\").Replace("'", "\\'")}' and " +
                 $"'{parentId ?? "root"}' in parents";
        list.Fields = "files(id)";
        list.PageSize = 1;
        var found = await list.ExecuteAsync(ct);
        if (found.Files is { Count: > 0 } files) return files[0].Id;
        var create = _service.Files.Create(new DriveFile { Name = name, MimeType = FolderType, Parents = parentId is null ? null : [parentId] });
        create.Fields = "id";
        return (await create.ExecuteAsync(ct)).Id;
    }

    public async Task<string> UploadAsync(string folderId, string name, string localPath, string mimeType, string? existingId, CancellationToken ct)
    {
        await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Google.Apis.Upload.IUploadProgress progress;
        string? id;
        if (existingId is not null)
        {
            var update = _service.Files.Update(new DriveFile { Name = name }, existingId, stream, mimeType);
            update.Fields = "id";
            progress = await update.UploadAsync(ct);
            id = update.ResponseBody?.Id;
        }
        else
        {
            var create = _service.Files.Create(new DriveFile { Name = name, Parents = [folderId] }, stream, mimeType);
            create.Fields = "id";
            progress = await create.UploadAsync(ct);
            id = create.ResponseBody?.Id;
        }
        if (progress.Exception is { } ex) throw ex;
        return id ?? throw new InvalidOperationException("Google Drive did not return the file.");
    }

    public void Dispose() => _service.Dispose();
}
