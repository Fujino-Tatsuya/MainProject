/// <summary>
/// 애니메이션 이벤트가 <b>이름으로 지목할 수 있는</b> 이펙트.
///
/// <see cref="EffectAnimEvents"/>가 이 인터페이스를 색인하므로, 새로운 연출 방식을 만들 때
/// 이것만 구현하면 클립에서 바로 부를 수 있다 — 릴레이의 이벤트 함수는 늘리지 않는다.
/// (유니티 애니메이션 이벤트는 인자를 하나만 넘길 수 있어서 <c>PlayEffect</c> / <c>StartEffect</c> /
/// <c>StopEffect</c> 셋이 상한이고, "어느 이펙트인가"는 그 하나뿐인 String 인자가 정한다.)
///
/// 구현체: <see cref="EffectSocketPlayer"/>(트랜스폼 하나에 붙는 이펙트) ·
/// <see cref="EffectPathPlayer"/>(트랜스폼 배열을 훑고 지나가는 펄스).
///
/// ⚠️ 구현체에 <c>IsServer</c> 가드를 넣지 말 것. 애니메이션 이벤트는 그 애니메이션을 재생하는
/// 모든 피어에서 각자 발화하므로 연출은 로컬 재생이 정답이다 — 서버로 게이트하면
/// <b>호스트에서만 이펙트가 보인다</b>(이 레포가 이미 겪은 버그다).
/// </summary>
public interface IAnimEventEffect
{
    /// <summary>애니메이션 이벤트가 이 이펙트를 지목하는 이름. 한 유닛 안에서 고유해야 한다.</summary>
    string Id { get; }

    /// <summary>원샷 재생. 끝을 스스로 정하는 짧은 연출용 — 회수 책임이 없다.</summary>
    void PlayOnce();

    /// <summary>루프 시작. 끝은 <see cref="Stop"/>이 정한다.</summary>
    void Play();

    /// <summary>루프 종료. 재생 중이 아니면 조용한 no-op이어야 한다.</summary>
    void Stop();
}
