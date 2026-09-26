# Archive Report: sgl-roles-por-sede

**Date**: 2026-09-26 · **Status**: `ARCHIVED` — 3 slices aplicados; `dotnet test` 601/601; verificación
manual contra copia de la base real (ver "Registro de aplicación" en `tasks.md`).

- Desviaciones del diseño documentadas en `tasks.md` (decorador `ScopedCaseRepository` en lugar de la
  fachada `IScopedCaseStore`, 404 por middleware, migración solo al crear la tabla).
- Spec promovida a `openspec/specs/office-scoped-access/spec.md`.
