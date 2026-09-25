# Proposal: Autoría visible y diagrama de arquitectura vivo

## Intent

- La app no muestra quién la creó. Se agrega un crédito de autoría: "Creado por Raúl Salazar".
- El diagrama `arquitectura.html` está fuera del repo y describe un stack falso (Blazor, Clean Architecture, SQL Server, EF Core). Hay que traerlo al repo y corregirlo para que refleje el stack real.

## Scope

### In Scope
- Pie de página "Desarrollado por Raúl Salazar" en `Dashboard/Pages/Shared/_Layout.cshtml` y en `Login.cshtml`, si ese layout no lo cubre.
- `AUTHORS.md` nuevo y una sección de créditos en `README.md`.
- `docs/arquitectura.html` en este repo, con el contenido reescrito:
  - Stack: .NET 10 Razor Pages, SQLite con repositorios de SQL crudo e importación con ClosedXML.
  - Módulos: Gestión de Licencias, F8 Urgentes y Cambio de Domicilio (EWS contra Exchange on‑prem).
  - Roles, auditoría (`CaseAuditEntry`), estadísticas y respaldos.
  - Enums reales, tomados de `Domain/`: `FolderState` (16 valores, 5 retirados de la selección), `FinalDecision` (9 valores), `FolderSector`, `Office` y `LicenceClass`.
  - Despliegue: hoy un ejecutable local generado con `deploy/publish.ps1`; más adelante, un servidor municipal.
- Se conserva el mecanismo `DATA` (VERSION, CHANGELOG, INFO, VIEWS). Esta versión queda como v0.2.0 y corrige la v0.1.0.
- Regla nueva en `openspec/config.yaml` → `rules.proposal`: "Todo cambio SDD que altere arquitectura, módulos, modelo o despliegue MUST actualizar `docs/arquitectura.html` (VERSION + CHANGELOG)".
- Copiar `mermaid.min.js` al repo en `docs/vendor/`, en lugar de cargarlo desde el CDN.

### Out of Scope
- Cambios en los enums: se guardan como ordinales y no se tocan.
- Cualquier cambio de esquema en SQLite.
- Diagrama multisede: corresponde a `sgl-evolucion-multisede`.

## Capabilities

### New Capabilities
- `authorship-branding`: crédito de autoría visible en la UI y en la documentación.
- `architecture-diagram`: diagrama vivo versionado dentro del repo y regla de actualización.

### Modified Capabilities
- None

## Approach

**Texto de autoría:** "Creado por" o "Desarrollado por" Raúl Salazar, sin "Todos los derechos reservados". El © acredita la autoría, pero si el sistema se hizo como funcionario municipal, la titularidad patrimonial podría ser de la municipalidad. No se afirma titularidad.

**¿Servir el diagrama desde la app?**

| Opción | Pro | Contra |
|---|---|---|
| A. Solo `docs/` (recomendada) | Cero impacto en la app y sin exponer la arquitectura a usuarios | Solo lo ven quienes tienen acceso al repo |
| B. Copiarlo a `wwwroot`, con enlace para admin | Visible en la operación | Dos copias que se desincronizan, y un enlace que exige autorización por rol |
| C. Endpoint admin que sirve `docs/` como archivo embebido | Una sola fuente | Código nuevo con su test; excede el alcance |

Se recomienda A ahora y C en un cambio futuro.

**Sin datos personales:** el diagrama muestra solo nombres de módulos, enums y rutas. No incluye RUT, correos ni nombres de ciudadanos.

## Affected Areas

| Área | Impacto |
|---|---|
| `src/LicenciasCarpetas/Dashboard/Pages/Shared/_Layout.cshtml` | Modified |
| `src/LicenciasCarpetas/Dashboard/Pages/Login.cshtml` | Modified (si aplica) |
| `AUTHORS.md` | New |
| `README.md` | Modified |
| `docs/arquitectura.html`, `docs/vendor/mermaid.min.js` | New |
| `openspec/config.yaml` | Modified |

Líneas cambiadas estimadas: unas 250 escritas a mano. `mermaid.min.js` (~3 MB, vendorizado) queda fuera del conteo. Cabe en un solo PR.

## Risks

| Riesgo | Prob. | Mitigación |
|---|---|---|
| El CDN está bloqueado en la red municipal | Alta | Copiar `mermaid.min.js` al repo |
| La titularidad de la obra es municipal | Media | Redactar "Desarrollado por", sin reservar derechos |
| El diagrama vuelve a quedar desactualizado | Media | Aplicar la regla nueva en config.yaml |
| El pie de página rompe el layout o los tests de UI | Baja | Ejecutar `dotnet test` |

## Rollback Plan

Revertir el commit con `git revert`. No hay cambios de esquema ni de datos, así que el rollback no afecta a SQLite ni a los libros Excel.

## Dependencies

- Ninguna. El `arquitectura.html` externo es solo la base de partida.

## Success Criteria

- [ ] El pie de página con la autoría se ve en todas las páginas y en el login.
- [ ] `docs/arquitectura.html` abre sin conexión y no menciona Blazor, SQL Server ni EF Core.
- [ ] Los enums del diagrama coinciden con los de `Domain/`.
- [ ] La regla de actualización está en `openspec/config.yaml`.
- [ ] `dotnet test` pasa.
