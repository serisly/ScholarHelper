# Scholar Helper

Lightweight Scholar & healer assistant for Final Fantasy XIV (Dalamud API 15). Purely read-only visual & audio reminders — zero combat automation.

## Install (Dalamud Custom Repository)
1. Open `/xlsettings` in FFXIV -> **Experimental** tab.
2. Under **Custom Plugin Repositories**, add:
   ```
   https://raw.githubusercontent.com/serisly/ScholarHelper/main/repo.json
   ```
3. Click **Save**, open `/xlplugins`, search **Scholar Helper**, and install.

## Features
- **Unified Alerts (`/sch`):** Every reminder supports native hotbar proc **Glow**, **Screen Banner** (with party job icons & player names), and opt-in built-in **`SE 1`–`SE 16`** audio chimes (with instant preview when selected). All abilities strictly auto-gate by synced level.
  - **Party:** Raise Tracker (dead / active co-healer raise / pending), Tank Low HP, Esuna (white-bar debuffs), Whispering Dawn, Missing Focus Target, Raid Food pre-pull check.
  - **Cooldowns:** Lucid Dreaming, Bio / Bio II / Biolysis DoT, Aetherflow, Critlo Deploy (`Lv56+`), Chain Stratagem (`Lv66+`), Fey Gauge Cap (`Lv70+`), Recitation (`Lv74+`).
  - **Fairy:** Missing Fairy (`Summon Eos`) & Fairy Leash Distance (`Heel`).
- **Ability Range Tooltip:** Hover fairy skills, self AoEs (`Sacred Soil`, `Succor`, `Indomitability`, `Expedient`), or `Deployment Tactics` (with live target shield %) — or target Eos in-world — to see party members in/out of range with job icons.
- **Active Raid Mitigation Strip:** Compact combat-only icon row of live boss/party mitigations (`Reprisal`, `Addle`, `Feint`, `Sacred Soil`, `Kerachole`, `Temperance`, etc.), anchored beneath the Party List by default.
- **Party List Inline Badges:** Optional raise status and out-of-Eos-range tags drawn beside native Party List rows.

## Commands
- `/sch` or `/scholarhelper` — Open settings
- `/parse` — Toggle LMeter meters #1 & #2
