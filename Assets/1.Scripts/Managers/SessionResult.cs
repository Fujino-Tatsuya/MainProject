using UnityEngine;

/// <summary>
/// 한 판의 결과. MapScene에서 채우고 ResultScene이 읽는다.
/// 씬 전환을 넘겨야 하는데 값이 몇 개뿐이라 정적 보관으로 둔다(리슨 서버 로컬 표시 기준).
/// 원격 클라이언트에도 같은 값을 보여야 하면 서버 브로드캐스트를 얹어야 한다 — 지금은 미구현.
/// </summary>
public static class SessionResult
{
    public static bool HasValue { get; private set; }

    /// <summary>보스 격파 등 목표 달성 여부. 전멸로 끝나면 false.</summary>
    public static bool Cleared { get; private set; }

    public static float SurvivalSeconds { get; private set; }

    public static int Kills { get; private set; }

    public static void Capture(bool cleared, float survivalSeconds, int kills)
    {
        HasValue = true;
        Cleared = cleared;
        SurvivalSeconds = Mathf.Max(0f, survivalSeconds);
        Kills = Mathf.Max(0, kills);

        Debug.Log($"[SessionResult] cleared={Cleared} survival={SurvivalSeconds:F1}s kills={Kills}");
    }

    public static void Clear()
    {
        HasValue = false;
        Cleared = false;
        SurvivalSeconds = 0f;
        Kills = 0;
    }

    /// <summary>mm:ss 표기.</summary>
    public static string FormatSurvival()
    {
        int total = Mathf.FloorToInt(SurvivalSeconds);
        return $"{total / 60:00}:{total % 60:00}";
    }
}
