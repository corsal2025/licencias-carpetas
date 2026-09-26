# Archive Report: sgl-evolucion-multisede (exploración)

**Date**: 2026-09-26

Los cambios recomendados por esta exploración quedaron aplicados y archivados:
`sgl-branding-y-diagrama`, `sgl-ficha-persona`, `sgl-kpi-dashboard-comparativo`, `sgl-roles-por-sede`.

## Pendientes que la exploración identificó y que NO tienen cambio propuesto
- **Local → servidor municipal**: la persistencia usa SQL específico de SQLite; mover a un motor de
  servidor implica reescribirla. Mientras tanto, el despliegue Docker/TrueNAS (`TRUENAS_DEPLOY.md`)
  permite servir la misma app+SQLite desde un servidor.
- **PII en texto plano** (RUT, nombre, correo, celular) en SQLite y en los respaldos.
- **Dependencia de EWS/Exchange on-prem** para Cambio de Domicilio.
