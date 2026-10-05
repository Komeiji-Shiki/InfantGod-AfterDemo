using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Graywill.InfantGodAfterDemo
{
    internal static class ScheduleConditions
    {
        private sealed class Part
        {
            internal string Text;
            internal bool ScheduleOnly;
            internal Part(string text, bool schedule = false) { Text = text; ScheduleOnly = schedule; }
        }

        internal static string KeepNarrative(IEnumerable<string> conditions, string target)
        {
            var retained = new List<string>();
            foreach (string raw in conditions)
            {
                string value = Regex.Replace(raw, @"""(?:\\.|[^""\\])*""|//[^\r\n]*", m => m.Value.StartsWith("//", StringComparison.Ordinal) ? "" : m.Value).Trim();
                if (value == "once" || value == "always") continue;
                if (value.StartsWith("once if ", StringComparison.Ordinal)) value = value.Substring(8);
                var part = Rewrite(value, target);
                if (!part.ScheduleOnly && part.Text != "true") retained.Add("(" + part.Text + ")");
            }
            return retained.Count == 0 ? "true" : string.Join(" and ", retained);
        }

        private static Part Rewrite(string text, string target)
        {
            text = text.Trim();
            while (HasOuterParentheses(text)) text = text.Substring(1, text.Length - 2).Trim();
            var tokens = Regex.Matches(text, @"""(?:\\.|[^""\\])*""|\$[\w]+|[A-Za-z_][\w]*|&&|\|\||[^\s]").Cast<Match>().ToArray();
            foreach (string operation in new[] { "or", "and" })
            {
                int depth = 0;
                for (int index = tokens.Length - 1; index >= 0; index--)
                {
                    string token = tokens[index].Value;
                    if (token == ")") depth++;
                    else if (token == "(") depth--;
                    else if (depth == 0 && (token == operation || token == (operation == "and" ? "&&" : "||")))
                    {
                        var left = Rewrite(text.Substring(0, tokens[index].Index), target);
                        var right = Rewrite(text.Substring(tokens[index].Index + tokens[index].Length), target);
                        if (left.ScheduleOnly && right.ScheduleOnly) return new Part("true", true);
                        if (operation == "and")
                        {
                            if (left.ScheduleOnly) return right;
                            if (right.ScheduleOnly) return left;
                        }
                        else if (left.ScheduleOnly || right.ScheduleOnly) return new Part("true", true);
                        return new Part("(" + left.Text + ") " + operation + " (" + right.Text + ")");
                    }
                }
            }
            if (Regex.IsMatch(text, @"^not\b") || text.StartsWith("!", StringComparison.Ordinal))
            {
                var inside = Rewrite(text.Substring(text[0] == '!' ? 1 : 3), target);
                return inside.ScheduleOnly ? inside : new Part("not (" + inside.Text + ")");
            }
            // 只调整明示的排期变量比较，不猜测自定义函数内部的业务含义。
            bool schedulingVariable = tokens.Any(t => t.Value == "$Today" || t.Value == "$CurrentHour"
                || t.Value == "$CurrentMinute" || t.Value == "$LineCount" || t.Value == "$Minor_Token");
            bool function = tokens.Select((t, i) => new { t, i }).Any(p => p.i + 1 < tokens.Length && tokens[p.i + 1].Value == "("
                && Regex.IsMatch(p.t.Value, @"^[A-Za-z_]\w*$"));
            if (schedulingVariable && !function) return new Part("true", true);
            if (Regex.IsMatch(text, "^CountAvailable\\(\\s*\"" + Regex.Escape(target.Split('.')[0]) + "\"\\s*\\)\\s*(?:>=\\s*1|>\\s*0|!=\\s*0)$")) return new Part("true", true);
            return new Part(text);
        }

        private static bool HasOuterParentheses(string text)
        {
            if (text.Length < 2 || text[0] != '(' || text[text.Length - 1] != ')') return false;
            int depth = 0;
            bool quoted = false, escaped = false;
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (value == '\\') escaped = true;
                    else if (value == '"') quoted = false;
                    continue;
                }
                if (value == '"') quoted = true;
                else if (value == '(') depth++;
                else if (value == ')' && --depth == 0 && index < text.Length - 1) return false;
            }
            return depth == 0;
        }
    }
}
