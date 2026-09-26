# Tasks: Autoría visible y diagrama de arquitectura vivo

## 1. Autoría en la UI (TDD)
- [x] 1.1 Test: `/Inicio` autenticado contiene "Desarrollado por Raúl Salazar" (reutiliza `CambioDomicilioWebAppFactory`)
- [x] 1.2 Test: `/Login` (sin autenticar) contiene "Desarrollado por Raúl Salazar"
- [x] 1.3 Agregar el pie de página a `_Layout.cshtml`
- [x] 1.4 Agregar el pie de página a `Login.cshtml`
- [x] 1.5 Confirmar que ambos tests pasan (GREEN)

## 2. Documentación de créditos
- [x] 2.1 Crear `AUTHORS.md`
- [x] 2.2 Agregar sección de créditos en `README.md`

## 3. Regla SDD de actualización del diagrama
- [x] 3.1 Agregar regla en `openspec/config.yaml` → `rules.proposal`

## 4. Diagrama de arquitectura vivo
- [x] 4.1 Copiar `arquitectura.html` a `docs/arquitectura.html`
- [x] 4.2 Vendorizar `mermaid.min.js` en `docs/vendor/mermaid.min.js`
- [x] 4.3 Referenciar el script vendorizado con ruta relativa (sin CDN)
- [x] 4.4 Reescribir el bloque `DATA` (VERSION, CHANGELOG, INFO, VIEWS) con el stack real:
      .NET 10 Razor Pages, SQLite + repositorios SQL crudo, ClosedXML, módulos Gestión de
      Licencias / F8 Urgentes / Cambio de Domicilio (EWS), roles, auditoría (`CaseAuditEntry`),
      estadísticas, respaldos, enums reales de `Domain/`
- [x] 4.5 VERSION = v0.2.0; CHANGELOG con entradas v0.1.0 y v0.2.0 (2026-09-25)
- [x] 4.6 Conservar el filtro de `click` bindings (regex sobre `INFO` keys)
- [x] 4.7 Footer del diagrama: "Desarrollado por Raúl Salazar" (sin "Todos los derechos reservados")
- [x] 4.8 Verificar que no hay datos personales (RUT, correos, nombres de ciudadanos)

## 5. Verificación
- [x] 5.1 `dotnet build`
- [x] 5.2 `dotnet test`
