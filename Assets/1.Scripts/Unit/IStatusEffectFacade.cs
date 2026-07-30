// 상태이상 "읽기" 공통 인터페이스 — Unit이 소비하는 표면.
//
// 배경(SuperArmor Unit 통합, Docs/tech/handoff-superarmor-unit-unification.md 권장안):
//  - feature/PlayerSkill의 Unit은 StatusEffects에서 정확히 2개만 소비한다:
//      HasSuperArmor(Unit.Knockback 슈퍼아머 가드) / GetStatMultiplier(Final* 스탯 계산).
//  - 이 인터페이스를 StatusEffectController(플레이어)와 MonsterStatusEffect(몹)가 둘 다 구현하면
//    Unit.HasSuperArmor가 몹까지 커버된다(현재는 플레이어 전용 구체 타입이라 몹에서 항상 false).
//
// 은희(코어) 쪽 연결 방법(머지 시):
//  1) StatusEffectController에 `: IStatusEffectFacade` 선언 추가(멤버 시그니처 이미 일치 — 코드 변경 불필요).
//  2) Unit의 StatusEffects 캐시 타입을 IStatusEffectFacade로 바꾸고 GetComponent<IStatusEffectFacade>()로 탐색
//     (Unity GetComponent는 인터페이스 지원).
//
// 참고: 몹의 "쓰기" 파사드는 별도(IMonsterStatusFacade: ApplyStatus/RemoveStatus/ClearAll).
public interface IStatusEffectFacade
{
    /// <summary>슈퍼아머 활성 여부 — Unit.Knockback 공통 진입점의 넉백/경직 거부 가드가 읽는다.</summary>
    bool HasSuperArmor { get; }

    /// <summary>statType(Modifier 플래그)의 스탯 배율. 효과 없으면 1f. Unit의 Final* 스탯 계산이 읽는다.</summary>
    float GetStatMultiplier(StatusEffectType statType);
}
