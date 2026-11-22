using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using ScholarHelper.Core;
using ScholarHelper.UI;

namespace ScholarHelper.Modules;

public sealed class SlidecastBarModule : IHelperModule
{
    private static readonly uint ColorBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.08f, 0.96f));
    private static readonly uint ColorBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.14f, 0.14f, 0.18f, 1f));
    private static readonly uint ColorUnlockBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.96f, 0.84f, 0.44f, 1f));
    private static readonly uint ColorCastingRed = ImGui.ColorConvertFloat4ToU32(new Vector4(0.90f, 0.20f, 0.20f, 1f));
    private static readonly uint ColorSlideGreen = ImGui.ColorConvertFloat4ToU32(new Vector4(0.14f, 0.86f, 0.34f, 1f));
    private static readonly uint ColorMarker = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f));
    private static readonly uint ColorShadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.90f));
    private static readonly uint ColorText = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IObjectTable objectTable;
    private readonly Configuration config;
    private readonly ExcelSheet<Lumina.Excel.Sheets.Action>? actionSheet;

    public bool IsPositionUnlocked { get; set; }

    public string Id => "slidecast_bar";
    public string DisplayName => "Slidecast Bar";
    public string Tooltip => "High-contrast opaque cast bar that turns from bright red to bright green during the slidecast window.";

    public SlidecastBarModule(
        IDalamudPluginInterface pluginInterface,
        IDataManager dataManager,
        IObjectTable objectTable,
        Configuration config)
    {
        this.pluginInterface = pluginInterface;
        this.objectTable = objectTable;
        this.config = config;
        actionSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
    }

    public void Enable() { }

    public void DrawOverlay()
    {
        if (!config.Enabled || !config.EnableSlidecastBar) return;

        var player = objectTable.LocalPlayer;
        var isLiveCast = player != null && player.IsCasting && player.TotalCastTime > 0.05f;
        if (!IsPositionUnlocked && !isLiveCast) return;

        var width = Math.Clamp(config.SlidecastBarWidth, 160, 440);
        var height = Math.Clamp(config.SlidecastBarHeight, 18, 48);
        var windowSec = Math.Clamp(config.SlidecastWindowMs, 200, 800) / 1000f;

        var vp = ImGui.GetMainViewport();
        if (config.HasCustomCastBarPosition)
        {
            var pos = new Vector2(vp.Pos.X + config.CastBarPosX, vp.Pos.Y + config.CastBarPosY);
            ImGui.SetNextWindowPos(pos, IsPositionUnlocked ? ImGuiCond.Appearing : ImGuiCond.Always);
        }
        else
        {
            var pos = new Vector2(vp.Pos.X + (vp.Size.X * 0.5f), vp.Pos.Y + (vp.Size.Y * 0.72f));
            ImGui.SetNextWindowPos(pos, IsPositionUnlocked ? ImGuiCond.Appearing : ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        }

        ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
                    | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBackground;
        if (!IsPositionUnlocked) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        if (ImGui.Begin("ScholarHelperSlidecastBar###SCHSlidecastBar", flags))
        {
            if (IsPositionUnlocked && ImGui.IsWindowHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var winPos = ImGui.GetWindowPos();
                config.HasCustomCastBarPosition = true;
                config.CastBarPosX = winPos.X - vp.Pos.X;
                config.CastBarPosY = winPos.Y - vp.Pos.Y;
                config.Save(pluginInterface);
            }

            var pMin = ImGui.GetWindowPos();
            var pMax = new Vector2(pMin.X + width, pMin.Y + height);
            var draw = ImGui.GetWindowDrawList();

            float progress;
            float totalSec;
            float remainingSec;
            string spellName;

            if (isLiveCast)
            {
                totalSec = player!.TotalCastTime;
                var current = Math.Clamp(player.CurrentCastTime, 0f, totalSec);
                remainingSec = MathF.Max(0f, totalSec - current);
                progress = Math.Clamp(current / totalSec, 0f, 1f);
                var row = actionSheet?.GetRowOrDefault(player.CastActionId);
                spellName = row.HasValue ? row.Value.Name.ExtractText() : "Casting";
            }
            else
            {
                totalSec = 2.0f;
                var cycle = (float)(ImGui.GetTime() % 2.0);
                progress = cycle / totalSec;
                remainingSec = totalSec - cycle;
                spellName = "Slidecast Bar (drag)";
            }

            var canSlide = remainingSec <= windowSec;
            var fillCol = canSlide ? ColorSlideGreen : ColorCastingRed;

            draw.AddRectFilled(pMin, pMax, ColorBg, 4f);

            var fillX = pMin.X + (width * progress);
            if (fillX > pMin.X + 1f)
            {
                draw.AddRectFilled(new Vector2(pMin.X + 1f, pMin.Y + 1f), new Vector2(MathF.Min(fillX, pMax.X - 1f), pMax.Y - 1f), fillCol, 3f);
            }

            if (totalSec > windowSec)
            {
                var markerT = Math.Clamp(1f - (windowSec / totalSec), 0.05f, 0.98f);
                var markerX = pMin.X + (width * markerT);
                draw.AddLine(new Vector2(markerX, pMin.Y + 2f), new Vector2(markerX, pMax.Y - 2f), ColorMarker, 2f);
            }

            draw.AddRect(pMin, pMax, IsPositionUnlocked ? ColorUnlockBorder : ColorBorder, 4f, ImDrawFlags.None, IsPositionUnlocked ? 2f : 1.5f);

            var timerText = $"{remainingSec:F2}s";
            var textY = pMin.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f);
            var namePos = new Vector2(pMin.X + 8f, textY);
            var timerSize = ImGui.CalcTextSize(timerText);
            var timerPos = new Vector2(pMax.X - timerSize.X - 8f, textY);

            DrawShadowedText(draw, namePos, spellName);
            DrawShadowedText(draw, timerPos, timerText);
        }

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private static void DrawShadowedText(ImDrawListPtr draw, Vector2 pos, string text)
    {
        draw.AddText(new Vector2(pos.X + 1f, pos.Y + 1f), ColorShadow, text);
        draw.AddText(pos, ColorText, text);
    }

    public bool DrawSettings()
    {
        var changed = false;

        var enabled = config.EnableSlidecastBar;
        if (ImGui.Checkbox("Enable Slidecast Bar", ref enabled))
        {
            config.EnableSlidecastBar = enabled;
            changed = true;
        }

        if (!config.EnableSlidecastBar) ImGui.BeginDisabled();
        ImGui.Indent(16f);

        var winMs = config.SlidecastWindowMs;
        if (UiControls.SliderIntOption("##scwin", "Slidecast", ref winMs, 200, 800, $"{winMs} ms", step: 10, labelWidth: 74f, sliderWidth: 130f, valueWidth: 60f))
        {
            config.SlidecastWindowMs = winMs;
            changed = true;
        }

        var w = config.SlidecastBarWidth;
        if (UiControls.SliderIntOption("##scw", "Width", ref w, 160, 440, $"{w} px", step: 10, labelWidth: 74f, sliderWidth: 130f, valueWidth: 60f))
        {
            config.SlidecastBarWidth = w;
            changed = true;
        }

        var h = config.SlidecastBarHeight;
        if (UiControls.SliderIntOption("##sch", "Height", ref h, 18, 48, $"{h} px", step: 2, labelWidth: 74f, sliderWidth: 130f, valueWidth: 60f))
        {
            config.SlidecastBarHeight = h;
            changed = true;
        }

        ImGui.Unindent(16f);
        if (!config.EnableSlidecastBar) ImGui.EndDisabled();

        return changed;
    }

    public void Dispose() { }
}
