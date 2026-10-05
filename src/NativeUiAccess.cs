using System;
using System.Collections.Generic;
using FairyGUI;
using HarmonyLib;
using UnityEngine;

namespace Graywill.InfantGodAfterDemo
{
    internal static class NativeUiAccess
    {
        internal static bool Alive(GObject value) => value != null && !value.isDisposed && value.displayObject != null
            && !value.displayObject.isDisposed && value.displayObject.cachedTransform != null;

        internal static void LoadPackage(string name)
        {
            if (name == "LiveStreamWindow" && UIPackage.GetByName("SongPlayer") == null) LoadPackage("SongPlayer");
            var package = UIPackage.GetByName(name) ?? UIPackage.AddPackage("FairyGUI/UI/" + name);
            // 提前载入包必须同时注册生成类型，避免后续原生窗口拿到普通 GComponent。
            var binder = AccessTools.TypeByName(name + "." + name + "Binder");
            if (binder != null) AccessTools.Method(binder, "BindAll")?.Invoke(null, null);
            foreach (var dependency in package.dependencies)
                if (dependency.TryGetValue("name", out string dependencyName) && UIPackage.GetByName(dependencyName) == null)
                    LoadPackage(dependencyName);
        }

        internal static GButton Button(string title, Action clicked)
        {
            LoadPackage("MainMenu");
            var button = UIPackage.CreateObject("MainMenu", "Btn_Settings").asButton;
            button.name = ProgressWindow.Prefix + "Button";
            button.title = button.selectedTitle = title;
            button.mode = ButtonMode.Common;
            var parts = new ButtonParts { Width = button.width, Height = button.height };
            foreach (var child in button.GetChildren())
                parts.Items.Add(Tuple.Create(child, new Rect(child.x, child.y, child.width, child.height)));
            button.data = parts;
            button.onClick.Add(context => { context.StopPropagation(); clicked(); });
            return button;
        }

        private sealed class ButtonParts
        {
            internal float Width, Height;
            internal readonly List<Tuple<GObject, Rect>> Items = new List<Tuple<GObject, Rect>>();
        }

        internal static void PlaceButton(GButton button, float x, float y, float width, float height, int fontSize)
        {
            button.SetXY(x, y); button.SetSize(width, height);
            if (button.data is ButtonParts parts)
                foreach (var part in parts.Items)
                {
                    var rect = part.Item2;
                    part.Item1.SetXY(rect.x * width / parts.Width, rect.y * height / parts.Height);
                    part.Item1.SetSize(rect.width * width / parts.Width, rect.height * height / parts.Height);
                }
            var label = button.GetTextField();
            label.autoSize = AutoSizeType.None; label.align = AlignType.Center; label.verticalAlign = VertAlignType.Middle;
            var format = label.textFormat; format.size = fontSize; label.textFormat = format;
        }
    }
}
