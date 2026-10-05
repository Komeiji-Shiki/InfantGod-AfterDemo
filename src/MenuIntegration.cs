using System;
using FairyGUI;
using HarmonyLib;
using InfantGod.Core.OSSystem;
using UnityEngine;

namespace Graywill.InfantGodAfterDemo
{
    internal sealed class MenuIntegration : IDisposable
    {
        internal const string EntryName = "InfantGodAfterDemo_ProgressMenu";
        private readonly AfterDemoHost owner;
        private GButton entry;
        internal MenuIntegration(AfterDemoHost owner) { this.owner = owner; }

        internal void Tick()
        {
            if (!owner.Enabled) { RemoveEntry(); return; }
            var os = UnityEngine.Object.FindObjectOfType<OSManager>();
            // GetWorkspaceRoot 直接返回已创建的 _workspace，不触发 UIPanel.ui 的懒加载。
            var workspace = os?.GetWorkspaceRoot();
            if (!NativeUiAccess.Alive(workspace)) return;
            var taskbar = workspace.GetChild("TaskBar")?.asCom;
            var menu = taskbar?.GetChild("TaskBar_Menu")?.asCom;
            var list = menu?.GetChild("Menu_List")?.asList;
            if (NativeUiAccess.Alive(list)) Bind(os, list);
        }

        internal void Bind(OSManager os, GList list)
        {
            if (!owner.Enabled || !NativeUiAccess.Alive(list)) return;
            var existing = list.GetChild(EntryName)?.asButton;
            if (NativeUiAccess.Alive(existing)) { entry = existing; return; }
            var template = AccessTools.Field(typeof(OSManager), "_workspaceQuickLoadButton").GetValue(os) as GButton;
            if (!NativeUiAccess.Alive(template) || string.IsNullOrEmpty(template.resourceURL)) return;
            var package = UIPackage.GetItemByURL(template.resourceURL)?.owner;
            if (package != null) NativeUiAccess.LoadPackage(package.name);
            var button = UIPackage.CreateObjectFromURL(template.resourceURL)?.asButton;
            if (button == null) return;
            button.name = EntryName;
            button.title = button.selectedTitle = "续玩进度";
            button.icon = template.icon;
            var caption = button.GetChild("Start");
            if (caption != null) caption.text = "续玩进度";
            button.SetSize(template.width, template.height);
            button.onClick.Clear();
            button.onClick.Add(context =>
            {
                context.StopPropagation();
                if (!NativeUiAccess.Alive(list)) return;
                for (GComponent current = list.parent; current != null; current = current.parent)
                {
                    var expanded = current.GetController("ExpandMenu");
                    if (expanded != null) { expanded.selectedIndex = 0; break; }
                }
                owner.OpenProgress();
            });
            // 保留原生快存/快读索引。普通保存先装则排在它后面，后装则由它插入时自然后移。
            int insertAt = Math.Min(4, list.numChildren);
            var normalSave = list.GetChild("InfantGodSaveManager_NormalSave");
            if (normalSave != null) insertAt = Math.Max(insertAt, list.GetChildIndex(normalSave) + 1);
            ResizeForChange(list, () => list.AddChildAt(button, insertAt));
            entry = button;
        }

        private static void ResizeForChange(GList list, Action change)
        {
            var menu = list.parent;
            if (!NativeUiAccess.Alive(menu)) return;
            float height = list.height, bottom = menu.y + menu.height;
            change();
            list.ResizeToFit(list.numChildren);
            // 只在实际增删时应用当前高度差，两个独立 Mod 不会在轮询中反复挤动菜单。
            menu.height += list.height - height;
            menu.y = bottom - menu.height;
        }

        private void RemoveEntry()
        {
            if (NativeUiAccess.Alive(entry) && entry.parent is GList list && NativeUiAccess.Alive(list))
                ResizeForChange(list, () => list.RemoveChild(entry, true));
            entry = null;
        }

        public void Dispose() { RemoveEntry(); }
    }

    [HarmonyPatch(typeof(OSManager), "BindWorkspaceQuickSaveButtons")]
    internal static class ProgressMenuPatch
    {
        private static void Postfix(OSManager __instance, GList menuList) => AfterDemoHost.Instance?.Menu?.Bind(__instance, menuList);
    }
}
