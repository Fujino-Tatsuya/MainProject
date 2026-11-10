# 평타 오너 예측 (할 일 256 — 팔라딘·어쌔신·거너)

> 계획(잠긴 사양): [Docs/history/PLANS/PLAN-owner-predicted-basic-attack.md](../history/PLANS/PLAN-owner-predicted-basic-attack.md).
> 이 문서 = 구현된 구조·서버 검증 규칙·캐릭터별 RPC·알려진 한계·QA 체크리스트.

## 1. 한 줄
세 캐릭터 평타 모두 **오너가 1차 판단 후 즉시 재생**하고, 서버는 **통지를 따라가며 판정 요청만 2차 검증**한다.
걸리면 **데미지만 없다**(모션·연출은 그대로, 롤백 없음). 예전 "오너 입력 → 서버 승인 → 재생"은 원격 클라가 매 타 RTT 만큼 늦게 휘둘렀다.

## 2. 공용 흐름

| 단계 | 누가 | 무엇을 |
|---|---|---|
| 시작·다음 타 | **오너**(체인 주도) | 자기 애니 이벤트(ComboWindowOpen/Close·End)·자기 입력(창 안 클릭·End 순간 눌림)으로 판단, **즉시 재생**. 어쌔신 모드·거너 과열도 오너가 1차 판단 |
| 타 시작 통지 | 오너 → 서버 (타마다 1회) | 일련번호·단·방향·(모드/루프백 오프셋/회전 지연)·이어짐·스탯 리포트. 서버는 **거부하지 않고** 공격 상태를 따라가며 기존 서버 시작 처리(튜토리얼 훅·강화 소모·castId 등) |
| 전달 | 서버 → **오너 뺀** 피어 | 기존 재생 RPC(팔라딘 `PlayDefaultAttackRpc` / 어쌔신 `StartRpc` / 거너 `StartRpc`·`ShotRpc`, 전부 `SendTo.NotOwner`). **NetworkAnimator 에 기대지 않는다**(NGO 2.12: 직접 `SetTrigger`·CrossFade 오프셋·같은 상태 재 Play·`Animator.speed` 복제 안 됨) |
| 판정 요청 | 오너 → 서버 (Reliable, Hit 시점) | 일련번호·단·히트 번호·**오너 위치·방향**. 서버는 2차 검증 → 오너 위치·방향으로 **기존 서버 판정 경로 그대로**(데미지·특성·VFX RPC). 서버가 보는 위치와 수평 2m 넘게 다르면 경고 로그만 |
| 종료 | 오너 → 서버 | 정상 종료·스스로 끊음(대시) 통지. 지금 따라가는 일련번호가 아니면 무시 |

- **네트워크에서 서버·관전자 쪽 애니 이벤트(Hit/End/ComboWindow)는 무시**한다 — 그 애니메이터도 같은 클립을 돌려 늦게 또 터진다.
- **호스트 오너·오프라인**은 같은 함수 경로에서 서버 처리를 직접 한다(통지·판정 요청 RPC 없음, 2차 검증 없음 — 예전과 같다).
- 회전 지연(할 일 43)은 오너가 정해 통지에 싣는다(서버 상한 자르기 없음 — 길면 보낸 쪽만 손해).

## 3. 서버 2차 검증 — `BasicAttackVerify.cs`(순수, EditMode `BasicAttackVerifyTests`)

- **순서**: 이어지는 콤보면 직전 +1, 마지막 타 뒤 루프면 0, 새 콤보면 0 만(`BasicAttackVerifyRules.IsValidStepOrder`). 서버가 콤보를 끝낸 뒤(종료 통지·끊기) 온 '이어짐'은 새 시작으로 본다.
- **간격**: `실제 간격 + 여유 ≥ 기대 간격 ÷ 최근 최대 공속`. 기대 간격 = 클립 `AnimationEvent.time` 기반(공속 1 기준 실제 초):
  같은 휘두름의 다단 히트 = 히트 간 시각, 다른 휘두름 = 앞 타 Hit→End + 다음 타 진입→Hit(루프백이면 `LoopBackEntryTime`).
  크로스페이드·회전 지연은 간격을 늘리기만 해 넣지 않는다(관대). 최근 최대 공속 = 공속 창 안의 최대값(버프가 막 풀린 순간 관대).
- **새 콤보 첫 히트**는 하한 없이 통과. **중복**: 같은 (휘두름, 히트 번호) 1회. 시작 통지 없는 휘두름의 히트 = `UnknownSwing`.
- **거너**는 `VerifyFixedInterval`(발 = 휘두름 하나·히트 하나, 기대 간격 = 데이터 `fireInterval`) + 첫 발은 시작 통지 도착부터 `windupDuration`.
- 수치 2칸 = `InterruptManager`(`NetworkManager.prefab`, xlsx `SlowMotion` 시트 — 간파 허용 오차와 같은 곳):
  `basicAttackIntervalSlack` 0.15초 · `basicAttackSpeedWindow` 0.5초. 🟪 xlsx 행은 **병합 Export 때** 생긴다(development 에서만 Export).
- 시간은 `Time.unscaledTimeAsDouble` — 간파 슬로우 중에는 실제 간격이 길어져 관대해질 뿐이다.

## 4. 캐릭터별

### 4.1 팔라딘 — `DefaultAttackController` / `PlayerDefaultAttack`
- RPC: `NotifySwingStartRpc`(서버) · `RequestHitRpc`(서버) · `NotifyAttackEndRpc`(서버) · `PlayDefaultAttackRpc`·`EndDefaultAttackRpc`(NotOwner) · `CancelAttackRpc`(NotServer) · `PlaySlashVfxRpc`(전 피어, 명중/헛스윙 — 검증에 걸리면 헛스윙).
- 매 프레임 `RequestQueueNextAttackRpc` 삭제. 루프백 CrossFade 오프셋은 전달 RPC 로 다른 피어도 같은 클립 시각(각자 자기 AttackSpeed 로 나눔).
- 기대 간격 시각표 = `StepTimings`(콤보 상태 Speed Multiplier = AttackSpeed 파라미터라 상태 속도 1).

### 4.2 어쌔신 — `AssassinBasicAttack`
- RPC: `NotifySwingStartRpc`(서버: 일련번호·단·**모드**·방향·회전 지연·이어짐·스탯) · `RequestHitRpc`(서버, 변신 묶음 = 히트 번호 0~3) · `NotifyAttackEndRpc` · `StartRpc`·`EndRpc`(NotOwner) · `CancelRpc`(NotServer).
- **모드(변신 > 강화 > 일반)**는 오너가 복제된 `AssassinState` 로 고르고, 서버가 같은 함수(`TrySelectAttackMode`)로 확인 — `AssassinOwnerPredictionRules.AcceptsClaimedMode`:
  같으면 수락 · **오너 일반 + 서버 강화 = 일반으로 수락**(서버가 막 강화를 켰는데 복제가 아직 안 닿음 — 강화는 소모하지 않고 다음 타로 남는다) ·
  그 밖(서버엔 강화 없음·변신 여부 다름·변신 종료 대기)은 **서버 끊기 경로**(`ServerRejectSwing` → `CancelRpc`).
- 강화 소모·일반 E 쿨·시전 ID(`NextServerCastId`)·튜토리얼 훅은 서버 시작 처리(`ServerOnSwingStarted`). 강타 뒤 일반은 1타부터(오너 콤보 모델).
- End 순간 눌림·창 예약 모두 오너 자기 입력(예전 서버 눌림 신호 `SetHoldRpc`·`HoldSignalRpc`·신선도 규칙 삭제).
- 2차 검증 시각표 = 모드별(`StepTimings(mode)`), 클립 시각 ÷ **`AssassinBasicAttackData` 단별 `animatorStateSpeed`**(컨트롤러 상태 Speed 복제 — Default_Attack0~3 = 1.2 / Enhanced 2 / Transformed 1.25, `DataTableIgnore`).
  원본은 `AssassinAnimatorController` — 상태 Speed 를 바꾸면 데이터도 같이(EditMode `AssassinComboWindowRulesTests.DataStateSpeedMatchesControllerStateSpeed` 가 잡는다).
  공격속도는 `Animator.speed`(공격 중) 라 검증기의 공속으로 나눈다. **모드가 바뀌면 새 콤보**(순서 0 부터, 첫 히트 하한 없음 — `ContinuesForVerify`).
- 서버 판정 경로 유지: 백어택·급소 파괴 밀치기·끝없는 칼날 예약·분노 충전·돌파의 여세(첫 히트 소모) — 위치·방향만 오너 값(`TryGetHitArea(…, casterPosition, …)`).

### 4.3 거너 — `GunnerBasicAttack`
- RPC: `NotifyStartRpc`(서버: 공격 일련번호·조준·스탯) · `FireShotRpc`(서버, Reliable: 일련번호·**총구 위치·방향**) · `NotifyAttackEndRpc` · `StartRpc`·`EndRpc`·`ShotRpc`(NotOwner) · `CancelRpc`(NotServer).
- 오너가 발사 순간 **즉시** 상체 Fire·레이저. 끝점은 피해 없는 예측 직선 `GunnerBeamAttack.PredictEnd`(관통이면 지형까지). 호스트 오너는 판정 결과로 연출.
- 창 예약·홀드·후속 동작 끝은 오너 시계(예전 서버 눌림·예약·`ReleaseRpc`·`HoldSignalRpc`·`FinishRpc` 삭제). 예전 서버 간격 검사(×0.75)는 공용 규칙으로 교체.
- **과열 오너 1차 예측** `GunnerHeatPrediction`(`GunnerHeatModel.cs`): 복제 기준점 + 서버 미반영 자기 발. 기준점 시각 이하인 발 = 반영됨(서버는 도착 시각에 반영하므로 오너 시각보다 뒤), 0.5초 지난 발 = 거부·유실로 보고 버림.
  예측상 과열이면 쏘지 않고, 거부 입력도 버린다(`IsOutOfResource`). **서버 `GunnerHeat` 가 원본** — 예측이 틀린 발(과열 직후 한 발)은 서버가 2차 거부: **레이저만, 데미지·과열 증가 없음**(다른 피어엔 벽까지 빗나간 레이저).
- 서버 안전망: 첫 발이 안 오면 준비 + 발사 간격 × 3, 그 뒤는 마지막 발 + max(간격, 후속 동작) + 1초.

## 5. 서버가 끊는 경로(그대로)
넉백·구속·연출 잠금(`PlayerEncounterLock`)·숨김·대시·다운 → 서버 `CancelCurrentAttack` → 끊기 RPC(`NotServer`, 일련번호) → 오너·관전자 즉시 멈춤.
오너는 일련번호가 지금 콤보 시작보다 앞(바이트 순환 비교 — 거너는 다른 번호)이면 무시(이미 끝난 옛 콤보). 서버는 끊은 콤보의 뒤늦은 '이어짐' 통지를 따라가지 않는다(팔라딘·어쌔신).
원격 오너가 스스로 끊으면(대시) 종료 통지로 서버가 따라 끝낸다. 통지가 끊기면 서버 안전망(End + 1초, 거너는 §4.3).
기절은 원래 진행 중 공격을 끊지 않는다(범위 밖).

## 6. 알려진 한계
- **서버 공격 상태가 통지 도착까지(RTT/2) 늦다** — 그 사이 서버 측 스킬·대시 판정과 경합할 수 있다(대시는 오너 종료 통지로 맞춰진다).
- **오너 위치 신뢰**(이동이 이미 오너 권한이라 새 구멍 아님) — 서버 NT 위치와 2m 넘게 다르면 경고 로그로 관찰.
- **새 콤보 첫 히트는 하한 없음** — 콤보를 매번 끊고 새로 시작하면 간격 검증을 피할 수 있다(사양 그대로). 거너는 첫 발에 준비 동작 검사가 있다.
- 어쌔신 `comboContinuationSeconds` > 0(지금 0)이면 '새 콤보인데 2타부터'가 순서 위반으로 데미지 없음이 된다 — 켜려면 검증 순서 규칙을 같이 고친다.
- 거너 과열 예측: 지연이 발사 간격보다 길면 반영 전 발을 반영됨으로 잘못 봐 한 발 덜 예측할 수 있다 → 그 발은 레이저만.
- 어쌔신 모드가 '오너 강화·서버 일반'으로 어긋나는 순간(강화 소모 복제 전에 다시 강타 등)은 그 타가 끊긴다.
- 관전 피어의 재생은 전달 RPC 도착 시각 기준 — 오너 화면보다 RTT/2 늦은 것은 그대로다.

## 7. QA 체크리스트 (사용자 Play — 원격 클라, 왕복 150ms)

### 7.1 지연 거는 법 — Multiplayer Tools `Network Simulator`(패키지 `com.unity.multiplayer.tools` 2.2.8, 이미 설치)
1. MPPM 으로 호스트(메인 에디터) + 클라(가상 플레이어) 를 띄우고 `0.BootStrapScene` 부터 정식 흐름으로 들어간다.
2. **클라 인스턴스**에서 Play 중 Hierarchy 의 `NetworkManager`(DontDestroyOnLoad)에 **Add Component → `Network Simulator`** 를 붙인다
   (프리팹에 붙이면 호스트도 같이 늦어진다 — 그러면 아래 값을 절반으로. 프리팹 변경은 커밋하지 않는다).
3. 프리셋 대신 Custom 으로 **Packet Delay** 를 넣고 Jitter·Loss 는 0 에서 시작한다.
4. **Runtime Network Stats Monitor**(같은 패키지 — 클라 인스턴스 `NetworkManager` 에 Add Component)의 **RTT 가 약 150ms** 인지 본다 — 넘치면 Delay 를 줄이고(예: 150 → 75) 다시 확인.
5. 끝나면 컴포넌트를 떼거나 Play 를 끈다(런타임 추가는 남지 않는다).

### 7.2 확인 항목 (세 캐릭터 각각)
- [ ] 누르는 즉시 휘두름/발사(원격 클라 화면에서 RTT 만큼 늦지 않다)
- [ ] 창 끝 탭으로도 콤보가 이어진다(팔라딘·어쌔신), 꾹 누르면 순환/연사
- [ ] 판정 위치가 오너 화면과 맞는다(이동하며 때려도 오너가 본 자리에서 맞는다)
- [ ] 넉백·연출 잠금 시 즉시 끊긴다, 대시로 끊어도 서버·관전 화면이 따라 끝난다
- [ ] 어쌔신 강타(E 강화 → 평타)·변신 묶음(4히트) 정상, 강타 뒤 일반은 1타부터, 강화 소모·E 쿨 정상
- [ ] 거너 과열이 정확히 막힌다(게이지 최대에서 다음 발이 안 나감, 과열 직후 한 발이 나가도 데미지 없음)
- [ ] 호스트 오너는 지금과 동일
- [ ] 3인(MPPM 호스트 + 클라 2) 관전 화면 정상 — 다른 플레이어 평타 모션·베기 VFX·레이저가 보인다
- [ ] Console 에 `[평타 검증] … 데미지 없음` 경고가 정상 플레이에서 뜨지 않는다(뜨면 사유 — TooFast·OutOfOrder 등 — 를 적어 보고)
