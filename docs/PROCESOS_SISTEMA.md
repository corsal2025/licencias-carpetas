# Manual Técnico y Operativo de Procesos del Sistema
## Sistema de Gestión de Licencias, Carpetas y Urgencias F8

---

## 1. Arquitectura General y Resumen Ejecutivo

El sistema **Licencias & Carpetas** es una solución enterprise diseñada para la **Dirección de Tránsito Municipal**, orientada a digitalizar, agilizar y auditar el ciclo de vida de los expedientes de licencias de conducir, solicitudes urgentes de archivo (F8) y trámites intermunicipales de cambio de domicilio.

```mermaid
flowchart TD
    subgraph CapaPresentacion["CAPA DE PRESENTACIÓN (UI / Razor Pages)"]
        UI_Casos["🪪 Gestión de Casos (/Index)<br/>Tabla Fija, Drawer, Observaciones"]
        UI_F8["🚨 F8 Urgentes (/F8/Index)<br/>Búsqueda, Asignación, Auditoría"]
        UI_CD_Rec["📥 Cambio Domicilio Recibidos<br/>EWS, Certificados"]
        UI_CD_Sol["📤 Cambio Domicilio Solicitados<br/>Envío a otras Comunas, SLA"]
        UI_Estad["📈 Estadísticas & KPIs<br/>Gráficos Nativos Offline"]
        UI_Admin["👥 Usuarios, Papelera & Admin"]
    end

    subgraph CapaAplicacion["CAPA DE APLICACIÓN Y LÓGICA DE NEGOCIO"]
        Srv_AutoSave["Autoguardado Asíncrono<br/>(Debounce + AbortController)"]
        Srv_Guard["UrgentRequestGuard<br/>(Semáforo de Concurrencia)"]
        Srv_EWS["EwsClientService<br/>(Extracción de Correos)"]
        Srv_Importer["WorkbookSanitizer & Importer<br/>(ClosedXML Sanitizado)"]
        Srv_Audit["AuditLogger & BackupService<br/>(Integridad SQLite)"]
    end

    subgraph CapaPersistencia["CAPA DE DATOS Y PERSISTENCIA (Fuente de Verdad)"]
        DB[("SQLite WAL (carpetas.db)<br/>Casos, Solicitudes, Auditoría, Users")]
        BackupStorage[("Directorio de Respaldos<br/>Rotación de Backups Verificados")]
        MatrizExcel[("Planilla Excel Externa / Drive<br/>(Sincronización F8)")]
    end

    UI_Casos --> Srv_AutoSave
    Srv_AutoSave --> DB
    UI_Casos -.->|Estado: No Existe Carpeta| Srv_Guard
    Srv_Guard --> DB
    UI_F8 <--> DB
    UI_F8 <--> MatrizExcel
    UI_CD_Rec <--> Srv_EWS
    UI_CD_Sol <--> DB
    Srv_Importer --> DB
    DB --> Srv_Audit
    Srv_Audit --> BackupStorage
```

---

## 2. Proceso 1: Gestión de Casos y Agendas Diarias (Módulo Casos)

### Descripción del Proceso
Permite a los operadores gestionar las citaciones diarias de licencias de conducir. Cada fila representa a un postulante/contribuyente citado en una fecha y oficina determinada.

```mermaid
sequenceDiagram
    autonumber
    actor Operador as 👤 Operador de Tránsito
    participant UI as 🖥️ Navegador (Tabla / Drawer)
    participant JS as ⚡ Frontend Script (Index.cshtml)
    participant Server as ⚙️ Servidor (IndexModel.OnPostSave)
    participant Guard as 🛡️ UrgentRequestGuard
    participant DB as 🗄️ SQLite (FolderCaseRepository)

    Operador->>UI: Edita campo (Estado, Decisión, Folio, Licencias, etc.)
    UI->>JS: Evento change / input
    JS->>JS: Marca fila visualmente (.fila-sin-guardar, borde naranja)
    JS->>JS: Debounce 400ms (cancela petición previa en vuelo)
    JS->>Server: POST Asíncrono (Fetch XMLHttpRequest) con formulario completo
    
    alt Estado es 'No Existe Carpeta'
        Server->>Guard: Adquiere SemaphoreSlim
        Server->>DB: Busca si ya existe en F8 (FindByRut)
        alt No existe en F8
            Server->>DB: Inserta solicitud urgente F8 automática
        end
        Server->>Guard: Libera SemaphoreSlim
    end

    Server->>DB: UpdateEditableFields & AuditLog (concurrencia optimista)
    Server-->>JS: Respuesta JSON { ok: true, isError: false }
    JS->>UI: Remueve borde naranja (.fila-sin-guardar)
    JS->>UI: Confirma guardado silencioso sin recargar ni saltar pantalla
```

### Paso a Paso Operativo:
1. **Filtro y Navegación**: El usuario selecciona Oficina, Año, Mes o busca por RUT/Nombre. La vista mantiene el estado de paginación (`size=100`, `p=1`).
2. **Edición en Tabla o Drawer**:
   - Se puede editar inline directamente en la celda correspondiente.
   - O abrir el **Drawer Lateral Master-Detail** haciendo clic en la fila para ver toda la información concentrada.
3. **Observaciones Flotantes**:
   - Al pulsar el ícono 📝/🗒️ junto al RUT, se despliega un panel fijo flotante (`position: fixed`) que no desborda la tabla y enfoca el texto para edición rápida.
4. **Autoguardado**:
   - Cada cambio activa el autoguardado asíncrono debounced (400 ms).
   - Si el operador presiona Enter, el envío se intercepta para evitar recargas de página y mantener la posición exacta de scroll (`scroll-preserve.js`).

---

## 3. Proceso 2: Flujo de Casos F8 Urgentes (Búsqueda de Carpetas)

### Descripción del Proceso
Cuando una carpeta física no es encontrada en el archivo municipal durante la citación de un usuario, el caso pasa a estado `NoExisteCarpeta` y se activa el flujo urgente F8.

```mermaid
stateDiagram-v2
    [*] --> Pendiente: Registro Manual o Derivación desde Casos
    Pendiente --> EnBusqueda: Operador asigna/inicia rastreo en Archivo
    EnBusqueda --> Encontrada: Carpeta física localizada en bodega
    EnBusqueda --> NoEncontrada: Agotada búsqueda en todas las estanterías
    
    Encontrada --> Resuelta: Entregada a mesón / subida al sistema
    NoEncontrada --> OficioEmitido: Se confecciona Oficio o F8 especial
    OficioEmitido --> Resuelta: Regularizada con CONASET / Archivo Central
    
    Resuelta --> [*]
```

```mermaid
flowchart LR
    A["🪪 Caso en Agenda<br/>'No Existe Carpeta'"] --> B{"¿RUT válido?"}
    B -- Sí --> C["🛡️ UrgentRequestGuard<br/>Control de Concurrencia"]
    C --> D["🚨 Inserción en F8 Urgentes<br/>(Estado: Pendiente)"]
    D --> E["📊 Sincronización Matriz Excel<br/>(Copia en Google Drive)"]
    D --> F["🏢 Vista Sector F8<br/>Impresión de Hoja de Ruta para Bodega"]
    F --> G["✅ Actualización de Estado<br/>y Trazabilidad en Auditoría"]
```

### Paso a Paso Operativo:
1. **Detección**: Al marcar `No Existe Carpeta` en la agenda diaria, el backend crea de forma atómica y segura (bajo semáforo) la solicitud en F8.
2. **Asignación & Búsqueda**: El personal de archivo consulta el módulo `/F8/Index` o imprime `/F8/SectorF8` para acudir a las bodegas físicas.
3. **Resolución**: Al encontrar la carpeta, se actualiza el estado a `Encontrada` o `Resuelta`, registrando fecha de última carpeta y código F8 correspondiente.

---

## 4. Proceso 3: Cambio de Domicilio (Intercomunal)

### Descripción del Proceso
Gestiona las solicitudes de antecedentes de conductores que se trasladaron desde o hacia otra comuna.

```mermaid
flowchart TD
    subgraph Flujo1["📥 FLUJO 1: RECEPCIÓN DESDE OTRAS COMUNAS"]
        MailIn["✉️ Correo recibido de otra comuna (Exchange / EWS)"]
        Extractor["🔍 PersonDataExtractor (Regex de RUT, Nombres, Comuna)"]
        ListRec["📋 Lista de Pendientes (/CambioDomicilio/Index)"]
        Cert["📄 Emisión de Certificado de Antecedentes (/Certificado)"]
        Enviado["📤 Envío de respuesta con carpeta digitalizada"]
    end

    subgraph Flujo2["📤 FLUJO 2: SOLICITUD A OTRAS COMUNAS"]
        Ingreso["➕ Ingreso de Solicitud (/CambioDomicilio/Solicitar)"]
        Directorio["🏘️ Directorio de Comunas (Comunas.cshtml)"]
        MailOut["📨 Generación de Correo Formal a DTT de destino"]
        SLA{"⏱️ Control de SLA<br/>(Días Hábiles)"}
        Verde["🟢 Normal (> 7 días)"]
        Amarillo["🟡 Advertencia (4 a 7 días)"]
        Rojo["🔴 Crítico / Vencido (<= 3 días)"]
    end

    MailIn --> Extractor --> ListRec --> Cert --> Enviado
    Ingreso --> Directorio --> MailOut --> SLA
    SLA --> Verde
    SLA --> Amarillo
    SLA --> Rojo
```

### Paso a Paso Operativo:
1. **Recepción Automática**: El servicio en segundo plano conecta vía EWS y extrae automáticamente los pedidos entrantes desde el buzón institucional.
2. **Emisión de Certificados**: Se revisa el folio y se descarga el certificado PDF oficial para adjuntar a la respuesta.
3. **Solicitudes Salientes**: El módulo `Solicitar` cuenta con un semáforo visual de SLA basado en días hábiles chilenos (excluyendo feriados y fines de semana), alertando casos próximos a vencer.

---

## 5. Proceso 4: Importación Masiva & Sanitización de Planillas Excel

### Descripción del Proceso
Ingesta planillas Excel históricas o matrices mensuales sin riesgo de corrupción ni bloqueos por validaciones de datos complejas.

```mermaid
sequenceDiagram
    autonumber
    actor Admin as 👤 Administrador / Operador
    participant Page as 🖥️ Pantalla Importar (/Importar)
    participant Sanitizer as 🧹 WorkbookSanitizer
    participant Detector as 🔎 HeaderDetector
    participant Mapper as 📐 AgendaRowMapper
    participant DB as 🗄️ Base de Datos SQLite

    Admin->>Page: Sube archivo .xlsx (Agenda / Citas / Histórico)
    Page->>Sanitizer: CreateCopyWithoutDataValidations()
    Note over Sanitizer: Crea copia temporal y remueve reglas de validación de ClosedXML
    Page->>Detector: Analiza encabezados de cada hoja (no por nombre)
    Detector->>Mapper: Mapea columnas (RUT, Nombre, Citación, Oficina, etc.)
    Mapper->>Mapper: Normaliza RUT (agrega 0 inicial si < 10M, calcula DV)
    Mapper->>DB: Inicia Transacción SQLite
    Mapper->>DB: Upsert por Lote (Oficina + Fecha Citación + RUT)
    DB-->>Page: Confirma importación con resumen de filas insertadas/actualizadas
    Page-->>Admin: Muestra estadísticas de importación y posibles advertencias
```

---

## 6. Proceso 5: Auditoría, Papelera & Respaldos Automáticos

### Descripción del Proceso
Garantiza la trazabilidad total de modificaciones y la integridad de los datos ante cortes de energía o errores de operación.

```mermaid
flowchart TD
    subgraph EventoModificacion["📝 CUALQUIER OPERACIÓN DE EDICIÓN O ELIMINACIÓN"]
        Edit["Edición de campo en Casos o F8"]
        Delete["Eliminación de un Caso"]
    end

    subgraph Auditoria["🛡️ REGISTRO DE AUDITORÍA"]
        Diff["Calcula Diff: Valor Anterior vs Valor Nuevo"]
        Log["Inserta en tabla audit_log con Usuario, Timestamp e IP"]
    end

    subgraph Papelera["🗑️ PAPELERA (SOFT DELETE)"]
        Soft["Marca deleted_at = UTC NOW (No borra el registro)"]
        Restore["Permite Restaurar con 1 Clic desde /Papelera"]
    end

    subgraph Respaldo["💾 BACKUP AUTOMÁTICO ROTATIVO"]
        Trigger["Disparo al iniciar app o antes de mutación crítica"]
        Verify["PRAGMA integrity_check en base origen"]
        Copy["File.Copy a /backups con timestamp"]
        Rotate["Conserva los 10 respaldos verificados más recientes"]
    end

    Edit --> Diff --> Log
    Delete --> Soft --> Log
    Trigger --> Verify --> Copy --> Rotate
```

---

## 7. Proceso 6: Búsqueda Global Unificada (Ctrl+K)

### Descripción del Proceso
Permite a los operadores localizar inmediatamente a cualquier persona en cualquiera de los módulos sin tener que cambiar de pantalla manualmente.

```mermaid
sequenceDiagram
    autonumber
    actor Operador as 👤 Operador
    participant Modal as 🔍 Modal Global Search (Ctrl+K)
    participant API as ⚙️ Endpoint /api/search
    participant DB as 🗄️ SQLite Database

    Operador->>Modal: Presiona Ctrl+K y escribe RUT (ej: 18.785.387-7) o Nombre
    Modal->>API: GET /api/search?q=187853877 (Debounced 250ms)
    API->>DB: Consulta simultánea en Casos, F8 Urgentes y Cambio Domicilio
    DB-->>API: Resultados ponderados por coincidencia
    API-->>Modal: JSON agrupado por Módulo con Estado y Enlace directo
    Modal->>Operador: Renderiza lista de resultados con badges SLA
    Operador->>Modal: Clic en resultado
    Modal->>Operador: Navega directamente a la fila/caso seleccionado
```

---

## 8. Resumen de Estándares de Diseño y UI

| Elemento | Token / Clase | Estándar Visual |
| :--- | :--- | :--- |
| **Fondo General** | `--color-bg` | `#f8fafc` (Slate 50, limpio y moderno) |
| **Color Institucional** | `--color-primary` | `#0f2e4a` (Azul profundo municipal) |
| **Acento Interactivo** | `--color-accent` | `#2563eb` (Cobalto / Índigo accesible) |
| **Estado Subida/OK** | `--color-uploaded` | `#059669` / `#ecfdf5` (Esmeralda suave) |
| **Estado Pendiente** | `--color-pending` | `#d97706` / `#fffbeb` (Ámbar cálido) |
| **Estado Revisión** | `--color-review` | `#e11d48` / `#fff1f2` (Carmesí controlado) |
| **Copiado de RUT** | `_Layout.js` | Sin puntos, con guión, con cero inicial si `< 10M` (ej: `09868496-4`) |
| **Scroll de Pantalla** | `scroll-preserve.js` | Preservación exacta de posición X/Y tras recargas y acciones |
