# Inbox Screen Specification

## Purpose

An Angular read-only screen surfaces every processed document's outcome — promoted, pending
manual review (discarded), or failed — with the 5 computed indicator flags as visual cues, so a
human can triage the inbox without querying the database directly. Manual actions are out of scope
(item #13).

## Requirements

### Requirement: List all InboxEvent outcomes

The system MUST list every `InboxEvent` sourced from the API, showing outcome (`PROMOVIDO` /
`DESCARTADO` / `PENDIENTE`) and, when promoted, the linked `Factura` summary with its 5 computed
indicator flags as chips (`EsReferenciaExterna` keeps its DDL default and is not computed here).

#### Scenario: Promoted document shows Factura summary and indicators

- GIVEN an `InboxEvent` with `EstadoConsumo='PROMOVIDO'` and a linked `Factura`
- WHEN the Inbox screen loads
- THEN the row displays the Factura's key fields and renders the 5 computed indicator flags as
  chips

#### Scenario: Discarded document shows the discard reason

- GIVEN an `InboxEvent` with `EstadoConsumo='DESCARTADO'` and a `MotivoDescarte`
- WHEN the Inbox screen loads
- THEN the row displays the outcome as pending manual review with the `MotivoDescarte` text

### Requirement: Filter by outcome

The system MUST allow filtering the list by `EstadoConsumo` (promoted / discarded / pending).

#### Scenario: Filtering to discarded only

- GIVEN the Inbox screen is showing all outcomes
- WHEN the user selects the "discarded" filter
- THEN only rows with `EstadoConsumo='DESCARTADO'` remain visible

### Requirement: Sort by fecha

The system MUST allow sorting the list by date (ascending/descending).

#### Scenario: Sorting newest first

- GIVEN the Inbox screen is showing the default order
- WHEN the user selects sort-by-fecha descending
- THEN rows are ordered from most recent to oldest

### Requirement: Read-only except the reprocesar action

The system MUST NOT expose approve, edit, discard, or any manual action on Inbox items other than
`reprocesar`. `inbox-list.ts`'s presentational contract is relaxed narrowly to allow exactly one
action — `reprocesar`, gated by confirmation — on rows where it applies (`INCIDENCIA` rows and
already-promoted `FACTURA` rows with error history). No other action is added by this item; those
remain #12's territory (`detalle-page`).
(Previously: "The system MUST NOT expose approve, edit, or re-trigger actions on Inbox items in
this item; those belong to item #13" — this item is #13, and reprocesar is now in scope.)

#### Scenario: No unrelated action controls are rendered

- GIVEN the Inbox screen is displaying any row
- WHEN the user views the row
- THEN no approve/edit/discard control is rendered for that row

#### Scenario: Reprocesar control renders only where applicable

- GIVEN an `INCIDENCIA` row or an already-promoted `FACTURA` row with error history
- WHEN the row renders
- THEN a reprocesar control is shown; rows without either condition render no reprocesar control

### Requirement: State management without a library

The Inbox screen SHALL manage its state with Angular signals only, with no external state-
management library, consistent with existing SPA conventions.

#### Scenario: List state is signal-driven

- GIVEN the Inbox component fetches data from the API
- WHEN the response arrives
- THEN the component updates an Angular signal holding the list, with no third-party state store
  involved

### Requirement: Filter inputs for date range and proveedor

`ui/inbox-filter/` MUST expose `desde`, `hasta`, and `proveedor` inputs in addition to the
existing `estado`/`orden` controls, and `inbox.service.ts` MUST pass them through to
`GET /api/bandeja` unmodified.

#### Scenario: Combined filters are sent as-is
- GIVEN the user sets `estado`, `desde`, `hasta`, and `proveedor`
- WHEN the filter is applied
- THEN the request to `GET /api/bandeja` includes all four parameters with the user's values

### Requirement: Panel de errores renders ProcesamientoError history

`inbox-list.ts` MUST render a panel de errores (`Mensaje`, `Clasificacion`, `OcurridoEn`) for any
row whose response includes error history, regardless of `origen`.

#### Scenario: INCIDENCIA row with multiple errors shows all entries
- GIVEN a row's error history has more than one `fact.ProcesamientoError` entry
- WHEN the row renders
- THEN the panel de errores lists every entry

#### Scenario: FACTURA row without error history renders no panel
- GIVEN a `FACTURA` row whose error-history field is empty or absent
- WHEN the row renders
- THEN no panel de errores element is rendered for that row (not an empty/broken panel)

### Requirement: Reprocesar requires confirmation and is gated by a pending-command window

The reprocesar control MUST show an explicit confirmation step before calling
`POST /api/incidencias/{id}/reprocesar` (`{id}` = `ProcesamientoId`). The control MUST be disabled
while a `CommandQueue` row for that `ProcesamientoId` is pending, and MUST re-enable after a fixed
5-minute timeout from the enqueue time, independent of whether the command has been claimed.

#### Scenario: Confirmation blocks accidental reprocesar
- GIVEN a user clicks reprocesar on an applicable row
- WHEN the confirmation dialog is shown and the user cancels
- THEN no request is sent to `POST /api/incidencias/{id}/reprocesar`

#### Scenario: Confirmed reprocesar disables the control
- GIVEN a user confirms reprocesar
- WHEN the request succeeds and enqueues a `CommandQueue` row
- THEN the control becomes disabled for that row

#### Scenario: Control stays disabled while the command is still pending
- GIVEN a `CommandQueue` row for that `ProcesamientoId` was enqueued less than 5 minutes ago
- WHEN the row re-renders
- THEN the reprocesar control remains disabled

#### Scenario: Control re-enables after the 5-minute timeout
- GIVEN a `CommandQueue` row for that `ProcesamientoId` was enqueued more than 5 minutes ago
- WHEN the row re-renders
- THEN the reprocesar control becomes enabled again, even if the command has not been claimed
