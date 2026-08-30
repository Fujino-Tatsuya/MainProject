using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 유닛 루트에 두는 <b>애니메이션 이벤트용 이펙트 등록소</b>.
/// 자식의 <see cref="EffectSocketPlayer"/>들을 <see cref="EffectSocketPlayer.Id"/>로 색인하고,
/// Animator 오브젝트에 <see cref="EffectAnimEventRelay"/>를 자동으로 얹는다.
///
/// <b>왜 자동 부착인가.</b> 애니메이션 이벤트는 Animator와 <b>같은 GameObject</b>의 메서드만 부를 수 있는데,
/// 이 프로젝트의 Animator는 중첩 모델 프리팹(FBX) 안에 있어 인스펙터로 컴포넌트를 붙이기 어렵다.
/// <c>MonsterBase</c>가 <c>MonsterAnimationEventRelay</c>를 같은 방식으로 붙이고 있고, 그 선례를 따른다.
///
/// <b>왜 이펙트마다 메서드를 만들지 않는가.</b> 클립이 <c>PlayEffect("Slash")</c>처럼 <b>이름으로</b> 지목하므로
/// 이펙트가 늘어도 메서드는 셋으로 고정된다(원샷 / 루프 시작 / 루프 종료). 유니티 애니메이션 이벤트는
/// 인자를 하나만 넘길 수 있어서 이 형태가 상한이다.
///
/// ⚠️ <b>이 계통에는 <c>IsServer</c> 가드를 넣지 않는다.</b> 애니메이션 이벤트는 그 애니메이션을 재생하는
/// 모든 피어에서 각자 발화하므로, 연출은 RPC 없이 로컬 재생이 정답이다. 서버로 게이트하면
/// <b>호스트에서만 이펙트가 보인다</b> — 이 레포가 이미 두 번 겪은 버그다.
/// 게임플레이 이벤트(<c>MonsterAnimationEventRelay</c> → <c>NotifyAttackHit</c> 등)는 정반대로 반드시
/// 서버 전용이다. <b>규칙이 반대라서 클래스를 나눠 둔 것이다</b> — 여기에 게임플레이 처리를 섞지 말 것.
/// </summary>
[DisallowMultipleComponent]
public class EffectAnimEvents : MonoBehaviour
{
    [Tooltip("비워두면 자식에서 Animator를 찾는다. 릴레이는 그 오브젝트에 자동으로 붙는다")]
    [SerializeField] Animator animator;

    [Tooltip("비워두면 자식에서 자동 수집한다. 특정한 것만 노출하고 싶을 때만 채운다")]
    [SerializeField] EffectSocketPlayer[] players;

    readonly Dictionary<string, EffectSocketPlayer> _byId =
        new Dictionary<string, EffectSocketPlayer>();

    void Awake()
    {
        BuildIndex();
        EnsureRelay();
    }

    void BuildIndex()
    {
        if (players == null || players.Length == 0)
            players = GetComponentsInChildren<EffectSocketPlayer>(true);

        _byId.Clear();

        for (int i = 0; i < players.Length; i++)
        {
            EffectSocketPlayer p = players[i];
            if (p == null || string.IsNullOrEmpty(p.Id)) continue;

            if (_byId.ContainsKey(p.Id))
            {
                // 같은 이름이 둘이면 클립이 어느 쪽을 부르는지 알 수 없다. 조용히 덮어쓰지 않는다.
                Edit.LogWarning($"[EffectAnimEvents] '{name}'에 id '{p.Id}'가 중복이다 — " +
                                $"'{p.name}'은 무시된다. 이름을 고칠 것.", this);
                continue;
            }

            _byId.Add(p.Id, p);
        }
    }

    void EnsureRelay()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Edit.LogWarning($"[EffectAnimEvents] '{name}'에서 Animator를 찾지 못했다 — " +
                            "애니메이션 이벤트로 이펙트를 재생할 수 없다.", this);
            return;
        }

        if (!animator.TryGetComponent(out EffectAnimEventRelay _))
            animator.gameObject.AddComponent<EffectAnimEventRelay>();
    }

    /// <summary>
    /// id로 이펙트를 찾는다. 못 찾으면 <b>경고를 남긴다</b> — 클립의 문자열 오타가
    /// 무음 no-op이 되면 "이펙트가 왜 안 나오지"로 하루를 쓴다.
    /// </summary>
    public EffectSocketPlayer Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_byId.TryGetValue(id, out EffectSocketPlayer player))
            return player;

        Edit.LogWarning($"[EffectAnimEvents] '{name}'에 id '{id}'인 EffectSocketPlayer가 없다 — " +
                        "애니메이션 클립의 이벤트 문자열과 컴포넌트의 Id를 맞출 것.", this);
        return null;
    }

    /// <summary>[릴레이] 원샷 재생.</summary>
    public void PlayEffect(string id) => Find(id)?.PlayOnce();

    /// <summary>[릴레이] 루프 시작.</summary>
    public void StartEffect(string id) => Find(id)?.Play();

    /// <summary>[릴레이] 루프 종료.</summary>
    public void StopEffect(string id) => Find(id)?.Stop();
}
