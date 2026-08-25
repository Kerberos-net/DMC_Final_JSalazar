# Configuración API + SPA Specification

## Purpose

Expose `fact.Configuracion` (thresholds, Telegram destination, credentials-adjacent operational
settings) for read/write through a dedicated .NET endpoint file and an Angular screen, so operators
no longer need a manual `UPDATE`.

## Requirements

### Requirement: Dedicated configuration endpoints
The system MUST expose `fact.Configuracion` read/write through a new `ConfiguracionEndpoints.cs`,
separate from `IntegracionEndpoints.cs`.

#### Scenario: Read configuration by section
- GIVEN `fact.Configuracion` has entries under a section (e.g. `TELEGRAM`)
- WHEN `GET /api/configuracion/{seccion}` is called with a valid session
- THEN it returns the section's key/value entries

#### Scenario: Write configuration value
- GIVEN a valid session and an existing `Configuracion` key
- WHEN `PUT /api/configuracion/{seccion}/{clave}` is called with a new value
- THEN the stored value is updated and reflected on the next read

### Requirement: Per-Tipo validation on write
The system MUST validate a written value against the key's declared `Tipo` before persisting and
MUST reject invalid values.

#### Scenario: Invalid value rejected
- GIVEN a `Configuracion` key declared with a numeric `Tipo`
- WHEN a non-numeric value is submitted
- THEN the write is rejected with a validation error
- AND the previously stored value remains unchanged

#### Scenario: Valid value accepted
- GIVEN a `Configuracion` key declared with a numeric `Tipo`
- WHEN a numeric value is submitted
- THEN the write succeeds

### Requirement: Authenticated access only
Configuration endpoints MUST reject any request without a valid session (Flujo 6 rule extended to
this new endpoint file).

#### Scenario: Unauthenticated request rejected
- GIVEN no valid session cookie is present
- WHEN a configuration endpoint is called
- THEN the request is rejected

### Requirement: Angular configuración screen
The system MUST provide an Angular `configuracion/` feature with a data-access layer that lists
and edits sections/keys from the configuration endpoints, with changes taking effect for
subsequent operations without redeploy.

#### Scenario: Operator edits Telegram destination
- GIVEN the operator opens the configuración screen
- WHEN they change `TELEGRAM.DESTINO_CHAT_ID` and save
- THEN the SPA calls the write endpoint
- AND the next notification uses the updated chat id without a redeploy

#### Scenario: Screen surfaces a rejected write
- GIVEN the operator submits an invalid value for a typed key
- WHEN the API rejects it
- THEN the screen displays the validation error and does not show the invalid value as saved
