---
name: macOS Ledger Blue
colors:
  surface: '#f5f5f7'
  surface-dim: '#eef0f3'
  surface-bright: '#ffffff'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#fbfbfc'
  surface-container: '#ffffff'
  surface-container-high: '#eef0f3'
  surface-container-highest: '#e4e6ea'
  on-surface: '#1d1d1f'
  on-surface-variant: '#6e6e73'
  inverse-surface: '#1c1c1e'
  inverse-on-surface: '#f5f5f7'
  outline: '#86868b'
  outline-variant: 'rgba(0,0,0,0.08)'
  surface-tint: '#0071e3'
  primary: '#0071e3'
  on-primary: '#ffffff'
  primary-container: 'rgba(0,113,227,0.08)'
  on-primary-container: '#0071e3'
  inverse-primary: '#0a84ff'
  secondary: '#6e6e73'
  on-secondary: '#ffffff'
  secondary-container: '#fbfbfc'
  on-secondary-container: '#6e6e73'
  tertiary: '#86868b'
  on-tertiary: '#ffffff'
  tertiary-container: '#eef0f3'
  on-tertiary-container: '#6e6e73'
  error: '#d70015'
  on-error: '#ffffff'
  error-container: 'rgba(255,59,48,0.1)'
  on-error-container: '#d70015'
  success: '#1f8a3d'
  on-success: '#ffffff'
  success-container: 'rgba(52,199,89,0.13)'
  on-success-container: '#1f8a3d'
  warning: '#c93400'
  on-warning: '#ffffff'
  warning-container: 'rgba(255,149,0,0.13)'
  on-warning-container: '#c93400'
  background: '#f5f5f7'
  on-background: '#1d1d1f'
  surface-variant: '#eef0f3'
  dark-surface: '#1c1c1e'
  dark-surface-container: '#2c2c2e'
  dark-surface-container-low: '#242426'
  dark-surface-container-high: '#232326'
  dark-on-surface: '#f5f5f7'
  dark-on-surface-variant: '#98989d'
  dark-outline: 'rgba(255,255,255,0.09)'
  dark-primary: '#0a84ff'
typography:
  display-lg:
    fontFamily: -apple-system
    fontSize: 28px
    fontWeight: '800'
    lineHeight: 34px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: -apple-system
    fontSize: 22px
    fontWeight: '700'
    lineHeight: 28px
    letterSpacing: -0.01em
  title-lg:
    fontFamily: -apple-system
    fontSize: 15px
    fontWeight: '600'
    lineHeight: 20px
  title-md:
    fontFamily: -apple-system
    fontSize: 13px
    fontWeight: '600'
    lineHeight: 18px
  body-lg:
    fontFamily: -apple-system
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 18px
  body-md:
    fontFamily: -apple-system
    fontSize: 12.5px
    fontWeight: '400'
    lineHeight: 17px
  label-sm:
    fontFamily: -apple-system
    fontSize: 11px
    fontWeight: '700'
    lineHeight: 14px
    letterSpacing: 0.05em
  code-md:
    fontFamily: -apple-system
    fontSize: 12.5px
    fontWeight: '500'
    lineHeight: 17px
rounded:
  sm: 0.375rem
  DEFAULT: 0.5rem
  md: 0.625rem
  lg: 0.75rem
  xl: 0.875rem
  full: 9999px
spacing:
  base: 2px
  xs: 6px
  sm: 8px
  md: 14px
  lg: 18px
  xl: 22px
  gutter: 12px
  margin-mobile: 16px
  margin-desktop: 20px
  max-width: none
---

## Brand & Style
The design system channels native **macOS** conventions into a back-office accounting tool. It favors quiet, utilitarian chrome — a light sidebar, hairline borders, and a single confident blue accent — over decorative UI, so attention stays on the data: invoice status, amounts, and account balances. The personality is precise, calm, and trustworthy, built for an accountant who lives in this screen for hours.

The style is **Minimal / Native-OS**, not corporate-brand-forward. Every surface reads as "system," not "marketing": no gradients, no illustration, no emoji, no drop shadows outside modals. Density is moderate-to-high (12.5–13px body text, tight table rows) because the primary user is a power user optimizing for throughput, not a first-time visitor.

## Colors
The palette is intentionally restrained: one neutral scale (`surface` family) plus one accent (`primary`, macOS blue) and four semantic colors reserved exclusively for invoice/asiento status.

- **Primary (`#0071e3` / dark `#0a84ff`):** validate/submit buttons, active nav item, active filter chip, links, focus accents. Never used decoratively.
- **Neutral (`surface-*`, `on-surface-*`):** page background, card surfaces, borders, and all body/label text. Three surface levels only — page bg, card bg, and a slightly recessed `surface-container-low` for nested groups (table headers, stat sub-rows).
- **Semantic:** `success` (validada / conectado), `error` (error / con error), `warning` (alerta / pendiente de validación). Each is used as a ~10–16% tint background with the solid tone as text/icon color — status pills, category dots, connection badges.
- **Dark mode** is a first-class palette (not a filter): every token above has a literal dark counterpart; accent shifts one step brighter (`#0071e3`→`#0a84ff`) to stay legible on near-black surfaces.

## Typography
System font stack (`-apple-system, BlinkMacSystemFont, "SF Pro Text"`) — no custom webfont, matching the native-OS goal.

- **Display (28px/800):** single page-level heading per screen (e.g. "Bandeja principal"), tight letter-spacing.
- **Headline (22px/700):** secondary page titles (Registro de compra, Proveedores, Plan contable).
- **Title (13–15px/600):** card headers, modal titles, section labels.
- **Body (12.5–13px/400):** table cells, form values, descriptions — the workhorse size.
- **Label (11px/700, uppercase, +0.05em):** table column headers, stat-card eyebrows.
- **Tabular data:** any amount, rate, date, or code sets `font-variant-numeric: tabular-nums` so columns of numbers align.

## Layout & Spacing
A **fixed-sidebar + fluid-content** shell, not a grid system — this is a data application, not a marketing page.

- **Sidebar:** 216px expanded / 60px collapsed, icon+label items, one hairline divider before the utility group (Sincronización, Configuración).
- **Rhythm:** a tight 2/6/8/14/18/22px scale drives gaps and padding; table rows use 11px vertical padding for density, cards use 18px.
- **Tables over grids:** primary content is CSS-grid table rows with fixed px column tracks plus one flexible `minmax(…,1fr)` column (Proveedor/Razón social); horizontal scroll is acceptable, wrapping is not.
- **Max width:** none — content fills the available pane; forms and modals self-constrain (420–620px) instead.

## Elevation & Depth
Depth is almost entirely **borders, not shadows** — consistent with flat native-OS chrome.

- **Level 0 (page):** `surface` background, no border.
- **Level 1 (cards, table containers, inputs):** `surface-container` background + 1px `outline-variant` hairline border, 10–12px radius, zero shadow.
- **Level 2 (popovers, dropdown pickers):** same card token + a real shadow (`0 12px 30px rgba(0,0,0,.25)`) since these float above content and need separation.
- **Level 3 (modals):** dark scrim (`rgba(0,0,0,.4)`) + large shadow (`0 24px 70px rgba(0,0,0,.35)`) on the dialog card — the only place shadow does the work borders can't.

## Shapes
Rounded but restrained — closer to macOS window chrome than to a consumer app.

- **Standard elements** (buttons, inputs, small icon-buttons): 8px radius.
- **Cards / table containers / modals:** 10–14px radius.
- **Pills** (status badges, filter chips, avatar-adjacent counters): full/20px radius.
- **Avatars / toggles / dots:** fully round.

## Components
- **Buttons:** primary = solid `primary`, white text, 600 weight, no border; secondary = card surface + hairline border; destructive = transparent + `error` border/text. All 12.5px, 7–9px vertical padding.
- **Status pill:** semantic tint background + solid semantic text, 3px/9-10px padding, full radius, 600 weight — the single vocabulary for Pendiente/Validada/Descartada, the derived attention pill, and Conectado/Con error everywhere in the app.
- **Attention pill:** the same pill component, but it is **derived**, never stored. Six invoice indicators raise it — generic supplier, possible duplicate, unextracted fields, Sunday issue date, unverified tax treatment (PDF-only document), external reference (credit note against a pre-system invoice). Hovering or opening the row must reveal *which* indicator fired: a pill that only says "needs attention" forces the user to hunt for the reason.
- **Conflict vs. validation error:** these are two different messages and must not share a treatment. A stale-record conflict ("someone else changed this, reload") is recoverable by reloading; a rule violation ("this value is not allowed") requires the user to change the data.
- **Filter chip / segmented tab:** pill-shaped; active = solid `primary` bg + white text, inactive = card bg + hairline border + `on-surface` text. Used for status filters, sync-provider tabs, category filters.
- **Table:** grid-based rows, uppercase 11px label header row on `surface-container-low`, hairline row dividers, tabular-nums for numeric columns, inline pencil/trash icon actions (20×20px hit targets), avatar-initial circle before the primary entity name.
- **Toggle switch:** 34×20px rounded track, `primary` when on / `outline-variant` when off — no shadow, no knob animation drawn (flat state indicator).
- **Search / lookup field:** plain bordered input; where the option set is large (cuenta contable, proveedor), replaced by a text-filtered dropdown panel (Level 2) instead of a native `<select>`.
- **Modal:** centered card, title + circular × close button, internal grouping via `surface-container-low` sub-panels, primary action bottom-right pattern (Cerrar / destructive / primary in a row).
- **Icons:** no icon font or SVG — every glyph is hand-built from `<div>` borders/transforms (clip, pencil, trash, gear, magnifier, sun/moon, circular sync arrows), always inheriting `currentColor` so nav/active states recolor for free.
