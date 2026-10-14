using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>게임이 지원하는 언어. 저장·시트 열 이름은 <see cref="LocText.Code"/>(ko / en).</summary>
public enum GameLanguage
{
    Ko = 0,
    En = 1,
}

/// <summary>
/// 문구 규칙(순수 함수, Unity 의존 없음) — 언어 코드 · 자리표시자 추출·문법 · <c>&lt;style&gt;</c> 태그 짝 검사.
/// 문자열 시트 검증(<c>StringTableValidator</c>)과 스킬 툴팁(<see cref="SkillTooltipFormatter"/>)이 이 규칙 하나를 같이 쓴다.
/// 자리표시자 패턴은 <see cref="TutorialGuideText.Fill"/> 과 같은 것을 쓴다(그쪽이 이 정규식을 참조한다).
/// 오류 문구의 위치는 <c>문자 N:</c>(1부터).
/// </summary>
public static class LocText
{
    /// <summary>{Q} {mm:ss} {스킬명} 처럼 중괄호 안에 중괄호가 없는 토큰. 콜론·한글 키를 허용한다.</summary>
    public static readonly Regex Placeholder = new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private const string StyleOpen = "<style=";
    private const string StyleClose = "</style>";

    public static string Code(GameLanguage language) => language == GameLanguage.En ? "en" : "ko";

    /// <summary>언어 이름은 번역하지 않는다 — 각자 자기 언어로 써야 못 읽는 사람도 찾아 돌아온다(설정창 언어 버튼).</summary>
    public static string NativeName(GameLanguage language) => language == GameLanguage.En ? "English" : "한국어";

    /// <summary>다음 언어(버튼 한 번 = 한 칸) — 언어가 둘이라 한국어 ↔ English.</summary>
    public static GameLanguage Next(GameLanguage language) => language == GameLanguage.Ko ? GameLanguage.En : GameLanguage.Ko;

    public static bool TryParse(string code, out GameLanguage language)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case "ko":
                language = GameLanguage.Ko;
                return true;
            case "en":
                language = GameLanguage.En;
                return true;
            default:
                language = GameLanguage.Ko;
                return false;
        }
    }

    /// <summary>문구 안 자리표시자 이름 집합(중복 제거).</summary>
    public static HashSet<string> Placeholders(string text)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
            return names;

        foreach (Match match in Placeholder.Matches(text))
            names.Add(match.Groups[1].Value);

        return names;
    }

    /// <summary>두 문구의 자리표시자 이름 집합이 같은가(순서·반복 횟수는 무시). 언어 간 검사 규칙.</summary>
    public static bool SamePlaceholders(string a, string b) => Placeholders(a).SetEquals(Placeholders(b));

    /// <summary>
    /// 중괄호 문법이 맞으면 null, 아니면 이유 — 짝 없는 <c>}</c>, 닫히지 않은 <c>{</c>, 빈 <c>{}</c>.
    /// 이걸 통과한 문구는 <see cref="Placeholder"/> 가 모든 <c>{…}</c> 를 정확히 잡는다(자리표시자 해석이 한 가지뿐).
    /// </summary>
    public static string BraceError(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '}')
                return ErrorAt(i, "여는 '{'가 없는 '}'이다.");

            if (text[i] != '{')
                continue;

            int close = text.IndexOfAny(new[] { '{', '}' }, i + 1);
            if (close < 0 || text[close] == '{')
                return ErrorAt(i, "닫히지 않은 '{'이다.");

            if (string.IsNullOrWhiteSpace(text.Substring(i + 1, close - i - 1)))
                return ErrorAt(i, "빈 자리표시자다.");

            i = close;
        }

        return null;
    }

    /// <summary>
    /// <c>&lt;style=이름&gt;</c> 와 <c>&lt;/style&gt;</c> 가 순서대로 짝을 이루면 null, 아니면 이유.
    /// 닫는 태그가 여는 태그보다 먼저 나오거나, 끝까지 닫히지 않거나, 여는 태그의 <c>&gt;</c>·이름이 없으면 오류다(중첩 허용).
    /// </summary>
    public static string StyleTagError(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (StartsWith(text, i, StyleOpen))
            {
                int close = text.IndexOf('>', i + StyleOpen.Length);
                if (close < 0)
                    return ErrorAt(i, "닫히지 않은 <style> 태그다.");

                if (close == i + StyleOpen.Length)
                    return ErrorAt(i, "style 이름이 비어 있다.");

                depth++;
                i = close;
            }
            else if (StartsWith(text, i, StyleClose))
            {
                if (depth == 0)
                    return ErrorAt(i, "여는 <style>이 없는 </style>이다.");

                depth--;
                i += StyleClose.Length - 1;
            }
        }

        return depth == 0 ? null : $"문자 {text.Length + 1}: 닫히지 않은 <style> 태그가 {depth}개 있다.";
    }

    /// <summary>오류 위치 표기 — <c>문자 N: 이유</c>(N 은 1부터).</summary>
    public static string ErrorAt(int zeroBasedIndex, string message) => $"문자 {zeroBasedIndex + 1}: {message}";

    private static bool StartsWith(string text, int index, string value) =>
        index + value.Length <= text.Length && string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
}
