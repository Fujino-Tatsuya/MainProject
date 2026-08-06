# 다음 세션 착수용 — 보스 피격 / 차징 기둥 / 플레이어 공격력

> 작성 2026-07-30 세션 종료 시점 · 브랜치 `feature/map-player-merge` (`5726074`, 전부 push됨)
> **조사는 아직 안 했다.** 진입점과 가설만 모아 둔 문서다. "작업 시작"만 있으면 바로 착수할 수 있다.

## 증상 (팀장 보고)

1. **보스가 어느 순간부터 안 맞는다 — 차징(charging) 이후로.** 차징 전에는 맞는다.
2. **플레이어 공격력이 조절됐는지 확인 필요.**
3. **차징 기둥(upper)이 4개 다 있는데, 이전에는 한 번 공격하면 바로 내려갔다.** 체력이 올라갔는지 확인 필요.

증상 ①이 **"차징 이후"로 시점이 특정**된 것이 이전 정보(“어느 순간”)보다 결정적이다.
차징이 보스의 피격 상태를 바꾸는 경로부터 본다.

## 착수 순서

### ① 차징이 보스 피격 상태를 바꾸는가 (증상 ①)

```
Assets/1.Scripts/Enemy/Boss/ChargeController.cs
Assets/1.Scripts/BT/Actions/Attack/SetChargingStateAction.cs      → StartCharge(PlayerCount)
Assets/1.Scripts/Enemy/Boss/Wells&No.23/TwentyThreeState.cs
Assets/1.Scripts/Monster/Boss/BossState.cs · BossBase.cs
Assets/8.BehaviorTreeGraph/Boss/  (BT 그래프 — 차징 진입/이탈 노드)
```

볼 것 — **차징 진입 시 켜지고 이탈 시 꺼지지 않는 것**이 있는지:

- 슈퍼아머 / 무적 플래그가 차징 중 세워지고 **해제 누락**
- hurtbox 콜라이더 `enabled` 토글 후 복구 누락
- 레이어 변경 후 복구 누락 (`TwentyThree.prefab` hurtbox = `EnemyHurtBox(14)` 1개 + `Enemy(8)` 1개)
- BT 그래프에서 차징 서브트리가 중단(interrupt)될 때 정리 노드를 안 지나는 경로

`Enter`/`Exit` 쌍이 맞는지, 그리고 **중단 경로에도 Exit 가 있는지**를 본다.

### ② 차징 기둥 체력 (증상 ③)

```
Assets/1.Scripts/Enemy/Boss/ChargingObject.cs:23-24   [SerializeField] int maxHp;  int defense;
                                              :62     Initialize(0, 0, 0, maxHp, defense)
                                              :93     TakeDamage(AttackInfo)
                                              :101    CheckHp() → CurrentHealth <= 0 → DestroyEvent
```

`maxHp` · `defense` 는 **프리팹 인스펙터 값**이다. 기둥 프리팹에서 현재값을 읽고 git 이력과 비교한다.
`defense` 가 올라가도 같은 증상이 나온다 — 데미지 경감이 들어가면 한 방에 안 죽는다. 둘 다 확인.

> **찾은 단서**: `ChargeController.StartCharge()` 는 인원수로 활성 기둥 수를 정한다.
> ```csharp
> int clampedPlayers = Mathf.Clamp(playerCount, 1, 3);
> _max = (clampedPlayers == 1) ? player1 : (clampedPlayers == 2) ? player2 : player3;   // 기본값 1, 2, 3
> ```
> **`Clamp(…, 1, 3)` 이라 4개째는 이 로직으로는 절대 활성되지 않는다.** "4개 다 있다"는 관측과
> 맞물리므로, 기둥이 4개 보이는 것이 의도인지(4번째는 연출/예비) 아니면 활성 개수 로직이
> 인원수와 어긋난 것인지부터 가른다. 로그도 이미 있다 — `[No.23] 충전 시작 — 인원 N → 기둥 M개 활성.`

### ③ 플레이어 공격력 (증상 ②)

```
Assets/9.ScriptableObject/Player/Garen/PlayerDefaultAttackData.asset
  hittableLayers: m_Bits 17664        (= Enemy 8 + Projectile 10 + EnemyHurtBox 14)
  attackDamageMultiplier: 1 / 1 / 1 / 1.2      ← 4타, 마지막만 1.2배
  flatDamageBonus: 0 (전부)
```

이 SO 에는 절대 데미지가 없다 — 배수만 있다. **기본 데미지는 스탯 쪽**이므로
플레이어 스탯 SO / `UnitBase` 초기화 값을 함께 본다. git 이력으로 최근 변경 여부를 확인한다.

## 이미 배제된 것 (다시 조사하지 말 것)

- **Wells 는 피격 대상이 아니다** — 23호만 맞는다(팀장 확인). Wells hurtbox 가 Default 뿐이라는 관측은 정상이다.
- **폭탄 마스크 `9→8` 은 무효** — `GroundProbe` 가 `Default|Ground` 를 강제 OR 한다.
- **Wells `NetworkObject` 제거는 맞다** — `TwentyThree` 안 중첩 자식이고 `NetworkBehaviour` 는 부모에 바인딩된다.
- **프리팹의 `targetLayer`/`hittableLayers` = 256** 은 무해하다 — 런타임에 위 SO(`17664`)가 덮는다.

## 함께 보면 좋은 것

`Paladin.prefab` 의 `DefaultAttackController.animator` 가 `{fileID: 0}`(null)이다.
`Awake` 에서 `GetComponentInChildren<Animator>()` 폴백으로 **아무 Animator** 를 집어
`PlayerAnimationEventRelay` 를 붙인다(`DefaultAttackController.cs:119-123`). Paladin 마이그레이션으로
계층이 바뀌었으니, 히트 판정 클립 이벤트가 엉뚱한 Animator 로 갈 수 있다.
증상 ①이 차징과 무관하게 재현되면 이쪽을 본다.

## 재개 시 첫 명령

```bash
# 차징 진입/이탈 대칭성
grep -rnE "StartCharge|EndCharge|StopCharge|Invulner|SuperArmor|superArmor" \
  Assets/1.Scripts/Enemy/Boss/ Assets/1.Scripts/Monster/Boss/ --include=*.cs

# 기둥 프리팹의 maxHp/defense 현재값과 이력
grep -rn -A2 "maxHp\|defense" Assets/2.Prefabs/Wells\&No.23/ 2>/dev/null
git log --oneline -8 -- Assets/9.ScriptableObject/Player/
```

## 이 세션에서 미뤄둔 다른 작업 5건

1. `aliveGroundMask` → Ground 전용. 메뉴 `Tools/Map/Authoring/Ground Layer - 플레이어 접지 마스크를 Ground 전용으로` 가 커밋돼 있고 안전성 검증도 끝났다. **실행만 하면 된다**
2. `convayorbelt.shadergraph.meta` GUID 원복 (SVN r249, 은희 확인 후)
3. SVN update (에디터 종료 필요)
4. `DefaultNetworkPrefabs` 의 Wells 등록 검토 (중첩 자식이면 불필요)
5. BT 에셋 churn(`BossArea`·`CommonMeleeRobot`) 폐기 — 에디터 끌 때 한 번

관련: [art-vcs-duplication-handoff.md](art-vcs-duplication-handoff.md) · [map-monster-boss-handoff.md](map-monster-boss-handoff.md)
