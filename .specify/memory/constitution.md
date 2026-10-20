<!--
Sync Impact Report
- Version change: (unversioned template) → 1.0.0
- Template source: .specify/templates/constitution-template.md (core fallback; resolver script
  failed because Resolve-TemplateContent is missing from scripts/powershell/common.ps1)
- Modified principles (placeholder → defined):
  - [PRINCIPLE_1_NAME] → I. Offline-First with Cloud-Ready Abstractions
  - [PRINCIPLE_2_NAME] → II. Security by Default (NON-NEGOTIABLE)
  - [PRINCIPLE_3_NAME] → III. Layered Architecture & Service-Layer Logic
  - [PRINCIPLE_4_NAME] → IV. Data Integrity & Schema Consistency
  - [PRINCIPLE_5_NAME] → V. Simplicity & Training Clarity
- Added sections: Technology Stack & Constraints; Development Workflow & Quality Gates
- Removed sections: none
- Follow-up TODOs:
  - Re-sync Spec Kit PowerShell scripts so resolve-template.ps1 works.
  - README states SQL Server LocalDB while the project uses SQLite; align README separately.
-->

# ContosoDashboard Constitution

## Core Principles

### I. Offline-First with Cloud-Ready Abstractions

- The application MUST run fully offline on a developer machine with no cloud subscription,
  external service, or internet connection.
- Every infrastructure dependency (file storage, database provider, identity, messaging) MUST be
  accessed through an interface registered via dependency injection (e.g., `IFileStorageService`).
- Local implementations (e.g., `LocalFileStorageService` using `System.IO`) are the default;
  cloud implementations (e.g., Azure Blob Storage, Microsoft Entra ID) MUST be swappable through
  DI/configuration only, with no changes to business logic, UI, or database schema.
- Cloud SDK packages MUST NOT be referenced by the training implementation unless a feature
  specification explicitly requires it.

Rationale: The project is a training asset that must work anywhere, while teaching the
abstraction patterns required for a production migration path.

### II. Security by Default (NON-NEGOTIABLE)

- Every page except `/login` MUST require authentication (`[Authorize]`); role-restricted
  features MUST use role/policy checks consistent with the hierarchy
  Employee < TeamLead < ProjectManager < Administrator.
- Authorization MUST be enforced in the service layer, not only in the UI. Every service method
  that reads or mutates user-owned data MUST verify the requesting user's identity and
  permissions (IDOR protection).
- Users MUST only see data they are authorized to access, including in lists, search results,
  dashboard widgets, and notifications.
- Existing security headers, cookie settings, and antiforgery protection MUST NOT be weakened.
- File handling MUST: store files outside `wwwroot`; serve them only through authorized
  endpoints; validate size and extension against an allow-list; never use user-supplied file
  names in storage paths (use GUID-based names).
- The login flow MUST issue all claims required by services (NameIdentifier, Name, Email, Role,
  Department).
- Mock authentication is accepted for training only and MUST remain clearly documented as such.

Rationale: The application demonstrates defense in depth; any regression in authorization is a
correctness defect, not a cosmetic one.

### III. Layered Architecture & Service-Layer Logic

- Code MUST follow the existing layers: `Models/` (entities), `Data/` (EF Core `DbContext`),
  `Services/` (business logic + authorization), `Pages/` and `Shared/` (Blazor/Razor UI).
- Business rules, validation, and data access MUST live in services exposed via interfaces;
  Razor components MUST NOT query `ApplicationDbContext` directly.
- New services MUST be registered in `Program.cs` via DI and consumed through their interfaces.
- Blazor file uploads MUST copy `IBrowserFile` streams into a `MemoryStream` before processing,
  capture metadata before opening the stream, and reset the `InputFile` (via `@key`) after use.

Rationale: Clear separation keeps features testable, secure, and migratable, and keeps the
codebase readable for learners.

### IV. Data Integrity & Schema Consistency

- Primary keys for new entities MUST be integers, consistent with existing `User` and `Project`
  keys.
- Enumerated business values (e.g., document categories) MUST be stored as text unless a
  specification justifies otherwise.
- String columns MUST be sized for real-world values (e.g., MIME type fields ≥ 255 characters).
- Operations combining storage and database writes MUST follow: generate unique path → write
  file → persist metadata. Failures MUST NOT leave orphaned records or files.
- Stored file paths MUST be relative and portable across local and cloud storage.
- Schema changes MUST keep seed data valid and MUST document any required database reset.

Rationale: Consistent keys and ordered writes prevent the duplicate-key and orphaned-record
failures observed in prior iterations.

### V. Simplicity & Training Clarity

- Features MUST fit the current architecture; major rewrites or new frameworks require an
  explicit, justified amendment or a documented Complexity Tracking entry in the plan.
- Prefer the simplest implementation that satisfies the specification (YAGNI); out-of-scope
  items listed in a specification MUST NOT be implemented.
- Code MUST be readable by learners: descriptive names, no dead code, and comments only where
  intent is not evident from the code.
- Known limitations of training shortcuts MUST be documented in the README or the feature spec.

Rationale: The repository exists to teach Spec-Driven Development; unnecessary complexity
obscures the lesson.

## Technology Stack & Constraints

- Platform: ASP.NET Core 8.0 (`net8.0`) with Blazor Server and Razor Pages for auth endpoints.
- Data: Entity Framework Core 8 with the provider configured in `appsettings.json` (currently
  SQLite); the database is created and seeded automatically on startup.
- UI: Bootstrap 5.3 and Bootstrap Icons; no additional front-end frameworks without amendment.
- Performance targets (default unless a spec states otherwise): list pages ≤ 2 s for up to
  500 records; search ≤ 2 s; preview ≤ 3 s; 25 MB upload ≤ 30 s on a typical network.
- Activity on sensitive data (uploads, downloads, deletions, sharing) MUST be logged for audit.
- The application is NOT intended for production use; production-only concerns (MFA, real
  identity provider, TLS termination) are out of scope unless a spec explicitly includes them.

## Development Workflow & Quality Gates

- Work follows the Spec Kit flow: `/speckit.specify` → `/speckit.clarify` (as needed) →
  `/speckit.plan` → `/speckit.tasks` → `/speckit.analyze` → `/speckit.implement`.
- Every plan MUST include a Constitution Check against Principles I–V; violations MUST be
  justified in Complexity Tracking or the plan MUST be revised.
- Before a feature is considered complete:
  - `dotnet build` MUST succeed with no new errors or warnings introduced by the change.
  - The application MUST start and the affected pages MUST be exercised manually.
  - The README security scenarios (authentication required, user isolation, IDOR protection,
    role-based access) MUST still pass.
  - New authorization rules MUST be verified for at least one permitted and one denied role.
- Reviews MUST confirm that no secrets, cloud credentials, or production connection strings are
  committed.

## Governance

- This constitution supersedes other project practices and guidance. Where a spec, plan, or
  task conflicts with it, the constitution prevails until amended.
- Amendments MUST be made via `/speckit.constitution`, include a Sync Impact Report, and state
  the rationale and any migration impact on existing specs or code.
- Versioning follows semantic versioning: MAJOR for removed or redefined principles; MINOR for
  new principles/sections or materially expanded guidance; PATCH for clarifications and wording.
- Compliance is reviewed at plan time (Constitution Check), during `/speckit.analyze`, and in
  code review of every change. Unjustified complexity or security regressions MUST block merge.
- Runtime development guidance lives in `README.md` and the feature documents under `specs/`.

**Version**: 1.0.0 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-20
