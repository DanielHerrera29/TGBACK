# TGBACK — API de Transportes Gutiérrez

ASP.NET Core 8. El Dockerfile de la raíz publica la API en el puerto definido por Render.

## Despliegue en Render

Servicio existente: `tgback-api`, repositorio `DanielHerrera29/TGBACK`, rama `main`.
Conservar las variables del servicio: `Supabase__Url`, `Supabase__Key`,
`Brevo__ApiKey`, `Brevo__SenderEmail` y las demás opciones ya configuradas.
Los secretos no se incluyen en el commit.

Después del push, usar **Manual Deploy → Deploy latest commit** si el servicio
no tiene despliegue automático. `GET /health` devuelve la revisión desplegada;
`GET /api/rndc/health` comprueba acceso a Supabase sin enviar operaciones RNDC.

Esta versión requiere el frontend actualizado: las rutas operativas usan el
Bearer token devuelto por `/api/sesion/iniciar`. Volver a iniciar sesión tras
el despliegue. El rol `operator` accede solo a órdenes y lectura de clientes;
`admin` y `administrativo` pueden crear clientes. Los roles y el estado activo
se comprueban en la base para cada solicitud.

## Contrato de base de datos

Las tres migraciones de septiembre documentan el incremento de órdenes y
servicios instalado previamente en Supabase. El contenedor **no ejecuta SQL**.
No repetir la creación inicial sobre tablas existentes. Verificar la presencia
de `guardar_orden_servicios`, `recuperar_orden_servicios`, `clientes_para_orden`,
`reclamar_entrega_orden` y `finalizar_entrega_orden` en el proyecto configurado.

Las entregas `POR_VERIFICAR` anteriores no se liberan por desplegar este código:
requieren conciliación antes de reintentar. La ausencia de configuración de
correo se detecta antes de adquirir un nuevo bloqueo de entrega.

El filtro de API no sustituye la revisión pendiente de RLS y de los accesos
directos de módulos antiguos a Supabase.

## Validación sin efectos externos

```powershell
dotnet publish TransportesGutierrez.Api.csproj -c Release -o artifacts/publish -p:UseAppHost=false
dotnet run --project tests/BackendContracts/BackendContracts.csproj
```

Las pruebas usan HTTP simulado: no crean datos ni envían correos o solicitudes RNDC.
