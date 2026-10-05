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
        private bool open;
        private Vector2 scroll;
        private GUIStyle label, heading, button, panel;
        private Texture2D background;
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
            ModLoader.ModPreferencesChanged += RefreshEnabled;
            ModLoader.ModsReloaded += RefreshEnabled;
            RefreshEnabled();
        }

        private void OnDestroy()
        {
            ModLoader.ModPreferencesChanged -= RefreshEnabled;
            ModLoader.ModsReloaded -= RefreshEnabled;
            foreach (var runner in registered)
                if (runner != null)
                {
                    runner.RemoveCommandHandler("AfterDemoRefresh");
                    runner.RemoveCommandHandler("AfterDemoFinished");
                }
            if (background != null) Destroy(background);
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
            if (!Enabled) open = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10)) open = !open;
            if (open && Input.GetKeyDown(KeyCode.Escape)) open = false;
            if (Time.unscaledTime < nextProbe) return;
            nextProbe = Time.unscaledTime + 1;
            var manager = FindObjectOfType<GameDialogueManager>();
            if (manager != null && manager.DialogueRunner != null) Bind(manager.DialogueRunner);
            if (Active && open) Refresh();
        }

        internal void Bind(DialogueRunner runner)
        {
            Runner = runner;
            if (!registered.Add(runner)) return;
            runner.AddCommandHandler("AfterDemoRefresh", (Action)Refresh);
            runner.AddCommandHandler("AfterDemoFinished", (Action)(() => { Refresh(); open = true; }));
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

        private void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = false };
            label.normal.textColor = new Color(.86f, .88f, .9f);
            heading = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 15, padding = new RectOffset(12, 12, 8, 8) };
            background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(.045f, .055f, .075f, .98f)); background.Apply();
            panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(20, 20, 16, 16) };
            panel.normal.background = background;
        }

        private void OnGUI()
        {
            if (!Enabled && !open) return;
            Styles();
            if (!open)
            {
                if (GUI.Button(new Rect(Screen.width - 220, 72, 204, 34), "第八天以后 · F10", button)) { open = true; Refresh(); }
                return;
            }
            float width = Mathf.Min(780, Screen.width - 32), height = Mathf.Min(690, Screen.height - 32);
            var rect = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            // 占用窗口区域的输入，防止查阅事件时误点背后的直播选项。
            GUI.ModalWindow(GetInstanceID(), rect, _ => DrawPanel(), "", panel);
        }

        private void DrawPanel()
        {
            GUILayout.Label("幼神 · 第八天以后", heading);
            GUILayout.Label("补充直播日：前七天保持原流程，补完当前分支的随机事件后再收束。", label);
            if (!Enabled) GUILayout.Label("请先在 设置 → Mod 管理器 中启用“幼神·第八天以后”，再重新进入存档。", label);
            else if (!Active) GUILayout.Label(RuntimeCatalog.Error == null ? "载入第八天或之后的存档时生效。已有存档的精确事件访问记录会自动识别。" : "角色目录生成失败：" + RuntimeCatalog.Error, label);
            else if (Catalog != null)
            {
                var displayed = Catalog.DisplayEvents.ToList();
                GUILayout.Label($"已触发 {displayed.Count - Catalog.UnseenCount} / {displayed.Count}    当前可触发 {Catalog.ReadyCount}    剧情条件未满足 {displayed.Count(e => !e.Seen && !e.Ready && e.Error == null)}", heading);
                if (Closed) GUILayout.Label("本存档已经完成续玩收束。", label);
                else if (Catalog.ReadyCount == 0 && Catalog.ErrorCount == 0) GUILayout.Label("当前分支已没有可继续的随机事件。关闭此窗口后，可在对话中选择结束。", label);
                if (RuntimeCatalog.UnsupportedEntries.Count > 0)
                    GUILayout.Label("有角色 Mod 使用了特殊入口，尚未自动纳入：" + string.Join("；", RuntimeCatalog.UnsupportedEntries) + "。兼容配置方式见说明文件。", label);
                scroll = GUILayout.BeginScrollView(scroll);
                foreach (var item in displayed)
                {
                    string state = item.Error != null ? "读取异常" : item.Seen ? "已触发" : item.Ready ? "可触发" : "剧情限制";
                    GUILayout.Label("[" + state + "] " + item.Label, label);
                    if (item.Error != null) GUILayout.Label(item.Error, label);
                    else if (!item.Seen && !item.Ready) GUILayout.Label("需要：" + item.requirement, label);
                }
                GUILayout.EndScrollView();
                if (Catalog.ReadyCount > 0 && Catalog.ErrorCount == 0 && !Runner.IsDialogueRunning
                    && GUILayout.Button("继续匹配未见事件", button))
                {
                    open = false;
                    YarnCommands.TriggerNodeFlow("AfterDemo_Resume", "AfterDemo progress window");
                }
            }
            if (!string.IsNullOrEmpty(error)) GUILayout.Label("读取暂停：" + error, label);
            GUILayout.FlexibleSpace();
            GUILayout.Label("F10 / Esc 关闭。事件以实际剧情变体计数，互斥分支不会强制解锁。", label);
            if (GUILayout.Button("关闭进度窗口", button)) open = false;
            GUI.DragWindow(new Rect(0, 0, 760, 40));
        }
    }
}
