using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using ScholarHelper.Core;
using ScholarHelper.Rules;

namespace ScholarHelper.Modules;

public sealed class AbilityRangeTooltipModule : IHelperModule
{
    private enum RangeOrigin { Fairy, Self, DeployTarget }

    private readonly record struct AbilityRangeSpec(string Label, float Radius, RangeOrigin Origin, byte MinLevel);

    private const uint FeyIlluminationActionId = 16538;
    private const uint FeyBlessingActionId = 16543;
    private const uint JobIconBaseId = 62100;

    private static readonly Dictionary<uint, AbilityRangeSpec> ActionSpecs = new()
    {
        [ScholarAlertRules.SummonEosActionId]         = new("Eos Aura (Whispering Dawn)", 15f, RangeOrigin.Fairy, 30),
        [ScholarAlertRules.WhisperingDawnActionId]    = new("Whispering Dawn",            15f, RangeOrigin.Fairy, 20),
        [FeyBlessingActionId]                         = new("Fey Blessing",               20f, RangeOrigin.Fairy, 76),
        [FeyIlluminationActionId]                     = new("Fey Illumination",           30f, RangeOrigin.Fairy, 40),
        [ScholarAlertRules.SacredSoilActionId]        = new("Sacred Soil (<me>)",         15f, RangeOrigin.Self, 50),
        [ScholarAlertRules.SuccorActionId]            = new("Succor",                     20f, RangeOrigin.Self, 35),
        [ScholarAlertRules.ConcitationActionId]       = new("Concitation",                20f, RangeOrigin.Self, 96),
        [ScholarAlertRules.IndomitabilityActionId]    = new("Indomitability",             20f, RangeOrigin.Self, 52),
        [ScholarAlertRules.ExpedientActionId]         = new("Expedient",                  30f, RangeOrigin.Self, 90),
        [ScholarAlertRules.DeploymentTacticsActionId] = new("Deployment Tactics",         30f, RangeOrigin.DeployTarget, 56),
    };

    private static readonly Vector4 ColorTitle = new(0.95f, 0.84f, 0.45f, 1f);
    private static readonly Vector4 ColorInRange = new(0.55f, 0.90f, 0.60f, 1f);
    private static readonly Vector4 ColorOutRange = new(0.98f, 0.48f, 0.44f, 1f);
    private static readonly Vector4 ColorCyan = new(0.48f, 0.88f, 1.00f, 1f);
    private static readonly Vector4 ColorMuted = new(0.70f, 0.72f, 0.76f, 1f);
    private static readonly Vector4 ColorWhite = new(0.95f, 0.96f, 0.98f, 1f);
    private static readonly Vector4 ColorBg = new(0.08f, 0.09f, 0.11f, 0.92f);
    private static readonly Vector4 ColorBorder = new(0.55f, 0.50f, 0.35f, 0.85f);

    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private readonly ITextureProvider textureProvider;
    private readonly AlertEngine alertEngine;
    private readonly Configuration config;

    private readonly List<(string Name, uint JobId, float Dist, bool Dead)> inRangeScratch = new(8);
    private readonly List<(string Name, uint JobId, float Dist, bool Dead)> outRangeScratch = new(8);

    public string Id => "ability_range_tooltip";
    public string DisplayName => "Ability Range Tooltip";
    public string Tooltip => "Hover an AoE or fairy skill (or target Eos) to see party members in/out of range.";

    public AbilityRangeTooltipModule(
        IGameGui gameGui,
        IObjectTable objectTable,
        ITextureProvider textureProvider,
        AlertEngine alertEngine,
        Configuration config)
    {
        this.gameGui = gameGui;
        this.objectTable = objectTable;
        this.textureProvider = textureProvider;
        this.alertEngine = alertEngine;
        this.config = config;
    }

    public void Enable() { }

    public void DrawOverlay()
    {
        if (!config.Enabled || !config.EnableRangeTooltip) return;

        var state = alertEngine.State;
        if (!state.IsLoggedIn || !state.IsScholar) return;

        var targetEntityId = objectTable.LocalPlayer?.TargetObject?.EntityId ?? 0;
        if (!TryResolveTrigger(state, targetEntityId, out var spec, out var originPos, out var followCursor, out var deployHeader))
        {
            return;
        }

        if (state.Level < spec.MinLevel) return;

        inRangeScratch.Clear();
        outRangeScratch.Clear();

        foreach (var m in state.Party)
        {
            var dist = Vector3.Distance(m.Position, originPos);
            var entry = (m.Name, m.ClassJobId, dist, m.IsDead);
            if (dist <= spec.Radius) inRangeScratch.Add(entry);
            else outRangeScratch.Add(entry);
        }

        if (inRangeScratch.Count == 0 && outRangeScratch.Count == 0) return;

        inRangeScratch.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));
        outRangeScratch.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));

        var vp = ImGui.GetMainViewport();
        Vector2 anchor;
        Vector2 pivot;
        if (followCursor)
        {
            var mouse = ImGui.GetMousePos();
            var preferLeft = mouse.X > vp.Pos.X + (vp.Size.X * 0.65f);
            anchor = new Vector2(preferLeft ? mouse.X - 18f : mouse.X + 18f, mouse.Y - 12f);
            pivot = new Vector2(preferLeft ? 1f : 0f, 1f);
        }
        else
        {
            anchor = new Vector2(vp.Pos.X + (vp.Size.X * 0.5f), vp.Pos.Y + (vp.Size.Y * 0.68f));
            pivot = new Vector2(0.5f, 0.5f);
        }

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, pivot);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ColorBg);
        ImGui.PushStyleColor(ImGuiCol.Border, ColorBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.2f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize;

        if (ImGui.Begin("ScholarHelperRangeTooltip###SCHRangeTooltip", flags))
        {
            var total = inRangeScratch.Count + outRangeScratch.Count;
            ImGui.TextColored(ColorTitle, $"{spec.Label}  ({inRangeScratch.Count}/{total} in {spec.Radius:F0}y)");
            if (!string.IsNullOrEmpty(deployHeader))
            {
                ImGui.TextColored(ColorCyan, deployHeader);
            }
            ImGui.Separator();

            if (inRangeScratch.Count > 0)
            {
                ImGui.TextColored(ColorInRange, $"In Range ({inRangeScratch.Count})");
                foreach (var item in inRangeScratch) DrawMemberRow(item.JobId, item.Name, item.Dist, item.Dead, true);
            }

            if (outRangeScratch.Count > 0)
            {
                if (inRangeScratch.Count > 0) ImGui.Spacing();
                ImGui.TextColored(ColorOutRange, $"Out of Range ({outRangeScratch.Count})");
                foreach (var item in outRangeScratch) DrawMemberRow(item.JobId, item.Name, item.Dist, item.Dead, false);
            }
        }

        ImGui.End();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private bool TryResolveTrigger(
        FrameState state,
        uint targetEntityId,
        out AbilityRangeSpec spec,
        out Vector3 originPos,
        out bool followCursor,
        out string? deployHeader)
    {
        spec = default;
        originPos = Vector3.Zero;
        followCursor = true;
        deployHeader = null;

        var hovered = gameGui.HoveredAction;
        var hoveredId = hovered.ActionId != 0 ? hovered.ActionId : hovered.BaseActionId;

        if (hoveredId != 0 && ActionSpecs.TryGetValue(hoveredId, out var matched))
        {
            switch (matched.Origin)
            {
                case RangeOrigin.Fairy:
                    if (!config.RangeTooltipFairySkills || !state.HasActivePet) return false;
                    spec = matched;
                    originPos = state.PetPosition;
                    return true;

                case RangeOrigin.Self:
                    if (!config.RangeTooltipSelfAoes) return false;
                    spec = matched;
                    originPos = state.Position;
                    return true;

                case RangeOrigin.DeployTarget:
                    if (!config.RangeTooltipDeploy) return false;
                    spec = matched;
                    ResolveDeployOrigin(state, targetEntityId, out originPos, out deployHeader);
                    return true;
            }
        }

        if (config.RangeTooltipTargetFairy && state.HasActivePet && targetEntityId != 0 && targetEntityId == state.PetEntityId)
        {
            spec = new AbilityRangeSpec("Eos Aura (Whispering Dawn)", 15f, RangeOrigin.Fairy, 30);
            originPos = state.PetPosition;
            followCursor = false;
            return true;
        }

        return false;
    }

    private static void ResolveDeployOrigin(FrameState state, uint targetEntityId, out Vector3 originPos, out string deployHeader)
    {
        PartyMemberState? chosen = null;
        if (targetEntityId != 0)
        {
            foreach (var m in state.Party)
            {
                if (m.EntityId == targetEntityId) { chosen = m; break; }
            }
        }

        if (!chosen.HasValue)
        {
            foreach (var m in state.Party)
            {
                if (m.IsLocalPlayer) { chosen = m; break; }
            }
        }

        if (chosen.HasValue)
        {
            var c = chosen.Value;
            originPos = c.Position;
            var crit = c.HasOwnCatalyze ? "  [Critlo]" : string.Empty;
            deployHeader = $"Source: {c.Name}  |  Shield: {c.ShieldPercentage}% HP{crit}";
        }
        else
        {
            originPos = state.Position;
            deployHeader = "Source: Self";
        }
    }

    private void DrawMemberRow(uint jobId, string name, float dist, bool dead, bool inRange)
    {
        if (jobId != 0)
        {
            var icon = textureProvider.GetFromGameIcon(new GameIconLookup(JobIconBaseId + jobId)).GetWrapOrDefault();
            if (icon != null)
            {
                ImGui.Image(icon.Handle, new Vector2(18f, 18f));
                ImGui.SameLine(0f, 6f);
            }
        }

        ImGui.TextColored(dead ? ColorMuted : (inRange ? ColorWhite : ColorOutRange), dead ? $"{name} (KO)" : name);
        ImGui.SameLine(0f, 10f);
        ImGui.TextColored(inRange ? ColorInRange : ColorOutRange, $"{dist:F1}y");
    }

    public bool DrawSettings()
    {
        var changed = false;

        var enabled = config.EnableRangeTooltip;
        if (ImGui.Checkbox("Enable Range Tooltip", ref enabled)) { config.EnableRangeTooltip = enabled; changed = true; }

        if (!config.EnableRangeTooltip) ImGui.BeginDisabled();
        ImGui.Indent(20f);

        var fairy = config.RangeTooltipFairySkills;
        if (ImGui.Checkbox("Fairy skills (Eos / Dawn 15y, Blessing 20y, Illumination 30y)", ref fairy)) { config.RangeTooltipFairySkills = fairy; changed = true; }

        var targetFairy = config.RangeTooltipTargetFairy;
        if (ImGui.Checkbox("Targeting Eos / Seraph in-world (15y)", ref targetFairy)) { config.RangeTooltipTargetFairy = targetFairy; changed = true; }

        var selfAoe = config.RangeTooltipSelfAoes;
        if (ImGui.Checkbox("Self AoEs (Sacred Soil 15y, Succor / Indom 20y, Expedient 30y)", ref selfAoe)) { config.RangeTooltipSelfAoes = selfAoe; changed = true; }

        var deploy = config.RangeTooltipDeploy;
        if (ImGui.Checkbox("Deployment Tactics (30y + target shield %)", ref deploy)) { config.RangeTooltipDeploy = deploy; changed = true; }

        ImGui.Unindent(20f);
        if (!config.EnableRangeTooltip) ImGui.EndDisabled();

        return changed;
    }

    public void Dispose() { }
}
