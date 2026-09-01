# ZixCafe Pro — Enterprise SaaS Design System & Architecture Specification (`DESIGN.md`)

> **Version:** 2.5 Enterprise Modern  
> **Aesthetic Philosophy:** Linear / Stripe / Fluent Minimalist Modern Desktop SaaS  
> **Source of Truth:** All XAML styles, control templates, view models, and code-behind implementations across ZixCafe Pro must strictly adhere to this document.

---

## 1. Visual Philosophy & Core Pillars

1. **Restraint & High Signal-to-Noise Ratio**:
   - Zero gratuitous neon glows, faux-gamer gradients, or heavy drop shadows.
   - Clean slate backgrounds (`#090D16`, `#0F172A`), subtle 1px structural dividing lines (`#334155`), crisp typography (`#F8FAFC`), and a single refined primary brand accent (Indigo / Cobalt `#6366F1` / `#4F46E5`).
2. **Strict 8px Spacing Grid**:
   - All margins, paddings, and layout gaps conform to the 8px multiplier (`8px`, `16px`, `24px`, `32px`, `48px`).
3. **Corner Radii Discipline**:
   - Inputs and Buttons: `6px` (`ButtonRadius="6"` / `InputRadius="6"`).
   - Surface Cards & Modals: `8px` (`CardRadius="8"` / `TileRadius="8"`).
   - Status Badges & Pills: `999px` (`PillRadius="999"`).
4. **Hierarchical Typography**:
   - **UI Headings & Body**: Segoe UI / Inter / Geist (`Regular`, `SemiBold`, `Bold`).
   - **Data, Money, Counters, Timers**: Monospaced tabular numerals (Consolas / JetBrains Mono / Cascadia Code) to prevent visual jitter on updates.
5. **Zero Hardcoded Colors**:
   - Every brush and color must bind to centralized semantic tokens in `Tokens.xaml`. No raw hex codes or RGB literals allowed in views.

---

## 2. Design Token Dictionary (`Themes/Tokens.xaml`)

### Surfaces & Backgrounds
| Token Key | Hex Value | Semantic Purpose |
|---|---|---|
| `VoidBrush` | `#090D16` | Deepest canvas and window root background |
| `PanelBrush` | `#0F172A` | Sidebar, toolbars, and primary container panels |
| `RaisedBrush` | `#1E293B` | Interactive cards, modal dialogs, table headers |
| `HoverBrush` | `#283548` | Hover state background on rows and buttons |
| `SelectedBrush` | `#334155` | Active selection background |
| `LineBrush` | `#334155` | 1px structural borders and card outlines |
| `LineSubtleBrush` | `#1E293B` | In-card subtle separators |

### Primary Brand Accent
| Token Key | Hex Value | Semantic Purpose |
|---|---|---|
| `BrandBrush` | `#6366F1` | Primary CTA buttons, active route indicators, focus outlines |
| `BrandDeepBrush` | `#4F46E5` | Button pressed / active state |
| `BrandSubtleBrush`| `#1E1B4B` | Subtle accent tint background for active pills |
| `TextOnBrandBrush`| `#FFFFFF` | Text rendered on top of primary brand elements |

### Typography Colors
| Token Key | Hex Value | Semantic Purpose |
|---|---|---|
| `InkBrush` | `#F8FAFC` | Primary text, titles, values (high contrast WCAG AAA 14:1) |
| `GhostBrush` | `#94A3B8` | Secondary labels, descriptions, column headers |
| `MutedBrush` | `#64748B` | Disabled text, subtle timestamps, footnotes |

### Semantic Status Tokens
| Token Key | Solid Hex | Subtle Hex | Meaning / Usage |
|---|---|---|---|
| `RunBrush` / `RunSubtleBrush` | `#10B981` | `#064E3B` | Running session, active terminal, positive balance |
| `WarnBrush` / `WarnSubtleBrush` | `#F59E0B` | `#451A03` | Paused session, low stock alert, expiring voucher |
| `AlertBrush` / `AlertSubtleBrush` | `#EF4444` | `#450A0A` | Terminated app, locked workstation, error, banned user |
| `InfoBrush` / `InfoSubtleBrush` | `#38BDF8` | `#082F49` | System notification, telemetry stream, hardware info |

---

## 3. Role-Based Access Control (RBAC) Architecture

ZixCafe Pro enforces a strict 2-Role security boundary:

```
                      ┌─────────────────────────────────┐
                      │    Cashier Authentication PIN   │
                      └────────────────┬────────────────┘
                                       │
                    ┌──────────────────┴──────────────────┐
                    ▼                                     ▼
        ┌───────────────────────┐             ┌───────────────────────┐
        │     EMPLOYEE ROLE     │             │      ADMIN ROLE       │
        │  (CashierRole.Staff)  │             │(CashierRole.Manager / │
        │                       │             │   CashierRole.Owner)  │
        └───────────┬───────────┘             └───────────┬───────────┘
                    │                                     │
                    ▼                                     ▼
        ┌───────────────────────┐             ┌───────────────────────┐
        │  OPERATIONAL WORKFLOW │             │ FULL CMS CONTROL SUITE│
        │ - Station Rack (Live) │             │ - Station Fleet CMS   │
        │ - Rapid POS Checkout  │             │ - Tariffs & Rates CMS │
        │ - My Shift & Float    │             │ - Members Club CMS    │
        │                       │             │ - Staff & Shifts CMS  │
        │ [Admin Tabs & Config  │             │ - POS & Inventory CMS │
        │  Structurally Omitted]│             │ - Tickets/Vouchers CMS│
        │                       │             │ - Audit & Analytics   │
        │                       │             │ - System Security     │
        └───────────────────────┘             └───────────────────────┘
```

### 1. Employee Role (`CashierRole.Staff`)
- **Operational Scope**:
  - Live Station Rack Grid (monitoring, start postpaid/prepaid, pause, end session).
  - Quick POS Cafeteria Retail Checkout (adding snacks/drinks to customer tabs).
  - Shift summary (current opening float and transactions handled during current shift).
- **Security Boundary**:
  - All administrative tabs, settings, rate adjustments, staff accounts, member wallet overrides, and audit logs are **structurally hidden (`Visibility.Collapsed`)** and route-guarded.

### 2. Admin Role (`CashierRole.Manager` & `CashierRole.Owner`)
- **Complete CMS Suite**:
  - Full CRUD operations on Station Fleet, Tariffs, Members, Staff, Inventory, Tickets, Analytics, and System Configuration.

---

## 4. Full Admin CMS Domains & CRUD Specifications

### A. Station Fleet CMS
- **Entities**: Station ID, Name, Zone, Hardware Profile, IP, MAC Address, Status, Display Hz, Agent Version.
- **CRUD Operations**: Add Station, Edit Station Parameters, Delete Station, Remote WoL / Reboot / Lock / Force 240Hz.

### B. Tariffs & Pricing CMS
- **Entities**: Tariff Name, Pricing Model (Flat, Tiered, DaySchedule), Base Rate ($/hr), Rounding Blocks (1m, 5m, 15m), Minimum Charges, Dynamic Time-Band Rules (Weekend, Peak, Off-Peak).
- **CRUD Operations**: Create Tariff, Edit Rates & Rules, Delete Tariff, Set Default Venue Rate.

### C. Members & Wallets CMS
- **Entities**: Member Code, Name, Loyalty Tier (Bronze/Silver/Gold/Platinum), Time Balance, Money Wallet Balance, Total Spent, Status (Active/Suspended/Banned).
- **CRUD Operations**: Register Member, Top-Up Balance (with tier bonus calculation), Edit Info, Ban/Unban Account, View Transaction History.

### D. Staff & Shifts CMS
- **Entities**: Username, Role (Admin vs Employee), Active Status, Current Shift Started, Opening Float, Revenue Handled, Cash Drawer Variance.
- **CRUD Operations**: Create Staff Member, Edit PIN & Role, Deactivate Account, Reconcile Cash Drawer, Generate Z-Report.

### E. Café POS & Inventory CMS
- **Entities**: SKU, Name, Category (Drinks, Snacks, Meals, Hardware Loan), Cost, Price, Margin, Stock Qty, Low-Stock Alert.
- **CRUD Operations**: Add Product, Restock / Adjust Inventory, Edit Pricing, Delete Product, Low-Stock Filter.

### F. Prepaid Tickets & Vouchers CMS
- **Entities**: Voucher Code, Type (Duration vs Monetary Credit), Value, Price, Generated At, Expiry, Status (Available, Used, Revoked).
- **CRUD Operations**: Batch Generate Tickets, Print Voucher Slips, Revoke Voucher, Export CSV.

### G. Audit & Financial Analytics CMS
- **Telemetry**: Real-time revenue breakdown, hourly workstation utilization graphs, margin analysis.
- **Immutable Ledger**: SHA-256 blockchain audit chain ledger tracking every administrative action (`tariff.modify`, `balance.refund`, `station.delete`, `session.override`) with instant tamper verification.

### H. System Configuration CMS
- **Controls**: Network port configuration, shell lockdown policies, process blacklist, USB whitelist, deepfreeze reboot-to-restore, automated database backup schedules.
