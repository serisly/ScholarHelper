using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ScholarHelper.Core;

namespace ScholarHelper.UI;

public sealed class AlertBannerOverlay : Window
{
    private const uint JobIconBaseId = 62100;

    private static readonly Vector4 ColorBg = new(0.08f, 0.08f, 0.10f, 0.86f);
    private static readonly Vector4 ColorUnlockBorder = new(0.95f, 0.82f, 0.44f, 0.95f);
    private static readonly Vector4 ColorWhite = new(0.95f, 0.96f, 0.98f, 1f);
    private static readonly Vector4 ColorMint = new(0.54f, 0.92f, 0.66f, 1f);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ITextureProvider textureProvider;
    private readonly AlertEngine alertEngine;
    private readonly Configuration config;

    public bool IsPositionUnlocked { get; set; }

    public AlertBannerOverlay(
        IDalamudPluginInterface pluginInterface,
        ITextureProvider textureProvider,
        AlertEngine alertEngine,
        Configuration config)
        : base("ScholarHelperAlertBanner###ScholarHelperAlertBanner", forceMainWindow: true)
    {
        this.pluginInterface = pluginInterface;
        this.textureProvider = textureProvider;
        this.alertEngine = alertEngine;
        this.config = config;

        IsOpen = true;
        RespectCloseHotkey = false;
    }

    public override bool DrawConditions()
        => IsPositionUnlocked || (config.Enabled && alertEngine.ActiveBanners.Count > 0);

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize;

        if (!IsPositionUnlocked) Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;

        var vp = ImGui.GetMainViewport();
        if (config.HasCustomBannerPosition)
        {
            var pos = new Vector2(vp.Pos.X + config.BannerPosX, vp.Pos.Y + config.BannerPosY);
            ImGui.SetNextWindowPos(pos, IsPositionUnlocked ? ImGuiCond.Appearing : ImGuiCond.Always);
        }
        else
        {
            var pos = new Vector2(vp.Pos.X + (vp.Size.X * 0.5f), vp.Pos.Y + (vp.Size.Y * 0.21f));
            ImGui.SetNextWindowPos(pos, IsPositionUnlocked ? ImGuiCond.Appearing : ImGuiCond.Always, new Vector2(0.5f, 0f));
        }

        var accent = IsPositionUnlocked
            ? ColorUnlockBorder
            : (alertEngine.ActiveBanners.Count > 0 ? alertEngine.ActiveBanners[0].AccentColor : ColorUnlockBorder);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, ColorBg);
        ImGui.PushStyleColor(ImGuiCol.Border, accent);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.4f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 8f));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    public override void Draw()
    {
        if (IsPositionUnlocked)
        {
            if (ImGui.IsWindowHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var vp = ImGui.GetMainViewport();
                var winPos = ImGui.GetWindowPos();
                config.HasCustomBannerPosition = true;
                config.BannerPosX = winPos.X - vp.Pos.X;
                config.BannerPosY = winPos.Y - vp.Pos.Y;
                config.Save(pluginInterface);
            }

            ImGui.TextColored(ColorUnlockBorder, "Alert Banner (drag to move)");
            if (alertEngine.ActiveBanners.Count == 0)
            {
                DrawJobIcon(40);
                ImGui.TextColored(ColorWhite, "Co-Healer");
                ImGui.SameLine(0f, 0f);
                ImGui.TextColored(ColorMint, "  ->  Raising  ");
                ImGui.SameLine(0f, 0f);
                DrawJobIcon(22);
                ImGui.TextColored(ColorWhite, "Party Member");
                return;
            }

            ImGui.Separator();
        }

        var banners = alertEngine.ActiveBanners;
        for (var i = 0; i < banners.Count; i++)
        {
            if (i > 0) ImGui.Spacing();

            var line = banners[i];
            for (var s = 0; s < line.Segments.Count; s++)
            {
                if (s > 0) ImGui.SameLine(0f, 0f);
                var seg = line.Segments[s];
                if (seg.IsJobIcon) DrawJobIcon(seg.JobIconClassId);
                else ImGui.TextColored(seg.Color ?? ColorWhite, seg.Text);
            }
        }
    }

    private void DrawJobIcon(uint classJobId)
    {
        var icon = textureProvider.GetFromGameIcon(new GameIconLookup(JobIconBaseId + classJobId)).GetWrapOrDefault();
        if (icon != null)
        {
            ImGui.Image(icon.Handle, new Vector2(19f, 19f));
            ImGui.SameLine(0f, 5f);
        }
    }
}
