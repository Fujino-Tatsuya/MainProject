using System;

/// <summary>특성 카드 C1 의 키캡 자리 표시(할 일 165). 키캡 4종 + 마우스 표시 2종 중 하나.</summary>
public enum TraitKeycap
{
    Blank,
    Q,
    E,
    R,
    LeftMouse,
    RightMouse,
}

/// <summary>
/// 특성 아이콘 파일 이름 → 키캡 판정(할 일 165) — 규칙은 여기 한 곳.
/// 이름을 <c>_</c> 로 나눈 토큰 중 <c>Q</c>/<c>E</c>/<c>R</c>/<c>LMB</c>/<c>RMB</c> 와 정확히 같은 것이 있으면 그 키(왼쪽부터 첫 토큰).
/// 그래서 <c>Trait_Gunner_Q_Change</c>·<c>Trait_Assassin_E_Trans</c>·<c>Trait_Assassin_R_Remove</c> 처럼 키 뒤에 접미사가 붙어도 그 키다.
/// 키 토큰이 없으면(<c>Passive</c>·<c>Heat</c>·<c>Heat_Remove</c> 등) 빈 키캡. 대소문자는 구분한다.
/// </summary>
public static class TraitKeycapRule
{
    /// <param name="iconName">스프라이트 이름 또는 파일 이름/경로(확장자·폴더는 무시). 비면 빈 키캡.</param>
    public static TraitKeycap FromIconName(string iconName)
    {
        if (string.IsNullOrEmpty(iconName))
            return TraitKeycap.Blank;

        string name = iconName;
        int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        if (slash >= 0)
            name = name.Substring(slash + 1);
        int dot = name.LastIndexOf('.');
        if (dot > 0)
            name = name.Substring(0, dot);

        string[] tokens = name.Split('_');
        for (int i = 0; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "Q": return TraitKeycap.Q;
                case "E": return TraitKeycap.E;
                case "R": return TraitKeycap.R;
                case "LMB": return TraitKeycap.LeftMouse;
                case "RMB": return TraitKeycap.RightMouse;
            }
        }

        return TraitKeycap.Blank;
    }
}
