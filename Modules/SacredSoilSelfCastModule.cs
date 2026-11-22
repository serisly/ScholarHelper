using System;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using ScholarHelper.Core;

namespace ScholarHelper.Modules;

public sealed unsafe class SacredSoilSelfCastModule : IHelperModule
{
    private const uint SacredSoilActionId = 188;

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate byte UseActionDelegate(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode mode,
        uint comboRouteId,
        byte* outOptAreaTargeted);

    private readonly IGameInteropProvider gameInteropProvider;
    private readonly IObjectTable objectTable;
    private readonly Configuration config;
    private readonly IPluginLog log;

    private Hook<UseActionDelegate>? hook;

    public string Id => "sacred_soil_self_cast";
    public string DisplayName => "Actions";
    public string Tooltip => "Optional hotbar casting tweaks.";

    public SacredSoilSelfCastModule(
        IGameInteropProvider gameInteropProvider,
        IObjectTable objectTable,
        Configuration config,
        IPluginLog log)
    {
        this.gameInteropProvider = gameInteropProvider;
        this.objectTable = objectTable;
        this.config = config;
        this.log = log;
    }

    public void Enable()
    {
        try
        {
            hook ??= gameInteropProvider.HookFromAddress<UseActionDelegate>(
                (nint)ActionManager.MemberFunctionPointers.UseAction,
                Detour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to initialize Sacred Soil self-cast hook.");
        }
    }

    public void DrawOverlay() { }

    public bool DrawSettings()
    {
        var selfCast = config.SacredSoilAtSelf;
        if (ImGui.Checkbox("Cast Sacred Soil at self", ref selfCast))
        {
            config.SacredSoilAtSelf = selfCast;
            return true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Drops Sacred Soil on yourself when cast from your hotbar instead of opening ground targeting.");
        }

        return false;
    }

    private byte Detour(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode mode,
        uint comboRouteId,
        byte* outOptAreaTargeted)
    {
        if (config.Enabled
            && config.SacredSoilAtSelf
            && actionType == ActionType.Action
            && actionId == SacredSoilActionId
            && mode == ActionManager.UseActionMode.None)
        {
            var player = objectTable.LocalPlayer;
            if (player != null)
            {
                return hook!.Original(
                    actionManager,
                    actionType,
                    actionId,
                    player.GameObjectId,
                    extraParam,
                    ActionManager.UseActionMode.Macro,
                    comboRouteId,
                    outOptAreaTargeted);
            }
        }

        return hook!.Original(
            actionManager,
            actionType,
            actionId,
            targetId,
            extraParam,
            mode,
            comboRouteId,
            outOptAreaTargeted);
    }

    public void Dispose()
    {
        hook?.Dispose();
        hook = null;
    }
}
