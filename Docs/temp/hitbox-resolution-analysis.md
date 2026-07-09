# 피격 판정(Hitbox/Hurtbox) 구조 분석 보고서

- 작성일: 2026-07-09
- 대상 브랜치: `origin/feature/Player` (읽기 전용 분석, 수정 없음, 미push분 포함)
- 요청자: 경석
- 방법: 전수 코드 스윕(sonnet) + 독립 시니어 관점 적대적 검증(opus) 교차 확인

## 1. 배경

경석이 제시한 아이디어: 공격 시마다 `GetComponent`로 부모의 `BaseAttack`을 가져와 `AttackInfo`를 얻고 `TakeDamage`를 호출하는 대신, 피격판정용 콜라이더(`Hurtbox`)에 `Unit`을 미리 캐싱해두고, 공격 측은 hurtbox 레이어만 걸러서 판정한다. layer로 hurtbox만 받고 아닌 경우 mask 처리. 캐싱 이후엔 GetComponent가 한 번만 돈다는 전제.

이 구조가 맞는지, 그리고 "대규모 수정"이 필요한지 확인 요청.

## 2. 결론 (TL;DR)

**제안된 hurtbox 방식이 맞다.** 그리고 **대규모 수정이 아니다.**

- 팀 문서 `Docs/tech/physics.md`가 이미 Body/Hurtbox/AttackHitbox 콜라이더 3분리 + 전용 레이어 구조를 규정해 두었고, `Docs/tech/base-weapon-rework-draft.md`는 판정 정책 초안을 "팀 승인 대기" 상태로 남겨둔 채 멈춰 있다. 즉 새 설계가 필요한 게 아니라 **정책 승인**이 필요한 상태다.
- 코드 변경 자체(판정 헬퍼 일원화)는 작다. 진짜 비용은 코드가 아니라 **레이어/프리팹 데이터 작업 + 멀티 검증**이며, 1인 기준 **3~4일**이 현실적 추정이다.
- "GetComponent 한 번만 돈다"는 전제는 성능상 논점이 아니다(이 규모에서 측정도 안 되는 비용). 진짜 문제는 **타깃 Unit을 찾는 정책이 3갈래로 파편화**되어 있어 특정 조합에서 히트가 조용히 누락된다는 정확성 이슈다.

## 3. 현재 코드의 문제: 판정 정책 파편화

타깃 `Unit`을 찾는 방식이 통일되어 있지 않다.

| 정책 | 사용처 | 실패 조건 |
|---|---|---|
| `transform.root` 후 GetComponent | `BaseAttack.TryResolveHit`, `KnockbackAttack` | 자식 콜라이더가 targetLayer에 있어도 **root 레이어가 다르면 조용히 기각** |
| 충돌 오브젝트에서 직접 GetComponent | `ColliderBasicAttack`, **`GrabController.cs:188`**, **`JumpController.cs:133`** | Unit이 루트에 있고 콜라이더가 자식이면 **null → 데미지 누락** |
| `GetComponentInParent<Unit>()` | `PlayerDefaultAttack`, `DefaultAttackProjectile`, `BombController` | 가장 견고. 최신 코드는 이미 이쪽으로 수렴 중 |

추가로 `Bomb.OnTriggerEnter`는 네 번째 패턴을 쓴다 — 피격자가 공격자를 역참조(`other.GetComponent<BaseAttack>()`)한다. 플레이어 기본공격이 오버랩 쿼리 기반이라 물리적으로 폭탄 트리거에 진입하는 콜라이더가 없어 **애초에 발동이 안 되는** 구조적 결함이 있다(은희가 `AttackInfo` 방식으로 프로토타입 후 리뷰 대기로 리버트한 기록이 `Docs/temp/player-bomb-attackinfo-worklog.md`에 있음).

## 4. 제안 방향에 대한 평가

- **레이어 분리 + 전용 트리거 콜라이더**: 팀 문서 방향과 일치, 타당함.
- **전용 `Hurtbox` 컴포넌트 + Awake 캐싱 + `TryGetComponent<Hurtbox>()`**: 유효한 선택이나 필수는 아님. `GetComponentInParent<Unit>()` 단일화만으로도 목표(자식 hurtbox 해석)의 대부분이 달성됨. 부위별 판정(그로기 가중치 등) 요구가 생기기 전까지는 over-engineering 소지가 있다는 반론도 있음 — 다만 컴포넌트 자체가 15줄 남짓이라 지금 넣는 비용도 낮음. 팀 취향 범위.
- **"대규모 수정 아님"**: 맞음. 단, 판정/쿼리 지점을 실제로 세면 4~5곳이 아니라 **10곳 이상**이다(`BaseAttack.TryResolveHit`, `OverlapAttack`(3형태), `PlayerDefaultAttack`(오버랩 3형태+레이캐스트), `DefaultAttackProjectile`, `ColliderBasicAttack.TakeDamage`, `KnockbackAttack.ApplyKnockbackAttack`, `GrabController.Detect`(3형태), `BombController.CheckHitBetween/HandleHit`). 각각은 소폭 수정이라 "대규모"는 아니지만 손댈 지점 수는 과소평가하지 말 것.

## 5. 놓치면 사고 나는 함정 7가지

1. **`QueryTriggerInteraction.Ignore` 하드코딩 7개소** (`OverlapAttack` 3형태, `PlayerDefaultAttack` 오버랩 3형태+레이캐스트). hurtbox를 `isTrigger=true`로 만드는 순간 이 쿼리들은 **hurtbox를 전부 스킵**해서 영구 미탐지가 된다. 전환 시 최우선 수정 대상. `Collide`로 바꾸면 반대로 같은 레이어의 다른 트리거(폭탄, 장판)까지 잡히므로 레이어 위생이 전제조건.
2. **`OverlapAttack.Hit()`에 dedup·자기 제외 없음**. 한 유닛에 콜라이더가 여러 개(body+hurtbox 등)면 중복 데미지, 자기 hurtbox가 targetLayer에 있으면 자해 가능. `PlayerDefaultAttack`은 이미 `HashSet<Unit>` + owner 체크로 처리 중이므로 그대로 이식하면 됨.
3. **`Bomb` NRE 지뢰**. `Bomb : Unit`인데 `Initialize()` 호출 지점이 없어 `_health`가 null. hurtbox 경로든 AttackInfo 경로든 `TakeDamage` 진입 시 널참조 크래시. 반드시 가드 필요.
4. **애니메이션 이벤트의 서버 발화 검증이 선행되어야 함**. `Hit()`/`HitCurrentStep()` 등은 전부 `IsServer` 게이트인데, 애니메이션 이벤트는 실제 재생 중인 애니메이터 인스턴스에서만 발화한다. 호스트+클라 2인스턴스(MPPM)에서 서버 측 이벤트 타이밍이 보장되는지 확인 없이 hurtbox 작업을 먼저 하는 건 순서가 틀렸다.
5. **`GrabController`/`JumpController`가 direct GetComponent 정책** (민경 담당 보스 기믹). hurtbox 전환으로 콜라이더가 자식으로 내려가면 이 둘은 조용히 데미지 누락. 마이그레이션 대상 목록에 반드시 포함.
6. ~~"잡기 시 parenting으로 root가 깨진다"~~ — 확인 결과 틀린 가설. `GrabController`는 `SetParent` 없이 `MovePosition`으로 소켓 추종만 한다. root 정책 폐기의 근거는 이게 아니라 4번(자식 콜라이더 조용한 기각)이다.
7. **전역 설정 변경 리스크**: hurtbox 레이어 추가는 `ProjectSettings/TagManager.asset`(레이어 인덱스), `DynamicsManager.asset`(Collision Matrix)을 건드린다. 전역 설정이라 두 에셋을 커밋하지 않으면 팀원 로컬과 레이어 인덱스가 어긋나 기존 직렬화된 `LayerMask`가 다른 대상을 가리키게 된다. 기존 이동/환경 충돌 회귀 테스트 필수.

## 6. 결정이 필요한 것 (팀장 판단 포인트)

**레이어 구성**: `physics.md`는 단일 `UnitHurtbox` 레이어 + Faction 검사를 권장하지만, Faction 시스템은 현재 코드에 존재하지 않는다(grep 0건). 7월 수직슬라이스(협동 PvE, 플레이어 2종+보스 2종, 아군 상호 피해 없음) 범위에서는 `PlayerHurtbox`/`EnemyHurtbox` **2레이어가 실용적**이다 — 기존 `targetLayer` 직렬화 필드와 1:1 대응이라 런타임 규칙 코드 0줄로 끝난다.

단, 이는 팀 문서 방향과의 명시적 이탈이므로:
- `IMPLEMENTATION_NOTES`(또는 동등 문서)에 이탈 사유 기록
- 해석 로직을 `TryGetTargetUnit(Collider, out Unit)` 한 곳에 일원화해 후일 Faction 전환이 "한 지점 교체"가 되도록 확보
- 레이어를 순수 물리 필터로만 쓰고 "root 레이어 == faction" 가정을 코드에 새기지 말 것

경고 지표: `BombController.HandleHit`/`CheckHitBetween`은 이미 `player|enemy|wall|ground` 4개 마스크로 다진영을 구분 중 — 객체 타입이 늘수록 마스크가 곱해지므로 Faction 전환 시점을 명시적 기술 부채로 추적해야 함.

## 7. 권장 진행 순서

`base-weapon-rework-draft.md`의 마이그레이션 플랜이 이미 옳게 잡혀 있음. 작은 PR로 분리:

1. 판정 정책 승인 (본 문서 §3, §6 기준)
2. `BaseAttack`에 `TryGetTargetUnit(Collider, out Unit)` / `ApplyDamage(Unit, int?)` 헬퍼 추가
3. `OverlapAttack` dedup + 자기 제외 + 트리거 쿼리 정책 명시화
4. `KnockbackAttack`, `ColliderBasicAttack`, `GrabController`, `JumpController`를 헬퍼로 교체
5. 레이어/프리팹 데이터 작업(TagManager, Collision Matrix, 유닛 프리팹 hurtbox 자식 추가) + 회귀 테스트
6. `Bomb` AttackInfo 경로 부활 (NRE 가드 포함, §5-3)

각 단계 전 애니메이션 이벤트 서버 발화 검증(§5-4)을 먼저 통과시킬 것.
