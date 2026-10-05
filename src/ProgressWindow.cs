using System;
using System.Collections.Generic;
using System.Linq;
using FairyGUI;
using InfantGod.Core;
using UnityEngine;

namespace Graywill.InfantGodAfterDemo
{
    internal sealed class ProgressWindow : IDisposable
    {
        internal const string Prefix = "InfantGodAfterDemo_";
        private readonly AfterDemoHost owner;
        private Window window;
        private GComponent pane;
        private GGraph paper, header;
        private GTextField title, summary, message;
        private GButton close, resume;
        private GList list;
        private List<LiveEvent> events = new List<LiveEvent>();
        private float lastWidth = -1, lastHeight = -1;
        private static readonly Color Paper = new Color32(229, 225, 181, 255);
        private static readonly Color Ink = new Color32(42, 48, 46, 255);
        private static readonly Color Muted = new Color32(98, 108, 91, 255);
        private static float Unit => Mathf.Clamp(Mathf.Min(GRoot.inst.width / 2560f, GRoot.inst.height / 1440f), .65f, 1.6f);

        internal ProgressWindow(AfterDemoHost owner) { this.owner = owner; }
        internal bool IsOpen => NativeUiAccess.Alive(window) && window.isShowing;

        internal void Open()
        {
            if (!NativeUiAccess.Alive(window)) Create();
            window.Show(); Layout(); Refresh();
        }

        internal void Close() { if (NativeUiAccess.Alive(window)) window.Hide(); }

        private void Create()
        {
            window = new Window { name = Prefix + "ProgressWindow", modal = true };
            pane = new GComponent { name = Prefix + "ProgressPane", opaque = true };
            window.contentPane = pane;
            paper = new GGraph { touchable = false }; pane.AddChild(paper);
            header = new GGraph { touchable = false }; pane.AddChild(header);
            title = Text("续玩进度", Paper); pane.AddChild(title);
            summary = Text("", Ink); pane.AddChild(summary);
            message = Text("", Muted); pane.AddChild(message);
            close = NativeUiAccess.Button("关闭", Close); pane.AddChild(close);
            resume = NativeUiAccess.Button("继续匹配未见事件", () =>
            {
                Close();
                YarnCommands.TriggerNodeFlow("AfterDemo_Resume", "AfterDemo progress window");
            });
            pane.AddChild(resume);
            // 沿用原生对话记录列表的滚动、裁切、项目池及像素滚动条。
            NativeUiAccess.LoadPackage("LiveStreamWindow");
            var template = UIPackage.CreateObject("LiveStreamWindow", "DialogueLog").asCom;
            list = template.GetChild("List_LogItem").asList;
            template.RemoveChild(list); template.Dispose();
            list.name = Prefix + "ProgressEvents";
            list.RemoveChildrenToPool(); list.selectionMode = ListSelectionMode.None;
            list.itemRenderer = RenderEvent;
            pane.AddChild(list); list.SetVirtual();
            lastWidth = lastHeight = -1;
        }

        internal void Tick()
        {
            if (!IsOpen) return;
            Layout();
            resume.enabled = owner.Active && owner.Catalog != null && owner.Catalog.ReadyCount > 0
                && owner.Catalog.ErrorCount == 0 && !owner.Runner.IsDialogueRunning;
        }

        internal void Refresh()
        {
            if (!NativeUiAccess.Alive(pane)) return;
            var messages = new List<string>();
            events = new List<LiveEvent>();
            if (!owner.Enabled) summary.text = "请在设置中的 Mod 管理器启用“幼神·第八天以后”，再重新载入存档。";
            else if (!owner.Active) summary.text = RuntimeCatalog.Error == null
                ? "从第八天开始显示当前存档的随机事件进度。" : "角色目录生成失败：" + RuntimeCatalog.Error;
            else
            {
                events = owner.Catalog.DisplayEvents.ToList();
                int blocked = events.Count(e => !e.Seen && !e.Ready && e.Error == null);
                summary.text = $"已触发 {events.Count - owner.Catalog.UnseenCount} / {events.Count}    可触发 {owner.Catalog.ReadyCount}    剧情条件未满足 {blocked}";
                if (owner.Closed) messages.Add("本存档已经完成续玩收束。");
                else if (owner.Catalog.ReadyCount == 0 && owner.Catalog.ErrorCount == 0)
                    messages.Add("当前分支已没有可继续的随机事件。关闭窗口后，可在对话中选择结束。");
                if (RuntimeCatalog.UnsupportedEntries.Count > 0)
                    messages.Add("以下角色 Mod 的特殊入口尚未纳入：" + string.Join("；", RuntimeCatalog.UnsupportedEntries) + "。配置方法见说明文件。");
            }
            if (!string.IsNullOrEmpty(owner.ProgressError)) messages.Add("读取暂停：" + owner.ProgressError);
            if (messages.Count == 0) messages.Add("事件按实际剧情变体记录。尚未满足的剧情前置和互斥选择会继续保留。");
            message.text = string.Join("\n", messages);
            list.numItems = events.Count; list.RefreshVirtualList();
            Tick();
        }

        private void Layout()
        {
            float u = Unit;
            float width = Mathf.Min(1680 * u, GRoot.inst.width - 64 * u);
            float height = Mathf.Min(1080 * u, GRoot.inst.height - 80 * u);
            if (width == lastWidth && height == lastHeight) return;
            lastWidth = width; lastHeight = height;
            window.SetSize(width, height); pane.SetSize(width, height); window.Center();
            paper.DrawRect(width, height, Mathf.Max(1, Mathf.RoundToInt(2 * u)), Ink, Paper);
            header.DrawRect(width, 108 * u, 0, Ink, Ink);
            Put(title, 36 * u, 26 * u, width - 300 * u, 62 * u, 36 * u);
            Put(summary, 36 * u, 140 * u, width - 72 * u, 70 * u, 29 * u);
            Put(message, 36 * u, 220 * u, width - 72 * u, 114 * u, 25 * u);
            list.SetXY(28 * u, 350 * u); list.SetSize(width - 56 * u, height - 468 * u);
            list.lineGap = Mathf.RoundToInt(14 * u);
            NativeUiAccess.PlaceButton(close, width - 224 * u, 25 * u, 188 * u, 60 * u, Mathf.RoundToInt(28 * u));
            NativeUiAccess.PlaceButton(resume, width - 416 * u, height - 92 * u, 380 * u, 58 * u, Mathf.RoundToInt(27 * u));
            list.RefreshVirtualList();
        }

        private void RenderEvent(int index, GObject value)
        {
            if (index < 0 || index >= events.Count) return;
            var item = events[index]; var row = value.asCom;
            if (!NativeUiAccess.Alive(row)) return;
            foreach (var child in row.GetChildren()) child.visible = false;
            var text = row.GetChild(Prefix + "EventText") as GTextField;
            if (!NativeUiAccess.Alive(text)) { text = Text("", Ink); text.name = Prefix + "EventText"; row.AddChild(text); }
            string state = item.Error != null ? "读取异常" : item.Seen ? "已触发" : item.Ready ? "可触发" : "剧情限制";
            text.text = "[" + state + "] " + item.Label + (item.Error != null ? "\n" + item.Error : !item.Seen && !item.Ready ? "\n需要：" + item.requirement : "");
            text.visible = true; text.autoSize = AutoSizeType.Height;
            float u = Unit, width = Mathf.Max(100, list.width - 36 * u);
            Put(text, 12 * u, 10 * u, width - 24 * u, 64 * u, 28 * u);
            row.SetSize(width, Mathf.Max(68 * u, text.textHeight + 24 * u));
        }

        private static GTextField Text(string text, Color color)
        {
            var label = new GTextField { text = text, touchable = false, singleLine = false };
            var format = label.textFormat; format.font = UIConfig.defaultFont; format.color = color; label.textFormat = format;
            return label;
        }
        private static void Put(GTextField value, float x, float y, float width, float height, float size)
        {
            value.SetXY(x, y); value.SetSize(width, height);
            var format = value.textFormat; format.size = Mathf.RoundToInt(size); value.textFormat = format;
        }

        public void Dispose()
        {
            if (NativeUiAccess.Alive(window)) window.Dispose();
            window = null; pane = null; list = null; events.Clear();
        }
    }
}
