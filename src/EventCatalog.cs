using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using InfantGod.Dialogue;
using Newtonsoft.Json;
using Yarn;
using Yarn.Unity;

namespace Graywill.InfantGodAfterDemo
{
    internal sealed class LiveEvent
    {
        public string id { get; set; }
        public string entry { get; set; }
        public string group { get; set; }
        public string subtitle { get; set; }
        public string character { get; set; }
        public string displayName { get; set; }
        public string requirement { get; set; }
        public string condition { get; set; }
        public bool oneShotEntry { get; set; }
        [JsonIgnore] internal string Node, EntryNode, Error;
        [JsonIgnore] internal string ExpectedNode, ExpectedEntry;
        [JsonIgnore] internal string[] LineIds;
        [JsonIgnore] internal bool UniqueGroup, UniqueEntry;
        [JsonIgnore] internal bool Seen, Ready;
        [JsonIgnore] internal string Label => displayName;
    }

    internal sealed class EventCatalog
    {
        internal readonly List<LiveEvent> Events;
        internal EventCatalog(string path)
        {
            Events = JsonConvert.DeserializeObject<List<LiveEvent>>(File.ReadAllText(path));
            if (Events == null || Events.Count == 0) throw new InvalidDataException("随机事件目录为空。");
        }
        internal EventCatalog(List<LiveEvent> events) { Events = events; }

        // 读取实际编译节点名，避免猜测编译器因源文件位置而产生的 CRC32 后缀。
        private static string Resolve(Program program, string group, string subtitle, string expected = null, string[] lineIds = null)
        {
            if (expected != null && program.Nodes.ContainsKey(expected)) return expected;
            if (!string.IsNullOrEmpty(subtitle))
            {
                string exact = group + "." + subtitle;
                return program.Nodes.ContainsKey(exact) ? exact : null;
            }
            var variants = program.Nodes.Values.Where(n => n.NodeGroup == group).ToList();
            if (variants.Count == 1) return variants[0].Name;
            if (lineIds != null && lineIds.Length > 0)
            {
                var matching = variants.Where(n => program.LineIDsForNode(n.Name).Any(lineIds.Contains)).ToList();
                if (matching.Count == 1) return matching[0].Name;
            }
            if (variants.Count == 0 && program.Nodes.ContainsKey(group)) return group;
            return null;
        }

        private static bool Seen(AistaltVariableStorage storage, HashSet<string> visited,
            Program original, string group, string subtitle, string current, bool unique, string[] lineIds = null)
        {
            if (visited.Contains(current) || storage.IsNodeVisited(current)) return true;
            if (original != null)
            {
                string old = Resolve(original, group, subtitle, lineIds: lineIds);
                if (old != null && visited.Contains(old)) return true;
            }
            // 无 subtitle 且仅有一个变体的组，旧 Mod 编译文件编号变化只会改变 CRC。
            // 只接受真实哈希变体名，不把聚合的基础 title 当作已见证据。
            if (unique && string.IsNullOrEmpty(subtitle) && current != group)
            {
                var pattern = new Regex("^" + Regex.Escape(group) + @"\.[0-9a-fA-F]{8}$");
                return visited.Any(pattern.IsMatch);
            }
            return false;
        }

        internal void Refresh(DialogueRunner runner, Program original)
        {
            var storage = runner.VariableStorage as AistaltVariableStorage;
            if (storage == null) throw new InvalidOperationException("当前存档尚未接入游戏变量存储。");
            var program = runner.YarnProject.Program;
            var visited = new HashSet<string>(storage.GetVisitedNodes(), StringComparer.Ordinal);
            foreach (var item in Events)
            {
                item.Error = null;
                item.Ready = item.Seen = false;
                item.Node = Resolve(program, item.group, item.subtitle, item.ExpectedNode, item.LineIds);
                item.EntryNode = Resolve(program, item.entry, "", item.ExpectedEntry);
                if (item.Node == null || item.EntryNode == null)
                {
                    item.Error = "原事件节点缺失或出现多个无法区分的变体";
                    continue;
                }
                item.Seen = Seen(storage, visited, original, item.group, item.subtitle, item.Node, item.UniqueGroup, item.LineIds)
                    || (item.oneShotEntry && Seen(storage, visited, original, item.entry, "", item.EntryNode, item.UniqueEntry));
                if (item.Seen) continue;
                bool allowed;
                if (!storage.TryGetValue("$AfterDemo_Can_" + item.id, out allowed))
                {
                    item.Error = "续玩条件未编译，请启用 Mod 后重新载入存档";
                    continue;
                }
                item.Ready = allowed;
            }
            // 一个角色 Mod 可以提供多个连线入口通往同一段正文；这仍然是一个事件。
            foreach (var group in Events.Where(e => e.Node != null).GroupBy(e => e.Node))
                if (group.Any(e => e.Seen))
                    foreach (var item in group) { item.Seen = true; item.Ready = false; }
        }

        internal IEnumerable<LiveEvent> DisplayEvents => Events.GroupBy(e => e.ExpectedNode ?? e.group + "." + e.subtitle)
            .Select(g => g.FirstOrDefault(e => e.Ready) ?? g.FirstOrDefault(e => e.Error == null) ?? g.First());
        internal int ReadyCount => DisplayEvents.Count(e => e.Ready);
        internal int UnseenCount => DisplayEvents.Count(e => !e.Seen);
        internal int ErrorCount => Events.Count(e => e.Error != null);
        internal bool HasEntry(string entry) => Events.Any(e => e.entry == entry);
        internal List<LiveEvent> ReadyForEntry(string entry) => Events.Where(e => e.entry == entry && e.Ready).ToList();
    }
}
