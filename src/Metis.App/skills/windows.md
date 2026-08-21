# Windows
description: How to get around Windows 11 — opening apps, reaching settings, and the names controls really have
applies-to: window, start, taskbar, explorer, settings, desktop, folder, file, open, launch, close, minimise, minimize, maximise, maximize, resize, app, menu, button, icon, screen, keyboard, shortcut, control panel, task manager, device manager, wifi, bluetooth, volume, display, update, notepad, browser

## Opening anything

The reliable way to open an app is the keyboard, not hunting for an icon:
press the Windows key, type a few letters of the name, press Enter. This works
from any screen, needs no visible Start menu, and does not depend on where
icons happen to sit. Desktop and taskbar icon positions differ on every
machine — never assume one is in a particular place.

Win+R then a command opens exact targets: `notepad`, `calc`, `mspaint`,
`explorer`, `cmd`, `powershell`, `control` (the old Control Panel), `winver`,
`shell:startup` (the startup folder), `shell:downloads`.

Settings pages open directly by URI, which is far more reliable than clicking
through the app: `ms-settings:` (root), `ms-settings:network-wifi`,
`ms-settings:bluetooth`, `ms-settings:display`, `ms-settings:sound`,
`ms-settings:powersleep`, `ms-settings:windowsupdate`,
`ms-settings:apps-defaultapps`, `ms-settings:privacy`.

## Shortcuts worth using instead of clicking

Win+E File Explorer · Win+I Settings · Win+X admin menu (Device Manager,
Terminal, Installed apps) · Win+D show desktop · Win+L lock · Win+arrows snap
a window · Alt+Tab switch app · Ctrl+Shift+Esc Task Manager · Win+Shift+S
screenshot a region · Win+V clipboard history · Alt+F4 close · F2 rename ·
F5 refresh.

In File Explorer, Ctrl+L or Alt+D puts the cursor in the address bar; typing a
full path and pressing Enter goes straight there.

In any dialog: Enter presses the default button, Esc cancels, Tab moves
between controls, Space ticks a checkbox.

## Windows 11 differences that catch people out

Right-click menus are shortened. The full classic menu is behind "Show more
options" at the bottom, or Shift+F10 directly.

Start is centred on the taskbar, and the full app list is behind "All apps" at
the top right of the Start panel.

Most of Control Panel has moved into Settings, but Control Panel still exists
and still holds a few things — reach it with Win+R then `control`.

## What controls are actually called

When naming a target so it can be marked on screen, use the accessibility name,
which is often not the visible text:

- Window buttons: "Minimise", "Maximise", "Restore Down", "Close". Spelling
  follows the display language — a British machine says "Minimise", an American
  one "Minimize". If one fails, try the other.
- Taskbar: "Start", "Search", "Task View", "Widgets", "Notification Chevron".
- File Explorer: "Address bar", "Search Box", "Navigation Pane", "Items View",
  and the toolbar buttons "New", "Sort", "View".
- Settings: the left-hand items carry their section name — "System",
  "Bluetooth & devices", "Network & internet", "Accessibility".

## Where automation cannot go

A User Account Control prompt — the dimmed screen asking for permission —
runs on a separate secure desktop. It cannot be clicked programmatically and
does not appear in a screenshot. Ask the person to press Yes themselves, then
carry on.

The same applies to any window belonging to a program running as administrator
when Metis is not: it can be seen but not operated. Say so rather than trying
and silently failing.
