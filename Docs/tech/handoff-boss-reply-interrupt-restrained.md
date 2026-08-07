# 회신 — 인터럽트 식별자 + `Restrained` (보스 쪽)

> 받는 사람: **은희** · 보내는 사람: **경석** · 작성 2026-08-07
> 원 문서: `player-interrupt-restrained-handoff.md` (2026-08-07)
> 보스 브랜치: `feature/Boss23`
>
> ⚠️ **개정 1판** — 질문 3·4 답변이 기획 회의 결과로 **초판과 반대**입니다.
> 초판을 이미 보셨다면 §1 과 §2 를 다시 읽어 주세요. 그 외 항목은 초판과 같습니다.

## 결론 먼저

**A(인터럽트 플래그) 형태 그대로 받겠습니다. B'(`Restrained`) 설계도 승인입니다 — 착수해 주세요.**
전제 정정 2건(`BaseAttack` 미경유 / 단죄의 방패 미구현)도 확인했습니다.

단 **슈퍼아머 처리에서 요청이 하나 늘었습니다** — §2 를 먼저 봐 주세요.

---

## 1. 질문 5건 답변 (기획 회의 확정)

| # | 질문 | 답 |
|---|---|---|
| 1 | 밀림 종료 후 기절은 **보스가** 거나? | ✅ **보스가 겁니다.** 지속시간이 보스 튜닝값이라 그쪽이 맞습니다. 플레이어 쪽 작업 0 |
| 2 | 돌진 중 보스가 방향을 트나? | ✅ **직선입니다.** 시작 시 방향을 고정하고 `NavMesh.Raycast` 로 메시 경계까지 클램프합니다(SpinnerBot 선례) — 곡선 처리는 필요 없습니다 |
| 3 | Q 슈퍼아머로 돌진을 버티는 게 의도? | ✅ **의도입니다.** 슈퍼아머면 **돌진을 무시하고 데미지만 받습니다**(기획 회의 확정) |
| 4 | `Restrained.Push` 에 슈퍼아머 검사? | ✅ **넣어 주세요.** 3번의 결론이 곧 이것입니다 — 검사가 없으면 "슈퍼아머면 안 밀린다"가 성립하지 않습니다 |
| 5 | 적 공격도 인터럽트를 거나? | ✅ **필요 없습니다.** 보스 그로기는 **인터럽트 스킬 + 송전기**만 유발이 확정 스펙입니다. `BaseAttack` 의 저작 토글은 되살리지 마세요 |

---

## 2. 🔴 요청 추가 — `Push` 진입이 **성공 여부를 반환**해야 합니다

3번이 "슈퍼아머면 무시 + 데미지만"으로 확정되면서, **C-2 가 지적한 비대칭이 다시 살아납니다.**
그런데 은희 님 설계 안에 이미 해답이 있습니다:

> §B-3: **밀림 중엔 `Stunned` 가 필요 없습니다.** 기절은 **밀림이 끝난 뒤에만** 걸면 됩니다.

이 규칙을 그대로 따르면 자동으로 대칭이 맞습니다:

| 대상 | 밀림 | 기절 | 데미지 |
|---|---|---|---|
| 일반 플레이어 | ○ | ○ (벽 도달 후) | ○ |
| **슈퍼아머 플레이어** | ✕ (거부) | ✕ — **밀림이 시작되지 않았으니 "끝난 뒤"도 없다** | ○ |

즉 **"안 밀리는데 기절만 걸리는"** 조합이 생기지 않습니다.
슈퍼아머 상태에서는 **데미지만** 들어갑니다 — 기획 확정 그대로입니다.

### 그래서 필요한 것

```csharp
// 반환값이 필요합니다 — 기존 BeginGrabbedByInstigator 와 같은 bool 규약이면 됩니다.
bool BeginRestrainedByInstigator(GameObject instigator, RestraintMode mode, float frontOffset);
```

**보스는 반환값으로만 갈라 처리합니다:**

```csharp
// 돌진이 플레이어에 적중
bool pushed = player.BeginRestrainedByInstigator(gameObject, RestraintMode.Push, frontOffset);

// 데미지는 밀림 여부와 무관하게 항상 (서버 경로)
player.ReceiveAttack(dashInfo, ctx);

if (pushed) _carried.Add(player);   // 벽 도달 시 이 목록만 기절시킨다
```

```csharp
// 벽/맵 끝 도달 → 밀고 온 대상만 정지 + 기절
foreach (var p in _carried)
{
    p.EndRestrainedByInstigator();
    p.StatusEffects.Apply(StatusEffectType.Stunned, wallStunDuration, NetworkObjectId);
}
```

**슈퍼아머 검사를 `Push` 안에 두는 이유** — `Unit.Knockback` 이 이미 `if (HasSuperArmor) return;` 로
같은 게이트를 갖고 있습니다. 보스가 밖에서 `HasSuperArmor` 를 직접 보면 **같은 규칙이 두 곳에** 생기고,
나중에 한쪽만 바뀝니다. 규칙은 플레이어 쪽 한 곳에 두고 보스는 결과만 보는 게 맞습니다.

### 밀림 도중 슈퍼아머를 얻는 경우는 신경 쓰지 않아도 됩니다

`Restrained` 진입만으로 `CanUseSkill` 이 false 가 되므로(`PlayerStateController.cs:25,29`)
**밀리는 중에 Q 를 켤 수 없습니다.** 진입 시점 1회 검사로 충분합니다.

> `frontOffset`·`wallStunDuration` 은 보스 SO 노출값입니다. **벽 판정은 보스가 합니다**(§B-3 수용).

---

## 3. 🔴 우리 쪽 결정 하나가 뒤집혔습니다 — C-1 덕분입니다

오전에 보스 쪽에서 **"돌진은 매 틱 스턴+넉백 재적용으로 밀기"** 로 확정했었습니다.
§C-1 이 그게 왜 안 되는지 실측으로 답해 줬습니다 — `Unit.Knockback` 시그니처에 duration 이 없고
매 틱 임펄스는 속도가 누적돼 플레이어가 튀어나갑니다.

→ **그 결정을 폐기하고 `Restrained.Push` 를 채택합니다.** 보스 쪽 계획서(`PLAN-boss-fsm.md` §5.2.1)도
같이 갱신했습니다. 짚어 주신 덕에 구현 전에 잡혔습니다.

---

## 4. 보스 쪽은 어디까지 됐나

카운터 수신부는 **이미 만들어져 있습니다.** 인터럽트 식별 한 줄만 갈아 끼우면 됩니다.

```csharp
// TwentyThreeBoss.ReceiveAttack — 이미 구현됨
bool counter = IsServer
               && _counterWindow.Value              // 카운터 창(Grab·Dash 만 연다)
               && IsInterruptAttack(attackInfo)      // ← 여기가 갈아 끼울 한 줄
               && IsCounterFromFront(hitContext);    // 정면 ±counterFrontAngle
```

`IsInterruptAttack` 은 지금 `attackInfo.isGroggyAttack` 을 읽습니다.
**머지 시 `attackInfo.isInterruptAttack` 으로 한 단어만 바꿉니다** — virtual 로 분리해 둬서 그게 끝입니다.

### 보스는 `isInterruptAttack` **누적을 쓰지 않습니다** (의도)

`MonsterBase` 의 `maxGroggyCount` 누적 경로를 보스만 끕니다(`AutoHitReactions => false`).
보스 그로기는 **카운터 창 + 정면 각도**로만 성립하고, 카운트는 보스가 자기 것을 셉니다.
→ 몹·중간보스의 기존 누적 동작은 **그대로 둡니다.** 두 규칙이 한 플래그를 공유하지만 소비처가 갈립니다
(정확히 §A-2 표의 구조입니다).

### 잡기(S4)는 이미 돌고 있습니다 — `Carry` 래퍼 유지가 중요합니다

`BeginGrabbedByInstigator(gameObject)` 를 `Carry` 기본값 래퍼로 남긴다고 하셨는데, **그게 필수입니다.**
보스 Grab 체인(`Windup→Acquire→Hold→Throw→Recovery`)이 이미 그 API 로 동작 중입니다.

🔴 **한 가지 유지 부탁**: 현재 `PlayerGrabbedState.Tick` 은 `followTarget == null` 이면 **위치 추종만
건너뛰고 물리 위임·입력 차단은 그대로** 합니다. 보스에 잡기 소켓이 아직 없는 동안 이 성질 덕에
"제자리에 붙잡힘"으로 성립하고 있습니다. `Restrained` 개명 시 이 널 허용을 유지해 주세요.

⚠️ **잡기(`Carry`)에는 슈퍼아머 검사를 넣지 마세요.** 4번은 `Push` 한정입니다 —
잡기는 원래 슈퍼아머와 무관하게 걸리고 있었고, 그 동작을 바꾸면 S4 가 회귀합니다.

---

## 5. 🔴 머지 충돌 예고 — `MonsterBase.cs` 가 겹칩니다

브랜치가 갈려 있습니다: 그쪽 `feature/InterruptSkill-CarrySocket`(base `development 6dbc1c34a`) /
보스 `feature/Boss23`. **보스 쪽에서 `MonsterBase.cs` 를 이번에 크게 고쳤습니다.**

| 파일 | 보스 쪽 변경 | 충돌 지점 · 해소 |
|---|---|---|
| `Monster/MonsterBase.cs` | `AutoHitReactions`(virtual) 추가 + `TakeDamage` 에 조기 반환 / `ForceHitReaction` / `ChaseSpeedMultiplier` / `_groggyAfterHit` | 🔴 `TakeDamage` 의 `isGroggyAttack` 줄이 **양쪽에서 수정**됩니다. 해소는 **둘 다 살리기** — 플래그 이름은 그쪽(`isInterruptAttack`), 그 앞의 `if (!AutoHitReactions) return;` 는 보스 쪽. 순서는 **조기 반환이 먼저** |
| `Unit/Weapon/BaseAttack.cs` | 없음 | 그쪽 변경 그대로 받습니다 |
| `Unit/HitFlash.cs` | `SetBaseTint`/`ClearBaseTint` 추가 | 겹칠 일 없음 |
| `Monster/MonsterMeleeAttack.cs` | `SetColliderInfo`/`ColliderInfo` 추가 | 겹칠 일 없음 |

**머지 순서 제안**: 그쪽을 `development` 에 먼저 올리고, 보스 브랜치가 그걸 받아 위 한 줄을 해소하겠습니다.
보스 쪽 변경이 훨씬 넓어서 그 방향이 충돌 면적이 작습니다.

### 폭탄은 걱정 안 하셔도 됩니다

`Bomb.attackType = 1` 보존을 검증해 주신 건 감사합니다 — 다만 보스 재작성에서 폭탄을
**`BossBomb` 으로 새로 썼고**, 되쳐내기 판정은 `deflectAttackType`(신규 필드, 기본 `Default`)로 갑니다.
레거시 `Bomb`/`BombController` 는 전환 검증까지만 공존하다 삭제됩니다.
따라서 **정수값 보존은 레거시 경로에만 유효**하고, 새 경로는 그 함정에서 벗어나 있습니다.

---

## 6. 남은 확인 1건 — 중간보스 그로기 밸런스 (§A-4)

`GauntletBot`/`SpinnerBot`/`WallBot` 의 `maxGroggyCount` 3/3/4 가 이제 실제로 돈다고 하셨는데,
**메커니즘은 맞습니다**(그 값은 원래 그 목적으로 저작된 것입니다).
다만 그로기 3~4회가 체감상 맞는지는 **플레이로 판단할 문제**라 지금 확정하지 않겠습니다.
중간보스는 이미 완성 상태이므로, 틀어진 게 보이면 그쪽이 아니라 **SO 값만** 조절하겠습니다.

---

## 7. 검증 관련 — §C-3 은 저희 테스트 계획에 반영했습니다

> 오프라인/미스폰이면 `CanWrite = IsSpawned && IsServer` 라 상태이상이 안 걸린다

이거 짚어 주신 게 큽니다. 보스 검증은 **MPPM 2인 이상**에서만 판정하겠습니다 —
단독 Play 로 "기절이 안 걸린다"를 버그로 오진할 뻔했습니다.
연출 잠금 중 기절 거부(`PlayerEncounterLock`)도 의도대로 두는 데 동의합니다.

---

## 정리 — 그쪽 착수 가능 항목

1. `Restrained` 개명 + `RestraintMode{Carry, Push}` 구현
2. 🔴 **`Push` 진입에 슈퍼아머 검사 + `bool` 반환** (§2)
3. `Carry` 는 **슈퍼아머 검사 없이** 현행 유지 + `BeginGrabbedByInstigator` 래퍼 유지
   (+ `followTarget` 널 허용 유지)
4. 기절 쪽은 손대지 않습니다 — 밀림이 거부되면 보스가 기절도 걸지 않습니다

보스 쪽은 S5 를 이 전제로 짜 두고, 그쪽 머지 후 호출 3줄만 연결하겠습니다.
