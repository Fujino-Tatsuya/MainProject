using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로비 포드 탑승 연출(<see cref="LobbyPodLaunch"/>, 할 일 100 C2)의 순수 규칙 — 참가 명단 순서·좌석·걷기 시작 지점·폴백 판단·보행 경로 나누기.
/// 네트워크·씬을 모른다. 서버와 모든 피어가 같은 복제 명단으로 같은 답을 얻는다.
/// </summary>
public static class PodLaunchRules
{
    /// <summary>참가자 순서(= 보행 슬롯 순서) — clientId 오름차순. 슬롯 배정이 결정적이어야 재현·디버깅이 된다(엘리베이터 89 와 같다).</summary>
    public static void OrderParticipants(List<ulong> clientIds) => clientIds.Sort();

    /// <summary>
    /// 슬롯의 지점(좌석·걷기 시작 지점) — 지점보다 사람이 많으면 돌려 쓴다(좌석은 숨김 지점, 걷기 시작 지점은 출발 간격이 있어 겹쳐도 된다). 지점이 없으면 −1.
    /// </summary>
    public static int PointIndex(int slot, int pointCount) => pointCount > 0 ? Mathf.Max(0, slot) % pointCount : -1;

    /// <summary>
    /// 연출을 못 하는 이유(없으면 null) — 있으면 호출자는 연출 없이 곧바로 맵 로딩으로 넘어간다(지금까지와 같은 폴백).
    /// 카메라 지점·포드·웨이포인트는 없어도 연출은 돈다(그 부분만 빠진다). 걷기 시작 지점은 보행 출발점이라 없으면 못 한다.
    /// </summary>
    public static string MissingSetup(bool hasSettings, int seatCount, int walkStartCount, int participantCount)
    {
        if (!hasSettings) return "PodDropSettings 가 비었다";
        if (seatCount <= 0) return "포드 안 좌석(Seat) 지점이 없다";
        if (walkStartCount <= 0) return "걷기 시작 지점(WalkStart)이 없다";
        if (participantCount <= 0) return "참가자가 없다";
        return null;
    }

    /// <summary>한 사람 보행 경로 — 충돌을 켠 채 걷는 앞 구간과 충돌 없이 보간하는 뒤 구간(문턱 … 좌석).</summary>
    public readonly struct WalkRoute
    {
        /// <summary>충돌을 켠 채 걷는 공용 경로점(순서 그대로, 빈 칸 뺌). 비면 걷기 시작 지점에서 곧바로 문턱으로 보간한다.</summary>
        public readonly Vector3[] Ground;

        /// <summary>충돌 없이 보간 — 문턱(있으면) → 좌석(마지막). 항상 1개 이상.</summary>
        public readonly Vector3[] Interior;

        /// <summary>공용 경로에서 건너뛴 빈 칸(null) 수 — 0 보다 크면 호출자가 경고한다.</summary>
        public readonly int SkippedCount;

        /// <summary>문턱이 있는가 — 있으면 <see cref="Interior"/>[0] 이 문턱이다.</summary>
        public bool HasThreshold => Interior.Length > 1;

        public WalkRoute(Vector3[] ground, Vector3[] interior, int skippedCount)
        {
            Ground = ground;
            Interior = interior;
            SkippedCount = skippedCount;
        }
    }

    /// <summary>
    /// 공용 경로점(<paramref name="walkPath"/>, 리스트 순서 = 걷는 순서, null = 빈 칸 → 건너뜀)·문턱·좌석으로 보행 경로를 만든다.
    /// 경로점 이름·개수와 무관하다. 사람마다 다른 건 걷기 시작 지점(첫 경로점으로 합류)과 좌석뿐이다.
    /// </summary>
    public static WalkRoute BuildRoute(IReadOnlyList<Vector3?> walkPath, Vector3? threshold, Vector3 seat)
    {
        var ground = new List<Vector3>(walkPath != null ? walkPath.Count : 0);
        int skipped = 0;
        if (walkPath != null)
        {
            foreach (Vector3? point in walkPath)
            {
                if (point.HasValue) ground.Add(point.Value);
                else skipped++;
            }
        }

        Vector3[] interior = threshold.HasValue ? new[] { threshold.Value, seat } : new[] { seat };
        return new WalkRoute(ground.ToArray(), interior, skipped);
    }

    /// <summary>충돌을 켠 채 걷는 앞 구간 길이(m) — 걷기 시작 지점 → 공용 경로점 끝.</summary>
    public static float GroundLength(Vector3 walkStart, WalkRoute route) => PathLength3D(walkStart, route.Ground);

    /// <summary>
    /// 걸어가는 길이(m) — 걷기 시작 지점 → 공용 경로점 → 문턱(3차원, 계단 높이 포함). 이 길이 ÷ 걷는 속도 = 그 사람 보행 시간.
    /// 문턱 → 좌석 미끄러짐은 들지 않는다(따로 정한 시간).
    /// </summary>
    public static float WalkLength(Vector3 walkStart, WalkRoute route)
    {
        float length = GroundLength(walkStart, route);
        if (route.HasThreshold)
            length += Vector3.Distance(route.Ground.Length > 0 ? route.Ground[route.Ground.Length - 1] : walkStart, route.Interior[0]);
        return length;
    }

    /// <summary>
    /// 한 사람 보행 시간 중 충돌을 켠 채 걷는 앞 구간(웨이포인트까지)의 비율(0~1). 걷는 속도가 일정하도록 길이 비율로 나눈다.
    /// 뒤 구간(마지막 경로점 → 문턱)은 충돌 없이 보간한다. 둘 다 0 이면 1(앞 구간에 다 준다 — 보간할 거리가 없다).
    /// </summary>
    public static float GroundShare(float groundLength, float interiorLength)
    {
        groundLength = Mathf.Max(0f, groundLength);
        interiorLength = Mathf.Max(0f, interiorLength);
        float total = groundLength + interiorLength;
        return total > 0f ? groundLength / total : 1f;
    }

    /// <summary><paramref name="from"/> → <paramref name="path"/> 의 점들을 잇는 꺾은선의 3차원 길이(m). 높이 차(문턱·좌석)도 센다.</summary>
    public static float PathLength3D(Vector3 from, IReadOnlyList<Vector3> path)
    {
        float length = 0f;
        Vector3 cursor = from;
        for (int i = 0; i < path.Count; i++)
        {
            length += Vector3.Distance(cursor, path[i]);
            cursor = path[i];
        }

        return length;
    }

    /// <summary>
    /// 꺾은선(<paramref name="from"/> → <paramref name="path"/>) 위 진행률 <paramref name="t"/>(0~1, 길이 기준 — 등속)의 점.
    /// 충돌을 무시하고 포드 안으로 보간할 때 쓴다. 점이 없으면 <paramref name="from"/>.
    /// </summary>
    public static Vector3 PointAlong(Vector3 from, IReadOnlyList<Vector3> path, float t)
    {
        if (path.Count == 0) return from;

        float remaining = PathLength3D(from, path) * Mathf.Clamp01(t);
        Vector3 cursor = from;
        for (int i = 0; i < path.Count; i++)
        {
            float segment = Vector3.Distance(cursor, path[i]);
            if (remaining <= segment)
                return segment > 0f ? Vector3.Lerp(cursor, path[i], remaining / segment) : path[i];

            remaining -= segment;
            cursor = path[i];
        }

        return path[path.Count - 1];
    }
}
