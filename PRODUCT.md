# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

Note: the `web` value governs the optional LAN web dashboard — the only surface
impeccable's web pipeline (screenshots, detector, live mode) can inspect. The
primary surfaces are two Windows desktop applications (Server dashboard + Client
agent, WPF, .NET 8), which sit outside impeccable's web/native-mobile scope; the
craft floor is applied to them by hand and the design detector does not run on
XAML.

## Stack

User-pinned brief: C# / .NET 8 (LTS) for both binaries. Server dashboard: WPF
(MVVM, CommunityToolkit.Mvvm) hosting an ASP.NET Core Kestrel + SignalR hub
in-process. Client agent: WPF locked UI + a Windows Service watchdog. Data:
SQLite via EF Core 8 (WAL). Real-time: SignalR (WebSockets) on the LAN.
Hardware: LibreHardwareMonitorLib (Phase 4). Packaging: WiX 5.

## Users

- Cashier/front-desk operator at a gaming lounge or internet cafe: runs
  sessions, sells time and products, answers chat, in a dim hall, on one
  screen, for full shifts.
- Manager/owner: configures tariffs and policies, audits cash and activity,
  needs tamper-evident history without becoming a DBA.
- Customer on a locked client terminal: redeems time, sees their countdown and
  balance, asks for help. Not a user of the dashboard.

## Product Purpose

ZixCafe Pro manages a paid PC venue end to end: it locks down customer
terminals, meters and bills session time against four tariff models, sells
cafeteria stock and print/USB services to the active session tab, monitors
hardware health, and produces shift and accounting reports. Success is: no
un-billed minute, no escaped terminal, no silent failure, and a cashier who
never needs training to run a shift.

## Positioning

Ops-grade engine with a luxury-esports identity: the reliability machinery
(typed contracts, signed commands, hash-chained audit log, watchdog) of an
operations product wearing a matte-black/gold brand that venue owners want on
their floor. PanCafe Pro's feature surface with a positioning PanCafe cannot
copy.

## Operating Context

- LAN-only deployment: one server PC (cashier desk), 40-100+ customer
  terminals, no cloud dependency. Engineered for the large venue by default.
- Ambient light is low; screens run long sessions; glare and burn-in risks are
  real.
- Customers actively try to escape the kiosk; cashier staff may be rotating and
  minimally trained, so destructive actions sit behind manager PIN overrides.
- Money is involved every minute; the system must survive power cuts mid
  session (SQLite WAL) and reconcile on reconnect.

## Capabilities and Constraints

- Sessions: prepaid time codes, member accounts with time and money balances,
  walk-up postpaid sessions, pause/resume with policy.
- Tariffs: flat rate, tiered time-of-day, day-of-week schedules, member tier
  discounts; per-minute billing with configurable rounding and minimum charge.
- Peripherals: print billing per page, USB data transfer metering (accuracy
  +-1% is the honest ceiling; the UI states it, it does not imply exactness).
- Remote ops: on-demand screen view (never silent surveillance — always
  announced on the client), chat, restart/shutdown, process kill of prohibited
  apps, wake-on-LAN (wired only).
- Reality constraints: Ctrl+Alt+Del cannot be blocked from user mode; the
  kiosk model is restricted auto-logon + shell replacement + registry policies,
  and the docs say so plainly. Anti-cheat/DeepFreeze interference is detection
  and reporting only, never silently disabling the customer's software.
- Offline resilience: terminals keep a signed countdown cache; reconnect
  reconciles drift; the server is the single source of truth.

## Brand Commitments

- Name: ZixCafe Pro. Wordmark and mark are authored in-repo (gold on matte
  black), used in both apps and the installer.
- Direction is brief-pinned and locked: Void #0C0A09, Panel #1C1917, Raised
  #292524, Line #44403C, Ink #FAFAF9, Ghost #A8A29E, Gold #FBBF24, GoldDeep
  #D97706, Run #22C55E, Warn #F97316, Alert #EF4444.
- Type: Chakra Petch 600 display, IBM Plex Sans 400/500 body, JetBrains Mono
  500 data. All OFL; shipped as bundled TTFs before release, composite fallback
  strings in the skeleton.
- Signature element: the Rack Tile — matte-black card, GoldDeep 2px left rail
  when active/selected, status pill that always carries a text label, Gold mono
  countdown, 2s pulse only while the session is live.
- Currency/locale: configurable at first-run setup; money is stored as decimal
  and formatted per venue locale.

## Evidence

None yet. There is no benchmark, uptime claim, or user quote anywhere in the
product. Demo data, when seeded, must be visibly labeled as sample data.

## Product Principles

1. The cashier's floor view is the product: one screen, glance distance,
   keyboard-first, no modal traps during a rush.
2. Money is never approximated in silence: every charge is itemized, every
   adjustment is attributable, the audit log is hash-chained.
3. Honest surfaces: status is text plus color, errors name the problem and the
   recovery, limitations are stated in the UI not just the docs.
4. The terminal is not the cashier's enemy: lock the kiosk, not the person;
   every remote action is announced to the customer.
5. Performance is a feature at 100 terminals: the rack stays responsive during
   an all-terminal rush, and that is a release gate.

## Accessibility

- Contrast floor: all text passes WCAG AA against its surface; the gold-on-
  black pairing is chosen for that reason (~10:1 on fills).
- Status is never color-only; every pill, rail, and light has a text label.
- Keyboard paths exist for every cashier flow used more than once per shift.
- SystemColors fallback map for High Contrast mode is part of the theme, not an
  afterthought.

## Undecided Facts

- Product licensing/activation scheme (how the venue pays for ZixCafe itself).
- Payment gateway integrations (QR payments stubbed to manual confirmation).
- Languages beyond English; the locale layer is built but translations are not
  scheduled.
