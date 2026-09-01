# Windows Kiosk Hardening & Group Policy Guide

This guide describes how to lock down client gaming terminals running `ZixCafe.Client.Agent` on Windows 10 and 11.

---

## 1. Auto-Logon Configuration

Terminals should automatically log into a standard, non-administrator guest user account (e.g. `Gamer` or `Player`).

1. Download **Sysinternals Autologon**:
   `https://learn.microsoft.com/en-us/sysinternals/downloads/autologon`
2. Run `Autologon.exe` with the non-admin username and password:
   ```cmd
   Autologon.exe Player . Password123!
   ```

---

## 2. Windows Service Watchdog Installation

`ZixCafe.Client.Service` runs in the background as a Windows Service under `LocalSystem`. It ensures the agent is relaunched immediately if terminated:

```cmd
:: Create the service
sc.exe create ZixCafeWatchdog binPath= "C:\Program Files\ZixCafe\ZixCafe.Client.Service.exe" start= auto

:: Set failure recovery actions (Restart Service on crash)
sc.exe failure ZixCafeWatchdog reset= 86400 actions= restart/5000/restart/10000/restart/20000

:: Start the service
sc.exe start ZixCafeWatchdog
```

---

## 3. Shell Replacement (Optional Full Lockdown)

To prevent users from accessing the Windows Explorer desktop or Taskbar entirely, set `ZixCafe.Client.Agent.exe` as the Windows Shell for the `Player` account.

In `regedit.exe` (under `HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Winlogon`):
```reg
[HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Winlogon]
"Shell"="C:\\Program Files\\ZixCafe\\ZixCafe.Client.Agent.exe"
```

To revert back to standard Windows Explorer:
```reg
[HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Winlogon]
"Shell"="explorer.exe"
```

---

## 4. Keyboard & Security Policy Limits (Honest Disclosure)

### What `KioskGuard` Blocks:
- `Alt + Tab` (Application switching)
- `WinKey` and `WinKey + ...` (Start menu, Search, Settings)
- `Alt + Esc` / `Alt + Space`
- `Ctrl + Esc`

### Architectural Limitation:
- **`Ctrl + Alt + Del`**: Windows intercepts `Ctrl + Alt + Del` at the kernel SAS (Secure Attention Sequence) level. It cannot be suppressed by any user-mode Win32 hook or process.
- **Remediation**: Use Local Group Policy (`gpedit.msc`) under `User Configuration -> Administrative Templates -> System -> Ctrl+Alt+Delete Options` to:
  - Remove Task Manager (`DisableTaskMgr = 1`)
  - Remove Lock Computer (`DisableLockWorkstation = 1`)
  - Remove Change Password (`DisableChangePassword = 1`)
