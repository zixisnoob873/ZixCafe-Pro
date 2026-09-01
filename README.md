# ZixCafe Pro

**ZixCafe Pro** is an enterprise-grade, high-performance, LAN-first cybercafé and esports lounge management studio built on **.NET 10**, **WPF**, and **ASP.NET Core SignalR**. Engineered from the ground up as a modern, zero-cloud, reliable, and cryptographically accountable PanCafe Pro alternative.

---

## 🏆 Key Feature Modules

### 🖥️ Hardware Integrity & Anti-Theft Watchdog
- **Hardware Asset Baseline**: Automatic hardware discovery on client boot logging CPU ID, GPU Device ID, RAM module capacities and serials, NVMe/SSD volume serials, and attached USB gaming peripherals.
- **Anti-Theft Real-Time Alerts**: Flags RAM module extraction, GPU swapping, storage tampering, or disconnected gaming peripherals (mice, mechanical keyboards, esports headsets) and raises immediate Critical/Warning alerts at the server studio with cryptographic audit logging.
- **Studio Hardware Inspector**: Real-time inspection panel displaying verified specs, connection health, and one-click baseline establishment.

### 🎮 Display Refresh Rate Enforcer
- **Auto-Enforcement for Esports**: Win32 native display mode enumerator auto-detects and forces monitors to their native high-refresh rate (144Hz / 240Hz / 360Hz) upon session launch.
- **Eliminate Misconfigured 60Hz Defaults**: Protects competitive esports guests from playing at low-frequency display settings.

### 🔄 Reboot-to-Restore & Diskless Compatibility
- **Clean Session Wipe**: Automatically purges temporary guest session data and downloads on session completion.
- **Diskless & Sandbox Integration**: Native coordination hooks for diskless systems (**CCBoot**, **iCafeCloud**) and restore engines (**Deep Freeze**, **Shadow Defender**) to ensure clean state resets while preserving game patch caches.

### 🛡️ Network Resilience & Offline Grace Period
- **Mid-Match Disconnect Protection**: If the server or network connection drops during a live match, client terminals enter a configurable offline grace period (e.g. 3–5 minutes) with a discrete countdown toast instead of disrupting competitive play.
- **Automatic Reconnection & Time Reconciliation**: When the connection restores, server-authoritative timestamps reconcile elapsed time seamlessly without penalizing players.

### 💾 Online Database Backup & Restore
- **Non-blocking SQLite WAL Snapshots**: Online `VACUUM INTO` backup engine with automated retention policies and manual export capabilities.
- **One-Click Restoration**: File-based restoration and live table restoration with automatic pre-restore safety snapshots and SQLite header validation.

### 🏢 Rack Management & Studio Operations
- **Virtualized High-Density Rack**: Real-time interactive tile grid supporting 100+ active gaming stations with multi-zone filtering, global search (`Ctrl+F`), and keyboard accelerators (`F1`–`F8`).
- **Session Types**: Prepaid, Postpaid, Voucher Tickets, and Member accounts with pause/resume, overtime grace, and remote time extensions.
- **Tariff Engine**: Flat, Tiered, DaySchedule, and MemberTier pricing models with exact minute-band resolution and minimum charges.
- **Retail POS & Stock Management**: Barcode scanner integration, split payments (Cash / Card / QR), change calculation, inventory adjustments (`Restock`, `Waste`), and ESC/POS thermal receipt printing.
- **Ticket Vouchers**: 13-character base-32 Luhn-mod-32 checksummed vouchers (`XXXX-XXXX-XXXX-X`) with single and batch generation.
- **Members Club**: Cash balance and prepaid time ledgers, automatic tier progression based on lifetime spend, and membership freeze/unfreeze controls.
- **Peripheral Metering**: Held print spooling with per-page/copy rate release and USB megabyte metered transfer charges.
- **Shift Management & Z-Reports**: Cash drawer opening float, interim X-Reports, closing Z-Reports with drawer variance tracking.
- **Cryptographic Audit Trail**: Every transaction and system action is linked in a SHA-256 hash-chain with one-click cryptographic tamper verification.
- **Remote Operations & Guest Privacy**: Announce-first remote screen viewer (shows guest assistance banner on client), remote reboot/shutdown, and prohibited application killer (`cheatengine`, `artmoney`, `wireshark`, etc.).
- **LAN Web Dashboard**: Embedded read-only HTML5/SignalR dashboard accessible from any browser at `http://<server-ip>:40000/dashboard`.

---

## 📊 Evidence & Verification Summary

| Metric | Status / Value |
|---|---|
| **Solution Projects** | 7/7 projects compile with **0 errors** (`net10.0` / `net10.0-windows`) |
| **Automated Unit Tests** | **31/31 Passing (100%)** |
| **Database Engine** | SQLite in **Write-Ahead Logging (WAL)** mode with foreign key enforcement |
| **Cryptographic Integrity** | 100% SHA-256 linked audit verification across all transactions |
| **LAN Port** | 40000 (Kestrel HTTP & SignalR Hubs) |

---

## 🚀 Quick Start

### 1. Build and Run Server Studio
```cmd
dotnet build ZixCafePro.sln
dotnet run --project src\ZixCafe.Server.App\ZixCafe.Server.App.csproj
```
*(On first launch, the First-Run Setup Wizard will appear to initialize venue branding, currency, tax, float, and the administrator account).*

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

## 📚 Documentation
- [OPERATIONS.md](OPERATIONS.md) — Comprehensive operator workflow & deployment guide.
- [docs/kiosk-policy.md](docs/kiosk-policy.md) — Windows auto-logon, watchdog service, and shell replacement guide.
- [DESIGN.md](DESIGN.md) — Design tokens, typography, luxury dark theme specifications, and UI guidelines.
- [PRODUCT.md](PRODUCT.md) — Core business principles and non-functional requirements.
