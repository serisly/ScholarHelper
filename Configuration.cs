using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;
using ScholarHelper.Core;

namespace ScholarHelper;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 8;

    public bool Enabled { get; set; } = true;
    public int SoundCooldownSeconds { get; set; } = 8;
    public int SoundVolume { get; set; } = 85;

    public bool RemindersInRaidsAndTrials { get; set; } = true;
    public bool RemindersInDungeons { get; set; } = false;

    public bool HasCustomBannerPosition { get; set; } = false;
    public float BannerPosX { get; set; } = 0f;
    public float BannerPosY { get; set; } = 0f;

    public Dictionary<string, AlertRuleConfig> RuleConfigs { get; set; } = new(StringComparer.Ordinal);

    public bool EnableAbilityRange { get; set; } = true;
    public bool ShowRangeTooltip { get; set; } = true;
    public bool ShowRangePartyBadges { get; set; } = false;
    public Dictionary<uint, bool> AbilityRangeEnabled { get; set; } = new();

    public bool EnableMitigationStrip { get; set; } = true;
    public bool MitStripCombatOnly { get; set; } = true;
    public bool HasCustomMitPosition { get; set; } = false;
    public float MitPosX { get; set; } = 0f;
    public float MitPosY { get; set; } = 0f;

    public bool EnableSlidecastBar { get; set; } = true;
    public int SlidecastWindowMs { get; set; } = 500;
    public int SlidecastBarWidth { get; set; } = 260;
    public int SlidecastBarHeight { get; set; } = 28;
    public bool HasCustomCastBarPosition { get; set; } = false;
    public float CastBarPosX { get; set; } = 0f;
    public float CastBarPosY { get; set; } = 0f;

    public bool SacredSoilAtSelf { get; set; } = false;
    public bool MouseoverPartyHealing { get; set; } = false;

    public bool IsRangeActionEnabled(uint actionId)
        => !AbilityRangeEnabled.TryGetValue(actionId, out var enabled) || enabled;

    public void ResetPositions()
    {
        HasCustomBannerPosition = false;
        BannerPosX = 0f;
        BannerPosY = 0f;
        HasCustomMitPosition = false;
        MitPosX = 0f;
        MitPosY = 0f;
        HasCustomCastBarPosition = false;
        CastBarPosX = 0f;
        CastBarPosY = 0f;
    }

    public void ResetModuleDefaults()
    {
        Enabled = true;
        SoundCooldownSeconds = 8;
        SoundVolume = 85;
        RemindersInRaidsAndTrials = true;
        RemindersInDungeons = false;
        ResetPositions();
        EnableAbilityRange = true;
        ShowRangeTooltip = true;
        ShowRangePartyBadges = false;
        AbilityRangeEnabled.Clear();
        EnableMitigationStrip = true;
        MitStripCombatOnly = true;
        EnableSlidecastBar = true;
        SlidecastWindowMs = 500;
        SlidecastBarWidth = 260;
        SlidecastBarHeight = 28;
        SacredSoilAtSelf = false;
        MouseoverPartyHealing = false;
    }

    public void Save(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.SavePluginConfig(this);
    }
}
