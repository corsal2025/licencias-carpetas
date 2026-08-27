# 🚀 Guía de Despliegue en Servidor TrueNAS SCALE / Docker

Esta guía documenta los pasos para desplegar **LicenciasCarpetas** en tu servidor **TrueNAS SCALE** o cualquier servidor con Docker.

---

## 1. Estructura de Archivos en el Servidor
Copia el repositorio o clónalo en un dataset de tu TrueNAS (por ejemplo en `/mnt/tank/apps/licencias-carpetas`):

```
licencias-carpetas/
├── Dockerfile
├── docker-compose.yml
├── data/                       <-- Aquí vivirá carpetas.db persistente
├── exports/                    <-- Aquí se guardan los reportes y respaldos
└── src/
```

---

## 2. Iniciar la Aplicación con Docker Compose

En la terminal de TrueNAS (SSH o Shell Web):
```bash
# 1. Entrar al directorio
cd /mnt/tank/apps/licencias-carpetas

# 2. Construir y levantar el contenedor en segundo plano
docker compose up -d --build

# 3. Ver los logs para comprobar inicio correcto
docker compose logs -f
```

---

## 3. Configurar Dominio y HTTPS (SSL) con Nginx Proxy Manager en TrueNAS

1. En el panel web de TrueNAS, ve a **Apps** e instala **Nginx Proxy Manager** (si no lo tienes instalado).
2. Abre la interfaz de Nginx Proxy Manager (`http://IP-TRUENAS:81`).
3. Ve a **Proxy Hosts** -> **Add Proxy Host**:
   - **Domain Names:** `licencias.tudominio.cl` (o tu subdominio asignado).
   - **Scheme:** `http`
   - **Forward Hostname / IP:** `IP-DE-TU-TRUENAS` (o nombre del contenedor).
   - **Forward Port:** `5010`
   - **Block Common Exploits:** Activado.
4. En la pestaña **SSL**:
   - **SSL Certificate:** *Request a new SSL Certificate*.
   - **Force SSL:** Activado.
   - **Agree to the Let's Encrypt Terms of Service:** Activado.
5. Haz clic en **Save**.

¡Listo! Tu aplicación estará accesible de forma segura bajo `https://licencias.tudominio.cl`.
