using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;
using ScholarHelper.Core;

namespace ScholarHelper;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    public bool Enabled { get; set; } = true;

    // Unified Alert Banner placement
    public bool HasCustomBannerPosition { get; set; } = false;
    public float BannerPosX { get; set; } = 0f;
    public float BannerPosY { get; set; } = 0f;

    // Unified per-rule settings keyed by AlertRule.Id
    public Dictionary<string, AlertRuleConfig> RuleConfigs { get; set; } = new(StringComparer.Ordinal);

    // Module 1: Generic Ability Range Tooltip
    public bool EnableRangeTooltip { get; set; } = true;
    public bool RangeTooltipFairySkills { get; set; } = true;
    public bool RangeTooltipTargetFairy { get; set; } = true;
    public bool RangeTooltipSelfAoes { get; set; } = true;
    public bool RangeTooltipDeploy { get; set; } = true;

    // Module 2: Active Raid Mitigation Strip (default: snaps cleanly under _PartyList)
    public bool EnableMitigationStrip { get; set; } = true;
    public bool MitStripCombatOnly { get; set; } = true;
    public bool HasCustomMitPosition { get; set; } = false;
    public float MitPosX { get; set; } = 0f;
    public float MitPosY { get; set; } = 0f;

    // Module 3: Direct Party-List Inline Annotations (opt-in alternatives)
    public bool EnablePartyRaiseBadges { get; set; } = false;
    public bool EnablePartyOutOfEosTag { get; set; } = false;

    public void ResetPositions()
    {
        HasCustomBannerPosition = false;
        BannerPosX = 0f;
        BannerPosY = 0f;
        HasCustomMitPosition = false;
        MitPosX = 0f;
        MitPosY = 0f;
    }

    public void ResetModuleDefaults()
    {
        Enabled = true;
        ResetPositions();
        EnableRangeTooltip = true;
        RangeTooltipFairySkills = true;
        RangeTooltipTargetFairy = true;
        RangeTooltipSelfAoes = true;
        RangeTooltipDeploy = true;
        EnableMitigationStrip = true;
        MitStripCombatOnly = true;
        EnablePartyRaiseBadges = false;
        EnablePartyOutOfEosTag = false;
    }

    public void Save(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.SavePluginConfig(this);
    }
}
