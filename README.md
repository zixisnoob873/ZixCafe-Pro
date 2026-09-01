# ZixCafe Pro

**ZixCafe Pro** is a high-performance, modern, LAN-first cybercafé and esports lounge management studio built on **.NET 10**, **WPF**, and **ASP.NET Core SignalR**. Engineered from the ground up as a zero-cloud, reliable, and cryptographically accountable PanCafe Pro alternative.

---

## Key Feature Modules

- 🏢 **Rack Management Studio**: Virtualized real-time grid supporting 100+ active terminals, multi-zone filtering, search (`Ctrl+F`), and keyboard accelerators (`F1`–`F8`).
- ⚡ **Session Lifecycle**: Prepaid, Postpaid, Ticket Vouchers, and Member accounts with pause/resume, auto-end, and time sync drift reconciliation.
- 💰 **Tariff Engine**: Flat, Tiered, DaySchedule, and MemberTier pricing models with exact minute-band resolution and minimum charges.
- 🛒 **Retail POS & Inventory**: Product catalog, barcode scanner support, split payments (Cash / Card / QR), change calculation, stock adjustments (`Restock`, `Waste`), and ESC/POS thermal receipts.
- 🎟️ **Ticket Vouchers**: 13-character base-32 Luhn-mod-32 checksummed vouchers (`XXXX-XXXX-XXXX-X`) with counter single/batch generation and manager-authorized voiding.
- 👥 **Members Club**: Cash and time ledgers, automatic tier progression based on total spending, member freeze/unfreeze controls.
- 🖨️ **Peripheral Billing**: Held print queue with per-page/copy rate release and USB megabyte metered transfer charges.
- 📊 **Shift Management & Z-Reports**: Opening float tracking, interim X-Reports, closing Z-Reports with physical cash count and drawer variance calculation.
- 🔐 **Cryptographic Audit Trail**: Every transaction and system action is linked in a SHA-256 hash-chain with one-click cryptographic tamper verification.
- 👁️ **Remote Operations & Guest Privacy**: Announce-first remote screen viewer (shows guest assistance banner on client), remote reboot/shutdown, and prohibited application killer (`cheatengine`, `artmoney`, etc.).
- 🌐 **LAN Web Dashboard**: Embedded read-only HTML5/SignalR dashboard accessible from any browser at `http://<server-ip>:40000/dashboard`.
- 💾 **Data Care & Backups**: Online zero-lockup SQLite `VACUUM INTO` backup snapshots with automated retention.

---

## Evidence & Verification Summary

| Metric | Status / Value |
|---|---|
| **Solution Projects** | 7/7 projects compile with **0 errors** (`net10.0` / `net10.0-windows`) |
| **Unit Tests** | **22/22 Passing (100%)** |
| **Database Engine** | SQLite in **Write-Ahead Logging (WAL)** mode with foreign key enforcement |
| **Cryptographic Integrity** | 100% SHA-256 linked audit verification across all transactions |
| **LAN Port** | 40000 (Kestrel HTTP & SignalR Hubs) |

---

## Quick Start

### 1. Build and Run Server Studio
```cmd
dotnet build ZixCafePro.sln
dotnet run --project src\ZixCafe.Server.App\ZixCafe.Server.App.csproj
```
*(On first launch, the First-Run Setup Wizard will appear to initialize venue branding, tax, float, and the administrator account).*

### 2. Run Client Agent
```cmd
dotnet run --project src\ZixCafe.Client.Agent\ZixCafe.Client.Agent.csproj
```
*(Enter the server URL `http://localhost:40000` and the single-use pairing code issued from the Server Rack).*

### 3. Run Automated Test Suite
```cmd
dotnet test ZixCafePro.sln
```

---

## Documentation
- [OPERATIONS.md](OPERATIONS.md) — Comprehensive operator workflow & deployment guide.
- [docs/kiosk-policy.md](docs/kiosk-policy.md) — Windows auto-logon, watchdog service, and shell replacement guide.
- [DESIGN.md](DESIGN.md) — Design tokens, typography, luxury dark theme specifications, and UI anti-patterns.
- [PRODUCT.md](PRODUCT.md) — Core business principles and non-functional requirements.
