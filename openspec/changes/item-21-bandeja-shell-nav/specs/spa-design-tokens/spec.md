# Delta for SPA Design Tokens

## No delta required

No `spa-design-tokens` change is required by this change at spec time.

- The sidebar surface token `--fondo-sidebar` already exists (see "Four-level
  surface hierarchy").
- Sidebar text, glyphs, active state, and the divider resolve from existing
  global text/accent/hairline tokens.
- The obligation that these pass WCAG AA over `--fondo-sidebar` in both themes is
  carried by the `spa-shell-nav` requirement "Sidebar text and affordances meet
  WCAG AA over the sidebar surface".

If, and only if, implementation finds an existing text token failing AA over
`--fondo-sidebar`, a dedicated "text on sidebar" token MUST then be added to the
light and dark theme blocks, to the `contraste.spec.ts` assertion array, and
covered by `paleta.spec.ts` theme-parity — with no new hue literal (reuse an
existing ink / derived value). In that case this file is replaced with an ADDED
Requirement delta.
