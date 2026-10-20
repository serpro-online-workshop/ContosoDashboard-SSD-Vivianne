using System.Text;

namespace ContosoDashboard.Services;

public static class DocumentFileRules
{
    public const long MaxFileSizeBytes = 25 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> MimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    public static string NormalizeExtension(string fileName) => Path.GetExtension(fileName).ToLowerInvariant();

    public static bool TryGetMimeType(string extension, out string mimeType) => MimeTypes.TryGetValue(extension, out mimeType!);

    public static bool IsPreviewable(string mimeType) =>
        mimeType is "application/pdf" or "image/jpeg" or "image/png";

    public static async Task<bool> HasValidSignatureAsync(Stream content, string extension, CancellationToken ct = default)
    {
        if (!MimeTypes.ContainsKey(extension))
        {
            return false;
        }

        content.Position = 0;
        var header = new byte[8192];
        var bytesRead = await content.ReadAsync(header.AsMemory(), ct);
        content.Position = 0;

        return extension switch
        {
            ".pdf" => StartsWith(header, bytesRead, "%PDF-"u8),
            ".docx" or ".xlsx" or ".pptx" => StartsWith(header, bytesRead, [0x50, 0x4B, 0x03, 0x04]),
            ".doc" or ".xls" or ".ppt" => StartsWith(header, bytesRead, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            ".txt" => !header.AsSpan(0, bytesRead).Contains((byte)0),
            ".jpg" or ".jpeg" => StartsWith(header, bytesRead, [0xFF, 0xD8, 0xFF]),
            ".png" => StartsWith(header, bytesRead, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            _ => false
        };
    }

    private static bool StartsWith(byte[] content, int length, ReadOnlySpan<byte> signature) =>
        length >= signature.Length && content.AsSpan(0, signature.Length).SequenceEqual(signature);
}