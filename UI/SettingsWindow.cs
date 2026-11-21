using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ScholarHelper.Core;
using ScholarHelper.Modules;

namespace ScholarHelper.UI;

/// <summary>
/// Clean 2-tab Settings UI:
/// - Alerts: compact aligned table (Alert | Glow | Banner | Sound | Options) with hover tooltips and live sound audition.
/// - Overlays: Range Tooltip, Raid Mitigation Strip, and Party List Inline Badges.
/// </summary>
public sealed class SettingsWindow : Window
{
    private static readonly Vector4 ColorMuted = new(0.64f, 0.67f, 0.72f, 1f);
    private static readonly Vector4 ColorGold = new(0.95f, 0.83f, 0.45f, 1f);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly AlertEngine alertEngine;
    private readonly AlertBannerOverlay bannerOverlay;
    private readonly ActiveMitigationStripModule mitStripModule;
    private readonly IReadOnlyList<IHelperModule> modules;

    private bool overlaysUnlocked;

    public SettingsWindow(
        IDalamudPluginInterface pluginInterface,
        Configuration config,
        AlertEngine alertEngine,
        AlertBannerOverlay bannerOverlay,
        ActiveMitigationStripModule mitStripModule,
        IReadOnlyList<IHelperModule> modules)
        : base("Scholar Helper###ScholarHelperSettings")
    {
        this.pluginInterface = pluginInterface;
        this.config = config;
        this.alertEngine = alertEngine;
        this.bannerOverlay = bannerOverlay;
        this.mitStripModule = mitStripModule;
        this.modules = modules;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(680, 460),
            MaximumSize = new Vector2(1050, 920),
        };
        Size = new Vector2(760, 590);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose() => SetOverlaysUnlocked(false);

    private void SetOverlaysUnlocked(bool unlocked)
    {
        overlaysUnlocked = unlocked;
        bannerOverlay.IsPositionUnlocked = unlocked;
        mitStripModule.IsPositionUnlocked = unlocked;
    }

    public override void Draw()
    {
        var changed = false;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled)) { config.Enabled = enabled; changed = true; }

        ImGui.SameLine(0f, 18f);
        var unlocked = overlaysUnlocked;
        if (ImGui.Checkbox("Move Overlays", ref unlocked)) SetOverlaysUnlocked(unlocked);

        if (config.HasCustomBannerPosition || config.HasCustomMitPosition)
        {
            ImGui.SameLine(0f, 10f);
            if (ImGui.SmallButton("Reset Positions"))
            {
                config.ResetPositions();
                changed = true;
            }
        }

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 100f);
        if (ImGui.SmallButton("Reset Defaults"))
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

            if (ImGui.BeginTabItem("Overlays & Tooltips"))
            {
                changed |= DrawModulesTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (!config.Enabled) ImGui.EndDisabled();

        if (changed) config.Save(pluginInterface);
    }

    private bool DrawAlertsTable()
    {
        var changed = false;

        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.RowBg
            | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.SizingFixedFit;

        if (!ImGui.BeginTable("ScholarAlertsTable", 5, tableFlags))
        {
            return false;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Alert", ImGuiTableColumnFlags.WidthFixed, 175f);
        ImGui.TableSetupColumn("Glow", ImGuiTableColumnFlags.WidthFixed, 44f);
        ImGui.TableSetupColumn("Banner", ImGuiTableColumnFlags.WidthFixed, 54f);
        ImGui.TableSetupColumn("Sound", ImGuiTableColumnFlags.WidthFixed, 105f);
        ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        changed |= DrawCategorySection("Party", AlertCategory.Party);
        changed |= DrawCategorySection("Cooldowns", AlertCategory.Cooldowns);
        changed |= DrawCategorySection("Fairy", AlertCategory.Fairy);

        ImGui.EndTable();
        return changed;
    }

    private bool DrawCategorySection(string header, AlertCategory category)
    {
        var changed = false;

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Spacing();
        ImGui.TextColored(ColorGold, header);

        foreach (var rule in alertEngine.Rules)
        {
            if (rule.Category != category) continue;

            var cfg = alertEngine.GetRuleConfig(rule);
            ImGui.PushID(rule.Id);
            ImGui.TableNextRow();

            // Col 0: Enable + Name (tooltip on hover)
            ImGui.TableSetColumnIndex(0);
            var en = cfg.Enabled;
            if (ImGui.Checkbox("##en", ref en)) { cfg.Enabled = en; changed = true; }
            ImGui.SameLine(0f, 6f);
            if (!cfg.Enabled) ImGui.TextColored(ColorMuted, rule.DisplayName);
            else ImGui.TextUnformatted(rule.DisplayName);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(rule.Tooltip);
            }

            if (!cfg.Enabled) ImGui.BeginDisabled();

            // Col 1: Glow
            ImGui.TableSetColumnIndex(1);
            if ((rule.Capabilities & AlertCapabilities.Glow) != 0)
            {
                var glow = cfg.EnableGlow;
                if (ImGui.Checkbox("##gl", ref glow)) { cfg.EnableGlow = glow; changed = true; }
            }
            else
            {
                ImGui.TextColored(ColorMuted, "-");
            }

            // Col 2: Banner
            ImGui.TableSetColumnIndex(2);
            if ((rule.Capabilities & AlertCapabilities.Banner) != 0)
            {
                var banner = cfg.EnableBanner;
                if (ImGui.Checkbox("##bn", ref banner)) { cfg.EnableBanner = banner; changed = true; }
            }
            else
            {
                ImGui.TextColored(ColorMuted, "-");
            }

            // Col 3: Sound + live preview dropdown
            ImGui.TableSetColumnIndex(3);
            if ((rule.Capabilities & AlertCapabilities.Sound) != 0)
            {
                var sound = cfg.EnableSound;
                if (ImGui.Checkbox("##sn", ref sound))
                {
                    cfg.EnableSound = sound;
                    if (sound) SoundEffects.Play(cfg.SoundId);
                    changed = true;
                }

                if (cfg.EnableSound)
                {
                    ImGui.SameLine(0f, 5f);
                    ImGui.SetNextItemWidth(68f);
                    if (ImGui.BeginCombo("##se", SoundEffects.GetLabel(cfg.SoundId)))
                    {
                        for (var id = SoundEffects.MinId; id <= SoundEffects.MaxId; id++)
                        {
                            var selected = id == cfg.SoundId;
                            if (ImGui.Selectable(SoundEffects.GetLabel(id), selected))
                            {
                                cfg.SoundId = id;
                                SoundEffects.Play(id);
                                changed = true;
                            }
                            if (selected) ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                }
            }

            // Col 4: Rule-specific compact inline options
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
