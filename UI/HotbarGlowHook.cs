using System;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using ScholarHelper.Core;

namespace ScholarHelper.UI;

/// <summary>
/// Thin native hotbar proc border hook. Performs an O(1) lookup against AlertEngine's
/// per-frame cached highlight set (purely visual UI; sends zero actions or packets).
/// </summary>
public sealed unsafe class HotbarGlowHook : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate byte IsActionHighlightedDelegate(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId);

    private readonly IGameInteropProvider gameInteropProvider;
    private readonly AlertEngine alertEngine;
    private readonly IPluginLog log;

    private Hook<IsActionHighlightedDelegate>? hook;

    public HotbarGlowHook(
        IGameInteropProvider gameInteropProvider,
        AlertEngine alertEngine,
        IPluginLog log)
    {
        this.gameInteropProvider = gameInteropProvider;
        this.alertEngine = alertEngine;
        this.log = log;
    }

    public void Enable()
    {
        try
        {
            hook ??= gameInteropProvider.HookFromAddress<IsActionHighlightedDelegate>(
                (nint)ActionManager.MemberFunctionPointers.IsActionHighlighted,
                Detour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to initialize Scholar Helper hotbar glow hook.");
        }
    }

    private byte Detour(ActionManager* actionManager, ActionType actionType, uint actionId)
    {
        var original = hook!.Original(actionManager, actionType, actionId);
        if (original != 0)
        {
            return original;
        }

        return alertEngine.IsHighlighted(actionType, actionId) ? (byte)1 : (byte)0;
    }

    public void Dispose()
    {
        hook?.Dispose();
        hook = null;
    }
}
