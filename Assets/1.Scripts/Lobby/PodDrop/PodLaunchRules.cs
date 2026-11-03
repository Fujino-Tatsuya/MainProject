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

    /// <summary>한 사람 보행 경로 — 충돌을 켠 채 걷는 앞 구간(개인 경로 → 공용 경로 합류 이후)과 충돌 없이 보간하는 뒤 구간(문턱 … 좌석).</summary>
    public readonly struct WalkRoute
    {
        /// <summary>
        /// 충돌을 켠 채 걷는 점(순서 그대로, 빈 칸 뺌) — 개인 경로점 <see cref="EntryCount"/>개 다음에 공용 경로점(합류 번호부터).
        /// 비면 걷기 시작 지점에서 곧바로 문턱으로 보간한다.
        /// </summary>
        public readonly Vector3[] Ground;

        /// <summary>
        /// <see cref="Ground"/> 각 점의 출처 — 0 이상 = 공용 경로점 리스트 칸 번호(0부터), 음수 = 개인 경로점(−1 = 개인 첫 점, −2 = 둘째 …). 표시·점검 라벨용.
        /// </summary>
        public readonly int[] Source;

        /// <summary>충돌 없이 보간 — 문턱(있으면) → 좌석(마지막). 항상 1개 이상.</summary>
        public readonly Vector3[] Interior;

        /// <summary>개인·공용 경로에서 건너뛴 빈 칸(null) 수(공용은 합류 번호 이후만 센다) — 0 보다 크면 호출자가 경고한다.</summary>
        public readonly int SkippedCount;

        /// <summary><see cref="Ground"/> 앞쪽의 개인 경로점 수 — <see cref="Ground"/>[EntryCount] 가 공용 경로 합류점이다(있으면).</summary>
        public readonly int EntryCount;

        /// <summary>실제로 쓴 합류 번호(범위 안으로 맞춘 값, 0 ~ 공용 경로점 칸 수). 칸 수와 같으면 공용 경로 없이 문턱으로.</summary>
        public readonly int JoinIndex;

        /// <summary>요청한 합류 번호가 범위 밖이라 맞췄는가 — true 면 호출자가 경고한다.</summary>
        public readonly bool JoinClamped;

        /// <summary>문턱이 있는가 — 있으면 <see cref="Interior"/>[0] 이 문턱이다.</summary>
        public bool HasThreshold => Interior.Length > 1;

        public WalkRoute(Vector3[] ground, int[] source, Vector3[] interior, int skippedCount, int entryCount, int joinIndex, bool joinClamped)
        {
            Ground = ground;
            Source = source;
            Interior = interior;
            SkippedCount = skippedCount;
            EntryCount = entryCount;
            JoinIndex = joinIndex;
            JoinClamped = joinClamped;
        }
    }

    /// <summary>
    /// 공용 경로점(<paramref name="walkPath"/>, 리스트 순서 = 걷는 순서, null = 빈 칸 → 건너뜀)·문턱·좌석으로 보행 경로를 만든다(개인 경로 없음, 첫 칸부터 합류).
    /// </summary>
    public static WalkRoute BuildRoute(IReadOnlyList<Vector3?> walkPath, Vector3? threshold, Vector3 seat) =>
        BuildRoute(null, walkPath, 0, threshold, seat);

    /// <summary>
    /// 한 사람 보행 경로 = 걷기 시작 지점 → 개인 경로점(<paramref name="entryPath"/>, 순서 그대로) → 공용 경로점 <paramref name="walkPath"/>[<paramref name="joinIndex"/>..] → 문턱 → 좌석.
    /// 빈 칸(null)은 건너뛴다. <paramref name="joinIndex"/> 는 공용 경로점 리스트 칸 번호(0부터, 빈 칸 포함) — 0 = 처음부터(개인 경로가 비면 C2d 까지와 같다),
    /// 칸 수 = 공용 경로 없이 문턱으로. 범위 밖이면 맞추고 <see cref="WalkRoute.JoinClamped"/> 를 켠다. 경로점 이름·개수와 무관하다.
    /// </summary>
    public static WalkRoute BuildRoute(IReadOnlyList<Vector3?> entryPath, IReadOnlyList<Vector3?> walkPath, int joinIndex, Vector3? threshold, Vector3 seat)
    {
        int pathCount = walkPath != null ? walkPath.Count : 0;
        int join = Mathf.Clamp(joinIndex, 0, pathCount);
        var ground = new List<Vector3>();
        var source = new List<int>();
        int skipped = 0;

        if (entryPath != null)
        {
            for (int i = 0; i < entryPath.Count; i++)
            {
                if (!entryPath[i].HasValue) { skipped++; continue; }
                ground.Add(entryPath[i].Value);
                source.Add(-(i + 1));
            }
        }

        int entryCount = ground.Count;
        for (int i = join; i < pathCount; i++)
        {
            if (!walkPath[i].HasValue) { skipped++; continue; }
            ground.Add(walkPath[i].Value);
            source.Add(i);
        }

        Vector3[] interior = threshold.HasValue ? new[] { threshold.Value, seat } : new[] { seat };
        return new WalkRoute(ground.ToArray(), source.ToArray(), interior, skipped, entryCount, join, join != joinIndex);
    }

    // 보행 길이는 모두 수평(XZ) 길이다(할 일 172) — 앞 구간 이동(BossElevatorTimeline.WalkStepAlongPath)이 남은 수평 길이 ÷ 남은 시간으로 걷기 때문에
    // 시간 계산도 같은 기준이어야 계단·문턱·좌석 높이 차와 무관하게 모든 구간 수평 속도가 걷는 속도와 같다.

    /// <summary>충돌을 켠 채 걷는 앞 구간 수평 길이(m) — 걷기 시작 지점 → 개인 경로점 → 공용 경로점 끝.</summary>
    public static float GroundLength(Vector3 walkStart, WalkRoute route) => PathLengthHorizontal(walkStart, route.Ground);

    /// <summary>충돌 없이 걷는 마지막 경로점(없으면 걷기 시작 지점) → 문턱 수평 길이(m). 문턱이 없으면 0.</summary>
    public static float ApproachLength(Vector3 walkStart, WalkRoute route) =>
        route.HasThreshold ? HorizontalDistance(GroundEnd(walkStart, route), route.Interior[0]) : 0f;

    /// <summary>좌석으로 미끄러져 들어가는 수평 길이(m) — 문턱(없으면 마지막 경로점, 그것도 없으면 걷기 시작 지점) → 좌석.</summary>
    public static float SlideLength(Vector3 walkStart, WalkRoute route) =>
        HorizontalDistance(route.HasThreshold ? route.Interior[0] : GroundEnd(walkStart, route), route.Interior[route.Interior.Length - 1]);

    /// <summary>
    /// 걸어가는 수평 길이(m) — 걷기 시작 지점 → 개인 경로점 → 공용 경로점 → 문턱 → 좌석(앞 구간 + 문턱 구간 + 미끄러짐).
    /// 이 길이 ÷ 걷는 속도 = 그 사람 보행 시간(좌석 도착까지, 할 일 172 — 미끄러짐도 같은 속도).
    /// </summary>
    public static float WalkLength(Vector3 walkStart, WalkRoute route) =>
        GroundLength(walkStart, route) + ApproachLength(walkStart, route) + SlideLength(walkStart, route);

    /// <summary>
    /// 보행 시간 중 앞쪽 <paramref name="headLength"/> 구간의 비율(0~1). 걷는 속도가 일정하도록 길이 비율로 나눈다.
    /// 앞 구간 끝(= 앞 길이 ÷ (앞 + 뒤 길이))·문턱 도착(= 앞 + 문턱 구간 ÷ 전체)에 쓴다. 둘 다 0 이면 1(앞 구간에 다 준다 — 나눌 거리가 없다).
    /// </summary>
    public static float GroundShare(float headLength, float tailLength)
    {
        headLength = Mathf.Max(0f, headLength);
        tailLength = Mathf.Max(0f, tailLength);
        float total = headLength + tailLength;
        return total > 0f ? headLength / total : 1f;
    }

    /// <summary>두 점의 수평(XZ) 거리(m) — 높이 차는 세지 않는다.</summary>
    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = b.x - a.x, dz = b.z - a.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary><paramref name="from"/> → <paramref name="path"/> 의 점들을 잇는 꺾은선의 수평(XZ) 길이(m). 높이 차는 세지 않는다.</summary>
    public static float PathLengthHorizontal(Vector3 from, IReadOnlyList<Vector3> path)
    {
        float length = 0f;
        Vector3 cursor = from;
        for (int i = 0; i < path.Count; i++)
        {
            length += HorizontalDistance(cursor, path[i]);
            cursor = path[i];
        }

        return length;
    }

    private static Vector3 GroundEnd(Vector3 walkStart, WalkRoute route) =>
        route.Ground.Length > 0 ? route.Ground[route.Ground.Length - 1] : walkStart;

    /// <summary>
    /// 꺾은선(<paramref name="from"/> → <paramref name="path"/>) 위 진행률 <paramref name="t"/>(0~1, 수평 길이 기준 — 수평 등속)의 점. 높이는 구간 안에서 같이 보간한다.
    /// 충돌을 무시하고 포드 안으로 보간할 때 쓴다. 점이 없으면 <paramref name="from"/>.
    /// </summary>
    public static Vector3 PointAlong(Vector3 from, IReadOnlyList<Vector3> path, float t)
    {
        if (path.Count == 0) return from;

        float remaining = PathLengthHorizontal(from, path) * Mathf.Clamp01(t);
        Vector3 cursor = from;
        for (int i = 0; i < path.Count; i++)
        {
            float segment = HorizontalDistance(cursor, path[i]);
            if (remaining <= segment)
                return segment > 0f ? Vector3.Lerp(cursor, path[i], remaining / segment) : path[i];

            remaining -= segment;
            cursor = path[i];
        }

        return path[path.Count - 1];
    }
}
