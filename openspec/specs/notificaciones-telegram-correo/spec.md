# Notificaciones Telegram/Correo Specification

## Purpose

Alert operators of classified errors through Telegram, with SMTP fallback when Telegram fails,
triggered per error class, both attempts always logged. Closes ADR 0010's "no proactive alert
channel" gap.

## Requirements

### Requirement: Per-class notification trigger
The system MUST trigger a notification attempt according to error class: TRANSITORIO only on
retry exhaustion (within 5 minutes of exhaustion), PERMANENTE immediately, DIFERIBLE once on entry
(not on exhaustion), OBSOLETO never.

#### Scenario: Permanente notifies immediately
- GIVEN an error is classified PERMANENTE
- WHEN classification completes
- THEN a notification attempt starts immediately

#### Scenario: Transitorio notifies only after exhaustion
- GIVEN a TRANSITORIO error is still within its 3 retry attempts
- WHEN a retry attempt fails but retries remain
- THEN no notification is sent
- AND once retries are exhausted, a notification is sent within 5 minutes

#### Scenario: Diferible notifies once on entry
- GIVEN an error is classified DIFERIBLE
- WHEN it enters the DIFERIBLE state
- THEN exactly one notification attempt is sent
- AND no further notification is sent when the deferred retry later succeeds or fails

#### Scenario: Obsoleto never notifies
- GIVEN an error is classified OBSOLETO
- WHEN classification completes
- THEN no notification attempt occurs

### Requirement: Telegram-primary with SMTP fallback, dual-attempt logging
The system MUST attempt Telegram first for a triggered notification; if the Telegram attempt
fails, it MUST fall back to SMTP email; both attempts MUST be logged regardless of outcome.

#### Scenario: Telegram succeeds
- GIVEN a notification is triggered
- WHEN the Telegram attempt succeeds
- THEN the attempt is logged as successful
- AND no SMTP fallback attempt occurs

#### Scenario: Telegram fails, email fallback sent
- GIVEN a notification is triggered
- WHEN the Telegram attempt fails
- THEN an SMTP email is sent as fallback
- AND both the failed Telegram attempt and the email attempt are logged

### Requirement: Single global Telegram destination
The system MUST send Telegram notifications to a single, globally configured chat
(`TELEGRAM.DESTINO_CHAT_ID` in `fact.Configuracion`), with no per-integration or per-severity
routing.

#### Scenario: All notifications go to the configured chat
- GIVEN `TELEGRAM.DESTINO_CHAT_ID` is configured
- WHEN any triggered notification is sent via Telegram
- THEN it is sent to that single configured chat, regardless of integration or error class

### Requirement: Credentials via environment convention
The system MUST read Telegram bot token and SMTP credentials via the existing
`smartnet_worker/config.py` environment-variable convention, consistent with Gmail/Drive/Sheets.

#### Scenario: Credentials loaded from environment
- GIVEN the worker process starts with Telegram/SMTP environment variables set
- WHEN a notification is sent
- THEN the notifier uses those environment-sourced credentials
- AND no credential is read from a source outside `config.py`'s convention
