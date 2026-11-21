# Scholar Helper

Personal FFXIV Scholar plugin (Dalamud API 15) I made for raiding. Visual and audio reminders only, no automation.

## Install
1. In FFXIV, open `/xlsettings` -> **Experimental**.
2. Add this under **Custom Plugin Repositories**:
   ```
   https://raw.githubusercontent.com/serisly/ScholarHelper/main/repo.json
   ```
3. Save, open `/xlplugins`, and install **Scholar Helper**.

## Features
Open `/sch` to toggle anything on or off. Synced level is checked automatically so lower-level duties stay quiet.

- **Hotbar glows & sounds:** Uses the native proc border (with optional screen text or in-game `SE 1` to `SE 16` sounds) for Scholar cooldowns and fairy reminders.
- **Party alerts:** Shows dead players and active co-healer raises before you hit Swiftcast, plus low tank HP, Esuna debuffs, raid food, and missing Focus Target in duties.
- **Range tooltips:** Hover fairy or AoE skills (or target Eos) to see which party members are in range.
- **Mitigation bar:** Small combat-only row under the Party List showing active raid mits and their timers.
- **Party List badges:** Optional inline Raise and out-of-Eos-range tags next to Party List rows.

## Commands
- `/sch` or `/scholarhelper`: settings
- `/parse`: toggle LMeter windows 1 and 2
