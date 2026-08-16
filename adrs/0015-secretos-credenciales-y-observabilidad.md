# ADR 0015: Secretos, credenciales de plataforma y observabilidad

## Estado

Aceptado. Revisión 2. Añade la alerta por **ausencia de latido** del worker, que revierte un riesgo
aceptado en la revisión 1 cuando su mitigación costaba mucho más de lo que cuesta ahora (revisión
adversarial v2, A4).

Decisión nueva en la revisión 1: el diseño anterior no tenía ADR de secretos, no decidía el modelo de
credenciales de Google y no contemplaba observabilidad fuera de la propia base de datos.

## Contexto

El sistema maneja secretos de peso: refresh token de Gmail, Drive y Sheets; token del bot de
Telegram; credenciales SMTP; cadena de conexión a SQL Server. El prototipo además **captura el token
de Telegram desde la interfaz**, lo que obliga a poder escribir secretos en tiempo de ejecución.

Sobre las credenciales de Google hay un modo de fallo concreto y fechado: una aplicación en modo
*testing* en Google Cloud **caduca sus refresh tokens cada 7 días**. El sistema funcionaría una
semana y se detendría. Y aunque ADR 0010 clasificaba correctamente "credenciales revocadas" como
error permanente, **no existía camino de reautenticación**: el botón "Conectar / Reconectar" del
prototipo no tenía backend.

Sobre observabilidad, el panel de errores lee tablas, de modo que **por construcción no puede
mostrar los fallos que impidieron escribir en la base**: SQL Server caído, worker que no arranca,
excepción no capturada antes del primer `INSERT`. Justo los fallos que más importan eran invisibles
en la única herramienta de diagnóstico prevista. Con el reinicio del worker en manual (ADR 0001),
esa ceguera pesa más aún.

## Decisión

### Secretos: gestor dedicado, desplegable in-house

Los tres artefactos obtienen sus credenciales de un **gestor de secretos desplegado en
infraestructura propia**.

> **El diseño no se acopla a ningún proveedor cloud.** La arquitectura define un **puerto de almacén
> de secretos**; la implementación es sustituible. **HashiCorp Vault** se documenta como
> implementación candidata, no como dependencia.

El puerto debe permitir **escritura en caliente**, no solo lectura al arrancar, porque el token de
Telegram se captura desde la interfaz.

**El secreto irreductible:** sigue existiendo un secreto fuera del gestor, el que permite acceder al
gestor. Es inevitable en cualquier diseño y se resuelve con la configuración protegida del servicio.
Queda declarado, no escondido.

### Credenciales de Google: OAuth de usuario, app en producción

Se usa **OAuth de usuario** con la aplicación en estado **"En producción"** en Google Cloud.

> **Nunca en modo *testing*.** Es un requisito de configuración, no una recomendación.

El refresh token no caduca por tiempo, pero **sí puede revocarse** —cambio de contraseña, revisión
de seguridad de Google, revocación manual—. Por eso son obligatorios:

1. **Flujo OAuth completo**, con *redirect URI* sobre HTTPS (ADR 0012).
2. **`POST /api/integraciones/google/reconectar`** funcional (ADR 0008).
3. **Detección de credencial inválida** con aviso, clasificada como `PERMANENTE` (ADR 0010).

Alcance solicitado: `gmail.modify` —para etiquetar el correo procesado, **nunca para borrarlo**—,
más los alcances de Drive y Sheets.

### Observabilidad: agregador in-house

Los tres artefactos emiten **logs estructurados** a un agregador desplegado en infraestructura
propia, con búsqueda, retención configurable y alertas por patrón. Implementaciones candidatas: Seq
o Grafana Loki; el diseño no se acopla a ninguna.

El **`CorrelationId`** de `CommandQueue` se propaga a los tres artefactos y a los eventos de
`OutboxEvent` e `InboxEvent`. Una sola búsqueda reconstruye el recorrido completo de una factura:
correo → procesamiento → promoción → validación → Drive y Sheets.

### Alerta por ausencia de latido del worker

El worker escribe su **latido** en `EstadoIntegracion` (ADR 0003) en cada ciclo. El agregador alerta
cuando el último éxito de la fila `WORKER` supera el intervalo esperado —30 minutos como punto de
partida—.

Esto cierra un riesgo que el diseño había **aceptado explícitamente**: *"si el worker se detiene o se
cuelga, nadie avisa, porque el mecanismo de notificación vive dentro de él. Una bandeja sin facturas
nuevas es indistinguible de un día sin facturas."*

> **Por qué se revisó la aceptación.** Era legítima con la información de entonces: montar vigilancia
> costaba desplegar algo. Ese algo **ya está en el plan de despliegue** —este mismo agregador, con
> alertas por patrón y `CorrelationId` propagado a los tres artefactos—, así que el costo bajó de
> "componente nuevo" a "una fila y una consulta". La aceptación merecía revisarse contra el precio
> nuevo, no contra el viejo.
>
> Dos criterios de éxito del PRD dependían de que alguien estuviera mirando: visibilidad en 15
> minutos y entrega de notificaciones ≥99%.

**Es una alerta por ausencia, no por evento.** Esa es la única forma de detectar un componente que se
detuvo: un componente detenido no emite nada, y una regla que espere un mensaje de error no se
dispara jamás.

**Riesgo residual, declarado:** nada vigila al vigilante. Si el agregador se detiene, la alerta por
ausencia tampoco llega.

### Umbral de espacio

Alerta por **espacio libre** emitida desde el agregador, sobre los tres consumidores: volumen de
documentos, crecimiento de la base y retención de logs. El modo de fallo real es un disco lleno que
detiene la ingesta en silencio.

La cuota de Drive frente a la conservación indefinida debe **verificarse contra el plan de Workspace
contratado** antes de la primera factura real.

## Alternativas consideradas

- **Secretos en archivos de configuración con ACL.** Cero infraestructura. Se descartó porque el
  token de Telegram se captura desde la UI y la aplicación tendría que escribir su propio archivo de
  configuración en caliente, porque los secretos quedarían en claro en disco, y porque caerían fuera
  del respaldo de ADR 0014 si nadie se acuerda de incluirlos.
- **Secretos cifrados en la base con clave maestra externa.** Resuelve la escritura en caliente y el
  respaldo unificado. Se descartó a favor de un gestor dedicado con rotación y auditoría de acceso.
- **Cuenta de servicio con delegación a nivel de dominio en Workspace.** No tiene refresh token que
  caduque ni que revocar, y el worker funcionaría desatendido de forma indefinida. Se descartó
  porque exige una configuración administrativa que hay que solicitar al administrador de Google
  Workspace y que el desarrollador no puede activar por su cuenta.
- **Logs en una tabla de la base de datos.** Se consultarían con SQL y el panel de errores los
  mostraría directamente. Se descartó porque **no resuelve nada**: si SQL Server está caído, no hay
  dónde escribir el log de que SQL Server está caído. Es exactamente el punto ciego que se quiere
  cubrir.
- **Logs a archivo con rotación, sin agregador.** Cero componentes nuevos y ya sacan los logs fuera
  de la base. Se descartó porque correlacionar un fallo entre la API y el worker significaría abrir
  dos archivos y cruzar marcas de tiempo a mano, justo cuando algo está roto y hay prisa.

## Consecuencias

- Los secretos dejan de vivir en archivos y entran en el respaldo de forma deliberada.
- El sistema no se detiene a los 7 días, y cuando la credencial se revoque habrá una salida desde la
  interfaz.
- Los fallos que la base no puede registrar quedan registrados, y el `CorrelationId` los conecta
  entre artefactos.
- **Costo:** el gestor de secretos es una **dependencia de arranque** de los tres artefactos. Si no
  está disponible, el sistema no levanta. Entra en el orden de despliegue de ADR 0012.
- **Costo:** dos componentes más que desplegar, respaldar y mantener.
- **Costo:** el flujo OAuth completo con pantalla de reconexión es trabajo de desarrollo que la
  alternativa de cuenta de servicio habría evitado.
- **Pendiente:** retención de logs y política de rotación, contra el costo de almacenamiento.
