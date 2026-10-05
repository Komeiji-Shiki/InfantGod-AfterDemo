using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Graywill.InfantGodAfterDemo
{
    [BepInPlugin("graywill.infantgod.afterdemo", "幼神·第八天以后", "1.0.0")]
    [BepInProcess("Aistalt.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string DataPath;
        internal static ConfigEntry<string> ExtraPrefixes;
        internal static ConfigEntry<string> EntryTargets;
        private static bool quitting;

        private void Awake()
        {
            Log = Logger;
            DataPath = Path.GetDirectoryName(Info.Location);
            ExtraPrefixes = Config.Bind("Compatibility", "AdditionalEntryPrefixes", "", "额外随机入口前缀，逗号分隔；修改后重新载入 Mod。Live_Entry_ 始终自动识别。");
            EntryTargets = Config.Bind("Compatibility", "EntryTargets", "{}", "特殊入口与实际事件目标的 JSON 对照，例如 {\"CustomLive_MyCharacter\":\"CustomStory\"}；入口需要 Character 头。");
            new Harmony("graywill.infantgod.afterdemo").PatchAll(typeof(Plugin).Assembly);
            SceneManager.sceneLoaded += SceneLoaded;
            Application.quitting += Quit;
            if (Time.frameCount > 0 && SceneManager.GetActiveScene().isLoaded) EnsureHost();
            Log.LogInfo("续玩补丁已注册；请在游戏 Mod 管理器中启用“幼神·第八天以后”。");
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode) { EnsureHost(); }
        private static void EnsureHost()
        {
            if (quitting || AfterDemoHost.Instance != null) return;
            // 游戏会清理第零帧的加载器宿主，实际窗口在场景载入后独立创建。
            var root = new GameObject("BepInEx_InfantGodAfterDemo");
            root.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(root);
            root.AddComponent<AfterDemoHost>();
        }
        private static void Quit()
        {
            quitting = true;
            SceneManager.sceneLoaded -= SceneLoaded;
            Application.quitting -= Quit;
        }
    }
}
