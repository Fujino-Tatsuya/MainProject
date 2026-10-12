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
/// 문구 규칙(순수 함수, Unity 의존 없음) — 언어 코드 · 자리표시자 추출 · <c>&lt;style&gt;</c> 태그 짝 검사.
/// 자리표시자 패턴은 <see cref="TutorialGuideText.Fill"/> 과 같은 것을 쓴다(그쪽이 이 정규식을 참조한다).
/// </summary>
public static class LocText
{
    /// <summary>{Q} {mm:ss} {스킬명} 처럼 중괄호 안에 중괄호가 없는 토큰. 콜론·한글 키를 허용한다.</summary>
    public static readonly Regex Placeholder = new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private static readonly Regex StyleTag = new Regex(@"<style=[^<>]*>|</style>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Code(GameLanguage language) => language == GameLanguage.En ? "en" : "ko";

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

    /// <summary>
    /// <c>&lt;style=…&gt;</c> 와 <c>&lt;/style&gt;</c> 가 순서대로 짝을 이루면 null, 아니면 이유.
    /// 닫는 태그가 여는 태그보다 먼저 나오거나, 끝까지 닫히지 않으면 오류다.
    /// </summary>
    public static string StyleTagError(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        int open = 0;
        foreach (Match match in StyleTag.Matches(text))
        {
            if (match.Value.StartsWith("</", StringComparison.Ordinal))
            {
                if (open == 0)
                    return $"여는 태그 없이 </style> 이 나왔다(위치 {match.Index}).";

                open--;
            }
            else
            {
                open++;
            }
        }

        return open == 0 ? null : $"<style=…> {open}개가 </style> 로 닫히지 않았다.";
    }
}
