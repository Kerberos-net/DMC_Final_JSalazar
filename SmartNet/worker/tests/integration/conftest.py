"""Arnes de pruebas de integracion (marker `integracion`, design.md Decision 7 / tasks.md 3.9).

Provisiona una base `fact_test_worker_<id>` efimera, aplica el esquema versionado real via
`SmartNet.Db.Runner` (nunca una reimplementacion en Python del splitting de scripts SQL — la
fuente de verdad del esquema sigue siendo el runner .NET, ADR 0016) y crea un LOGIN efimero
literal `usr_worker` para ejercer los GRANT reales de `008_usuarios_y_permisos.sql` sobre
`fact_worker` (mismo mecanismo que el job de CI `pruebas-de-worker-python` descrito en
design.md — contenedor propio + `CREATE LOGIN` efimero).

Dos dialectos de cadena de conexion conviven aqui, y no son intercambiables: `SmartNet.Db.Runner`
usa `Microsoft.Data.SqlClient` (sintaxis ADO.NET: `Server=`, `Integrated Security=`) mientras que
este arnes habla con la base via `pyodbc` (sintaxis ODBC: `DRIVER={...}`, `Trusted_Connection=`).
Se contruyen ambas por separado a partir del mismo host/nombre de base, nunca se reusa una para
la otra.

Requisitos para correr estas pruebas localmente (documentados aqui porque, en el entorno de
implementacion, no habia una instancia SQL Server real alcanzable — ver README.md, seccion
"Limitaciones conocidas"):

  1. Una instancia SQL Server alcanzable donde el usuario que corre pytest tenga permisos de
     sysadmin (CREATE DATABASE, CREATE LOGIN). NUNCA apuntar esto a la base compartida real:
     debe ser una instancia o contenedor desechable, igual regla que
     `SmartNet.Db.TestBootstrap.TestDatabaseFixture` del lado .NET.
  2. El SDK de .NET (`dotnet`) en el PATH, para invocar `SmartNet.Db.Runner` contra la base
     efimera.
  3. El driver ODBC de SQL Server instalado (`ODBC Driver 18 for SQL Server`) y el paquete
     `pyodbc` instalado (`pip install -e .[dev]`).

Variables de entorno:
  SMARTNET_TEST_SQL_HOST                -- host de la instancia (por defecto `localhost`,
                                            autenticacion integrada de Windows en ambos dialectos)
  SMARTNET_WORKER_TEST_LOGIN_PASSWORD   -- password del LOGIN efimero `usr_worker` (por defecto,
                                            una constante de prueba — nunca usar en un entorno real)

Si SQL Server o `dotnet` no estan disponibles, el fixture hace `pytest.skip(...)` con un mensaje
explicito. Nunca se marca una prueba como pasada sin haber corrido contra una base real.
"""

from __future__ import annotations

import os
import subprocess
import uuid
from pathlib import Path

import pyodbc
import pytest

_REPO_ROOT = Path(__file__).resolve().parents[4]
_RUNNER_PROJECT = _REPO_ROOT / "SmartNet" / "db" / "runner" / "SmartNet.Db.Runner"

_SQL_HOST = os.environ.get("SMARTNET_TEST_SQL_HOST", "localhost")
_LOGIN_NAME = "usr_worker"
_LOGIN_PASSWORD = os.environ.get("SMARTNET_WORKER_TEST_LOGIN_PASSWORD", "SoloParaPruebas_2026!")

# Los cuatro catalogos externos dbo.* que 008_usuarios_y_permisos.sql (GRANT SELECT) y
# 010_motivo_atributo_demo.sql (INSERT ... SELECT ... FROM dbo.Motivo) necesitan que existan como
# ESTRUCTURA, sin datos de negocio reales — mismo fixture que
# TestDatabaseFixture.CreateExternalDboCatalogsAsync() del lado .NET (item #3/#4, misma
# dependencia). Nunca se aplica contra la base compartida real.
_CREATE_EXTERNAL_DBO_CATALOGS_SQL = """
IF OBJECT_ID('dbo.DocumentoIdentidad', 'U') IS NULL
    CREATE TABLE dbo.DocumentoIdentidad (coddocide CHAR(2) NOT NULL,
        nomdocide NVARCHAR(60) NOT NULL,
        CONSTRAINT PK_DocumentoIdentidad PRIMARY KEY CLUSTERED (coddocide));
IF OBJECT_ID('dbo.Origen', 'U') IS NULL
    CREATE TABLE dbo.Origen (codigo CHAR(2) NOT NULL, origen NVARCHAR(40) NOT NULL,
        CONSTRAINT PK_Origen PRIMARY KEY CLUSTERED (codigo));
IF OBJECT_ID('dbo.Motivo', 'U') IS NULL
    CREATE TABLE dbo.Motivo (codigo INT NOT NULL, motivo NVARCHAR(60) NOT NULL,
        cuenta VARCHAR(120) NULL, CONSTRAINT PK_Motivo PRIMARY KEY CLUSTERED (codigo));
IF OBJECT_ID('dbo.CuentaContable', 'U') IS NULL
    CREATE TABLE dbo.CuentaContable (cuenta VARCHAR(10) NOT NULL,
        descripcion NVARCHAR(60) NOT NULL, nivel TINYINT NULL, ctarefleja VARCHAR(10) NULL,
        ctapuente VARCHAR(10) NULL, CONSTRAINT PK_CuentaContable PRIMARY KEY CLUSTERED (cuenta));
IF OBJECT_ID('dbo.Proveedor', 'U') IS NULL
    CREATE TABLE dbo.Proveedor (codpro CHAR(6) NOT NULL, proveedor NVARCHAR(80) NOT NULL,
        coddocide CHAR(2) NULL, rucpro VARCHAR(11) NULL,
        CONSTRAINT PK_Proveedor PRIMARY KEY CLUSTERED (codpro));
"""

# TEST FIXTURE, no dato de negocio real — mismo subconjunto que
# TestDatabaseFixture.SeedDboMotivoFixtureRowsAsync() del lado .NET: los 23 motivos `†` que
# 010_motivo_atributo_demo.sql reclasifica, mas cinco motivos de control.
_SEED_DBO_MOTIVO_FIXTURE_SQL = """
INSERT INTO dbo.Motivo (codigo, motivo, cuenta) VALUES
    (5, 'Transferencia a Caja chica', '1013,1021,1022'),
    (13, 'Movilidad', '631123'),
    (16, 'Parqueo o cochera', '6393'),
    (17, 'Tasas de contratos', '644311'),
    (18, 'Peaje', '639915'),
    (19, 'Utiles de escritorio menores', '656111'),
    (20, 'Utiles de Limpieza menores', '656211'),
    (21, 'Botiquin menores', '656212'),
    (30, 'Mantenimiento local menores', '634311'),
    (38, 'Copia Literal o vigencia poder', '636913'),
    (40, 'Legalizaciones', '632211'),
    (42, 'Recarga de nextel menor a 100', '636412'),
    (46, 'Repuesto soporte tecnico menor a 50', '656511'),
    (48, N'Gastos de representacion menor a 100', '6373'),
    (49, N'Servicio Reparacion equipo menor a 50', '634314'),
    (53, 'Recarga de tarjetas peruanas', '169901'),
    (56, 'Reniec', '636912'),
    (59, 'Tasas Judiciales y Policiales', '644311'),
    (60, 'Arreglo Floral', '659913'),
    (77, N'Periodico', '659914'),
    (81, 'Movilidad-Taxi por viaje', '631124'),
    (88, N'Devolucion Comprobante CChica', '169105'),
    (90, 'Mantenimiento rep muebles y eq', '634313'),
    (11, 'Servicio custodia mercaderia SS', '639922'),
    (12, N'Fotocopia-Impresion', '639914'),
    (22, 'Fletes traslado de mercaderia', '631111'),
    (1, 'Pago a Cuenta de Proveedores', '656412'),
    (28, 'NO USAR', '1424');
"""


def _odbc_master_connection_string() -> str:
    return (
        f"DRIVER={{ODBC Driver 18 for SQL Server}};SERVER={_SQL_HOST};"
        "Trusted_Connection=yes;TrustServerCertificate=yes;Encrypt=no;"
    )


def _ado_connection_string(db_name: str) -> str:
    # Dialecto de Microsoft.Data.SqlClient — consumido unicamente por SmartNet.Db.Runner.
    return (
        f"Server={_SQL_HOST};Database={db_name};Integrated Security=True;"
        "TrustServerCertificate=True;Encrypt=False"
    )


def _odbc_worker_connection_string(db_name: str) -> str:
    return (
        f"DRIVER={{ODBC Driver 18 for SQL Server}};SERVER={_SQL_HOST};DATABASE={db_name};"
        f"UID={_LOGIN_NAME};PWD={_LOGIN_PASSWORD};TrustServerCertificate=yes;Encrypt=no;"
    )


def _odbc_admin_connection_string(db_name: str) -> str:
    return (
        f"DRIVER={{ODBC Driver 18 for SQL Server}};SERVER={_SQL_HOST};DATABASE={db_name};"
        "Trusted_Connection=yes;TrustServerCertificate=yes;Encrypt=no;"
    )


@pytest.fixture
def worker_db():
    odbc_master_cs = _odbc_master_connection_string()

    try:
        master_conn = pyodbc.connect(odbc_master_cs, timeout=5, autocommit=True)
    except pyodbc.Error as error:
        pytest.skip(
            f"No se pudo conectar a SQL Server en '{_SQL_HOST}' via pyodbc: {error}. "
            "Prueba de integracion omitida, no fallida."
        )
        return

    db_name = f"fact_test_worker_{uuid.uuid4().hex}"

    try:
        master_conn.execute(f"CREATE DATABASE [{db_name}];")

        # usr_worker es un LOGIN real de instancia (server-scoped) — 008_usuarios_y_permisos.sql
        # hace el propio `CREATE USER usr_worker FOR LOGIN usr_worker` al migrar (SUSER_ID ya no
        # es NULL). Debe existir ANTES de correr el runner, o el THROW de guarda del script se
        # dispara.
        master_conn.execute(
            f"IF SUSER_ID('{_LOGIN_NAME}') IS NULL "
            f"CREATE LOGIN [{_LOGIN_NAME}] WITH PASSWORD = '{_LOGIN_PASSWORD}';"
        )

        # usr_api no se usa desde estas pruebas, pero 008 exige que exista algun principal (login
        # o usuario WITHOUT LOGIN) antes de migrar — mismo patron que
        # TestDatabaseFixture.CreateWithoutLoginUserAsync() del lado .NET, sin login de instancia.
        with pyodbc.connect(_odbc_admin_connection_string(db_name), autocommit=True) as db_conn:
            db_conn.execute(
                "IF DATABASE_PRINCIPAL_ID('usr_api') IS NULL CREATE USER [usr_api] WITHOUT LOGIN;"
            )
            db_conn.execute(_CREATE_EXTERNAL_DBO_CATALOGS_SQL)
            db_conn.execute(_SEED_DBO_MOTIVO_FIXTURE_SQL)

        if not _RUNNER_PROJECT.exists():
            pytest.skip(f"No se encontro el proyecto de SmartNet.Db.Runner en {_RUNNER_PROJECT}.")
            return

        try:
            runner_result = subprocess.run(
                [
                    "dotnet",
                    "run",
                    "--project",
                    str(_RUNNER_PROJECT),
                    "--",
                    "--connection",
                    _ado_connection_string(db_name),
                ],
                capture_output=True,
                text=True,
                timeout=180,
            )
        except FileNotFoundError:
            pytest.skip("El SDK de .NET ('dotnet') no esta disponible en PATH.")
            return

        if runner_result.returncode != 0:
            pytest.skip(
                "SmartNet.Db.Runner no pudo aplicar el esquema contra la base efimera: "
                f"{runner_result.stderr[:500] or runner_result.stdout[:500]}"
            )
            return

        yield {
            "master_connection": master_conn,
            "db_name": db_name,
            "worker_connection_string": _odbc_worker_connection_string(db_name),
        }
    finally:
        try:
            master_conn.execute(
                f"IF DB_ID('{db_name}') IS NOT NULL BEGIN "
                f"ALTER DATABASE [{db_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
                f"DROP DATABASE [{db_name}]; END;"
            )
        except pyodbc.Error:
            pass
        try:
            master_conn.execute(
                f"IF SUSER_ID('{_LOGIN_NAME}') IS NOT NULL DROP LOGIN [{_LOGIN_NAME}];"
            )
        except pyodbc.Error:
            pass
        master_conn.close()
