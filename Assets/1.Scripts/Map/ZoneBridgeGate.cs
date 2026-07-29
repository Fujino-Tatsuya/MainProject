using System.Collections.Generic;
using UnityEngine;

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

    [Tooltip("활성 표시 링의 반지름(m). 스크린샷의 청록 링.")]
    [SerializeField, Min(0.2f)] private float ringRadius = 1.2f;

    [Tooltip("활성 표시 링 색.")]
    [SerializeField] private Color ringColor = new Color(0.45f, 0.9f, 1f, 1f);

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
        ApplyOpenProgress(0f);
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

            _rings.Add(ZoneInteractRing.Create(panel, ringRadius, ringColor));
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
