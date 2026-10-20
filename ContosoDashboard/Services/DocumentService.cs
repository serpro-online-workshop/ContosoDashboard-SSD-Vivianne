using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<DocumentUploadResult> UploadAsync(DocumentUploadRequest request, Stream content, int requestingUserId);
    Task<List<DocumentListItem>> GetMyDocumentsAsync(int requestingUserId);
    Task<List<Project>> GetUploadableProjectsAsync(int requestingUserId);
    Task<DocumentFileResult?> OpenFileAsync(int documentId, int requestingUserId, bool preview = false);
}

public sealed class DocumentService : IDocumentService
{
    private const string AccessDeniedMessage = "Document not found or you do not have access.";
    private const string UnsupportedFileMessage = "Unsupported file type. Allowed: PDF, Word, Excel, PowerPoint, text, JPEG, PNG.";

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IMalwareScanner _malwareScanner;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService fileStorage,
        IMalwareScanner malwareScanner,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _malwareScanner = malwareScanner;
        _logger = logger;
    }

    public async Task<List<Project>> GetUploadableProjectsAsync(int requestingUserId)
    {
        var requester = await _context.Users.AsNoTracking()
            .Where(user => user.UserId == requestingUserId)
            .Select(user => new { user.UserId, user.Role })
            .SingleOrDefaultAsync();

        if (requester is null)
        {
            return [];
        }

        return await _context.Projects.AsNoTracking()
            .Where(project => project.ProjectManagerId == requestingUserId ||
                              project.ProjectMembers.Any(member => member.UserId == requestingUserId))
            .OrderBy(project => project.Name)
            .ToListAsync();
    }

    public async Task<DocumentUploadResult> UploadAsync(DocumentUploadRequest request, Stream content, int requestingUserId)
    {
        if (request.SizeBytes <= 0)
        {
            return DocumentUploadResult.Failure("File is empty.");
        }

        if (request.SizeBytes > DocumentFileRules.MaxFileSizeBytes)
        {
            return DocumentUploadResult.Failure("File exceeds the 25 MB limit.");
        }

        var extension = DocumentFileRules.NormalizeExtension(request.OriginalFileName);
        if (!DocumentFileRules.TryGetMimeType(extension, out var mimeType))
        {
            return DocumentUploadResult.Failure(UnsupportedFileMessage);
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 255)
        {
            return DocumentUploadResult.Failure("A title is required and must be 255 characters or fewer.");
        }

        if (!DocumentCategories.IsValid(request.Category))
        {
            return DocumentUploadResult.Failure("Select a valid document category.");
        }

        if (request.OriginalFileName.Length > 255)
        {
            return DocumentUploadResult.Failure("File name must be 255 characters or fewer.");
        }

        if (request.Description?.Length > 2000)
        {
            return DocumentUploadResult.Failure("Description must be 2000 characters or fewer.");
        }

        var uploader = await _context.Users.SingleOrDefaultAsync(user => user.UserId == requestingUserId);
        if (uploader is null)
        {
            return DocumentUploadResult.Failure(AccessDeniedMessage);
        }

        if (request.ProjectId.HasValue)
        {
            var canUploadToProject = await _context.Projects.AnyAsync(project =>
                project.ProjectId == request.ProjectId.Value &&
                (project.ProjectManagerId == requestingUserId ||
                 project.ProjectMembers.Any(member => member.UserId == requestingUserId)));

            if (!canUploadToProject)
            {
                return DocumentUploadResult.Failure(AccessDeniedMessage);
            }
        }

        var tags = NormalizeTags(request.Tags, out var tagsError);
        if (tagsError is not null)
        {
            return DocumentUploadResult.Failure(tagsError);
        }

        content.Position = 0;
        var scan = await _malwareScanner.ScanAsync(content, extension);
        content.Position = 0;
        if (!scan.IsClean)
        {
            _context.DocumentActivities.Add(new DocumentActivity
            {
                DocumentId = 0,
                DocumentTitle = request.Title.Trim(),
                FileType = mimeType,
                ActorUserId = requestingUserId,
                Action = DocumentActions.ScanRejected,
                Details = scan.Reason,
                OccurredAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return DocumentUploadResult.Failure("File failed the security scan and was not uploaded.");
        }

        var storageFolder = request.ProjectId?.ToString() ?? "personal";
        var storagePath = $"{requestingUserId}/{storageFolder}/{Guid.NewGuid():N}{extension}";
        var document = new Document
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Category = request.Category,
            OriginalFileName = Path.GetFileName(request.OriginalFileName),
            FileType = mimeType,
            FileExtension = extension,
            FileSizeBytes = request.SizeBytes,
            StoragePath = storagePath,
            UploadedByUserId = requestingUserId,
            ProjectId = request.ProjectId,
            UploadedDate = DateTime.UtcNow,
            LastModifiedDate = DateTime.UtcNow,
            Tags = tags.Select(tag => new DocumentTag { Tag = tag }).ToList()
        };

        try
        {
            await _fileStorage.UploadAsync(content, storagePath, mimeType);
            _context.Documents.Add(document);
            _context.DocumentActivities.Add(CreateActivity(document, requestingUserId, DocumentActions.Upload));
            await _context.SaveChangesAsync();
            return DocumentUploadResult.Success(document.DocumentId);
        }
        catch (Exception exception)
        {
            try
            {
                await _fileStorage.DeleteAsync(storagePath);
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(cleanupException, "Failed to remove uploaded file {StoragePath} after database failure", storagePath);
            }

            _context.ChangeTracker.Clear();
            _logger.LogError(exception, "Document upload failed for user {UserId}", requestingUserId);
            return DocumentUploadResult.Failure("Upload failed. Please try again.");
        }
    }

    public async Task<List<DocumentListItem>> GetMyDocumentsAsync(int requestingUserId)
    {
        var documents = await _context.Documents.AsNoTracking()
            .Where(document => document.UploadedByUserId == requestingUserId)
            .Include(document => document.Project)
            .Include(document => document.UploadedByUser)
            .Include(document => document.Tags)
            .OrderByDescending(document => document.UploadedDate)
            .ToListAsync();

        return documents.Select(document => new DocumentListItem(
            document.DocumentId,
            document.Title,
            document.Category,
            document.UploadedDate,
            document.FileSizeBytes,
            document.FileType,
            document.Project?.Name,
            document.UploadedByUser.DisplayName,
            document.Tags.Select(tag => tag.Tag).ToList())).ToList();
    }

    public async Task<DocumentFileResult?> OpenFileAsync(int documentId, int requestingUserId, bool preview = false)
    {
        var requester = await _context.Users.AsNoTracking()
            .Where(user => user.UserId == requestingUserId)
            .Select(user => new { user.UserId, user.Role, user.Department })
            .SingleOrDefaultAsync();

        if (requester is null)
        {
            return null;
        }

        var document = await AccessibleDocuments(requester.UserId, requester.Role, requester.Department)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.DocumentId == documentId);

        if (document is null)
        {
            return null;
        }

        Stream content;
        try
        {
            content = await _fileStorage.DownloadAsync(document.StoragePath);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        _context.DocumentActivities.Add(CreateActivity(document, requestingUserId, DocumentActions.Download));
        try
        {
            await _context.SaveChangesAsync();
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }

        return new DocumentFileResult(content, document.FileType, document.OriginalFileName);
    }

    private IQueryable<Document> AccessibleDocuments(int userId, UserRole role, string? department)
    {
        if (role == UserRole.Administrator)
        {
            return _context.Documents;
        }

        return _context.Documents.Where(document =>
            document.UploadedByUserId == userId ||
            (document.ProjectId.HasValue &&
             (document.Project!.ProjectManagerId == userId ||
              document.Project.ProjectMembers.Any(member => member.UserId == userId))) ||
            document.Shares.Any(share =>
                share.SharedWithUserId == userId ||
                (department != null && share.SharedWithDepartment == department)) ||
            (role == UserRole.TeamLead && department != null &&
             document.UploadedByUser.Department == department));
    }

    private static DocumentActivity CreateActivity(Document document, int actorUserId, string action) => new()
    {
        DocumentId = document.DocumentId,
        DocumentTitle = document.Title,
        FileType = document.FileType,
        ActorUserId = actorUserId,
        Action = action,
        OccurredAt = DateTime.UtcNow
    };

    private static List<string> NormalizeTags(IEnumerable<string> tags, out string? error)
    {
        var normalized = tags
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Any(tag => tag.Length > 50))
        {
            error = "Tags must be 50 characters or fewer.";
            return [];
        }

        if (normalized.Count > 10)
        {
            error = "A document can have at most 10 tags.";
            return [];
        }

        error = null;
        return normalized;
    }
}

public sealed record DocumentUploadRequest(
    string Title,
    string? Description,
    string Category,
    int? ProjectId,
    IReadOnlyList<string> Tags,
    string OriginalFileName,
    long SizeBytes);

public sealed record DocumentUploadResult(bool Succeeded, int? DocumentId, string? ErrorMessage)
{
    public static DocumentUploadResult Success(int documentId) => new(true, documentId, null);
    public static DocumentUploadResult Failure(string message) => new(false, null, message);
}

public sealed record DocumentListItem(
    int DocumentId,
    string Title,
    string Category,
    DateTime UploadedDate,
    long FileSizeBytes,
    string FileType,
    string? ProjectName,
    string UploadedByName,
    IReadOnlyList<string> Tags);

public sealed record DocumentFileResult(Stream Content, string ContentType, string DownloadFileName);