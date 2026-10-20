using System.Security.Claims;
using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ContosoDashboard.Pages;

[Authorize]
public sealed class DocumentFileModel : PageModel
{
    private readonly IDocumentService _documentService;

    public DocumentFileModel(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    public async Task<IActionResult> OnGetAsync(int documentId)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return NotFound();
        }

        var file = await _documentService.OpenFileAsync(documentId, userId);
        if (file is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.DownloadFileName);
    }
}