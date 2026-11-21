using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ScholarHelper.Core;

namespace ScholarHelper.Modules;

/// <summary>
/// Compact horizontal icon strip showing active raid mitigations with remaining seconds.
/// Automatically anchors beneath _PartyList unless dragged elsewhere via Move Overlays.
/// </summary>
public sealed class ActiveMitigationStripModule : IHelperModule
{
    private static readonly Vector4 ColorBg = new(0.07f, 0.08f, 0.10f, 0.84f);
    private static readonly Vector4 ColorBorderDefault = new(0.40f, 0.44f, 0.50f, 0.70f);
    private static readonly Vector4 ColorUnlockBorder = new(0.95f, 0.82f, 0.44f, 0.95f);
    private static readonly Vector4 ColorBossTag = new(1.00f, 0.78f, 0.36f, 1f);
    private static readonly Vector4 ColorPartyTag = new(0.58f, 0.92f, 0.82f, 1f);

    private static readonly (uint IconId, string Label, bool IsBoss)[] PreviewMits =
    [
        (213901, "8s", true),
        (212803, "12s", false),
        (213917, "6s", true),
    ];

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IGameGui gameGui;
    private readonly ITextureProvider textureProvider;
    private readonly AlertEngine alertEngine;
    private readonly Configuration config;

    public bool IsPositionUnlocked { get; set; }

    public string Id => "active_mitigation_strip";
    public string DisplayName => "Active Raid Mitigation Strip";
    public string Tooltip => "Icon row of live boss/party mitigations (Reprisal, Addle, Feint, Soil, Kerachole, etc.). Anchors under Party List by default.";

    public ActiveMitigationStripModule(
        IDalamudPluginInterface pluginInterface,
        IGameGui gameGui,
        ITextureProvider textureProvider,
        AlertEngine alertEngine,
        Configuration config)
    {
        this.pluginInterface = pluginInterface;
        this.gameGui = gameGui;
        this.textureProvider = textureProvider;
        this.alertEngine = alertEngine;
        this.config = config;
    }

    public void Enable() { }

    public void DrawOverlay()
    {
        if (!config.Enabled || !config.EnableMitigationStrip) return;

        var state = alertEngine.State;
        var mits = state.ActiveMitigations;
        var hasLive = mits.Count > 0 && (!config.MitStripCombatOnly || state.InCombat);
        if (!IsPositionUnlocked && !hasLive) return;

        var vp = ImGui.GetMainViewport();
        if (config.HasCustomMitPosition)
        {
            var pos = new Vector2(vp.Pos.X + config.MitPosX, vp.Pos.Y + config.MitPosY);
            ImGui.SetNextWindowPos(pos, IsPositionUnlocked ? ImGuiCond.Appearing : ImGuiCond.Always);
        }
        else if (PartyListGeometry.TryGetPartyBounds(gameGui, out var pMin, out var pMax))
        {
            var pos = new Vector2(vp.Pos.X + pMin.X, vp.Pos.Y + pMax.Y + 6f);
            ImGui.SetNextWindowPos(pos, IsPositionUnlocked ? ImGuiCond.Appearing : ImGuiCond.Always);
        }
        else
        {
            var pos = new Vector2(vp.Pos.X + 28f, vp.Pos.Y + (vp.Size.Y * 0.45f));
            ImGui.SetNextWindowPos(pos, IsPositionUnlocked ? ImGuiCond.Appearing : ImGuiCond.Always);
        }

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
                    | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize;
        if (!IsPositionUnlocked) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, ColorBg);
        ImGui.PushStyleColor(ImGuiCol.Border, IsPositionUnlocked ? ColorUnlockBorder : ColorBorderDefault);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.2f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 6f));

        if (ImGui.Begin("ScholarHelperMitStrip###SCHMitStrip", flags))
        {
            if (IsPositionUnlocked)
            {
                if (ImGui.IsWindowHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    var winPos = ImGui.GetWindowPos();
                    config.HasCustomMitPosition = true;
                    config.MitPosX = winPos.X - vp.Pos.X;
                    config.MitPosY = winPos.Y - vp.Pos.Y;
                    config.Save(pluginInterface);
                }

                ImGui.TextColored(ColorUnlockBorder, "Mitigation Strip");
            }

            if (hasLive)
            {
                for (var i = 0; i < mits.Count; i++)
                {
                    if (i > 0) ImGui.SameLine(0f, 8f);
                    var m = mits[i];
                    var timer = m.RemainingSeconds >= 10f
                        ? $"{(int)MathF.Ceiling(m.RemainingSeconds)}s"
                        : $"{m.RemainingSeconds:F1}s";
                    DrawIconTile(m.IconId, timer, m.IsBossDebuff);
                }
            }
            else if (IsPositionUnlocked)
            {
                for (var i = 0; i < PreviewMits.Length; i++)
                {
                    if (i > 0) ImGui.SameLine(0f, 8f);
                    var p = PreviewMits[i];
                    DrawIconTile(p.IconId, p.Label, p.IsBoss);
                }
            }
        }

        ImGui.End();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private void DrawIconTile(uint iconId, string timerText, bool isBossDebuff)
    {
        ImGui.BeginGroup();
        var icon = textureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
        if (icon != null) ImGui.Image(icon.Handle, new Vector2(28f, 34f));

        var pad = MathF.Max(0f, (28f - ImGui.CalcTextSize(timerText).X) * 0.5f);
        if (pad > 0.5f) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + pad);
        ImGui.TextColored(isBossDebuff ? ColorBossTag : ColorPartyTag, timerText);
        ImGui.EndGroup();
    }

    public bool DrawSettings()
    {
        var changed = false;

        var enabled = config.EnableMitigationStrip;
        if (ImGui.Checkbox("Enable Mitigation Strip", ref enabled)) { config.EnableMitigationStrip = enabled; changed = true; }

        if (!config.EnableMitigationStrip) ImGui.BeginDisabled();
        ImGui.Indent(20f);

        var combatOnly = config.MitStripCombatOnly;
        if (ImGui.Checkbox("Combat only##mit", ref combatOnly)) { config.MitStripCombatOnly = combatOnly; changed = true; }

        if (config.HasCustomMitPosition)
        {
            ImGui.SameLine(0f, 14f);
            if (ImGui.SmallButton("Snap Under Party"))
            {
                config.HasCustomMitPosition = false;
                config.MitPosX = 0f;
                config.MitPosY = 0f;
                changed = true;
            }
        }

        ImGui.Unindent(20f);
        if (!config.EnableMitigationStrip) ImGui.EndDisabled();

        return changed;
    }

    public void Dispose() { }
}
