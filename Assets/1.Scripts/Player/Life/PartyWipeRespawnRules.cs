using System;
using System.Collections.Generic;

// 전멸 → 리스폰의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상). 할 일 30 (2).
// 원본: Docs/tech/party-lives-respawn.md §3. 배선은 PartyWipeRespawner(서버)가 한다.

/// <summary>전멸 판정 뒤 무엇을 하는가.</summary>
public enum PartyWipeOutcome : byte
{
    /// <summary>전멸 아님 — 게임 계속.</summary>
    None = 0,
    /// <summary>결과 화면(기존 흐름).</summary>
    Result = 1,
    /// <summary>복도4(절차 맵 = 보스 진입 패드 밖)로 전원 리스폰 + 보스 리셋.</summary>
    Respawn = 2,
}

/// <summary>
/// 전멸 리스폰 규칙.
///  · 전멸이면 리스폰 가능할 때 리스폰, 아니면 결과 화면. 보스 클리어·나가기는 이 판정을 거치지 않는다(GoToResult 직접 호출).
///  · 리스폰 가능 = 보스가 아직 안 죽었고(죽었으면 결과 전환 예약 중 — 클리어), 튜토리얼이면 G10(보스 진입 존·패드) 열림,
///    절차 맵이면 보스 진입 패드가 씬에 있음.
///  · 리스폰 지점 = 후보(바닥·점유 검사 통과) 중 패드 범위 + 여유 밖, 기준점에 가까운 순으로 서로 <c>minSpacing</c> 이상 떨어진 점.
///    모자라면 간격을 버리고 남은 후보로, 그래도 모자라면 고른 점을 돌려 쓴다(겹침은 최후 수단).
/// </summary>
public static class PartyWipeRespawnRules
{
    /// <summary>리스폰 지점 사이 최소 간격(m) — 부활 지점 격자(1.5m)보다 넓게.</summary>
    public const float DefaultSpacing = 2f;
    /// <summary>절차 맵 폴백 — 패드 범위 + 여유 밖으로 이만큼 더(m).</summary>
    public const float DefaultRingOffset = 1.5f;
    /// <summary>절차 맵 폴백 — 패드 둘레 링 위 후보 간격(m).</summary>
    public const float DefaultRingStep = 1f;

    public static PartyWipeOutcome Decide(bool wiped, bool respawnAvailable) =>
        !wiped ? PartyWipeOutcome.None : respawnAvailable ? PartyWipeOutcome.Respawn : PartyWipeOutcome.Result;

    /// <summary>리스폰할 수 있는 판인가. 튜토리얼이면 G10 열림, 아니면 보스 진입 패드 있음. 보스가 이미 죽었으면 불가(클리어).</summary>
    public static bool CanRespawn(bool tutorialActive, bool bossEnterGateOpen, bool hasBossPad, bool bossDefeated)
    {
        if (bossDefeated) return false;
        return tutorialActive ? bossEnterGateOpen : hasBossPad;
    }

    /// <summary>
    /// 후보 중 <paramref name="count"/> 개를 고른다(인덱스를 <paramref name="chosen"/> 에). 패드 범위 + <paramref name="padMargin"/> 안은 버린다.
    /// 기준점(<paramref name="refX"/>, <paramref name="refZ"/>)에 가까운 순으로 서로 <paramref name="minSpacing"/> 이상인 점을 먼저,
    /// 모자라면 간격 무시, 그래도 모자라면 고른 점을 처음부터 돌려 쓴다. 쓸 후보가 하나도 없으면 false(<paramref name="chosen"/> 비움).
    /// </summary>
    public static bool TrySelectSpread(IReadOnlyList<(float x, float z)> candidates, int count, float refX, float refZ, float minSpacing,
                                       IReadOnlyList<MapScenePadArea> pads, float padMargin, List<int> chosen)
    {
        chosen.Clear();
        if (candidates == null || count <= 0) return false;

        var order = new List<int>();
        for (int i = 0; i < candidates.Count; i++)
            if (!MapSceneRevivePointRules.IsOnPad(pads, candidates[i].x, candidates[i].z, padMargin)) order.Add(i);
        if (order.Count == 0) return false;

        order.Sort((a, b) =>
        {
            int c = DistSq(candidates[a], refX, refZ).CompareTo(DistSq(candidates[b], refX, refZ));
            return c != 0 ? c : a.CompareTo(b);
        });

        float minSq = Math.Max(0f, minSpacing) * Math.Max(0f, minSpacing);
        foreach (int i in order)
        {
            if (chosen.Count >= count) break;
            if (IsFarFromAll(candidates, chosen, candidates[i], minSq)) chosen.Add(i);
        }
        foreach (int i in order)
        {
            if (chosen.Count >= count) break;
            if (!chosen.Contains(i)) chosen.Add(i);
        }
        int unique = chosen.Count;
        for (int k = 0; chosen.Count < count; k++) chosen.Add(chosen[k % unique]);
        return true;
    }

    /// <summary>
    /// 절차 맵 폴백 후보 — 패드 범위를 <paramref name="padMargin"/> + <paramref name="offset"/> 만큼 넓힌 둘레 위 점(<paramref name="step"/> 간격).
    /// 사각형은 네 변을, 원은 원둘레를 돈다. 전부 패드 범위 + 여유 밖이다. 바닥·점유 검사는 호출자 몫.
    /// </summary>
    public static void RingCandidates(MapScenePadArea pad, float padMargin, float offset, float step, List<(float x, float z)> into)
    {
        float grow = Math.Max(0f, padMargin) + Math.Max(0.01f, offset);
        step = Math.Max(0.1f, step);

        if (pad.IsCircle)
        {
            float r = pad.Radius + grow;
            int n = Math.Max(8, (int)Math.Ceiling(2d * Math.PI * r / step));
            for (int i = 0; i < n; i++)
            {
                double a = 2d * Math.PI * i / n;
                into.Add((pad.CenterX + (float)(Math.Cos(a) * r), pad.CenterZ + (float)(Math.Sin(a) * r)));
            }
            return;
        }

        float hx = pad.HalfX + grow, hz = pad.HalfZ + grow;
        int nx = Math.Max(1, (int)Math.Ceiling(2f * hx / step));
        int nz = Math.Max(1, (int)Math.Ceiling(2f * hz / step));
        for (int i = 0; i <= nx; i++)
        {
            float x = pad.CenterX - hx + 2f * hx * i / nx;
            into.Add((x, pad.CenterZ - hz));
            into.Add((x, pad.CenterZ + hz));
        }
        for (int j = 1; j < nz; j++)
        {
            float z = pad.CenterZ - hz + 2f * hz * j / nz;
            into.Add((pad.CenterX - hx, z));
            into.Add((pad.CenterX + hx, z));
        }
    }

    private static float DistSq((float x, float z) p, float x, float z)
    {
        float dx = p.x - x, dz = p.z - z;
        return dx * dx + dz * dz;
    }

    private static bool IsFarFromAll(IReadOnlyList<(float x, float z)> c, List<int> chosen, (float x, float z) p, float minSq)
    {
        foreach (int j in chosen)
            if (DistSq(c[j], p.x, p.z) < minSq) return false;
        return true;
    }
}
