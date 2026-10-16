using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 엘리베이터 연출의 로컬 재생기 — <see cref="BossElevatorTimeline"/> 과 시작 시각을 받아 칸(cab) 높이·문 Animator 를 맞춘다. 네트워크를 모른다.
///
/// · 움직이는 건 칸(<c>PF_Prop_object_MV_bossroomenter</c>, 문 Animator 가 자식)뿐 — 레일·램프·파츠·floor_charger 는 바닥에 고정된 구조물이다.
/// · 칸 높이 = <see cref="_landedLocalY"/> + 타임라인 오프셋. 문은 컨트롤러에 전이가 없어 <see cref="Animator.Play(int,int,float)"/> 로 상태를 직접 고른다
///   (<see cref="TutorialElevatorDoor"/> 와 같은 방식).
/// · 경과 시간으로 매 프레임 다시 계산하므로 늦게 합류해도 같은 그림. 지난 단계 경계의 이벤트는 처음 적용할 때 순서대로 한꺼번에 낸다.
/// · 상승은 끝 높이 없이 계속된다. <see cref="BossElevatorPhase.Blackout"/>(완전 암전)에 닿으면 재생을 멈추고 <see cref="ResetToIdle"/> 한 뒤
///   <see cref="OnBlackout"/> 을 낸다. 화면 페이드 자체는 리그가 그리지 않는다 — 호출자가 <see cref="OnFadeOutStart"/>·
///   <see cref="BossElevatorTimeline.FadeAlphaAt"/> 로 맞춘다.
/// · 칸 대기 자리 = 프리팹에 놓인 자리(위). 높이 설정이 0 이하면 하강이 그 자리에서 시작한다(<see cref="BuildTimeline"/>).
/// · <see cref="ResetToIdle"/> = 칸을 프리팹에 놓인 자리로, 문은 대기 포즈(<see cref="IdleDoorOpen"/>).
/// · 보행 경로(<see cref="GetFrontPose"/> → <see cref="GetInsidePoint"/>)도 여기서 정한다 — 칸 안 지점 = 착지 중앙(이 오브젝트 위치)에서
///   문 앞 지점 배치를 <see cref="_insideSpread"/> 배로 줄인 자리. 서버·클라가 같은 프리팹이면 같은 거리·시간표가 나온다.
/// </summary>
[DisallowMultipleComponent]
[DataTableSheet("Map", Order = 1)]
public sealed class BossElevatorRig : MonoBehaviour
{
    const string DoorClosedState = "Closed";
    const string DoorOpenState = "Open";
    const string DoorCloseState = "Close";

    static readonly int DoorClosedHash = Animator.StringToHash(DoorClosedState);
    static readonly int DoorOpenHash = Animator.StringToHash(DoorOpenState);
    static readonly int DoorCloseHash = Animator.StringToHash(DoorCloseState);

    [Tooltip("위아래로 움직일 칸 — 문 Animator 를 자식으로 둔다")]
    [SerializeField] Transform _cab;
    [Tooltip("문 Animator (MV_BossroomEnter_Doors — Closed/Open/Close 상태)")]
    [SerializeField] Animator _doorAnimator;
    [Tooltip("문 열림/닫힘 시간이 0 이하일 때 길이를 쓰는 클립")]
    [SerializeField] AnimationClip _doorOpenClip;
    [SerializeField] AnimationClip _doorCloseClip;
    [DataTableIgnore]
    [Tooltip("문이 열리는 착지 높이(칸 localPosition.y) — 칸 밑면이 floor_charger 판에 얹히는 값")]
    [SerializeField] float _landedLocalY = 0.55f;
    [Tooltip("플레이어 순간이동 지점 — 문 앞. 슬롯(P1, P2, P3) 순서")]
    [SerializeField] Transform[] _frontPoints = Array.Empty<Transform>();
    [Tooltip("칸 안 도착 지점 간격 배율 — 문 앞 지점들의 서로 간 배치를 칸 중앙 기준으로 이 배만큼 줄여 세운다. 0 = 전원 정중앙")]
    [SerializeField, Min(0f)] float _insideSpread = 0.6f;
    [SerializeField] BossElevatorTimingSettings _timing = BossElevatorTimingSettings.Default;

    BossElevatorTimeline _timeline;
    double _startTime;
    Func<double> _clock;
    Vector3 _idleLocalPosition;
    bool _idleCaptured;
    bool _playing;
    BossElevatorPhase _lastPhase;
    bool _hasLastPhase;
    BossElevatorDoorState _appliedDoor;
    bool _hasAppliedDoor;

    /// <summary>문이 닫히고 상승이 시작되는 순간.</summary>
    public event Action OnAscendStart;
    /// <summary>화면 페이드 아웃이 시작되는 순간 — 길이는 <see cref="BossElevatorTimeline.FadeOutDuration"/>.</summary>
    public event Action OnFadeOutStart;
    /// <summary>완전 암전 순간 — 재생은 멈췄고 칸·문은 이미 대기 자리로 돌아가 있다.</summary>
    public event Action OnBlackout;

    public IReadOnlyList<Transform> FrontPoints => _frontPoints;
    public bool IsPlaying => _playing;
    public BossElevatorTimeline Timeline => _timeline;
    /// <summary>움직이는 칸 — 상승 중 카메라가 따라간다.</summary>
    public Transform Cab => _cab;
    /// <summary>착지 중앙(이 오브젝트) — 포커스부터 상승 전까지 카메라가 본다. 칸은 위에서 내려오므로 칸 대신 이 자리를 본다.</summary>
    public Transform FocusPoint => transform;

    /// <summary>
    /// 대기(재생 밖) 문 포즈가 열림인가. 튜토리얼 G10(<see cref="TutorialElevatorDoor"/>)이 켠다 —
    /// 재생 중엔 리그가 문을 쥐고, <see cref="ResetToIdle"/> 에서 이 값대로 Open 끝 프레임/Closed 로 돌아간다.
    /// </summary>
    public bool IdleDoorOpen { get; set; }

    /// <summary>씬에 있는 리그(보스 진입 존은 BossRoom 존 하나). 존이 각 피어에서 맵 생성 때 스폰되므로 없을 수 있다.</summary>
    public static BossElevatorRig FindActive() => FindFirstObjectByType<BossElevatorRig>();

    void Awake()
    {
        CaptureIdle();
    }

    void Update()
    {
        if (!_playing) return;
        Apply(_clock() - _startTime);
    }

    /// <summary>
    /// 이 리그의 설정·문 클립 길이·보행 거리로 시간표를 만든다. 서버와 클라가 같은 프리팹·같은 인원이면 같은 시간표가 나온다.
    /// 높이 설정이 0 이하면 칸 대기 높이 − 착지 높이 — 하강이 칸이 놓인 자리에서 시작한다.
    /// </summary>
    /// <param name="walkerCount">걸어 들어가는 사람 수(참가자 수).</param>
    public BossElevatorTimeline BuildTimeline(int walkerCount)
    {
        CaptureIdle();
        float idleAboveLanded = _idleCaptured ? _idleLocalPosition.y - _landedLocalY : 0f;
        return new BossElevatorTimeline(
            _timing.WithResolvedHeight(idleAboveLanded),
            _doorOpenClip != null ? _doorOpenClip.length : 0f,
            _doorCloseClip != null ? _doorCloseClip.length : 0f,
            walkerCount,
            MaxWalkDistance());
    }

    /// <summary>슬롯의 문 앞 지점(지점보다 사람이 많으면 돌려 쓴다). 지점이 비어 있으면 false.</summary>
    public bool GetFrontPose(int slot, out Vector3 position, out Quaternion rotation)
    {
        Transform point = _frontPoints.Length > 0 ? _frontPoints[Mathf.Max(0, slot) % _frontPoints.Length] : null;
        if (point == null)
        {
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        position = point.position;
        rotation = point.rotation;
        return true;
    }

    /// <summary>
    /// 슬롯의 칸 안 도착 지점(월드). 착지 중앙에서 (문 앞 지점 − 첫 지점) × <see cref="_insideSpread"/> 만큼 — 문 앞에 선 줄 모양 그대로 줄여 들어간다.
    /// 높이는 문 앞 지점 높이(평지 보행).
    /// </summary>
    public Vector3 GetInsidePoint(int slot)
    {
        Vector3 center = transform.position;
        if (!GetFrontPose(slot, out Vector3 front, out _) || !GetFrontPose(0, out Vector3 first, out _))
            return center;

        Vector3 inside = center + (front - first) * _insideSpread;
        inside.y = front.y;
        return inside;
    }

    /// <summary>문 앞 지점 → 칸 안 지점 수평 거리 중 가장 먼 것(m) — 시간표의 보행 시간 기준.</summary>
    public float MaxWalkDistance()
    {
        float max = 0f;
        for (int slot = 0; slot < _frontPoints.Length; slot++)
        {
            if (!GetFrontPose(slot, out Vector3 front, out _)) continue;
            Vector3 d = GetInsidePoint(slot) - front;
            d.y = 0f;
            max = Mathf.Max(max, d.magnitude);
        }
        return max;
    }

    /// <summary>
    /// <paramref name="startTime"/> 부터 재생한다. <paramref name="clock"/> 은 시작 시각과 같은 시간 도메인(null = <see cref="Time.timeAsDouble"/>).
    /// 이미 지난 시작 시각이면 그 경과 시점부터 이어 그린다.
    /// </summary>
    public void Play(BossElevatorTimeline timeline, double startTime, Func<double> clock = null)
    {
        if (timeline == null) throw new ArgumentNullException(nameof(timeline));
        CaptureIdle();
        _timeline = timeline;
        _startTime = startTime;
        _clock = clock ?? (() => Time.timeAsDouble);
        _hasLastPhase = false;
        _hasAppliedDoor = false;
        _playing = true;
        Apply(_clock() - _startTime);
    }

    /// <summary>재생을 멈추고 칸을 프리팹 자리로, 문을 대기 포즈(<see cref="IdleDoorOpen"/> — Open 끝 프레임 또는 Closed)로 되돌린다. 이벤트는 내지 않는다.</summary>
    public void ResetToIdle()
    {
        CaptureIdle();
        _playing = false;
        _hasLastPhase = false;
        _hasAppliedDoor = false;
        if (_cab != null) _cab.localPosition = _idleLocalPosition;
        PlayDoor(IdleDoorOpen ? DoorOpenHash : DoorClosedHash, 1f);
    }

    void CaptureIdle()
    {
        if (_idleCaptured || _cab == null) return;
        _idleLocalPosition = _cab.localPosition;
        _idleCaptured = true;
    }

    void Apply(double elapsed)
    {
        BossElevatorPhase phase = _timeline.PhaseAt(elapsed);
        BossElevatorPhase from = _hasLastPhase ? _lastPhase : BossElevatorPhase.FrontFadeOut;

        if (phase == BossElevatorPhase.Blackout)
        {
            ResetToIdle();
        }
        else
        {
            ApplyPose(elapsed);
            _lastPhase = phase;
            _hasLastPhase = true;
        }

        // 이번 프레임에 넘은 경계마다 한 번씩(늦은 합류·긴 프레임이면 여러 개를 순서대로).
        if (from < BossElevatorPhase.Ascending && phase >= BossElevatorPhase.Ascending) OnAscendStart?.Invoke();
        if (from < BossElevatorPhase.FadingOut && phase >= BossElevatorPhase.FadingOut) OnFadeOutStart?.Invoke();
        if (from < BossElevatorPhase.Blackout && phase >= BossElevatorPhase.Blackout) OnBlackout?.Invoke();
    }

    void ApplyPose(double elapsed)
    {
        if (_cab != null)
        {
            Vector3 p = _idleLocalPosition;
            p.y = _landedLocalY + _timeline.HeightAt(elapsed);
            _cab.localPosition = p;
        }

        BossElevatorDoorState door = _timeline.DoorPoseAt(elapsed, out float normalized);
        if (!_hasAppliedDoor || door != _appliedDoor)
        {
            // 상태가 바뀔 때만 Play — 그 뒤는 Animator 가 클립을 이어 재생한다. 늦은 합류는 정규화 시간으로 중간부터.
            switch (door)
            {
                case BossElevatorDoorState.Opening: PlayDoor(DoorOpenHash, normalized); break;
                case BossElevatorDoorState.Open: PlayDoor(DoorOpenHash, 1f); break;
                case BossElevatorDoorState.Closing: PlayDoor(DoorCloseHash, normalized); break;
                default: PlayDoor(DoorClosedHash, 1f); break;
            }
            _appliedDoor = door;
            _hasAppliedDoor = true;
        }
    }

    void PlayDoor(int stateHash, float normalizedTime)
    {
        if (_doorAnimator == null || !_doorAnimator.isActiveAndEnabled) return;
        _doorAnimator.Play(stateHash, 0, normalizedTime);
    }
}
