# 튜토리얼 판정용 플레이어 이벤트 훅 (할 일 15)

튜토리얼 개인 체크리스트가 구독하는 **공개 이벤트만** 모아 둔다. 훅 자체는 기존 동작을 바꾸지 않고,
튜토리얼 로직은 여기 없다(본류 브랜치 담당).

- **static 이벤트**는 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 로 Play 진입 때 비운다.
  도메인 리로드를 끈 Play 진입(`EnterPlayModeOptions`) 대비다. 지금 프로젝트 설정은 도메인 리로드가 켜져 있다
  (`m_EnterPlayModeOptions: 0`). 구독자는 기존 static 이벤트(`Player.LocalPlayerChanged`)처럼
  `OnEnable`/`OnDisable` 에서 짝을 맞춰 붙이고 뗀다.
- **"서버"** 이벤트는 호스트에서 난다. 오프라인(비네트워크) 실행은 표에 따로 적었다.
- 서버 이벤트로 **클라이언트 화면의 체크리스트**를 갱신하려면 본류에서 따로 복제(RPC·NetworkVariable)해야 한다.

## 훅 표

| 이벤트 | 선언 위치 | 언제 발생 | 피어 | 인자 | 비고 |
|---|---|---|---|---|---|
| `PlayerDashController.ServerDashApproved` (static) | `Assets/1.Scripts/Player/PlayerDashController.cs` | `SubmitDashRequestServerRpc` 에서 검증이 **승인되고 실제로 진행될 때**(승인 ∧ 서버 상태로 중단 아님 ∧ 남은시간 > 0). 서버 무적 토큰 부여 바로 뒤 | 서버 | `Player` 대시한 플레이어 | 거부·승인 후 중단·남은시간 0 은 안 남. **일반 대시와 대시 캔슬(공격·스킬을 끊는 대시, `cancelsAction`)은 같은 요청 RPC 라 구분하지 않는다**(서버로 그 플래그를 보내지 않는다). 어쌔신 Q 돌진 같은 "대시형 스킬"은 대시가 아니라 스킬이다(→ `ServerSkillUsed`). 오프라인(비스폰)은 서버 경로가 없어 안 남 |
| `SkillTooltipView.TooltipShown` (static) | `Assets/1.Scripts/UI/Combat/SkillTooltipView.cs` | 호버 뒤 표시 지연(`showDelay`)이 지나 툴팁이 **실제로 뜰 때**(`visible` false→true) | 오너 로컬 | `string` displayKey | displayKey = 그 슬롯의 키 표기. Q·E·R·우클릭 = `"Q"`·`"E"`·`"R"`·`"RMB"`(`SkillCooldownHUD.KeyLabel`), **패시브 칸 = `"Passive"`**(`PassiveHUD`) |
| `SkillTooltipView.TooltipHidden` (static) | 위와 같음 | 떠 있던 툴팁이 닫힐 때(`visible` true→false): 호버 종료(`EndHover`), 다른 슬롯으로 바로 이동(`BeginHover`), 출처가 사라짐(`RefreshHover` source null) | 오너 로컬 | 없음 | 슬롯 비활성화도 `SkillSlotHover.OnDisable → EndHover` 를 타서 난다. "P 칸 툴팁 1초 유지" = `TooltipShown("Passive")` 뒤 1초 안에 `TooltipHidden` 이 안 오면 성공 |
| `PlayerInterruptSkillBase.ServerInterruptResolved` (static) | `Assets/1.Scripts/Player/Skill/PlayerInterruptSkillBase.cs` | 간파(우클릭) 판정 1회가 끝날 때. 기존 `OnInterruptResolutionCompleted` 의 **두 호출 지점** 각각 바로 뒤(`CompleteBeforeAttackLanded` 에 따라 `RaiseServerAttackLanded` 앞/뒤) | 서버 (오프라인은 로컬) | `Player` 시전자, `int` hitCount | hitCount = 수신이 받아들여진 대상 수. **0 = 빗나감**. 상자 같은 비 Unit 수신자도 센다. 판정 앵커 누락으로 판정 자체를 못 하면 안 남 |
| `PlayerInterruptSkillBase.ServerInterruptSucceeded` (static) | 위와 같음 | 간파 판정 1회에서 **열린 카운터 창을 소비한 대상이 하나라도 있을 때**, 시전당 1회(할 일 26). 시전자 쿨 감소를 적용한 **뒤**, `ServerInterruptResolved` 보다 **먼저** | 서버 (오프라인은 로컬) | `Player` 시전자, `PlayerInterruptSkillBase` 그 간파 스킬(대체 슬롯 포함) | 성공 원천 = 수신 호출 동안 동기로 난 `CombatStatsEvents.ServerCounterSucceeded`(23호·중간보스 3종·튜토리얼 스피너) + 허수아비 `IInterruptSlowMotionTarget.ServerInterruptSuccessCount` 증가. clientId 매핑 없이 시전 단위로 묶는다. 쿨 조작 API: `PlayerSkillController.ReduceCooldownServer(skill|slot, seconds)`·`ResetCooldownServer(skill|slot)`(서버 권위, 원격 오너 HUD 장부에 미러) |
| `PlayerSkillController.ServerSkillUsed` (static) | `Assets/1.Scripts/Player/Skill/PlayerSkillController.cs` | `StartSkillServer` 가 시전을 **승인해 시작할 때**(쿨타임 시작 지점 = 승인 즉시). `OnServerStart`·연출 RPC 뒤 | 서버 (오프라인은 권한을 가진 로컬) | `Player` 시전자, `PlayerSkillSlot` 슬롯 | Q=`Main`, E=`Sub`, 우클릭=`Interrupt`, R=`Ultimate`. 대체 슬롯 스킬(어쌔신 변신 Q 등)도 같은 슬롯으로 난다. **수동 커밋 스킬**(`CommitCooldownManually`: 거너 Q 차지 레이저, 어쌔신 일반 E 강화)은 실제 쿨 시작이 아니라 **승인 시점**에 난다. 거부된 요청은 안 남 |
| `GunnerHeat.StageChanged` (기존, 인스턴스) | `Assets/1.Scripts/Player/Gunner/GunnerHeat.cs` | `Update` 가 계산한 과열 단계(0~3)가 바뀔 때 | **모든 피어**(서버·오너·남의 화면) | `int` 단계 | 확인만(무수정). 서버가 `NetworkVariable` 기준점을 쓰고 각 피어가 같은 함수로 단계를 계산한다 — 오너는 복제 지연만큼 늦게 바뀔 수 있다. 원격 플레이어의 사본에서도 나므로 구독자는 `Player.LocalPlayer` 의 컴포넌트에만 붙일 것 |
| `Player.ServerBasicAttackStarted` (static, v8) | `Assets/1.Scripts/Player/Player.cs` — 발행 `RaiseServerBasicAttackStarted()` | 기본 공격 1타가 **실제로 시작**될 때(적중 무관): 가붕이 `DefaultAttackController.StartAttackServer` · 거너 `GunnerBasicAttack.ServerFire`(한 발) · 어쌔신 `AssassinBasicAttack.StartServer`(강타 포함) | 서버 | `Player` | 거부된 요청·거너 준비 자세만으로는 안 남. R 대상 확정 좌클릭은 조준 경로라 안 남. 튜토리얼 존1 기본 공격 판정 |
| `GunnerChargeLaserSkill.ServerFired` (static, v8) | `Assets/1.Scripts/Player/Gunner/GunnerChargeLaserSkill.cs` | 거너 Q 를 실제로 **발사**했을 때(`CommitCooldownServer` 직후) | 서버 | `Player` | 충전 시작(= `ServerSkillUsed`)·대시 취소(쿨 커밋은 됨)·피격 종료는 안 남. 튜토리얼 존1 "거너 Q 는 발사까지" |
| `PlayerSkillTargeting.TargetingStarted` (인스턴스) | `Assets/1.Scripts/Player/Skill/Targeting/PlayerSkillTargeting.cs` | `Begin` 이 조준 모드 진입에 성공할 때(`OnOwnerAimStart` 바로 뒤) | 오너 로컬 | `PlayerSkillSlot` 슬롯 | 조준(`TargetingMode != None` ∧ `ClickToConfirm`) 스킬이면 R 이 아니어도 난다 — 본류에서 `slot == Ultimate` 로 거른다. 시전 여부와 무관. `Begin` 은 `CanBeginTargeting`(오너/오프라인) 경로에서만 불린다. `Player.LocalPlayer.GetComponent<PlayerSkillTargeting>()` 로 구독 |

## 확인 결과 — `Player.ServerAttackLanded`

`Player.RaiseServerAttackLanded` (`Assets/1.Scripts/Player/Player.cs` 1612줄)는 서버에서만, 적중 Unit 목록이 비어 있지 않을 때만 낸다.

1. **허수아비 적중에도 온다.**
   - `TrainingDummy : Unit` (`Assets/1.Scripts/Map/TrainingDummy/TrainingDummy.cs` 16줄)이고,
     `ReceiveAttack` 은 항상 `true` 를 돌려준다(98~108줄).
   - 공격 경로들은 수신이 `true` 인 Unit 을 적중 목록에 넣는다. 예: `AssassinConeAttack`(87~91·107~109줄),
     `PlayerInterruptSkillBase.ResolveHit`(173~178줄). 그 목록으로 `RaiseServerAttackLanded` 를 부른다.
   - 허수아비를 따로 빼는 곳은 결과 통계(`SessionStatsTracker`)뿐이다. 적중 이벤트에서는 빠지지 않는다.
2. **어쌔신 강타는 `AttackType.Default` 로 온다.**
   - 강타(일반 E 강화 뒤 평타 1회)는 일반·변신 평타와 같은 `AssassinBasicAttack → coneAttack.Fire` 경로다
     (`AssassinBasicAttack.cs` 444줄).
   - `AssassinConeAttack` 은 `RaiseServerAttackLanded(AttackType.Default, …)` 로 고정해서 보낸다(113줄).
   - 그래서 `AttackType` 만으로는 강타와 일반 평타를 **구분할 수 없다**. `source` 도 둘 다 `AssassinConeAttack` 이다.
     구분이 필요하면 `AssassinState`(강화 준비 상태)를 함께 봐야 한다 — 본류 판단.
   - 대조: `AssassinCircleStrikeSkill`(E, 169줄)·`AssassinDashStrikeSkill`(Q, 290줄)은 `DamageAttackType`
     = 슬롯 기준(`SkillE`·`SkillQ`)으로 보낸다.
