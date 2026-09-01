# ZixCafe Pro — Operational & Deployment Guide

ZixCafe Pro is a modern, high-performance, LAN-first internet café and gaming lounge management platform built on .NET 10 and WPF. It operates with zero cloud dependencies, complete offline resilience, SQLite WAL persistence, and cryptographically verified audit trails.

---

## 1. System Architecture

```
                                  ┌──────────────────────────────┐
                                  │   ZixCafe Server App         │
                                  │   (WPF Management Studio)    │
                                  │   Port: 40000 (HTTP/SignalR) │
                                  └──────────────┬───────────────┘
                                                 │
                     ┌───────────────────────────┴───────────────────────────┐
                     │ SignalR Hubs (/hubs/terminal, /hubs/dashboard)        │
                     ▼                                                       ▼
        ┌────────────────────────┐                              ┌────────────────────────┐
        │  ZixCafe Client Agent  │                              │ LAN Web Dashboard      │
        │  (Lock Window + Kiosk) │                              │ (Read-only Operator)   │
        └────────────┬───────────┘                              │ http://<server>:40000/ │
                     │                                          └────────────────────────┘
        ┌────────────┴───────────┐
        │  ZixCafe Watchdog Svc  │
        │  (Windows Service)     │
        └────────────────────────┘
```

### Components
1. **`ZixCafe.Server.App`**: WPF operator studio + in-process ASP.NET Core Kestrel SignalR host on port 40000.
2. **`ZixCafe.Client.Agent`**: WPF fullscreen lock screen with keyboard hooks, announce-first remote screen viewer, telemetry broadcaster, and prohibited process terminator.
3. **`ZixCafe.Client.Service`**: Windows watchdog service that automatically monitors and relaunches the client agent if terminated.
4. **`ZixCafe.Infrastructure`**: EF Core 8 SQLite database with Write-Ahead Logging (`WAL`), Foreign Keys, and busy timeouts.
5. **`ZixCafe.Domain`**: Pure business logic (Tariff Engine, SHA-256 Audit Chain, Ticket Code Generator with Luhn-mod-32 checksums, and Shift Variance reconciler).

---

## 2. First-Run Setup & Venue Configuration

When launching `ZixCafe.Server.App` for the first time:
1. The **First-Run Setup Wizard** automatically appears.
2. Enter the venue name, primary currency code and symbol (e.g. `$`, `€`, `£`), default opening float, and tax rate.
3. Set the Administrator account credentials (username and 4+ digit PIN).
4. Optionally check *"Load sample terminals, tariffs, and catalog items"* to seed demo data tagged with `[SAMPLE]`.
5. Upon completion, the server initializes the database, applies all schema migrations, and opens the main Management Studio.

---

## 3. Operator & Cashier Workflows

### Opening & Closing Shifts
- **Opening Float**: When starting a shift, the cashier enters the starting cash float.
- **Interim Reading (X-Report)**: Press *"Print X-Report"* at any time to view current time sales, retail sales, print/USB revenue, discounts, and expected drawer cash.
- **Closing Shift (Z-Report)**: Enter the physical cash counted in the drawer. The system automatically computes the drawer variance (`Counted - Expected`) and commits a permanent shift record into the audit log.

### Session Types
1. **Walk-up (Postpaid)** (`F2`): Billed continuously based on the active tariff model until ended by cashier or operator.
2. **Prepaid Blocks** (30, 60, 120 min): Automatically locks the terminal when remaining time reaches `00:00`.
3. **Voucher / Ticket Codes**: 13-character base-32 format (`XXXX-XXXX-XXXX-X`) with Luhn checksum validation. Supports duration tickets or prepaid credit tickets.
4. **Members Club**: Auto-deducts time or money balance with member tier discounts.

### Manager PIN Overrides
The following actions strictly require a Manager or Admin PIN override:
- Voiding unredeemed or active voucher codes.
- Modifying active tariff rates or schedule rules.
- Manually adjusting inventory stock (waste / write-offs).
- Staff and cashier user administration.

---

## 4. Retail POS & Inventory

- **Fast POS Catalog** (`F5`): Visual product catalog with search by name or barcode scanner.
- **Split Tender Reconciliation**: Cash, Credit Card, and QR payment methods can be combined on a single transaction.
- **Oversell Guard**: Prevents selling items with insufficient stock.
- **Thermal Receipt Printing**: Generates clean ESC/POS formatted receipts sent to the receipt printer or local spooler.

---

## 5. Peripheral Billing (Print & USB)

- **Print Billing Queue**: Captures print jobs from terminals, calculates page/copy charges based on `VenueSettings.PrintCostPerPage`, and holds jobs in queue until paid and released by cashier.
- **USB Metered Transfers**: Tracks volume of data transferred to external USB drives and applies per-GB rate billing.

---

## 6. Remote Operations & Guest Privacy

- **Announce-First Screen Capture**: When an operator clicks *"View Screen"* in the Rack Inspector, an announcement banner is displayed on the terminal: *"The front desk is viewing this screen for technical assistance."*
- **Remote Controls**: Reboot, Shutdown, and Lock commands can be dispatched directly from the Management Studio.
- **Prohibited Application Guard**: Monitors background processes for cheat engines, memory injectors, or illegal tools, automatically terminates them, and dispatches real-time alerts to the front desk.

---

## 7. Data Safety, Cryptographic Audits & Backups

### Online SQLite `VACUUM INTO` Backup
- Generate complete database snapshots at any time while the server is running with zero lockups or table blocking.
- Backup files are saved under the `backups/` directory stamped with ISO timestamps.

### Cryptographic Hash-Chained Audit Trail
- Every transaction, shift close, tariff edit, and session event is chained using SHA-256:
  $$\text{Hash}_n = \text{SHA256}(\text{Hash}_{n-1} + \text{Action} + \text{TargetType} + \text{TargetId} + \text{Detail} + \text{Cashier} + \text{Timestamp})$$
- Press **"Verify Audit Chain"** in the Reports tab to cryptographically verify 100% of audit entries against their stored signatures to confirm zero database tampering.

---

## 8. Client Kiosk Hardening (Windows)

To deploy terminals in public kiosk mode:
1. Copy `ZixCafe.Client.Agent` and `ZixCafe.Client.Service` to `C:\Program Files\ZixCafe\`.
2. Install the watchdog service:
   ```cmd
   sc create ZixCafeWatchdog binPath= "C:\Program Files\ZixCafe\ZixCafe.Client.Service.exe" start= auto
   sc start ZixCafeWatchdog
   ```
3. Set the client user account auto-logon via Windows Sysinternals `Autologon.exe`.
4. Configure Windows Shell Replacement (optional) pointing `Userinit` or `Shell` registry key to `ZixCafe.Client.Agent.exe`.
