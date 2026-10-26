using System.Collections.Generic;

/// <summary>집결 표시 대상 후보 한 개 — <see cref="PartyGatherDisplayRules.Pick"/>.</summary>
public readonly struct PartyGatherDisplayCandidate
{
    public PartyGatherDisplayCandidate(bool showable, bool containsLocal, float distanceSqr)
    {
        Showable = showable;
        ContainsLocal = containsLocal;
        DistanceSqr = distanceSqr;
    }

    /// <summary>표시할 수 있는 구역(활성 + 확정 전).</summary>
    public bool Showable { get; }

    /// <summary>로컬 플레이어 캐릭터가 구역 안.</summary>
    public bool ContainsLocal { get; }

    /// <summary>로컬 플레이어 캐릭터 ~ 구역 중심 거리 제곱(가까운 구역 고르기용).</summary>
    public float DistanceSqr { get; }
}

/// <summary>
/// [클라 표시용] 활성 집결 구역이 여럿일 때 상태 UI(<see cref="PartyGatherStatusView"/>)가 보여 줄 구역 — 할 일 86 T2c(사용자 결정 2026-10-22).
/// 판정은 로컬 위치 기준 표시용이고 서버 집결 판정과 무관하다. Unity 비의존(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>로컬 캐릭터가 서 있는 표시 가능 구역이 있으면 그 구역(여럿이면 앞쪽).</item>
/// <item>아무 구역에도 없으면 가장 가까운 표시 가능 구역(같으면 앞쪽).</item>
/// <item>로컬 위치를 모르면(캐릭터 스폰 전) 첫 표시 가능 구역.</item>
/// </list>
/// </summary>
public static class PartyGatherDisplayRules
{
    /// <summary>고를 구역이 없음.</summary>
    public const int None = -1;

    /// <summary>표시할 후보 인덱스(없으면 <see cref="None"/>).</summary>
    public static int Pick(IReadOnlyList<PartyGatherDisplayCandidate> candidates, bool hasLocalPosition)
    {
        var nearest = None;
        var nearestDistance = float.MaxValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!candidate.Showable)
                continue;

            if (!hasLocalPosition)
                return i;

            if (candidate.ContainsLocal)
                return i;

            if (candidate.DistanceSqr < nearestDistance)
            {
                nearest = i;
                nearestDistance = candidate.DistanceSqr;
            }
        }

        return nearest;
    }
}
