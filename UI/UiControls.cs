using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace ScholarHelper.UI;

public static class UiControls
{
    private const float OptionLabelWidth = 66f;
    private const float OptionSliderWidth = 104f;
    private const float OptionValueWidth = 48f;

    private static readonly Vector4 ColorLabel = new(0.68f, 0.71f, 0.76f, 1f);
    private static readonly Vector4 ColorValue = new(0.95f, 0.96f, 0.98f, 1f);
    private static readonly Vector4 ColorTrack = new(0.15f, 0.16f, 0.19f, 1f);
    private static readonly Vector4 ColorTrackHover = new(0.19f, 0.21f, 0.25f, 1f);
    private static readonly Vector4 ColorTrackActive = new(0.22f, 0.24f, 0.29f, 1f);
    private static readonly Vector4 ColorGrab = new(0.86f, 0.74f, 0.40f, 0.96f);
    private static readonly Vector4 ColorGrabActive = new(0.99f, 0.87f, 0.50f, 1f);

    private static readonly uint TrackFillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.86f, 0.74f, 0.40f, 0.36f));

    public static bool SliderIntOption(
        string id,
        string label,
        ref int value,
        int min,
        int max,
        string valueText,
        int step = 1,
        float labelWidth = OptionLabelWidth,
        float sliderWidth = OptionSliderWidth,
        float valueWidth = OptionValueWidth)
    {
        var startX = ImGui.GetCursorPosX();
        var rowY = ImGui.GetCursorPosY();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorLabel, label);

        ImGui.SameLine(0f, 0f);
        ImGui.SetCursorPosX(startX + labelWidth);

        var changed = DrawSleekSliderInt(id, ref value, min, max, sliderWidth, rowY);
        if (changed && step > 1)
        {
            value = Math.Clamp(((value + (step / 2)) / step) * step, min, max);
        }

        ImGui.SameLine(0f, 8f);
        ImGui.SetCursorPosY(rowY);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorValue, valueText);

        if (valueWidth > 0f)
        {
            var endX = startX + labelWidth + sliderWidth + 8f + valueWidth;
            ImGui.SameLine(0f, 0f);
            ImGui.SetCursorPosX(endX);
            ImGui.SetCursorPosY(rowY);
            ImGui.Dummy(new Vector2(0f, ImGui.GetFrameHeight()));
        }

        return changed;
    }

    public static bool SliderFloatOption(
        string id,
        string label,
        ref float value,
        float min,
        float max,
        string valueText,
        float step = 0.5f,
        float labelWidth = OptionLabelWidth,
        float sliderWidth = OptionSliderWidth)
    {
        var startX = ImGui.GetCursorPosX();
        var rowY = ImGui.GetCursorPosY();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorLabel, label);

        ImGui.SameLine(0f, 0f);
        ImGui.SetCursorPosX(startX + labelWidth);

        var changed = DrawSleekSliderFloat(id, ref value, min, max, sliderWidth, rowY);
        if (changed && step > 0f)
        {
            value = Math.Clamp(MathF.Round(value / step) * step, min, max);
        }

        ImGui.SameLine(0f, 8f);
        ImGui.SetCursorPosY(rowY);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorValue, valueText);
        return changed;
    }

    public static bool DrawSleekSliderInt(string id, ref int value, int min, int max, float width, float? rowTopY = null)
    {
        var baseY = rowTopY ?? ImGui.GetCursorPosY();
        var frameH = ImGui.GetFrameHeight();
        var sliderH = ImGui.GetTextLineHeight();
        var offsetY = MathF.Max(0f, (frameH - sliderH) * 0.5f);

        ImGui.SetCursorPosY(baseY + offsetY);
        ImGui.SetNextItemWidth(width);

        PushSliderStyle();
        var changed = ImGui.SliderInt(id, ref value, min, max, string.Empty);
        DrawTrackFill(max > min ? Math.Clamp((value - min) / (float)(max - min), 0f, 1f) : 0f);
        PopSliderStyle();

        return changed;
    }

    public static bool DrawSleekSliderFloat(string id, ref float value, float min, float max, float width, float? rowTopY = null)
    {
        var baseY = rowTopY ?? ImGui.GetCursorPosY();
        var frameH = ImGui.GetFrameHeight();
        var sliderH = ImGui.GetTextLineHeight();
        var offsetY = MathF.Max(0f, (frameH - sliderH) * 0.5f);

        ImGui.SetCursorPosY(baseY + offsetY);
        ImGui.SetNextItemWidth(width);

        PushSliderStyle();
        var changed = ImGui.SliderFloat(id, ref value, min, max, string.Empty);
        DrawTrackFill(max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f);
        PopSliderStyle();

        return changed;
    }

    public static bool CompactStepperInt(string id, string label, ref int value, int min, int max, string suffix)
    {
        var changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorLabel, label);
        ImGui.SameLine(0f, 6f);

        if (ImGui.SmallButton($"-##{id}_dec") && value > min)
        {
            value--;
            changed = true;
        }

        ImGui.SameLine(0f, 6f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ColorValue, $"{value}{suffix}");

        ImGui.SameLine(0f, 6f);
        if (ImGui.SmallButton($"+##{id}_inc") && value < max)
        {
            value++;
            changed = true;
        }

        return changed;
    }

    private static void PushSliderStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 7f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 7f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 12f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ColorTrack);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ColorTrackHover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ColorTrackActive);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, ColorGrab);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ColorGrabActive);
    }

    private static void PopSliderStyle()
    {
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(4);
    }

    private static void DrawTrackFill(float t)
    {
        var rMin = ImGui.GetItemRectMin();
        var rMax = ImGui.GetItemRectMax();
        var innerW = MathF.Max(0f, (rMax.X - rMin.X) - 16f);
        var grabLeftX = rMin.X + 2f + (innerW * t);
        if (grabLeftX > rMin.X + 3f)
        {
            ImGui.GetWindowDrawList().AddRectFilled(
                new Vector2(rMin.X + 1f, rMin.Y + 1f),
                new Vector2(grabLeftX + 2f, rMax.Y - 1f),
                TrackFillColor,
                6f);
        }
    }
}
