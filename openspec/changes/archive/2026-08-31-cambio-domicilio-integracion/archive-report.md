# Archive Report: cambio-domicilio-integracion

**Date**: 2026-08-31  
**Status**: `ARCHIVED` — 0 CRITICAL, 0 WARNING, 520/520 tests passing  
**Verification**: Pass — ready for production deployment

---

## Change Summary

Integrated the "Cambio de Domicilio" module from the sibling application (`outlook-comuna-router`) into `licencias-carpetas` as a first-class feature. The fold-in consolidates two separate applications, two databases (`router.db` and `carpetas.db`), and two login systems into a single unified platform while maintaining strict backward compatibility with existing modules.

### Scope Completed

**In Scope (All Delivered):**
- Port `Domain`, `Persistence`, `Ews`, `Mail`, `Extraction`, `Routing`, `Directories`, `Notifications`, `Reporting`, `Statistics` modules
- Dashboard pages: `Index`, `Discarded`, `Comunas`, `Estadisticas`, `Certificado`, `Sector`
- Synchronous "Sincronizar ahora" sync cycle handler (POST to `IndexModel.OnPostSyncNowAsync`)
- Anti-overlap semaphore (`SemaphoreSlim cycleGuard`) to prevent concurrent cycles
- Internal navigation link (replacing external `https://localhost:5001`)
- Schema coexistence: new tables in existing `carpetas.db` with zero collision
- Added: Outbound "Solicitar" workflow (`OutboundAddressChangeRequest`, `OutboundAddressChangeAttachment`) with reverse-flow email and matrix loading

**Out of Scope (Deferred):**
- EWS protocol changes or logic refactoring
- User migration from sibling `outlook-comuna-router`
- Background worker or polling loop

---

## Specifications Merged into Main Specs

Two new domain specs created in `openspec/specs/`:

| Domain | Action | Requirements | Location |
|--------|--------|--------------|----------|
| `cambio-domicilio-routing` | Created | 10 core requirements (Manual Sync, Overlap Prevention, Request Extraction, Comuna Resolution, Case Creation, Cycle Reporting, Configuration, Non-Goals, Routing Directory One-Directionality) | `openspec/specs/cambio-domicilio-routing/spec.md` |
| `cambio-domicilio-dashboard` | Created | 3 core requirements (Access Control via `CambioDomicilioAccess` policy + `mod:cambio-domicilio` claim, Schema Coexistence in `carpetas.db`, Internal Navigation) | `openspec/specs/cambio-domicilio-dashboard/spec.md` |

**Note on Specs:** These are new domain specifications, not deltas to existing specs. They are added to the main specs directory to reflect the architecture decision that `ComunaContact` (editable from Comunas screens, populated with the 513 official comuna emails per commit 01dc110) is the single source of truth for routing data, and the routing CSV is a **read-only projection** derived one-directionally from that table. The routing module NEVER writes to `ComunaContact` and does NOT fabricate comuna rows.

---

## Database Schema Changes

The following tables are added to the existing `carpetas.db` via the standard `EnsureSchema()` pattern in `Program.cs`, after `DatabaseBackup` and existing module schemas:

### Core Tables (Incoming Flow)
1. **`PersonRequest`** — Extracts from emails, validated RUT, assigned to a pending folder-case
2. **`DeletedSourceMessage`** — EWS message IDs processed in past cycles (idempotence)

### Supporting Tables
3. **`DiscardedEmail`** — Emails flagged for manual review (invalid RUT, ambiguous comuna, etc.)

### Outbound Flow Tables (Phase 7 Addition)
4. **`OutboundAddressChangeRequest`** — Reverse workflow: asking other comunas for folder cases
5. **`OutboundAddressChangeAttachment`** — File attachments linked to outbound requests

**Collision Verification**: Existing tables are `FolderCase`, `DailyCounter`, `ComunaContact`, `DashboardUser`, `UrgentRequest`, `UrgentRequestFlag`, `UrgentImportRun`. None of the new table names collide. All tables are created in order within `EnsureSchemas()` after the backup.

**Naming Strategy**: Tables are **not prefixed**. Names are already unique. The SQL porteded from the sibling remains untouched, minimizing risk and code duplication.

---

## Archive Verification

**Pre-Archive Checks (All Passed):**
- [x] Main specs synced to `openspec/specs/` (routing + dashboard domains)
- [x] Change folder structure complete with all SDD artifacts
- [x] Verification status: 0 CRITICAL / 0 WARNING / 2 SUGGESTION (non-blocking)
- [x] Build clean: `dotnet build -c Release` — zero errors, zero warnings
- [x] Test suite: 520/520 passing (up from 511, +9 new tests for remediations)
- [x] All 7 implementation phases marked complete with [x]
- [x] All verification findings (C1–C2–W1–W6) remediated in code and tests

**Archive Contents Verified:**

```
openspec/changes/cambio-domicilio-integracion/
├── proposal.md              ✅ Intent, scope, affected areas
├── explore.md               ✅ (optional) Initial exploration
├── design.md                ✅ Technical approach, architecture decisions, DI wiring
├── specs/
│   ├── cambio-domicilio-routing/
│   │   └── spec.md          ✅ (MERGED to openspec/specs/)
│   └── cambio-domicilio-dashboard/
│       └── spec.md          ✅ (MERGED to openspec/specs/)
├── tasks.md                 ✅ 7 phases, all [x] complete, review workload forecast
└── verify-report.md         ✅ Pass: 0 CRITICAL, 0 WARNING, 520/520 tests
```

**Moved to Archive Path:**

```
openspec/changes/archive/2026-08-31-cambio-domicilio-integracion/
└── [All above artifacts]
```

---

## Artifacts

- **Main Specs Created**:
  - `openspec/specs/cambio-domicilio-routing/spec.md`
  - `openspec/specs/cambio-domicilio-dashboard/spec.md`
- **Archive Location**: `openspec/changes/archive/2026-08-31-cambio-domicilio-integracion/`
- **Archive Report**: This document

---

## Implementation Highlights

### Phase 1–2: Domain & Data (Repositories)
Ported `PersonRequest`, `DiscardedEmail`, `ComunaRoutingEntry` (renamed from `ComunaContact` in the CSV layer to avoid collision with the editable `ComunaContact` table). Repositories created with `EnsureSchema()` for both incoming (`PersonRequest`, `DeletedSourceMessage`) and outbound (`OutboundAddressChangeRequest`) workflows.

### Phase 3: Extraction, Routing, Directories
RUT validator, person data extractor, sync service (no longer a `BackgroundService`; now a plain singleton with `RunCycleAsync` as public method). Anti-overlap semaphore maintained. Routing directory is read-only projection from `ComunaContact` table.

### Phase 4: EWS, Notifications, Reporting, Statistics
EWS client, email reader/sender, notification channels (email + Windows toast), CSV report writer, statistics service. All ported from sibling with minimal changes.

### Phase 5: Dashboard & Host Wiring
Six pages added (`Index`, `Discarded`, `Comunas`, `Estadisticas`, `Certificado`, `Sector`), all gated by `CambioDomicilioAccess` policy. DI registration in `Program.cs` with all services and `EnsureSchema()` calls. Navigation link in `_Layout.cshtml` replaced with internal `asp-page` link, visible only to authorized users.

### Phase 6: E2E Smoke Tests
Access control tests, startup tests (app starts without `CambioDomicilio:` section), sync cycle tests with fake dependencies.

### Phase 7: Solicitar (Outbound Flow)
Added post-verification: reverse workflow for requesting address changes from other comunas. Tables `OutboundAddressChangeRequest` and `OutboundAddressChangeAttachment` created, sync service loads optional Excel matrix, pages added with authorization.

---

## Verification Findings Resolution

| Finding | Status | Evidence |
|---------|--------|----------|
| **C1** — CSV report path resolution | Closed | `appsettings.json:31` publishes `ReportCsvPath`, resolved against `AppContext.BaseDirectory` in `Program.cs:99-100` via `CambioDomicilioPathResolver` |
| **C2** — ComunaContact coupling | Closed | `SeedRepositoryFromDefault()` removed; `ComunaDirectory.EnsureSeed` unidirectional (reads only); specs updated (routing spec: "One-Directional Projection of ComunaContact"); tests added |
| **W1** — SyncService inheritance | Closed | `CambioDomicilioSyncService` is plain singleton (no `BackgroundService`), `RunCycleAsync` public |
| **W2** — UI sync result counts | Closed | `CambioDomicilioSyncResult(Outcome, Creados, Descartados, ParaRevision)` returned and displayed in `Index.cshtml.cs:310-312` |
| **W3** — Empty directory outcome | Closed | New outcome `SkippedNoDirectory` with operator-facing error message in `Index.cshtml.cs:313-315` |
| **W4** — Path resolution centralized | Closed | `CambioDomicilioPathResolver` in `Program.cs:97-102` resolves three paths once at startup |
| **W5** — Solicitar phase documented | Closed | `design.md:191-209` and `tasks.md` Phase 7; schema coexistence note added |
| **W6** — Nav visibility test | Closed | `CambioDomicilioAccessTests` confirms nav entry hidden from unauthorized users |

---

## SDD Cycle Complete

This change has successfully completed the full SDD pipeline:

1. **Proposal** ✅ — Intent, scope, affected areas, rollback plan defined
2. **Specification** ✅ — Two domain specs written and merged into main specs
3. **Design** ✅ — Architecture decisions, DI wiring, schema coexistence verified
4. **Tasks** ✅ — 7 phases, 50+ work items (all checked [x])
5. **Implementation** ✅ — 35+ new files, 3 modified files, 4600+ changed lines, strict TDD
6. **Verification** ✅ — 0 CRITICAL, 0 WARNING, 520/520 tests, build clean
7. **Archive** ✅ — Delta specs merged, change folder moved, audit trail persisted

**Ready for**: Production deployment, merging to main branch, continued operation in parallel with sibling `outlook-comuna-router` (independent, no migration required).

---

## Next Steps

None. The change is complete and archived. The Cambio de Domicilio feature is integrated into licencias-carpetas and ready for live deployment. Sibling `outlook-comuna-router` remains operational with no changes.
