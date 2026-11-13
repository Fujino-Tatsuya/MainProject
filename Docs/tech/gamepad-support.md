# 게임패드(Xbox) 지원 — 할 일 278

> 범위: **1단계 = 전투 최소(조작 가능)**, **2단계 = 기본 UI**(캐릭터 선택·특성 카드·ESC 메뉴/설정·로비 결과·로비 UI).
> 범위 밖(나중 별도 할 일): Xbox 아이콘 자동 전환 · 패드 리바인드 · 튜토리얼 문구.
> 네트워크: 방향·지점은 예전처럼 **오너 로컬 계산 → 기존 RPC 그대로**. 새 RPC 없음.

## 1. 장치 모드 — 한 곳에서 정하고 나머지는 조회
- 결정 지점 = `InputDeviceMode`(정적, `Settings/InputDeviceMode.cs`). 드라이버(`InputDeviceModeDriver`, 런타임 DDOL 자동 생성,
  실행 순서 -2000)가 매 프레임 장치를 보고 **마지막 입력 장치**로 정한다. 규칙 = 순수 함수 `InputDeviceModeRules.Next`.
  - 마우스 이동(프레임당 2px 이상)·마우스 버튼·휠·키보드 키 = 마우스 모드(같은 프레임에 둘 다면 마우스가 이긴다).
  - 패드 버튼 누름·스틱/트리거가 문턱(0.35)을 **넘는 순간**(엣지) = 패드 모드. 스틱을 계속 기울이고 있는 것만으로는 다시 주장하지 않는다
    (마우스를 움직이면 바로 마우스 모드로 돌아가고 깜빡이지 않는다).
- 패드 모드에서 바뀌는 것(전부 `InputDeviceMode.IsGamepad` 조회):
  - 조준 원천 = 패드(`PlayerAimIndicator`) · OS 커서 숨김(드라이버가 모드가 바뀔 때만 `Cursor.visible` 를 씀)
  - HUD 슬롯 호버의 평타·간파 차단(`SkillSlotHover.BlocksPrimaryInput`) 해제
  - 몬스터 호버 외곽선 = 조준 방향 원뿔 안 대상(`PlayerAimIndicator.GamepadAimedEnemy`)
- `PlayerInput`(Player.prefab)은 예전처럼 자동 스킴 전환을 켠 채 둔다 — 에셋 바인딩은 스킴 그룹(`Gamepad`)을 단다.

## 2. 버튼표(Xbox)
| 버튼 | 동작 | 경로 |
|---|---|---|
| 왼쪽 스틱 | 이동 | `Player/Move`(기존) |
| 오른쪽 스틱 | 조준 | `Player/Aim`(새 액션, Value Vector2) |
| X | 평타 | `Player/Attack`(기존) |
| A | 대시 · 조준 확정 · 연출 스킵 홀드 · UI 확인 | `Player/Dash`(기존) / 조준·스킵·UI 는 `GamepadButtons` |
| Y | 상호작용 | `Player/Interact`(기존 바인딩, 로비·다리 게이트는 같은 키로 독립 액션) |
| RB | 간파 | `Player/Interrupt` |
| LB | Q(주 스킬) | `Player/SkillMain` |
| LT | E(보조 스킬) | `Player/SkillSub` |
| RT | R(궁극기) | `Player/SkillUltimate` |
| Start | ESC 메뉴 열기/닫기 | `GameMenuController` |
| B | 취소/뒤로(조준 취소·패널 닫기·메뉴 뒤로) | ESC 와 같은 사슬(`GamepadButtons.BackPressedThisFrame`) |
| 십자키 좌·우 | 관전 대상 이전/다음 · 단일 대상 후보 순환 | `CameraTargetSwitcher` · `PlayerSkillTargeting` |

- 템플릿에서 온 `Crouch`(B·C)·`Previous`/`Next`(십자키·1·2) 액션은 코드가 안 써서 **삭제**했다(충돌 방지).
- 디버그 단축키는 키보드 전용이라 겹치지 않는다.

## 3. 조준 공급(혼합) — `PlayerAimIndicator`
- 오른쪽 스틱이 데드존(`gamepadStickDeadzone`, 기본 0.3)을 넘으면 그 방향(이동과 같은 뷰 yaw 로 월드 변환).
- 놓으면 기준 방향 = 이동 중이면 이동 방향, 정지면 마지막 조준 방향. 기준 방향 원뿔(`aimAssistHalfAngle` 기본 45°)·
  거리(`aimAssistRange` 기본 10m) 안 고를 수 있는 적 중 **각도+거리 점수**가 가장 낮은 적 쪽으로 자동 보정. 없으면 기준 방향 그대로.
- `AimGroundPoint` = 보정 대상 위치, 없으면 발 위치 + 방향 × `gamepadAimDistance`(기본 5m).
- 순수 규칙 `GamepadAimRules`(스틱 판정·월드 변환·기준 방향·보정 대상 선택).

## 4. 타겟팅 패드 흐름 — `PlayerSkillTargeting`
- 단일 대상(팔라딘 R `FirstMeleeUltimate`, 거너 R `GunnerTrackingLaser`): 누른 순간 조준 방향을 원뿔 축으로 고정하고,
  원뿔(`gamepadTargetHalfAngle` 기본 60°)·거리(사거리 + `gamepadTargetExtraRange` 기본 6m — 밖이면 기존처럼 자동 접근) 안 후보를
  왼→오른쪽 각도 순으로 세운다. 처음 = 가장 가까운 적. 십자키 좌·우 또는 오른쪽 스틱 튕김(0.6 넘었다 0.3 아래로 돌아와야 다음)으로
  이전/다음(끝에서 돌아감). 같은 스킬 버튼 재입력 또는 A = 확정, B = 취소.
- 지점(어쌔신 변신 E — 고정 거리): 조준 방향만 쓴다(`AimGroundPoint` 방향 투영). 확정·취소는 위와 같다.
- 패드 모드에선 "같은 스킬 재입력" 이 취소가 아니라 **확정**이다(마우스 모드는 예전대로 취소).
- 패드 모드에선 조준 중 대시로 조준 취소(거너 R)가 꺼진다 — A 가 확정이라서. B 로 취소 뒤 대시.
- 순수 규칙 `GamepadTargetCycle`(순환·튕김 엣지·각도 정렬·처음 선택).

## 5. 취소/메뉴 사슬
- ESC 를 쓰던 곳(스킬 조준·자동 접근 취소, 로비 결과, 캐릭터 선택, ESC 메뉴)은 `GamepadButtons.BackPressedThisFrame`(ESC 또는 B)를 본다.
  "이 프레임 ESC 를 썼다" 정적 플래그(`IsEscapeClaimed`)는 B 에도 그대로 선다.
- ESC 메뉴: ESC = 예전 그대로. **B 는 메뉴가 열려 있을 때만**(뒤로/닫기) — 닫혀 있으면 열지 않는다. **Start** = 열기/닫기(리바인드 중 무시).
- 연출 스킵: Space 홀드 또는 패드 A 홀드(`CinematicOverlay.TickSkip`).

## 6. 단계
1. 1단계(전투): 장치 모드 · 에셋 바인딩 · 조준 혼합 · 타겟팅 패드 흐름 · 호버 외곽선 · 상호작용 Y · 연출 스킵 · 취소/메뉴 사슬 · 관전 십자키.
2. 2단계(UI): 패드 모드에서 패널이 열리면 첫 선택 지정·명시 내비게이션, 마우스 모드면 예전 "선택 해제" 규칙 유지. 아래 §7.

## 7. 2단계 UI
- 공용 `GamepadUiFocus`(`UI/GamepadUiFocus.cs`): 모달 루트(`UiModalBlocker`)가 켜질 때 패널로 등록(꺼질 때 해제)되고, 드라이버 LateUpdate 가
  패드 모드일 때만 가장 위(마지막에 켜진) 패널 아래에 유효한 선택이 없으면 첫 선택을 건다 — `Prefer` 로 지정한 것(최근 우선) →
  없으면 **내비게이션이 None 이 아닌** 첫 상호작용 가능 `Selectable`. 이동·확인·취소는 EventSystem(InputSystemUIInputModule 기본 액션:
  왼쪽 스틱·십자키 / A / B)이 한다.
- 마우스 모드로 돌아오는 순간 등록된 패널 안 선택을 지운다. 그 뒤는 각 화면의 기존 규칙 그대로
  (선택을 null 로 지우던 `ButtonLabelStateColor`·`SettingsDropdown` 은 마우스 클릭 때만 도는 경로라 패드에 영향 없음).
- 화면별:
  - 캐릭터 선택: 버튼이 전부 내비게이션 None 이라 첫 선택을 걸지 않고 패널 자체 키 경로를 쓴다 —
    십자키/왼쪽 스틱(0.5) = 포커스 이동(키 반복 그대로), A = 확인, B = 팝업·패널 닫기.
  - 특성 카드: 카드 줄 왼·오른쪽 명시 내비게이션(끝에서 돌아감), 첫 선택 = 첫 카드, A = 고르기. 상충 확인창·초기화 팝업은
    [예]/[아니오] 좌우, 첫 선택 = [아니오].
  - ESC 메뉴: 메뉴 열면 [계속], 그만두기 팝업은 [타이틀]. 설정창은 첫 상호작용 가능 요소(자동 내비게이션). B = 뒤로(메뉴)/닫기.
  - 로비 결과: 닫기 버튼 하나(A = 닫기), B = 닫기.
  - 로비 UI: 상호작용(Y)·캐릭터 선택·결과 패널이 위 경로를 탄다(따로 버튼 있는 로비 패널 없음).

## 8. 알려진 한계
- 패드 리바인드·Xbox 아이콘·튜토리얼 문구 없음(키보드 표기 그대로).
- 오른쪽 스틱 조준 지점 거리는 고정(5m) — 비고정 지점 스킬은 사거리 밖이면 기존 Clamp/자동 접근 규칙을 탄다.
- 패드 모드 거너 R 조준 중 대시 취소 없음(B 로 취소 뒤 대시).
- 장치 모드는 프로세스 전역 하나 — 한 PC 에서 두 사람이 키보드·패드를 나눠 쓰는 경우는 고려하지 않는다.
- 설정창 키 리바인드는 키보드·마우스만 받는다(패드로 리바인드 버튼을 누르면 키보드 입력 대기 — ESC 로 취소).

## 9. 패드 진동(햅틱)
**진입점 하나**: `GamepadHaptics.Play(GamepadHapticEvent, scale)` (`Assets/1.Scripts/Settings/GamepadHaptics.cs`). 호출자는 사건만 알린다. 모터 세기·길이·감쇠·겹침은 서비스가 처리한다.
- **겹침**: 모터마다 살아 있는 진동 중 **가장 센 값**을 쓴다(합산하지 않음). 같은 사건은 프리셋 `minInterval` 안에 다시 오면 무시한다(평타 다단 히트용).
- **시간**: unscaled 시간을 쓴다. 히트스톱·간파 슬로우 중에도 정해진 길이에 끝난다. 매 프레임 `InputDeviceModeDriver.Update` 가 `Tick` 을 부르고, `SetMotorSpeeds` 는 값이 바뀔 때만 보낸다.
- **0 으로 되돌리는 때**: 포커스 잃음 · 앱 일시정지 · 게임 메뉴(ESC/Start) 열림 · 씬 전환(activeSceneChanged) · Play 끝/종료 · 패드 분리·교체 · 마우스 모드 전환 · 설정 0%.
- **누가 울리나**: 패드 모드(`InputDeviceMode.IsGamepad`)일 때만 울린다. 플레이어 사건은 **이 피어 오너 자신의 사건**만 울린다(남이 맞아도 내 패드는 조용). 보스 처치·등장 충격은 공용 사건이라 모든 피어가 각자 자기 패드를 울린다.
- **새 RPC 없음**: 아래 표의 기존 지점에서만 부른다.

| 사건 | low / high | 길이(초) | 감쇠 | 연결한 기존 이벤트 |
|---|---|---|---|---|
| 내 피격 `DamageTaken` | 0.25 / 0.35 × 피해 배율 | 0.15 | 직선, 최소 간격 0.1 | `UnitCameraFeedbackReporter.HandleReplicatedDamage`(로컬 플레이어 대상). 배율 = 피해 ÷ (최대 체력 × 0.25), 최소 0.3, 최대 1 |
| 간파 성공 일반 | 0.45 / 0.6 | 0.25 | ^1.5 | `InterruptProductionDirector.OwnerJudgeSucceeded`(오너 판정 cue, `IInterruptOwnerCueSink`) 단계 Normal |
| 간파 성공 대쉬 | 0.6 / 0.75 | 0.3 | ^1.5 | 같은 곳, 단계 Dash |
| 간파 성공 제압 | 0.85 / 1.0 | 0.45 | ^1.5 | 같은 곳, 단계 Suppress |
| 간파 실패 | 0.15 / 0.3 | 0.08 | 직선 | `OwnerEnded(JudgeFailed)`만. 서버 거부(성공 진동이 이미 남)·끊김·판정 버림은 안 울림 |
| 대시 | 0 / 0.12 | 0.06 | 직선 | `PlayerDashController.TryBeginPredictedDash` 성공(입력 주체) |
| 평타 적중 | 0.05 / 0.18 | 0.05 | 직선, 최소 간격 0.08 | `UnitCameraFeedbackReporter.HandleAttributedDamage` — 내가 공격자, `AttackType.Default`, 대상이 플레이어 아님 |
| 스킬 시전 Q·E | 0.12 / 0.2 | 0.1 | 직선 | `PlayerSkillController.Cast` 오너 요청 순간(오프라인은 시작 성공) |
| 궁극기 R | 0.3 / 0.4 | 0.25 | 직선 | 같은 곳, 슬롯 Ultimate |
| 쓰러짐 | 0.5 / 0.3 | 0.8 | ^1.5 | `PlayerLifeCycleController.NotifyStateObservers` — 오너, Alive → DeadPresentation |
| 보스 처치 | 0.8 / 0.9 | 0.6 | ^1.5 | `BossVictoryGlitchPresentation.Begin`(처치 판정 순간, 모든 피어) |
| 보스 등장 큰 충격 | 0.5 / 0.4 | 0.4 | ^1.5 | `BossEncounterDirector.IntroImpactShakeClientRpc`(기존 카메라 충격 RPC) |

- **수치 원본**: `Assets/Resources/GamepadHaptics/GamepadHapticsSettings.asset` (`GamepadHapticsSettings`). 사건별 프리셋(켜기·low·high·길이·감쇠 지수·최소 간격·선택 곡선) + 전체 켜기 + 전체 배율(`masterScale`) + 피해 비율. 에셋이 없으면 코드 기본값(위 표와 같음)으로 돈다. 기본값은 보수적으로 잡았다.
- **끄는 법**: 플레이어는 설정창 → 게임플레이 → **패드 진동** 슬라이더(0~100%, 0 = 끔, PlayerPrefs `Settings.Gameplay.Haptics`, 기본 100%)로 끈다. 개발에서는 에셋 `enabled` 를 끄거나, 사건별 `enabled` 를 끈다. 이 행은 프리팹에 없다. `GameplaySettingsController.Awake` 가 화면 흔들림 행을 복제해 만든다(문구 키 `UI.Settings.Gameplay.Haptics`).
- **한계**:
  - 원격 클라의 스킬 시전 진동은 요청 순간 울린다. 서버가 거부해도 이미 울린 진동은 되돌리지 않는다.
  - 피격 진동은 복제 피해 RPC 기준이라 왕복 지연만큼 늦다.
  - 평타 판정은 `AttackType.Default` 를 쓴다. 특성·스킬이 평타 타입으로 넣는 피해도 같이 울린다.
  - 진동 대상은 `Gamepad.current` 하나다. Xbox 트리거 모터(임펄스 트리거)는 쓰지 않는다.
  - 게임 메뉴가 열린 동안은 설정 슬라이더 맛보기 진동도 울리지 않는다(타이틀 설정창에서는 울린다).
