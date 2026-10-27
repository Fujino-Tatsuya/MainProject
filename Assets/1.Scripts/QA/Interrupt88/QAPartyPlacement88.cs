using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 할 일 88 — 파티를 대상 보스 바로 옆에 둘 후보 지점과 간격 선택, 런 전 위치 검사, 봇 낭떠러지 판정(순수 계산, EditMode <c>QAPartyPlacement88Tests</c>).
/// <para>스모크 4차(88-20261022_145020): 구간 사각형 격자(<c>MapSceneDeathRules.TryFindAreaSpreadPoints</c>)는 중간보스 <b>섬</b>
/// (다리 개통 전 NavMesh 가 끊긴 중앙 섬) 밖 플랫폼을 골랐다 — 보스는 경로가 없어 10분 동안 한 번도 공격하지 않았고(창 0),
/// 클라 봇은 NavMesh 가 없어(클라는 베이크 안 함) 직선으로 걷다 틈에 떨어졌다. 그래서 후보를 보스 NavMesh 위치 둘레 링으로 만들고
/// 서버가 보스→후보 경로가 끊기지 않은(같은 섬) 것만 고른다. 실제 NavMesh·물리 검사는 <c>QABossStart88</c> 이 한다.</para>
/// </summary>
public static class QAPartyPlacement88
{
    /// <summary>링 반경(m) — 가까운 것부터. 3m = 스매시 반경 안(창이 열리는 공격 사거리).</summary>
    public static readonly float[] DefaultRadii = { 3f, 4.5f, 6f, 8f };
    /// <summary>링 하나의 각도 칸 수.</summary>
    public const int DefaultSteps = 16;
    /// <summary>파티원 사이 최소 수평 간격(m).</summary>
    public const float DefaultSpacing = 1.5f;
    /// <summary>보스→후보 NavMesh 경로가 직선의 이 배 + <see cref="DetourSlackMeters"/> 보다 길면 우회(섬을 돌아가는 길)로 본다.</summary>
    public const float MaxDetourRatio = 1.6f;
    public const float DetourSlackMeters = 1f;

    /// <summary>봇 낭떠러지 검사 — 이동 방향으로 이만큼 앞(m)의 바닥을 본다.</summary>
    public const float EdgeProbeAhead = 0.8f;
    /// <summary>발 높이보다 이만큼(m) 아래까지 바닥이 없으면 낭떠러지.</summary>
    public const float EdgeMaxDrop = 1.2f;

    /// <summary><paramref name="center"/> 둘레 링 후보 — 반경 오름차순, 같은 반경은 각도 순. 높이는 center 그대로.</summary>
    public static void RingCandidates(Vector3 center, IReadOnlyList<float> radii, int steps, List<Vector3> into)
    {
        into.Clear();
        if (radii == null || steps <= 0)
            return;
        foreach (float r in radii)
        {
            if (!(r > 0f))
                continue;
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                into.Add(new Vector3(center.x + Mathf.Cos(a) * r, center.y, center.z + Mathf.Sin(a) * r));
            }
        }
    }

    /// <summary>경로 길이가 직선 대비 우회가 아닌가. 음수(경로 없음)면 false.</summary>
    public static bool IsDirectPath(float pathLength, float straightDistance) =>
        pathLength >= 0f && pathLength <= straightDistance * MaxDetourRatio + DetourSlackMeters;

    /// <summary>
    /// 통과한 후보(가까운 순)에서 서로 수평 <paramref name="minSpacing"/> 이상 떨어진 것을 앞에서부터 <paramref name="count"/> 개 고른다. 고른 수.
    /// </summary>
    public static int SelectSpread(IReadOnlyList<Vector3> accepted, int count, float minSpacing, List<Vector3> into)
    {
        into.Clear();
        if (accepted == null || count <= 0)
            return 0;
        foreach (Vector3 p in accepted)
        {
            bool spaced = true;
            foreach (Vector3 q in into)
            {
                if (HorizontalDistance(p, q) < minSpacing)
                {
                    spaced = false;
                    break;
                }
            }
            if (!spaced)
                continue;
            into.Add(p);
            if (into.Count >= count)
                break;
        }
        return into.Count;
    }

    /// <summary>런 전 위치 검사 — 중간보스 근처 반경(m). 링 최대 반경 8m + 보스가 쫓아오며 벌어지는 여유.</summary>
    public const float MidbossNearMeters = 12f;
    /// <summary>런 전 위치 검사 — 23호 근처 반경(m). 보스방 안이면 통과(23호는 점프·돌진으로 방 안을 넓게 움직인다).</summary>
    public const float TwentyThreeNearMeters = 25f;
    /// <summary>런 전 위치 검사 — 높이 차 상한(m). 섬 아래로 떨어졌거나 다른 층이면 근처가 아니다.</summary>
    public const float NearMaxDy = 4f;

    public static float NearRadius(QAInterruptTargetKind kind) =>
        kind == QAInterruptTargetKind.TwentyThree ? TwentyThreeNearMeters : MidbossNearMeters;

    /// <summary>런 전 위치 검사 — 플레이어가 대상 보스 근처인가(수평 반경 안 + 높이 차 <see cref="NearMaxDy"/> 이하).</summary>
    public static bool IsNearTarget(Vector3 player, Vector3 boss, float radius) =>
        HorizontalDistance(player, boss) <= radius && Mathf.Abs(player.y - boss.y) <= NearMaxDy;

    /// <summary>
    /// 봇이 이 방향으로 걸어도 되는가 — 앞 바닥을 찾았고(<paramref name="groundFound"/>) 그 높이가 발보다 <see cref="EdgeMaxDrop"/> 넘게 낮지 않다.
    /// </summary>
    public static bool IsSafeStep(bool groundFound, float footY, float groundY) =>
        groundFound && footY - groundY <= EdgeMaxDrop;

    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
