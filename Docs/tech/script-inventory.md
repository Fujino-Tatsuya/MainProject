# C# 스크립트 전수 인벤토리

> 기준: 2026-07-15. `Assets` 아래 `*.cs` 189개를 파일 시스템 기준으로 전수 집계했다. Git/ignore 여부와 관계없이 실제 파일을 포함한다.
> 구조 UML과 교차 시스템 설명: [game-structure-uml.md](game-structure-uml.md)
> 부분 갱신: 2026-07-23, `feature/PlayerSkill` / `558ab43`. 아래 Player Skill 절만 최신 구현과 프리팹 배선을 반영했으며 상위 전체 파일 수 집계는 2026-07-15 감사 기준을 유지한다.

## 범위와 판정법

- **연결됨**: 주요 scene/prefab/SO의 GUID 참조, Behavior Graph 타입 참조, 또는 연결된 코드의 직접 호출이 확인됨.
- **지원 코드**: 직접 컴포넌트로 붙지 않아도 연결된 상속/데이터/인터페이스 경로에서 사용됨.
- **부분 연결**: 컴포넌트나 호출은 있으나 필수 참조·실행 경로가 비어 있음.
- **미연결**: 컴파일되지만 현재 주요 자산·호출자·Graph 참조가 확인되지 않음. Reflection이나 테스트 외 호출 가능성은 별도 설명.
- **Editor**: Unity Editor에서만 실행.
- **외부/데모**: 제3자 패키지 또는 샘플. 게임 고유 도메인 코드가 아님.

| 구분 | 파일 수 |
| --- | ---: |
| 1차 게임 코드 `Assets/1.Scripts` | 159 |
| 프로젝트 전용 로컬 도구 `Assets/Editor` | 3 |
| 외부·데모 코드 | 27 |
| **총계** | **189** |

## 1. Player·Unit·Camera·Combat UI — 40개

### Player 루트 — 14개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Player/CharacterDefinition.cs` | 캐릭터 ID·표현 prefab·기본 데이터를 담는 ScriptableObject | **미연결**. 현재 CharacterDefinition 자산/Player 배선 증거가 없음 |
| `Assets/1.Scripts/Player/DefaultAttackController.cs` | 평타 step, chain policy, 이동/회전, animation event, 서버 RPC를 통합한 콤보 오케스트레이터 | **연결됨**. Player prefab·DefaultAttackData·FSM·event relay와 연결 |
| `Assets/1.Scripts/Player/DefaultAttackData.cs` | 평타 콤보 step 목록과 입력/이동/회전 정책 ScriptableObject | **연결됨**. 현재 4단 overlap 콤보 자산이 Player에서 사용 |
| `Assets/1.Scripts/Player/DefaultAttackProjectile.cs` | 서버에서 직선 이동·충돌하는 `BaseAttack` 파생 투사체 | **구현됨/미연결**. 현재 attack SO는 overlap만 사용; NetworkObject가 아님 |
| `Assets/1.Scripts/Player/PlayableCharacterVisual.cs` | CharacterDefinition에 따라 시각 prefab을 교체·장착하는 표현 컴포넌트 | **미연결**. 주요 Player/Paladin prefab 참조 없음 |
| `Assets/1.Scripts/Player/Player.cs` | `Unit` 파생 로컬 플레이어 루트, LocalPlayer 이벤트, grab/knockback 수신, 네트워크 spawn 초기화 | **연결됨**. Player와 Paladin prefab, HUD·카메라의 진입점 |
| `Assets/1.Scripts/Player/PlayerAimIndicator.cs` | 소유자의 pointer/aim 방향을 읽어 projector 조준 표시를 회전·이동 | **연결됨**. Player/Paladin 조준 데칼 |
| `Assets/1.Scripts/Player/PlayerAnimationEventRelay.cs` | Animator animation event를 평타·스킬 컨트롤러로 전달 | **연결됨**. armature animation event와 Controller 사이 어댑터 |
| `Assets/1.Scripts/Player/PlayerColorAssigner.cs` | clientId 기준 Renderer 색상을 배정하는 네트워크 표현 보조 | **부분 연결**. 현재 기준 Player가 아니라 레거시 Paladin에만 연결 |
| `Assets/1.Scripts/Player/PlayerDefaultAttack.cs` | overlap/raycast/projectile step 판정, owner 제외, Hurtbox·Unit 중복 제거 | **연결됨**. DefaultAttackController의 실제 적중 실행기 |
| `Assets/1.Scripts/Player/PlayerInputReader.cs` | Input System callback을 소유자 전용 이동/조준/one-shot action 버퍼로 변환 | **연결됨**. PlayerInput과 FSM·평타·스킬이 소비 |
| `Assets/1.Scripts/Player/PlayerMovement.cs` | Rigidbody 기반 이동과 회전, 상태별 이동 정책 적용 | **연결됨**. Idle/Move/Attack/Skill 상태에서 호출 |
| `Assets/1.Scripts/Player/PlayerRootMotionRelay.cs` | Animator root motion delta를 Player 이동/공격 흐름에 전달 | **연결됨**. armature의 root motion 중계 |
| `Assets/1.Scripts/Player/PlayerStateController.cs` | Player FSM 전체와 상태 context, Idle/Move/Attack/Interrupt/Grabbed/Knockback 구현 | **연결됨**. Player 행동의 중앙 상태 관리자; Dead enum은 구현 전 |

### Player Skill — 20개 (PlayerSkill 부분 갱신)

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Player/Skill/FirstMeleeMainSkill.cs` | Q 진격의 방패; 오너 전진·조향, 서버 overlap 피해·넉백, 시전 중 SuperArmor | **연결됨**. Player prefab Main/Q 슬롯 |
| `Assets/1.Scripts/Player/Skill/FirstMeleeMainSkillData.cs` | Q 전진 속도·조향각·넉백·판정 수를 추가한 `PlayerSkillData` | **연결됨**. cooldown 10초, 피해계수 0.3 자산 |
| `Assets/1.Scripts/Player/Skill/FirstMeleeSubSkill.cs` | Instant E 스킬; 서버에서 실드를 부여하고 지속시간 후 회수 | **연결됨**. Player prefab Sub/E 슬롯 |
| `Assets/1.Scripts/Player/Skill/FirstMeleeSubSkillData.cs` | 실드량·지속시간을 추가한 `PlayerSkillData` | **연결됨**. cooldown 14초, 실드 10/5초 자산 |
| `Assets/1.Scripts/Player/Skill/FirstMeleeUltimateSkill.cs` | R 최후의 심판; SingleTarget 지정 후 채널 완주 시 단일 피해 | **연결됨**. Player prefab Ultimate/R 슬롯; 연출·상세 메커니즘은 후속 |
| `Assets/1.Scripts/Player/Skill/FirstMeleeUltimateSkillData.cs` | R 타겟팅·사거리·채널·피해 설정을 추가한 `PlayerSkillData` | **연결됨**. 사거리 8, 채널 1.5초, 피해계수 3, cooldown 60초 placeholder 자산 |
| `Assets/1.Scripts/Player/Skill/PlayerChannelingSkill.cs` | 채널 시간을 서버에서 추적하고 완료/취소를 관리하는 추상 베이스 | **지원 코드/연결됨**. FirstMeleeUltimateSkill의 부모 |
| `Assets/1.Scripts/Player/Skill/PlayerHoldSkill.cs` | press/update/release 수명주기를 갖는 hold 스킬 추상 베이스 | **지원 코드/현재 구체 스킬 없음** |
| `Assets/1.Scripts/Player/Skill/PlayerInstantSkill.cs` | 시작 후 짧은 active window로 끝나는 instant 스킬 베이스 | **지원 코드**. FirstMeleeSubSkill의 부모 |
| `Assets/1.Scripts/Player/Skill/PlayerSkillBase.cs` | 스킬 서버 수명주기·프레젠테이션·aim·movement/rotation 정책의 최상위 추상 클래스 | **지원 코드**. Controller가 모든 슬롯을 이 타입으로 다룸 |
| `Assets/1.Scripts/Player/Skill/PlayerSkillController.cs` | Q/E/R/RMB 슬롯, cooldown, 서버 검증, aim/release RPC, 활성 스킬 tick의 단일 오케스트레이터 | **연결됨**. Player prefab에 Q/E/R 배선; RMB/Interrupt는 미배정 |
| `Assets/1.Scripts/Player/Skill/PlayerSkillData.cs` | 공통 스킬 수치와 targeting mode·confirm mode·cast range·target layer를 보관하는 SO 베이스 | **지원 코드/연결됨**. Q/E/R 데이터의 부모 |
| `Assets/1.Scripts/Player/Skill/PlayerSkillSlot.cs` | Main/Sub/Ultimate/Interrupt 슬롯 enum | **지원 코드**. InputReader, Controller, HUD 공통 계약 |
| `Assets/1.Scripts/Player/Skill/PlayerSkillState.cs` | 활성 스킬에 이동·회전·종료를 위임하는 Player FSM 상태 | **연결됨**. 스킬 승인 시 StateController가 진입 |
| `Assets/1.Scripts/Player/Skill/Targeting/PlayerSkillTargeting.cs` | 조준 진입·확정·취소, SingleTarget 탐색, 사거리 밖 자동 이동 후 시전을 중계 | **연결됨**. Player prefab의 Controller·AimIndicator·표시 컴포넌트와 연결 |
| `Assets/1.Scripts/Player/Skill/Targeting/SkillConfirmMode.cs` | 조준 확정 입력 방식을 정의하는 enum | **지원 코드**. PlayerSkillData와 타겟팅 흐름의 계약 |
| `Assets/1.Scripts/Player/Skill/Targeting/SkillCursorState.cs` | 기본·조준·유효·무효 커서 상태 enum | **지원 코드**. SkillCursorView의 표시 계약 |
| `Assets/1.Scripts/Player/Skill/Targeting/SkillCursorView.cs` | 조준 상태에 따라 시스템 커서 텍스처를 교체 | **연결됨**. Player prefab에 기본·타겟팅 커서 배선 |
| `Assets/1.Scripts/Player/Skill/Targeting/SkillRangeIndicator.cs` | DecalProjector 기반 시전 사거리와 지면 마커 표시 | **연결됨**. Player prefab의 SkillRangeIndicator 오브젝트와 range decal 연결 |
| `Assets/1.Scripts/Player/Skill/Targeting/SkillTargetingMode.cs` | None·SingleTarget·GroundPoint 타겟팅 방식 enum | **지원 코드**. PlayerSkillData와 PlayerSkillTargeting 공통 계약 |

### Unit와 Weapon — 12개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Unit/Health.cs` | HP·방어·실드와 heal/damage를 보관·계산하는 순수 C# 값 객체 | **지원 코드**. Unit이 서버에서 생성하고 일부 값을 NetworkVariable과 맞춤 |
| `Assets/1.Scripts/Unit/Hurtbox.cs` | 자식 collider를 소유 Unit 또는 부모 `IAttackReceiver`에 연결 | **연결됨**. Player, Bomb 등 실제 피격 진입점 |
| `Assets/1.Scripts/Unit/StatusEffectController.cs` | 서버 권위 `NetworkList` 상태이상, stack/expiry, 스탯 multiplier와 blocker 계산 | **부분 연결**. Player에 붙고 Unit이 읽지만 Apply/Remove 외부 호출자는 0개 |
| `Assets/1.Scripts/Unit/StatusEffectType.cs` | buff/debuff 및 행동 차단 종류 enum | **지원 코드**. StatusEffectInstance의 타입 계약 |
| `Assets/1.Scripts/Unit/Unit.cs` | 공통 공격/이동/속도, HP·실드·방어, RPC, `IAttackReceiver`를 가진 네트워크 전투 기반 | **연결됨**. Player·Enemy·ChargingObject 부모 |
| `Assets/1.Scripts/Unit/Weapon/AttackElement.cs` | 공격 속성 enum | **미연결**. 현재 AttackInfo·피해식에는 포함되지 않음 |
| `Assets/1.Scripts/Unit/Weapon/AttackTriggerRelay.cs` | trigger enter를 서버 공격 판정으로 중계하는 NetworkBehaviour | **미연결**. 주요 프리팹 참조 없음 |
| `Assets/1.Scripts/Unit/Weapon/BaseAttack.cs` | AttackInfo/HitContext, layer/server guard, Hurtbox 우선 전달을 정의하는 공격 베이스 | **지원 코드/연결됨**. 플레이어·보스 공격 공통 부모 |
| `Assets/1.Scripts/Unit/Weapon/IAttackReceiver.cs` | 공격을 수신하는 도메인 인터페이스 | **지원 코드/연결됨**. Unit과 Bomb이 구현 |
| `Assets/1.Scripts/Unit/Weapon/IKnockbackable.cs` | 방향·세기 기반 넉백 수신 인터페이스 | **지원 코드**. Unit이 선택 컴포넌트를 찾지만 현재 부착 구현체는 확인되지 않음 |
| `Assets/1.Scripts/Unit/Weapon/LinearKnockback.cs` | 일정 시간 선형 이동하는 일반 네트워크 넉백 구현 | **미연결**. Player는 자체 FSM 경로를 사용 |
| `Assets/1.Scripts/Unit/Weapon/OverlapAttack.cs` | 지정 영역 overlap으로 AttackInfo를 전달하는 일반 `BaseAttack` 파생체 | **미연결**. 현재 Player/보스는 전용 파생체 사용 |

### Camera — 2개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Camera/CameraTargetSwitcher.cs` | Main/Cinemachine 카메라를 만들고 로컬 Player follow target에 초점, 테스트 target 순환 | **연결됨**. CameraSwitcher prefab과 Player spawn |
| `Assets/1.Scripts/Camera/CameraTestPlayer.cs` | 네트워크 색상·소유 카메라 초점을 확인하는 간단한 테스트 Player | **연결됨/테스트 전용**. DefaultNetworkPrefabs에 등록 |

### Combat UI — 2개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/UI/Combat/CombatHUD.cs` | `Player.LocalPlayerChanged`를 구독해 HUD 하위 요소를 로컬 Player에 bind | **연결됨**. CombatHUD prefab; 최종 스캔 기준 PlayerScene에 배치 |
| `Assets/1.Scripts/UI/Combat/SkillCooldownHUD.cs` | 네 스킬 슬롯의 fill·초 단위 cooldown을 polling 표시 | **연결됨**. CombatHUD prefab; 현재 HUD의 유일한 기능 |

## 2. Enemy·Boss — 24개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Enemy/FloorAreaEffect.cs` | 바닥 폭탄 영역의 merge 방식, 성장·겹침·표현을 관리 | **연결됨**. Bomb prefab의 floor 상태 효과 |
| `Assets/1.Scripts/Enemy/Boss/BaseAttackChoice.cs` | 거리/상태에 따른 보스 공격 선택기의 추상 기반 | **지원 코드**. TwentyThreeBasicAttackChoice 부모 |
| `Assets/1.Scripts/Enemy/Boss/Bomb.cs` | `IAttackReceiver`로 평타를 받아 BombController에 반사/재발사를 지시 | **연결됨**. Bomb prefab과 Hurtbox 부모 receiver |
| `Assets/1.Scripts/Enemy/Boss/BombController.cs` | Hold→Timer→Flight→Floor 서버 FSM, spherecast, ClientRpc 프레젠테이션 | **연결됨**. Wells Graph가 생성·보유·투척 |
| `Assets/1.Scripts/Enemy/Boss/ChargeController.cs` | ChargingObject 시작, 도착·격파 이벤트 집계, 차지 단계 상태 제공 | **부분 연결**. Boss 컴포넌트는 있으나 Arena의 대상 목록 설정이 주석 처리됨 |
| `Assets/1.Scripts/Enemy/Boss/ChargingObject.cs` | `Unit`의 HP/피격을 재사용하고 상승·도착·파괴 이벤트를 내는 차지 목표물 | **부분 연결**. 두 보스 테스트 씬에 4개씩 있으나 Controller 목록과 단절 |
| `Assets/1.Scripts/Enemy/Boss/ColilderBasicAttack.cs` | trigger/animation 제어 가능한 보스 근접 `ColliderBasicAttack : BaseAttack` | **연결됨**. No.23 공격 collider; 파일명에 오탈자 있음 |
| `Assets/1.Scripts/Enemy/Boss/GrabController.cs` | 서버 overlap으로 Player를 잡아 Grabbed FSM과 비율 피해·release를 제어 | **연결됨**. No.23 Grab 경로 |
| `Assets/1.Scripts/Enemy/Boss/JumpController.cs` | 가장 가까운 Player의 착지점 선택, 표식 ClientRpc, 착지 overlap 피해 | **연결됨**. No.23 JumpAttack 경로 |
| `Assets/1.Scripts/Enemy/Boss/KnockbackAttack.cs` | 공격 피해 뒤 `IKnockbackable`에 방향·세기를 추가 전달하는 `BaseAttack` | **연결됨**. 보스 넉백 공격 |
| `Assets/1.Scripts/Enemy/Boss/TriggerKnockbackAttack.cs` | animation/trigger 이벤트를 KnockbackAttack 실행으로 중계 | **연결됨**. 보스 공격 오브젝트 relay |
| `Assets/1.Scripts/Enemy/Boss/TwentyThreeAnimEvents.cs` | No.23 Animator event를 Grab/Jump 컨트롤러에 전달 | **부분 검증**. outer prefab override 흔적은 있으나 원본 nested model prefab GUID가 저장소에 없음 |
| `Assets/1.Scripts/Enemy/Boss/WeightedAttack.cs` | enum 공격 값과 weight를 묶는 제네릭 직렬화 struct | **지원 코드**. TwentyThreeBasicAttackChoice의 후보 데이터 |
| `Assets/1.Scripts/Enemy/Boss/Wells&No.23/TwentyThreeArenaContext.cs` | 서버에서 보스 NetworkObject를 생성하고 BT를 여는 arena bootstrap | **연결됨**. PlayerBossTest 보스 진입점; charge 목록 배선은 미완성 |
| `Assets/1.Scripts/Enemy/Boss/Wells&No.23/TwentyThreeBasicAttackChoice.cs` | 거리 구간·가중치로 No.23 기본 공격 enum을 선택 | **연결됨**. No.23 Behavior Graph 커스텀 노드/컴포넌트 |
| `Assets/1.Scripts/Enemy/Boss/Wells&No.23/TwentyThreeBasicAttackType.cs` | `None/Hook/Upper/Grab/Jump/Dash` 기본 공격 선택 enum | **지원 코드/연결됨**. 좌우 Hook은 이후 target 방향으로 분기 |
| `Assets/1.Scripts/Enemy/Boss/Wells&No.23/TwentyThreeState.cs` | Idle, Walk, 공격, Grab/Hold/Throw, Charge/Rage/Groggy/Dead 보스 상태 enum | **지원 코드/연결됨**. Graph와 BossStateChanged 이벤트 계약 |
| `Assets/1.Scripts/Enemy/Boss/Wells&No.23/WellsState.cs` | `Normal/Throw/Dead` Wells 상태 enum | **지원 코드/연결됨**. Wells Graph blackboard 계약 |
| `Assets/1.Scripts/Enemy/DistanceState.cs` | 대상과의 거리 구간 enum | **지원 코드/연결됨**. 보스/몬스터 Graph 분기 |
| `Assets/1.Scripts/Enemy/Enemy.cs` | `Unit` 파생 AI 전투 루트, 서버 초기화와 Graph blackboard 속도 주입 | **연결됨**. ModularRobot과 TwentyThree 계열 |
| `Assets/1.Scripts/Enemy/EnemyBTActivator.cs` | 복수 BehaviorGraphAgent의 `IsOpen` blackboard 값을 열어 실행 시작 | **연결됨**. No.23와 Wells Graph 활성화 |
| `Assets/1.Scripts/Enemy/JumpState.cs` | 보스 점프 과정 상태 enum | **지원 코드/연결됨**. JumpController/Graph 계약 |
| `Assets/1.Scripts/Enemy/RunningOnlyOnServer.cs` | 서버에서만 BehaviorGraphAgent와 NavMeshAgent를 활성화 | **연결됨**. 모든 주요 AI prefab의 권위 게이트 |
| `Assets/1.Scripts/Enemy/TrashMobState.cs` | 일반 몬스터 상태 enum | **지원 코드/연결됨**. CommonMeleeRobot Graph blackboard |

## 3. Map — 27개

### Runtime·Data — 19개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Map/GeneratedNodeData.cs` | 생성 노드의 좌표·tier·content를 NGO 직렬화하는 레거시 struct | **지원/레거시**. 현재 ZoneSlot v2의 중심 데이터는 아님 |
| `Assets/1.Scripts/Map/KeySystem.cs` | key 보유·획득 여부를 관리하는 독립 MonoBehaviour | **미연결**. 호출자와 주요 에셋 참조 없음 |
| `Assets/1.Scripts/Map/LayoutPlacer.cs` | 정렬된 ZoneSlot마다 catalog에서 layout을 결정론 선택·인스턴스화 | **부분 연결**. 코드 완성, MapGenerator 필드와 catalog가 현재 비어 있음 |
| `Assets/1.Scripts/Map/MapContentSpawner.cs` | layout의 로컬 비주얼과 서버 권위 몬스터 NetworkObject를 분리 생성 | **부분 연결**. v2 입력 부재; 생성 root가 active Lobby scene에 들어가 unload 때 제거될 위험 |
| `Assets/1.Scripts/Map/MapCorridors.cs` | corridor 연결과 구간 데이터를 계산하는 정적 레거시 도우미 | **지원/레거시**. 현재 고정 ZoneSlot v2 주 흐름 밖 |
| `Assets/1.Scripts/Map/MapEnums.cs` | zone grade/type/role, tier/content, behavior, difficulty, size 등 맵 도메인 enum 집합 | **지원 코드/연결됨**. 모든 맵 SO와 생성 단계의 공통 계약 |
| `Assets/1.Scripts/Map/MapGenConfigSO.cs` | exclusion radius와 난이도별 MonsterGroup 배치 규칙을 담는 생성 설정 SO | **부분 연결**. MapScene 참조는 유효하나 현재 두 monster prefab이 모두 null |
| `Assets/1.Scripts/Map/MapGenerator.cs` | seed/difficulty를 받아 slot 수집·역할 할당·layout/content/validation을 조정 | **부분 연결**. config/catalog는 있으나 LayoutPlacer 참조가 없음 |
| `Assets/1.Scripts/Map/MapNetworkSync.cs` | 서버가 seed·difficulty·ready NetworkVariable을 정하고 각 피어 Generate 호출 | **연결됨/하위 배선 미완성**. MapScene 네트워크 진입점 |
| `Assets/1.Scripts/Map/MapOverviewUI.cs` | M키로 runtime Canvas를 만들고 role별 zone 개요를 표시 | **연결됨/현재 빈 결과**. MapScene에 있으나 ZoneSlot 0개 |
| `Assets/1.Scripts/Map/MapPrefabCatalogSO.cs` | 레거시 node/prop/geometry와 overview icon prefab catalog | **연결됨/레거시 혼합**. v2 runtime에서는 주로 icon만 소비 |
| `Assets/1.Scripts/Map/MapValidator.cs` | 맵 경로를 검증하려는 stub | **미구현**. MapScene에 component는 있으나 호출자 0, 현재 항상 true |
| `Assets/1.Scripts/Map/MonsterGroupData.cs` | 몬스터 prefab과 수량/행동을 묶는 직렬화 struct | **지원 코드/연결됨**. ZoneDefinition·content spawn 데이터 |
| `Assets/1.Scripts/Map/SpawnPoint.cs` | 역할/종류를 가진 scene 위치 마커 | **연결됨**. Stage1에 93개, 레거시와 v2 콘텐츠 양쪽 보조 |
| `Assets/1.Scripts/Map/ZoneDefinitionSO.cs` | zone의 grade/type/clear condition/monster group/보상 데이터 SO | **연결됨**. Stage1 관련 zone 자산과 catalog |
| `Assets/1.Scripts/Map/ZoneLayout.cs` | 하나의 교체 가능한 zone layout root와 size/content 참조 | **구현됨/현재 catalog 미작성**. v2 필수 prefab 컴포넌트 |
| `Assets/1.Scripts/Map/ZoneLayoutCatalogSO.cs` | role/size별 ZoneLayout 후보 entry 목록 SO | **구현됨/미연결**. 현재 직렬화 catalog 자산·LayoutPlacer 참조 없음 |
| `Assets/1.Scripts/Map/ZoneSlot.cs` | 고정 씬 슬롯의 index·role·size·anchor를 표현 | **구현됨/Stage1 미전환**. 현재 Stage1에는 0개 |
| `Assets/1.Scripts/Map/ZoneVolume.cs` | 구역 bounds와 정의를 가진 기존 볼륨 컴포넌트 | **연결됨/레거시**. Stage1에 10개 |

### Editor 제작 도구 — 8개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Map/Editor/MapArtCollector.cs` | 씬/선택 오브젝트의 맵 아트 계층을 수집·정돈 | **Editor 도구**. MapDevTools 메뉴에서 호출 |
| `Assets/1.Scripts/Map/Editor/MapCatalogPopulator.cs` | 프로젝트 자산을 검색해 맵 prefab/layout catalog를 자동 채움 | **Editor 도구**. catalog 제작 파이프라인 |
| `Assets/1.Scripts/Map/Editor/MapDevTools.cs` | 맵 setup·geometry·scatter·catalog 작업을 노출하는 메뉴 진입점 | **Editor 도구/연결됨** |
| `Assets/1.Scripts/Map/Editor/MapEditorPaths.cs` | 맵 씬·prefab·SO 폴더 경로 상수 | **Editor 지원 코드**. 다른 Map Editor 도구가 공유 |
| `Assets/1.Scripts/Map/Editor/MapGeometryBuilder.cs` | zone rect에서 바닥·벽 등 개발용 geometry를 생성 | **Editor 도구**. `ZRect` 내부 struct 포함 |
| `Assets/1.Scripts/Map/Editor/MapSceneSetup.cs` | MapScene에 generator/sync/spawner/validator 기본 구성을 생성·배선 | **Editor 도구**. 현재 부분 배선의 생성기 |
| `Assets/1.Scripts/Map/Editor/MapSlotSetup.cs` | ZoneSlot을 생성·index 정렬하고 layout anchor를 구성 | **Editor 도구**. Stage1 v2 전환에 필요 |
| `Assets/1.Scripts/Map/Editor/MapSpawnPointScatter.cs` | zone bounds 안에 spawn marker를 규칙적으로 산포 | **Editor 도구**. Stage1 콘텐츠 위치 제작 |

## 4. Network·Lobby·Loading·Scene Flow — 11개

### Network — 3개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Network/BaseNetworkBehaviour.cs` | NGO 활성 여부와 오프라인 포함 상태 권위(`HasStateAuthority`)를 추상화 | **지원 코드/연결됨**. Unit·Input·평타·스킬·상태 부모 |
| `Assets/1.Scripts/Network/ForProfile.cs` | OnGUI 버튼으로 Host를 시작하는 간단한 profiler/test bootstrap | **연결됨/테스트 전용**. PlayerScene과 PlayerBossTest |
| `Assets/1.Scripts/Network/NetworkSessionLauncher.cs` | IP를 transport에 설정하고 Host/Client/Server 시작·종료 callback 등록 | **연결됨/부분 UI 손상**. NetworkManager prefab; Lobby IP 선택 버튼 5개 target은 `fileID: 0` |

### Loading — 3개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs` | additive Loading/Target 씬, custom progress/state messages, 서버 집계, PlayerObject 수동 spawn, source unload를 조정 | **연결됨/씬 등록 필요**. 코드 기본은 Temp_inGameScene, prefab override는 MapScene; active scene 전환 누락 위험 |
| `Assets/1.Scripts/Loading/NetworkLoadingPhase.cs` | 네트워크 로딩 단계를 표현하는 byte enum | **지원 코드/연결됨**. FlowController와 View 계약 |
| `Assets/1.Scripts/Loading/NetworkLoadingScreenView.cs` | phase·progress·상태 문구를 표시하는 로딩 UI | **연결됨**. LoadingScene 표현 계층 |

### Lobby — 2개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Lobby/LobbyPlayerSlotView.cs` | 한 로비 슬롯의 이름·연결·ready 표시를 갱신 | **연결됨**. 3인 슬롯 UI |
| `Assets/1.Scripts/Lobby/LobbyUIController.cs` | ReadyRequest/ReadyState custom message, 서버 clientId dictionary, 전원 준비 로딩 gate | **연결됨**. Temp_LobbyScene과 NetworkLoadingFlowController |

### Scene·SceneFlow — 3개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Scene/KMKScene.cs` | NetworkBehaviour 기반의 소형 씬 실험 코드 | **미연결/실험**. 주요 실행 흐름 참조 없음 |
| `Assets/1.Scripts/SceneFlow/TitleOptionsPanel.cs` | 옵션 panel 열기·닫기, 최초 UI 선택과 navigation focus 관리 | **연결됨**. TitleScene UI |
| `Assets/1.Scripts/SceneFlow/TitleSceneManager.cs` | 타이틀 시작/종료와 Temp_LobbyScene 전환을 관리 | **연결됨/Build Settings 미등록**. 일반 세션 시작점 |

## 5. Utility — 8개

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/Utility/BitMaskHelper.cs` | enum flag의 set/clear/test를 제공하는 제네릭 비트마스크 도우미 | **지원 코드**. 런타임 데이터 연산용 |
| `Assets/1.Scripts/Utility/ColliderInfo.cs` | Box/Capsule/Sphere collider 정보를 공통 형식으로 보관·overlap 계산 | **연결됨**. 보스 공격과 BT 물리 노드 |
| `Assets/1.Scripts/Utility/Edit.cs` | `[Conditional("UNITY_EDITOR")]` 로그와 편집기 보조를 감싼 정적 도우미 | **지원 코드**. Player build에서는 호출 자체가 제거됨 |
| `Assets/1.Scripts/Utility/Editor/BossAreaSubgraphExampleBuilder.cs` | BossArea Behavior Graph/subgraph 예제 자산을 생성 | **Editor 도구**. 메뉴 기반 제작 지원 |
| `Assets/1.Scripts/Utility/EnableCollider.cs` | Box/Capsule/Sphere/Mesh collider를 서버 gate로 enable/disable | **연결됨/비복제**. Robot·TwentyThree와 BT Action; 서버 변화가 클라이언트에 동기화되지 않음 |
| `Assets/1.Scripts/Utility/Math/ColliderMathUtility.cs` | collider의 월드 중심·반경·half extents·방향을 계산하는 정적 수학 도우미 | **지원 코드/연결됨**. ColliderInfo와 공격 판정 |
| `Assets/1.Scripts/Utility/SpawnPointer.cs` | AI spawn 위치·방향을 보관하는 scene marker | **연결됨**. Robot·TwentyThree prefab과 GetSpawnPointAction |

## 6. 프로젝트 전용 Editor 도구 — 3개

이 세 파일은 Git/rg 기본 검색에서 ignore되어 있었지만 `Assets` 파일 시스템과 Unity Editor compile 범위에 실제로 존재한다. 전수 감사에서는 제외하지 않았다.

| 파일 | 선언/역할 | 상태와 핵심 연결 |
| --- | --- | --- |
| `Assets/Editor/BoundsFixerEditor.cs` | `BoundsFixerWindow`; 여러 prefab의 모든 SkinnedMeshRenderer localBounds extents를 X/Y/Z 배율로 일괄 확장 | **로컬 Editor 도구**. `Tools/Bounds Fixer Window`, Undo/dirty/save 지원 |
| `Assets/Editor/FileExtensions.cs` | `FileExtensionGUI`; Project 창 list view 항목 옆에 파일 확장자를 그림 | **로컬 Editor 통합**. `[InitializeOnLoad]`, UnityEditor 내부 field/property reflection 의존 |
| `Assets/Editor/FolderStructureGenerator.cs` | 캐릭터명 아래 `1.Scripts/2.Prefabs/3.Materials/4.Animations` 폴더를 생성 | **로컬 Editor 도구**. `Tools/Generate Project Folders`; 일부 UI 문자열 인코딩 손상 |

## 7. Behavior Tree/Graph 확장 — 49개

Behavior Graph 자산은 C# GUID가 아니라 직렬화된 타입 문자열로 노드를 참조하는 경우가 많다. 아래 “Graph 연결”은 `Assets/8.BehaviorTreeGraph`의 실제 자산 본문에서 해당 타입명이 확인되었다는 뜻이다.

### Animation·공통 계산 — 3개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Actions/Animation/GetAnimClipPlayTimeAction.cs` | Animator의 지정 clip 재생 길이를 blackboard 숫자로 반환 | **Graph 연결됨**: No.23, CommonMeleeRobot |
| `Assets/1.Scripts/BT/Actions/Animation/SetAnimtorEnumAction.cs` | enum 값을 Animator integer state parameter로 설정 | **Graph 연결됨**: No.23, CommonMeleeRobot; 타입명 오탈자 유지 |
| `Assets/1.Scripts/BT/Actions/CalculateDistanceAction.cs` | 두 Transform 거리와 거리 상태를 계산해 blackboard에 기록 | **Graph 연결됨**: No.23, CommonMeleeRobot |

### Attack·Boss 기믹 — 6개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Actions/Attack/AddRandomAttackAction.cs` | 공격 선택 후보/가중치 집합에 무작위 공격을 추가 | **Graph 연결됨**: No.23 BasicAttack Timer, No.23 |
| `Assets/1.Scripts/BT/Actions/Attack/GetRamdomAttackTypeAction.cs` | TwentyThreeBasicAttackChoice 결과를 공격 enum blackboard에 기록 | **Graph 연결됨**: No.23; Random 오탈자 유지 |
| `Assets/1.Scripts/BT/Actions/Attack/HoldBombAction.cs` | Wells가 BombController의 폭탄을 hold socket에 보유하도록 지시 | **Graph 연결됨**: Wells |
| `Assets/1.Scripts/BT/Actions/Attack/RemoveRandomAttackAction.cs` | 사용한/제외할 공격을 후보 집합에서 제거 | **Graph 연결됨**: No.23 |
| `Assets/1.Scripts/BT/Actions/Attack/SetChargingStateAction.cs` | ChargeController/보스 차지 상태를 설정 | **Graph 연결됨**: No.23; 현재 charge 대상 배선은 미완성 |
| `Assets/1.Scripts/BT/Actions/Attack/ThrowBombAction.cs` | Wells가 보유 Bomb을 목표 방향으로 발사 | **Graph 연결됨**: Wells |

### Collider·GameObject·수학 — 6개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Actions/Enable/SetEnableBoxColliderAction.cs` | 대상 BoxCollider 활성 상태를 설정 | **Graph 연결됨**: BossArea, MonsterArea, No.23 |
| `Assets/1.Scripts/BT/Actions/EnableColldierAction.cs` | 지정 collider/EnableCollider 래퍼의 활성 상태를 변경 | **Graph 연결됨**: No.23, CommonMeleeRobot; Collider 오탈자 유지 |
| `Assets/1.Scripts/BT/Actions/GameObject/InstantiateNetworkObjectAction.cs` | 이름과 달리 이미 만들어진 NetworkObject에 서버 `Spawn()`만 호출 | **Graph 연결됨**: Wells; Bomb 생성 흐름 일부 |
| `Assets/1.Scripts/BT/Actions/Math/PlusFloatAction.cs` | blackboard float에 값을 더한 뒤 계속 Running을 반환 | **미연결**. 현재 Graph 타입 참조 없음; 연결 시 종료 경로 주의 |
| `Assets/1.Scripts/BT/Actions/Math/PlusIntAction.cs` | blackboard int에 값을 더함 | **Graph 연결됨**: BossArea, No.23 |
| `Assets/1.Scripts/BT/Actions/Physics/CheckCollisionInBoxAction.cs` | box 영역의 충돌 여부를 Action 성공/실패로 반환 | **미연결**. 현재 Graph 타입 참조 없음 |

### NavMesh — 4개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Actions/NavMesh/MoveTowardDirectionAction.cs` | NavMeshAgent를 찾지만 실제 이동 없이 Running을 반환하는 미완성 Action | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/NavMesh/ResetPathAction.cs` | NavMeshAgent의 현재 경로를 초기화 | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/NavMesh/ResetVelocityAction.cs` | NavMeshAgent velocity를 초기화 | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/NavMesh/SetAgentDashModeAction.cs` | dash 중 NavMeshAgent 이동 파라미터/모드를 전환 | **Graph 연결됨**: No.23 |

### 제어 반환 — 3개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Actions/Return/ReturnFailAction.cs` | 즉시 Failure를 반환하는 명시적 제어 노드 | **Graph 연결됨**: BossArea, MonsterArea, No.23 |
| `Assets/1.Scripts/BT/Actions/Return/ReturnRunningAction.cs` | Running을 유지하는 명시적 제어 노드 | **Graph 연결됨**: No.23 |
| `Assets/1.Scripts/BT/Actions/Return/ReturnSuccessAction.cs` | 즉시 Success를 반환하는 명시적 제어 노드 | **Graph 연결됨**: No.23 |

### 서버 Animator 변형 — 8개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Actions/Server/ServerResetTriggerIntAction.cs` | 서버에서 integer hash로 Animator trigger reset | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/Server/ServerResetTriggerStringAction.cs` | 서버에서 string 이름으로 Animator trigger reset | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/Server/ServerSetAnimIntegerIntEnumAction.cs` | int hash parameter에 enum 값을 서버에서 설정 | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/Server/ServerSetAnimIntegerIntIntAction.cs` | int hash parameter에 int 값을 서버에서 설정 | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/Server/ServerSetAnimIntegerStringEnumAction.cs` | string parameter에 enum 값을 서버에서 설정 | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/Server/ServerSetAnimIntegerStringIntAction.cs` | string parameter에 int 값을 서버에서 설정 | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/Server/ServerSetTriggerIntAction.cs` | int hash Animator trigger를 서버에서 설정 | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Actions/Server/ServerSetTriggerStringAction.cs` | string Animator trigger를 서버에서 설정 | **미연결**. 현재 Graph 타입 참조 없음 |

### Blackboard·Timer — 2개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Actions/SetNumberWithTagAction.cs` | 지정 tag의 GameObject 수를 세어 blackboard 숫자에 설정 | **Graph 연결됨**: No.23의 플레이어 수 초기화 |
| `Assets/1.Scripts/BT/Actions/Timer/AddDeltaTimeAction.cs` | blackboard 시간 누적값에 `deltaTime`을 더함 | **Graph 연결됨**: No.23 BasicAttack Timer, No.23 |

### Transform·공격 이동 — 6개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Actions/Transform/GetSpawnPointAction.cs` | SpawnPointer/대상에서 spawn 위치 Transform을 취득 | **Graph 연결됨**: No.23, CommonMeleeRobot |
| `Assets/1.Scripts/BT/Actions/Transform/KnockbackAttackAction.cs` | 지정 KnockbackAttack을 실행해 피해·넉백 판정 | **Graph 연결됨**: No.23 |
| `Assets/1.Scripts/BT/Actions/Transform/LookAtRotateAction.cs` | 일정 속도로 목표를 바라보도록 회전 | **Graph 연결됨**: No.23 |
| `Assets/1.Scripts/BT/Actions/Transform/MoveForDurationAction.cs` | 지정 방향·속도로 정해진 시간 Transform 이동 | **Graph 연결됨**: No.23 |
| `Assets/1.Scripts/BT/Actions/Transform/SetPositionThroughRaycastAction.cs` | raycast 결과 지점을 Transform/blackboard 위치로 설정 | **Graph 연결됨**: No.23 |
| `Assets/1.Scripts/BT/Actions/Transform/SetPositionToTargetAction.cs` | 한 Transform을 target 위치로 직접 이동 | **미연결**. 현재 Graph 타입 참조 없음 |

### Unit 조작 — 2개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Actions/Unit/IncreaseUnitHpAction.cs` | 대상 Unit HP를 서버에서 회복 | **Graph 연결됨**: No.23 |
| `Assets/1.Scripts/BT/Actions/Unit/IncreaseUnitShieldAction.cs` | 대상 Unit 실드를 서버에서 증가/설정 | **Graph 연결됨**: No.23 |

### Conditions — 6개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Conditions/CheckArrivalInChargeControllerCondition.cs` | ChargeController 목표의 도착 완료 여부 검사 | **Graph 연결됨**: No.23; 현재 대상 목록 미배선 |
| `Assets/1.Scripts/BT/Conditions/CheckCollisionInBoxCondition.cs` | box 영역에 목표 collider가 있는지 조건으로 검사 | **미연결**. 현재 Graph 타입 참조 없음 |
| `Assets/1.Scripts/BT/Conditions/CheckDefeatInChargeControllerCondition.cs` | ChargeController 목표 격파 여부 검사 | **Graph 연결됨**: No.23; 현재 대상 목록 미배선 |
| `Assets/1.Scripts/BT/Conditions/CheckHealthPercentCondition.cs` | Unit 현재 HP 비율을 threshold와 비교 | **Graph 연결됨**: No.23, CommonMeleeRobot |
| `Assets/1.Scripts/BT/Conditions/IsCurrentAnimStateEqualTooStateNameCondition.cs` | 현재 Animator state 이름과 기대값을 비교 | **미연결**. 현재 Graph 타입 참조 없음; 타입명 문법 오탈자 유지 |
| `Assets/1.Scripts/BT/Conditions/IsTargetOnRightSideCondition.cs` | 목표가 owner 오른쪽에 있는지 내적으로 판정 | **Graph 연결됨**: No.23 |

### Event·네트워크 Animator 컴포넌트 — 3개

| 파일 | 선언/역할 | 상태와 Graph 연결 |
| --- | --- | --- |
| `Assets/1.Scripts/BT/Events/BossStateChanged.cs` | `EventChannel<TwentyThreeState>`로 보스 상태 변경을 Graph 사이에 전달 | **Graph 연결됨**: Boss State Changed, No.23, Wells |
| `Assets/1.Scripts/BT/NetworkSetAnimState.cs` | Animator 상태를 네트워크 표현으로 설정하는 MonoBehaviour 보조 | **미연결**. 주요 prefab/Graph 참조 없음 |
| `Assets/1.Scripts/BT/ServerSetAnimState.cs` | 서버에서 Animator parameter를 직접 바꾸는 NetworkBehaviour API | **부분/복제 불확실**. No.23 blackboard·누락 nested model 흔적은 있으나 호출 BT Action 8개는 모두 휴면, 자체 RPC 없음 |

## 8. 외부·데모 코드 — 27개

### Unity Starter Assets — 2개

| 파일 | 역할 | 상태 |
| --- | --- | --- |
| `Assets/INab Studio/Demo Assets/Unity Companion License/StarterAssets/FirstPersonController/Scripts/BasicRigidBodyPush.cs` | First Person Controller가 접촉 Rigidbody를 미는 Unity 샘플 | **외부/데모**. 본 게임 Player FSM과 무관 |
| `Assets/INab Studio/Demo Assets/Unity Companion License/StarterAssets/FirstPersonController/Scripts/FirstPersonController.cs` | Starter Assets 1인칭 이동·시점·점프 컨트롤러 | **외부/데모**. INab 데모 씬용 |

### INab Character Effects — 4개

| 파일 | 역할 | 상태 |
| --- | --- | --- |
| `Assets/INab Studio/Vfx Assets/Character Effects/Core/Scripts/CharacterEffect.cs` | UniformMeshSample 기반 캐릭터 VFX의 재생·속성을 제어 | **외부/데모 코어**. vendor 데모 씬에서만 연결 |
| `Assets/INab Studio/Vfx Assets/Character Effects/Core/Scripts/Editor/CharacterEffectEditor.cs` | CharacterEffect custom inspector | **외부 Editor** |
| `Assets/INab Studio/Vfx Assets/Character Effects/Demo Files/CharacterEffectAPIShowcase.cs` | CharacterEffect public API 시연 | **외부/데모** |
| `Assets/INab Studio/Vfx Assets/Character Effects/Demo Files/ShowcaseSpawnerCharacterEffect.cs` | 데모 캐릭터 이펙트 prefab 순환 생성 | **외부/데모** |

### INab Common·Uniform Mesh — 5개

| 파일 | 역할 | 상태 |
| --- | --- | --- |
| `Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs` | INab 에디터 GUI·asset 경로 공통 도우미 | **외부 Editor** |
| `Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/Editor/UniformMeshSampleEditor.cs` | UniformMeshSample custom inspector/베이크 버튼 | **외부 Editor** |
| `Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/UniformMeshBaking.cs` | VFX 샘플링용 균일 분포 mesh 데이터를 베이크 | **외부 런타임/제작 지원** |
| `Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/UniformMeshSample.cs` | ExecuteAlways effect 수명주기와 mesh baker/VFX binder를 조정하는 추상 기반 | **외부/데모 코어**. runtime 폴더의 무조건 `using UnityEditor`로 Player build 위험 |
| `Assets/INab Studio/Vfx Assets/Common/Utilities/VFXLossyTransformBinder.cs` | Transform의 world position/euler/lossy scale을 VFX property에 bind | **외부 런타임/게임 연결됨**. Player sword/shield trail과 Paladin armature 포함 다수 자산 |

### INab Weapon Trails — 10개

| 파일 | 역할 | 상태 |
| --- | --- | --- |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/Editor/TrailTransformEditor.cs` | TrailTransform custom inspector | **외부 Editor** |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/Editor/WeaponTrailEffectEditor.cs` | WeaponTrailEffect custom inspector와 animation event 편집 | **외부 Editor** |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/TrailTransform.cs` | 무기 trail의 start/end sample Transform과 폭을 제공 | **외부 런타임/연결됨**. Player armature 무기 표현 |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/WeaponTrailEffect.cs` | trail mesh/VFX 재생과 AnimationClip event 주입을 관리 | **외부 런타임/연결됨**. Player 검/방패에 2개; 공유 clip 이벤트 누적 위험 |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/API Examples/TrailAnimationEventsShowcase.cs` | trail animation event 사용 예제 | **외부/데모** |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/API Examples/TrailAPIShowcase.cs` | trail API 직접 호출 예제 | **외부/데모** |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/RotateAroundAxisTrail.cs` | 데모 오브젝트 축 회전 | **외부/데모** |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/RuntimeAnimatorPlayer.cs` | 데모에서 runtime Animator clip을 선택·재생 | **외부/데모** |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseAutoPlay.cs` | 쇼케이스 항목 자동 순환 | **외부/데모** |
| `Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs` | trail showcase prefab 생성·전환 | **외부/데모** |

### ScansFactory Warehouse — 4개

| 파일 | 역할 | 상태 |
| --- | --- | --- |
| `Assets/ScansFactory/Warehouse/Common/Scripts/Elevator/SF_ElevatorMoving.cs` | Warehouse 샘플 엘리베이터 이동 | **외부/미연결**. 직렬화 참조 0 |
| `Assets/ScansFactory/Warehouse/Common/Scripts/Elevator/SF_ParentElevator.cs` | 탑승 오브젝트를 엘리베이터에 parent 처리 | **외부/미연결**. 직렬화 참조 0, 원래 parent를 복원하지 않음 |
| `Assets/ScansFactory/Warehouse/Common/Scripts/Elevator/SF_TriggerElevator.cs` | trigger로 엘리베이터 이동 시작 | **외부/미연결**. 직렬화 참조 0 |
| `Assets/ScansFactory/Warehouse/Common/Scripts/Player/SF_FPSController.cs` | Warehouse 데모 1인칭 컨트롤러 | **외부/미연결**. 직렬화 참조 0 |

### Unity TutorialInfo — 2개

| 파일 | 역할 | 상태 |
| --- | --- | --- |
| `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs` | Unity template Readme custom inspector | **외부 Editor/미연결**. Readme asset 참조 0 |
| `Assets/TutorialInfo/Scripts/Readme.cs` | 튜토리얼 Readme 데이터 ScriptableObject | **외부/미연결**. 자산 참조 0 |

## 9. 전수성 확인

- `Assets/1.Scripts`: **159/159** 파일을 위 1~5절과 7절에 각각 한 번씩 기록.
- `Assets/Editor`: **3/3** 파일을 6절에 각각 한 번씩 기록.
- 외부·데모: **27/27** 파일을 8절에 각각 한 번씩 기록.
- 전체: **189/189**, 의도적 제외 없음.
- C# 파일명에 `Test`가 들어간 것은 `CameraTestPlayer.cs` 하나이며, Unity Test Framework용 EditMode/PlayMode 테스트 어셈블리·테스트 스크립트는 확인되지 않았다.

## 10. 사용 상태를 해석할 때의 주의

1. 추상 클래스, enum, struct, interface는 prefab에 직접 붙지 않아도 **지원 코드**로 실제 사용될 수 있다.
2. Behavior Graph 노드는 일반 `m_Script` 참조가 아니라 타입 문자열로 직렬화될 수 있다.
3. `[McpToolProvider]` 처럼 attribute/reflection으로 발견되는 코드는 직접 호출자가 없어도 활성이다. 해당 MCP 도구들은 이 레포를 떠나 MCP 패키지로 옮겼다.
4. `WeaponTrailEffect`처럼 외부 패키지 코드도 Player 시각 prefab에 붙으면 실제 런타임 경로다.
5. “연결됨”은 기능 완성을 뜻하지 않는다. Map의 하위 배선, Charge 목록, Build Settings처럼 필요한 주변 연결이 빠진 경우는 **부분 연결**로 따로 표시했다.
