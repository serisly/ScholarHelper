# Scholar Helper

Small FFXIV Scholar plugin I made for raiding. Everything is visual or audio only.

## Install
1. Open `/xlsettings` -> **Experimental**.
2. Add this URL under **Custom Plugin Repositories**:
   ```
   https://raw.githubusercontent.com/serisly/ScholarHelper/main/repo.json
   ```
3. Save, open `/xlplugins`, and install **Scholar Helper**.

## Features
Type `/sch` to turn things on or off. Every ability respects your synced level so lower-level duties stay quiet.

- **Hotbar glows & sounds:** Uses the game's built-in proc border (with optional screen text or `SE 1` to `SE 16` sounds) for Scholar cooldowns and fairy reminders.
- **Party alerts:** Shows dead players and active co-healer raises before you hit Swiftcast, plus low tank HP, Esuna debuffs, raid food, and missing Focus Target.
- **Ability range preview:** Hover any enabled Scholar AoE skill (Whispering Dawn, Succor, Sacred Soil, Indomitability, Deployment Tactics, etc.) to see who is in its real game range via tooltip or Party List badges.
- **Mitigation bar:** Small combat-only row under the Party List showing active raid mits with timers.
- **Party List raise badges:** Optional inline Raise tags next to dead Party List rows.

## Commands
- `/sch` or `/scholarhelper`: open settings
