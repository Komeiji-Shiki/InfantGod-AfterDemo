using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using InfantGod.Core;
using InfantGod.Core.ModSystem;
using InfantGod.Dialogue;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yarn.Unity;

namespace Graywill.InfantGodAfterDemo
{
    public sealed class AfterDemoHost : MonoBehaviour
    {
        internal static AfterDemoHost Instance;
        internal EventCatalog Catalog;
        internal DialogueRunner Runner;
        internal bool Enabled { get; private set; }
        private string error;
        private LiveEvent selectedEvent;
        private float nextProbe;
        internal MenuIntegration Menu;
        private ProgressWindow view;
        internal string ProgressError => error;
        internal bool CapturesInput => view?.IsOpen == true;
        private readonly HashSet<DialogueRunner> registered = new HashSet<DialogueRunner>();
        private static readonly MethodInfo InstalledMods = typeof(ModLoader).GetMethod("GetInstalledMods", BindingFlags.Public | BindingFlags.Static);

        internal bool Active
        {
            get
            {
                if (!Enabled || Runner == null || Runner.YarnProject == null || RuntimeCatalog.Current == null) return false;
                Catalog = RuntimeCatalog.Current;
                float day;
                return Runner.VariableStorage != null && Runner.VariableStorage.TryGetValue("$Today", out day)
                    && day >= 8 && Runner.Dialogue.NodeExists("AfterDemo_Hub");
            }
        }

        internal bool Closed
        {
            get { bool closed; return Runner != null && Runner.VariableStorage.TryGetValue("$AfterDemo_Closed", out closed) && closed; }
        }

        private void Awake()
        {
            Instance = this;
            try { Catalog = new EventCatalog(Path.Combine(Plugin.DataPath, "events.json")); }
            catch (Exception ex) { Fail(ex); }
            Menu = new MenuIntegration(this);
            view = new ProgressWindow(this);
            SceneManager.sceneLoaded += SceneLoaded;
            ModLoader.ModPreferencesChanged += RefreshEnabled;
            ModLoader.ModsReloaded += RefreshEnabled;
            RefreshEnabled();
        }

        private void OnDestroy()
        {
            ModLoader.ModPreferencesChanged -= RefreshEnabled;
            ModLoader.ModsReloaded -= RefreshEnabled;
            SceneManager.sceneLoaded -= SceneLoaded;
            foreach (var runner in registered)
                if (runner != null)
                {
                    runner.RemoveCommandHandler("AfterDemoRefresh");
                    runner.RemoveCommandHandler("AfterDemoFinished");
                }
            view?.Dispose();
            Menu?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void RefreshEnabled()
        {
            Enabled = false;
            foreach (object mod in (IEnumerable)InstalledMods.Invoke(null, null))
            {
                var type = mod.GetType();
                if ((string)type.GetField("FolderName").GetValue(mod) != "InfantGodAfterDemo") continue;
                Enabled = (bool)type.GetField("Enabled").GetValue(mod);
                break;
            }
            nextProbe = 0;
            if (!Enabled) { view?.Close(); Menu?.Dispose(); }
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            view?.Dispose(); Menu?.Dispose(); nextProbe = 0;
        }

        private void Update()
        {
            view?.Tick();
            if (CapturesInput && Input.GetKeyDown(KeyCode.Escape)) view.Close();
            if (Time.unscaledTime < nextProbe) return;
            nextProbe = Time.unscaledTime + 1;
            var manager = FindObjectOfType<GameDialogueManager>();
            if (manager != null && manager.DialogueRunner != null) Bind(manager.DialogueRunner);
            Menu?.Tick();
            if (CapturesInput) { Refresh(); view.Refresh(); }
        }

        internal void Bind(DialogueRunner runner)
        {
            Runner = runner;
            if (!registered.Add(runner)) return;
            runner.AddCommandHandler("AfterDemoRefresh", (Action)Refresh);
            runner.AddCommandHandler("AfterDemoFinished", (Action)OpenProgress);
        }

        internal void OpenProgress()
        {
            if (!Enabled) return;
            try { Refresh(); view.Open(); }
            catch (Exception ex) { Fail(ex); }
        }

        internal void ResetSelection() { selectedEvent = null; }

        internal void Refresh()
        {
            if (!Active) return;
            try
            {
                Catalog = RuntimeCatalog.Current;
                var manager = FindObjectOfType<GameDialogueManager>();
                Catalog.Refresh(Runner, manager?.DefaultYarnProject?.Program);
                // 收束后安装的新角色仍可继续；只有出现新的可达事件才重新开放当前存档。
                if (Catalog.ReadyCount > 0 && Catalog.ErrorCount == 0 && Closed)
                    Runner.VariableStorage.SetValue("$AfterDemo_Closed", false);
                Runner.VariableStorage.SetValue("$AfterDemo_ReadyCount", (float)Catalog.ReadyCount);
                Runner.VariableStorage.SetValue("$AfterDemo_UnseenCount", (float)Catalog.UnseenCount);
                Runner.VariableStorage.SetValue("$AfterDemo_ErrorCount", (float)Catalog.ErrorCount);
                error = null;
            }
            catch (Exception ex)
            {
                foreach (var item in Catalog.Events) { item.Ready = false; item.Error = ex.Message; }
                Runner.VariableStorage.SetValue("$AfterDemo_ErrorCount", 1f);
                Runner.VariableStorage.SetValue("$AfterDemo_ReadyCount", 0f);
                Fail(ex);
            }
        }

        private void Fail(Exception ex)
        {
            if (error != ex.Message) Plugin.Log.LogError(ex);
            error = ex.Message;
        }

        internal string Redirect(string node)
        {
            if (!Active) return node;
            if (node == "AfterDemo_Closed")
            {
                Refresh();
                return Closed ? node : "AfterDemo_Welcome";
            }
            if (node.StartsWith("AfterDemo_", StringComparison.Ordinal)) return node;
            string root = node.Split('.')[0];
            if (root == "Initial" || root == "Initial_Node" || root == "Welcome" || root == "Welcome_Node") Refresh();
            if (root == "Live_Hub" || root == "Initial" || root == "Welcome") selectedEvent = null;
            // 日期照常增长；在原生跳转边界接管日程，不执行未完成的 Day9+ 剧情。
            switch (root)
            {
                case "Initial": case "Initial_Node": return Closed ? "AfterDemo_Closed" : "AfterDemo_Initial";
                case "Welcome": case "Welcome_Node": case "Aistalt_Day": return Closed ? "AfterDemo_Closed" : "AfterDemo_Welcome";
                case "Live": case "Live_Node": return Closed ? "AfterDemo_Closed" : "AfterDemo_Live";
                case "Live_Hub": return Closed ? "AfterDemo_Closed" : "AfterDemo_Hub";
                case "Resume_Live_Hub": return Closed ? "AfterDemo_Closed" : "AfterDemo_Resume";
                case "Chat": case "Chat_Node": case "Aistalt_Night": case "ClockOut": case "Dream": case "Dream_Node":
                    return Closed ? "AfterDemo_Closed" : "AfterDemo_DayEnd";
            }
            if (Catalog.HasEntry(root))
            {
                Refresh();
                var available = Catalog.ReadyForEntry(root);
                selectedEvent = available.Count > 0 ? available[0] : null;
                return selectedEvent != null ? selectedEvent.EntryNode : "AfterDemo_Hub";
            }
            // 包装节点仍负责角色出场与原变量写入，只将事件组路由到当前可用的精确变体。
            if (node == root && Catalog.Events.Any(e => e.group == root))
            {
                // 一次性入口会在出场时被原存档标为已见；保留本次已选事件直到实际正文开始。
                if (selectedEvent != null && selectedEvent.group == root)
                {
                    string target = selectedEvent.Node;
                    selectedEvent = null;
                    return target;
                }
                Refresh();
                var available = Catalog.Events.Where(e => e.group == root && e.Ready).ToList();
                return available.Count > 0 ? available[UnityEngine.Random.Range(0, available.Count)].Node : "AfterDemo_Hub";
            }
            return node;
        }

    }
}
