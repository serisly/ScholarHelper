using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ScholarHelper.Core;

public enum AlertCategory
{
    Party,
    Cooldowns,
    Fairy,
}

[Flags]
public enum AlertCapabilities
{
    None = 0,
    Glow = 1 << 0,
    Banner = 1 << 1,
    Sound = 1 << 2,
}

public readonly record struct HotbarGlowTarget(ActionType ActionType, uint ActionId);

public readonly record struct BannerSegment(string Text, Vector4? Color = null, uint JobIconClassId = 0)
{
    public bool IsJobIcon => JobIconClassId != 0;
    public static BannerSegment Plain(string text, Vector4? color = null) => new(text, color, 0);
    public static BannerSegment Job(uint classJobId) => new(string.Empty, null, classJobId);
}

public sealed record AlertBannerLine(
    string RuleId,
    int Priority,
    Vector4 AccentColor,
    IReadOnlyList<BannerSegment> Segments);

public sealed record AlertEvaluation(
    bool IsActive,
    IReadOnlyList<HotbarGlowTarget>? Glows = null,
    IReadOnlyList<AlertBannerLine>? Banners = null,
    string? EdgeKey = null)
{
    public static readonly AlertEvaluation Inactive = new(false);

    public static AlertEvaluation Active(
        HotbarGlowTarget? glow = null,
        AlertBannerLine? banner = null,
        string? edgeKey = null)
        => new(
            true,
            glow.HasValue ? [glow.Value] : null,
            banner != null ? [banner] : null,
            edgeKey);
}

[Serializable]
public sealed class AlertRuleConfig
{
    public bool Enabled { get; set; } = true;
    public bool EnableGlow { get; set; } = true;
    public bool EnableBanner { get; set; } = false;
    public bool EnableSound { get; set; } = false;
    public int SoundId { get; set; } = 1;
    public float SoundCooldownSeconds { get; set; } = 8f;

    public int Threshold { get; set; }
    public float FloatThreshold { get; set; }
    public int MinCount { get; set; } = 1;
    public bool CombatOnly { get; set; } = true;
    public bool AllJobs { get; set; } = true;
    public bool Flag { get; set; } = false;
}

public static class SoundEffects
{
    public const int MinId = 1;
    public const int MaxId = 16;

    public static string GetLabel(int soundId) => $"SE {Math.Clamp(soundId, MinId, MaxId)}";

    public static void Play(int soundId)
    {
        try
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(soundId, MinId, MaxId));
        }
        catch
        {
            // Ignore audio failures during teardown.
        }
    }
}

public readonly record struct ActiveMitigation(
    uint StatusId,
    uint IconId,
    string Name,
    float RemainingSeconds,
    bool IsBossDebuff);

public readonly record struct PartyMemberState(
    uint EntityId,
    ulong GameObjectId,
    string Name,
    uint ClassJobId,
    byte Role,
    uint CurrentHp,
    uint MaxHp,
    float HpPercent,
    byte ShieldPercentage,
    Vector3 Position,
    bool IsLocalPlayer,
    bool IsDead,
    bool HasPendingRaise,
    string? RaisingByName,
    uint RaisingByJobId,
    bool HasOwnCatalyze,
    string? DispellableDebuffName);

public sealed unsafe class FrameState
{
    public const uint ScholarClassJobId = 28;
    public const byte RoleTank = 1;
    public const byte RoleHealer = 4;

    public static readonly FrameState Empty = new();

    public bool IsLoggedIn { get; init; }
    public uint SelfEntityId { get; init; }
    public uint ClassJobId { get; init; }
    public byte Role { get; init; }
    public byte Level { get; init; }
    public bool IsAlive { get; init; }
    public uint CurrentMp { get; init; }
    public Vector3 Position { get; init; }
    public bool IsScholar => ClassJobId == ScholarClassJobId;
    public bool IsHealer => Role == RoleHealer;
    public bool IsCasting { get; init; }
    public uint CastActionId { get; init; }
    public bool InCombat { get; init; }
    public bool InDuty { get; init; }
    public bool IsPetSuppressed { get; init; }

    public bool HasFocusTarget { get; init; }
    public bool HasFoodBuff { get; init; }
    public float FoodRemainingMinutes { get; init; }

    public byte AetherflowStacks { get; init; }
    public byte FairyGauge { get; init; }
    public short SeraphTimer { get; init; }
    public bool HasActivePet { get; init; }
    public bool IsDissipated { get; init; }
    public bool IsFairyDismissed { get; init; }
    public uint PetEntityId { get; init; }
    public Vector3 PetPosition { get; init; }
    public float FairyDistanceYalms { get; init; } = -1f;

    public bool HasHostileTarget { get; init; }
    public float OwnBioRemainingSeconds { get; init; }

    public IReadOnlyList<PartyMemberState> Party { get; init; } = Array.Empty<PartyMemberState>();
    public IReadOnlyList<ActiveMitigation> ActiveMitigations { get; init; } = Array.Empty<ActiveMitigation>();

    // Valid only during the current Framework tick.
    public ActionManager* ActionManagerPtr { get; init; }

    public bool IsOffCooldown(uint actionId)
        => ActionManagerPtr != null && !ActionManagerPtr->IsRecastTimerActive(ActionType.Action, actionId);
}

public abstract class AlertRule
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Tooltip { get; }
    public abstract AlertCategory Category { get; }
    public abstract byte MinLevel { get; }
    public virtual bool ScholarOnly => true;
    public virtual bool AllowAllJobsOption => false;
    public virtual bool RequireAlive => true;
    public abstract AlertCapabilities Capabilities { get; }

    public abstract AlertRuleConfig CreateDefaultConfig();
    public abstract AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg);

    /// <summary>Draws compact inline controls in the Options column. Return true if cfg changed.</summary>
    public virtual bool DrawOptions(AlertRuleConfig cfg) => false;
}

public interface IHelperModule : IDisposable
{
    string Id { get; }
    string DisplayName { get; }
    string Tooltip { get; }

    void Enable();
    void DrawOverlay();
    bool DrawSettings();
}
