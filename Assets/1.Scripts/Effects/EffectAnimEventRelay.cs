using UnityEngine;

/// <summary>
/// Animator 오브젝트에 얹혀 애니메이션 이벤트를 루트의 <see cref="EffectAnimEvents"/>로 넘기는 중계자.
/// <see cref="EffectAnimEvents"/>가 <c>Awake</c>에서 자동으로 붙이므로 프리팹에 직접 넣지 않아도 된다.
///
/// <b>클립에 넣을 이벤트 함수는 셋뿐이다.</b> 어느 이펙트인지는 String 인자가 지정한다 —
/// 이펙트가 늘어도 이 목록은 늘지 않는다.
/// <list type="bullet">
/// <item><c>PlayEffect(string id)</c> — 원샷(베기 섬광, 착지 충격)</item>
/// <item><c>StartEffect(string id)</c> — 루프 시작(공격 궤적, 오라)</item>
/// <item><c>StopEffect(string id)</c> — 루프 종료</item>
/// </list>
/// id는 대상 <see cref="EffectSocketPlayer"/>의 <c>Id</c> 필드와 같아야 하고, 틀리면 경고가 뜬다.
///
/// ⚠️ <b>여기에 <c>IsServer</c> 가드를 넣지 말 것.</b> 애니메이션 이벤트는 모든 피어에서 각자 발화하므로
/// 연출은 로컬 재생이 정답이다. 서버로 게이트하면 호스트에서만 이펙트가 보인다.
/// 게임플레이 판정이 필요하면 <c>MonsterAnimationEventRelay</c>(서버 전용)를 쓸 것 — 그쪽과 규칙이 반대다.
/// </summary>
[DisallowMultipleComponent]
public class EffectAnimEventRelay : MonoBehaviour
{
    EffectAnimEvents _events;

    void Awake()
    {
        _events = GetComponentInParent<EffectAnimEvents>();
    }

    /// <summary>[애니메이션 이벤트] 원샷 재생.</summary>
    public void PlayEffect(string id) => _events?.PlayEffect(id);

    /// <summary>[애니메이션 이벤트] 루프 시작.</summary>
    public void StartEffect(string id) => _events?.StartEffect(id);

    /// <summary>[애니메이션 이벤트] 루프 종료.</summary>
    public void StopEffect(string id) => _events?.StopEffect(id);
}
