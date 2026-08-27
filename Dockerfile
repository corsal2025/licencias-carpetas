# ================================================================
# Etapa 1: Build y Publicación
# ================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar archivos de definición de proyectos y restaurar dependencias
COPY ["src/LicenciasCarpetas/LicenciasCarpetas.csproj", "src/LicenciasCarpetas/"]
RUN dotnet restore "src/LicenciasCarpetas/LicenciasCarpetas.csproj"

# Copiar todo el código fuente y compilar
COPY . .
WORKDIR "/src/src/LicenciasCarpetas"
RUN dotnet publish "LicenciasCarpetas.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ================================================================
# Etapa 2: Runtime de Producción
# ================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Variables de entorno por defecto
ENV ASPNETCORE_URLS=http://+:5010
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Crear directorio de datos persistentes
RUN mkdir -p /app/data /app/data/exports

# Copiar artefactos publicados
COPY --from=build /app/publish .

# Exponer puerto HTTP
EXPOSE 5010

# Iniciar la aplicación
ENTRYPOINT ["dotnet", "LicenciasCarpetas.dll"]
