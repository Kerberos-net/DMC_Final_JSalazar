"""Prueba de integracion (marker `integracion`) del script `020_outbox_clasificacion.sql` -- BACKLOG
#17, Fase 1, tasks.md 1.3, design.md D1b.

`worker_db` (conftest.py) ya aplica el esquema completo (incluido 020) via `SmartNet.Db.Runner`
antes de ceder el fixture -- reaplicarlo aqui ejercita la convergencia sin fallar (create-if-absent,
mismo patron que 009/013/015). No reimplementa el runner: invoca el mismo binario .NET, la fuente
de verdad del esquema (ADR 0016)."""

from __future__ import annotations

import subprocess
from pathlib import Path

import pyodbc
import pytest

pytestmark = pytest.mark.integracion

_REPO_ROOT = Path(__file__).resolve().parents[4]
_RUNNER_PROJECT = _REPO_ROOT / "SmartNet" / "db" / "runner" / "SmartNet.Db.Runner"


def _ado_connection_string(db_name: str, host: str) -> str:
    return (
        f"Server={host};Database={db_name};Integrated Security=True;"
        "TrustServerCertificate=True;Encrypt=False"
    )


def test_020_reaplicado_converge_sin_fallar(worker_db):
    """Reaplicar el runner completo (que incluye 020) contra la misma base ya migrada por el
    fixture es un no-op -- ninguna guarda `NOT EXISTS` dispara un fallo."""
    db_name = worker_db["db_name"]

    resultado = subprocess.run(
        [
            "dotnet",
            "run",
            "--project",
            str(_RUNNER_PROJECT),
            "--",
            "--connection",
            _ado_connection_string(db_name, "localhost"),
        ],
        capture_output=True,
        text=True,
        timeout=180,
    )

    assert resultado.returncode == 0, resultado.stderr or resultado.stdout


def test_columna_y_check_existen(worker_db):
    with pyodbc.connect(worker_db["worker_connection_string"]) as conn:
        cursor = conn.cursor()
        cursor.execute(
            "SELECT COUNT(*) FROM sys.columns WHERE name = 'Clasificacion' "
            "AND object_id = OBJECT_ID('fact.OutboxEventIntegracion')"
        )
        assert cursor.fetchone()[0] == 1

        cursor.execute(
            "SELECT COUNT(*) FROM sys.check_constraints "
            "WHERE name = 'CK_OutboxEventIntegracion_Clasificacion'"
        )
        assert cursor.fetchone()[0] == 1


def test_check_rechaza_valor_fuera_de_vocabulario(worker_db):
    with pyodbc.connect(worker_db["api_connection_string"]) as conn:
        cursor = conn.cursor()
        # FK_OutboxEvent_Factura -- FacturaId debe apuntar a una fila real (mismo patron que
        # test_outbox_contrato_bidireccional.py::_insertar_factura_como_usr_api).
        factura_id = cursor.execute(
            "INSERT INTO fact.Factura (ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision) "
            "OUTPUT INSERTED.FacturaId "
            "VALUES ('P00000', '01', 100.00, 'PEN', '2026-01-01');"
        ).fetchone()[0]
        cursor.execute(
            # Secuencia NOT NULL (006_contratos.sql:19), alimentada por fact.SeqOutbox
            # (design.md item 10, 019_permiso_secuencia_seqoutbox.sql) -- se debe pasar inline en
            # el INSERT, un SELECT NEXT VALUE FOR suelto no la asigna a la fila.
            # Tipo debe estar en CK_OutboxEvent_Tipo (006_contratos.sql:24-27) -- 'FACTURA_VALIDADA'
            # es el unico de los cinco valores validos que corresponde a un evento nuevo.
            "INSERT INTO fact.OutboxEvent (Tipo, FacturaId, Payload, Secuencia) "
            "VALUES ('FACTURA_VALIDADA', ?, '{}', NEXT VALUE FOR fact.SeqOutbox)",
            factura_id,
        )
        cursor.execute(
            "SELECT OutboxEventId FROM fact.OutboxEvent WHERE FacturaId = ?", factura_id
        )
        outbox_event_id = cursor.fetchone()[0]
        cursor.execute(
            "INSERT INTO fact.OutboxEventIntegracion (OutboxEventId, Integracion, Estado) "
            "VALUES (?, 'DRIVE', 'PENDIENTE')",
            outbox_event_id,
        )
        conn.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conn:
        cursor = conn.cursor()
        with pytest.raises(pyodbc.IntegrityError):
            cursor.execute(
                "UPDATE fact.OutboxEventIntegracion SET Clasificacion = 'NO_EXISTE' "
                "WHERE OutboxEventId = ? AND Integracion = 'DRIVE'",
                outbox_event_id,
            )
            conn.commit()


def test_seed_correo_destinatarios_existe_con_valor_null(worker_db):
    with pyodbc.connect(worker_db["worker_connection_string"]) as conn:
        cursor = conn.cursor()
        cursor.execute(
            "SELECT Tipo, Valor, ValorPorDefecto FROM fact.Configuracion "
            "WHERE Seccion = 'CORREO' AND Clave = 'DESTINATARIOS'"
        )
        fila = cursor.fetchone()
        assert fila is not None
        assert fila[0] == "LISTA"
        assert fila[1] is None
        assert fila[2] is None
