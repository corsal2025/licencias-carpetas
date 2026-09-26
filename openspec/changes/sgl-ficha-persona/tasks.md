# Tasks: sgl-ficha-persona

## 1. Consulta (TDD)
- [x] 1.1 Tests `PersonFileQueryTests`: formatos de RUT, papelera excluida, alcance por sede, auditoría, módulos F8/CD/Certificados, tablas ausentes
- [x] 1.2 `Persistence/PersonFileQuery.cs` + `PersonFileScope` + modelos de la ficha
- [x] 1.3 Registro DI en `Program.cs`

## 2. Página
- [x] 2.1 Tests de integración: POST inválido → mensaje; POST válido → redirect sin RUT en URL; GET muestra la ficha
- [x] 2.2 `Dashboard/Pages/Persona.cshtml(.cs)` (PRG + TempData)
- [x] 2.3 Enlace "Ficha de persona" en el menú lateral

## 3. Búsqueda global
- [x] 3.1 Test: encuentra PersonRequest, OutboundAddressChangeRequest y CertificadoRequest
- [x] 3.2 Corregir columnas en `GlobalSearchService` y agregar Certificados
- [x] 3.3 Botón "Ver ficha" en resultados (POST vía formulario oculto del layout)

## 4. Documentación y verificación
- [x] 4.1 `docs/arquitectura.html`: VERSION v0.3.0 + CHANGELOG + DATA
- [x] 4.2 `dotnet build` y `dotnet test`
