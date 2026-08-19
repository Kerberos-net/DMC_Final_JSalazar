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

### Requirement: Read-only in this item

The system MUST NOT expose approve, edit, or re-trigger actions on Inbox items in this item; those
belong to item #13.

#### Scenario: No manual-action controls are rendered

- GIVEN the Inbox screen is displaying a discarded item
- WHEN the user views the row
- THEN no approve/edit/re-trigger control is rendered for that row

### Requirement: State management without a library

The Inbox screen SHALL manage its state with Angular signals only, with no external state-
management library, consistent with existing SPA conventions.

#### Scenario: List state is signal-driven

- GIVEN the Inbox component fetches data from the API
- WHEN the response arrives
- THEN the component updates an Angular signal holding the list, with no third-party state store
  involved
