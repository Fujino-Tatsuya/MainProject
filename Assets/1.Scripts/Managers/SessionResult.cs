using System.Collections.Generic;
using UnityEngine;

/// <summary>판이 끝난 방식. 페이로드에 byte 로 실린다 — 값을 바꾸지 말고 뒤에만 추가할 것.</summary>
public enum SessionOutcome : byte
{
    Cleared = 1, // 보스 격파
    Wiped = 2,   // 전멸
    Aborted = 3  // ExitButton 중도 종료 — 누른 시점까지의 집계값 그대로
}

/// <summary>결과 화면 한 행(플레이어 한 명)의 통계.</summary>
public struct PlayerSessionStats
{
    /// <summary>로비 캐릭터 id 를 모를 때(스토어에 없음).</summary>
    public const int UnknownCharacterId = -1;

    public ulong ClientId;
    public int CharacterId;
    public int Damage;   // 몬스터 HP+실드 실제 감소량 합(막타 초과분 제외)
    public int Kills;    // 막타 수
    public int Counters; // 간파 성공 수
    public int Deaths;   // Alive→DeadPresentation 전이 수
}

/// <summary>
/// 한 판의 결과. MapScene에서 채우고 ResultScene이 읽는다.
/// 씬 전환을 넘겨야 하는데 값이 몇 개뿐이라 정적 보관으로 둔다(리슨 서버 로컬 표시 기준).
/// 집계는 서버에서만 한다. 원격 클라이언트는 MapSceneManager의 GoToResult 메시지 페이로드
/// (<see cref="SessionResultPayload"/>)로 같은 값을 받는다.
/// </summary>
public static class SessionResult
{
    private static readonly List<PlayerSessionStats> PlayerRows = new List<PlayerSessionStats>();

    public static bool HasValue { get; private set; }

    public static SessionOutcome Outcome { get; private set; }

    /// <summary>보스 격파 등 목표 달성 여부. 전멸·중도 종료면 false.</summary>
    public static bool Cleared => HasValue && Outcome == SessionOutcome.Cleared;

    /// <summary>플레이 시간(초). 집계 시작(첫 플레이어 스폰)부터 결과 확정까지.</summary>
    public static float SurvivalSeconds { get; private set; }

    /// <summary>플레이어별 행. clientId 오름차순(P1 = 호스트). 판 중 끊긴 플레이어 행도 남는다.</summary>
    public static IReadOnlyList<PlayerSessionStats> Players => PlayerRows;

    public static void Capture(SessionOutcome outcome, float survivalSeconds, IReadOnlyList<PlayerSessionStats> players)
    {
        HasValue = true;
        Outcome = outcome;
        SurvivalSeconds = Mathf.Max(0f, survivalSeconds);

        PlayerRows.Clear();
        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
                PlayerRows.Add(players[i]);
        }

        Debug.Log($"[SessionResult] outcome={Outcome} survival={SurvivalSeconds:F1}s players={PlayerRows.Count}");
    }

    public static void Clear()
    {
        HasValue = false;
        Outcome = default;
        SurvivalSeconds = 0f;
        PlayerRows.Clear();
    }

    /// <summary>mm:ss 표기.</summary>
    public static string FormatSurvival()
    {
        int total = Mathf.FloorToInt(SurvivalSeconds);
        return $"{total / 60:00}:{total % 60:00}";
    }
}
