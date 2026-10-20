---

description: "Task list for Document Upload and Management"
---

# Tasks: Document Upload and Management

**Input**: Design documents from `specs/001-document-upload-management/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/services.md](contracts/services.md), [contracts/http-and-ui.md](contracts/http-and-ui.md), [quickstart.md](quickstart.md)

**Tests**: No automated test tasks. The spec did not request them, and the plan (research R12) sets verification as `dotnet build` plus manual role-based scenarios from [quickstart.md](quickstart.md). Each story ends with a validation task.

**Organization**: Tasks are grouped by user story (spec priorities P1–P4) so each story can be implemented and validated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: User story label (US1–US8) for story-phase tasks only
- All paths are relative to the repository root; application code lives in `ContosoDashboard/`

## Path Conventions

Single ASP.NET Core project: `ContosoDashboard/Models/`, `ContosoDashboard/Data/`, `ContosoDashboard/Services/`, `ContosoDashboard/Pages/`, `ContosoDashboard/Shared/` (Constitution III). No new projects or top-level folders.

**Shared-file note**: `ContosoDashboard/Services/DocumentService.cs` grows across stories. Tasks touching it within or across stories are sequential (never [P] with each other).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration and repository hygiene

- [ ] T001 Add `ContosoDashboard/AppData/` to `.gitignore` so uploaded files are never committed (research R13)
- [ ] T002 [P] Add a `"FileStorage": { "RootPath": "AppData/uploads" }` section to `ContosoDashboard/appsettings.json` (relative to content root, outside `wwwroot`, research R3)
- [ ] T003 [P] Create `ContosoDashboard/Models/DocumentCategories.cs` with a static class exposing `public static readonly IReadOnlyList<string> All` = "Project Documents", "Team Resources", "Personal Files", "Reports", "Presentations", "Other", plus `IsValid(string)` (text values, not an enum, per Constitution IV)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entities, schema, storage/scanner abstractions, the authorization core of `DocumentService`, the Department claim and DI. Every user story depends on these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Entities (data-model.md)

- [ ] T004 [P] Create `ContosoDashboard/Models/Document.cs`: `DocumentId` int PK identity; `Title` [Required][MaxLength(255)] "Required, trimmed, non-empty"; `Description` [MaxLength(2000)] nullable; `Category` [Required][MaxLength(50)] "one of `DocumentCategories.All`"; `OriginalFileName` [Required][MaxLength(255)] "display/download name only (never used in paths)"; `FileType` [Required][MaxLength(255)] "MIME type derived server-side from extension"; `FileExtension` [Required][MaxLength(10)] "lower-case, from allow-list"; `FileSizeBytes` long "1 … 26,214,400"; `StoragePath` [Required][MaxLength(500)] "unique; `{uploaderId}/{projectId|personal}/{guid}{ext}`; generated before file write"; `UploadedByUserId` int FK → User; `ProjectId` int? FK → Project; `UploadedDate`, `LastModifiedDate` DateTime UTC; navigations `UploadedByUser`, `Project`, `Tags`, `Shares`, `TaskLinks`
- [ ] T005 [P] Create `ContosoDashboard/Models/DocumentTag.cs`: `DocumentTagId` int PK; `DocumentId` int FK → Document; `Tag` [Required][MaxLength(50)] "Required, trimmed, stored lower-case; max 10 per document; unique per (DocumentId, Tag)"; navigation `Document`
- [ ] T006 [P] Create `ContosoDashboard/Models/DocumentShare.cs`: `DocumentShareId` int PK; `DocumentId` int FK; `SharedWithUserId` int? FK → User; `SharedWithDepartment` [MaxLength(100)] nullable "Exactly one of SharedWithUserId / SharedWithDepartment is set"; `SharedByUserId` int FK → User "Must equal Document.UploadedByUserId"; `SharedDate` DateTime UTC; navigations `Document`, `SharedWithUser`, `SharedByUser`
- [ ] T007 [P] Create `ContosoDashboard/Models/TaskDocument.cs`: `TaskDocumentId` int PK; `TaskId` int FK → TaskItem; `DocumentId` int FK → Document; `AttachedByUserId` int FK → User; `AttachedDate` DateTime UTC; navigations `Task`, `Document`, `AttachedByUser`
- [ ] T008 [P] Create `ContosoDashboard/Models/DocumentActivity.cs`: `DocumentActivityId` int PK; `DocumentId` int "**No FK** (records survive deletion)"; `DocumentTitle` [MaxLength(255)] snapshot; `FileType` [MaxLength(255)] snapshot; `ActorUserId` int FK → User; `Action` [Required][MaxLength(30)]; `Details` [MaxLength(500)] nullable; `OccurredAt` DateTime UTC. In the same file add static class `DocumentActions` with string constants: Upload, Preview, Download, MetadataEdit, Replace, Delete, Share, RevokeShare, AttachToTask, ScanRejected
- [ ] T009 [P] Append `DocumentShared` and `DocumentAdded` to the end of the `NotificationType` enum in `ContosoDashboard/Models/Notification.cs` (append-only, so existing stored values stay the same)

### Schema

- [ ] T010 Update `ContosoDashboard/Data/ApplicationDbContext.cs` (depends on T004–T009):
  - Add `DbSet`s `Documents`, `DocumentTags`, `DocumentShares`, `TaskDocuments`, `DocumentActivities`.
  - Configure delete behaviors:
    - Document→User: Restrict
    - Document→Project: SetNull
    - DocumentTag/DocumentShare/TaskDocument→Document: Cascade
    - TaskDocument→TaskItem: Cascade
    - DocumentShare.SharedWithUser: Cascade
    - DocumentShare.SharedByUser: Restrict
    - TaskDocument.AttachedByUser: Restrict
    - DocumentActivity.ActorUser: Restrict
  - Add indexes:
    - Document: `UploadedByUserId`, `ProjectId`, `Category`, `UploadedDate`, and unique `StoragePath`
    - DocumentTag: `Tag`, and unique `(DocumentId, Tag)`
    - DocumentShare: `SharedWithUserId`, `SharedWithDepartment`, `DocumentId`
    - TaskDocument: unique `(TaskId, DocumentId)`
    - DocumentActivity: `OccurredAt`, `Action`, `ActorUserId`, `DocumentId`
  - Seed user 5: `UserId = 5`, `Email = "mateo.rossi@contoso.com"`, `DisplayName = "Mateo Rossi"`, `Department = "Marketing"`, `JobTitle = "Marketing Specialist"`, `Role = UserRole.Employee`, not a project member.

### Infrastructure abstractions (Constitution I)

- [ ] T011 [P] Create `ContosoDashboard/Services/IFileStorageService.cs` exactly per [contracts/services.md](contracts/services.md): `UploadAsync(Stream content, string storagePath, string contentType, CancellationToken ct = default)`, `DownloadAsync(string storagePath, CancellationToken ct = default)` (throws `FileNotFoundException` if missing), `DeleteAsync(string storagePath, CancellationToken ct = default)` (no-op if missing)
- [ ] T012 Create `ContosoDashboard/Services/LocalFileStorageService.cs` implementing `IFileStorageService` (depends on T002, T011):
  - Resolve the root from `FileStorage:RootPath` combined with `IWebHostEnvironment.ContentRootPath`.
  - Create directories on demand.
  - Map forward-slash `storagePath` to the OS path.
  - **Reject any resolved full path outside the root** with `Path.GetFullPath` plus a prefix check.
  - Use `System.IO` only.
- [ ] T013 [P] Create `ContosoDashboard/Services/DocumentFileRules.cs` (research R6):
  - `MaxFileSizeBytes = 25 * 1024 * 1024`.
  - Allow-list map from extension to MIME type and magic bytes:
    - `.pdf` → application/pdf, `%PDF-`
    - `.docx`/`.xlsx`/`.pptx` → the OpenXML MIME types, `50 4B 03 04`
    - `.doc`/`.xls`/`.ppt` → application/msword, application/vnd.ms-excel, application/vnd.ms-powerpoint, `D0 CF 11 E0 A1 B1 1A E1`
    - `.txt` → text/plain, no NUL bytes in the first 8 KB
    - `.jpg`/`.jpeg` → image/jpeg, `FF D8 FF`
    - `.png` → image/png, `89 50 4E 47 0D 0A 1A 0A`
  - Helpers: `TryGetMimeType(extension)`, `IsPreviewable(mime)` (pdf/jpeg/png only), `NormalizeExtension(fileName)`.
- [ ] T014 [P] Create `ContosoDashboard/Services/IMalwareScanner.cs` with `Task<ScanResult> ScanAsync(Stream content, string fileExtension, CancellationToken ct = default)` and `public sealed record ScanResult(bool IsClean, string? Reason)`
- [ ] T015 Create `ContosoDashboard/Services/LocalMalwareScanner.cs` implementing `IMalwareScanner` (depends on T013, T014):
  - Verify the file signature for the extension using `DocumentFileRules`.
  - Search the content for the EICAR test string `X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*`.
  - Return `IsClean=false` with a reason if either check fails.
  - Always reset the stream to position 0.

### Identity

- [ ] T016 [P] Add `new Claim("Department", user.Department ?? string.Empty)` to the claims list in `ContosoDashboard/Pages/Login.cshtml.cs` (Constitution II: all claims including Department)

### Document service core (authorization single source of truth — research R7)

- [ ] T017 Create `ContosoDashboard/Services/DocumentService.cs` with the `IDocumentService` interface and `DocumentService` class (depends on T010–T015):
  - Add the DTOs from [contracts/services.md](contracts/services.md): `DocumentUploadRequest`, `DocumentUploadResult`, `DocumentQuery` (plus a `DocumentSortBy` enum: Title, UploadedDate, Category, FileSize), `DocumentListItem`, `DocumentDetails`, `DocumentMetadataUpdate`, `DocumentFileResult`, `DocumentShareItem`, `OperationResult` with the generic message "Document not found or you do not have access.".
  - Inject `ApplicationDbContext`, `IFileStorageService`, `IMalwareScanner`, `INotificationService`, `ILogger<DocumentService>`.
  - Private helpers:
    - `LoadRequesterAsync(userId)` reads role and department **from the database**.
    - `AccessibleDocuments(requester)` returns an `IQueryable<Document>` that grants:
      - Administrators: all documents.
      - Everyone else, any of: uploader; project PM; project member; direct share; department share where the department is non-null and equal; or the requester is a TeamLead in the same non-null department as the uploader.
    - `CanManage(doc, requester)` is true for the uploader, the PM of the document's project, a TeamLead in the uploader's department, or an Administrator.
    - `CanShare(doc, requester)` is true for the uploader only.
    - `AddActivity(doc, actorId, action, details)` adds a `DocumentActivity` without saving.
    - `ToListItem` projection fills `CanPreview`, `CanManage`, `CanShare`.
    - `NormalizeTags(IEnumerable<string>)` trims, lower-cases, de-duplicates, keeps at most 10 tags, and rejects any tag over 50 characters.
- [ ] T018 Register services in `ContosoDashboard/Program.cs` after the existing registrations: `AddScoped<IDocumentService, DocumentService>()`, `AddSingleton<IFileStorageService, LocalFileStorageService>()`, `AddSingleton<IMalwareScanner, LocalMalwareScanner>()`. Do NOT modify the security-header middleware (depends on T012, T015, T017)
- [ ] T019 Delete `ContosoDashboard/ContosoDashboard.db`, run `dotnet build ContosoDashboard/ContosoDashboard.csproj` (must succeed, no new warnings), start the app and confirm the schema is created and all 5 seed users can log in

**Checkpoint**: Foundation ready. Schema, abstractions and authorization core exist, and user story work can begin.

---

## Phase 3: User Story 1 - Upload and view my documents (Priority: P1) 🎯 MVP

**Goal**: Users upload one or more validated files with metadata, see them in "My Documents", and download them through an authorized endpoint.

**Independent Test**: As Ni Kang, upload a PDF and a PNG with title, category, project and tags. Confirm both are listed with the correct metadata and download unchanged. Confirm that oversize, empty, unsupported and signature-mismatch files are rejected with nothing left on disk (quickstart scenario 1).

- [ ] T020 [US1] Implement `GetUploadableProjectsAsync(requestingUserId)` in `ContosoDashboard/Services/DocumentService.cs`: projects where the user is PM or a member (FR-008)
- [ ] T021 [US1] Implement `UploadAsync(request, content, requestingUserId)` in `ContosoDashboard/Services/DocumentService.cs`, strictly in this order (research R4):
  1. Validate size: 0 < size ≤ 25 MB. Use the messages "File is empty." / "File exceeds the 25 MB limit.".
  2. Validate the extension against the allow-list. Message: "Unsupported file type. Allowed: PDF, Word, Excel, PowerPoint, text, JPEG, PNG.".
  3. Validate that Title is non-empty and Category is valid.
  4. Normalize tags.
  5. If `ProjectId` is set, it must be in the uploadable projects, otherwise return the generic denial.
  6. Run `IMalwareScanner.ScanAsync`. On failure, log a `ScanRejected` activity and return "File failed the security scan and was not uploaded.".
  7. Generate `StoragePath` = `{userId}/{projectId or "personal"}/{Guid.NewGuid():N}{ext}`.
  8. Call `IFileStorageService.UploadAsync`.
  9. Add the Document, its Tags and an `Upload` activity, then call `SaveChangesAsync` once.
  10. If the save fails, delete the written file (best effort, log a warning) and return an error.

  Set `FileType` from `DocumentFileRules` (never the browser value), and set UTC dates.
- [ ] T022 [US1] Implement `GetMyDocumentsAsync(requestingUserId, query)` in `ContosoDashboard/Services/DocumentService.cs` returning the user's own uploads, newest first, as `DocumentListItem` with `AsNoTracking()` (sorting and filtering are added in US2)
- [ ] T023 [US1] Implement `OpenFileAsync(documentId, requestingUserId, preview)` download path in `ContosoDashboard/Services/DocumentService.cs`:
  - Apply the `AccessibleDocuments` filter. Return `null` if the document is not found, not permitted, or its file is missing.
  - Open the stream with `IFileStorageService.DownloadAsync`.
  - Log a `Download` activity and save.
  - Return `DocumentFileResult` with `Inline=false` and `DownloadFileName=OriginalFileName`.
- [ ] T024 [US1] Create `ContosoDashboard/Pages/DocumentFile.cshtml` (`@page "/documents/{documentId:int}/file"`) and `ContosoDashboard/Pages/DocumentFile.cshtml.cs` (`[Authorize]` PageModel):
  - Read the user id from `ClaimTypes.NameIdentifier`.
  - Call `OpenFileAsync`. Return `NotFound()` when the result is null (identical for missing and denied, FR-024).
  - Otherwise return `File(stream, contentType, downloadFileName)` with `Cache-Control: no-store`.
  - GET only.
- [ ] T025 [P] [US1] Create `ContosoDashboard/Shared/DocumentTable.razor`:
  - Parameters: `Items` (`List<DocumentListItem>`), `ShowUploader`, and `EventCallback`s for later actions.
  - Columns: title, category, upload date, human-readable size, file type, project, uploader.
  - Download link to `/documents/{id}/file`.
  - Empty-state row "No documents found.".
- [ ] T026 [P] [US1] Create `ContosoDashboard/Shared/DocumentUploadDialog.razor` (research R5):
  - Parameters: `ProjectId?`, `TaskId?`, `OnUploaded`. Inject `IDocumentService` and `AuthenticationStateProvider`.
  - `<InputFile multiple @key="inputKey">`. Per file, capture name, size and content type **before** opening the stream, and pre-reject files over 25 MB or 0 bytes.
  - Title defaults to the file name (editable per file). Required category select from `DocumentCategories.All`. Optional project select from `GetUploadableProjectsAsync` (fixed when `ProjectId` is supplied). Optional description and comma-separated tags shared by the batch.
  - Copy each file with `OpenReadStream(DocumentFileRules.MaxFileSizeBytes)` into a `MemoryStream` in 64 KB chunks, updating the per-file percentage and calling `StateHasChanged()`. Reset position, set the `IBrowserFile` reference to null, then call `UploadAsync`.
  - Show a per-file success ("Uploaded '{title}'.") or error message. Bump `inputKey` after the batch.
  - Validation messages for a missing title or category, without starting the upload.
- [ ] T027 [US1] Create `ContosoDashboard/Pages/Documents.razor` (`@page "/documents"`, `@attribute [Authorize]`, injects `IDocumentService` and `AuthenticationStateProvider`): "My Documents" heading, Upload button opening `DocumentUploadDialog`, `DocumentTable` bound to `GetMyDocumentsAsync`, list refreshed on `OnUploaded` (depends on T022, T025, T026)
- [ ] T028 [P] [US1] Add a "Documents" nav link (`href="documents"`, Bootstrap icon `bi-file-earmark-text`) to `ContosoDashboard/Shared/NavMenu.razor`
- [ ] T029 [US1] Delete `ContosoDashboard/ContosoDashboard.db`, build, run and execute [quickstart.md](quickstart.md) scenario 1 (steps 1–7), including that `ContosoDashboard/AppData/uploads/4/1/` holds only GUID-named files, one per successful upload

**Checkpoint**: MVP. Upload, list and secure download work independently.

---

## Phase 4: User Story 2 - Browse, sort, filter and search documents (Priority: P2)

**Goal**: Sort and filter "My Documents", and search all accessible documents by title, description, tags, uploader or project.

**Independent Test**: Sorting by size and filtering by category/date return the correct subsets. A search for `budget` returns results for Floris Kregel but none for Mateo Rossi (quickstart scenario 2).

- [ ] T030 [US2] Extend `GetMyDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs` to apply `DocumentQuery` server-side: filters `Category`, `ProjectId`, `FromDate`/`ToDate` (inclusive, UTC dates); sorting by Title, UploadedDate, Category or FileSize, ascending or descending
- [ ] T031 [US2] Implement `SearchAsync(requestingUserId, term, query)` in `ContosoDashboard/Services/DocumentService.cs`:
  - Start from `AccessibleDocuments`.
  - Match `EF.Functions.Like(x, "%term%")` against Title, Description, any Tag, uploader DisplayName or Project Name.
  - Apply the same `DocumentQuery` filters and sorting, with `AsNoTracking()`.
  - Return an empty list for a blank term.
- [ ] T032 [US2] Add filter and sort controls to `ContosoDashboard/Pages/Documents.razor`: category select, project select, from/to date inputs, sort-by select and ascending/descending toggle, re-querying `GetMyDocumentsAsync`
- [ ] T033 [P] [US2] Create `ContosoDashboard/Pages/DocumentSearch.razor` (`@page "/documents/search"`, `[Authorize]`): search box plus the same filter/sort controls, results in `DocumentTable` with `ShowUploader=true`, empty-state message "No documents match your search."
- [ ] T034 [US2] Add a "Search" link/button on `ContosoDashboard/Pages/Documents.razor` header pointing to `/documents/search` (same file as T032, so do it after T032)
- [ ] T035 [US2] Execute [quickstart.md](quickstart.md) scenario 2 as Ni Kang, Mateo Rossi and Floris Kregel

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: User Story 3 - Project documents for the team (Priority: P2)

**Goal**: Project members see and download the project's documents and can upload to the project. Members are notified of new project documents.

**Independent Test**: Ni Kang uploads to Project 1. Camille sees it on `/projects/1` with a notification. Mateo Rossi gets 404 on the direct file URL (quickstart scenario 3, steps 1 and 3; step 2 is validated after US4).

- [ ] T036 [US3] Implement `GetProjectDocumentsAsync(projectId, requestingUserId)` in `ContosoDashboard/Services/DocumentService.cs`: return an empty list unless the requester is PM, a member or an Administrator; otherwise list the project's documents (newest first) from `AccessibleDocuments`
- [ ] T037 [US3] Extend `UploadAsync` in `ContosoDashboard/Services/DocumentService.cs`: after a successful save of a document with `ProjectId`, create a `NotificationType.DocumentAdded` notification (Informational, title "New project document", message including the document title and project name) for the project PM and every project member **except the uploader** via `INotificationService` (FR-031)
- [ ] T038 [US3] Add a "Documents" card to `ContosoDashboard/Pages/ProjectDetails.razor`: inject `IDocumentService`, list with `DocumentTable` (`ShowUploader=true`) from `GetProjectDocumentsAsync`, Upload button opening `DocumentUploadDialog` with `ProjectId` set, refresh on upload
- [ ] T039 [US3] Execute [quickstart.md](quickstart.md) scenario 3, steps 1 and 3

**Checkpoint**: Project documents work. Acceptance scenarios 4–5 of US3 (PM delete/edit) become testable after Phase 6.

---

## Phase 6: User Story 4 - Manage my documents: edit, replace, delete (Priority: P3)

**Goal**: The uploader, the PM of the document's project, a TeamLead in the uploader's department, or an Administrator can edit metadata, replace the file, or permanently delete a document after confirmation.

**Independent Test**: Edit, replace and delete as the uploader. Camille (PM) edits and replaces a project document. Floris (TeamLead) manages Ni Kang's personal file. Camille gets 404 on Ni Kang's personal file (quickstart scenarios 3.2 and 4).

- [ ] T040 [US4] Implement `GetDocumentAsync(documentId, requestingUserId)` in `ContosoDashboard/Services/DocumentService.cs` returning `DocumentDetails` (`null` if not accessible)
- [ ] T041 [US4] Implement `UpdateMetadataAsync(documentId, update, requestingUserId)` in `ContosoDashboard/Services/DocumentService.cs`: require `CanManage`; validate Title "Required, trimmed, non-empty" (≤ 255), Description ≤ 2000, Category in `DocumentCategories.All`; replace tags with `NormalizeTags` (max 10, each ≤ 50 characters); set `LastModifiedDate`; log `MetadataEdit`; one save
- [ ] T042 [US4] Implement `ReplaceFileAsync(documentId, originalFileName, sizeBytes, content, requestingUserId)` in `ContosoDashboard/Services/DocumentService.cs`, ordered per research R4:
  1. Require `CanManage`.
  2. Run the same size, extension and scan validation as upload.
  3. Write the content to a **new** GUID `StoragePath`.
  4. Update `StoragePath`, `OriginalFileName`, `FileType`, `FileExtension`, `FileSizeBytes` and `LastModifiedDate`, and log a `Replace` activity in one save.
  5. Then delete the old file (best effort, log a warning on failure).
  6. If the save fails, delete the new file.
- [ ] T043 [US4] Implement `DeleteAsync(documentId, requestingUserId)` in `ContosoDashboard/Services/DocumentService.cs`:
  1. Require `CanManage`.
  2. Snapshot title and type into a `Delete` activity.
  3. Remove the Document (tags, shares and task links cascade) in one save.
  4. Then delete the file via `IFileStorageService.DeleteAsync` (best effort, log a warning).
- [ ] T044 [P] [US4] Create `ContosoDashboard/Shared/ConfirmDialog.razor`: Bootstrap modal with parameters `Title`, `Message`, `ConfirmText`, `OnConfirm`, `OnCancel`
- [ ] T045 [P] [US4] Create `ContosoDashboard/Shared/DocumentEditDialog.razor`:
  - Load the document with `GetDocumentAsync`.
  - Edit title, description, category and tags, then call `UpdateMetadataAsync`.
  - Separate "Replace file" section using `<InputFile @key>` with the same MemoryStream pattern as T026 (capture metadata first, chunked copy, clear the reference), then call `ReplaceFileAsync`.
  - Show result messages. `OnSaved` callback.
- [ ] T046 [US4] Extend `ContosoDashboard/Shared/DocumentTable.razor` with Edit and Delete buttons shown only when `item.CanManage`, raising `OnEdit` / `OnDelete` callbacks
- [ ] T047 [US4] Wire `DocumentEditDialog` and `ConfirmDialog` ("Permanently delete '{title}'? This cannot be undone.") into `ContosoDashboard/Pages/Documents.razor`, calling `DeleteAsync` and refreshing the list
- [ ] T048 [US4] Wire the same edit/delete handling into the Documents card of `ContosoDashboard/Pages/ProjectDetails.razor`
- [ ] T049 [US4] Execute [quickstart.md](quickstart.md) scenario 4 and scenario 3 step 2, including a permitted and a denied role for edit, replace and delete

**Checkpoint**: Full document lifecycle works, and US3 acceptance scenarios 4–5 now pass.

---

## Phase 7: User Story 5 - Share documents and "Shared with Me" (Priority: P3)

**Goal**: The uploader shares with users or departments and can revoke shares. Recipients are notified and see the documents in "Shared with Me".

**Independent Test**: Ni Kang shares with Mateo and with Marketing, then revokes the shares. Mateo's access follows immediately. Camille and the Administrator cannot share or revoke (quickstart scenario 5).

- [ ] T050 [US5] Implement `ShareWithUserAsync` and `ShareWithDepartmentAsync` in `ContosoDashboard/Services/DocumentService.cs`:
  - Require `CanShare` (uploader only).
  - Reject self-shares and duplicates for the same (DocumentId, SharedWithUserId) or (DocumentId, SharedWithDepartment).
  - The department must exist among `Users.Department`.
  - Create the `DocumentShare` with `SharedByUserId` = the uploader and a `Share` activity whose Details names the recipient.
  - Save, then create `NotificationType.DocumentShared` notifications: the recipient user, or every user in the department except the uploader.
- [ ] T051 [US5] Implement `RevokeShareAsync(documentShareId, requestingUserId)` and `GetSharesAsync(documentId, requestingUserId)` in `ContosoDashboard/Services/DocumentService.cs`: both require the requester to be the uploader; revoke deletes the row and logs a `RevokeShare` activity; the list returns `DocumentShareItem` (user display name or "Department: X")
- [ ] T052 [US5] Implement `GetSharedWithMeAsync(requestingUserId, query)` in `ContosoDashboard/Services/DocumentService.cs`: documents with a direct share to the user or a share to the user's non-null department, excluding the user's own uploads, applying `DocumentQuery` filters and sorting
- [ ] T053 [P] [US5] Create `ContosoDashboard/Shared/DocumentShareDialog.razor`:
  - Inject `IDocumentService` and `IUserService`.
  - User picker from `GetAllUsersAsync` (excluding self).
  - Department picker of distinct non-null departments.
  - Lists current shares with Revoke buttons.
  - Shows result messages.
- [ ] T054 [US5] Extend `ContosoDashboard/Shared/DocumentTable.razor` with a Share button shown only when `item.CanShare`, raising `OnShare`, and wire `DocumentShareDialog` into `ContosoDashboard/Pages/Documents.razor`
- [ ] T055 [P] [US5] Create `ContosoDashboard/Pages/SharedDocuments.razor` (`@page "/documents/shared"`, `[Authorize]`): "Shared with Me" list via `GetSharedWithMeAsync` in `DocumentTable` (`ShowUploader=true`), with filter/sort controls like `Documents.razor`
- [ ] T056 [US5] Add a "Shared with Me" nav link (`href="documents/shared"`, icon `bi-people`) to `ContosoDashboard/Shared/NavMenu.razor`
- [ ] T057 [US5] Execute [quickstart.md](quickstart.md) scenario 5, steps 1–4

**Checkpoint**: Sharing and revocation work, and access changes take effect immediately.

---

## Phase 8: User Story 6 - Preview documents in the browser (Priority: P3)

**Goal**: PDF, JPEG and PNG documents open inline in a new browser tab. Other types are download-only. Security headers stay unchanged.

**Independent Test**: PDF and PNG previews open inline within 3 s. A .docx offers download only. `X-Frame-Options: DENY` is still present. A denied user gets 404 (quickstart scenario 6).

- [ ] T058 [US6] Extend `OpenFileAsync` in `ContosoDashboard/Services/DocumentService.cs`: when `preview=true` and `DocumentFileRules.IsPreviewable(FileType)`, return `Inline=true` and log a **`Preview`** activity (distinct from `Download`, Clarification Q3); otherwise behave as a download
- [ ] T059 [US6] Update `ContosoDashboard/Pages/DocumentFile.cshtml.cs` to accept `[FromQuery] bool preview`. When `Inline`, set `Content-Disposition: inline; filename*=UTF-8''{escaped name}` and return `File(stream, contentType)` without a download name; keep `Cache-Control: no-store`. Do not change global headers (research R2)
- [ ] T060 [US6] Add a "Preview" link to `ContosoDashboard/Shared/DocumentTable.razor` when `item.CanPreview`: `href="/documents/{id}/file?preview=true" target="_blank" rel="noopener noreferrer"`
- [ ] T061 [US6] Execute [quickstart.md](quickstart.md) scenario 6, steps 1–3, plus a denied preview attempt as Mateo Rossi

**Checkpoint**: Preview works without weakening security headers.

---

## Phase 9: User Story 7 - Documents in tasks and on the dashboard (Priority: P4)

**Goal**: Attach or upload documents from a task, with the task's project inherited. The dashboard shows Recent Documents and a document count.

**Independent Test**: Upload from task "Implement authentication" and confirm it is linked to the task and Project 1. The dashboard shows the 5 latest documents and the correct count (quickstart scenario 7).

- [ ] T062 [US7] Add a private `CanViewTaskAsync(taskId, requester)` helper in `ContosoDashboard/Services/DocumentService.cs`: Administrator, assignee, creator, or PM/member of the task's project
- [ ] T063 [US7] Implement `AttachToTaskAsync`, `DetachFromTaskAsync` and `GetTaskDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs`:
  - Require `CanViewTaskAsync` and document access.
  - Enforce unique `(TaskId, DocumentId)`.
  - If the task has a ProjectId and the document has none, set `Document.ProjectId` to the task's project (FR-029).
  - If the document belongs to a different project, reject with "Document belongs to a different project.".
  - Log an `AttachToTask` activity.
  - Detach removes the link only.
- [ ] T064 [US7] Extend `UploadAsync` in `ContosoDashboard/Services/DocumentService.cs` to handle `request.TaskId`:
  - Require `CanViewTaskAsync`.
  - Default `ProjectId` to the task's ProjectId.
  - Create the `TaskDocument` link in the same save as the Document.
- [ ] T065 [US7] Add a per-task "Documents" button and modal to `ContosoDashboard/Pages/Tasks.razor`: inject `IDocumentService`, list attached documents with `GetTaskDocumentsAsync`, attach an existing accessible document (select from `GetMyDocumentsAsync` + `GetSharedWithMeAsync`), upload a new one via `DocumentUploadDialog` with `TaskId`, and detach
- [ ] T066 [US7] Implement `GetRecentDocumentsAsync(requestingUserId, count = 5)` and `GetMyDocumentCountAsync(requestingUserId)` in `ContosoDashboard/Services/DocumentService.cs`: the user's own uploads, newest first
- [ ] T067 [P] [US7] Add `public int DocumentCount { get; set; }` to `DashboardSummary` and populate it in `GetDashboardSummaryAsync` (count of `Documents` where `UploadedByUserId == userId`) in `ContosoDashboard/Services/DashboardService.cs`
- [ ] T068 [US7] Update `ContosoDashboard/Pages/Index.razor`: add a "My Documents" summary card showing `DocumentCount` and a "Recent Documents" widget listing `GetRecentDocumentsAsync` (title, date, download link) with a "View all" link to `/documents` (depends on T066, T067)
- [ ] T069 [US7] Execute [quickstart.md](quickstart.md) scenario 7, steps 1–2

**Checkpoint**: Task and dashboard integration work.

---

## Phase 10: User Story 8 - Audit activity and administrator reports (Priority: P4)

**Goal**: Every document action is recorded. Administrators view uploads by type, top uploaders, and preview/download access patterns over a date range.

**Independent Test**: After performing actions, the Administrator's report matches them, including deleted documents. Ni Kang is denied and sees no nav link (quickstart scenario 8).

- [ ] T070 [P] [US8] Create `ContosoDashboard/Services/DocumentReportService.cs` with `IDocumentReportService.GetReportAsync(fromUtc, toUtc, requestingUserId)`:
  - Return `null` unless the requester's DB role is Administrator.
  - Build `DocumentReport` with:
    - `UploadsByFileType` (Upload activities grouped by FileType).
    - `TopUploaders` (top 10 by Upload count, joined to user DisplayName).
    - `AccessByDocument` (top 20 by Preview + Download, with separate Previews/Downloads columns).
    - `AccessByDay` (date, previews, downloads).
  - All built from `DocumentActivities` with `AsNoTracking()`.
- [ ] T071 [US8] Register `builder.Services.AddScoped<IDocumentReportService, DocumentReportService>();` in `ContosoDashboard/Program.cs`
- [ ] T072 [US8] Create `ContosoDashboard/Pages/DocumentReports.razor` (`@page "/documents/reports"`, `@attribute [Authorize(Policy = "Administrator")]`): from/to date inputs (default last 30 days), four Bootstrap tables for the report sections, "Not authorized." message if the service returns null
- [ ] T073 [US8] Add a "Document Reports" nav link (`href="documents/reports"`, icon `bi-bar-chart`) inside `<AuthorizeView Roles="Administrator">` in `ContosoDashboard/Shared/NavMenu.razor`
- [ ] T074 [US8] Audit sweep of `ContosoDashboard/Services/DocumentService.cs`: confirm an activity is written for Upload, Preview, Download, MetadataEdit, Replace, Delete, Share, RevokeShare, AttachToTask and ScanRejected (FR-032); add any missing ones
- [ ] T075 [US8] Execute [quickstart.md](quickstart.md) scenario 8, steps 1–2

**Checkpoint**: All user stories are complete and independently validated.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, security/performance review and full regression

- [ ] T076 [P] Update `README.md`:
  - Add Document Management to Implemented Features.
  - Add the new pages to the Application Pages table.
  - Add the new files to Project Structure.
  - Add Mateo Rossi to the sample users.
  - Add a "Training limitations" note: malware scanning is simulated (signature + EICAR), files are stored in `ContosoDashboard/AppData/uploads`, delete `ContosoDashboard.db` after pulling.
  - Describe the database as SQLite.
- [ ] T077 [P] Security review:
  - Grep `ContosoDashboard/Pages` and `ContosoDashboard/Shared` to confirm no component injects `ApplicationDbContext`.
  - Confirm that no user-supplied file name reaches `IFileStorageService`.
  - Confirm that every new page has `[Authorize]`.
  - Confirm the security-header middleware in `ContosoDashboard/Program.cs` is unchanged.
- [ ] T078 [P] Performance review of `ContosoDashboard/Services/DocumentService.cs`: read queries use `AsNoTracking()` and server-side filtering/sorting with no client-side evaluation. Spot-check that list and search for ~500 documents render in ≤ 2 s
- [ ] T079 Run `dotnet build ContosoDashboard/ContosoDashboard.csproj` (no new warnings), delete `ContosoDashboard/ContosoDashboard.db`, run the full [quickstart.md](quickstart.md) scenarios 1–9 including the README security tests 1–4, and confirm that no file under `ContosoDashboard/AppData/uploads` lacks a document row and no document row lacks a file

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup. **Blocks all user stories**
- **US1 (Phase 3)**: Depends on Foundational. It is the MVP
- **US2–US8 (Phases 4–10)**: Depend on Foundational and reuse US1 UI (`DocumentTable`, `DocumentUploadDialog`, `Documents.razor`, `DocumentFile` page)
- **Polish (Phase 11)**: Depends on all desired stories

### User Story Dependencies

| Story | Depends on | Notes |
|---|---|---|
| US1 (P1) | Foundational | MVP |
| US2 (P2) | US1 (`Documents.razor`, `DocumentTable`) | Service methods are independent |
| US3 (P2) | US1 (`DocumentUploadDialog`, `DocumentTable`) | Scenarios 4–5 are validated after US4 |
| US4 (P3) | US1 | Also wires into the US3 ProjectDetails card (T048) |
| US5 (P3) | US1 | Access via shares is already in the foundational filter (T017) |
| US6 (P3) | US1 (`DocumentFile` page) | — |
| US7 (P4) | US1 (`DocumentUploadDialog`) | Attach list uses `GetSharedWithMeAsync` from US5 if present, otherwise own documents only |
| US8 (P4) | Foundational (activities written from US1 on) | Report accuracy depends on the actions implemented |

### Within Each Story

Service methods → shared components → pages → nav → quickstart validation. Tasks on `DocumentService.cs`, `DocumentTable.razor`, `Documents.razor`, `NavMenu.razor` and `Program.cs` are sequential.

### Parallel Opportunities

- **Phase 1**: T002 and T003 can run in parallel after or alongside T001.
- **Phase 2**: entities T004–T009 can run in parallel. T011, T013, T014 and T016 can run in parallel. Then T010 → T012/T015 → T017 → T018 → T019.
- **US1**: T025, T026 and T028 can run in parallel with the service tasks T020–T023.
- **US2**: T033 can run in parallel with T030–T032.
- **US4**: T044 and T045 can run in parallel with the service tasks T040–T043.
- **US5**: T053 and T055 can run in parallel with T050–T052.
- **US7**: T067 can run in parallel with T062–T066.
- **US8**: T070 can run in parallel with work on any other story once Foundational is done.
- **Polish**: T076, T077 and T078 can run in parallel.

---

## Parallel Example: User Story 1

```text
# After Phase 2, in parallel:
Task: "T025 [P] [US1] Create ContosoDashboard/Shared/DocumentTable.razor"
Task: "T026 [P] [US1] Create ContosoDashboard/Shared/DocumentUploadDialog.razor"
Task: "T028 [P] [US1] Add Documents nav link in ContosoDashboard/Shared/NavMenu.razor"
# Meanwhile, sequentially on DocumentService.cs:
Task: "T020 → T021 → T022 → T023"
# Then: T024 (DocumentFile page) → T027 (Documents.razor) → T029 (validation)
```

## Parallel Example: Foundational

```text
Task: "T004 Document.cs"   Task: "T005 DocumentTag.cs"   Task: "T006 DocumentShare.cs"
Task: "T007 TaskDocument.cs"   Task: "T008 DocumentActivity.cs"   Task: "T009 NotificationType"
Task: "T011 IFileStorageService.cs"   Task: "T013 DocumentFileRules.cs"   Task: "T014 IMalwareScanner.cs"   Task: "T016 Login Department claim"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → Phase 2 Foundational (T001–T019)
2. Phase 3 US1 (T020–T029)
3. **STOP and VALIDATE** with quickstart scenario 1, then demo

### Incremental Delivery

1. Foundation + US1 gives the MVP: secure upload, list and download
2. Add US2 (search) and US3 (project documents) for the P2 increment
3. Add US4 (manage), US5 (share) and US6 (preview) for the P3 increment
4. Add US7 (tasks/dashboard) and US8 (audit reports) for the P4 increment
5. Polish (T076–T079) before declaring the feature complete

### Notes

- Re-run `dotnet build` after each task group. Delete `ContosoDashboard.db` whenever entities change.
- UI gating (`CanManage`, `CanShare`, `CanPreview`) is a convenience only. Every service method re-authorizes (Constitution II).
- Commit after each task or logical group, and stop at any checkpoint to validate.
