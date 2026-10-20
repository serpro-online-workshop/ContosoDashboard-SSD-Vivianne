namespace ContosoDashboard.Services;

public interface IFileStorageService
{
    Task UploadAsync(Stream content, string storagePath, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default);
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}