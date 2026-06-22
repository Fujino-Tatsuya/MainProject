# 아키텍처

> 원칙: **상속 최소, 합성 우선.** 데이터는 ScriptableObject. 네트워크 권한은 [networking.md](networking.md) 참조.

## UnitBase + 컴포넌트 조립
`UnitBase : NetworkBehaviour` 는 **공통(기본 상태 + 스탯)만** 보유:
- 현재 체력(`currentHP`, 서버 권한), `IDamageable.TakeDamage(...)`, 사망 이벤트
- 팀/진영, 스탯 블록, 상태이상 리시버
- `LevelComponent` **자리만**(7월 비활성, 8월 연결)
- 파생:
  - `PlayerUnit` — 오너 입력, 클라 이동, AbilityController, 빌드 적용
  - `BossUnit` — 서버 FSM, 페이즈, 기믹
- **이동/어빌리티/상태이상/스탯은 각각 컴포넌트**로 분리(플레이어·보스가 다른 구현 주입).

## 스탯 (struct, 단 3분할)
- `struct CombatStats { maxHP, atk, def, moveSpeed, atkSpeed }` — **정적**(빌드/후일 레벨 때만 변경).
  값타입·캐시 친화, NGO `INetworkSerializable`로 `NetworkVariable<T>` 가능.
- **`currentHP` 는 분리**(런타임, 서버 NetworkVariable). 매 타격마다 변하므로 스탯 struct에 묶으면
  피격 1회마다 struct 전체가 dirty → 스탯 전체 재전송(낭비). 분리해 트래픽·의미 분리.
- **Base → Current 모디파이어 레이어**: base 스냅샷 위에 빌드 모디파이어(가산/배율)를 적용해 current 산출.

## 상태이상
- **`[Flags] StatusFlags`** 요약(복제, 입력 게이팅용) + **서버측 효과 인스턴스 리스트**`{type,endTime,stacks,source}`.
- 매 틱 만료 처리 → Flags 재계산 → 복제. 게이팅 규칙은 [../design/status-effects.md](../design/status-effects.md).

## 어빌리티
- `AbilityController`: **좌클릭(평타)** + **우클릭(캐릭터별: 가붕이 패링 / 징크스 스택 발동)** + Q/E/R 슬롯.
  각 `AbilityConfig`(SO) 참조(데미지/쿨/사거리/CC).
- **R 잠금 플래그**(빌드 해금 시 true), UI 잠금/해금 표시.
- 발동/판정은 클라(오너) 트리거, **데미지/CC/패링 방향판정/스택은 서버 RPC**(서버 권한).
- 적(UnitBase)은 **스택 카운트** 보유(원거리 평타 누적, 우클릭 소비). 쿨/범위/스택수는 `[SerializeField]`로 노출.

## 빌드(모디파이어)
- `BuildModifier`(SO) → 스폰 시 **서버에서** StatSet/AbilityController에 적용.
- 효과 종류·연결: [../design/builds.md](../design/builds.md).

## 보스 FSM + 페이즈
- `BossStateMachine`(서버 전용, 코드 `TwentyThreeState`): `Idle`·`Walk`, **근접** `HookAttack`(좌/우 훅)·`UpperAttack`(어퍼컷→공중에뜸)·`Grab`, **원거리** `JumpAttack`·`DashAttack`, `Charging`·`Groggy`(그로기)·`Break`(그로기 강화)·`Dead`. (Hit는 색깔만 변경이라 상태 아님 / Bomb은 웰즈, Pylon/Rage/PhaseTransition은 로직 레이어)
- `PhaseManager`: 체력 감시, **66%/33%에서 `pendingPhaseTransition` 플래그**.
  각 상태는 **종료 시점에 플래그 확인 → 다음 전환을 고정 패턴(송전기/차징)으로 강제**.
- 고정 패턴은 `BossPhasePattern`(SO)로 데이터화. 기믹 상세: [../design/boss-wells-and-no23.md](../design/boss-wells-and-no23.md).

## 에셋 로딩 — Addressables
- 프로젝트에 **Addressables**가 이미 도입됨. 보스/투사체/프리팹 스폰, 씬 콘텐츠 로딩에 활용.
- 네트워크 스폰 프리팹은 NGO 등록 + Addressables 주소 관리 정합성 주의(주소/그룹명에 점·공백 금지 — [conventions.md](conventions.md)).