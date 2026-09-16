using UnityEngine;

/// <summary>
/// <b>인트로 → 지속 → 종료</b> 로 이어지는 단계형 이펙트 재생기.
///
/// <see cref="EffectSocketPlayer"/>(트랜스폼 하나) · <see cref="EffectPathPlayer"/>(트랜스폼 배열)와
/// 같은 가족이다. 저 둘이 <b>어디서</b> 재생하는지를 달리한다면, 이쪽은 <b>수명을 단계로 나눈다.</b>
///
/// 쓰는 자리는 "차오르고 → 유지하다 → 두 가지 중 한 방식으로 끝나는" 연출이다:
/// <list type="bullet">
/// <item>송전기 차징 구슬 — 차오름 → 유지 → 완주하면 사그라짐 / 기둥이 다 부서지면 <b>깨짐</b></item>
/// <item>인터럽트 가능 표시 — 창이 열리며 차오름 → 유지 → 시간 만료로 사그라짐 / 인터럽트 성공하면 <b>깨짐</b></item>
/// </list>
///
/// 🔴 <b>이 컴포넌트는 아무것도 애니메이션하지 않는다.</b> "차오름"은 각 단계의 프리팹이 스스로 하는 일이고
/// (셰이더 float 0→1 이든 스케일이든 파티클이든 — 이쪽은 모른다), 여기가 하는 일은
/// <b>정해진 시간에 엔트리를 갈아 끼우는 것</b>뿐이다. 그래서 연출 방식이 바뀌어도 이 코드는 안 바뀐다.
///
/// ⚠️ <see cref="introDuration"/>은 <b>인트로 프리팹의 저작 길이와 맞춰야 한다.</b>
/// 짧으면 다 차오르기 전에 갈아타 튀고, 길면 다 찬 채로 멈춰 있다가 넘어간다.
///
/// ⚠️ <b>서버 가드를 넣지 말 것.</b> 연출은 각 피어가 자기 화면에 그려야 한다 —
/// <c>IsServer</c>로 감싸면 호스트에서만 보인다(이 레포의 단골 버그).
/// </summary>
[DisallowMultipleComponent]
public class EffectStagePlayer : MonoBehaviour, IAnimEventEffect
{
    [Header("식별")]
    [Tooltip("애니메이션 이벤트가 이 이펙트를 지목할 때 쓰는 이름.\n" +
             "코드에서 직접 부르면 비워도 된다 — EffectAnimEvents 색인에서만 쓰인다")]
    [SerializeField] string id;

    /// <summary>애니메이션 이벤트에서 이 이펙트를 지목하는 이름.</summary>
    public string Id => id;

    [Header("단계")]
    [Tooltip("차오르는 인트로. 비워두면 곧장 지속 단계로 간다")]
    [SerializeField] EffectEntry intro;

    [Tooltip("인트로가 최종 상태에 닿기까지의 시간(초). **인트로 프리팹의 저작 길이와 맞출 것.**\n" +
             "0이면 인트로를 건너뛴다")]
    [SerializeField, Min(0f)] float introDuration = 1f;

    [Tooltip("유지 루프. 끝은 Stop()/Abort()가 정한다")]
    [SerializeField] EffectEntry sustain;

    [Tooltip("정상 종료 원샷 — Stop(). 예: 서서히 사그라짐")]
    [SerializeField] EffectEntry outro;

    [Tooltip("끊긴 종료 원샷 — Abort(). 예: 깨짐.\n" +
             "비워두면 Abort()도 위의 정상 종료를 쓴다(끝을 가르지 않는 연출)")]
    [SerializeField] EffectEntry abortOutro;

    [Header("어디서")]
    [Tooltip("따라다닐 트랜스폼. 비워두면 이 오브젝트 자신을 따라간다.\n" +
             "PlayAt(월드 좌표)로 부르면 그 좌표에 고정되고 이 필드는 무시된다")]
    [SerializeField] Transform follow;

    [Tooltip("기준점에서의 월드 오프셋. 배율(scale)에 곱해지지 않는다")]
    [SerializeField] Vector3 offset;

    [Tooltip("프리팹에 저작된 크기에 곱해지는 배율. 런타임에 정해지는 크기는 PlayAt으로 밀어넣는다")]
    [SerializeField, Min(0.01f)] float scale = 1f;

    EffectHandle _introHandle = EffectHandle.None;
    EffectHandle _sustainHandle = EffectHandle.None;

    Transform _target;        // null = 월드 고정
    Vector3 _point;           // 고정일 때의 월드 좌표
    float _scale = 1f;
    float _elapsed;
    bool _running;
    bool _sustaining;
    bool _warnedEmpty;

    /// <summary>지금 단계가 진행 중인가. (종료 원샷은 포함되지 않는다 — 회수 책임이 없다)</summary>
    public bool IsPlaying => _running;

    /// <summary>[애니메이션 이벤트] 인스펙터 설정대로 시작한다.</summary>
    public void Play() => Begin(follow != null ? follow : transform, default, scale);

    /// <summary>
    /// 월드 좌표에 고정해 시작한다. 크기가 저작 시점에 정해지지 않는 연출용이다 —
    /// 예: 차징 구슬은 판정 장판과 같은 반경이어야 하는데 그 값이 런타임 산출물이다.
    ///
    /// 🔴 좌표를 <b>붙잡아 둔다.</b> 기준이 되던 오브젝트가 도중에 사라져도(장판 despawn 등)
    /// 종료 연출이 제자리에 뜬다 — 좌표를 그때 다시 읽으면 엉뚱한 데서 끝난다.
    /// </summary>
    public void PlayAt(Vector3 point, float scaleOverride = 0f)
        => Begin(null, point, scaleOverride > 0f ? scaleOverride : scale);

    /// <summary>지정한 트랜스폼을 따라가며 시작한다(움직이는 몸에 붙는 연출).</summary>
    public void PlayFollowing(Transform target, float scaleOverride = 0f)
        => Begin(target != null ? target : transform, default, scaleOverride > 0f ? scaleOverride : scale);

    /// <summary>[애니메이션 이벤트] <b>정상 종료.</b> 재생 중이 아니면 조용한 no-op이다.</summary>
    public void Stop() => Finish(aborted: false);

    /// <summary><b>끊긴 종료.</b> 재생 중이 아니면 조용한 no-op이다.</summary>
    public void Abort() => Finish(aborted: true);

    /// <summary>
    /// [애니메이션 이벤트] 인트로만 원샷으로 낸다 — 차오르고 그대로 사라진다.
    /// 단계를 이어갈 필요 없이 "한 번 번쩍"이면 되는 자리용이다. 회수 책임이 없다.
    /// </summary>
    public void PlayOnce()
    {
        EffectEntry entry = intro != null ? intro : sustain;
        if (entry == null)
        {
            WarnEmptyOnce();
            return;
        }
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        Transform from = follow != null ? follow : transform;
        effects.Play(entry, from.position + offset, from.rotation, scale);
    }

    void Begin(Transform target, Vector3 point, float scaleValue)
    {
        Finish(aborted: false, silent: true);   // 재진입 방어 — 이전 단계가 남아 있으면 조용히 회수

        if (intro == null && sustain == null)
        {
            WarnEmptyOnce();
            return;
        }
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        _target = target;
        _point = point;
        _scale = Mathf.Max(0.01f, scaleValue);
        _elapsed = 0f;
        _running = true;
        _sustaining = false;

        // 인트로가 없거나 길이가 0이면 곧장 지속 단계로 — 빈 인트로를 한 프레임 태우지 않는다.
        if (intro == null || introDuration <= 0f)
        {
            EnterSustain(effects);
            return;
        }

        _introHandle = Borrow(effects, intro);
    }

    void Update()
    {
        if (!_running || _sustaining) return;

        _elapsed += Time.deltaTime;
        if (_elapsed < introDuration) return;

        if (!EffectManager.TryGet(out EffectManager effects, this)) return;
        EnterSustain(effects);
    }

    /// <summary>
    /// 🔴 <b>지속을 먼저 켜고 인트로를 지운다.</b> 순서를 뒤집으면 한 프레임 연출이 통째로 사라진다
    /// (레거시 ChargeController가 같은 주석을 달고 있던 자리다).
    /// </summary>
    void EnterSustain(EffectManager effects)
    {
        _sustaining = true;

        if (sustain != null) _sustainHandle = Borrow(effects, sustain);

        if (_introHandle.IsSet)
        {
            effects.ReleaseImmediate(_introHandle);   // 부드러운 마무리는 종료 원샷이 맡는다
            _introHandle = EffectHandle.None;
        }
    }

    EffectHandle Borrow(EffectManager effects, EffectEntry entry)
        => _target != null
            ? effects.PlayLooping(entry, _target, offset, _scale)
            : effects.PlayLooping(entry, _point + offset, Quaternion.identity, _scale);

    void Finish(bool aborted, bool silent = false)
    {
        if (!_running) return;

        _running = false;
        _sustaining = false;
        _elapsed = 0f;

        // 비워 두면 정상 종료로 갈음한다 — 끝을 가르지 않는 연출도 있다.
        EffectEntry end = aborted && abortOutro != null ? abortOutro : outro;

        if (!silent && end != null && EffectManager.Instance != null)
        {
            // 추종 대상이 사라졌으면 붙잡아 둔 좌표를 쓴다(PlayAt 주석 참조).
            Vector3 at = _target != null ? _target.position : _point;
            EffectManager.Instance.Play(end, at + offset, Quaternion.identity, _scale);
        }

        ReleaseLoops();
    }

    void ReleaseLoops()
    {
        if (EffectManager.Instance == null)
        {
            _introHandle = EffectHandle.None;
            _sustainHandle = EffectHandle.None;
            return;
        }

        // 즉시 회수다. 부드러운 마무리는 종료 원샷이 맡으므로 루프까지 outro를 태우면 겹친다.
        if (_introHandle.IsSet) EffectManager.Instance.ReleaseImmediate(_introHandle);
        if (_sustainHandle.IsSet) EffectManager.Instance.ReleaseImmediate(_sustainHandle);

        _introHandle = EffectHandle.None;
        _sustainHandle = EffectHandle.None;
    }

    /// <summary>
    /// 안전망. 그로기·사망·despawn으로 컴포넌트가 꺼지면 Update가 멈춰 종료 경로를 타지 못한다 —
    /// 그대로 두면 핸들이 새서 풀이 고갈된다. 종료 원샷 없이 <b>즉시</b> 걷는다.
    /// </summary>
    void OnDisable()
    {
        _running = false;
        _sustaining = false;
        ReleaseLoops();
    }

    void WarnEmptyOnce()
    {
        if (_warnedEmpty) return;
        _warnedEmpty = true;
        Edit.LogWarning($"[EffectStagePlayer] '{name}'에 인트로도 지속도 연결되지 않았다 — " +
                        "아무 일도 일어나지 않는다.", this);
    }
}
