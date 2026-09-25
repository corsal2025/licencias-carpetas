# Continuar aquí (actualizado 2026-09-25)

## Orden acordado
1. **sgl-kpi-dashboard-comparativo** ← EN CURSO (propuesta lista)
2. sgl-ficha-persona (propuesta lista, en pausa)
3. Servidor municipal + API para integrar otras aplicaciones (sin planificar)
4. sgl-roles-por-sede (propuesta, specs, diseño y tareas listos; en pausa hasta que haya más usuarios)

## Preguntas pendientes para el dashboard
1. "PARA DENEGAR": ¿cuenta como pendiente o denegada? (recomendado: pendiente)
2. ¿Guardar desde ahora la fecha exacta de decisión? (recomendado: sí; cambio chico de BD)
3. Tiempo citación→decisión: ¿días hábiles o corridos? (recomendado: hábiles)
4. ¿Entrega en 3 partes? (recomendado: sí — cálculos, pantalla, Excel+docs)

Con las respuestas: sdd-spec + sdd-design → sdd-tasks → sdd-apply (entrega 1).

## Pendientes detectados
- `Dashboard/Pages/Estadisticas.cshtml` carga Chart.js desde CDN: puede fallar sin internet en la red municipal.
- Modo SDD: interactivo, artefactos openspec, entregas ask-on-risk, commits directos a main.
- Cada cambio debe actualizar `docs/arquitectura.html` (VERSION, DATA, CHANGELOG).
