# Delta for spa-theme-toggle

Revises archived `2026-08-24-diseno-visual-spa-item-12`.

## ADDED Requirements

### Requirement: Theme control remains a native `<select>` in this change

The system MUST keep the theme control as a native `<select>` element in the app shell header. The handoff's sun/moon icon toggle and any sidebar navigation redesign are explicitly out of scope for item #18 (user decision 5).

#### Scenario: Theme control element type

- GIVEN the app shell header renders the theme control
- WHEN its DOM is inspected
- THEN the control is a `<select>` element with options for light, dark, and system

#### Scenario: No sidebar theme toggle introduced

- GIVEN the SPA after this change
- WHEN the layout is inspected
- THEN no sidebar sun/moon "Apariencia" toggle is added
