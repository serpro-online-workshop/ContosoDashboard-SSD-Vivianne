# Contract: HTTP Endpoint and UI Routes

**Feature**: `001-document-upload-management`

## HTTP endpoint (Razor Page — [research.md](../research.md) R1)

### `GET /documents/{documentId:int}/file[?preview=true]`

| Aspect | Contract |
|---|---|
| Implementation | `Pages/DocumentFile.cshtml` + `DocumentFile.cshtml.cs`, `[Authorize]` |
| Auth | Existing cookie authentication; unauthenticated → redirect to `/login` |
| Authorization | `IDocumentService.OpenFileAsync(documentId, userId, preview)` |
| 200 (download) | Body = file bytes; `Content-Type` = stored MIME; `Content-Disposition: attachment; filename*=UTF-8''<original name>` |
| 200 (preview) | Only when `preview=true` **and** type ∈ {application/pdf, image/jpeg, image/png}; `Content-Disposition: inline` |
| Preview of other types | Served as download (attachment) |
| 404 | Document missing, deleted, **or not permitted** (identical response — FR-024) |
| Side effects | `DocumentActivity` row: `Preview` or `Download` |
| Headers | Global security headers unchanged (incl. `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`); add `Cache-Control: no-store` |
| Methods | GET only; no state change beyond audit logging, so antiforgery not required |

## Blazor UI routes

All pages `@attribute [Authorize]` and obtain the user id from `ClaimTypes.NameIdentifier` like existing pages.

| Route | Component | Purpose | Requirements |
|---|---|---|---|
| `/documents` | `Pages/Documents.razor` | My Documents: list, sort, filter, upload, edit, replace, delete, share | FR-001–FR-012, FR-018–FR-022a |
| `/documents/shared` | `Pages/SharedDocuments.razor` | Shared with Me | FR-015 |
| `/documents/search` | `Pages/DocumentSearch.razor` | Search across accessible documents | FR-014 |
| `/documents/reports` | `Pages/DocumentReports.razor` | Admin reports; `[Authorize(Policy = "Administrator")]` + service check | FR-033 |
| `/projects/{projectId:int}` | `Pages/ProjectDetails.razor` (modified) | Project documents section + upload to project | FR-013, US3 |
| `/tasks` | `Pages/Tasks.razor` (modified) | Per-task "Documents" action → attach/upload | FR-028, FR-029 |
| `/` | `Pages/Index.razor` (modified) | Recent Documents widget + document count card | FR-030 |
| Nav | `Shared/NavMenu.razor` (modified) | "Documents" link; "Document Reports" for Administrators (`AuthorizeView Roles="Administrator"`) | — |

## Shared components (`Shared/`)

| Component | Responsibility |
|---|---|
| `DocumentUploadDialog.razor` | `InputFile multiple` with `@key`, metadata form (title per file, category, project, description, tags), per-file progress & result. Parameters: `ProjectId?`, `TaskId?`, `OnUploaded` |
| `DocumentTable.razor` | Renders `DocumentListItem` rows with Preview / Download links (new tab for preview) and action buttons gated by `CanManage` / `CanShare` |
| `DocumentEditDialog.razor` | Edit metadata and replace file |
| `DocumentShareDialog.razor` | Share with user / department; list and revoke existing shares |
| `ConfirmDialog.razor` | Delete confirmation (FR-020) |

UI gating is a convenience only; every action is re-authorized in `IDocumentService` (Constitution II).

## User-facing messages (stable wording for testing)

| Situation | Message |
|---|---|
| Too large | "File exceeds the 25 MB limit." |
| Empty | "File is empty." |
| Unsupported type | "Unsupported file type. Allowed: PDF, Word, Excel, PowerPoint, text, JPEG, PNG." |
| Scan rejected | "File failed the security scan and was not uploaded." |
| Not found / not permitted | "Document not found or you do not have access." |
| Success | "Uploaded '{title}'." |
