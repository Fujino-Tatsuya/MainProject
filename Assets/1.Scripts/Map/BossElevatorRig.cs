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
/// · <see cref="ResetToIdle"/> = 칸을 프리팹에 놓인 자리로, 문 Closed.
/// </summary>
[DisallowMultipleComponent]
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
    [Tooltip("문이 열리는 착지 높이(칸 localPosition.y) — 칸 밑면이 floor_charger 판에 얹히는 값")]
    [SerializeField] float _landedLocalY = 0.55f;
    [Tooltip("플레이어 순간이동 지점 — 문 앞, 엘리베이터를 바라본다")]
    [SerializeField] Transform[] _frontPoints = Array.Empty<Transform>();
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

    /// <summary>플레이어가 사라지는 순간(문 열림 끝).</summary>
    public event Action OnVanish;
    /// <summary>문이 닫히고 상승이 시작되는 순간.</summary>
    public event Action OnAscendStart;
    /// <summary>상승이 끝난 순간 — 재생은 멈추고 칸은 위에 남는다.</summary>
    public event Action OnDone;

    public IReadOnlyList<Transform> FrontPoints => _frontPoints;
    public bool IsPlaying => _playing;
    public BossElevatorTimeline Timeline => _timeline;

    void Awake()
    {
        CaptureIdle();
    }

    void Update()
    {
        if (!_playing) return;
        Apply(_clock() - _startTime);
    }

    /// <summary>이 리그의 설정·문 클립 길이로 시간표를 만든다. 서버와 클라가 같은 프리팹이면 같은 시간표가 나온다.</summary>
    public BossElevatorTimeline BuildTimeline()
    {
        return new BossElevatorTimeline(
            _timing,
            _doorOpenClip != null ? _doorOpenClip.length : 0f,
            _doorCloseClip != null ? _doorCloseClip.length : 0f);
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

    /// <summary>재생을 멈추고 칸을 프리팹 자리로, 문을 Closed 로 되돌린다. 이벤트는 내지 않는다.</summary>
    public void ResetToIdle()
    {
        CaptureIdle();
        _playing = false;
        _hasLastPhase = false;
        _hasAppliedDoor = false;
        if (_cab != null) _cab.localPosition = _idleLocalPosition;
        PlayDoor(DoorClosedHash, 1f);
    }

    void CaptureIdle()
    {
        if (_idleCaptured || _cab == null) return;
        _idleLocalPosition = _cab.localPosition;
        _idleCaptured = true;
    }

    void Apply(double elapsed)
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

        BossElevatorPhase phase = _timeline.PhaseAt(elapsed);
        BossElevatorPhase from = _hasLastPhase ? _lastPhase : BossElevatorPhase.TeleportFront;
        _lastPhase = phase;
        _hasLastPhase = true;
        if (phase == BossElevatorPhase.Done) _playing = false;

        // 이번 프레임에 넘은 경계마다 한 번씩(늦은 합류·긴 프레임이면 여러 개를 순서대로).
        if (from < BossElevatorPhase.Vanished && phase >= BossElevatorPhase.Vanished) OnVanish?.Invoke();
        if (from < BossElevatorPhase.Ascending && phase >= BossElevatorPhase.Ascending) OnAscendStart?.Invoke();
        if (from < BossElevatorPhase.Done && phase >= BossElevatorPhase.Done) OnDone?.Invoke();
    }

    void PlayDoor(int stateHash, float normalizedTime)
    {
        if (_doorAnimator == null || !_doorAnimator.isActiveAndEnabled) return;
        _doorAnimator.Play(stateHash, 0, normalizedTime);
    }
}
