/*  instance-bootstrap.example.sql  --  NO es esquema versionado. NO lo corre SmartNet.Db.Runner.

    Lo corre UNA vez el administrador de la instancia SQL Server (sysadmin), antes del primer
    deploy, para crear la premisa que el esquema versionado (008_usuarios_y_permisos.sql) da por
    hecha: la base BDSmartNet y los dos LOGIN de servidor. El runner se encarga del resto
    (CREATE USER ... FOR LOGIN, roles fact_api / fact_worker, GRANT/DENY).

    Uso en la VM (consola de Administrador, con sqlcmd):
        sqlcmd -S localhost -E -C -i .\instance-bootstrap.example.sql -v ApiPwd="..." WorkerPwd="..."
        (-C = trust server certificate; ODBC Driver 18 cifra y valida por defecto)

    Ajustá las contraseñas. Deben coincidir con las que pongas en config.prod.ps1
    (SMARTNET_API_DB_CONNECTION -> usr_api ; SMARTNET_WORKER_ODBC_CONNECTION -> usr_worker).
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* --- 1. Autenticación mixta -------------------------------------------------------------------
   usr_api / usr_worker son LOGIN de SQL (usuario+contraseña). Si la instancia quedó en
   "Windows Authentication mode", los SQL logins no pueden conectar. Esto lo habilita, pero
   REQUIERE reiniciar el servicio SQL Server para que tome efecto. */
EXEC xp_instance_regwrite
    N'HKEY_LOCAL_MACHINE',
    N'Software\Microsoft\MSSQLServer\MSSQLServer',
    N'LoginMode', REG_DWORD, 2;   -- 2 = Mixed Mode
PRINT 'LoginMode = Mixed. Reiniciá el servicio SQL Server para que tome efecto.';

/* --- 2. Base de datos ----------------------------------------------------------------------- */
IF DB_ID('BDSmartNet') IS NULL
BEGIN
    CREATE DATABASE BDSmartNet;
    PRINT 'Base BDSmartNet creada.';
END
ELSE
    PRINT 'Base BDSmartNet ya existe -- sin cambios.';

/* Modelo de recuperación: en esta demo académica no hay política de respaldo (ADR 0014).
   SIMPLE evita que el log transaccional crezca sin LOG BACKUP. En producción real esta decisión
   es del administrador de la instancia compartida, NO de este proyecto. */
ALTER DATABASE BDSmartNet SET RECOVERY SIMPLE;

/* --- 3. LOGIN de servidor ----------------------------------------------------------------- */
IF SUSER_ID('usr_api') IS NULL
BEGIN
    CREATE LOGIN usr_api WITH PASSWORD = '$(ApiPwd)', CHECK_POLICY = ON, DEFAULT_DATABASE = BDSmartNet;
    PRINT 'LOGIN usr_api creado.';
END
ELSE
    PRINT 'LOGIN usr_api ya existe -- sin cambios (no se toca la contraseña).';

IF SUSER_ID('usr_worker') IS NULL
BEGIN
    CREATE LOGIN usr_worker WITH PASSWORD = '$(WorkerPwd)', CHECK_POLICY = ON, DEFAULT_DATABASE = BDSmartNet;
    PRINT 'LOGIN usr_worker creado.';
END
ELSE
    PRINT 'LOGIN usr_worker ya existe -- sin cambios.';

/* --- 4. Nada más -------------------------------------------------------------------------------
   NO crear los USER de base, NO asignar roles, NO dar GRANT aquí: eso es 008, aplicado por el
   runner con el principal de despliegue. Este script termina donde empieza el esquema versionado. */
GO
