using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FairyGUI;
using HarmonyLib;
using InfantGod.Core;
using InfantGod.Core.NewspaperSystem;
using InfantGod.Core.SaveSystem;
using InfantGod.Core.SituationSystem;
using UnityEngine;

namespace Graywill.InfantGodAfterDemo
{
    internal static class DailyInformation
    {
        internal const string BriefKey = "Info_AfterDemo_SupplementalDay";
        internal const string PublishedDayKey = "$AfterDemo_NewspaperPublishedDay";
        private static readonly FieldInfo InfoKeys = AccessTools.Field(typeof(SituationManager), "_playerInfoKeys");
        private static readonly MethodInfo RefreshNews = AccessTools.Method(typeof(NewspaperUIManager), "UpdateNewspaperUI");

        internal static bool SupplementalDay => AfterDemoHost.Instance?.Enabled == true
            && !AfterDemoHost.Instance.Closed && GameTimeManager.Instance != null && GameTimeManager.Instance.GetCurrentDay() >= 8;

        internal static void Prepare()
        {
            if (!SupplementalDay) return;
            var situation = SituationManager.Instance;
            if (situation != null) situation.AddPlayerInfo(BriefKey);
            foreach (var ui in UnityEngine.Object.FindObjectsOfType<NewspaperUIManager>()) RefreshNews.Invoke(ui, null);
        }

        internal static void EnsureBrief(SituationManager manager)
        {
            var keys = (List<string>)InfoKeys.GetValue(manager);
            // 读到补充日前已保存的空简报时，也补齐本 Mod 的日程说明，不推定历史剧情进度。
            if (SupplementalDay)
            {
                if (!keys.Contains(BriefKey)) keys.Add(BriefKey);
            }
            else keys.Remove(BriefKey);
        }

        internal static bool PublishedToday()
        {
            var runner = AfterDemoHost.Instance?.Runner;
            float day;
            return runner?.VariableStorage != null && runner.VariableStorage.TryGetValue(PublishedDayKey, out day)
                && GameTimeManager.Instance != null && day == GameTimeManager.Instance.GetCurrentDay();
        }
    }

    [HarmonyPatch(typeof(SituationManager), "RenderPlayerInfo")]
    internal static class SupplementalBriefPatch
    {
        private static void Prefix(SituationManager __instance) { DailyInformation.EnsureBrief(__instance); }
    }

    [HarmonyPatch]
    internal static class PreservePublishedEditionPatch
    {
        private static readonly FieldInfo Main = AccessTools.Field(typeof(NewspaperManager), "publishedMainNews");
        private static readonly FieldInfo Minor = AccessTools.Field(typeof(NewspaperManager), "publishedMinorNews");
        private sealed class Edition
        {
            internal List<News> Main, Minor;
        }
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(NewspaperManager), "OnDayChanged");
            yield return AccessTools.Method(typeof(NewspaperManager), "EnsureQueueStateForCurrentDay");
        }
        private static void Prefix(NewspaperManager __instance, out Edition __state)
        {
            __state = DailyInformation.SupplementalDay ? new Edition
            {
                Main = __instance.GetPublishedMainNews(), Minor = __instance.GetPublishedMinorNews()
            } : null;
        }
        private static void Postfix(NewspaperManager __instance, Edition __state)
        {
            if (__state == null) return;
            // 只保护原生换日清理之前真实存在的版面；读档和重置仍沿原生规则替换全部新闻状态。
            var main = (List<News>)Main.GetValue(__instance);
            var minor = (List<News>)Minor.GetValue(__instance);
            if (main.Count == 0 && minor.Count == 0)
            {
                main.AddRange(__state.Main); minor.AddRange(__state.Minor);
            }
        }
    }

    [HarmonyPatch(typeof(NewspaperManager), nameof(NewspaperManager.SendNewspaper))]
    internal static class RecordSupplementalPublicationPatch
    {
        private static void Prefix()
        {
            // 在原生发布回调刷新页面之前记录。若版面为空，UI 仍会使用明确的无记录说明。
            var host = AfterDemoHost.Instance;
            if (DailyInformation.SupplementalDay && host?.Runner?.VariableStorage != null)
                host.Runner.VariableStorage.SetValue(DailyInformation.PublishedDayKey, (float)GameTimeManager.Instance.GetCurrentDay());
        }
    }

    [HarmonyPatch(typeof(NewspaperManager), nameof(NewspaperManager.LoadSaveData))]
    internal static class NewspaperRestoreEvidencePatch
    {
        private static void Prefix(NewspaperSaveData saveData)
        {
            if (!DailyInformation.SupplementalDay) return;
            Plugin.Log.LogInfo("续玩读档新闻原始记录：主新闻=" + (saveData?.publishedMainNews?.Count ?? 0)
                + "，次新闻=" + (saveData?.publishedMinorNews?.Count ?? 0) + "；缺失的历史版面不会补造。");
        }
    }

    [HarmonyPatch(typeof(NewspaperUIManager), "UpdateNewspaperUI")]
    internal static class SupplementalNewspaperPatch
    {
        private static readonly FieldInfo Labels = AccessTools.Field(typeof(NewspaperUIManager), "_labels");
        private static readonly Dictionary<GTextField, string> OriginalIssueText = new Dictionary<GTextField, string>();

        private static void Prefix()
        {
            // 先恢复原生期数模板，让原生 SetVar/FlushVars 正常运行；停用后也能立即恢复原版报头。
            foreach (var pair in OriginalIssueText.ToArray())
            {
                if (NativeUiAccess.Alive(pair.Key)) pair.Key.text = pair.Value;
                else OriginalIssueText.Remove(pair.Key);
            }
        }

        private static void Postfix(NewspaperUIManager __instance)
        {
            if (!DailyInformation.SupplementalDay) return;
            var manager = NewspaperManager.Instance;
            if (manager == null) return;
            bool hasEdition = manager.GetPublishedMainNews().Count > 0 || manager.GetPublishedMinorNews().Count > 0;
            if (hasEdition && DailyInformation.PublishedToday()) return;
            var labels = Labels.GetValue(__instance) as IEnumerable<GComponent>;
            if (labels == null) return;
            foreach (var label in labels)
            {
                if (!NativeUiAccess.Alive(label)) continue;
                var issue = label.GetChild("Times")?.asTextField;
                if (NativeUiAccess.Alive(issue))
                {
                    if (!OriginalIssueText.ContainsKey(issue)) OriginalIssueText.Add(issue, issue.text);
                    issue.text = hasEdition ? "本日无新增 · 重阅" : "本日无新增";
                }
                if (hasEdition) continue;
                // 使用原生新闻面板显示状态，不创建或发布一条虚构的世界新闻。
                var panel = label.GetChild("Panel_NewsPaper_Main_1")?.asCom;
                if (!NativeUiAccess.Alive(panel)) continue;
                panel.visible = true;
                if (panel is GLabel nativeLabel) nativeLabel.title = "补充直播日：本日无新增新闻";
                else
                {
                    var title = panel.GetChild("title");
                    if (NativeUiAccess.Alive(title)) title.text = "补充直播日：本日无新增新闻";
                }
                var description = panel.GetChild("desc");
                if (NativeUiAccess.Alive(description)) description.text =
                    "今天继续安排尚未触发的直播事件。新的主线简报将在补充直播收束后发布。\n\n"
                    + "当前存档没有保留可回看的已发布版面，因此这里只显示本日状态，不补写未发生或未记录的新闻。";
                var image = panel.GetController("HasImg");
                if (image != null) image.selectedPage = "no";
            }
        }
    }
}
