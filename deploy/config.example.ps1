# Copia este archivo a `config.<entorno>.ps1` (p. ej. config.prod.ps1) EN EL HOST, fuera del
# checkout de git si es posible, y ajusta los valores. `config.*.ps1` está en .gitignore salvo este
# ejemplo -- los secretos nunca entran al repo (DEPLOY-PLAN.md § Config & Secrets).
#
# Los scripts de deploy hacen dot-source de este archivo: `. .\config.prod.ps1`

# --- Rutas en el host ---------------------------------------------------------------------------
$SmartNetRoot      = 'C:\SmartNet'                       # raíz de instalación
$ReleasesDir       = Join-Path $SmartNetRoot 'releases'  # se conservan los últimos $KeepReleases
$CurrentDir        = Join-Path $SmartNetRoot 'current'   # release activo (api\ web\ admin\ runner\ worker\)
$LogsDir           = Join-Path $SmartNetRoot 'logs'
$BackupsDir        = Join-Path $SmartNetRoot 'backups'
$StorageRoot       = Join-Path $SmartNetRoot 'adjuntos'  # volumen compartido API <-> worker
$KeyringPath       = 'C:\ProgramData\SmartNet\dataprotection-keys'
$SecretsDir        = Join-Path $SmartNetRoot 'secrets'   # worker.env, ACL restringida
$WorkerVenv        = Join-Path $SmartNetRoot 'worker-venv'
$KeepReleases      = 3

# --- Red ---------------------------------------------------------------------------------------
$ApiListenUrl      = 'http://127.0.0.1:5080'             # Kestrel, SOLO loopback (Caddy proxea)
$PublicHost        = 'facturas.empresa.local'

# --- Servicio de Windows (API) + tarea programada (Caddy) -----------------------------------
# La API corre como Servicio de Windows real (Program.cs UseWindowsService()).
# Caddy NO implementa el protocolo del SCM de Windows: `caddy run` como servicio se cuelga al
# arrancar (error 1053). Se corre como tarea programada AtStartup, como SYSTEM, con reinicio
# automático. Un wrapper de servicio (WinSW / NSSM) es la mejora de producción (DEPLOY-PLAN.md).
$ApiServiceName    = 'SmartNetApi'
$CaddyTaskName     = 'SmartNet-Caddy'                    # bajo \SmartNet\ en Task Scheduler
$CaddyExe          = 'C:\SmartNet\caddy\caddy.exe'
$CaddyfilePath     = 'C:\SmartNet\caddy\Caddyfile'

# --- Catálogo externo dbo.* (SOLO demo) -----------------------------------------------------
# En producción real las 5 tablas dbo.* (DocumentoIdentidad, Origen, Motivo, CuentaContable,
# Proveedor) las mantiene el sistema contable de la compañía y este proyecto solo tiene SELECT
# (ADR 0003). En esta demo académica la base está vacía, así que hay que crearlas y cargarlas
# desde los CSV del paquete (fixtures/), ANTES del runner (008 hace GRANT SELECT sobre ellas).
#
#   $true  -> deploy.ps1 aplica fixtures/010 (DDL) + fixtures/020 (datos) antes del runner.
#   $false -> asume que las tablas dbo.* ya existen (deploy real). NUNCA $true contra la BD
#             contable compartida: 020 hace DELETE FROM dbo.Proveedor / CuentaContable / etc.
$AplicarFixturesCatalogoDemo = $true
$FixturesDataDir             = 'C:\SmartNet\catalog-data'   # los CSV se copian acá (lo lee SQL Server)

# --- Base de datos ---------------------------------------------------------------------------
# El principal de DESPLIEGUE (aplica el esquema). NO es usr_api. Solo se usa durante el deploy.
$DbConnectionDeploy = 'Server=localhost;Database=BDSmartNet;Integrated Security=True;TrustServerCertificate=True;Encrypt=False'
# El principal de la API en runtime (usr_api). Distinto a propósito (SmartNetApi/CLAUDE.md).
$DbConnectionApi    = 'Server=localhost;Database=BDSmartNet;User Id=usr_api;Password=CAMBIAR;TrustServerCertificate=True;Encrypt=False'

# --- Secretos del worker (se escriben a $SecretsDir\worker.env con ACL restringida) ----------
# Cadena ODBC (usr_worker) -- dialecto distinto al de la API (ODBC, no ADO.NET).
$WorkerOdbcConnection   = 'DRIVER={ODBC Driver 18 for SQL Server};SERVER=localhost;DATABASE=BDSmartNet;UID=usr_worker;PWD=CAMBIAR;TrustServerCertificate=yes;'
# JSON atómico (un secreto multi-campo por variable, nunca N variables sueltas).
$WorkerGmailCredentials = '{"client_id":"...","client_secret":"...","refresh_token":"...","token_uri":"https://oauth2.googleapis.com/token"}'
$WorkerTelegramCreds    = ''   # opcional
$WorkerSmtpCreds        = ''   # opcional
$TesseractCmd           = 'C:\Program Files\Tesseract-OCR\tesseract.exe'

# --- Cadencia de las tareas del worker (Task Scheduler) --------------------------------------
# ScheduleType: Daily | Minute ; Interval en minutos para Minute.
$WorkerTasks = @(
    @{ Name = 'SmartNet-TipoCambio';   Entry = 'smartnet-tipo-cambio';   Schedule = 'Daily';  At = '08:00' }
    @{ Name = 'SmartNet-Gmail';        Entry = 'smartnet-gmail';         Schedule = 'Minute'; Interval = 15 }
    @{ Name = 'SmartNet-Procesamiento';Entry = 'smartnet-procesamiento'; Schedule = 'Minute'; Interval = 15 }
    @{ Name = 'SmartNet-Inbox';        Entry = 'smartnet-inbox';         Schedule = 'Minute'; Interval = 15 }
    @{ Name = 'SmartNet-Outbox';       Entry = 'smartnet-outbox';        Schedule = 'Minute'; Interval = 30 }
    @{ Name = 'SmartNet-CommandQueue'; Entry = 'smartnet-command-queue'; Schedule = 'Minute'; Interval = 5 }
)
