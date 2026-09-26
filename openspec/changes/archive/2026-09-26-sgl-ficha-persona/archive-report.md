# Archive Report: sgl-ficha-persona

**Date**: 2026-09-26 · **Status**: `ARCHIVED` — tareas completas; `dotnet test` 601/601.

- `/Persona` (PRG + TempData, el RUT no aparece en la URL) y `PersonFileQuery` (RUT canónico).
- `GlobalSearchService`: las ramas de Cambio de Domicilio consultaban columnas inexistentes y el
  `catch` vacío ocultaba el error; corregido y ampliado a Certificados. Botón "Ficha" en Ctrl+K.
- El alcance (`PersonFileScope` en la propuesta) quedó unificado como `Domain/OfficeScope`.
- Spec promovida a `openspec/specs/ficha-persona/spec.md`.
