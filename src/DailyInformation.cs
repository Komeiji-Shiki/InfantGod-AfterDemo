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
        internal const string SidebarDayKey = "$AfterDemo_SidebarPublishedDay";
        private static readonly FieldInfo InfoKeys = AccessTools.Field(typeof(SituationManager), "_playerInfoKeys");
        private static readonly MethodInfo RefreshNews = AccessTools.Method(typeof(NewspaperUIManager), "UpdateNewspaperUI");

        internal static bool SupplementalDay => AfterDemoHost.Instance?.Enabled == true
            && !AfterDemoHost.Instance.Closed && GameTimeManager.Instance != null && GameTimeManager.Instance.GetCurrentDay() >= 8;

        internal static void Prepare()
        {
            if (!SupplementalDay) return;
            var situation = SituationManager.Instance;
            if (situation != null) situation.AddPlayerInfo(BriefKey);
            PrepareSidebar(NewspaperManager.Instance);
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

        private static readonly FieldInfo MainSlots = AccessTools.Field(typeof(NewspaperManager), "mainNewsSlots");
        private static readonly FieldInfo MainQueue = AccessTools.Field(typeof(NewspaperManager), "mainNewsQueue");
        private static readonly FieldInfo MinorQueue = AccessTools.Field(typeof(NewspaperManager), "minorNewsQueue");
        private static readonly FieldInfo MinorPublished = AccessTools.Field(typeof(NewspaperManager), "publishedMinorNews");
        private static readonly FieldInfo QueueDay = AccessTools.Field(typeof(NewspaperManager), "queueStateDay");
        private static readonly MethodInfo EnsureQueueDay = AccessTools.Method(typeof(NewspaperManager), "EnsureQueueStateForCurrentDay");
        private static readonly MethodInfo FillSlots = AccessTools.Method(typeof(NewspaperManager), "FillEmptySlots");
        private static readonly MethodInfo RegisterUsed = AccessTools.Method(typeof(NewspaperManager), "RegisterPublishedRandomNews");
        private static readonly FieldInfo PublishedEvent = AccessTools.Field(typeof(NewspaperManager), "OnNewspaperPublished");

        internal static void PrepareSidebar(NewspaperManager manager)
        {
            var storage = AfterDemoHost.Instance?.Runner?.VariableStorage;
            if (!SupplementalDay || manager == null || storage == null) return;
            int day = GameTimeManager.Instance.GetCurrentDay();
            float preparedDay;
            if (storage.TryGetValue(SidebarDayKey, out preparedDay) && preparedDay == day) return;
            // 原版或旧存档已经保存了当天侧栏时直接采用，不因重复进入欢迎页而重抽。
            if ((int)QueueDay.GetValue(manager) == day && manager.GetPublishedMinorNews().Count > 0)
            {
                storage.SetValue(SidebarDayKey, (float)day);
                return;
            }
            EnsureQueueDay.Invoke(manager, null);
            var mainQueue = (List<News>)MainQueue.GetValue(manager);
            int mainSlots = (int)MainSlots.GetValue(manager);
            try
            {
                // 只让原生补空位函数处理侧栏；它负责日期、priority² 权重、队列排重和未读随机池。
                // 主新闻的内容与待发布队列均保留，不让补充日提前生成主线新闻。
                MainSlots.SetValue(manager, mainQueue.Count);
                FillSlots.Invoke(manager, new object[] { day });
            }
            finally { MainSlots.SetValue(manager, mainSlots); }
            var queue = (List<News>)MinorQueue.GetValue(manager);
            var published = (List<News>)MinorPublished.GetValue(manager);
            published.Clear(); published.AddRange(queue); queue.Clear();
            RegisterUsed.Invoke(manager, null);
            manager.CachePublishedNewsIcons();
            storage.SetValue(SidebarDayKey, (float)day);
            (PublishedEvent.GetValue(manager) as Action)?.Invoke();
            Plugin.Log.LogInfo("补充日侧栏按原生随机池发布：Day " + day + "，" + published.Count + " 条。");
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
        private sealed class Edition
        {
            internal List<News> Main;
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
                Main = __instance.GetPublishedMainNews()
            } : null;
        }
        private static void Postfix(NewspaperManager __instance, Edition __state)
        {
            if (__state == null) return;
            // 只保留实际已发布的主新闻；侧栏仍按原生规则清空并在补充日重新抽取。
            var main = (List<News>)Main.GetValue(__instance);
            if (main.Count == 0) main.AddRange(__state.Main);
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
            {
                host.Runner.VariableStorage.SetValue(DailyInformation.SidebarDayKey, (float)GameTimeManager.Instance.GetCurrentDay());
            }
        }
    }

    [HarmonyPatch(typeof(NewspaperManager), nameof(NewspaperManager.LoadSaveData))]
    internal static class NewspaperRestoreEvidencePatch
    {
        private static void Prefix(NewspaperSaveData saveData)
        {
            if (!DailyInformation.SupplementalDay) return;
            Plugin.Log.LogInfo("续玩读档新闻原始记录：主新闻=" + (saveData?.publishedMainNews?.Count ?? 0)
                + "，次新闻=" + (saveData?.publishedMinorNews?.Count ?? 0) + "；空侧栏将按当日原生随机池补充，主线历史不补造。");
        }
        private static void Postfix(NewspaperManager __instance) { DailyInformation.PrepareSidebar(__instance); }
    }

    [HarmonyPatch(typeof(NewspaperUIManager), "UpdateNewspaperUI")]
    internal static class SupplementalNewspaperPatch
    {
        private static readonly FieldInfo Labels = AccessTools.Field(typeof(NewspaperUIManager), "_labels");
        private static void Postfix(NewspaperUIManager __instance)
        {
            if (!DailyInformation.SupplementalDay) return;
            var manager = NewspaperManager.Instance;
            if (manager == null) return;
            bool hasMain = manager.GetPublishedMainNews().Count > 0;
            bool hasSidebar = manager.GetPublishedMinorNews().Count > 0;
            if (hasMain) return;
            var labels = Labels.GetValue(__instance) as IEnumerable<GComponent>;
            if (labels == null) return;
            foreach (var label in labels)
            {
                if (!NativeUiAccess.Alive(label)) continue;
                // 使用原生新闻面板显示状态，不创建或发布一条虚构的世界新闻。
                var panel = label.GetChild("Panel_NewsPaper_Main_1")?.asCom;
                if (!NativeUiAccess.Alive(panel)) continue;
                panel.visible = true;
                if (panel is GLabel nativeLabel) nativeLabel.title = "补充直播日：本日暂无主线新闻";
                else
                {
                    var title = panel.GetChild("title");
                    if (NativeUiAccess.Alive(title)) title.text = "补充直播日：本日暂无主线新闻";
                }
                var description = panel.GetChild("desc");
                if (NativeUiAccess.Alive(description)) description.text =
                    "今天继续安排尚未触发的直播事件。新的主线简报将在补充直播收束后发布。\n\n"
                    + (hasSidebar ? "今日其他报道详见右侧新闻栏。" : "本日暂无其他新闻。");
                var image = panel.GetController("HasImg");
                if (image != null) image.selectedPage = "no";
            }
        }
    }
}
