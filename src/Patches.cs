using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using InfantGod.UI.WindowSystem;
using Yarn.Unity;

namespace Graywill.InfantGodAfterDemo
{
    [HarmonyPatch(typeof(DialogueRunner), nameof(DialogueRunner.StartDialogue))]
    internal static class BindRunnerPatch
    {
        private static void Prefix(DialogueRunner __instance)
        {
            AfterDemoHost.Instance?.Bind(__instance);
            AfterDemoHost.Instance?.ResetSelection();
        }
    }

    [HarmonyPatch]
    internal static class NodeRoutePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Yarn.VirtualMachine"), "SetNode", new[] { typeof(string), typeof(bool) });
        private static void Prefix(ref string nodeName)
        {
            if (AfterDemoHost.Instance != null) nodeName = AfterDemoHost.Instance.Redirect(nodeName);
        }
    }

    [HarmonyPatch(typeof(NPCLiveOptionManager), "EvaluateWhenConditions")]
    internal static class CandidateConditionPatch
    {
        private static bool Prefix(string nodeName, ref bool __result)
        {
            var host = AfterDemoHost.Instance;
            if (host == null || !host.Active || !nodeName.StartsWith("Live_Entry_", StringComparison.Ordinal)) return true;
            host.Refresh();
            __result = !host.Closed && host.Catalog.ErrorCount == 0 && host.Catalog.ReadyForEntry(nodeName).Count > 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(NPCLiveOptionManager), "GetActualNodeFromGroup")]
    internal static class CandidateCharacterPatch
    {
        private static bool Prefix(string nodeGroupOrNodeName, ref string __result)
        {
            var host = AfterDemoHost.Instance;
            if (host == null || !host.Active || !host.Catalog.HasEntry(nodeGroupOrNodeName)) return true;
            var candidates = host.Catalog.ReadyForEntry(nodeGroupOrNodeName);
            if (candidates.Count == 0) return true;
            __result = candidates[0].EntryNode;
            return false;
        }
    }
}
