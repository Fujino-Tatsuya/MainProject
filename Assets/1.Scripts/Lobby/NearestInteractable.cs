using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로비 상호작용 대상 고르기·거리 판정(순수, EditMode 테스트 대상) — PLAN-48 §4.6, C10.
/// 거리는 모두 <b>평면(XZ)</b> 거리다. 높이 차(계단·소품 위 표식)는 반경에 넣지 않는다.
/// 클라 <see cref="LobbyInteractor"/>(프롬프트·F)와 서버 <see cref="LobbySession"/>(준비 요청 재검증)가 같은 판정을 쓴다.
/// </summary>
public static class NearestInteractable
{
    /// <summary>고를 후보 하나 — 위치·상호작용 반경·지금 쓸 수 있는가.</summary>
    public readonly struct Candidate
    {
        public Candidate(Vector3 position, float radius, bool usable)
        {
            Position = position;
            Radius = radius;
            Usable = usable;
        }

        public Vector3 Position { get; }
        public float Radius { get; }
        public bool Usable { get; }
    }

    /// <summary>
    /// 반경 안(경계 포함)의 사용 가능한 후보 중 평면 거리가 가장 가까운 것의 인덱스. 없으면 -1.
    /// 동률이면 앞 인덱스가 이긴다(매 프레임 같은 대상이 골라져 프롬프트가 떨리지 않는다).
    /// </summary>
    public static int Pick(Vector3 playerPosition, IReadOnlyList<Candidate> candidates)
    {
        if (candidates == null)
            return -1;

        var best = -1;
        var bestSqr = float.MaxValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!candidate.Usable || candidate.Radius < 0f)
                continue;

            var sqr = PlanarSqrDistance(playerPosition, candidate.Position);
            if (sqr > candidate.Radius * candidate.Radius || sqr >= bestSqr)
                continue;

            best = i;
            bestSqr = sqr;
        }

        return best;
    }

    /// <summary>평면 거리가 <paramref name="radius"/> + <paramref name="tolerance"/> 이하인가(경계 포함). 서버 재검증용 — 여유는 클라·서버 위치 어긋남 몫.</summary>
    public static bool IsWithinReach(Vector3 playerPosition, Vector3 targetPosition, float radius, float tolerance)
    {
        var reach = Mathf.Max(0f, radius) + Mathf.Max(0f, tolerance);
        return PlanarSqrDistance(playerPosition, targetPosition) <= reach * reach;
    }

    public static float PlanarDistance(Vector3 a, Vector3 b) => Mathf.Sqrt(PlanarSqrDistance(a, b));

    private static float PlanarSqrDistance(Vector3 a, Vector3 b)
    {
        var dx = a.x - b.x;
        var dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
