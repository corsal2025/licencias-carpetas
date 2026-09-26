# Spec: ficha-persona

## ADDED Requirements

### Requirement: Consulta por RUT con validación
El sistema MUST aceptar un RUT con o sin puntos/guion y con K mayúscula o minúscula, validar su dígito verificador con `RutValidator.NormalizeAndValidate` y rechazar (con mensaje) un RUT inválido sin consultar la base.

#### Scenario: RUT en distintos formatos
- GIVEN un caso guardado con RUT `09.876.543-3`
- WHEN el operador consulta `9876543-3`, `9.876.543-3` o `098765433`
- THEN la ficha muestra el mismo caso

#### Scenario: DV inválido
- GIVEN el operador ingresa `12.345.678-0`
- WHEN envía el formulario
- THEN ve "RUT inválido" y no se muestra ficha

### Requirement: Contenido consolidado
La ficha MUST mostrar, para el RUT, en solo lectura:
- Casos `FolderCase` no eliminados (`DeletedAt IS NULL`) de todas las sedes del alcance, ordenados por fecha de citación descendente.
- Historial `CaseAuditLog` de esos casos (quién, cuándo, campo, antes → después), más reciente primero.
- Solicitudes F8 (`UrgentRequest`), Cambio de Domicilio recibidas (`PersonRequest`) y solicitadas (`OutboundAddressChangeRequest`), y Certificados (`CertificadoRequest`).
Una tabla inexistente (módulo sin inicializar) MUST tratarse como lista vacía.

#### Scenario: Persona con trámites en varios módulos
- GIVEN un RUT con 2 casos en sedes distintas, 1 F8 y 1 certificado
- WHEN se consulta la ficha
- THEN aparecen los 2 casos con su sede, el F8 y el certificado

#### Scenario: Caso en papelera
- GIVEN un caso del RUT con `DeletedAt` informado
- THEN no aparece en la ficha

### Requirement: Alcance por sede
`IPersonFileQuery` MUST recibir un `OfficeScope`. Con `AllowedOffices` nulo se consultan todas las sedes; con una lista, solo los casos (y su auditoría) de esas sedes.

#### Scenario: Alcance restringido
- GIVEN casos del RUT en Placilla y Av. Argentina
- WHEN se consulta con alcance {Placilla}
- THEN solo aparece el caso de Placilla

### Requirement: El RUT no viaja en la URL
La consulta MUST enviarse por POST y mostrarse tras un redirect GET (PRG) con el RUT en `TempData`. Los enlaces "Ver ficha" (búsqueda global) MUST enviar un POST.

#### Scenario: PRG
- WHEN el operador envía un RUT válido
- THEN la respuesta es un redirect a `/Persona` sin el RUT en la URL

### Requirement: Búsqueda global corregida
`GlobalSearchService` MUST consultar las columnas reales de `PersonRequest` (`FullName`, `Rut`) y `OutboundAddressChangeRequest` (`FullName`, `Rut`, `DestinationComuna`), y MUST incluir `CertificadoRequest`.

## Impacto Excel
Ninguno: la ficha no exporta ni importa.
