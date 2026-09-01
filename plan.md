# ZixCafe Pro - Remaining Work Plan (A to Z)

Status snapshot: Phases 1-3 are built and smoke-verified (72/72 checks, 16/16 domain tests).
Built so far: pairing (code + secret), sessions (prepaid/postpaid/member/ticket start, pause/resume,
auto-end at time-up, itemized end), TariffEngine (all 4 models, unit-tested), ticket generate/redeem,
desk-terminal chat, cashier login, member find + time/money deduction + ledger, POS extras (stock
decrement + movements + oversell guard), shifts (open/close/drawer variance), waitlist (add/skip/seat),
item loans (loan/return/forfeit), Lock All (idle-only), rack UI + inspector + themes, kiosk guard +
watchdog service, SQLite WAL + hash-chained audit, EF migrations (InitialCreate, AddTerminalSecret,
AddSessionPausedAt).

Everything below is NOT yet implemented. Ordered roughly by dependency, then by value.

---

## A. Cashier roles + manager PIN overrides (security floor)

- [ ] Enforce CashierRole on the hubs: Staff runs sessions/POS/waitlist; Manager+ can void,
      adjust, refund, discount, close shift with variance, edit members; Owner manages
      cashiers/settings/tariffs.
- [ ] Hub methods use the authenticated cashier identity (mapped from connection), not free-form
      cashierName strings; free-form names remain only in audit detail.
- [ ] Manager PIN re-entry for destructive actions (PRODUCT.md): void line, cancel session,
      discount, forfeit loan, drawer adjustment, member balance edit.
- [ ] Audit "override" entries: who approved, what was approved.
- [ ] Smoke: staff blocked from manager action; override audit written.

## B. Cashier management UI (Settings tab)

- [ ] List/create/edit/disable cashiers; assign role; reset PIN (Owner only).
- [ ] Reuse SecretHasher for PINs (never plaintext).
- [ ] Disable prevents login; guard against disabling a cashier with an open shift.
- [ ] Smoke: create cashier, login, disabled login rejected.

## C. First-run setup wizard

- [ ] On empty DB: venue name, currency code + locale (drives all money formatting), tax
      label/rate, opening float default, admin cashier creation.
- [ ] New VenueSettings entity (single row); every money display reads from it.
- [ ] Dev seed flag skips wizard; demo data visibly labeled "SAMPLE" (DESIGN.md anti-pattern 8).
- [ ] Smoke: fresh DB -> wizard -> settings persisted -> sample data labeled.

## D. Tariff management (engine built, management missing)

- [ ] CRUD UI: Flat, Tiered (time-of-day bands), DaySchedule (per-weekday), MemberTier (discount %).
- [ ] Validation: no overlapping bands, base rate > 0, rounding >= 1.
- [ ] Tariff assignment: per terminal group or venue default + time-based selection in
      SessionService.StartAsync (currently uses seeded default only).
- [ ] Unit tests: band selection at boundaries; member tier discount composition.
- [ ] Smoke: session under Tiered night band bills a different rate.

## E. Sales + payments (Sale/SaleLine entities exist, unused)

- [ ] Standalone sale (no session): walk-up retail, split payment cash/card/QR (entity fields
      exist), change due.
- [ ] QR = manual confirmation stub (PRODUCT.md Undecided): "Mark QR paid" button, honest copy.
- [ ] Session-close payment: postpaid total = time + extras; tender, split, change; v1 policy =
      full payment required, partial blocked with clear error.
- [ ] Discounts: line + sale level, percent or amount, attributed (cashier + reason), manager
      override per A.
- [ ] Receipt: plain-text/printable receipt view (hardware printing is M).
- [ ] Adjustment SaleLine kind for manual corrections with mandatory note.
- [ ] Smoke: split payment; discount with override; adjustment audit.

## F. Ticket sales UI (engine exists, desk UI missing)

- [ ] Sell ticket: duration or credit, price from tariff, pay, show code + PIN, batch generate
      for counter resale.
- [ ] Ticket list: unused/used/expired; void (manager).
- [ ] Smoke: sell -> list -> void.

## G. Members UI (deduction works, management missing)

- [ ] CRUD members, code generation (M-#### pattern), tier assignment (drives MemberTier).
- [ ] Top-up time/money with receipt + MemberTransaction ledger entries.
- [ ] Freeze/flag member; per-member session history; debt view (ties to E).
- [ ] Smoke: top-up -> ledger; tier change -> next session bills at tier rate.

## H. Inventory UI (stock engine works, management missing)

- [ ] Product CRUD: name, price, category, stock, low-stock threshold.
- [ ] Restock / waste / adjust flows (StockReason exists) each writing StockMovement + audit.
- [ ] Low-stock Warning alert when a sale drops below threshold.
- [ ] Sales report per product (ties to J).
- [ ] Smoke: waste movement; low-stock alert raised.
## I. Peripheral services: print + USB metering

- [ ] Print job billing: per-page price, PrintStatus queue (Queued->Released->Printed/Failed),
      release at desk after payment; cancel with reason.
- [ ] USB transfer metering: MB counter via agent, LineKind.Usb line at close; UI copy states the
      +-1% honesty ceiling (PRODUCT.md).
- [ ] Agent side: print/USB event surface on TerminalHub (announced on client, anti-pattern 7).
- [ ] Smoke: metered USB line lands on session bill; print released->printed.

## J. Reports + audit surfaces

- [ ] X-report (mid-shift, read-only) and Z-report (on close): time revenue by mode, product sales,
      print/USB, discounts, adjustments, drawer math, variance.
- [ ] Daily/period revenue report with chart (Ink/Ghost + one accent; it is live data so it earns
      pixels per DESIGN.md).
- [ ] Session history browser with filters (terminal, mode, cashier, date).
- [ ] Audit log viewer with hash-chain verify button (walks chain, names first broken link).
- [ ] CSV export for money reports.
- [ ] Smoke: Z-report totals reconcile against seeded sessions; chain verify OK.

## K. Alerts center (currently MessageBox popups only)

- [ ] Persisted alert list panel (severity, kind, message, time, ack); acknowledge + mute per kind;
      unread badge in nav.
- [ ] Hardware-critical and offline-terminal alerts land here (ties to L/M/N).
- [ ] Smoke: ack persists; muted kind stops popup.

## L. Remote ops (announce-first)

- [ ] On-demand screen view: agent captures frame on request; client shows "Front desk is viewing
      this screen" banner for the duration; auto-stop timer. Never silent.
- [ ] Restart / shutdown terminal (confirm + announce + agent graceful close).
- [ ] Process kill of prohibited apps: prohibited list in settings; agent watches + reports kill
      events (audit "process.kill").
- [ ] Wake-on-LAN (wired only): magic packet from server; best-effort with honest status.
- [ ] Smoke: screen-view banner shown; kill event audited; WOL packet sent (loopback OK).

## M. Hardware integration (Phase 4 per stack note)

- [ ] LibreHardwareMonitorLib in agent: CPU/GPU temps, fan, uptime -> heartbeat payload; threshold
      breaches -> Critical alerts.
- [ ] Receipt printer (ESC/POS raw text) for receipts + Z-report; failure = honest error + fallback
      to screen receipt.
- [ ] Anti-cheat/DeepFreeze interference: detection + reporting only, never silently disabling the
      customer software (PRODUCT.md constraint).

## N. Offline resilience + reconnect reconciliation

- [ ] Terminal keeps signed countdown cache (extend TerminalStateStore with server signature) and
      keeps ticking during server outage.
- [ ] On reconnect: agent reports elapsed drift; server reconciles billing (server is source of
      truth), writes adjustment line if drift > policy threshold.
- [ ] Server restart mid-session: SessionMonitor recovers Active sessions on boot (verify + test).
- [ ] Smoke: kill server mid-session -> restart -> countdown resumes; drift reconciled.

## O. Kiosk hardening (implementation + honest docs)

- [ ] Restricted auto-logon + shell replacement + registry policies: configurable KioskGuard levels
      + plain README that Ctrl+Alt+Del cannot be blocked from user mode.
- [ ] Watchdog service: auto-restart killed agent; service status surfaced in desk alerts.
- [ ] Smoke: agent killed -> watchdog restarts; desk sees event.

## P. Rack performance gate (release gate, PRODUCT.md principle 5)

- [ ] Virtualized rack grid (VirtualizingPanel) for 100+ tiles.
- [ ] Perf test: seed 120 terminals, burst start/stop of 40 sessions, rack stays responsive;
      record measured numbers in Evidence.
- [ ] TimeSync batching per group if 1Hz-per-session saturates.
- [ ] Smoke: 120-tile seed + burst test, no dropped broadcasts under threshold.

## Q. Dashboard UX completeness (DESIGN.md keyboard + a11y)

- [ ] Accelerators for repeated flows: start (F2), pause/resume (F3), end (F4), POS add (F6), chat
      reply (F8), rack search (Ctrl+F); tab order rack -> actions -> POS.
- [ ] Focus ring 2px GoldDeep everywhere; SystemColors High Contrast fallback map in themes.
- [ ] Ghost text never < 12px on Panel; audit all XAML.
- [ ] Rack search/filter (name/status) + sort; density option.
- [ ] Empty states with recovery copy (e.g. no tariffs -> pointer to settings).

## R. Fonts + brand assets

- [ ] Bundle TTFs: Chakra Petch 600, IBM Plex Sans 400/500, JetBrains Mono 500 via pack URIs
      (composite fallbacks currently in place).
- [ ] Wordmark + mark (gold on Void) in both apps title bars / about.
- [ ] Tabular numerals verified on countdowns/money (no layout shift, anti-pattern 6).

## S. Reserved + Maintenance statuses (enums exist, unused)

- [ ] Reserved: book terminal for a time window; desk sees upcoming; optional auto-start.
- [ ] Maintenance: out-of-service flag with reason; excluded from Lock All + seating; restore flow.
- [ ] Smoke: reserved terminal cannot be double-booked; maintenance tile excluded.

## T. Chat upgrades (v1 works)

- [ ] Persist chat history per session; inspector shows thread.
- [ ] Unread badge + sound toggle; canned quick replies.

## U. Web dashboard (later phase per DESIGN.md)

- [ ] Read-only LAN mirror of rack + alerts (same SignalR hub, read-only group); no control
      actions; venue-PIN auth. This is the surface the impeccable detector audits.

## V. Backup + data care

- [ ] Scheduled SQLite backup (VACUUM INTO) to configurable folder + retention.
- [ ] Export/import venue settings; DB size + WAL checkpoint indicator in Settings.
- [ ] Smoke: backup file created and reopens.

## W. Licensing/activation (Undecided - decide, then build)

- [ ] DECISION NEEDED (owner): trial vs license key vs per-seat. Placeholder: offline license key
      file validated at boot; grace mode with visible "unlicensed" ribbon; never blocks a running
      session mid-shift.
- [ ] Smoke: invalid key -> ribbon; valid key -> clean.

## X. Packaging (WiX 5)

- [ ] Installer: Server app + Client agent + watchdog service; per-machine install; firewall rule
      for the Kestrel port; autostart config; uninstall leaves DB intact.
- [ ] Brand assets in installer UI; version + channel.
- [ ] Upgrade path test: v1.1 over v1.0, migrations apply.

## Y. Docs

- [ ] OPERATIONS.md: first-run, daily open/close, troubleshooting (honest kiosk limits, offline
      behavior), peripheral setup.
- [ ] Kiosk policy README: shell replacement steps, what is blocked, what cannot be.
- [ ] README Evidence section: perf numbers from P, test counts, honest limitations.

## Z. Release hardening pass

- [ ] Full clean-clone verification: build, 100% tests, 100% smoke on a fresh machine.
- [ ] Cross-check every PRODUCT.md commitment and DESIGN.md anti-pattern against the running app,
      item by item; fix stragglers.
- [ ] First real-venue pilot checklist (float count, staff training script, rollback plan).

---

## Suggested build order (dependency-aware)

1. A + B (identity/roles) - everything money-touching later needs attribution
2. C (settings wizard) - currency/locale needed by E/J receipts and reports
3. E + F + G (sales, tickets UI, members UI) - the revenue core
4. D (tariff management) + H (inventory UI) - configuration completeness
5. J + K (reports, alerts center) - accountability layer
6. I + L + M (peripherals, remote ops, hardware) - floor operations
7. N + O (resilience, kiosk hardening) - reliability promises
8. P + Q + R (perf gate, UX completeness, fonts) - release-gate quality
9. S + T (nice-to-have floor features)
10. U + V + W (web mirror, backup, licensing decision)
11. X + Y + Z (packaging, docs, release pass)

Current status: A-Z all open; Phases 1-3 (sessions, login/pause/resume/members/POS, desk/shifts/
waitlist/loans/lock-all) are DONE and verified.

---

## Development machine setup (new PC checklist)

1. Git for Windows (git-scm.com)
2. .NET 8 SDK (x64) - dotnet.microsoft.com/download/dotnet/8.0
   (`winget install Microsoft.DotNet.SDK.8`)
3. EF Core tooling: `dotnet tool install --global dotnet-ef`
   (note: on this PC `$env:USERPROFILE\.dotnet` and `$env:USERPROFILE\.dotnet\tools`
   must be in PATH for dotnet and dotnet-ef respectively)
4. IDE (pick one):
   - Visual Studio 2022 Community + ".NET desktop development" workload (best for WPF XAML)
   - or VS Code + C# Dev Kit (lighter)
5. WiX Toolset 5 (only needed for phase X packaging)

Then, from the repo root:
- `dotnet restore ZixCafePro.sln`
- `dotnet build ZixCafePro.sln -c Debug`
- `dotnet test ZixCafePro.sln` (expect 16/16)
- migrations only: `dotnet ef migrations list --project src\ZixCafe.Infrastructure --startup-project src\ZixCafe.Server.App`

WARNING: the smoke harness lives OUTSIDE the repo at
`C:\Users\<user>\AppData\Local\Temp\opencode\zixcafe-smoke\` - it does NOT survive a PC move.
Recreate it or move it into the repo (e.g. tools\smoke) before switching PCs.
