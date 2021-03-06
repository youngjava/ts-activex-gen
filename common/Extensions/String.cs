﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using static System.StringComparison;
using static System.Linq.Enumerable;

namespace TsActivexGen {
    public static class StringExtensions {
        public static string RegexReplace(this string s, Regex re, string replacement) => re.Replace(s, replacement);
        public static string RegexReplace(this string s, string pattern, string replacement) => Regex.Replace(s, pattern, replacement);
        public static bool IsNullOrEmpty(this string s) => string.IsNullOrEmpty(s);
        public static string IfNullOrEmpty(this string s, string replacement) => s.IsNullOrEmpty() ? replacement : s;

        public static void AppendLineTo(this string s, StringBuilder sb, int indentationLevel = 0) {
            s = s.TrimEnd();
            if (s.IsNullOrEmpty()) { return; }
            var toAppend = new String(' ', indentationLevel * 4) + s.TrimEnd();
            sb.AppendLine(toAppend);
        }

        /// <summary>Appends the passed-in string as a line, followed by another empty line</summary>
        public static void AppendWithNewSection(this string s, StringBuilder sb, int indentationLevel = 0) {
            s.AppendLineTo(sb, indentationLevel);
            sb.AppendLine();
        }
        public static void AppendLinesTo(this IEnumerable<string> lines, StringBuilder sb, int indentationLevel = 0, string endOfLine = null) {
            var indentation = new string(' ', indentationLevel * 4);
            lines.Select((x, index, atEnd) => {
                var actualEndOfLine = atEnd ? "" : endOfLine;
                return $"{x}{actualEndOfLine}";
            }).ForEach(line => line.AppendLineTo(sb, indentationLevel));
        }
        public static void AppendLinesTo<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> lines, StringBuilder sb, Func<TKey, TValue, string> selector, int indentationLevel = 0, string endOfLine = null) {
            lines.SelectKVP(selector).AppendLinesTo(sb, indentationLevel, endOfLine);
        }

        public static void AppendTo(this string s, StringBuilder sb) => sb.Append(s);

        public static bool Contains(this string source, string toCheck, StringComparison comp) => source.IndexOf(toCheck, comp) >= 0;
        public static bool ContainsAny(this string source, params char[] toCheck) => toCheck.Any(x => source.IndexOf(x) != -1);
        public static bool ContainsOnly(this string source, params char[] toCheck) => source.All(x => toCheck.Contains(x));

        public static string ForceEndsWith(this string s, string end, StringComparison comparisonType = OrdinalIgnoreCase) {
            var ret = s;
            if (!s.EndsWith(end, comparisonType)) { ret += end; }
            return ret;
        }

        public static bool StartsWithAny(this string s, IEnumerable<string> tests, StringComparison comparisonType = OrdinalIgnoreCase) => tests.Any(x => s.StartsWith(x, comparisonType));
        public static bool EndsWithAny(this string s, IEnumerable<string> tests, StringComparison comparisonType = OrdinalIgnoreCase) => tests.Any(x => s.EndsWith(x, comparisonType));
        public static bool In(this string s, IEnumerable<string> vals, StringComparison comparisonType = OrdinalIgnoreCase) => vals.Any(x => string.Equals(s, x, comparisonType));

        public static Match ShortestMatch(this string s, params Regex[] regexes) {
            var matches = regexes.Select(re => re.Match(s)).Where(x => x.Success).ToList();
            if (matches.None()) { return null; }
            var earliestPos = matches.Min(x => x.Length);
            return matches.FirstOrDefault(x => x.Length == earliestPos);
        }

        public static bool TryParse(this string s, out short? i) {
            if (short.TryParse(s, out short result)) {
                i = result;
                return true;
            } else {
                i = null;
                return false;
            }
        }

        public static bool IsMatchedBy(this string s, Regex re) => re.IsMatch(s);

        public static string Repeated(this string s, int count) => Repeat(s, count).Joined("");
    }
}
