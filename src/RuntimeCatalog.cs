using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using InfantGod.Core.ModSystem;
using Newtonsoft.Json;
using UnityEngine;

namespace Graywill.InfantGodAfterDemo
{
    // 使用游戏已经完成排序与覆盖合并的节点，角色 Mod 的源码和载入顺序由原加载器负责。
    internal static class RuntimeCatalog
    {
        private const string GeneratedTitle = "AfterDemo_RuntimeConditions";
        internal static EventCatalog Current;
        internal static string Error;
        internal static readonly List<string> UnsupportedEntries = new List<string>();
        private static readonly FieldInfo NodeSources = AccessTools.Field(typeof(ModLoader), "yarnNodesByName");
        private static readonly MethodInfo Crc = AccessTools.Method(typeof(Yarn.Dialogue).Assembly.GetType("Yarn.Utility.CRC32"), "GetChecksumString", new[] { typeof(string) });

        private sealed class SourceNode
        {
            internal string Key, Title, Subtitle, Character, Content, Path, Body, Compiled;
            internal string[] When;
            internal int Line;
        }

        internal static string Checksum(string value) => (string)Crc.Invoke(null, new object[] { value });

        internal static void Prepare(ref string[] yarnFiles)
        {
            Current = null;
            Error = null;
            UnsupportedEntries.Clear();
            var sources = NodeSources.GetValue(null) as IDictionary;
            if (sources == null) return;
            sources.Remove(GeneratedTitle);
            // 停用时不注入条件或接管游戏；启用状态由当前实际合并结果决定。
            if (!sources.Contains("AfterDemo_Hub")) return;
            try
            {
                var parsed = new List<SourceNode>();
                foreach (DictionaryEntry pair in sources)
                {
                    var type = pair.Value.GetType();
                    string content = (string)type.GetField("Content").GetValue(pair.Value);
                    string path = (string)type.GetField("SourcePath").GetValue(pair.Value);
                    var node = Parse((string)pair.Key, content, path);
                    if (node != null) parsed.Add(node);
                }
                var prefixes = new[] { "Live_Entry_" }.Concat((Plugin.ExtraPrefixes.Value ?? "").Split(','))
                    .Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToArray();
                var mappings = JsonConvert.DeserializeObject<Dictionary<string, string>>(Plugin.EntryTargets.Value)
                    ?? new Dictionary<string, string>();
                var seeds = JsonConvert.DeserializeObject<List<LiveEvent>>(File.ReadAllText(Path.Combine(Plugin.DataPath, "events.json")));
                var events = new List<LiveEvent>();
                var bindings = new List<Tuple<LiveEvent, SourceNode, SourceNode>>();
                foreach (var entry in parsed.Where(n => prefixes.Any(p => n.Title.StartsWith(p, StringComparison.Ordinal)) || mappings.ContainsKey(n.Title)))
                {
                    if (IsExcludedBaseSource(entry.Path)) continue;
                    if (string.IsNullOrWhiteSpace(entry.Character))
                    {
                        UnsupportedEntries.Add(entry.Title + "：缺少 Character 头");
                        Plugin.Log.LogWarning("续玩入口缺少 Character 头，未纳入：" + entry.Title);
                        continue;
                    }
                    string target;
                    if (!mappings.TryGetValue(entry.Title, out target))
                    {
                        var jumps = Regex.Matches(entry.Body, @"(?m)^[ \t]*<<jump\s+([A-Za-z_][\w.]*)\s*>>").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToArray();
                        if (jumps.Length == 1) target = jumps[0];
                        else if (jumps.Length == 0) target = entry.Key; // 正文直接写在连线入口中的角色 Mod。
                        else
                        {
                            UnsupportedEntries.Add(entry.Title + "：存在多个跳转目标，请配置 EntryTargets");
                            Plugin.Log.LogWarning("续玩入口含多个目标，请配置 EntryTargets：" + entry.Title);
                            continue;
                        }
                    }
                    var targets = parsed.Where(n => n.Key == target || n.Title == target).Where(n => !IsExcludedBaseSource(n.Path)).ToArray();
                    if (targets.Length == 0)
                    {
                        UnsupportedEntries.Add(entry.Title + "：未找到事件目标 " + target);
                        Plugin.Log.LogWarning("续玩入口目标未找到：" + entry.Title + " → " + target);
                        continue;
                    }
                    foreach (var node in targets)
                    {
                        string identity = entry.Key + "\n" + node.Key;
                        var original = entry == node ? entry.When : entry.When.Concat(node.When);
                        string condition = ScheduleConditions.KeepNarrative(original, target);
                        var seed = seeds?.FirstOrDefault(e => e.entry == entry.Title && e.group == node.Title && e.subtitle == node.Subtitle);
                        var item = new LiveEvent
                        {
                            id = "r" + Checksum(identity), entry = entry.Title, group = node.Title, subtitle = node.Subtitle,
                            character = entry.Character, condition = condition,
                            displayName = seed?.displayName ?? (entry.Character + " · " + (string.IsNullOrEmpty(node.Subtitle) ? "随机连线" : node.Subtitle)),
                            requirement = seed != null && IsBaseSource(entry.Path) && IsBaseSource(node.Path) ? seed.requirement : condition,
                            oneShotEntry = entry.When.Any(w => w == "once" || w.StartsWith("once if ", StringComparison.Ordinal)),
                            UniqueGroup = parsed.Count(n => n.Title == node.Title) == 1,
                            UniqueEntry = parsed.Count(n => n.Title == entry.Title) == 1,
                            LineIds = Regex.Matches(node.Body, @"#line:([^\s]+)").Cast<Match>().Select(m => m.Groups[1].Value).ToArray()
                        };
                        events.Add(item);
                        bindings.Add(Tuple.Create(item, entry, node));
                    }
                }
                var text = new StringBuilder("title: " + GeneratedTitle + "\n---\n");
                foreach (var item in events) text.Append("<<declare $AfterDemo_Can_").Append(item.id).Append(" = ").Append(item.condition).Append(">>\n");
                text.Append("===\n");
                string generatedPath = Path.Combine(Application.temporaryCachePath, "InfantGodAfterDemo", "RuntimeConditions.yarn");
                Directory.CreateDirectory(Path.GetDirectoryName(generatedPath));
                File.WriteAllText(generatedPath, text.ToString(), new UTF8Encoding(false));
                Type sourceType = sources.GetType().GetGenericArguments()[1];
                sources[GeneratedTitle] = Activator.CreateInstance(sourceType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new object[] { GeneratedTitle, text.ToString(), generatedPath }, null);
                // 加入原加载器的缓存输入；角色内容或兼容配置变化都会使编译缓存失效。
                yarnFiles = yarnFiles.Concat(new[] { generatedPath }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var keys = sources.Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal).ToArray();
                foreach (var node in parsed)
                {
                    string compilePath = Path.Combine(Application.temporaryCachePath, "ModYarnCompile", "node_" + Array.IndexOf(keys, node.Key).ToString("00000") + ".yarn");
                    node.Compiled = node.When.Length == 0 ? node.Title : node.Title + "." +
                        (!string.IsNullOrEmpty(node.Subtitle) ? node.Subtitle : Checksum(compilePath + node.Title + node.Line));
                }
                foreach (var binding in bindings)
                {
                    binding.Item1.ExpectedEntry = binding.Item2.Compiled;
                    binding.Item1.ExpectedNode = binding.Item3.Compiled;
                }
                Current = new EventCatalog(events);
                Plugin.Log.LogInfo("续玩目录已随启用的角色 Mod 更新：" + events.Select(e => e.entry).Distinct().Count() + " 个入口，" + Current.DisplayEvents.Count() + " 个事件。");
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Plugin.Log.LogError("动态续玩目录生成失败：" + ex);
                throw; // 不把一个未生成完整的目录冒充“所有事件已经完成”。
            }
        }

        private static SourceNode Parse(string key, string content, string path)
        {
            int divider = content.IndexOf("---", StringComparison.Ordinal);
            if (divider < 0) return null;
            string header = content.Substring(0, divider);
            var fields = Regex.Matches(header, @"(?m)^[ \t]*(\w+)[ \t]*:[ \t]*([^\r\n]*)").Cast<Match>().ToArray();
            Func<string, string> value = name => fields.FirstOrDefault(m => string.Equals(m.Groups[1].Value, name, StringComparison.OrdinalIgnoreCase))?.Groups[2].Value.Trim() ?? "";
            var title = fields.FirstOrDefault(m => string.Equals(m.Groups[1].Value, "title", StringComparison.OrdinalIgnoreCase));
            if (title == null) return null;
            return new SourceNode { Key = key, Title = value("title"), Subtitle = value("subtitle"), Character = value("Character"),
                Content = content, Path = path, Body = content.Substring(divider + 3), Line = content.Substring(0, title.Index).Count(c => c == '\n') + 1,
                When = fields.Where(m => string.Equals(m.Groups[1].Value, "when", StringComparison.OrdinalIgnoreCase)).Select(m => m.Groups[2].Value.Trim()).ToArray() };
        }

        private static bool IsExcludedBaseSource(string path)
        {
            string normalized = path.Replace('\\', '/');
            return IsBaseSource(path)
                && normalized.Split('/').Any(p => p.Equals("Storage", StringComparison.OrdinalIgnoreCase)
                    || p.Equals("Bin", StringComparison.OrdinalIgnoreCase) || p.Equals("ForShot", StringComparison.OrdinalIgnoreCase) || p.Equals("Ends", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsBaseSource(string path) => path.Replace('\\', '/').StartsWith(
            (Application.streamingAssetsPath + "/Mods/MyMod/Yarn/").Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    [HarmonyPatch(typeof(ModLoader), "CompileAndReplaceYarn")]
    internal static class MergeCatalogPatch
    {
        private static void Prefix(ref string[] yarnFiles) { RuntimeCatalog.Prepare(ref yarnFiles); }
    }
}
