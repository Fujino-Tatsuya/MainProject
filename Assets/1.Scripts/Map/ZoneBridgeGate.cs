using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 존 프리팹이 <b>스스로 들고 다니는</b> 다리 개통 장치 저작 데이터.
/// (대상 = <c>ZoneL_typeB</c> — 네 모서리 <c>Env_panel</c> 4개 + 중앙을 잇는 다리 4조각)
///
/// 규약: 존 프리팹은 <b>비네트워크</b>다(<see cref="MapContentSpawner"/> — 양쪽에서 로컬
/// Instantiate되고 NGO 복제를 타지 않는다). 그래서 이 컴포넌트는 <b>순수 저작 데이터와 로컬
/// 연출</b>만 담당하고, 상태 복제·판정은 씬 상주 <see cref="ZoneBridgeGateManager"/>가 맡는다.
///
/// <b>좌표는 전부 로컬이다.</b> 존은 셔플로 슬롯이 바뀌어 매 실행 월드 위치가 달라진다 —
/// 절대좌표를 넣으면 그 즉시 어긋난다(보스 아레나에서 이미 밟은 함정).
///
/// 다리 목표 위치는 <b>저작으로 받는다</b>. 메시 길이를 코드로 추측해 중앙까지 밀면 반드시
/// 어긋나므로, 미저작이면 움직이지 않고 경고한다(조용히 틀린 위치로 가는 것보다 낫다).
/// 저작 도구 = <c>Tools/Map/Authoring/Wire Zone Bridge Gate (ZoneL_typeB)</c>.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZoneBridgeGate : MonoBehaviour
{
    /// <summary>다리 한 조각의 닫힘↔열림 로컬 위치. 둘 다 저작값이다.</summary>
    [System.Serializable]
    public struct Segment
    {
        [Tooltip("움직일 다리 조각(존 프리팹 내부 Transform).")]
        public Transform Target;

        [Tooltip("평상시(끊긴 상태) 로컬 위치. 저작 도구가 현재 위치로 채운다.")]
        public Vector3 ClosedLocalPosition;

        [Tooltip("개통 후(연결된 상태) 로컬 위치. 씬에서 맞춘 뒤 Record로 저장한다.")]
        public Vector3 OpenLocalPosition;

        [Tooltip("false면 OpenLocalPosition이 아직 저작되지 않은 것 — 움직이지 않는다.")]
        public bool HasOpenPosition;
    }

    [Header("상호작용 패널 (F키 대상)")]
    [Tooltip("네 모서리 Env_panel. 순서가 곧 패널 인덱스이며 복제 키로 쓰이므로 바꾸지 말 것.")]
    [SerializeField] private List<Transform> panels = new List<Transform>();

    [Header("다리 조각")]
    [SerializeField] private List<Segment> segments = new List<Segment>();

    [Header("연출")]
    [Tooltip("다리가 닫힘→열림으로 이동하는 시간(초).")]
    [SerializeField, Min(0.05f)] private float openDuration = 1.5f;

    [Tooltip("상호작용 가능 거리(m). 플레이어가 패널에서 이 안에 있어야 F가 먹는다.")]
    [SerializeField, Min(0.5f)] private float interactRadius = 2.5f;

    [Header("활성 표시 링")]
    [Tooltip("링 반지름(m).")]
    [SerializeField, Min(0.2f)] private float ringRadius = 1.2f;

    [Tooltip("링 색.")]
    [SerializeField] private Color ringColor = new Color(0.45f, 0.9f, 1f, 1f);

    [Tooltip("링 선 굵기(m).")]
    [SerializeField, Min(0.01f)] private float ringWidth = 0.12f;

    [Tooltip("바닥에서 띄우는 높이(m). 너무 작으면 바닥과 Z-fighting으로 지글거린다.")]
    [SerializeField, Min(0f)] private float ringGroundLift = 0.05f;

    [Tooltip("비우면 절차 생성 원(LineRenderer)을 쓴다. 채우면 이 프리팹을 대신 쓴다 — " +
             "전용 아트로 갈아끼우는 경로. 이 경우 위 반지름·색·굵기는 무시되고 프리팹이 스스로 정한다.")]
    [SerializeField] private GameObject ringPrefabOverride;

    public IReadOnlyList<Transform> Panels => panels;
    public int PanelCount => panels != null ? panels.Count : 0;
    public float OpenDuration => openDuration;
    public float InteractRadius => interactRadius;

    /// <summary>이 존이 놓인 슬롯 ID. 스폰 시 <see cref="GeneratedZoneIdentity"/>에서 받아 채운다.</summary>
    public int SlotID { get; private set; } = -1;

    readonly List<ZoneInteractRing> _rings = new List<ZoneInteractRing>();

    float _openProgress;   // 0 = 닫힘, 1 = 열림
    bool _ringsBuilt;

    public void SetSlotID(int slotID) => SlotID = slotID;

    /// <summary>현재 개통 진행도(0~1). 매니저가 복제값으로 밀어 넣는다.</summary>
    public float OpenProgress => _openProgress;

    void Awake()
    {
        BuildRings();
        BuildGapObstacle();
        ApplyOpenProgress(0f);
    }

    // ── NavMesh: 미리 굽고 카브로 막는다 ──────────────────────────────────
    //
    // 런타임에 서피스를 다시 굽는 건 답이 아니다 — 이 프로젝트의 NavMeshSurface는 맵 전체
    // (원점~x≈500)를 덮어서 재베이크가 수백 ms 단위로 멈춘다. 서버에서 그 멈춤은 전원이 겪는다.
    //
    // 그래서 반대로 한다: **베이크 시점에 다리를 연결된 상태로 두고 굽고**(BakeOpenScope),
    // 평상시에는 그 위를 NavMeshObstacle로 **카브해 막는다**. 개통되면 카브를 끄면 끝 —
    // 카브 갱신은 부분 갱신이라 값이 싸고, 재베이크가 0회다.
    //
    // 카브가 필요한 이유: 열린 상태로 구워두면 다리가 물러나 있는 동안에도 NavMesh는 "걸을 수 있다"고
    // 말한다. 플레이어는 물리라 떨어지지만 NavMeshAgent(몬스터)는 허공을 건너간다.

    const float CarveVerticalPadding = 2f;

    readonly List<NavMeshObstacle> _gapObstacles = new List<NavMeshObstacle>();

    /// <summary>
    /// 열림 위치를 점유할 구간을 <b>조각마다 따로</b> 카브한다.
    ///
    /// 🔴 조각 전체를 한 바운즈로 합치면 안 된다. 다리는 존 <b>양쪽</b>에 하나씩 있어서 합친 박스가
    /// 중앙 플랫폼을 가로지르는 20m 넘는 띠가 되고, 평상시에 그 띠만큼 <b>중앙 바닥의 NavMesh 가
    /// 사라진다</b>. 플레이어는 물리라 멀쩡해서 눈치채기 어렵고 몬스터만 중앙을 못 지나간다.
    /// (2026-08-17 확인 — 구 저작은 x=±10 이라 같은 모양이었다.)
    ///
    /// 움직이지 않는 조각은 대상이 아니다 — 제자리에 있으니 베이크 결과가 이미 맞다.
    /// </summary>
    void BuildGapObstacle()
    {
        if (_gapObstacles.Count > 0 || segments == null || segments.Count == 0) return;

        foreach (Segment s in segments)
        {
            if (s.Target == null || !s.HasOpenPosition) continue;
            if (s.OpenLocalPosition == s.ClosedLocalPosition) continue;
            if (!TryGetOpenSpanBounds(s, out Bounds localBounds)) continue;

            var go = new GameObject($"BridgeGapCarve_{s.Target.name}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localBounds.center;
            go.transform.localRotation = Quaternion.identity;

            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;

            // 데크가 얇아 바운즈 그대로 쓰면 카브 박스가 NavMesh 표면을 스치지 못하고 빗나간다.
            // 위아래로 넉넉히 부풀려 확실히 파낸다(가로·세로는 다리 폭 그대로 — 넓히면 옆 바닥을 깎는다).
            Vector3 size = localBounds.size;
            size.y = Mathf.Max(size.y, 1f) + CarveVerticalPadding;
            obstacle.size = size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;   // 켜고 끄는 순간 바로 반영돼야 한다

            _gapObstacles.Add(obstacle);
        }
    }

    /// <summary>조각 하나가 열렸을 때 점유하는 로컬 바운즈. 카브 박스 크기의 근거.</summary>
    bool TryGetOpenSpanBounds(Segment s, out Bounds localBounds)
    {
        localBounds = default;

        Renderer[] renderers = s.Target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;

        // 조각을 열림 위치로 옮겨 놓고 바운즈를 읽은 뒤 되돌린다 — 열림 상태의 실제 점유 공간이
        // 필요하므로 닫힘 위치의 바운즈를 평행이동하는 것으로는 회전·스케일을 못 맞춘다.
        Vector3 saved = s.Target.localPosition;
        s.Target.localPosition = s.OpenLocalPosition;

        bool any = false;
        foreach (Renderer r in renderers)
        {
            Bounds w = r.bounds;
            Vector3 c = transform.InverseTransformPoint(w.center);
            Vector3 e = transform.InverseTransformVector(w.extents);
            var lb = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);

            if (!any) { localBounds = lb; any = true; }
            else localBounds.Encapsulate(lb);
        }

        s.Target.localPosition = saved;
        return any;
    }

    /// <summary>
    /// NavMesh를 굽는 동안만 다리를 연결 상태로 만드는 스코프. <see cref="MapNavMeshBaker"/>가 쓴다.
    /// 이 함수 안에서 왕복이 끝나므로 "굽기 전에 열고 굽고 나서 닫는" 순서가 보장된다
    /// (OnGenerated 구독자 사이의 호출 순서에 의존하지 않는다).
    /// </summary>
    public sealed class BakeOpenScope : System.IDisposable
    {
        readonly List<(ZoneBridgeGate gate, float progress)> _saved = new List<(ZoneBridgeGate, float)>();

        public static BakeOpenScope Begin()
        {
            var scope = new BakeOpenScope();

            foreach (ZoneBridgeGate gate in FindObjectsByType<ZoneBridgeGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (gate == null) continue;

                scope._saved.Add((gate, gate.OpenProgress));
                gate.ApplyOpenProgress(1f);           // 연결된 상태로 굽는다
                gate.SetGapCarveEnabled(false);       // 카브가 켜져 있으면 구운 결과가 다시 파인다
            }

            return scope;
        }

        public int GateCount => _saved.Count;

        public void Dispose()
        {
            foreach ((ZoneBridgeGate gate, float progress) in _saved)
            {
                if (gate == null) continue;
                gate.ApplyOpenProgress(progress);     // 원래(대개 끊긴) 상태로 되돌린다
            }
        }
    }

    void SetGapCarveEnabled(bool enabled)
    {
        foreach (NavMeshObstacle o in _gapObstacles)
            if (o != null) o.enabled = enabled;
    }

    /// <summary>
    /// 패널 위치에 링 표시를 만든다(전 피어 로컬 연출). 아트가 따로 없으므로 <see cref="LineRenderer"/>로
    /// 원을 그린다 — 존 프리팹에 의존물을 추가하지 않아도 되고, 색·반지름을 인스펙터로 조절할 수 있다.
    /// </summary>
    void BuildRings()
    {
        if (_ringsBuilt) return;
        _ringsBuilt = true;

        for (int i = 0; i < PanelCount; i++)
        {
            Transform panel = panels[i];
            if (panel == null)
            {
                _rings.Add(null);
                continue;
            }

            _rings.Add(ZoneInteractRing.Create(panel, ringRadius, ringColor, ringWidth,
                                               ringGroundLift, ringPrefabOverride));
        }
    }

    /// <summary>패널 i의 활성 표시를 켜고 끈다(로컬 연출 — 판정과 무관).</summary>
    public void SetPanelActivatedVisual(int index, bool activated)
    {
        if (index < 0 || index >= _rings.Count) return;

        ZoneInteractRing ring = _rings[index];
        if (ring != null) ring.SetVisible(activated);
    }

    /// <summary>패널 i의 월드 위치. 거리 판정에 쓴다.</summary>
    public bool TryGetPanelPosition(int index, out Vector3 position)
    {
        if (index >= 0 && index < PanelCount && panels[index] != null)
        {
            position = panels[index].position;
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>
    /// 개통 진행도를 적용한다. 서버·클라 모두 같은 값으로 호출되어 같은 결과가 나온다
    /// (로컬 보간 — 다리를 NetworkTransform으로 복제하지 않는 이유는 존이 비네트워크이기 때문).
    /// </summary>
    public void ApplyOpenProgress(float progress)
    {
        _openProgress = Mathf.Clamp01(progress);

        if (segments == null) return;

        foreach (Segment s in segments)
        {
            if (s.Target == null) continue;

            // 미저작 조각은 움직이지 않는다 — 추측한 목표로 밀면 통로와 어긋난다.
            if (!s.HasOpenPosition)
            {
                s.Target.localPosition = s.ClosedLocalPosition;
                continue;
            }

            s.Target.localPosition = Vector3.Lerp(s.ClosedLocalPosition, s.OpenLocalPosition, _openProgress);
        }

        // 완전히 열렸을 때만 카브를 뺀다. 이동 중에 미리 빼면 다리가 도착하기 전에 몬스터가 허공으로 들어간다.
        SetGapCarveEnabled(_openProgress < 1f);
    }

    /// <summary>저작이 빠진 조각 수. 스폰 시 매니저가 경고에 쓴다.</summary>
    public int CountUnauthoredSegments()
    {
        if (segments == null) return 0;

        int n = 0;
        foreach (Segment s in segments)
            if (s.Target != null && !s.HasOpenPosition) n++;
        return n;
    }

    public int SegmentCount => segments != null ? segments.Count : 0;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = ringColor;
        for (int i = 0; i < PanelCount; i++)
            if (panels[i] != null) Gizmos.DrawWireSphere(panels[i].position, interactRadius);

        if (segments == null) return;
        foreach (Segment s in segments)
        {
            if (s.Target == null || !s.HasOpenPosition) continue;

            Vector3 from = transform.TransformPoint(s.ClosedLocalPosition);
            Vector3 to = transform.TransformPoint(s.OpenLocalPosition);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireCube(to, Vector3.one * 0.5f);
        }
    }
}
