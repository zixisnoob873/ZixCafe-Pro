# ZixCafe Pro — Design System & UI/UX Architecture Specification

<!-- impeccable:design-schema 2.0 -->

## 1. Executive Direction & World Language

**ZixCafe Pro** is a high-performance cyber-lounge and esports operations command center. It unifies the proven dual-dock operations architecture of classic cafe management systems (**PanCafe Pro**) with a modern, high-contrast **Matte Obsidian & Luminous Amber Gold** design language.

### World Thesis: "Quiet Ownership of a Loud Floor"
- **Restraint as Luxury**: A high-traffic gaming lounge floor is loud and visual. The cashier's management desk must be calm, razor-sharp, and legible from 10 feet away.
- **Elevation via Hairlines**: Elevation is communicated through 1px border contrasts (`LineBrush` on `PanelBrush`), never fuzzy blur shadows.
- **Gold is Attention**: Gold lands strictly where money, active time, and selection live. Everything else recedes into obsidian blacks.
- **Tabular Data Fidelity**: All ticking digits, financial amounts, and timers render in monospaced tabular numerals (`JetBrains Mono`) to prevent layout shift.
- **Never Color Alone**: Semantic indicators always pair a distinct color badge with explicit human-readable text.

---

## 2. Color Palette & Semantic Tokens

| Token | Hex | Solid Brush | Semantic Role |
| :--- | :--- | :--- | :--- |
| **Void** | `#0C0A09` | `VoidBrush` | App foundation background, the deepest obsidian surface |
| **Panel** | `#18181B` | `PanelBrush` | Cards, tiles, docks, inspector surfaces, data tables |
| **Raised** | `#27272A` | `RaisedBrush` | Elevated controls, inputs, headers, toolbar trays |
| **Hover** | `#323238` | `HoverBrush` | Interactive hover highlight on buttons and list items |
| **Selected** | `#3F3F46` | `SelectedBrush` | Selected row / pressed surface highlight |
| **Line** | `#3F3F46` | `LineBrush` | Hairline borders, dividers, subtle grid lines (1px) |
| **LineSubtle**| `#27272A` | `LineSubtleBrush` | Secondary subtle dividers |
| **Ink** | `#FAFAF9` | `InkBrush` | High-contrast primary typography (14:1+ on Void) |
| **Ghost** | `#A1A1AA` | `GhostBrush` | Secondary labels, captions, table headers |
| **Muted** | `#71717A` | `MutedBrush` | Helper text, disabled controls, placeholder copy |
| **Gold** | `#FBBF24` | `GoldBrush` | Primary accent, running timers, active selection, primary CTAs |
| **GoldDeep** | `#D97706` | `GoldDeepBrush` | Active rail indicator, focus ring, pressed gold states |
| **GoldSubtle**| `#2A1F0A` | `GoldSubtleBrush` | Selected row background tint, checkmark container fill |
| **Run** | `#22C55E` | `RunBrush` | Active in-use sessions, healthy connectivity, money loaded |
| **RunSubtle**| `#052E16` | `RunSubtleBrush` | Background tint for success badges |
| **Warn** | `#F97316` | `WarnBrush` | Warnings, idle timeouts, USB storage policy alerts |
| **WarnSubtle**| `#431407` | `WarnSubtleBrush` | Background tint for warning pills |
| **Alert** | `#EF4444` | `AlertBrush` | Disconnects, hardware tamper, lockouts, errors |
| **AlertSubtle**| `#450A0A` | `AlertSubtleBrush` | Background tint for critical alert cards |
| **Info** | `#06B6D4` | `InfoBrush` | Reserved terminals, print services, IoT MQTT messages |
| **InfoSubtle**| `#083344` | `InfoSubtleBrush` | Background tint for info pills |

---

## 3. Typography Architecture

| Font Family | Weight | Token | Primary Application |
| :--- | :--- | :--- | :--- |
| **Chakra Petch** | `SemiBold` (600) / `Bold` (700) | `DisplayFont` | Branding wordmarks, module ribbon headers, terminal names, action buttons |
| **IBM Plex Sans** | `Regular` (400) / `Medium` (500) | `BodyFont` | Form labels, dialogs, member details, settings fields, chat messages |
| **JetBrains Mono** | `Medium` (500) / `Bold` (700) | `DataFont` | Digital clock, countdown timers, currency amounts, IP/MAC addresses, live logs |

### Typography Scale
- **Display / Digital Clock**: `22px` - `26px` (High-contrast gold / ink)
- **Heading / View Titles**: `16px` - `18px`
- **Body Text**: `13px` - `14px`
- **Small Controls / Buttons**: `12px`
- **Micro Labels / Caps Badges**: `10px` - `11px` (Uppercase, tracked `1px`)

---

## 4. PanCafe Pro Operations Layout Hierarchy

```
+=============================================================================================================+
| 1. TOP MODULE RIBBON & DIGITAL CLOCK                                                                        |
|    [Branding] [Home] [Settings] [Members] [Tickets] [Cash] [Cafeteria] [Reports] ...     [21:41:56 | Tue 1 Sep] |
+=============================================================================================================+
| 2. FAST ACTION COMMAND TOOLBAR                                                                              |
|    [+ Postpaid] [+ Prepaid] [Member] [Pause] [End] | [WoL] [Reboot] [Lock] | [Screen] [Chat] [Relay] [Kill] |
+------------------------+--------------------------------------------------------------------+---------------+
| 3. LEFT DOCKED         | 4. MULTI-VIEW CENTER WORKSPACE                                     | 6. RIGHT      |
|    INSPECTOR PANEL     |    [Tabs: Terminal View | Screen View | Performance Telemetry]     |    QUICK      |
|    [General]           |                                                                    |    DRAWER     |
|    [Orders & Hardware] |    • Terminal Grid with Computer Monitor Cards (PC-01 .. PC-35)    |    (Waitlist  |
|    [Activity History]  |    • Multi-Screen Remote Viewer Wall                               |     & Passes) |
|                        |    • CPU/GPU/RAM Telemetry Meters                                  |               |
+------------------------+--------------------------------------------------------------------+               |
| 5. BOTTOM SPLIT CONSOLE                                                                                     |
|    [Tabs: Live Event Log (Color-Coded Stream) | Cashier-Terminal Chat Thread]                               |
+=============================================================================================================+
| 7. TELEMETRY STATUS BAR                                                                                     |
|    Cashier: admin · Owner | In Use: 12 | Idle: 23 | Occupancy: [====44%====] | Server: 40000 ONLINE | v1.0.0    |
+=============================================================================================================+
```

---

## 5. Component Specifications

### 1. Top Application Ribbon
- **Left Branding**: Gold `ZixBoltGeometry` emblem with `ZIXCAFE PRO` title and `COMMERCIAL EDITION` micro badge.
- **Module Navigation**: Full-width icon + label ribbon with hover highlight and active gold indicator strip (`Home/Rack`, `Desk & Shifts`, `POS & Cafeteria`, `Vouchers`, `Members Club`, `Inventory`, `Print/USB`, `Reports & Audit`, `Alerts Center`, `Settings & Tariffs`).
- **Live Digital Clock**: Top-right high-visibility digital clock ticking at 1Hz (`JetBrains Mono`, `24px`, Gold) paired with human-readable date (`Tuesday, 1 September 2026`).

### 2. Fast Action Command Toolbar
- High-utility, single-click operational bar acting directly on the selected workstation:
  - **Session Group**: `Start Postpaid (F2)`, `Start Prepaid`, `Member Login`, `Pause/Resume (F3)`, `End Session (F4)`.
  - **Power & System Group**: `Wake-on-LAN`, `Remote Reboot`, `Remote Shutdown`, `Lock Terminal`, `Lock All`.
  - **Diagnostics & Remote Group**: `Live Screen View`, `Client Chat (F8)`, `Smart Relay Power`, `Kill Prohibited Cheats`.

### 3. Left Docked Inspector Panel
- Tabbed detail inspector for instant terminal triage without opening modals:
  - **Tab 1: General**: Terminal name, IP, MAC, Zone, Active Session ID, User Name, Hourly Rate, Time Elapsed/Remaining, Total Amount Due, Telemetry (CPU Temp, RAM %, Disk Free).
  - **Tab 2: Orders & Hardware**: Active cafeteria orders, Hardware baseline specs (CPU, GPU, RAM, Native Refresh Rate), USB peripherals attached, hardware loans held.
  - **Tab 3: Activity History**: Live audit history for this specific workstation (login times, unlock events, disconnect logs).

### 4. Visual Computer Monitor Card (`TerminalTile`)
- Designed as an authentic gaming monitor frame:
  - **Display Bezel**: Rounded monitor bezel with status border.
  - **Power Rail Indicator**: 2px left accent bar (`GoldDeepBrush`) with subtle 2s breathing pulse when session is active.
  - **Status Pill**: Red (`LOCKED / IDLE`), Green (`IN USE`), Blue (`RESERVED`), Orange (`MAINTENANCE`).
  - **Monospaced Countdown**: Large `22px` timer in `GoldBrush` (`JetBrains Mono`).
  - **Bottom Info**: Workstation zone (e.g. `VIP Booth`, `Main Floor`), current amount charged.

### 5. Multi-View Center Workspace
- Switch between 3 views instantly:
  - **Terminal View**: Interactive grid of all 35+ terminal cards with search and zone filtering.
  - **Screen View**: Multi-screen remote thumbnail surveillance wall with one-click refresh and zoom preview.
  - **Performance Telemetry**: Live CPU, GPU, and RAM load bars across all networked rigs.
- Seamlessly swaps to dedicated workspace panels for POS/Cafeteria, Members Club, Voucher Generation, Inventory, and Settings when selected from the top ribbon.

### 6. Bottom Split Real-Time Console
- **Tab 1: Live Event Log**: Color-coded, timestamped operational event stream:
  - `Green (#22C55E)`: Money loaded, session started, terminal connected, payment received.
  - `Orange (#F97316)`: USB storage detached, idle standby warning, session extension alert.
  - `Red (#EF4444)`: Unexpected disconnect, prohibited process detected, hardware baseline mismatch.
  - `Cyan (#06B6D4)`: Shift opened/closed, print job queued, smart relay toggled.
- **Tab 2: Messages & Chat**: Terminal-to-cashier chat thread with quick replies.

### 7. Bottom Telemetry Status Bar
- Comprehensive real-time store metrics:
  - `Cashier Name` (e.g. `ADMIN · OWNER`)
  - `Total Terminals` (`35`)
  - `In Use` (`12`)
  - `Idle / Ready` (`23`)
  - `Occupancy Rate` (Visual progress bar + percentage, e.g. `44%`)
  - `Active Shift Status` (`SHIFT OPEN · SINCE 09:00`)
  - `Server Connection` (`127.0.0.1:40000 · ONLINE`)
  - `Software Version` (`v1.0.0 Commercial Edition`)

---

## 6. Accessibility & Consistency Rules

1. **WCAG AA Contrast**: Text on dark backgrounds must meet at least 4.5:1 for body and 3:1 for large display titles.
2. **Deterministic Layouts**: No layout shifting when timers tick or numbers update.
3. **No Hidden Errors**: Background errors render explicit toast/log alerts with recovery actions.
4. **Keyboard Accelerators**: Full cashier flow mapped to standard keys:
   - `F1`: Terminal Rack (Home)
   - `F2`: Start Postpaid Session
   - `F3`: Pause / Resume Session
   - `F4`: End Session & Bill
   - `F5`: Retail POS / Cafeteria
   - `F6`: Add Product to Order
   - `F8`: Send Chat Message
   - `F9`: Save Settings
   - `Ctrl+F`: Focus Workstation Search
   - `Esc`: Close Inspector / Modal
