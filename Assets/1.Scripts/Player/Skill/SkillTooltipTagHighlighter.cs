using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 툴팁 설명문 안의 태그 이름(넉백·저지불가… — 현재 언어 표시 이름 <see cref="SkillTooltipTags.Name"/>)을
/// 그 태그 칩 색으로 칠한다(할 일 208). 스킬 툴팁·특성 카드·특성 호버 툴팁이 렌더 직전 한 번 부른다.
/// 8종 전부 대상(그 스킬에 붙은 태그만이 아님). 색 출처 = 칩 색 표 <see cref="SkillTooltipTagStyles"/> 한 곳.
/// <list type="bullet">
/// <item>rich text 태그(<c>&lt;…&gt;</c>)·남은 <c>{…}</c> 자리표시자 안은 건드리지 않는다.</item>
/// <item>이미 <c>&lt;color&gt;</c>·<c>&lt;style&gt;</c> 안에 있는 글자(예 <c>&lt;style=heal&gt;회복&lt;/style&gt;</c>, {dmg} 숫자)는 다시 감싸지 않는다.</item>
/// <item>긴 이름 우선(같은 위치에서 "저지불가" 가 "저지" 보다 먼저). 대소문자 무관.</item>
/// <item>영문 등 글자·숫자로 시작/끝나는 ASCII 이름은 단어 경계에서만 맞춘다("Heal" 은 "Healing" 안에서 칠하지 않음). 한글은 조사가 붙으므로 경계 없음.</item>
/// </list>
/// </summary>
public static class SkillTooltipTagHighlighter
{
    /// <summary>칠할 단어 하나 — 색은 <c>#RRGGBB</c>(또는 <c>#RRGGBBAA</c>).</summary>
    public readonly struct Term
    {
        public Term(string word, string colorHex)
        {
            Word = word;
            ColorHex = colorHex;
        }

        public string Word { get; }
        public string ColorHex { get; }
    }

    /// <summary>
    /// 칩 색 표에서 8종 태그의 (현재 언어 이름, 색) 목록. 표가 없으면 빈 목록(칠하지 않음).
    /// 색 = 칩 글자·테두리색(<see cref="SkillTooltipTagStyles.TryGetColors"/> 의 text) — 칩 배경색은 어두운 반투명이라
    /// 툴팁 패널 위 글자로는 읽히지 않는다(대비 약 1.4:1).
    /// </summary>
    public static List<Term> TermsFrom(SkillTooltipTagStyles styles)
    {
        var terms = new List<Term>();
        if (styles == null)
            return terms;

        foreach (SkillTooltipTag tag in (SkillTooltipTag[])Enum.GetValues(typeof(SkillTooltipTag)))
        {
            styles.TryGetColors(tag, out _, out Color32 text);
            terms.Add(new Term(SkillTooltipTags.Name(tag).Text, "#" + ColorUtility.ToHtmlStringRGB(text)));
        }

        return terms;
    }

    /// <summary>표 하나로 바로 칠한다(표가 없으면 그대로).</summary>
    public static string Highlight(string text, SkillTooltipTagStyles styles) =>
        styles == null ? text : Highlight(text, TermsFrom(styles));

    /// <summary>순수 함수 — <paramref name="terms"/> 의 단어를 <c>&lt;color=#…&gt;단어&lt;/color&gt;</c> 로 감싼다.</summary>
    public static string Highlight(string text, IReadOnlyList<Term> terms)
    {
        if (string.IsNullOrEmpty(text) || terms == null || terms.Count == 0)
            return text;

        var sorted = new List<Term>();
        for (int i = 0; i < terms.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(terms[i].Word) && !string.IsNullOrEmpty(terms[i].ColorHex))
                sorted.Add(terms[i]);
        }

        if (sorted.Count == 0)
            return text;
        sorted.Sort((a, b) => b.Word.Length.CompareTo(a.Word.Length));

        var result = new StringBuilder(text.Length + 32);
        int colorDepth = 0;
        int index = 0;
        while (index < text.Length)
        {
            char c = text[index];
            if (c == '<')
            {
                int close = text.IndexOf('>', index + 1);
                if (close > index)
                {
                    string tag = text.Substring(index, close - index + 1);
                    colorDepth = Math.Max(0, colorDepth + ColorDepthDelta(tag));
                    result.Append(tag);
                    index = close + 1;
                    continue;
                }
            }
            else if (c == '{')
            {
                int close = text.IndexOf('}', index + 1);
                if (close > index)
                {
                    result.Append(text, index, close - index + 1);
                    index = close + 1;
                    continue;
                }
            }

            if (colorDepth == 0 && TryMatch(text, index, sorted, out Term term))
            {
                result.Append("<color=").Append(term.ColorHex).Append('>')
                    .Append(text, index, term.Word.Length)
                    .Append("</color>");
                index += term.Word.Length;
                continue;
            }

            result.Append(c);
            index++;
        }

        return result.ToString();
    }

    // <color…>/<style…> 열면 +1, </color>/</style> 닫으면 -1. 나머지 태그(<b>·<nobr>·<sprite …> 등)는 0.
    private static int ColorDepthDelta(string tag)
    {
        if (StartsWithTag(tag, "</color") || StartsWithTag(tag, "</style"))
            return -1;
        if (StartsWithTag(tag, "<color") || StartsWithTag(tag, "<style"))
            return 1;
        return 0;
    }

    private static bool StartsWithTag(string tag, string name)
    {
        if (!tag.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            return false;
        char next = tag.Length > name.Length ? tag[name.Length] : '>';
        return next == '>' || next == '=' || next == ' ';
    }

    private static bool TryMatch(string text, int index, List<Term> terms, out Term match)
    {
        for (int i = 0; i < terms.Count; i++)
        {
            string word = terms[i].Word;
            if (index + word.Length > text.Length)
                continue;
            if (string.Compare(text, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                continue;
            if (IsAsciiWordChar(word[0]) && index > 0 && IsAsciiWordChar(text[index - 1]))
                continue;
            int end = index + word.Length;
            if (IsAsciiWordChar(word[word.Length - 1]) && end < text.Length && IsAsciiWordChar(text[end]))
                continue;

            match = terms[i];
            return true;
        }

        match = default;
        return false;
    }

    private static bool IsAsciiWordChar(char c) => c < 128 && char.IsLetterOrDigit(c);
}
