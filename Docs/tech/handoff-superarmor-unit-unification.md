# 전달사항 — SuperArmor Unit 통합 (은희 앞) 2026-07-20

> 발신: 경석 / 대상: 은희(코어·상태이상 담당)
> 배경: 커밋 `40038b3 refactor: SuperArmor 거부를 Unit.Knockback 공통 진입점으로 이전` (feature/PlayerSkill) 리뷰 결과.

## 결론
SuperArmor를 Unit으로 올린 방향은 맞으나 **플레이어 경로만 반영**됐고, **몬스터는 빠져 있습니다.** 아래 인터페이스화가 되어야 통합이 완성되고, 경석의 C(Q 지속넉백+경직)가 재개됩니다.

## 커밋 40038b3 — 정상인 부분 (플레이어)
- `Unit.Knockback` 공통 진입점에 `if (HasSuperArmor) return;` 추가 — 슈퍼아머 넉백 거부를 한 곳으로 모음. ✅
- `PlayerStateController.BeginKnockback`의 중복 슈퍼아머 검사 제거(사망만 유지). Unit.Knockback이 선차단하므로 안전. ✅
- 미사용 `PlayerStateController.HasSuperArmor` 삭제. ✅

## 문제 — 몬스터 미포함
`Unit.StatusEffects`가 **구체 타입 `StatusEffectController`(플레이어 전용)** 로 하드타입돼 있음:
```
StatusEffectController _statusEffects;                                   // Unit.cs:199
public StatusEffectController StatusEffects { ... GetComponent<...>() }  // :202
public bool HasSuperArmor => StatusEffects != null && StatusEffects.HasSuperArmor; // :221
```
- 몬스터는 `StatusEffectController`가 없고 별도 클래스 **`MonsterStatusEffect`** 사용(서로 무관).
- 따라서 몹에서 `Unit.StatusEffects` == null → **`Unit.HasSuperArmor`는 항상 false**.
- 결과: `Unit.Knockback`의 슈퍼아머 가드가 **슈퍼아머 몹을 절대 못 막음**(예: `hasSuperArmorWhileAttacking` 중간보스가 넉백에 그대로 밀림).

## 요청 작업 (택1)
- **(권장) 인터페이스화**: `Unit.StatusEffects`를 공통 인터페이스(예 `IStatusEffectFacade` — `HasSuperArmor` / `GetStatMultiplier` 노출)로 바꾸고, `StatusEffectController`(플레이어)와 `MonsterStatusEffect`(몹)가 **둘 다 구현**하게.
- **(대안) 몹이 StatusEffectController 채택**: 단 C에 필요한 **시간제 CC(Stunned/duration)** 를 `StatusEffectController`가 지원해야 함(현재는 `MonsterStatusEffect`만 지원).

## 머지 시 주의
- `StatusEffectController.cs` 경로가 브랜치마다 다름:
  - feature/PlayerSkill: `Assets/1.Scripts/Unit/StatusEffectController.cs`
  - feature/map-player-merge: `Assets/1.Scripts/Player/StatusEffectController.cs`
  → 폴더 이동 충돌 + 머지 후 `StatusEffectController`(플레이어)·`MonsterStatusEffect`(몹) 공존 정리 필요.

## ★ 경석 측 준비 완료 (2026-07-21 — 몹 쪽 인터페이스 구현 끝)
map-player-merge 브랜치에 아래가 이미 들어가 있음:
- **`Assets/1.Scripts/Unit/IStatusEffectFacade.cs`** 신설 — Unit이 소비하는 표면 그대로:
  ```csharp
  public interface IStatusEffectFacade
  {
      bool HasSuperArmor { get; }
      float GetStatMultiplier(StatusEffectType statType);
  }
  ```
- **`MonsterStatusEffect : ..., IStatusEffectFacade`** 구현 완료 — HasSuperArmor(기존), GetStatMultiplier=1f(몹 스탯 modifier 도입 전 무효과).

**은희 쪽 남은 작업 2줄** (PlayerSkill 계약 실측 기준 — Unit은 StatusEffects에서 위 2멤버만 소비):
1. `StatusEffectController`에 `: IStatusEffectFacade` 선언 추가 (멤버 시그니처 이미 일치 — 본문 변경 불필요).
2. `Unit.StatusEffects` 캐시 타입을 `IStatusEffectFacade`로 + `GetComponent<IStatusEffectFacade>()` 탐색 (Unity GetComponent는 인터페이스 지원).
→ 이것만 되면 몹 슈퍼아머가 `Unit.Knockback` 가드에 걸리고, 몹 Final* 스탯도 안전(1f).

## 경석 측 대기 상태
- C(Q 지속넉백 → 넉백 종료 후 ~0.2s Stunned 경직)는 위 통합 완료 후 `Unit.Knockback` 공통 진입점 + 통합 슈퍼아머 위에 얹어 재개.
