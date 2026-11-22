# Scholar Helper

Small FFXIV Scholar plugin I made for raiding.

## Install
1. Open `/xlsettings` -> **Experimental**.
2. Add this URL under **Custom Plugin Repositories**:
   ```
   https://raw.githubusercontent.com/serisly/ScholarHelper/main/repo.json
   ```
3. Save, open `/xlplugins`, and install **Scholar Helper**.

## Features
Open `/sch` to turn anything on or off. Synced level is checked automatically so lower-level duties stay quiet.

- **Cooldown & fairy glows:** Highlights Scholar cooldowns and fairy actions on your hotbar with the game's proc border, plus optional banners and volume-controlled alert tones.
- **Raise tracking:** Tracks dead party members, co-healer raises, and pending raises. Highlights and alerts only when someone is ready to be raised, with optional Party List tags.
- **Tank HP alert:** Warns when a tank drops below your HP threshold in combat.
- **Reminders:** Optional reminders for Esuna debuffs, low gear durability, raid food (with meal highlights on hotbars and in open bags), and missing Focus Target, with Raids & Trials / Dungeons toggles.
- **AoE range preview:** Hover any enabled Scholar AoE skill to see who is in range via tooltip or Party List tags.
- **Mitigation & slidecast bars:** Combat-only raid mitigation bar under the Party List and an opaque red-to-green slidecast cast bar.
- **Action helpers:** Optional toggles to drop Sacred Soil directly at your feet and cast single-target heals on hovered Party List members without switching targets.

## Commands
- `/sch` or `/scholarhelper`: open settings
