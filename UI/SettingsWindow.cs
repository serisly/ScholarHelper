using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ScholarHelper.Core;
using ScholarHelper.Modules;

namespace ScholarHelper.UI;

public sealed class SettingsWindow : Window
{
    private static readonly Vector4 ColorMuted = new(0.58f, 0.61f, 0.67f, 1f);
    private static readonly Vector4 ColorGold = new(0.95f, 0.83f, 0.45f, 1f);
    private static readonly Vector4 ColorWhite = new(0.95f, 0.96f, 0.98f, 1f);
    private static readonly uint CategoryRowBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.14f, 0.15f, 0.18f, 0.72f));

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly AlertEngine alertEngine;
    private readonly AlertBannerOverlay bannerOverlay;
    private readonly ActiveMitigationStripModule mitStripModule;
    private readonly SlidecastBarModule slidecastBarModule;
    private readonly IReadOnlyList<IHelperModule> modules;

    private bool overlaysUnlocked;

    public SettingsWindow(
        IDalamudPluginInterface pluginInterface,
        Configuration config,
        AlertEngine alertEngine,
        AlertBannerOverlay bannerOverlay,
        ActiveMitigationStripModule mitStripModule,
        SlidecastBarModule slidecastBarModule,
        IReadOnlyList<IHelperModule> modules)
        : base("Scholar Helper###ScholarHelperSettings")
    {
        this.pluginInterface = pluginInterface;
        this.config = config;
        this.alertEngine = alertEngine;
        this.bannerOverlay = bannerOverlay;
        this.mitStripModule = mitStripModule;
        this.slidecastBarModule = slidecastBarModule;
        this.modules = modules;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 520),
            MaximumSize = new Vector2(1200, 960),
        };
        Size = new Vector2(880, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose() => SetOverlaysUnlocked(false);

    private void SetOverlaysUnlocked(bool unlocked)
    {
        overlaysUnlocked = unlocked;
        bannerOverlay.IsPositionUnlocked = unlocked;
        mitStripModule.IsPositionUnlocked = unlocked;
        slidecastBarModule.IsPositionUnlocked = unlocked;
    }

    public override void Draw()
    {
        var changed = false;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);

        var toolbarY = ImGui.GetCursorPosY();
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled)) { config.Enabled = enabled; changed = true; }

        ImGui.SameLine(0f, 14f);
        var unlocked = overlaysUnlocked;
        if (ImGui.Checkbox("Move Overlays", ref unlocked)) SetOverlaysUnlocked(unlocked);

        ImGui.SameLine(0f, 18f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorMuted, "Volume");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Volume for Scholar Helper alert tones (Chime, Bell, Alert, etc.). Game SE 1-16 follow FFXIV System Sounds.");
        }

        ImGui.SameLine(0f, 6f);
        var vol = config.SoundVolume;
        if (UiControls.DrawSleekSliderInt("##vol", ref vol, 0, 100, 76f, toolbarY))
        {
            config.SoundVolume = vol;
            changed = true;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            SoundEffects.Play(SoundEffects.ToneChime, config.SoundVolume);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Volume for Scholar Helper alert tones (Chime, Bell, Alert, etc.). Game SE 1-16 follow FFXIV System Sounds.");
        }

        ImGui.SameLine(0f, 6f);
        ImGui.SetCursorPosY(toolbarY);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorWhite, $"{config.SoundVolume}%");

        ImGui.SameLine(0f, 16f);
        ImGui.SetCursorPosY(toolbarY);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorMuted, "Sound CD");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Minimum seconds between repeated sounds for the same alert.");
        }

        ImGui.SameLine(0f, 6f);
        var scd = config.SoundCooldownSeconds;
        if (UiControls.DrawSleekSliderInt("##scd", ref scd, 1, 30, 66f, toolbarY))
        {
            config.SoundCooldownSeconds = scd;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Minimum seconds between repeated sounds for the same alert.");
        }

        ImGui.SameLine(0f, 6f);
        ImGui.SetCursorPosY(toolbarY);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorWhite, $"{config.SoundCooldownSeconds}s");

        var hasCustomPos = config.HasCustomBannerPosition || config.HasCustomMitPosition || config.HasCustomCastBarPosition;
        var rightEdge = ImGui.GetWindowContentRegionMax().X;
        var resetDefaultsW = ImGui.CalcTextSize("Reset Defaults").X + 16f;
        var resetPosW = hasCustomPos ? ImGui.CalcTextSize("Reset Positions").X + 16f + 8f : 0f;
        var buttonsX = rightEdge - resetDefaultsW - resetPosW;
        if (buttonsX > ImGui.GetCursorPosX() + 12f)
        {
            ImGui.SameLine(buttonsX);
        }
        else
        {
            ImGui.SameLine(0f, 12f);
        }
        ImGui.SetCursorPosY(toolbarY);

        if (hasCustomPos)
        {
            if (ImGui.Button("Reset Positions"))
            {
                config.ResetPositions();
                changed = true;
            }
            ImGui.SameLine(0f, 8f);
        }

        if (ImGui.Button("Reset Defaults"))
        {
            config.ResetModuleDefaults();
            alertEngine.ResetAllRuleConfigs();
            SetOverlaysUnlocked(false);
            changed = true;
        }

        ImGui.Spacing();

        if (!config.Enabled) ImGui.BeginDisabled();

        if (ImGui.BeginTabBar("ScholarHelperTabs"))
        {
            if (ImGui.BeginTabItem("Alerts"))
            {
                changed |= DrawAlertsTable();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Overlays & Actions"))
            {
                changed |= DrawModulesTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (!config.Enabled) ImGui.EndDisabled();

        ImGui.PopStyleVar(2);

        if (changed) config.Save(pluginInterface);
    }

    private bool DrawAlertsTable()
    {
        var changed = false;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8f, 6f));

        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.RowBg
            | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.SizingFixedFit;

        if (!ImGui.BeginTable("ScholarAlertsTable", 5, tableFlags))
        {
            ImGui.PopStyleVar();
            return false;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Alert", ImGuiTableColumnFlags.WidthFixed, 162f);
        ImGui.TableSetupColumn("Glow", ImGuiTableColumnFlags.WidthFixed, 44f);
        ImGui.TableSetupColumn("Banner", ImGuiTableColumnFlags.WidthFixed, 54f);
        ImGui.TableSetupColumn("Sound", ImGuiTableColumnFlags.WidthFixed, 138f);
        ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        DrawHeaderCell(0, "Alert", null);
        DrawHeaderCell(1, "Glow", "Highlights the ability on your hotbar (or dead party row / food in bags).");
        DrawHeaderCell(2, "Banner", "Shows a text line in the floating Alert Banner.");
        DrawHeaderCell(3, "Sound", "Plays an alert tone (uses Volume slider) or in-game SE 1-16.");
        DrawHeaderCell(4, "Options", null);

        changed |= DrawCategorySection("Party", AlertCategory.Party);
        changed |= DrawCategorySection("Reminders", AlertCategory.Reminders);
        changed |= DrawCategorySection("Cooldowns", AlertCategory.Cooldowns);
        changed |= DrawCategorySection("Fairy", AlertCategory.Fairy);

        ImGui.EndTable();
        ImGui.PopStyleVar();
        return changed;
    }

    private static void DrawHeaderCell(int col, string label, string? tooltip)
    {
        ImGui.TableSetColumnIndex(col);
        ImGui.TableHeader(label);
        if (tooltip != null && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static void CenterInCell(float itemWidth)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > itemWidth)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((avail - itemWidth) * 0.5f));
        }
    }

    private bool DrawCategorySection(string header, AlertCategory category)
    {
        var changed = false;

        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, CategoryRowBg);
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorGold, header);

        if (category == AlertCategory.Reminders)
        {
            ImGui.TableSetColumnIndex(4);
            var raids = config.RemindersInRaidsAndTrials;
            if (ImGui.Checkbox("Raids & Trials##rem_rt", ref raids)) { config.RemindersInRaidsAndTrials = raids; changed = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Run Focus Target and Raid Food reminders in Raids and Trials.");

            ImGui.SameLine(0f, 14f);
            var dungeons = config.RemindersInDungeons;
            if (ImGui.Checkbox("Dungeons##rem_dg", ref dungeons)) { config.RemindersInDungeons = dungeons; changed = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Also run Focus Target (on bosses) and Raid Food reminders in Dungeons. Esuna and Repair Gear stay active everywhere.");
        }

        var cbWidth = ImGui.GetFrameHeight();
        var dashWidth = ImGui.CalcTextSize("-").X;

        foreach (var rule in alertEngine.Rules)
        {
            if (rule.Category != category) continue;

            var cfg = alertEngine.GetRuleConfig(rule);
            ImGui.PushID(rule.Id);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            var en = cfg.Enabled;
            if (ImGui.Checkbox("##en", ref en)) { cfg.Enabled = en; changed = true; }
            ImGui.SameLine(0f, 8f);
            ImGui.AlignTextToFramePadding();
            if (!cfg.Enabled) ImGui.TextColored(ColorMuted, rule.DisplayName);
            else ImGui.TextUnformatted(rule.DisplayName);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(rule.Tooltip);
            }

            if (!cfg.Enabled) ImGui.BeginDisabled();

            ImGui.TableSetColumnIndex(1);
            if ((rule.Capabilities & AlertCapabilities.Glow) != 0)
            {
                CenterInCell(cbWidth);
                var glow = cfg.EnableGlow;
                if (ImGui.Checkbox("##gl", ref glow)) { cfg.EnableGlow = glow; changed = true; }
            }
            else
            {
                CenterInCell(dashWidth);
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(ColorMuted, "-");
            }

            ImGui.TableSetColumnIndex(2);
            if ((rule.Capabilities & AlertCapabilities.Banner) != 0)
            {
                CenterInCell(cbWidth);
                var banner = cfg.EnableBanner;
                if (ImGui.Checkbox("##bn", ref banner)) { cfg.EnableBanner = banner; changed = true; }
            }
            else
            {
                CenterInCell(dashWidth);
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(ColorMuted, "-");
            }

            ImGui.TableSetColumnIndex(3);
            if ((rule.Capabilities & AlertCapabilities.Sound) != 0)
            {
                var sound = cfg.EnableSound;
                if (ImGui.Checkbox("##sn", ref sound))
                {
                    cfg.EnableSound = sound;
                    if (sound) SoundEffects.Play(cfg.SoundId, config.SoundVolume);
                    changed = true;
                }

                if (cfg.EnableSound)
                {
                    ImGui.SameLine(0f, 6f);
                    ImGui.SetNextItemWidth(100f);
                    if (ImGui.BeginCombo("##se", SoundEffects.GetLabel(cfg.SoundId)))
                    {
                        foreach (var tone in SoundEffects.BuiltInTones)
                        {
                            var selected = tone.Id == cfg.SoundId;
                            if (ImGui.Selectable(tone.Label, selected))
                            {
                                cfg.SoundId = tone.Id;
                                SoundEffects.Play(tone.Id, config.SoundVolume);
                                changed = true;
                            }
                            if (selected) ImGui.SetItemDefaultFocus();
                        }

                        ImGui.Separator();

                        for (var id = SoundEffects.GameMinId; id <= SoundEffects.GameMaxId; id++)
                        {
                            var selected = id == cfg.SoundId;
                            if (ImGui.Selectable(SoundEffects.GetLabel(id), selected))
                            {
                                cfg.SoundId = id;
                                SoundEffects.Play(id, config.SoundVolume);
                                changed = true;
                            }
                            if (selected) ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                }
            }

            ImGui.TableSetColumnIndex(4);
            changed |= rule.DrawOptions(cfg);

            if (!cfg.Enabled) ImGui.EndDisabled();
            ImGui.PopID();
        }

        return changed;
    }

    private bool DrawModulesTab()
    {
        var changed = false;
        ImGui.Spacing();

        for (var i = 0; i < modules.Count; i++)
        {
            if (i > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            var mod = modules[i];
            ImGui.PushID(mod.Id);
            ImGui.TextColored(ColorGold, mod.DisplayName);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(mod.Tooltip);
            ImGui.Spacing();
            ImGui.Indent(10f);
            changed |= mod.DrawSettings();
            ImGui.Unindent(10f);
            ImGui.PopID();
        }

        return changed;
    }
}
