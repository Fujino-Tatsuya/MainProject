/// <summary>결과창 등급(할 일 223) — 소요 시간이 짧을수록 높다.</summary>
public enum LobbyResultGradeRank { S, A, B, C }

/// <summary>
/// 로비 결과창 등급 도장 규칙(할 일 223) — MonoBehaviour 밖 순수 함수(EditMode 테스트 대상).
/// 소요 시간 ≤ S 경계 = S, ≤ A 경계 = A, ≤ B 경계 = B, 그보다 길면 C. 경계 = 분(인스펙터 <see cref="LobbyResultPanel"/>, 기본 6/9/12).
/// 판단은 화면 표기(mm:ss, 초 아래 버림)와 같은 정수 초로 한다 — 06:00.4 는 "06:00" 으로 보이므로 S.
/// 판 결과(클리어/전멸/중단)와 무관하게 시간만 본다.
/// </summary>
public static class LobbyResultGrade
{
    public const float DefaultSMaxMinutes = 6f;
    public const float DefaultAMaxMinutes = 9f;
    public const float DefaultBMaxMinutes = 12f;

    /// <summary>소요 시간(초) → 등급. 경계(분)가 뒤집혀 있어도 위에서부터 차례로 본다(S → A → B).</summary>
    public static LobbyResultGradeRank Evaluate(float seconds, float sMaxMinutes, float aMaxMinutes, float bMaxMinutes)
    {
        float whole = seconds > 0f ? (float)System.Math.Floor(seconds) : 0f;
        if (whole <= sMaxMinutes * 60f) return LobbyResultGradeRank.S;
        if (whole <= aMaxMinutes * 60f) return LobbyResultGradeRank.A;
        if (whole <= bMaxMinutes * 60f) return LobbyResultGradeRank.B;
        return LobbyResultGradeRank.C;
    }

    /// <summary>기본 경계(6/9/12분).</summary>
    public static LobbyResultGradeRank Evaluate(float seconds) =>
        Evaluate(seconds, DefaultSMaxMinutes, DefaultAMaxMinutes, DefaultBMaxMinutes);

    /// <summary>도장 위 글자.</summary>
    public static string Letter(LobbyResultGradeRank rank)
    {
        switch (rank)
        {
            case LobbyResultGradeRank.S: return "S";
            case LobbyResultGradeRank.A: return "A";
            case LobbyResultGradeRank.B: return "B";
            default: return "C";
        }
    }
}
