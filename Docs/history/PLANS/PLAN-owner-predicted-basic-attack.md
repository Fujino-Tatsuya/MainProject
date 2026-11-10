# PLAN 256 — 평타 오너 예측 (팔라딘·어쌔신·거너)

> 2026-11-10 grill 확정(이은희). 레인은 이 파일을 브랜치의 `Docs/history/PLANS/PLAN-owner-predicted-basic-attack.md` 로 옮겨 커밋하고,
> 끝나면 `Docs/tech/owner-predicted-basic-attack.md`(구조·검증 규칙·알려진 한계)와 AGENTS.md §6 인덱스 한 줄을 남긴다.

## 문제
세 캐릭터 평타가 모두 "오너 입력 → 서버 승인 → 재생" 이라 원격 클라는 매 타 RTT 만큼 늦게 휘두른다.
팔라딘은 콤보 창을 서버 애니 이벤트로만 판정해 클라 체감보다 창이 RTT 만큼 일찍 닫히고, 공격 중 누르고 있으면
`RequestQueueNextAttackRpc` 를 매 프레임(스탯 리포트 포함) 보낸다. AGENTS §4 "플레이어 스킬 = 오너 권한" 과도 어긋난다.

## 확정 사양

### 흐름
1. **오너 1차 판단 후 즉시 재생.** 다음 타 = 오너 자기 애니의 ComboWindow 안에 입력이 있으면 나간다(서버 승인 대기 없음).
   어쌔신 모드(변신 > 강화 > 일반)·거너 과열도 오너가 1차 판단한다.
2. **타 시작 통지 RPC**(오너 → 서버, 타마다 1회): 인덱스·방향·모드·스탯 리포트.
   - 서버는 거부하지 않고 공격 상태(`PlayerActionState.Attack`)를 따라간다. 튜토리얼 훅 `RaiseServerBasicAttackStarted`, 어쌔신 강화 소모·E 쿨, castId 발급 등 기존 서버 시작 처리를 여기서.
   - 서버는 **기존 재생 RPC**(팔라딘 `PlayDefaultAttackClientRpc` / 어쌔신 `StartRpc` / 거너 `ShotRpc`)로 **오너를 뺀** 나머지 피어에 전달.
   - **NetworkAnimator 에 기대지 않는다** — NGO 2.12: `animator.SetTrigger` 직접 호출은 복제 안 됨(TransitionStateInfoList 비어 있음), 스크립트 CrossFade 시작 오프셋(LoopBackEntryTime) 유실, 같은 상태 재 `Play` 미감지, `Animator.speed` 미복제.
   - 매 프레임 예약 RPC(팔라딘)는 없앤다. 공격 종료도 통지.
3. **판정 요청 RPC**(Reliable, 오너 Hit 시점): 타 인덱스·히트 번호·위치·방향.
   - 서버는 **오너가 보낸 위치·방향**으로 판정(이동이 이미 오너 권한이라 새 구멍 아님). 서버가 보는 NT 위치와 크게 다르면 경고 로그만.
   - 데미지 계산·특성(Dref·적중 추가 피해·AttackLanded·급소 파괴 밀치기·뇌진탕·끝없는 칼날 예약·분노)·VFX RPC 는 기존 서버 판정 경로 그대로.
   - **네트워크에서 서버 쪽 애니 이벤트(Hit/End/ComboWindow)는 무시**(서버 애니메이터도 NetworkAnimator 로 돌아 늦게 또 터진다). 호스트=오너·오프라인은 기존대로.
4. **서버 2차 검증 — 순수 함수 + EditMode 테스트.** 걸리면 **데미지만 없음**(모션은 그대로, 롤백 없음).
   - 간격: `실제 간격 + 여유(0.15s) ≥ 기대 간격`. 기대 간격 = 클립 이벤트 시각 기반(k타 Hit→End + k+1타 진입→Hit, 루프백은 LoopBackEntryTime, 같은 타 다단 히트는 히트 간 시각) ÷ **최근 0.5초 안의 최대 공속**. 비율 계수 없음. 크로스페이드·회전 지연은 간격을 늘리기만 하므로 무시(관대).
   - 콤보가 끊긴 뒤 새로 시작한 1타는 하한 없이 통과.
   - 순서: 직전 +1 / 루프 0 / 새 시작 0 만.
   - 중복: 같은 (타, 히트 번호) 1회.
   - 여유 0.15s·공속 창 0.5s 는 xlsx `SlowMotion` 시트(간파 허용 오차와 같은 시트)에 행 추가 → `[DataTableSheet]` 경로. 기대 간격 데이터는 클립 `AnimationEvent.time` 을 런타임에 읽는다(`AnimationEventRequirements.HasEvent` 처럼 clip.events 순회 가능).
5. **서버가 끊는 경로는 그대로**: 넉백·구속·연출 잠금(PlayerEncounterLock)·숨김·대시 → `CancelCurrentAttack` → 끊기 RPC → 오너 즉시 멈춤. 기절은 원래 진행 중 공격을 끊지 않는다(이번 범위 밖). 어쌔신 모드가 서버와 어긋나면(서버엔 강화 없음 등) 같은 끊기 경로.

### 캐릭터별
- **팔라딘** (`DefaultAttackController`/`PlayerDefaultAttack`): 매 프레임 `RequestQueueNextAttackRpc` 제거, 오너가 자기 창으로 체인 주도, 회전 지연은 오너가 정함(서버 상한 자르기 불필요). 루프백 CrossFade 오프셋은 전달 RPC 로 다른 피어도 맞춘다.
- **어쌔신** (`AssassinBasicAttack`): 모드 선택 오너 → 통지에 실어 서버 확인·강화 소모. 상태 speed(Default_Attack0~3 = 1.2 / Enhanced 2 / Transformed 1.25)를 `AssassinBasicAttackData` 에 복제 + 컨트롤러 값과 같은지 EditMode 테스트. 변신 4연타는 히트 번호로 구분.
- **거너** (`GunnerBasicAttack`): 오너가 발사 연출(상체 Fire·레이저) 즉시, `FireShotRpc` 에 위치·방향. 기존 간격 검사(×0.75)를 공용 규칙으로 교체. **과열 오너 1차 예측** = 복제 과열값 + 서버 미반영 자기 발수로 예측, 예측상 과열이면 안 쏜다. 서버 `GunnerHeat` 가 원본·2차 거부(과열 직후 한 발 = 레이저만, 데미지 없음).

## 작업 방식
- 한 레인·한 브랜치, 커밋 순서: ① 공용 검증 규칙(순수 함수·테스트·시트 행) → ② 팔라딘 → ③ 어쌔신 → ④ 거너.
- 깨질 수 있는 테스트를 새 흐름에 맞춘다: `DefaultAttackChainRulesTests`, `AssassinComboWindowRulesTests`, `PlayerBasicAttackInputRulesTests`, `PlayerAnimationEventRequirementTests`, `AssassinTraitTier3/4Tests` 일부.
- 컴파일·EditMode 는 batchmode 로 직접. push·병합은 지시 후. 기존 xlsx 수치 오버라이드는 버림.

## 검증 (사용자 Play, 원격 클라, Multiplayer Tools Network Simulator 왕복 150ms — 켜는 법을 QA 문서에)
- 세 캐릭터 누르는 즉시 휘두름/발사
- 창 끝 탭으로도 콤보 이어짐
- 판정 위치가 오너 화면과 맞음
- 넉백·연출 잠금 시 즉시 끊김
- 어쌔신 강타·변신 정상, 거너 과열 정확히 막힘
- 호스트 오너는 지금과 동일
- 3인 관전 화면 정상

## 리스크
- 오너·관전 피어 재생 경로 이중화(오너 로컬 + 전달 RPC + NetworkAnimator 상태 동기) — 오너가 전달 RPC 를 받지 않게, NetworkAnimator 가 덮어쓰지 않게 확인.
- 서버 공격 상태가 통지 도착까지 늦음(RTT/2) — 그 사이 서버 측 스킬/대시 판정과의 경합.
- 오너 위치 신뢰 → 서버 NT 위치와 차이 경고 로그로 관찰.
