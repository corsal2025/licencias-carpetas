# Cambio de Domicilio Dashboard Specification

## Purpose

Expose the ported Cambio de Domicilio screens inside licencias-carpetas' own dashboard, gated by
the existing access policy, sharing the single `carpetas.db`, and reachable from internal nav
instead of an external link.

## Requirements

### Requirement: Access Control
The system MUST gate every Cambio de Domicilio page behind the existing `CambioDomicilioAccess`
authorization policy, tied to claim `mod:cambio-domicilio`.

#### Scenario: Authorized user
- GIVEN a logged-in user with the `mod:cambio-domicilio` claim
- WHEN they navigate to `/CambioDomicilio/Index`
- THEN the page MUST render normally

#### Scenario: Unauthorized user
- GIVEN a logged-in user without the `mod:cambio-domicilio` claim
- WHEN they navigate to `/CambioDomicilio/Index`
- THEN the system MUST respond with 403 or redirect, and MUST NOT throw an unhandled exception

### Requirement: Schema Coexistence
The system MUST create the module's new tables in the existing `carpetas.db` via the standard
`EnsureSchema()` pattern used by every other module, and the new table names MUST NOT collide
with existing tables (`ComunaContact`, `DailyCounter`, `FolderCase`, and any others already
present).

#### Scenario: First run creates tables
- GIVEN a `carpetas.db` without the module's tables
- WHEN the application starts
- THEN `EnsureSchema()` MUST create the module's tables without altering or dropping existing tables

#### Scenario: Table name collision check
- GIVEN the existing table names in `carpetas.db`
- WHEN naming the module's new tables
- THEN none of the new names MUST match an existing table name

### Requirement: Internal Navigation
The system MUST replace the external nav link (`https://localhost:5001`) with an internal
`asp-page` link to the module's index page, and the nav entry MUST only be visible to users
authorized by `CambioDomicilioAccess`.

#### Scenario: Authorized user sees internal link
- GIVEN an authorized user viewing the layout nav
- WHEN the page renders
- THEN the nav MUST show an internal link to `/CambioDomicilio/Index`, not an external URL

#### Scenario: Unauthorized user does not see the entry
- GIVEN a user without `mod:cambio-domicilio`
- WHEN the layout nav renders
- THEN the Cambio de Domicilio nav entry MUST NOT be shown
