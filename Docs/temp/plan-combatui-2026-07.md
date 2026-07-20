# PLAN — CombatUI 1차 (전투 HUD)

> 브랜치: feature/CombatUI (feature/PlayerSkill에서 분기)
> 상태: **1차 완료 (2026-07-16)** — 4종 전부 구현 + MPPM 검증 통과 + 원격 푸시(`112cce7`). PR(→development) 대기
> 이전 PlayerSkill 플랜: Docs/temp/plan-playerskill-2026-07.md 로 이동 (남은 단계 4~7 기록 보존)

## 0. 구현 중 변경된 결정 (원 계획과의 차이)

| 항목 | 원 계획 | 실제 구현 | 사유 |
|---|---|---|---|
| HUD 배치 | MapScene·PlayerBossTest 씬 배치 | **CombatHUD를 Player.prefab 자식으로 중첩** + `OnNetworkSpawn`에서 비오너 비활성 (`00e728f`) | 공용 씬 배치 회피, 씬별 수동 배치 제거 (사용자 합의) |
| 실드 바 | HP 바에 오버레이 레이어 | **HP 바와 동일한 별도 바** (HP 바 위, 실드 0이면 숨김), 수치 텍스트 각 바에 표시 (`3b452c3`) | 사용자 지시 |
| MaxShield | 유지 (FinalMaxShield 게터) | **개념 전면 제거** — 실드 상한 없음. `Unit.Initialize`가 5개 파라미터로 변경, `MaxShieldModifier` enum 삭제 (`3b452c3`) | 사용자 지시. 실드 바 비율은 최대 HP 대비 |
| 보스 바 비주얼 | 플레이스홀더 | **사용자 제작 BossUI.prefab → BossHealthHUD.prefab 개명** 후 컨트롤러 부착, CombatHUD에 중첩 (`984f0e3`) | 아트 선행 제작 |
| BossHudTarget | Wells·23호 프리팹 부착 | **TwentyThree만 부착** — Wells는 Enemy(Unit) 미부착이라 보류 | Wells에 Unit 계열 붙으면 컴포넌트 1개 추가 |
| (계획 외) | — | **PlayerInput 프리팹 기본 비활성** + 오너/오프라인만 `EnableLocalInput` (`18868c1`) | 원격 클론 control scheme 페어링 경고 수정 |
| (계획 외) | — | UGUI 함정: Sprite None인 Image는 Filled 무시 → 필 이미지에 스프라이트 필수 (`e892b13`) | 검증 D에서 발견 |

## 1. 목표

MPPM 수직 슬라이스용 전투 HUD 4종:

1. 스킬 쿨타임 HUD (Q/E/우클릭/R)
2. 로컬 플레이어 HP/실드 바 + 팀원 머리 위 월드스페이스 체력바
3. 보스 HP 바 (화면 상단)
4. 로컬 플레이어 상태이상 아이콘 + 남은시간

## 2. 비범위 (합의됨)

- 아트 — 전부 플레이스홀더(유니티 기본 스프라이트 + 단색, 스킬 아이콘은 Q/E/RMB/R 텍스트). 아트 에셋은 사용자가 준비 중 → 수급 후 프리팹 스프라이트 교체만으로 반영되는 구조가 목표
- 팀원/보스의 상태이상 표시 (보스 StatusEffectController 부착은 Q 무력화 때)
- 데미지 숫자, 킬 로그, 궁극기 게이지, 쿨타임 UI 정밀 동기화(표시용 오차 허용)
- 재접속(late join) 시 쿨타임 미러 복원 — 진행 중 쿨타임은 재접속 시 초기화됨(표시만; 서버 검증은 그대로 유효)

## 3. 데이터 소스 (그릴 확정)

| 항목 | 소스 | 방식 |
|---|---|---|
| HP/실드 | `Unit`의 `NetworkVariable<int>` (이미 전 피어 복제) | 매 프레임 폴링 |
| 상태이상 | `StatusEffectController`의 `NetworkList<StatusEffectInstance>` | 폴링, 남은시간 = duration − (ServerTime − appliedServerTime) |
| 쿨타임 | **클라 로컬 미러** — `PlaySkillClientRpc` 수신(=서버 승인) 시 오너가 `nextReadyTime` 로컬 기록 | 호스트는 서버 장부 그대로, 추가 트래픽 0 |

## 4. 구조

### 4.1 새 파일 (Assets/1.Scripts/UI/Combat/)

| 파일 | 내용 |
|---|---|
| `CombatHUD.cs` | 캔버스 루트. 로컬 플레이어 스폰 이벤트 구독 → 하위 위젯에 바인딩 |
| `SkillCooldownHUD.cs` | 슬롯 4개: 키 라벨 + 쿨타임 필(fillAmount) + 남은 초 텍스트. `GetCooldownRemaining` 폴링 |
| `PlayerHealthHUD.cs` | HP 바 + 실드 바(별도 레이어) + 수치 텍스트 |
| `StatusEffectHUD.cs` | 활성 인스턴스별 아이콘(타입 라벨) + 남은시간. 수동해제(duration 0)는 시간 미표시 |
| `BossHealthHUD.cs` | 상단 보스 바. `BossHudTarget` 스폰/디스폰 static 이벤트 구독. 보스 부재 시 숨김 |
| `BossHudTarget.cs` | 보스 프리팹 부착 마커(NetworkBehaviour). OnNetworkSpawn/Despawn에서 static 이벤트 발생 — Enemy.cs 무수정 |
| `UnitOverheadHealthBar.cs` | Player 프리팹 하위 월드스페이스 캔버스. LateUpdate 카메라 빌보드. `IsOwner`면 비활성(내 것은 HUD가 담당) |

### 4.2 기존 코드 수정 (최소)

- `Player.cs`: `public static Player LocalPlayer` + `OnLocalPlayerSpawned` static 이벤트. `OnNetworkSpawn`의 `IsOwner` 분기에서 설정. 오프라인(!IsNetworkActive) 폴백은 Start에서 설정
- `PlayerSkillController.cs`: `PlaySkillClientRpc`에서 오너일 때 `nextReadyTime[slot] = Time.time + CooldownTime` 1줄 (기존 `GetCooldownRemaining`이 클라에서도 유효해짐)
- `Unit.cs`: `CurrentShield`/`MaxShield` public 게터 없으면 추가 (NetworkVariable 읽기)

### 4.3 프리팹/씬

- `CombatHUD.prefab` — Screen Space Overlay 캔버스. 배치: MapScene(런타임 씬) + PlayerBossTest(검증용)
- 머리 위 체력바 — Player.prefab 하위에 World Space 캔버스 추가
- `BossHudTarget` — 보스 프리팹에 부착

## 5. 완료 조건 — ✅ 전부 충족 (2026-07-16)

1. ~~컴파일 통과 + 전 파일 UTF-8(BOM) + 기존 동작 무변화~~ ✅
2. ~~MPPM 2인 검증: ① 각자 자기 쿨타임(E 시전 후 필 회전)·HP/실드·버프 아이콘 표시 ② 상대 머리 위 체력바 표시/감소 ③ 보스 피격 시 상단 바가 전 피어에서 감소~~ ✅
3. ~~아트 수급 시 수정 지점 = 프리팹의 스프라이트/폰트 교체로 한정~~ ✅ (쿨타임 HUD 외 스프라이트 연결 완료 — `0a79ac9`)

## 6. 구현 순서 — ✅ 전부 완료

1. ~~데이터 노출: Player.LocalPlayer 훅 + 쿨타임 클라 미러 + Unit 실드 게터~~ ✅ `54603b0`
2. ~~CombatHUD 프리팹 + 쿨타임/HP/상태이상 위젯~~ ✅ `54603b0`, `b61e747`, `3b452c3`
3. ~~머리 위 체력바 (Player 프리팹)~~ ✅ `b3393ff`
4. ~~보스 HP 바 + BossHudTarget~~ ✅ `984f0e3` (Wells 부착은 Enemy 부착 시)
5. ~~씬 배치 → MPPM 검증~~ ✅ 씬 배치는 프리팹 자식 방식으로 대체(`00e728f`), MPPM 검증 완료

## 7. 다음 (2차 후보 — 미착수)

- Wells에 Enemy(Unit) 부착 시 BossHudTarget 추가
- 복수 보스 동시 체력바 (웰즈&23호 듀오 연출 확정 시)
- 쿨타임 HUD 아트 교체 (스킬 아이콘)
- 팀원/보스 상태이상 표시, 데미지 숫자 등 (원 비범위 항목)
