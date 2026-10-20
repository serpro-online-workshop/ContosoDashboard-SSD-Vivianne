# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-document-upload-management/spec.md`

## Summary

Add secure document upload, organization, sharing, preview, task/dashboard integration and admin audit reports to ContosoDashboard. Files are validated (size, extension allow-list, signature + EICAR "scan") and stored on the local filesystem outside `wwwroot` through an `IFileStorageService` abstraction using GUID-based relative paths; metadata lives in new EF Core entities in the existing SQLite database. All authorization is enforced in a new `DocumentService` (single "accessible documents" filter based on role, project membership, department and shares), files are served only through an `[Authorize]` Razor Page that re-checks access and writes audit records, and Blazor pages/components provide the UI. Details: [research.md](research.md).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (`net8.0`)  
**Primary Dependencies**: ASP.NET Core 8 Blazor Server + Razor Pages, EF Core 8 (Sqlite provider), Bootstrap 5.3 — no new NuGet packages  
**Storage**: SQLite (`ContosoDashboard.db`, `EnsureCreated`) for metadata; local filesystem `ContosoDashboard/AppData/uploads` (configurable `FileStorage:RootPath`) for file content  
**Testing**: `dotnet build` + manual role-based validation per [quickstart.md](quickstart.md) (no test project exists; see research R12)  
**Target Platform**: Windows/Linux/macOS developer machine, offline, browser client  
**Project Type**: Single ASP.NET Core web application (`ContosoDashboard/`)  
**Performance Goals**: Lists/search ≤ 2 s for 500 docs; preview ≤ 3 s; 25 MB upload ≤ 30 s  
**Constraints**: Fully offline; no cloud SDKs; files outside `wwwroot`; integer keys; text categories; existing security headers unchanged; max 25 MB/file  
**Scale/Scope**: Training app — 5 seed users, ≤ 500 documents per user; 4 new pages + 1 file endpoint, 5 shared components, 5 new entities, 4 new services/abstractions

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Pre-research | Post-design |
|---|---|---|---|
| I. Offline-First, Cloud-Ready | Works offline; infra behind DI interfaces; no cloud SDKs | PASS — local FS + SQLite | PASS — `IFileStorageService`, `IMalwareScanner` swappable via DI; relative blob-compatible paths (R3, R6) |
| II. Security by Default | `[Authorize]` everywhere; service-layer authZ; IDOR; files outside `wwwroot`, allow-list, GUID names; all claims incl. Department; headers not weakened | PASS — required by spec FR-023–FR-027 | PASS — single access filter (R7); 404 on deny; Razor Page file endpoint re-authorizes (R1); preview in new tab keeps `X-Frame-Options: DENY` (R2); server-derived MIME (R6); Department claim added |
| III. Layered Architecture | Models/Data/Services/Pages; no `DbContext` in components; DI; InputFile pattern | PASS | PASS — new code only in existing folders; file endpoint follows existing Razor Page convention; InputFile → MemoryStream + `@key` (R5) |
| IV. Data Integrity | int keys; text category; MIME ≥ 255; path → file → record; relative paths; seed valid; DB reset documented | PASS | PASS — [data-model.md](data-model.md); upload/replace/delete ordering (R4); quickstart documents DB reset |
| V. Simplicity | No rewrites/new frameworks; YAGNI; out-of-scope excluded | PASS | PASS — no new packages; `GetUrlAsync` and migrations deferred with rationale (R3, R9); no version history/soft delete |
| Tech Stack & Constraints | .NET 8, EF Core provider from config, Bootstrap; perf targets; audit logging | PASS | PASS — indexes + server-side queries (R8); `DocumentActivity` (R11) |
| Quality Gates | Build clean; manual exercise; README security scenarios; allow/deny per role | PASS | PASS — quickstart scenarios 1–9 |

No violations → Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── services.md      # Service/abstraction interfaces and DTOs
│   └── http-and-ui.md   # File endpoint, UI routes, components, messages
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit.tasks — not created here)
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Models/
│   ├── Document.cs                 # NEW
│   ├── DocumentTag.cs              # NEW
│   ├── DocumentShare.cs            # NEW
│   ├── TaskDocument.cs             # NEW
│   ├── DocumentActivity.cs         # NEW (+ DocumentActions constants)
│   ├── DocumentCategories.cs       # NEW (allowed category text values)
│   └── Notification.cs             # MODIFIED: append DocumentShared, DocumentAdded
├── Data/
│   └── ApplicationDbContext.cs     # MODIFIED: DbSets, keys, indexes, delete behaviors, seed user 5
├── Services/
│   ├── IFileStorageService.cs      # NEW abstraction
│   ├── LocalFileStorageService.cs  # NEW (System.IO, root containment check)
│   ├── IMalwareScanner.cs          # NEW abstraction (+ ScanResult)
│   ├── LocalMalwareScanner.cs      # NEW (signature + EICAR)
│   ├── DocumentFileRules.cs        # NEW (extension → MIME/signature allow-list, size limit)
│   ├── DocumentService.cs          # NEW (IDocumentService + DTOs, authorization, activity, notifications)
│   ├── DocumentReportService.cs    # NEW (IDocumentReportService, admin only)
│   └── DashboardService.cs         # MODIFIED: DocumentCount
├── Pages/
│   ├── Documents.razor             # NEW  /documents
│   ├── SharedDocuments.razor       # NEW  /documents/shared
│   ├── DocumentSearch.razor        # NEW  /documents/search
│   ├── DocumentReports.razor       # NEW  /documents/reports (Administrator)
│   ├── DocumentFile.cshtml(.cs)    # NEW  GET /documents/{id}/file[?preview=true]
│   ├── Login.cshtml.cs             # MODIFIED: add Department claim
│   ├── Index.razor                 # MODIFIED: Recent Documents widget + count card
│   ├── ProjectDetails.razor        # MODIFIED: project documents section + upload
│   └── Tasks.razor                 # MODIFIED: per-task documents dialog
├── Shared/
│   ├── DocumentUploadDialog.razor  # NEW
│   ├── DocumentTable.razor         # NEW
│   ├── DocumentEditDialog.razor    # NEW
│   ├── DocumentShareDialog.razor   # NEW
│   ├── ConfirmDialog.razor         # NEW
│   └── NavMenu.razor               # MODIFIED: Documents + Reports links
├── Program.cs                      # MODIFIED: DI registrations only (headers untouched)
└── appsettings.json                # MODIFIED: FileStorage:RootPath = "AppData/uploads"

.gitignore                          # MODIFIED: ContosoDashboard/AppData/
README.md                           # MODIFIED: feature notes, training limitations (simulated scan)
```

**Structure Decision**: Keep the existing single-project layered layout (`Models/`, `Data/`, `Services/`, `Pages/`, `Shared/`) per Constitution III; no new projects or top-level folders. The only HTTP endpoint is a Razor Page, matching the existing Login/Logout pattern.

## Phase Outputs

- **Phase 0** — [research.md](research.md): R1–R13, no open NEEDS CLARIFICATION.
- **Phase 1** — [data-model.md](data-model.md), [contracts/services.md](contracts/services.md), [contracts/http-and-ui.md](contracts/http-and-ui.md), [quickstart.md](quickstart.md).
- **Phase 2** — `/speckit.tasks` (not part of this command). Suggested order by priority: foundation (entities, DbContext, storage/scanner, DocumentService core, file page, Department claim, seed user) → US1 → US2/US3 → US4/US5/US6 → US7/US8 → README/regression.

## Risks & Notes

- **DB reset**: `EnsureCreated` will not alter an existing DB — delete `ContosoDashboard.db` once (quickstart).
- **Scoped `DbContext` in Blazor circuits**: follows existing services' pattern; each upload/replace/delete uses a single `SaveChangesAsync` to limit long-lived tracking issues.
- **Corporate antivirus** may quarantine the EICAR test file; the signature-mismatch test covers the rejection path.
- **README inconsistency** (LocalDB vs SQLite) noted in the constitution follow-ups; README update for this feature should use SQLite.

## Complexity Tracking

No constitution violations; nothing to justify.
