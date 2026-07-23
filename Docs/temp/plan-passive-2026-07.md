# PLAN — 패시브 '불굴의 의지' (First Melee Passive)

> 브랜치: feature/PlayerSkill
> 상태: **그릴 완료 — 승인 대기 (구현 미착수)**
> 기획: Google Sheet(VeyTrace) + 그릴 확정(2026-07-23)

## 1. 스펙 (그릴 확정)

탱커 콘셉트 자동 패시브. 맞을수록 빨리 충전되고, 충전되면 다음 기본공격이 강타+회복.

- **쿨다운 기본 30초.** ① 시간 경과 + ② **내가 피격당할 때마다 -2초**(데미지량 무관, 실드로 막아도 카운트) — **둘 다**로 감소.
- 쿨다운 0 → **Ready**.
- **Ready 상태에서 내 기본공격이 적에게 명중** → **발동**:
  - **추가 피해**: 이번 스윙에 **맞은 적 전원**에게 (최종공격력 × 계수 [+ 고정 보너스]).
  - **체력 회복(1회)**: 맞은 적 수 N 기준
    - `N ≤ minTargetThreshold(5)` → `minHealPercent(5%)`
    - `N > 5` → `N × perTargetHealPercent(1%)` (예: 7마리 → 7%)
  - 쿨다운 30초로 **리셋**(Ready 해제).
- 판정: 기본공격 판정 재사용(별도 판정 없음), 캐스트·상태이상·넉백 없음.
- 씬 전환 시 초기화(스폰 시 remainingCooldown = 30이라 자연 초기화).

### 튜닝 변수 (전부 placeholder, 인스펙터 조정)
| 변수 | 기본값 | 의미 |
|---|---|---|
| cooldownTime | 30 | 기본 쿨다운(초) |
| hitCooldownReduction | 2 | 피격 1회당 감소(초) |
| bonusDamageMultiplier | 1 | 추가피해 = 최종공격력 × 이 값 |
| bonusFlatDamage | 0 | 추가피해 고정 보너스 |
| minTargetThreshold | 5 | 최소 회복 적용 타겟 수 경계 |
| minHealPercent | 5 | 경계 이하일 때 회복 % |
| perTargetHealPercent | 1 | 경계 초과 시 타겟당 회복 % |

## 2. 비범위 / 후속 (훅만 제공)
- **Ready 시각 피드백 예정(사용자 확정)**: 검 오브젝트에 오라("기") VFX — Ready면 켜고, 발동해 쿨다운 돌면 끈다.
  → 패시브는 **`NetworkVariable<bool> isReady`** 복제 + **`IsReady` / `event ReadyChanged`** 훅만 제공.
  실제 VFX 컴포넌트(검 참조·이펙트 토글)는 후속 Unity 작업.
- **HUD 반영 예정(사용자 확정)**: 패시브를 HUD에 표시 → HUD가 `IsReady`(복제값) 바인딩. HUD 위젯은 후속 UI 작업.
  (쿨다운 fill이 필요하면 remainingCooldown 스로틀 복제를 추후 추가 — 현재는 Ready bool만 복제)
- 투사체/레이캐스트형 기본공격 연동 (이 근접 캐릭터는 Overlap만 — 이벤트 구조라 후속 캐릭터가 붙이면 됨)
- 아웃라인/애니메이션 (별도 작업)

## 3. 아키텍처

슬롯 스킬(PlayerSkillBase) 아님 — **독립 서버 권위 컴포넌트**. 기존 상태이상 시스템 미사용.

### 3.1 신규 파일
- `Assets/1.Scripts/Player/Skill/FirstMeleePassive.cs` : `BaseNetworkBehaviour`, Player 루트 부착.
  - 서버(또는 오프라인 권위)에서만 동작. `HasGameplayAuthority => !IsNetworkActive || IsServer`.
  - `float remainingCooldown`(스폰 시 = cooldownTime), 서버 판정 `ServerIsReady => remainingCooldown <= 0`.
  - **복제**: `NetworkVariable<bool> isReady`(서버 쓰기 / **오너만 읽기**). 공개 `IsReady`(복제값) + `event ReadyChanged`.
    서버가 쿨다운 전이 시 `RefreshReadyState()`로 갱신 → 오너 로컬 VFX/HUD가 바인딩. (Ready는 서버 전용 이벤트
    의존이라 오너 로컬 미러 불가 — 스킬 쿨타임 HUD의 시간기반 로컬 미러와 다름)
  - `Update()`: 권위 피어면 `remainingCooldown -= Time.deltaTime` (clamp ≥ 0) 후 RefreshReadyState.
  - `NotifyOwnerHit()`: `remainingCooldown -= hitCooldownReduction` (clamp ≥ 0).
  - `OnBasicAttackHitResolved(IReadOnlyList<Unit> enemies)`: IsReady && enemies.Count>0 이면 발동
    (적 전원 추가피해 + 힐 1회 + 쿨다운 리셋).

### 3.2 통합 지점 (기존 파일 수정)
- **`PlayerDefaultAttack.cs`**: `HitOverlap`(서버)에서 이번 스윙에 새로 맞은 Unit들을 로컬 리스트로 모아,
  루프 종료 후 **서버 이벤트** `event Action<IReadOnlyList<Unit>> ServerHitEnemiesResolved` 로 통지.
  (캐릭터 무관 — 패시브가 있으면 구독). PlayerDefaultAttack은 패시브를 직접 몰라도 됨.
- **`Player.cs`**: `ReceiveAttack(AttackInfo, ctx)` 오버라이드 → base 호출 + 서버에서 `passive?.NotifyOwnerHit()`.
  (ReceiveAttack = 모든 연결 피격의 단일 관문, 경감 전 — "데미지 무관" 충족).
- **`FirstMeleePassive`**: Awake/OnNetworkSpawn에서 `PlayerDefaultAttack.ServerHitEnemiesResolved` 구독,
  Despawn/OnDestroy에서 해제.

### 3.3 데미지/힐 적용
- 추가피해: 각 적 `ReceiveAttack(new AttackInfo(bonusDamage, AttackType.Default), ctx)`.
  `bonusDamage = Round(player.FinalAttackDamage × bonusDamageMultiplier) + bonusFlatDamage`.
- 힐: `healPercent` 위 공식 → `healAmount = Round(player.MaxHp × healPercent / 100)` → `player.HealHp(healAmount)`.

## 4. 네트워크/권위
- **이 게임은 항상 온라인(NGO 리슨서버)** — WAN/LAN/WiFi 미연결이어도 로컬 네트워크 카드가 동작하므로
  `IsNetworkActive`는 항상 true다. 따라서 "오프라인" 경로는 실사용 시나리오가 아니다(코드의 `!IsNetworkActive`
  분기는 방어적 잔재). 패시브는 **서버(호스트)** 에서 동작한다.
- 서버 권위 단일 처리. 데미지·힐은 기존 Unit NetworkVariable로 이미 동기화(추가 RPC 불필요).
- 검증은 **호스트 모드/MPPM**로.
- 발동 조건: **명중한 적이 1명 이상일 때만**(허공 스윙은 통지 안 함 → Ready 유지).

## 5. 완료 조건
1. dotnet build 오류 0, UTF-8(BOM).
2. 기존 기본공격/스킬 동작 무변화(패시브 미부착 시 이벤트 구독자 0 → 무영향).
3. 피격 시 쿨다운 감소 + 시간 감소 동시 동작, 0에서 Ready.
4. Ready에서 기본공격 명중 → 맞은 적 전원 추가피해 + 힐(공식대로) + 쿨다운 리셋. (호스트/MPPM 검증)
5. Player.prefab에 FirstMeleePassive 부착·값 세팅(Unity 에디터 배선).

## 6. 리스크/확인
- Ready 시각 피드백 없음(스펙대로). 플레이테스트에서 "언제 Ready인지 모른다" 피드백 나오면 VFX/HUD 후속.
- 추가피해 AttackType은 Default로(기본공격 일부). 데미지 숫자/로그 분리 필요 시 조정.
