/*  FIXTURE DE ENTORNO — NO ES PARTE DEL ESQUEMA VERSIONADO
    ============================================================================
    Carga los cinco catalogos desde los CSV de .\data\.

    Es idempotente: vacia cada tabla antes de cargarla, de modo que volver a
    ejecutarlo deja el mismo resultado. El orden respeta la clave foranea de
    Proveedor hacia DocumentoIdentidad.

    Los CSV van delimitados por barra vertical y NO por coma, porque
    Motivo.cuenta contiene comas dentro del valor.

    REQUISITO: BULK INSERT lee las rutas desde el SERVIDOR, no desde el cliente.
    Si SQL Server no corre en esta maquina, copia .\data\ a una ruta que el
    servidor alcance y ajusta @ruta.
    ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*  Ruta de los CSV. Ajustar si el servidor no es local. */
DECLARE @ruta NVARCHAR(400) = N'D:\Proyectos\Claude\Clases\Proyecto\DMC_Final_JSalazar\SmartNet\db\fixtures\data\';
DECLARE @sql  NVARCHAR(MAX);

/*  El orden importa: el hijo se vacia antes que el padre. */
DELETE FROM dbo.Proveedor;
DELETE FROM dbo.CuentaContable;
DELETE FROM dbo.Motivo;
DELETE FROM dbo.Origen;
DELETE FROM dbo.DocumentoIdentidad;

/*  CODEPAGE 65001 = UTF-8. Los CSV se escriben sin BOM. */
SET @sql = N'
BULK INSERT dbo.DocumentoIdentidad FROM ''' + @ruta + N'DocumentoIdentidad.csv''
    WITH (FIELDTERMINATOR = ''|'', ROWTERMINATOR = ''0x0a'', CODEPAGE = ''65001'', TABLOCK);

BULK INSERT dbo.Origen FROM ''' + @ruta + N'Origen.csv''
    WITH (FIELDTERMINATOR = ''|'', ROWTERMINATOR = ''0x0a'', CODEPAGE = ''65001'', TABLOCK);

BULK INSERT dbo.Motivo FROM ''' + @ruta + N'Motivo.csv''
    WITH (FIELDTERMINATOR = ''|'', ROWTERMINATOR = ''0x0a'', CODEPAGE = ''65001'', TABLOCK);

BULK INSERT dbo.CuentaContable FROM ''' + @ruta + N'CuentaContable.csv''
    WITH (FIELDTERMINATOR = ''|'', ROWTERMINATOR = ''0x0a'', CODEPAGE = ''65001'', TABLOCK);

BULK INSERT dbo.Proveedor FROM ''' + @ruta + N'Proveedor.csv''
    WITH (FIELDTERMINATOR = ''|'', ROWTERMINATOR = ''0x0a'', CODEPAGE = ''65001'', TABLOCK);
';

EXEC sp_executesql @sql;
GO

/*  ------------------------------------------------------------------------
    Comprobaciones. Los conteos y las cifras notables se afirman contra lo
    que los documentos del proyecto dan por cierto, de modo que una carga
    equivocada falla aqui y no tres items mas adelante.
    ------------------------------------------------------------------------ */

DECLARE @errores INT = 0;

IF (SELECT COUNT(*) FROM dbo.DocumentoIdentidad) <> 6
    BEGIN PRINT 'ERROR: DocumentoIdentidad no tiene 6 filas.'; SET @errores += 1; END

IF (SELECT COUNT(*) FROM dbo.Origen) <> 13
    BEGIN PRINT 'ERROR: Origen no tiene 13 filas.'; SET @errores += 1; END

IF (SELECT COUNT(*) FROM dbo.Motivo) <> 90
    BEGIN PRINT 'ERROR: Motivo no tiene 90 filas.'; SET @errores += 1; END

IF (SELECT COUNT(*) FROM dbo.CuentaContable) <> 1650
    BEGIN PRINT 'ERROR: CuentaContable no tiene 1650 filas.'; SET @errores += 1; END

IF (SELECT COUNT(*) FROM dbo.Proveedor) <> 6600
    BEGIN PRINT 'ERROR: Proveedor no tiene 6600 filas.'; SET @errores += 1; END

/*  907 cuentas imputables de 6 digitos: la cifra que REGLAS.md y el
    TECH-DESIGN citan para la resolucion por prefijo. */
IF (SELECT COUNT(*) FROM dbo.CuentaContable WHERE LEN(cuenta) = 6) <> 907
    BEGIN PRINT 'ERROR: las cuentas de 6 digitos no son 907.'; SET @errores += 1; END

/*  El proveedor generico existe y es exactamente 'P00000', no 'P0000'.
    De esto depende que la invariante global 4 de ADR 0006 llegue a dispararse. */
IF NOT EXISTS (SELECT 1 FROM dbo.Proveedor WHERE codpro = 'P00000')
    BEGIN PRINT 'ERROR: no existe el proveedor generico P00000.'; SET @errores += 1; END

/*  El numero de documento quedo solo con digitos: el prefijo DNI se retiro. */
IF EXISTS (SELECT 1 FROM dbo.Proveedor WHERE rucpro LIKE '%[^0-9]%')
    BEGIN PRINT 'ERROR: hay numeros de documento con caracteres no numericos.'; SET @errores += 1; END

/*  283 cuentas declaran destino (ADR 0006). */
IF (SELECT COUNT(*) FROM dbo.CuentaContable WHERE ctarefleja IS NOT NULL AND ctarefleja <> '') <> 283
    BEGIN PRINT 'AVISO: las cuentas con ctarefleja no son 283. Revisar contra ADR 0006.'; END

IF @errores = 0
    PRINT 'Catalogos cargados y verificados.';
ELSE
    THROW 50001, 'La carga de catalogos no supero las comprobaciones.', 1;
GO
