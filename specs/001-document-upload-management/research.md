# Research: Document Upload and Management

**Feature**: `001-document-upload-management` | **Date**: 2026-10-20 | **Plan**: [plan.md](plan.md)

All Technical Context unknowns are resolved below. Each entry: Decision / Rationale / Alternatives considered.

---

## R1. Serving files (download & preview) from a Blazor Server app

- **Decision**: Add a Razor Page `Pages/DocumentFile.cshtml` (`@page "/documents/{documentId:int}/file"`, `[Authorize]`) whose `OnGetAsync` calls `IDocumentService` to authorize, logs the activity, and returns `File(stream, contentType, ...)`. `?preview=true` returns `Content-Disposition: inline` (only for PDF/JPEG/PNG); otherwise `attachment` with the original file name.
- **Rationale**: Blazor components cannot stream HTTP responses; a real HTTP endpoint is needed. The project already uses Razor Pages for HTTP-level work (Login/Logout), so this follows the existing convention (Constitution III) without adding controllers or a new endpoint folder. Cookie auth applies automatically, so authorization and IDOR checks run on every request (FR-024, FR-027).
- **Alternatives**: MVC controller (requires `AddControllers`/`MapControllers`, a new pattern); Minimal API in `Program.cs` (valid but bypasses the established Razor Page convention); serving from `wwwroot` (violates FR-027 and the constitution).

## R2. Preview rendering vs. existing security headers

- **Decision**: Preview opens the inline file URL in a **new browser tab** (`target="_blank"`, `rel="noopener"`). No `<iframe>`/`<embed>`.
- **Rationale**: `Program.cs` sends `X-Frame-Options: DENY`, which blocks framing even same-origin content. Opening a tab uses the browser's built-in PDF/image viewer, keeps the headers unchanged (Constitution II: security headers MUST NOT be weakened), and meets the 3 s preview target for typical sizes.
- **Alternatives**: Relax to `SAMEORIGIN` + `frame-src 'self'` (weakens headers); client-side PDF.js (new JS dependency, CDN or vendoring; unnecessary).

## R3. File storage abstraction and path scheme

- **Decision**: `IFileStorageService` with `UploadAsync(Stream, storagePath, contentType)`, `DownloadAsync(storagePath)`, `DeleteAsync(storagePath)`. `LocalFileStorageService` writes under a configurable root (`FileStorage:RootPath`, default `AppData/uploads`, relative to content root, outside `wwwroot`). `DocumentService` generates the storage path **before** any write: `{uploaderUserId}/{projectId|"personal"}/{Guid:N}{.ext}` (forward slashes, relative). Local service resolves the full path and verifies it stays inside the root.
- **Rationale**: Matches stakeholder pattern and Constitution I/IV (generate path → write file → persist record; relative, portable keys usable as blob names). GUID names make user-supplied names irrelevant to storage (FR-010). Root-containment check is defense in depth against traversal.
- **`GetUrlAsync` (from stakeholder sketch) is intentionally omitted**: direct/SAS URLs would bypass application authorization and activity logging (FR-024, FR-032). All access goes through R1. Can be added in a cloud implementation later if a spec requires it (YAGNI, Constitution V).
- **Alternatives**: Storing bytes in the database (bloats SQLite, not cloud-portable); user file names in paths (traversal/collision risk).

## R4. Upload consistency (atomicity) and replace/delete ordering

- **Decision**:
  - *Upload*: validate → scan (in memory) → generate path → `UploadAsync` → insert DB rows (Document, Tags, Activity) in one `SaveChangesAsync`. If the DB save fails, delete the just-written file (best effort, logged).
  - *Replace*: validate/scan new file → write to a **new** GUID path → update record (path, size, type, original name, modified date) → delete old file (best effort, logged).
  - *Delete*: remove DB record + dependents in one save → delete file (best effort, logged). Activity records survive (no FK cascade).
- **Rationale**: Satisfies FR-009/SC-006 and Constitution IV. Writing the new file before touching the record means a failure never leaves a record pointing at a missing file; at worst an orphan file is left and logged.
- **Alternatives**: DB-first (risks records with missing/empty paths — the exact failure the stakeholder doc warns about); distributed transaction (unavailable for filesystem).

## R5. Blazor `InputFile` handling, progress and size limits

- **Decision**: `<InputFile multiple @key="inputKey">`; for each file capture `Name`, `Size`, `ContentType` first, reject >25 MB or 0 bytes before reading, then `OpenReadStream(25 * 1024 * 1024)` and copy to a `MemoryStream` in 64 KB chunks, updating a per-file percentage and calling `StateHasChanged()`. Clear the `IBrowserFile` reference and bump `inputKey` after processing.
- **Rationale**: Mandated by the stakeholder doc and Constitution III; avoids disposed-stream errors; chunked copy provides the progress indicator (FR-006). `InputFile` streams over SignalR in chunks, so the default 32 KB hub message limit does not apply.
- **Alternatives**: Raw HTTP multipart upload endpoint (needs antiforgery + JS; more complexity); no progress (fails FR-006).

## R6. Allowed types and offline malware scanning (FR-002, FR-007)

- **Decision**: Allow-list by extension, with the stored MIME type **derived server-side** from the extension (never trusted from the browser):

  | Extension | MIME type | Signature check |
  |---|---|---|
  | .pdf | application/pdf | starts with `%PDF-` |
  | .docx/.xlsx/.pptx | `application/vnd.openxmlformats-officedocument.*` | ZIP `50 4B 03 04` |
  | .doc/.xls/.ppt | application/msword, application/vnd.ms-excel, application/vnd.ms-powerpoint | OLE `D0 CF 11 E0 A1 B1 1A E1` |
  | .txt | text/plain | no NUL bytes in first 8 KB |
  | .jpg/.jpeg | image/jpeg | `FF D8 FF` |
  | .png | image/png | `89 50 4E 47 0D 0A 1A 0A` |

  `IMalwareScanner.ScanAsync(Stream, extension)` → `ScanResult { IsClean, Reason }`. `LocalMalwareScanner` performs the signature check and searches the content for the EICAR test string; registered via DI so a real scanner (e.g., ClamAV/Defender/cloud) can replace it.
- **Rationale**: Clarified answer (Option A). Works offline, testable (EICAR), Constitution I abstraction. Server-derived MIME prevents content-type spoofing and keeps `X-Content-Type-Options: nosniff` effective.
- **Alternatives**: Windows Defender CLI (machine-dependent); no scanning (fails requirement).

## R7. Authorization model (single source of truth)

- **Decision**: `DocumentService` builds an `IQueryable<Document>` "accessible documents" filter from the requesting user's **database** record (role, department), not from UI state:
  - Administrator → all.
  - Otherwise any of: uploader = me; project manager = me; project member includes me; direct share to me; department share to my department (non-null); I am TeamLead and uploader.Department = my department (non-null).
  - Manage (edit/replace/delete): uploader; PM of the document's project; TeamLead of uploader's department; Administrator.
  - Share/revoke: uploader only.
  All lists, search, dashboard widgets and the file page use this filter; single-document operations return `null`/`false` (rendered as "not found") on denial so existence is not revealed.
- **Rationale**: FR-023–FR-026 and Constitution II (service-layer authorization, IDOR protection). Using the DB record makes revocation/department changes effective immediately.
- **Department claim**: Login will also emit a `"Department"` claim (stakeholder requirement, Constitution II), but authorization decisions read the DB to avoid stale cookies (8 h sliding expiration).
- **Alternatives**: ASP.NET resource-based `IAuthorizationHandler` (more ceremony, not used elsewhere in the app); claim-only checks (stale after department change).

## R8. Search and list performance on SQLite

- **Decision**: Server-side filtering with EF Core `EF.Functions.Like(..., "%term%")` across Title, Description, Tag, uploader DisplayName and Project Name, applied on top of the accessible filter; sort/filter in the query; `AsNoTracking()` for read lists; indexes on Document(UploadedByUserId), (ProjectId), (Category), (UploadedDate), DocumentTag(Tag), DocumentShare(SharedWithUserId), (SharedWithDepartment), DocumentActivity(OccurredAt), (Action).
- **Rationale**: Meets 2 s for ≤500 documents comfortably on SQLite; no full-text engine needed (Constitution V). SQLite `LIKE` is case-insensitive for ASCII.
- **Alternatives**: SQLite FTS5 (extra setup, not supported by EnsureCreated model); in-memory filtering (leaks authorization risk and scales poorly).

## R9. Data conventions and schema changes

- **Decision**: Integer identity keys; `Category` stored as text (validated against a constant list); `FileType` max 255; `StoragePath` max 500; Title 255; Description 2000; Tag max 50 chars, max 10 tags per document; OriginalFileName 255. Schema applied by existing `EnsureCreated()` — developers must delete `ContosoDashboard.db` once after pulling this feature (documented in quickstart).
- **Rationale**: Stakeholder constraints and Constitution IV. Introducing EF migrations would be a larger change than the feature needs (Constitution V).
- **Alternatives**: GUID keys (rejected by stakeholders); enum category (rejected by stakeholders); migrations (deferred).

## R10. Notifications

- **Decision**: Append `DocumentShared` and `DocumentAdded` to `NotificationType` (append-only, so existing stored integer values are unchanged). Created via existing `INotificationService` with `Informational` priority. Department shares notify all current users of that department except the uploader. Follows existing service behavior (no per-user preference filtering is applied elsewhere).
- **Rationale**: FR-022, FR-031; reuse existing infrastructure (Constitution III).
- **Alternatives**: Separate document notification table (duplication).

## R11. Audit and reports

- **Decision**: `DocumentActivity` rows (DocumentId **without FK**, DocumentTitle snapshot, FileType snapshot, ActorUserId, Action text, OccurredAt) written in the same `SaveChangesAsync` as the action where possible; download/preview logged by the file page before streaming. `IDocumentReportService` (Administrator-only) aggregates: uploads by file type, top uploaders, previews/downloads per document and per day, over a date range.
- **Rationale**: FR-032/FR-033; activity must outlive deleted documents. Clarification: previews recorded separately.
- **Alternatives**: `ILogger`-only logging (not queryable for reports).

## R12. Testing approach

- **Decision**: No new test project. Verification = `dotnet build` + manual scenarios in [quickstart.md](quickstart.md) covering all four roles (permitted and denied), EICAR rejection, size/type rejection, and orphan checks on disk.
- **Rationale**: Matches the constitution's Quality Gates (build + manual exercise + README security scenarios + allow/deny per role) and keeps the training solution single-project (Constitution V).
- **Alternatives**: xUnit + EF InMemory project for `DocumentService` authorization — valuable, but would add a second project not required by the constitution; can be proposed as a follow-up.

## R13. Repository hygiene

- **Decision**: Add `ContosoDashboard/AppData/` to `.gitignore`; storage root configured in `appsettings.json` under `FileStorage:RootPath`.
- **Rationale**: Uploaded files must never be committed (Constitution workflow: no secrets/data in repo).
