using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이동 플랫폼. MapScene 비의존 자기완결 프리팹의 컨트롤러(부모 오브젝트에 부착).
///
/// 구조: 부모(이 컴포넌트, 고정) 아래에
///   - <see cref="platformBody"/> : 실제로 이동하는 큐브(라이더 판정 콜라이더 포함, 가시 메시보다 크게)
///   - waypoints                  : 고정 앵커(부모 자식). 이동 대상이 아님.
///
/// 동기 모델: NetworkObject/NetworkTransform 없음. 위치 = f(<see cref="NetworkClock.MainGameElapsed"/>)
/// 순수함수라 모든 클라가 동일 계산으로 일치한다. dt 적분 금지(FP 드리프트 방지).
/// </summary>
public class MovingPlatform : MonoBehaviour
{
    /// <summary>
    /// 경로 형태만 결정한다(정지 여부는 각 노드의 <see cref="WaypointNode"/>가 담당).
    /// 순환(Cycle): 닫힌 루프(A→B→C→D→A).
    /// 반복(PingPong): 왕복 바운스(A→B→C→B→A…).
    /// </summary>
    public enum PathMode { Cycle, PingPong }

    [Header("구성")]
    [Tooltip("실제로 이동하는 자식(큐브). 라이더 판정 콜라이더는 가시 메시보다 크게.")]
    [SerializeField] private Transform platformBody;
    [Tooltip("고정 웨이포인트(부모 자식). 최소 2개.")]
    [SerializeField] private List<Transform> waypoints = new List<Transform>();

    [Header("모드")]
    [SerializeField] private PathMode mode = PathMode.Cycle;

    [Header("이동 프로파일")]
    [SerializeField] private float cruiseSpeed = 3f;   // 순항 속도 (m/s)
    [SerializeField] private float acceleration = 4f;  // 가/감속도 (m/s^2)

    [Header("정지 시간")]
    [Tooltip("목적지 노드의 기본 정지시간(초). 노드가 WaypointNode로 개별 지정하면 그 값이 우선.")]
    [SerializeField] private float defaultPauseSeconds = 1f;

    /// <summary>이번 프레임 이동량(월드). 소유자측 플레이어 캐리가 읽는다.</summary>
    public Vector3 CurrentDelta { get; private set; }

    private struct Segment
    {
        public Vector3 start;
        public Vector3 end;
        public double distance;
        public double accelTime;   // 가속(=감속) 시간
        public double cruiseTime;  // 순항 시간(0이면 삼각 프로파일)
        public double peakSpeed;   // 삼각일 때 실제 도달 속도
        public double moveTime;    // 이동 총시간
        public double pauseAfter;  // 도착 후 정지
    }

    private readonly List<Segment> _segments = new List<Segment>();
    private double _period;
    private bool _built;
    private Vector3 _prevPos;
    private bool _hasPrev;

    private void Awake()
    {
        BuildTimeline();
        if (_built && platformBody != null)
        {
            platformBody.position = _segments[0].start;
            _prevPos = platformBody.position;
            _hasPrev = true;
        }
    }

    private void Update()
    {
        if (!_built || platformBody == null)
        {
            CurrentDelta = Vector3.zero;
            return;
        }

        double elapsed = 0.0;
        var clock = NetworkClock.Instance;
        if (clock != null && clock.HasMainGameStarted)
        {
            elapsed = clock.MainGameElapsed;
        }
        // MainGame 미시작이면 elapsed=0 → 시작점에서 정지.

        Vector3 newPos = Evaluate(elapsed);
        CurrentDelta = _hasPrev ? newPos - _prevPos : Vector3.zero;
        platformBody.position = newPos;
        _prevPos = newPos;
        _hasPrev = true;
    }

    /// <summary>elapsed(초) → 월드 위치. 순수함수(적분 없음).</summary>
    private Vector3 Evaluate(double elapsed)
    {
        if (_period <= 0.0)
        {
            return _segments[0].start;
        }

        double t = elapsed % _period;
        for (int i = 0; i < _segments.Count; i++)
        {
            Segment seg = _segments[i];
            if (t < seg.moveTime)
            {
                double s = DistanceAt(seg, t);
                Vector3 delta = seg.end - seg.start;
                double len = delta.magnitude;
                if (len < 1e-6)
                {
                    return seg.start;
                }
                return seg.start + (delta / (float)len) * (float)s;
            }
            t -= seg.moveTime;

            if (t < seg.pauseAfter)
            {
                return seg.end; // 정지 구간
            }
            t -= seg.pauseAfter;
        }

        return _segments[_segments.Count - 1].end;
    }

    /// <summary>세그먼트 로컬 시간 τ에서 시작점부터의 이동 거리(사다리꼴/삼각 프로파일).</summary>
    private double DistanceAt(Segment seg, double tau)
    {
        double a = acceleration;
        double accelDist = 0.5 * a * seg.accelTime * seg.accelTime;

        if (tau <= seg.accelTime)
        {
            return 0.5 * a * tau * tau;
        }

        if (seg.cruiseTime > 0.0)
        {
            double cruiseEnd = seg.accelTime + seg.cruiseTime;
            if (tau <= cruiseEnd)
            {
                return accelDist + cruiseSpeed * (tau - seg.accelTime);
            }
            double td = tau - cruiseEnd; // 감속 경과
            double cruiseDist = cruiseSpeed * seg.cruiseTime;
            return accelDist + cruiseDist + (cruiseSpeed * td - 0.5 * a * td * td);
        }
        else
        {
            double td = tau - seg.accelTime; // 감속 경과 (삼각)
            return accelDist + (seg.peakSpeed * td - 0.5 * a * td * td);
        }
    }

    private void BuildTimeline()
    {
        _segments.Clear();
        _period = 0.0;
        _built = false;

        var pts = GetValidWaypoints();
        if (pts.Count < 2 || cruiseSpeed <= 0f || acceleration <= 0f)
        {
            return;
        }

        List<int> seq = BuildIndexSequence(pts.Count);

        for (int k = 0; k < seq.Count; k++)
        {
            int fromIdx = seq[k];
            int toIdx = seq[(k + 1) % seq.Count];

            // 정지 여부/시간은 도착 노드의 WaypointNode가 결정(경유지=0, 목적지=지정 시간).
            double pause = PauseForNode(pts[toIdx]);

            _segments.Add(BuildSegment(pts[fromIdx].position, pts[toIdx].position, pause));
        }

        foreach (var seg in _segments)
        {
            _period += seg.moveTime + seg.pauseAfter;
        }

        _built = _segments.Count > 0 && _period > 0.0;
    }

    private Segment BuildSegment(Vector3 from, Vector3 to, double pauseAfter)
    {
        Segment seg = new Segment { start = from, end = to, pauseAfter = pauseAfter };
        double d = (to - from).magnitude;
        seg.distance = d;

        double a = acceleration;
        double v = cruiseSpeed;
        double accelDist = v * v / (2.0 * a); // 가속 한쪽 거리

        if (2.0 * accelDist <= d)
        {
            // 사다리꼴
            seg.accelTime = v / a;
            double cruiseDist = d - 2.0 * accelDist;
            seg.cruiseTime = cruiseDist / v;
            seg.peakSpeed = v;
            seg.moveTime = 2.0 * seg.accelTime + seg.cruiseTime;
        }
        else
        {
            // 삼각(순항 못 찍음)
            double tAccel = System.Math.Sqrt(d / a);
            seg.accelTime = tAccel;
            seg.cruiseTime = 0.0;
            seg.peakSpeed = a * tAccel;
            seg.moveTime = 2.0 * tAccel;
        }

        return seg;
    }

    private double PauseForNode(Transform wp)
    {
        WaypointNode node = wp != null ? wp.GetComponent<WaypointNode>() : null;
        if (node != null)
        {
            return node.ResolvePause(defaultPauseSeconds);
        }
        // WaypointNode 없는 WP는 목적지 + 기본 정지시간으로 취급(하위 호환).
        return Mathf.Max(0f, defaultPauseSeconds);
    }

    /// <summary>모드별 웨이포인트 인덱스 순회 시퀀스(한 주기). 세그먼트는 seq[k] → seq[(k+1)%count].</summary>
    private List<int> BuildIndexSequence(int n)
    {
        var seq = new List<int>();
        switch (mode)
        {
            case PathMode.Cycle:
                for (int i = 0; i < n; i++) seq.Add(i); // wrap(n-1→0)이 닫힌 루프 담당
                break;

            case PathMode.PingPong:
                for (int i = 0; i < n; i++) seq.Add(i);         // 0 → n-1
                for (int i = n - 2; i >= 1; i--) seq.Add(i);    // n-2 → 1 (wrap 1→0이 복귀 담당)
                break;
        }
        return seq;
    }

#if UNITY_EDITOR
    // ---- 에디터 미리보기(편집 모드에서 네트워크 플로우 없이 모션 확인) ----
    private Vector3 _editorPreviewOriginalPos;
    private bool _editorPreviewActive;

    /// <summary>미리보기 시작: platformBody 원위치를 기억한다.</summary>
    public void EditorPreviewBegin()
    {
        if (platformBody == null)
        {
            return;
        }
        _editorPreviewOriginalPos = platformBody.position;
        _editorPreviewActive = true;
    }

    /// <summary>미리보기 갱신: 현재 인스펙터 설정으로 타임라인을 다시 계산해 위치를 반영(실시간 편집 반영).</summary>
    public void EditorPreviewTick(double elapsed)
    {
        if (!_editorPreviewActive || platformBody == null)
        {
            return;
        }
        BuildTimeline();
        if (_built)
        {
            platformBody.position = Evaluate(elapsed);
        }
    }

    /// <summary>미리보기 종료: platformBody를 원위치로 복원.</summary>
    public void EditorPreviewEnd()
    {
        if (_editorPreviewActive && platformBody != null)
        {
            platformBody.position = _editorPreviewOriginalPos;
        }
        _editorPreviewActive = false;
    }
#endif

    private static bool IsViaNode(Transform wp)
    {
        WaypointNode node = wp != null ? wp.GetComponent<WaypointNode>() : null;
        return node != null && node.IsWaypoint;
    }

    private List<Transform> GetValidWaypoints()
    {
        var list = new List<Transform>();
        foreach (var w in waypoints)
        {
            if (w != null)
            {
                list.Add(w);
            }
        }
        return list;
    }

    private void OnDrawGizmosSelected()
    {
        var pts = GetValidWaypoints();
        if (pts.Count < 2)
        {
            return;
        }

        for (int i = 0; i < pts.Count; i++)
        {
            // 노드 구체: 경유지=노랑, 목적지=시안.
            Gizmos.color = IsViaNode(pts[i]) ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(pts[i].position, 0.3f);

            int next = (i + 1) % pts.Count;
            if (mode == PathMode.Cycle || i < pts.Count - 1)
            {
                // 선 색은 향하는(다음) 노드 기준: 경유지로 향하는 선=노랑, 목적지로 향하는 선=시안.
                Gizmos.color = IsViaNode(pts[next]) ? Color.yellow : Color.cyan;
                Gizmos.DrawLine(pts[i].position, pts[next].position);
            }
        }
    }
}
