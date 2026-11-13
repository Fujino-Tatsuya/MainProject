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
- 공용 `GamepadUiFocus`: 패드 모드일 때 열린 패널 루트 아래에 선택이 없으면 첫 선택(지정 값, 없으면 첫 상호작용 가능한 `Selectable`)을 건다.
  마우스 모드로 돌아오면 손대지 않는다(각 화면의 기존 선택 해제 규칙 그대로).
- 선택을 null 로 지우던 곳(`ButtonLabelStateColor`·`SettingsDropdown`·`CharacterSelectPanel`)은 패드 모드에서 지우지 않는다.
- 화면별: 캐릭터 선택(십자키/왼쪽 스틱 = 포커스 이동, A = 확인, B = 닫기) · 특성 카드(카드 3장 좌우 내비게이션, A = 고르기) ·
  ESC 메뉴/설정(버튼 위아래, 설정 탭은 기본 내비게이션) · 로비 결과(B/A 닫기) · 로비 UI.

## 8. 알려진 한계
- 패드 리바인드·Xbox 아이콘·튜토리얼 문구 없음(키보드 표기 그대로).
- 오른쪽 스틱 조준 지점 거리는 고정(5m) — 비고정 지점 스킬은 사거리 밖이면 기존 Clamp/자동 접근 규칙을 탄다.
- 패드 모드 거너 R 조준 중 대시 취소 없음(B 로 취소 뒤 대시).
- 장치 모드는 프로세스 전역 하나 — 한 PC 에서 두 사람이 키보드·패드를 나눠 쓰는 경우는 고려하지 않는다.
