# Cambio de Domicilio Routing Specification

## Purpose

Fold the "Cambio de Domicilio" sync cycle (EWS mailbox scan, extraction, comuna routing, case
lifecycle, confirmation, reporting) into licencias-carpetas, on-demand and synchronous, same
behavior as the sibling app `outlook-comuna-router`, no new background hosting.

## Requirements

### Requirement: Manual Sync Execution
The system MUST expose a "Sincronizar ahora" action that runs the sync cycle synchronously
within the same HTTP POST request that triggered it (no background job, no polling).

#### Scenario: Operator triggers sync
- GIVEN an authorized operator on the Cambio de Domicilio index page
- WHEN they submit "Sincronizar ahora"
- THEN the request blocks until the cycle completes and the page re-renders with updated results

### Requirement: Overlap Prevention
The system MUST prevent two sync cycles from running concurrently, using a semaphore held for
the duration of the cycle.

#### Scenario: Second sync while one is running
- GIVEN a sync cycle already in progress
- WHEN another request triggers "Sincronizar ahora"
- THEN the second request MUST NOT start a new cycle and MUST report that a cycle is already running

### Requirement: Request Extraction
During a sync cycle, the system MUST read unread/pending emails from the configured source
folder, extract one or more person requests per email (name + RUT), and validate each RUT by
its check digit.

#### Scenario: Valid multi-person email
- GIVEN an email in the source folder listing two people with valid RUTs
- WHEN the cycle processes it
- THEN two person requests MUST be extracted, each with a validated RUT

#### Scenario: Invalid RUT check digit
- GIVEN an email containing a RUT that fails checksum validation
- WHEN the cycle processes it
- THEN that person request MUST be discarded/flagged, not created as a Pending case

### Requirement: Comuna Resolution via Directory
The system MUST resolve the requesting comuna using the CSV directory (`Comuna,ContactEmail,Domain`):
exact sender-address match first; if no exact match, fall back to a match on sender domain only
when that domain maps to a single comuna owner.

#### Scenario: Exact address match
- GIVEN a sender address present verbatim in the CSV directory
- WHEN the cycle resolves the comuna
- THEN it MUST use the comuna tied to that exact address

#### Scenario: Domain fallback, single owner
- GIVEN a sender address absent from the CSV but whose domain maps to exactly one comuna
- WHEN the cycle resolves the comuna
- THEN it MUST use that comuna

#### Scenario: Domain fallback, ambiguous owner
- GIVEN a sender domain that maps to more than one comuna in the CSV
- WHEN the cycle resolves the comuna
- THEN the request MUST NOT be auto-resolved and MUST be reported as unresolved

### Requirement: Case Creation and Confirmation
The system MUST create a Pending case per validated, comuna-resolved person request, persisted
in `carpetas.db`, and MUST check the confirmation folder each cycle to mark matching Pending
cases as Uploaded.

#### Scenario: New request creates Pending case
- GIVEN a validated person request with a resolved comuna
- WHEN the cycle runs
- THEN a case MUST be persisted with state Pending

#### Scenario: Confirmation folder marks case Uploaded
- GIVEN a Pending case whose folder now appears in the confirmation folder
- WHEN the cycle checks confirmations
- THEN that case MUST transition to Uploaded

### Requirement: Cycle Reporting
The system MUST write a CSV report per cycle and MUST report accurate success/failure counts
back to the operator in the UI, without silently swallowing per-item errors.

#### Scenario: Cycle with mixed outcomes
- GIVEN a cycle that creates 3 cases, flags 1 for review, and discards 1 unresolved comuna
- WHEN the cycle finishes
- THEN the CSV report and the UI status message MUST both reflect those exact counts
  (`Creados` / `ParaRevision` / `Descartados`)

#### Scenario: No routing directory available
- GIVEN a cycle where the routing directory resolves to zero comunas
- WHEN the cycle runs
- THEN it MUST NOT report success: the outcome MUST be a distinct `SkippedNoDirectory` state with
  an operator-facing error, and no mail MUST be read

### Requirement: Configuration Section
The system MUST read EWS, mailbox, folder names, deadline, SQLite path, directory CSV path,
report path, and notification settings from a `CambioDomicilio:` configuration section, with
EWS/SMTP credentials sourced from User Secrets or environment variables, never committed to
`appsettings.json`.

#### Scenario: Missing CambioDomicilio section
- GIVEN `appsettings.json` has no `CambioDomicilio:` section
- WHEN the rest of licencias-carpetas starts
- THEN the application MUST start normally and other modules MUST be unaffected

### Requirement: Non-Goals
The system MUST NOT alter EWS protocol logic during the port, and MUST NOT decommission or
migrate users from the sibling app `outlook-comuna-router` as part of this change.

#### Scenario: Sibling app still running
- GIVEN the fold-in is deployed
- WHEN an operator opens `outlook-comuna-router` directly
- THEN it MUST still function against its own `router.db`, independent of licencias-carpetas

### Requirement: Routing Directory Is a One-Directional Projection of ComunaContact
`ComunaContact` (the SQLite table, editable from the Comunas screens and populated with the
official comuna emails — see commit 01dc110) is the single editable source of comuna routing
data. The routing `Directories` CSV is a **read-only projection** derived from that table
one-directionally (`ComunaContact` table -> routing CSV). The routing module MUST NOT write to
`ComunaContact` and MUST NOT fabricate comuna rows: if the CSV is missing/empty it is
regenerated from `ComunaContact`, and if `ComunaContact` is also empty the directory stays
absent and the sync cycle reports `SkippedNoDirectory` rather than routing against invented data.

#### Scenario: ComunaContact is never written by the routing module
- GIVEN the existing `ComunaContact` table
- WHEN the Cambio de Domicilio routing module regenerates the routing CSV
- THEN it MUST only read `ComunaContact`; `ComunaContact` behavior and schema MUST remain unchanged

#### Scenario: Empty source produces no directory, not fake data
- GIVEN a missing routing CSV and an empty `ComunaContact` table
- WHEN a sync cycle runs
- THEN no CSV is fabricated and the cycle returns `SkippedNoDirectory` with an operator-facing error
