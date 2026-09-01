# Design — ZixCafe Pro

<!-- impeccable:design-schema 1 -->

## Direction

**Locked via user brief (beats concept roll).** "Luxury esports ops desk": a
matte-black operations surface where gold is the only precious metal. The
reference is not a sci-fi HUD and not a neon gaming rig — it is a private
members' club desk that happens to run a hundred terminals. Restraint is the
luxury: one accent, hairline borders, no glows, no gradients-as-decoration.
Gold appears where money and attention live; everything else recedes into
warm blacks so the floor's status reads at a glance from across the desk.

## World Language

- **Material:** matte, not glossy. Panels are flat warm blacks separated by
  1px hairlines, never drop-shadow stacks. Elevation is borders, not blur.
- **Light:** gold is light. Where the eye must land (active session countdown,
  primary CTA, selection rail), gold is present. Nowhere else.
- **Type as interface:** Chakra Petch for the voice of the system (headers,
  wordmark, section labels — squared, technical, athletic), IBM Plex Sans for
  human reading (body, forms, chat), JetBrains Mono for anything numeric that
  ticks (countdowns, money, metered usage). Digits never shift layout: all
  ticking numbers render in mono with tabular alignment.
- **Signature element — the Rack Tile:** the terminal card. Matte Panel
  surface, PC name in Chakra Petch 13px caps, status pill (always text + dot),
  JetBrains Mono countdown in Gold when running, 2px GoldDeep left rail when
  active or selected, 2s opacity pulse on the rail only while a session is
  live. At 100+ tiles this is the product's face; it must stay calm.
- **Client lock screen shares the system:** the customer's countdown screen is
  the same Rack Tile language at full-screen scale — big mono timer, quiet
  instructions, announced remote actions as inline banners.

## Palette (locked)

| Token | Hex | Role |
| --- | --- | --- |
| Void | #0C0A09 | App background, the deepest surface |
| Panel | #1C1917 | Cards, tiles, sidebars |
| Raised | #292524 | Hover/pressed surfaces, inputs |
| Line | #44403C | Hairline borders, dividers |
| Ink | #FAFAF9 | Primary text |
| Ghost | #A8A29E | Secondary text — never below 12px on Panel |
| Gold | #FBBF24 | Accent, active countdown, primary CTA fill (black text on gold) |
| GoldDeep | #D97706 | Selection rail, pressed gold, focus ring |
| Run | #22C55E | Session running, success |
| Warn | #F97316 | Warnings — hue-split from Gold to avoid collision |
| Alert | #EF4444 | Errors, lockouts, critical hardware |

Rules: black text (#0C0A09) on any gold fill (~10:1). Status is never
color-only — every color pairs with a text label. Warn is orange, distinct in
hue from Gold's yellow. No other colors enter the system; charts use Ink/Ghost
plus at most one accent.

## Type (locked, all OFL)

| Face | Weight | Use |
| --- | --- | --- |
| Chakra Petch | 600 | Display: headers, section labels, wordmark, tile names |
| IBM Plex Sans | 400 / 500 | Body: forms, chat, reports, everything long-form |
| JetBrains Mono | 500 | Data: countdowns, money, metrics, IDs, log lines |

Scale: Display 20 / Title 16 / Body 14 / Small 12 / Micro 11 (caps labels,
Ghost minimum). Bundled as TTF via pack URIs; skeleton ships with composite
fallback strings (`Chakra Petch, Segoe UI`), fonts land before release.

## Geometry

- Spacing scale: 4 / 8 / 12 / 16 / 24 / 32. Nothing off-scale.
- Radius: 8 on tiles and panels, 4 on inputs and buttons, 999 on pills.
- Elevation: borders only. Raised surface + Line border = one step up. No
  drop shadows on tiles; a shadow is allowed only on the single topmost modal.
- Density: the rack grid is the densest view; tiles target a 3-line minimum
  readable state at 100-terminal virtualized grids. Lists beyond 50 rows
  virtualize.

## Motion

- Rail pulse: 2s opacity loop, active sessions only, nothing else pulses.
- Transitions: 120–160ms ease-out on hover/press; no entrance choreography on
  the dashboard — an ops desk must feel instant.
- Countdown updates: text swap in place, no flip/fade per tick at 1Hz.

## Accessibility

- WCAG AA text contrast everywhere; Ghost (#A8A29E) is the floor and never
  under 12px on Panel.
- Every status pill: color dot + text label.
- Keyboard: tab order follows cashier flow (rack → actions → POS), every
  repeated flow has an accelerator; focus ring is 2px GoldDeep.
- High Contrast: SystemColors fallback map ships with the theme dictionaries.

## Anti-Pattern Register (standing bans)

1. No decorative charts; every chart earns its pixels with live data.
2. No hidden error states — failures render, name themselves, offer recovery.
3. No color-only status anywhere, including logs and hardware panels.
4. No gradients, glows, or neon bloom on black; matte only.
5. No modal during an active rush that can be an inline panel instead.
6. No layout-shifting tickers; mono + tabular numerals only.
7. No silent surveillance: screen view/chat always announced client-side.
8. Demo/sample data must be visibly labeled; never styled to look real.

## Surfaces

1. **Server dashboard** (WPF): rack grid (home), session inspector, POS panel,
   members, inventory, reports, settings. Rack grid is the launch view.
2. **Client lock screen** (WPF): full-screen countdown, redeem code entry,
   chat drawer, announced-action banners.
3. **LAN web dashboard** (later phase): read-only monitoring mirror of the
   rack — the only surface the detector can audit automatically.
