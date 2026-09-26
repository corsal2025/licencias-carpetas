# Proposal: Ficha de persona por RUT

## Intent

Hoy el historial de una persona está disperso: `GlobalSearchService` lista coincidencias y redirige a `/Index?search=...`. No hay una vista única con todos sus casos (sedes/meses), cambios auditados y trámites F8 / Cambio de Domicilio / Certificados. Éxito: escribir un RUT y ver todo en una página, solo lectura.

## Scope

### In Scope
- Página `/Persona` (Razor Page, solo lectura): entrada de RUT, validación DV con `Domain/RutValidator.NormalizeAndValidate`.
- Secciones: FolderCase (fecha citación, sede, estado, decisión, idoneidad), timeline `CaseAuditLog` (quién/cuándo/qué), `UrgentRequest` (F8), `PersonRequest`/`OutboundAddressChangeRequest` (Cambio Domicilio), `CertificadoRequest`. Todas tienen columna `Rut`.
- Nuevo servicio de consulta `IPersonFileQuery` con parámetro de alcance (`OfficeScope`, hoy "todas las sedes") como punto de enchufe para `sgl-roles-por-sede`.
- Enlaces "Ver ficha" desde resultados de búsqueda global y filas de casos.
- Actualizar `docs/arquitectura.html` (VERSION + CHANGELOG + DATA).

### Out of Scope
- Edición desde la ficha; filtrado por roles/sede (lo hará `sgl-roles-por-sede`).
- Exportación Excel de la ficha.
- Normalizar RUT almacenado (migración de datos).

## Capabilities

### New Capabilities
- `ficha-persona`: consulta y visualización consolidada del historial de una persona por RUT.

### Modified Capabilities
- None

## Approach

- **Transporte del RUT (recomendado)**: formulario POST → valida → guarda RUT normalizado en `TempData` → redirect GET `/Persona` (PRG). El RUT no aparece en URL, historial ni logs de servidor/proxy futuros. Los enlaces desde filas usan POST con form oculto. Alternativa: ruta `/Persona/{rut}` (más simple, compartible/marcable, pero expone PII en logs cuando haya servidor). Tradeoff: PRG pierde "recargar/compartir enlace"; aceptable.
- **Consulta**: comparar con forma canónica sin puntos/guion vía `replace(...)` como hace hoy la búsqueda global. Esto anula índices existentes (`IX_FolderCase_Rut`, `IX_UrgentRequest_Rut`, `IX_CertificadoRequest_Rut`, `IX_PersonRequest_RutComuna`); con volumen actual es aceptable. Sin cambio de esquema. Si hiciera falta: índice por expresión (`CREATE INDEX ... ON T(replace(replace(Rut,'.',''),'-',''))`), aditivo.
- Timeline de auditoría: JOIN `CaseAuditLog` ↔ `FolderCase` por ids de la persona.
- Excluir `DeletedAt IS NOT NULL`.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Dashboard/Pages/Persona/*` | New | Página ficha |
| `Persistence/PersonFileQuery.cs` | New | Consulta consolidada + scope |
| `Persistence/GlobalSearchService.cs` | Modified | Url a ficha |
| `Dashboard/Pages/Index.cshtml` | Modified | Enlace en filas |
| `Program.cs` | Modified | Registro DI |
| `docs/arquitectura.html` | Modified | VERSION/CHANGELOG/DATA |

Estimación: ~550-700 líneas (≈300 producción, ≈300 tests, ≈50 docs). Riesgo de superar 400: Alto → decidir en tasks.

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| RUT almacenado en formatos distintos (padding "0" de `RutValidator`, K minúscula) | Med | Comparar sin ceros a la izquierda y en mayúscula; tests por formato |
| Full scan sin índice | Low | Índice por expresión si crece |
| Filtración PII en URL | Low | PRG + TempData |

## Rollback Plan

Revertir commit(s): páginas y servicio nuevos, sin esquema ni datos. Enlaces vuelven a `/Index?search=`.

## Dependencies

- Ninguna bloqueante; `sgl-roles-por-sede` consumirá `OfficeScope`.

## Success Criteria

- [ ] RUT con/sin puntos/guion encuentra los mismos registros; DV inválido muestra error.
- [ ] Ficha muestra casos de todas las sedes, timeline y trámites vinculados.
- [ ] El RUT no aparece en la URL.
- [ ] `dotnet test` en verde; `arquitectura.html` actualizado.
