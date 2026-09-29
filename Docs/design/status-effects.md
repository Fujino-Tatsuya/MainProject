# 상태이상 (Status Effects)

> 플레이어·보스가 공유하는 **중앙 시스템**. 서버 권한으로 적용·만료, 클라에 복제(애니/VFX·입력 게이팅).
> 자료구조 권장안은 [../tech/architecture.md](../tech/architecture.md) §상태 참조.

## 목록과 게이팅 규칙
| 상태 | 효과(게이팅) |
|------|--------------|
| **공중에 뜸(Airborne)** | 공중 CC. 이동/행동 불가, 착지까지 무력. |
| **기절(Stun)** | 모든 입력 차단(이동·스킬·평타). |
| **둔화(Slow)** | 이동속도 배율 감소(행동은 가능). |
| **속박(Root)** | 이동만 불가, 스킬/평타는 가능. |
| **침묵(Silence)** | 스킬 봉인(이동/평타는 가능). |
| **허약(Weak)** | **대쉬 불가 + 둔화** 의 조합 상태. |

### 버프 / 디버프 분류 (2026-09-29)

코드 `StatusEffectCategories.Of(instance)` (`Unit/StatusEffectType.cs`). 인스턴스에서 파생 — 네트워크 구조체는 그대로다.

| 분류 | 타입 |
|------|------|
| **Buff** | `SuperArmor` · `PassiveCharge`(패시브 충전 — 소모 전까지 유지, [PLAN-passive-onhit.md](../../PLAN-passive-onhit.md)) |
| **Debuff** | `Airborne` · `Stunned` · `Slowed` · `Rooted` · `Silenced` · `Debilitated` |
| **magnitude 로 판정** | 스탯 modifier 5종(`MoveSpeed/AttackDamage/AttackSpeed/Defense/MaxHpModifier`) — `>= 1` Buff, `< 1` Debuff |

- 새 타입을 추가하면 **이 분류표(코드)에 반드시 넣는다** — 빠지면 경고 로그와 함께 Debuff 로 취급된다.
- 보스 연출의 일괄 해제(`StatusEffectController.ClearAllServer`)는 **디버프만** 지우도록 바뀔 예정(경석) — 이 분류를 쓴다.
- HUD: 상태이상 슬롯에 아이콘 + 타입명·스택·남은시간. 아이콘은 타입별 표(`StatusEffectHUD.icons`)가 비면 공용 `white_512`.

## 설계 메모
- **동시 다중 상태 가능**(예: 둔화 + 침묵). → 단일 enum이 아니라 **`[Flags]` 비트마스크**로 표현.
  - 예: `[Flags] StatusFlags { None, Airborne, Stun, Slow, Root, Silence, NoDash }`
  - **허약 = `Slow | NoDash`** 조합으로 표현(별도 값 불필요).
- **지속시간/스택/출처**는 Flags로 못 담으므로, **서버가 활성효과 인스턴스 리스트**
  `{ type, endTime, stacks, source }` 를 들고 매 틱 만료 처리 → 그 결과로 **Flags 요약을 재계산**해 복제.
- **대쉬 무적**은 상태이상과 별개의 무적 윈도우. 서버가 피격 시점에 판정.

## 적용 주체
- 보스 기믹(잡기→전기→던지기, 폭탄 그로기/밀쳐냄, 차징 근접 데미지 등)이 플레이어에게 부여.
- 플레이어 스킬(예: CC 계열)이 보스에게 부여(그로기 등은 별도 보스 상태로 처리 가능).
