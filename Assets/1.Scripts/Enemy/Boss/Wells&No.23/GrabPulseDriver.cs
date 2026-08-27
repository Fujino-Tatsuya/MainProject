using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GrabAttack 동안 보스의 팔을 훑고 지나가는 전기 펄스를 반복 재생한다.
///
/// <b>왜 StateMachineBehaviour가 아닌가</b>: 처음엔 GrabAttack 서브 스테이트머신에 SMB를 붙여
/// OnStateMachineEnter/Exit로 켜고 끄려 했지만, 이 컨트롤러는 SSM을 통과하지 않고 안쪽 상태를
/// 직접 겨냥하도록 배선돼 있다(진입 트랜지션이 m_DstState=Grab, 이탈은 m_IsExit=0으로 Idle/Walking 직행).
/// 그러면 두 콜백 모두 발화하지 않는다. 그래프를 재배선하는 대신 여기서 애니메이터 상태를 직접 읽는다 —
/// 어차피 상태별 튜닝 값을 매 프레임 조회하고 있었으므로 조회 결과가 "재생 여부"까지 겸하게 했다.
///
/// <b>왜 앵커가 여러 개인가</b>: <see cref="EffectManager.PlayLooping"/>은 Transform 하나를 추종한다.
/// 펄스 겹침을 허용하므로(펄스 A가 손 근처일 때 펄스 B가 어깨에서 출발) 앵커를 공유할 수 없다 —
/// 하나를 돌려쓰면 살아 있는 펄스 전부가 같은 지점으로 끌려온다. 그래서 앵커도 풀로 돌린다.
///
/// <b>서버 가드가 없는 이유</b>: 이건 순수 연출이다. 애니메이터가 NetworkAnimator로 동기화되므로
/// 각 클라이언트가 같은 상태를 보고 알아서 재생한다. IsServer를 걸면 호스트에서만 보인다.
/// </summary>
public class GrabPulseDriver : MonoBehaviour
{
    [Header("팔 본 — 인스펙터에서 직접 물릴 것")]
    [Tooltip("펄스가 출발하는 어깨 본 (예: shoulder.r)")]
    [SerializeField] Transform shoulder;

    [Tooltip("경로 중간점이 되는 팔꿈치 본 (예: forearm.r)")]
    [SerializeField] Transform forearm;

    [Tooltip("펄스가 도착해 소멸하는 손 본 (예: hand.r)")]
    [SerializeField] Transform hand;

    [Header("이펙트")]
    [SerializeField] EffectEntry entry;

    [Tooltip("어느 애니메이터 상태에서 얼마나 자주 펄스를 낼지. 여기 없는 상태에서는 재생되지 않는다")]
    [SerializeField] GrabPulseProfile profile;

    [Tooltip("프리팹 크기에 곱해지는 배율")]
    [Min(0.01f)] [SerializeField] float scale = 1f;

    [Header("앵커 풀")]
    [Tooltip("동시에 살아 있을 수 있는 펄스 수. ceil((travelTime + outroDuration) / interval) + 1 정도면 충분하다")]
    [Min(1)] [SerializeField] int anchorPoolSize = 4;

    [Header("회전")]
    [Tooltip("진행 방향으로 앵커를 돌릴 때의 감쇠 계수. 팔꿈치에서 구간이 바뀌며 방향이 꺾이는 걸 완충한다. 0이면 완충 없이 즉시 스냅")]
    [Min(0f)] [SerializeField] float rotationSmoothing = 18f;

    [Header("진단")]
    [Tooltip("펄스 시작·정지 시점을 콘솔에 남긴다. 연출이 안 보일 때 상태 판정까지 왔는지 확인용")]
    [SerializeField] bool logPulses;

    /// <summary>진행 중인 펄스 하나. 앵커와 이펙트 핸들이 한 몸으로 움직인다.</summary>
    private class Pulse
    {
        public Transform anchor;
        public EffectHandle handle;
        public float elapsed;
        public float travelTime;
        public bool active;
    }

    private readonly List<Pulse> _pulses = new List<Pulse>();
    private Animator _animator;
    private bool _ready;
    private bool _wasInPattern;
    private float _sinceLastPulse;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();

        Transform poolRoot = new GameObject("GrabPulseAnchors").transform;
        poolRoot.SetParent(transform, false);

        for (int i = 0; i < anchorPoolSize; i++)
        {
            Transform anchor = new GameObject($"GrabPulseAnchor_{i}").transform;
            anchor.SetParent(poolRoot, false);
            _pulses.Add(new Pulse { anchor = anchor });
        }
    }

    // EffectManager는 자기 Awake에서 Instance를 세운다. 순서에 기대지 않도록 Start에서 검사한다.
    private void Start() => _ready = Validate();

    private void Update()
    {
        if (!_ready) return;

        float dt = Time.deltaTime;
        GrabPulseProfile.StateSetting setting = ActiveSetting();
        bool inPattern = setting != null;

        if (inPattern)
        {
            // 진입 프레임에 첫 펄스가 바로 나가도록 타이머를 채워 둔다.
            if (!_wasInPattern)
            {
                _sinceLastPulse = float.MaxValue;
                if (logPulses) Edit.Log("[No.23] GrabPulse 시작", this);
            }

            if (setting.emit) TrySpawn(dt, setting);
        }
        else if (_wasInPattern)
        {
            // 패턴을 벗어났다 → 새 펄스를 멈추고 남은 펄스는 outro를 태우며 자연 소멸시킨다.
            ReleaseAll(false);
            if (logPulses) Edit.Log("[No.23] GrabPulse 정지", this);
        }

        _wasInPattern = inPattern;

        Advance(dt);
    }

    /// <summary>
    /// 지금 재생해야 하는 상태인가. 프로파일 목록에 없으면 null.
    ///
    /// 전이 중에는 GetCurrentAnimatorStateInfo가 <b>출발 상태</b>를 계속 돌려준다 —
    /// 그대로 두면 Throw에서 Idle로 빠지는 블렌드 내내 펄스가 계속 나간다. 다음 상태도 함께 본다.
    /// </summary>
    private GrabPulseProfile.StateSetting ActiveSetting()
    {
        if (profile == null || _animator == null) return null;

        GrabPulseProfile.StateSetting current = profile.Resolve(_animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
        if (current == null) return null;

        if (_animator.IsInTransition(0) &&
            profile.Resolve(_animator.GetNextAnimatorStateInfo(0).shortNameHash) == null)
        {
            return null;   // 이미 패턴 밖으로 나가는 중
        }

        return current;
    }

    private void TrySpawn(float dt, GrabPulseProfile.StateSetting setting)
    {
        _sinceLastPulse += dt;
        if (_sinceLastPulse < setting.interval) return;

        // 스폰 성패와 무관하게 여기서 리셋한다. 실패 시에만 남겨두면 다음 프레임에 또 시도해
        // 경고가 프레임마다 쏟아진다 — 알림 주기를 interval에 묶어 둔다.
        _sinceLastPulse = 0f;

        Pulse pulse = FreePulse();
        if (pulse == null)
        {
            // 풀이 비었다 = 설정상 동시 펄스 수가 앵커 수를 넘었다. 값을 고쳐야 하므로 조용히 넘기지 않는다.
            Edit.LogWarning($"[No.23] GrabPulse 앵커 풀({anchorPoolSize}) 고갈. interval을 늘리거나 anchorPoolSize를 올릴 것.", this);
            return;
        }

        pulse.elapsed = 0f;
        pulse.travelTime = setting.travelTime;
        pulse.active = true;

        // 출발 프레임에 앵커를 제자리에 앉혀 둔다. 안 그러면 지난 펄스가 남긴 위치에서 한 프레임 튄다.
        Place(pulse, 0f, snapRotation: true);

        pulse.handle = EffectManager.Instance.PlayLooping(entry, pulse.anchor, Vector3.zero, scale);
    }

    private void Advance(float dt)
    {
        for (int i = 0; i < _pulses.Count; i++)
        {
            Pulse pulse = _pulses[i];
            if (!pulse.active) continue;

            pulse.elapsed += dt;
            float t = pulse.travelTime > 0f ? pulse.elapsed / pulse.travelTime : 1f;

            if (t >= 1f)
            {
                Place(pulse, 1f, snapRotation: false);
                Retire(pulse, false);   // 손에 닿았다 → outro를 태우며 자연 소멸
                continue;
            }

            Place(pulse, t, snapRotation: false);
        }
    }

    /// <summary>
    /// 어깨 → 팔꿈치 → 손 2구간을 <b>길이 비례</b>로 보간해 앵커를 앉힌다.
    /// 구간을 절반씩 나누면 위팔과 아래팔의 길이 차만큼 팔꿈치에서 속도가 튄다.
    /// 회전은 진행 방향(구간 탄젠트)을 향하되, 팔꿈치에서의 꺾임을 <see cref="rotationSmoothing"/>으로 완충한다.
    /// </summary>
    private void Place(Pulse pulse, float t, bool snapRotation)
    {
        Vector3 a = shoulder.position;
        Vector3 b = forearm.position;
        Vector3 c = hand.position;

        float upper = Vector3.Distance(a, b);
        float lower = Vector3.Distance(b, c);
        float total = upper + lower;

        Vector3 position;
        Vector3 tangent;

        if (total <= Mathf.Epsilon)
        {
            position = c;
            tangent = transform.forward;
        }
        else
        {
            float travelled = t * total;
            if (travelled <= upper)
            {
                position = Vector3.Lerp(a, b, upper > Mathf.Epsilon ? travelled / upper : 1f);
                tangent = b - a;
            }
            else
            {
                position = Vector3.Lerp(b, c, lower > Mathf.Epsilon ? (travelled - upper) / lower : 1f);
                tangent = c - b;
            }
        }

        Quaternion target = tangent.sqrMagnitude > Mathf.Epsilon
            ? Quaternion.LookRotation(tangent.normalized, Vector3.up)
            : pulse.anchor.rotation;

        Quaternion rotation = (snapRotation || rotationSmoothing <= 0f)
            ? target
            // 프레임레이트에 독립인 감쇠. Slerp에 dt를 그대로 넣으면 프레임이 빠를수록 덜 따라간다.
            : Quaternion.Slerp(pulse.anchor.rotation, target, 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime));

        pulse.anchor.SetPositionAndRotation(position, rotation);
    }

    private Pulse FreePulse()
    {
        for (int i = 0; i < _pulses.Count; i++)
        {
            if (!_pulses[i].active) return _pulses[i];
        }
        return null;
    }

    private void Retire(Pulse pulse, bool immediate)
    {
        pulse.active = false;

        if (!pulse.handle.IsSet) return;

        if (immediate) EffectManager.Instance.ReleaseImmediate(pulse.handle);
        else EffectManager.Instance.Release(pulse.handle);

        pulse.handle = EffectHandle.None;
    }

    private void ReleaseAll(bool immediate)
    {
        if (EffectManager.Instance == null) return;

        for (int i = 0; i < _pulses.Count; i++)
        {
            if (_pulses[i].active) Retire(_pulses[i], immediate);
        }
    }

    /// <summary>
    /// 안전망. 그로기·사망·despawn으로 컴포넌트가 꺼지거나 오브젝트가 파괴되면 Update가 멈춰
    /// 정지 경로를 타지 못한다 — 그대로 두면 핸들이 새서 풀이 고갈된다.
    /// 패턴이 꺾인 자리에 전기가 남으면 "끊겼다"는 피드백이 죽으므로 여기서는 즉시 회수한다.
    /// </summary>
    private void OnDisable()
    {
        _wasInPattern = false;
        ReleaseAll(true);
    }

    private bool Validate()
    {
        if (shoulder == null || forearm == null || hand == null)
        {
            Edit.LogError("[No.23] GrabPulseDriver에 팔 본(shoulder/forearm/hand)이 연결되어 있지 않습니다.", this);
            return false;
        }

        if (entry == null)
        {
            Edit.LogError("[No.23] GrabPulseDriver에 EffectEntry가 연결되어 있지 않습니다.", this);
            return false;
        }

        if (profile == null)
        {
            Edit.LogError("[No.23] GrabPulseDriver에 GrabPulseProfile이 연결되어 있지 않습니다.", this);
            return false;
        }

        if (_animator == null)
        {
            Edit.LogError("[No.23] GrabPulseDriver가 자식에서 Animator를 찾지 못했습니다.", this);
            return false;
        }

        if (EffectManager.Instance == null)
        {
            Edit.LogError("[No.23] EffectManager가 씬에 없습니다. 이 씬에서는 팔 전기 이펙트가 재생되지 않습니다.", this);
            return false;
        }

        return true;
    }
}
