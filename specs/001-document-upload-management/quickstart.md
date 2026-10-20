# Quickstart & Validation Guide: Document Upload and Management

**Feature**: `001-document-upload-management` | Contracts: [services.md](contracts/services.md), [http-and-ui.md](contracts/http-and-ui.md) | Model: [data-model.md](data-model.md)

## Prerequisites

- .NET 8 SDK (`dotnet --version` ≥ 8.0). If not on PATH: `& "C:\Program Files\dotnet\dotnet.exe"`.
- Sample files: a small PDF, a PNG/JPEG, a .docx, a .txt, a file > 25 MB, a 0-byte file, and an `.exe` renamed to `.pdf` (signature mismatch).
- Optional: EICAR test file saved as `eicar.txt`. Corporate antivirus may quarantine it immediately — if so, rely on the renamed `.exe` test.

## Setup

```powershell
# from repo root — schema changed, recreate the SQLite DB (EnsureCreated)
Remove-Item ContosoDashboard/ContosoDashboard.db -ErrorAction SilentlyContinue
dotnet build ContosoDashboard/ContosoDashboard.csproj        # must succeed, no new warnings
dotnet run --launch-profile http --project ContosoDashboard/ContosoDashboard.csproj
# open http://localhost:5000 and log in via the user dropdown
```

Uploaded files appear under `ContosoDashboard/AppData/uploads/` (git-ignored).

## Seed users for validation

| User | Role | Department | Project 1 |
|---|---|---|---|
| System Administrator | Administrator | IT | — |
| Camille Nicole | ProjectManager | Engineering | PM |
| Floris Kregel | TeamLead | Engineering | Member |
| Ni Kang | Employee | Engineering | Member |
| Mateo Rossi *(new seed)* | Employee | Marketing | — |

## Validation scenarios

Expected outcomes reference spec acceptance scenarios (US#) and requirements (FR-#).

### 1. Upload & My Documents (US1, P1)
1. As **Ni Kang**, `/documents` → Upload: select PDF + PNG, title each, category "Reports", project "ContosoDashboard Development", tags `budget, q3`. → Per-file progress, success messages; both listed with date, size, type, project.
2. Upload the > 25 MB file → "File exceeds the 25 MB limit."; nothing new on disk.
3. Upload the 0-byte file → "File is empty."
4. Upload the renamed `.exe` → "File failed the security scan…"; (EICAR, if available → same).
5. Submit without title/category → validation messages; no upload.
6. Download the PDF → original bytes, original file name.
7. Check `AppData/uploads/4/1/` contains only GUID-named files, one per successful upload (SC-006).

### 2. Sort, filter, search (US2)
1. `/documents` sort by size desc, filter by category and date range → correct subset/order.
2. As **Mateo Rossi**, `/documents/search` term `budget` → **no** results (not permitted).
3. As **Floris Kregel**, search `budget` → Ni Kang's project documents returned.

### 3. Project documents & notifications (US3)
1. As **Camille Nicole**, `/projects/1` → documents section lists Ni Kang's uploads; notifications show "document added".
2. Camille edits title and replaces the file of Ni Kang's project document → saved (Clarification Q1).
3. As **Mateo Rossi**, open `/documents/{id}/file` for a project document directly → 404 (FR-024, SC-005).

### 4. Manage (US4) and role checks
1. As **Ni Kang**, upload a "Personal Files" document (no project). As **Camille** (PM, not TeamLead), `/documents/{id}/file` → 404.
2. As **Floris Kregel** (TeamLead, Engineering), the same personal document is visible and editable (FR-025).
3. As **Ni Kang**, delete with confirmation → removed from all lists; file gone from disk; activity retained.

### 5. Sharing & revoke (US5)
1. As **Ni Kang**, share a personal document with **Mateo Rossi** → Mateo gets a notification and sees it in `/documents/shared`; can download.
2. Share with department **Marketing** → same for Mateo; share with **Engineering** → Camille gains access.
3. Revoke Mateo's direct share (department share still present → access remains); revoke department share → Mateo gets 404 immediately (FR-022a).
4. As **Camille** or **Administrator**, try to share/revoke Ni Kang's document → denied (Clarifications Q2, Q5).

### 6. Preview (US6)
1. Preview PDF and PNG → opens in new tab inline within 3 s.
2. `.docx` shows download only.
3. Response headers still include `X-Frame-Options: DENY` and `X-Content-Type-Options: nosniff` (browser dev tools).

### 7. Tasks & dashboard (US7)
1. As **Ni Kang**, `/tasks` → task "Implement authentication" → Documents → upload → document attached and associated with Project 1.
2. `/` dashboard → "Recent Documents" shows latest 5, newest first; document count card equals number of own uploads.

### 8. Audit & reports (US8)
1. As **Administrator**, `/documents/reports` for today → uploads by type, top uploaders, previews vs downloads match actions above (including deleted documents' history).
2. As **Ni Kang**, navigate to `/documents/reports` → access denied; no nav link visible.

### 9. Regression (Constitution quality gates)
- README security tests 1–4 still pass (auth required, user isolation, IDOR, role-based features).
- Each new authorization rule verified for at least one permitted and one denied role (scenarios 3–5, 8).

## Done criteria

- All scenarios above pass; `dotnet build` clean; no files under `AppData/uploads` without a matching document; no document rows with missing files.
