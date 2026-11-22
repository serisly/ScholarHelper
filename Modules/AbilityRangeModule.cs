using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using ScholarHelper.Core;
using ScholarHelper.Rules;

namespace ScholarHelper.Modules;

public sealed unsafe class AbilityRangeModule : IHelperModule
{
    private enum RangeOrigin { Fairy, Self, DeployTarget }

    private readonly record struct AbilitySource(uint HotbarActionId, uint RangeDataActionId, RangeOrigin Origin);

    private readonly record struct ResolvedAbility(
        uint HotbarActionId,
        string Name,
        byte UnlockLevel,
        float Radius,
        RangeOrigin Origin);

    private const uint JobIconBaseId = 62100;

    private static readonly AbilitySource[] ScholarAoESources =
    [
        new(16537, 803,   RangeOrigin.Fairy),
        new(186,   186,   RangeOrigin.Self),
        new(16538, 805,   RangeOrigin.Fairy),
        new(188,   188,   RangeOrigin.Self),
        new(3583,  3583,  RangeOrigin.Self),
        new(3585,  3585,  RangeOrigin.DeployTarget),
        new(16543, 16544, RangeOrigin.Fairy),
        new(16546, 16547, RangeOrigin.Fairy),
        new(25868, 25868, RangeOrigin.Self),
        new(37013, 37013, RangeOrigin.Self),
        new(37016, 37016, RangeOrigin.Self),
    ];

    private static readonly Dictionary<uint, uint> ReplacementActionAliases = new()
    {
        [16550] = 16537,
        [16551] = 16538,
    };

    private static readonly Vector4 ColorTitle = new(0.95f, 0.84f, 0.45f, 1f);
    private static readonly Vector4 ColorInRange = new(0.55f, 0.90f, 0.60f, 1f);
    private static readonly Vector4 ColorOutRange = new(0.98f, 0.48f, 0.44f, 1f);
    private static readonly Vector4 ColorCyan = new(0.48f, 0.88f, 1.00f, 1f);
    private static readonly Vector4 ColorMuted = new(0.70f, 0.72f, 0.76f, 1f);
    private static readonly Vector4 ColorWhite = new(0.95f, 0.96f, 0.98f, 1f);
    private static readonly Vector4 ColorBg = new(0.08f, 0.09f, 0.11f, 0.92f);
    private static readonly Vector4 ColorBorder = new(0.55f, 0.50f, 0.35f, 0.85f);

    private static readonly uint PillBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.07f, 0.08f, 0.10f, 0.88f));
    private static readonly uint PillIn = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 0.60f, 1f));
    private static readonly uint PillOut = ImGui.ColorConvertFloat4ToU32(new Vector4(0.98f, 0.48f, 0.44f, 1f));

    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private readonly ITextureProvider textureProvider;
    private readonly AlertEngine alertEngine;
    private readonly Configuration config;

    private readonly List<ResolvedAbility> orderedAbilities;
    private readonly Dictionary<uint, ResolvedAbility> abilityByActionId;
    private readonly List<(string Name, uint JobId, float Dist, bool Dead)> inRangeScratch = new(8);
    private readonly List<(string Name, uint JobId, float Dist, bool Dead)> outRangeScratch = new(8);
    private readonly List<PartyRowBounds> rowScratch = new(8);

    public string Id => "ability_range";
    public string DisplayName => "Ability Range Preview";
    public string Tooltip => "Hover any enabled Scholar AoE skill to check who is in its real game range.";

    public AbilityRangeModule(
        IDataManager dataManager,
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

        orderedAbilities = LoadAbilitiesFromGameData(dataManager);
        abilityByActionId = orderedAbilities.ToDictionary(a => a.HotbarActionId);
    }

    private static List<ResolvedAbility> LoadAbilitiesFromGameData(IDataManager dataManager)
    {
        var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        var list = new List<ResolvedAbility>(ScholarAoESources.Length);
        if (sheet == null) return list;

        foreach (var src in ScholarAoESources)
        {
            var hotbarRow = sheet.GetRowOrDefault(src.HotbarActionId);
            var rangeRow = sheet.GetRowOrDefault(src.RangeDataActionId);
            if (!hotbarRow.HasValue || !rangeRow.HasValue) continue;

            var radius = rangeRow.Value.EffectRange;
            if (radius == 0) continue;

            var name = hotbarRow.Value.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name)) continue;

            list.Add(new ResolvedAbility(
                src.HotbarActionId,
                name,
                hotbarRow.Value.ClassJobLevel,
                radius,
                src.Origin));
        }

        list.Sort(static (a, b) =>
        {
            var cmp = a.UnlockLevel.CompareTo(b.UnlockLevel);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return list;
    }

    public void Enable() { }

    public void DrawOverlay()
    {
        if (!config.Enabled || !config.EnableAbilityRange) return;
        if (!config.ShowRangeTooltip && !config.ShowRangePartyBadges) return;

        var state = alertEngine.State;
        if (!state.IsLoggedIn || !state.IsScholar || state.Party.Count == 0) return;

        var detailPtr = gameGui.GetAddonByName("ActionDetail", 1);
        if (detailPtr == nint.Zero) return;
        var detailAddon = (AtkUnitBase*)detailPtr.Address;
        if (detailAddon == null || !detailAddon->IsVisible || detailAddon->RootNode == null || !detailAddon->RootNode->IsVisible()) return;

        var hovered = gameGui.HoveredAction;
        if (hovered.DetailKind != DetailKind.Action) return;

        var hoveredId = hovered.ActionId != 0 ? hovered.ActionId : hovered.BaseActionId;
        if (hoveredId == 0) return;
        if (ReplacementActionAliases.TryGetValue(hoveredId, out var aliasedId)) hoveredId = aliasedId;
        if (!abilityByActionId.TryGetValue(hoveredId, out var ability)) return;
        if (!config.IsRangeActionEnabled(ability.HotbarActionId) || state.Level < ability.UnlockLevel) return;

        if (!TryResolveOrigin(state, ability, out var originPos, out var deployHeader)) return;

        if (config.ShowRangeTooltip)
        {
            DrawTooltip(state, ability, originPos, deployHeader);
        }

        if (config.ShowRangePartyBadges && state.Party.Count > 1)
        {
            DrawPartyRowBadges(state, ability, originPos);
        }
    }

    private bool TryResolveOrigin(
        FrameState state,
        ResolvedAbility ability,
        out Vector3 originPos,
        out string? deployHeader)
    {
        originPos = Vector3.Zero;
        deployHeader = null;

        switch (ability.Origin)
        {
            case RangeOrigin.Fairy:
                if (!state.HasActivePet) return false;
                originPos = state.PetPosition;
                return true;

            case RangeOrigin.Self:
                originPos = state.Position;
                return true;

            case RangeOrigin.DeployTarget:
                var targetId = objectTable.LocalPlayer?.TargetObject?.EntityId ?? 0;
                ResolveDeployOrigin(state, targetId, out originPos, out deployHeader);
                return true;

            default:
                return false;
        }
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
            var crit = c.HasOwnCatalyze ? " [Critlo]" : string.Empty;
            deployHeader = $"Source: {c.Name} | Shield: {c.ShieldPercentage}% HP{crit}";
        }
        else
        {
            originPos = state.Position;
            deployHeader = "Source: Self";
        }
    }

    private void DrawTooltip(FrameState state, ResolvedAbility ability, Vector3 originPos, string? deployHeader)
    {
        inRangeScratch.Clear();
        outRangeScratch.Clear();

        foreach (var m in state.Party)
        {
            var dist = Vector3.Distance(m.Position, originPos);
            var entry = (m.Name, m.ClassJobId, dist, m.IsDead);
            if (dist <= ability.Radius) inRangeScratch.Add(entry);
            else outRangeScratch.Add(entry);
        }

        inRangeScratch.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));
        outRangeScratch.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));

        var vp = ImGui.GetMainViewport();
        var mouse = ImGui.GetMousePos();
        var preferLeft = mouse.X > vp.Pos.X + (vp.Size.X * 0.65f);
        var anchor = new Vector2(preferLeft ? mouse.X - 18f : mouse.X + 18f, mouse.Y - 12f);
        var pivot = new Vector2(preferLeft ? 1f : 0f, 1f);

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
            ImGui.TextColored(ColorTitle, $"{ability.Name} ({inRangeScratch.Count}/{total} in {ability.Radius:F0}y)");
            if (!string.IsNullOrEmpty(deployHeader)) ImGui.TextColored(ColorCyan, deployHeader);
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

    private void DrawPartyRowBadges(FrameState state, ResolvedAbility ability, Vector3 originPos)
    {
        if (!PartyListGeometry.TryGetPartyBounds(gameGui, out _, out _, rowScratch) || rowScratch.Count == 0) return;

        var vp = ImGui.GetMainViewport();
        var draw = ImGui.GetForegroundDrawList(vp);

        foreach (var row in rowScratch)
        {
            if (!PartyListGeometry.TryMatchMember(state.Party, row, out var m) || m.IsDead) continue;

            var dist = Vector3.Distance(m.Position, originPos);
            var inRange = dist <= ability.Radius;
            var label = inRange ? $"IN {dist:F0}y" : $"OUT {dist:F0}y";
            var x = vp.Pos.X + row.Max.X + 6f;
            var centerY = vp.Pos.Y + ((row.Min.Y + row.Max.Y) * 0.5f);
            PartyListGeometry.DrawRowPill(draw, x, centerY, label, PillBg, inRange ? PillIn : PillOut);
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

        var enabled = config.EnableAbilityRange;
        if (ImGui.Checkbox("Enable Ability Range Preview", ref enabled))
        {
            config.EnableAbilityRange = enabled;
            changed = true;
        }

        if (!config.EnableAbilityRange) ImGui.BeginDisabled();
        ImGui.Indent(16f);

        var showTooltip = config.ShowRangeTooltip;
        if (ImGui.Checkbox("Hover tooltip", ref showTooltip)) { config.ShowRangeTooltip = showTooltip; changed = true; }

        ImGui.SameLine(0f, 18f);
        var showBadges = config.ShowRangePartyBadges;
        if (ImGui.Checkbox("Party List IN/OUT badges while hovering", ref showBadges)) { config.ShowRangePartyBadges = showBadges; changed = true; }

        ImGui.Spacing();

        if (ImGui.BeginTable("ScholarRangeAbilities", 2, ImGuiTableFlags.SizingStretchSame))
        {
            for (var i = 0; i < orderedAbilities.Count; i++)
            {
                if (i % 2 == 0) ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(i % 2);

                var ab = orderedAbilities[i];
                var isOn = config.IsRangeActionEnabled(ab.HotbarActionId);
                var originTag = ab.Origin == RangeOrigin.Fairy ? "Fairy" : (ab.Origin == RangeOrigin.DeployTarget ? "Target" : "Self");
                if (ImGui.Checkbox($"{ab.Name} (Lv{ab.UnlockLevel}, {ab.Radius:F0}y {originTag})##rng_{ab.HotbarActionId}", ref isOn))
                {
                    config.AbilityRangeEnabled[ab.HotbarActionId] = isOn;
                    changed = true;
                }
            }
            ImGui.EndTable();
        }

        ImGui.Unindent(16f);
        if (!config.EnableAbilityRange) ImGui.EndDisabled();

        return changed;
    }

    public void Dispose() { }
}
