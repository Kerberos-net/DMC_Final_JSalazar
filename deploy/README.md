# deploy/ — despliegue del host único (Windows Server)

Operativa del despliegue descrito en [`../DEPLOY-PLAN.md`](../DEPLOY-PLAN.md). Todo esto se corre
**en el host**, desde una consola de PowerShell 7 (`pwsh`) **como Administrador**.

El build NO ocurre aquí: `.github/workflows/deploy-build.yml` produce `smartnet-vX.Y.Z.zip` al
empujar un tag `deploy/vX.Y.Z` sobre `main`, y lo publica como GitHub Release. El host descarga ese
`.zip` y lo pasa a `deploy.ps1`.

## Archivos

| Script | Rol | Frecuencia |
|---|---|---|
| `config.example.ps1` | Plantilla de configuración/secretos. Copiar a `config.<entorno>.ps1` en el host. | una vez |
| `preflight.ps1` | Verifica prerequisitos de sistema (runtimes, ODBC, Tesseract, base, LOGINs). Solo lectura. | antes de cada deploy |
| `install-services.ps1` | Crea el Servicio de Windows `SmartNetApi` + la tarea programada `\SmartNet\SmartNet-Caddy` (AtStartup, SYSTEM). | una vez / cambio de topología |
| `register-worker-tasks.ps1` | Registra las 6 tareas de Task Scheduler del worker. | una vez / cambio de cadencia |
| `deploy.ps1` | Despliega un release en el orden de ADR 0012. | cada release |
| `run-worker-entry.ps1` | Wrapper que Task Scheduler invoca por cada entry point. | automático |
| `verify.ps1` | Verificación post-deploy. | después de cada deploy |
| `backup.ps1` | Copia keyring + secretos antes de cada release. | lo llama `deploy.ps1` |
| `Caddyfile.example` | Config del proxy inverso. | una vez / cambio de topología |

## Primera vez en un host nuevo

```powershell
# 0. Prerequisitos de sistema (los instala el operador, no estos scripts):
#    PowerShell 7 · .NET 10 ASP.NET Core Runtime · Python 3.13 · ODBC Driver 18 ·
#    Tesseract + idioma spa · sqlcmd · caddy.exe
#    En la instancia SQL: base BDSmartNet + LOGIN usr_api / usr_worker
#      -> deploy\instance-bootstrap.example.sql  (lo corre el sysadmin de la instancia, una vez)
#    En hosts (VM y clientes): una entrada para $PublicHost (p. ej. 127.0.0.1  facturas.empresa.local)

Copy-Item .\config.example.ps1 .\config.prod.ps1
notepad .\config.prod.ps1                      # ajustar rutas, host, cadenas de conexión, secretos

pwsh -File .\preflight.ps1 -Environment prod
pwsh -File .\deploy.ps1  -Environment prod -PackageZip C:\SmartNet\smartnet-v1.0.0.zip
pwsh -File .\install-services.ps1     -Environment prod
Start-Service SmartNetApi
Start-ScheduledTask -TaskPath '\SmartNet\' -TaskName 'SmartNet-Caddy'
pwsh -File .\register-worker-tasks.ps1 -Environment prod
pwsh -File .\verify.ps1  -Environment prod
```

> Con `tls internal` (CA local de Caddy), Caddy instala su raíz en el almacén de confianza de la VM
> al correr como SYSTEM. Para que un navegador en otra máquina no muestre advertencia, hay que
> importar esa raíz (`caddy trust` en la VM exporta el .crt) o resolver el certificado por otra vía
> (ADR 0012, pendiente).

## Cada release posterior

```powershell
pwsh -File .\preflight.ps1 -Environment prod
pwsh -File .\deploy.ps1    -Environment prod -PackageZip C:\descargas\smartnet-v1.1.0.zip
pwsh -File .\verify.ps1    -Environment prod
```

## Revertir al release anterior

```powershell
# Los últimos 3 releases quedan en C:\SmartNet\releases\ . Re-desplegá el zip anterior:
pwsh -File .\deploy.ps1 -Environment prod -PackageZip C:\SmartNet\releases\smartnet-v1.0.0.zip -SkipSchema
```

> Un rollback de código **no** revierte el esquema (los scripts son aditivos e idempotentes; el
> código anterior sigue funcionando contra el esquema nuevo). Revertir un cambio de esquema
> destructivo requiere un **script forward nuevo**, nunca correr un `rollback/*_down.sql` en
> producción. La restauración de la base **no es una operación de este proyecto** (ADR 0014).

## Lo que estos scripts NO hacen (deuda de puesta en producción — ver `DEPLOY-PLAN.md`)

- No montan HashiCorp Vault ni un agregador de logs con alertas (ADR 0015).
- No definen la política de respaldo de la instancia SQL Server (ADR 0014).
- No resuelven el origen del certificado TLS (ADR 0012) — usan `tls internal` de Caddy.
- No stand-up el entorno de pruebas separado.
