using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <b>트랜스폼 배열을 따라 흐르는 펄스</b>를 반복 재생하는 재사용 컴포넌트.
/// (예: 보스 팔의 어깨 → 팔꿈치 → 손을 훑고 지나가는 전기)
///
/// <see cref="EffectSocketPlayer"/>와 짝이다 — 저쪽은 트랜스폼 <b>하나</b>에 이펙트를 붙여 두고,
/// 이쪽은 트랜스폼 <b>여럿</b>을 잇는 경로 위로 이펙트를 흘려보낸다. 둘 다
/// <see cref="IAnimEventEffect"/>라 애니메이션 이벤트에서 이름으로 부르는 방법이 같다.
///
/// <b>경로는 본이 아니어도 된다.</b> 이 컴포넌트가 아는 것은 "월드 좌표를 가진 트랜스폼의 순서"뿐이다.
/// 팔·다리·무기·레일·체인 어디에나 붙는다. 그래서 이름에 팔이 들어가지 않는다.
///
/// <b>왜 앵커가 여러 개인가.</b> <see cref="EffectManager.PlayLooping"/>은 트랜스폼 하나를 추종한다.
/// 펄스 겹침을 허용하므로(펄스 A가 끝에 있을 때 펄스 B가 출발) 앵커를 공유할 수 없다 —
/// 하나를 돌려쓰면 살아 있는 펄스 전부가 같은 지점으로 끌려온다. 그래서 앵커도 풀로 돌린다.
///
/// ⚠️ <b>루프 핸들은 버리면 풀 인스턴스가 영원히 돌아오지 않는다.</b> 종료 신호는 생각보다 자주 유실된다 —
/// 전이가 잘리거나(그로기·사망·경직), 클립을 편집하다 이벤트가 지워지거나. 그래서
/// 펄스 도착 · <see cref="Stop"/> · <see cref="OnDisable"/> 세 곳에서 회수한다.
///
/// ⚠️ <b>서버 가드를 넣지 말 것.</b> 애니메이션 이벤트는 모든 피어에서 각자 발화한다 —
/// <c>IsServer</c>로 감싸면 호스트에서만 보인다.
/// </summary>
[DisallowMultipleComponent]
public class EffectPathPlayer : MonoBehaviour, IAnimEventEffect
{
    [Header("식별")]
    [Tooltip("애니메이션 이벤트가 이 이펙트를 지목할 때 쓰는 이름(예: ArmElectric).\n" +
             "한 유닛 안에서 고유해야 한다. 비워두면 EffectAnimEvents가 찾지 못한다 — " +
             "애니메이션 이벤트를 안 쓰고 코드에서 직접 부르는 경우에는 비워도 된다")]
    [SerializeField] string id;

    /// <summary>애니메이션 이벤트에서 이 이펙트를 지목하는 이름.</summary>
    public string Id => id;

    [Header("무엇을")]
    [Tooltip("펄스 하나로 재생할 이펙트 엔트리. 비어 있으면 이 컴포넌트는 아무 일도 하지 않는다")]
    [SerializeField] EffectEntry effect;

    [Tooltip("프리팹에 저작된 크기에 곱해지는 배율")]
    [SerializeField, Min(0.01f)] float scale = 1f;

    [Header("어디를 따라")]
    [Tooltip("펄스가 지나갈 트랜스폼을 **순서대로**. 최소 2개 (예: shoulder.r → forearm.r → hand.r).\n" +
             "구간 사이는 길이 비례로 보간한다 — 균등하게 나누면 구간 길이 차만큼 관절에서 속도가 튄다")]
    [SerializeField] Transform[] path;

    [Header("리듬")]
    [Tooltip("펄스 하나가 경로 처음부터 끝까지 가는 데 걸리는 시간(초)")]
    [SerializeField, Min(0.02f)] float travelTime = 0.4f;

    [Tooltip("펄스 시작과 다음 펄스 시작 사이의 간격(초). travelTime보다 짧으면 펄스가 겹친다(의도된 동작)")]
    [SerializeField, Min(0.02f)] float interval = 0.6f;

    [Tooltip("동시에 살아 있을 수 있는 펄스 수. ceil((travelTime + outroDuration) / interval) + 1 정도면 충분하다")]
    [SerializeField, Min(1)] int pulsePoolSize = 4;

    [Header("회전")]
    [Tooltip("진행 방향으로 앵커를 돌릴 때의 감쇠 계수. 관절에서 구간이 바뀌며 방향이 꺾이는 걸 완충한다.\n" +
             "0이면 완충 없이 즉시 스냅")]
    [SerializeField, Min(0f)] float rotationSmoothing = 18f;

    [Header("진단")]
    [Tooltip("펄스 시작·정지 시점을 콘솔에 남긴다. 연출이 안 보일 때 호출까지 왔는지 확인용")]
    [SerializeField] bool logPulses;

    /// <summary>진행 중인 펄스 하나. 앵커와 이펙트 핸들이 한 몸으로 움직인다.</summary>
    class Pulse
    {
        public Transform anchor;
        public EffectHandle handle;
        public float elapsed;
        public bool active;
    }

    readonly List<Pulse> _pulses = new List<Pulse>();
    float[] _segments;            // 구간 길이 스크래치(매 프레임 재계산 — 본이 움직인다)
    bool _ready;
    bool _emitting;
    float _sinceLastPulse;

    /// <summary>지금 새 펄스를 내고 있는가. (이미 떠 있는 펄스와는 별개다)</summary>
    public bool IsEmitting => _emitting;

    void Awake()
    {
        Transform poolRoot = new GameObject("EffectPathAnchors").transform;
        poolRoot.SetParent(transform, false);

        for (int i = 0; i < pulsePoolSize; i++)
        {
            Transform anchor = new GameObject($"EffectPathAnchor_{i}").transform;
            anchor.SetParent(poolRoot, false);
            _pulses.Add(new Pulse { anchor = anchor });
        }
    }

    // EffectManager는 자기 Awake에서 Instance를 세운다. 순서에 기대지 않도록 Start에서 검사한다.
    void Start()
    {
        _ready = Validate();
        if (_ready) _segments = new float[path.Length - 1];
    }

    /// <summary>
    /// [애니메이션 이벤트] 펄스 방출 시작. 재진입은 안전하다 —
    /// 같은 공격이 연속으로 들어와 시작이 두 번 불려도 타이머만 다시 채운다.
    /// </summary>
    public void Play()
    {
        if (!_ready) return;

        _emitting = true;
        _sinceLastPulse = float.MaxValue;   // 시작 프레임에 첫 펄스가 바로 나가게
        if (logPulses) Edit.Log($"[EffectPathPlayer] '{name}' 펄스 시작", this);
    }

    /// <summary>
    /// [애니메이션 이벤트] 펄스 <b>하나</b>만 낸다. 방출 상태를 바꾸지 않는다 —
    /// 리듬 없이 한 번만 훑고 지나가는 연출용이다.
    /// </summary>
    public void PlayOnce()
    {
        if (!_ready) return;
        Spawn();
    }

    /// <summary>
    /// [애니메이션 이벤트] 펄스 방출 종료. <b>이미 떠 있는 펄스는 제 갈 길을 간다</b> —
    /// 도착하면서 자연스럽게 꺼진다. 중간에서 뚝 끊고 싶으면 컴포넌트를 비활성화할 것
    /// (<see cref="OnDisable"/>이 즉시 회수한다).
    /// </summary>
    public void Stop()
    {
        if (!_emitting) return;

        _emitting = false;
        if (logPulses) Edit.Log($"[EffectPathPlayer] '{name}' 펄스 정지", this);
    }

    void Update()
    {
        if (!_ready) return;

        float dt = Time.deltaTime;

        if (_emitting)
        {
            _sinceLastPulse += dt;
            if (_sinceLastPulse >= interval)
            {
                // 스폰 성패와 무관하게 여기서 리셋한다. 실패 시에만 남겨두면 다음 프레임에 또 시도해
                // 경고가 프레임마다 쏟아진다 — 알림 주기를 interval에 묶어 둔다.
                _sinceLastPulse = 0f;
                Spawn();
            }
        }

        Advance(dt);
    }

    void Spawn()
    {
        Pulse pulse = FreePulse();
        if (pulse == null)
        {
            // 풀이 비었다 = 설정상 동시 펄스 수가 앵커 수를 넘었다. 값을 고쳐야 하므로 조용히 넘기지 않는다.
            Edit.LogWarning($"[EffectPathPlayer] '{name}' 앵커 풀({pulsePoolSize}) 고갈. " +
                            "interval을 늘리거나 pulsePoolSize를 올릴 것.", this);
            return;
        }

        pulse.elapsed = 0f;
        pulse.active = true;

        // 출발 프레임에 앵커를 제자리에 앉혀 둔다. 안 그러면 지난 펄스가 남긴 위치에서 한 프레임 튄다.
        Place(pulse, 0f, snapRotation: true);

        pulse.handle = EffectManager.Instance.PlayLooping(effect, pulse.anchor, Vector3.zero, scale);
    }

    void Advance(float dt)
    {
        for (int i = 0; i < _pulses.Count; i++)
        {
            Pulse pulse = _pulses[i];
            if (!pulse.active) continue;

            pulse.elapsed += dt;
            float t = travelTime > 0f ? pulse.elapsed / travelTime : 1f;

            if (t >= 1f)
            {
                Place(pulse, 1f, snapRotation: false);
                Retire(pulse, false);   // 끝에 닿았다 → outro를 태우며 자연 소멸
                continue;
            }

            Place(pulse, t, snapRotation: false);
        }
    }

    /// <summary>
    /// 경로 위 진행률 <paramref name="t"/>(0~1) 지점에 앵커를 앉힌다.
    ///
    /// <b>길이 비례</b> 보간이다 — 구간을 균등하게 나누면 구간 길이 차(위팔 vs 아래팔)만큼
    /// 관절에서 속도가 튄다. 회전은 진행 방향(구간 탄젠트)을 향하되 관절에서의 꺾임을
    /// <see cref="rotationSmoothing"/>으로 완충한다.
    /// </summary>
    void Place(Pulse pulse, float t, bool snapRotation)
    {
        float total = 0f;
        for (int i = 0; i < _segments.Length; i++)
        {
            _segments[i] = Vector3.Distance(path[i].position, path[i + 1].position);
            total += _segments[i];
        }

        Vector3 position;
        Vector3 tangent;

        if (total <= Mathf.Epsilon)
        {
            // 경로가 한 점으로 접혔다(본이 완전히 겹침). 끝점에 두고 방향만 유지한다.
            position = path[path.Length - 1].position;
            tangent = transform.forward;
        }
        else
        {
            float travelled = Mathf.Clamp01(t) * total;

            int seg = 0;
            while (seg < _segments.Length - 1 && travelled > _segments[seg])
            {
                travelled -= _segments[seg];
                seg++;
            }

            Vector3 a = path[seg].position;
            Vector3 b = path[seg + 1].position;
            position = Vector3.Lerp(a, b, _segments[seg] > Mathf.Epsilon ? travelled / _segments[seg] : 1f);
            tangent = b - a;
        }

        Quaternion target = tangent.sqrMagnitude > Mathf.Epsilon
            ? Quaternion.LookRotation(tangent.normalized, Vector3.up)
            : pulse.anchor.rotation;

        Quaternion rotation = (snapRotation || rotationSmoothing <= 0f)
            ? target
            // 프레임레이트에 독립인 감쇠. Slerp에 dt를 그대로 넣으면 프레임이 빠를수록 덜 따라간다.
            : Quaternion.Slerp(pulse.anchor.rotation, target,
                               1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime));

        pulse.anchor.SetPositionAndRotation(position, rotation);
    }

    Pulse FreePulse()
    {
        for (int i = 0; i < _pulses.Count; i++)
        {
            if (!_pulses[i].active) return _pulses[i];
        }
        return null;
    }

    void Retire(Pulse pulse, bool immediate)
    {
        pulse.active = false;

        if (!pulse.handle.IsSet) return;

        if (EffectManager.Instance != null)
        {
            if (immediate) EffectManager.Instance.ReleaseImmediate(pulse.handle);
            else EffectManager.Instance.Release(pulse.handle);
        }

        pulse.handle = EffectHandle.None;
    }

    /// <summary>
    /// 안전망. 그로기·사망·despawn으로 컴포넌트가 꺼지면 Update가 멈춰 정지 경로를 타지 못한다 —
    /// 그대로 두면 핸들이 새서 풀이 고갈된다.
    /// 패턴이 꺾인 자리에 이펙트가 남으면 "끊겼다"는 피드백이 죽으므로 여기서는 <b>즉시</b> 회수한다.
    /// </summary>
    void OnDisable()
    {
        _emitting = false;

        for (int i = 0; i < _pulses.Count; i++)
        {
            if (_pulses[i].active) Retire(_pulses[i], true);
        }
    }

    bool Validate()
    {
        if (path == null || path.Length < 2)
        {
            Edit.LogError($"[EffectPathPlayer] '{name}'의 path에 트랜스폼이 2개 이상 필요하다 " +
                          "(예: shoulder → forearm → hand).", this);
            return false;
        }

        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == null)
            {
                Edit.LogError($"[EffectPathPlayer] '{name}'의 path[{i}]가 비어 있다.", this);
                return false;
            }
        }

        if (effect == null)
        {
            Edit.LogError($"[EffectPathPlayer] '{name}'에 EffectEntry가 연결되지 않았다.", this);
            return false;
        }

        // 씬에 매니저가 없으면 조용히 아무 일도 안 일어난다 — 원인이 하나뿐인데 증상이 모호하다.
        return EffectManager.TryGet(out EffectManager _, this);
    }
}
