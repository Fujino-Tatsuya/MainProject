using UnityEngine;

/// <summary>
/// <b>붙잡힌 대상에게 붙는 연출</b>을 한 곳에서 몬다 — 프록시 트랜스폼을 대상의 가슴 높이로 옮기고,
/// 거기에 매달린 <b>다리</b>(<see cref="EffectPathPlayer"/>: 보스 손 → 프록시)와
/// <b>감싸기</b>(<see cref="EffectSocketPlayer"/>: 프록시 기준 몸 전체)를 함께 켜고 끈다.
///
/// <b>왜 프록시가 필요한가.</b> <see cref="EffectPathPlayer.path"/> 는 프리팹에 직렬화된
/// <c>Transform[]</c> 이라 <b>런타임에 스폰되는 플레이어를 넣을 수 없다.</b> 그래서 보스 프리팹 안에
/// 빈 트랜스폼을 하나 두고 그것을 대상 위치로 옮긴다 — 경로 배열은 그대로 두고 끝점만 움직이는 셈이다.
///
/// <b>보스를 모른다.</b> 받는 것은 대상 트랜스폼 하나뿐이라, 같은 구조가 필요한 다른 유닛에도 붙는다.
/// 시작·종료를 언제 부를지는 부르는 쪽(23호는 그랩 체인)이 정한다.
///
/// 🔴 <b><c>IsServer</c> 가드를 넣지 말 것.</b> 이 컴포넌트는 각 피어에서 로컬로 돈다 —
/// 가드를 달면 <b>호스트에서만 보인다</b>(<see cref="EffectPathPlayer"/> · <c>DissolveOverlay</c> 가
/// 같은 경고를 달고 있고, 이 레포가 이미 여러 번 겪은 버그다).
///
/// 🔴 <b>"잡혀 있다"는 상태는 복제되지 않는다.</b> <c>Player.BeginRestrainedByInstigator</c> 가 보내는
/// ClientRpc 는 오너 전용이고(<c>if (!IsOwner) return;</c>) FSM 자체도 복제 대상이 아니라,
/// 남의 화면에서 그 플레이어는 계속 Idle 이다. <b>각 피어가 스스로 판단할 수 없으므로</b>
/// 붙잡은 쪽이 브로드캐스트로 알려 줘야 한다 — 그게 <see cref="Play"/> 를 부르는 경로다.
///
/// ⚠️ <b>루프 핸들은 버리면 풀 인스턴스가 영원히 돌아오지 않는다.</b> 종료 신호는 생각보다 자주
/// 유실되므로 <see cref="Play"/> 재진입 · <see cref="Stop"/> · <see cref="OnDisable"/> 에서 회수한다
/// (<see cref="EffectSocketPlayer"/> 와 같은 규약).
/// </summary>
[DisallowMultipleComponent]
public class GrabbedEffectAnchor : MonoBehaviour
{
    [Header("어디에")]
    [Tooltip("대상을 따라 움직일 빈 트랜스폼. 다리의 경로 끝점이자 감싸기의 소켓이다.\n" +
             "비워두면 이 컴포넌트는 아무 일도 하지 않는다")]
    [SerializeField] Transform proxy;

    [Tooltip("대상의 발 기준 높이(m). 다리가 여기에 닿는다. 플레이어 캡슐은 높이 1.79 / 중심 0.88 이라 " +
             "가슴은 대략 1.2 다")]
    [SerializeField, Min(0f)] float chestHeight = 1.2f;

    [Tooltip("대상을 잃었을 때 프록시를 떨어뜨릴 곳(보통 붙잡은 유닛 자신).\n" +
             "비워두면 이 오브젝트 자리를 쓴다")]
    [SerializeField] Transform fallback;

    [Header("무엇을")]
    [Tooltip("보스 손 → 프록시로 흐르는 전기 줄기. 경로의 마지막 원소가 proxy 여야 한다")]
    [SerializeField] EffectPathPlayer bridge;

    [Tooltip("대상의 몸을 덮는 전기. 소켓이 proxy 여야 하고, offset 으로 가슴에서 몸 중심까지 내린다")]
    [SerializeField] EffectSocketPlayer envelope;

    Transform _target;
    bool _playing;
    bool _warnedNoProxy;

    /// <summary>
    /// 지금 <b>유효한</b> 대상을 물고 있나. 대상이 죽거나 디스폰되면 false 로 떨어진다 —
    /// 대상 좌표에 무언가를 찍으려는 쪽은 이 값을 먼저 보고 아니면 자기 폴백으로 가야 한다.
    /// </summary>
    public bool HasTarget => _playing && _target != null && _target.gameObject.activeInHierarchy;

    /// <summary>다리가 닿는 지점(= 프록시의 현재 위치). 대상을 잃었으면 폴백 자리다.</summary>
    public Vector3 ChestPoint => proxy != null ? proxy.position : FallbackPoint;

    Vector3 FallbackPoint => fallback != null ? fallback.position : transform.position;

    /// <summary>
    /// 연출 시작. 이미 재생 중이면 먼저 회수하고 새 대상으로 다시 시작한다
    /// (같은 유닛이 연속으로 붙잡는 경우).
    /// </summary>
    public void Play(Transform target)
    {
        Stop();

        if (proxy == null)
        {
            WarnNoProxyOnce();
            return;
        }

        _target = target;
        _playing = true;

        // 🔴 켜기 **전에** 한 번 스냅한다. 안 그러면 첫 펄스가 직전 대상(또는 원점) 자리에서
        //    출발했다가 다음 프레임에 순간이동한다 — 짧은 펄스라 그 한 프레임이 그대로 보인다.
        SyncProxy();

        bridge?.Play();
        envelope?.Play();
    }

    /// <summary>연출 종료. 재생 중이 아니면 조용한 no-op 이다 — 여러 길목에서 불려도 안전하다.</summary>
    public void Stop()
    {
        bridge?.Stop();
        envelope?.Stop();
        _target = null;
        _playing = false;
    }

    void OnDisable()
    {
        // 풀 인스턴스를 들고 꺼지면 영영 안 돌아온다. 디스폰·씬 전환도 이 길을 지난다.
        Stop();
    }

    void LateUpdate()
    {
        if (!_playing || proxy == null) return;

        // 🔴 LateUpdate 여야 한다. 붙잡힌 포즈는 PlayerRestrainedState.FixedTick 이 쓰고
        //    NetworkTransform 이 보간하므로, Update 에서 읽으면 한 프레임 뒤처진 자리에 붙는다.
        SyncProxy();
    }

    void SyncProxy()
    {
        if (_target != null && _target.gameObject.activeInHierarchy)
        {
            proxy.SetPositionAndRotation(
                _target.position + Vector3.up * chestHeight, _target.rotation);
            return;
        }

        // 🔴 대상을 잃어도 **끄지 않는다.** 끄는 것은 붙잡은 쪽의 종료 신호 몫이다 —
        //    여기서 멋대로 끄면 그 신호가 왔을 때 이미 없어서, 켠 쪽과 끈 쪽의 상태가 갈린다.
        //    (기존 "소켓이 없으면 보스 위치" 정책과 같은 판단이다.)
        proxy.SetPositionAndRotation(FallbackPoint, transform.rotation);
    }

    void WarnNoProxyOnce()
    {
        if (_warnedNoProxy) return;
        _warnedNoProxy = true;
        Edit.LogWarning(
            $"[GrabbedEffectAnchor] '{name}' 에 proxy 가 연결되지 않아 붙잡힌 대상 연출이 재생되지 않는다. " +
            "따라다닐 빈 트랜스폼을 만들어 이 필드에 물리고, 다리의 경로 끝점과 감싸기의 소켓도 " +
            "같은 트랜스폼으로 맞출 것.", this);
    }
}
