"""BACKLOG #17, Fase 3, tasks.md 3.6 -- `config.obtener_credenciales_telegram_json`/
`obtener_credenciales_smtp_json`, mismo contrato que `obtener_credenciales_gmail_json`: sin
default en codigo, `ConfiguracionError` si la variable no esta definida o no es JSON valido."""

from __future__ import annotations

import pytest

from smartnet_worker import config


def test_credenciales_telegram_lanza_si_env_ausente(monkeypatch):
    monkeypatch.delenv(config.TELEGRAM_CREDENTIALS_ENV_VAR, raising=False)
    with pytest.raises(config.ConfiguracionError):
        config.obtener_credenciales_telegram_json()


def test_credenciales_telegram_lanza_si_json_invalido(monkeypatch):
    monkeypatch.setenv(config.TELEGRAM_CREDENTIALS_ENV_VAR, "{no-es-json")
    with pytest.raises(config.ConfiguracionError):
        config.obtener_credenciales_telegram_json()


def test_credenciales_telegram_parsea_json_valido(monkeypatch):
    monkeypatch.setenv(config.TELEGRAM_CREDENTIALS_ENV_VAR, '{"bot_token": "abc"}')
    assert config.obtener_credenciales_telegram_json() == {"bot_token": "abc"}


def test_credenciales_smtp_lanza_si_env_ausente(monkeypatch):
    monkeypatch.delenv(config.SMTP_CREDENTIALS_ENV_VAR, raising=False)
    with pytest.raises(config.ConfiguracionError):
        config.obtener_credenciales_smtp_json()


def test_credenciales_smtp_parsea_json_valido(monkeypatch):
    valor = (
        '{"host": "smtp.x.com", "port": 587, "usuario": "u", "password": "p", '
        '"remitente": "r@x.com"}'
    )
    monkeypatch.setenv(config.SMTP_CREDENTIALS_ENV_VAR, valor)
    creds = config.obtener_credenciales_smtp_json()
    assert creds["host"] == "smtp.x.com"
    assert creds["port"] == 587
