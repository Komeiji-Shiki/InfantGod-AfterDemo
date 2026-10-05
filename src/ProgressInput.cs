using System;
using System.Collections.Generic;
using System.Reflection;
using FairyGUI;
using HarmonyLib;
using InfantGod.Core;
using InfantGod.Core.OSSystem;
using InfantGod.Core.SaveSystem;
using InfantGod.Core.SceneManagement;
using InfantGod.UI.WindowSystem.Windows;

namespace Graywill.InfantGodAfterDemo
{
    [HarmonyPatch(typeof(FGUICommandManager), "GetBaseUserData")]
    internal static class ProgressMetadataPatch
    {
        private static bool Prefix(GObject obj, ref string __result)
        {
            // 克隆原生按钮只继承外观，不执行模板附带的快读或设置命令。
            for (GObject current = obj; current != null; current = current.parent)
                if (current.name?.StartsWith(ProgressWindow.Prefix, StringComparison.Ordinal) == true)
                {
                    __result = "";
                    return false;
                }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class ProgressInputPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(OSManager), "UpdateEscapeShortcut");
            yield return AccessTools.Method(typeof(SaveSystem), "HandleQuickSaveInput");
            yield return AccessTools.Method(typeof(LiveStreamSpeakerManager), "UpdateAdvanceHotKey");
            yield return AccessTools.Method(typeof(LiveStreamSpeakerManager), "TryToggleDanmakuInputByHotKey");
            yield return AccessTools.Method(typeof(LiveStreamSpeakerManager), "TrySendDanmakuByEnter");
            yield return AccessTools.Method(typeof(PVShortcutManager), "Update");
        }
        private static bool Prefix() => AfterDemoHost.Instance?.CapturesInput != true;
    }
}
