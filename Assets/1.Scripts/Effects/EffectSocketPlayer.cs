using UnityEngine;

/// <summary>
/// <b>소켓 하나 + 이펙트 하나</b>를 짝지어 켜고 끄는 재사용 컴포넌트.
/// 애니메이션 이벤트로 시작·종료가 결정되는 지속형 연출(공격 궤적, 차징 오라 등)이 대상이다.
///
/// <b>네트워크를 모른다.</b> 애니메이션 이벤트는 그 애니메이션을 재생하는 <b>모든 피어에서 각자</b> 발화하므로
/// (같은 이유로 <c>TwentyThreeAnimEvents</c>의 게임플레이 메서드들이 <c>IsServer</c> 가드를 달고 있다),
/// 연출은 RPC 없이 각 피어가 로컬로 재생하면 된다. 트래픽도 없고 애니메이션과 프레임 단위로 붙는다.
/// ⚠️ 중계 쪽에서 <c>IsServer</c>로 감싸면 <b>호스트에서만 이펙트가 보인다</b> — 이 레포가 이미 겪은 버그다.
///
/// <b>왜 원샷이 아니라 루프인가.</b> 수명이 시간이 아니라 "공격 애니메이션이 끝나는 시점"이라는 이벤트다.
/// 예측한 duration으로 끄면 클립 길이를 조정할 때마다 어긋난다.
///
/// ⚠️ <b>루프 핸들은 버리면 풀 인스턴스가 영원히 돌아오지 않는다.</b> 종료 이벤트는 생각보다 자주 유실된다 —
/// 전이가 잘리거나(그로기·사망·경직), 클립을 편집하다 이벤트가 지워지거나, 같은 공격이 연속으로 들어오면
/// 시작이 두 번 불린다. 그래서 <see cref="Play"/> 재진입 · <see cref="OnDisable"/> · 안전 타임아웃
/// 세 곳에서 회수한다.
/// </summary>
[DisallowMultipleComponent]
public class EffectSocketPlayer : MonoBehaviour
{
    [Header("식별")]
    [Tooltip("애니메이션 이벤트가 이 이펙트를 지목할 때 쓰는 이름(예: Slash, UpperTrail).\n" +
             "한 유닛 안에서 고유해야 한다. 비워두면 EffectAnimEvents가 찾지 못한다 — " +
             "애니메이션 이벤트를 안 쓰고 코드에서 직접 부르는 경우에는 비워도 된다")]
    [SerializeField] string id;

    /// <summary>애니메이션 이벤트에서 이 이펙트를 지목하는 이름.</summary>
    public string Id => id;

    [Header("무엇을")]
    [Tooltip("재생할 이펙트 엔트리. 비어 있으면 이 컴포넌트는 아무 일도 하지 않는다")]
    [SerializeField] EffectEntry effect;

    [Header("어디서")]
    [Tooltip("따라다닐 트랜스폼(손 소켓 등). 비워두면 이 오브젝트 자신을 따라간다.\n" +
             "SetParent를 쓰지 않으므로 대상의 scale이 이펙트에 곱해지지 않는다")]
    [SerializeField] Transform socket;

    [Tooltip("소켓 기준 월드 단위 오프셋. 배율(scale)에 곱해지지 않는다")]
    [SerializeField] Vector3 offset;

    [Tooltip("프리팹에 저작된 크기에 곱해지는 배율")]
    [SerializeField, Min(0.01f)] float scale = 1f;

    [Header("안전장치")]
    [Tooltip("종료 이벤트가 오지 않아도 이 시간(초)이 지나면 강제로 회수한다. 0이면 타임아웃 없음.\n" +
             "가장 긴 공격 클립보다 넉넉히 길게 잡을 것 — 정상 재생을 잘라내면 안 된다")]
    [SerializeField, Min(0f)] float safetyTimeout = 5f;

    EffectHandle _handle;
    float _elapsed;

    /// <summary>지금 재생 중인가.</summary>
    public bool IsPlaying => _handle.IsSet;

    /// <summary>
    /// [애니메이션 이벤트] 재생 시작. 이미 재생 중이면 먼저 회수하고 새로 시작한다
    /// (같은 공격이 연속으로 들어와 시작이 두 번 불리는 경우).
    /// </summary>
    public void Play()
    {
        Stop();

        if (effect == null)
        {
            Edit.LogWarning($"[EffectSocketPlayer] '{name}'에 EffectEntry가 연결되지 않았다.", this);
            return;
        }
        if (EffectManager.Instance == null) return;

        Transform follow = socket != null ? socket : transform;
        _handle = EffectManager.Instance.PlayLooping(effect, follow, offset, scale);
        _elapsed = 0f;
    }

    /// <summary>
    /// [애니메이션 이벤트] <b>원샷</b> 재생. 종료 이벤트가 필요 없는 짧은 연출용이다
    /// (베기 섬광, 착지 충격 등). 수명은 엔트리의 duration이 정한다.
    ///
    /// <see cref="Play"/>와 달리 핸들을 들고 있지 않으므로 회수 책임도 없다 —
    /// 대신 <b>도중에 끌 수 없다</b>. 끝을 애니메이션이 정해야 하는 연출이면 <see cref="Play"/>를 쓸 것.
    ///
    /// 소켓을 <b>따라가지 않고</b> 재생 시점의 위치·회전에 찍는다. 원샷은 짧아서 추종이 필요 없고,
    /// 추종하려면 루프 핸들이 필요해진다.
    /// </summary>
    public void PlayOnce()
    {
        if (effect == null)
        {
            Edit.LogWarning($"[EffectSocketPlayer] '{name}'에 EffectEntry가 연결되지 않았다.", this);
            return;
        }
        if (EffectManager.Instance == null) return;

        Transform from = socket != null ? socket : transform;
        EffectManager.Instance.Play(effect, from.position + offset, from.rotation, scale);
    }

    /// <summary>[애니메이션 이벤트] 재생 종료. 재생 중이 아니면 조용한 no-op이다.</summary>
    public void Stop()
    {
        if (!_handle.IsSet) return;

        if (EffectManager.Instance != null) EffectManager.Instance.Release(_handle);
        _handle = EffectHandle.None;
        _elapsed = 0f;
    }

    void Update()
    {
        if (!_handle.IsSet || safetyTimeout <= 0f) return;

        _elapsed += Time.deltaTime;
        if (_elapsed < safetyTimeout) return;

        // 여기 도달했다는 건 종료 이벤트가 오지 않았다는 뜻이다. 조용히 넘기면 풀이 조금씩 마른다.
        Edit.LogWarning($"[EffectSocketPlayer] '{name}'가 {safetyTimeout:F1}초 동안 종료되지 않아 강제 회수했다. " +
                        "애니메이션 클립의 종료 이벤트를 확인할 것.", this);
        Stop();
    }

    // 오브젝트가 꺼지거나 파괴될 때(보스 사망·디스폰·씬 언로드) 마지막으로 회수한다.
    void OnDisable() => Stop();
}
