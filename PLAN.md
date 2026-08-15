# CURRENT PLAN — 회전·표식·Wells 폭탄·차징 (2026-08-13)

> 상태: **승인·구현 완료 (Play 미검증)**. 브랜치 `feature/Boss23`. 담당: 경석(Claude).
> 출처: 팀장 Play 스크린샷 + 지시 8건 + 문답 3건(표식 색 기준 · 차징 위치 · 차징 공격 방식).
>
> | C1 표식색 | C2 회전 | C3 Wells | D1 폭탄착지 | F1 점프억제 | G1 차징이동 | H1/H2 오라 |
> |---|---|---|---|---|---|---|
> | ✅ | ✅ | ✅ 정상동작 | ✅ | ✅ | ✅ | ✅ |
>
> **Play 피드백 6라운드까지 반영 완료** — 커밋 `03c2966`…`30ee2e5`(미푸시). 컴파일 0/0.
> ✅ 팀장 육안 승인: 넉백 3종 · 차징 이동 · 점프 때 안 뜸 · 표식 색 유지 · 훅 명중 · 폭탄 투척.
> 🔴 마지막 2커밋(`66eed97` 체공 무적 · `30ee2e5` 차징 정확 도착)은 **Play 미검증**.
> 라운드별 증상→원인 표는 **CONTEXT.md 최상단**.

## 목표

Play 에서 관찰된 **거동 4건**(표식 색 · 공격 중 회전 · Wells 폭탄 · 차징)을 확정 스펙대로 고친다.

## 확정된 스펙 (팀장 지시 + 문답)

| # | 확정 내용 |
|---|---|
| **A** | **표식 색은 처음 저작값 그대로 끝까지 유지**. 전방 = 주황빨강, 후방 = 파랑. 잡기 때 노랑 전환 **제거**. 잡기 인터럽트는 **추후 이펙트로** 처리 |
| **B** | **공격을 시도 중일 때는 회전이 없다.** 돌진은 플레이어를 밀고 **지나가고**, 돌진이 **끝나야** 다시 플레이어를 본다. 지금은 모든 공격이 플레이어를 계속 따라 돈다 |
| **C** | **Wells 가 실제로 폭탄을 던져야 한다**(프리팹엔 이미 중첩돼 있다 → 왜 안 던지는지 진단) |
| **D** | 폭탄은 **랜덤하게** 던지되 **무조건 room 안**에 떨어진다. **벽에 걸쳐도 안 된다** |
| **E** | 폭탄은 Wells **손에서 Throw 순간 AddForce** 로 날아간다(현행 유지) |
| **F** | **JumpAttack 중에는 폭탄을 던지지 않는다**(공중 투척 금지) |
| **G** | 차징은 **송전탑 4개의 중심**으로 **이동한 뒤** 애니메이션을 한다 |
| **H** | 차징 동안 보스 주변에 **원형 강공격** — 데미지 + 넉백으로 접근을 막는다. 크기는 **점프어택과 비슷**(`jumpAoeRadius` 3.5m 기준). **주기 반복**. 값은 **SO 로 노출**해 팀장이 조절 |

## 확정된 사실 (실측)

| 사실 | 근거 |
|---|---|
| 표식 노랑의 정체 = `ApplyColors()` 가 카운터 창에 `counterReadyColor` 로 바꾼다 | [BossDirectionIndicator.cs:342](Assets/1.Scripts/Monster/Boss/BossDirectionIndicator.cs:342) |
| 🔴 후방 호는 **파랑으로 저작**돼 있는데 화면엔 빨강이다 — 색 적용에 **별도 결함**이 있다 | 프리팹 `backColor: {0.3, 0.7, 1}` vs 스크린샷 |
| 회전 출처 **2곳** — 체인은 `FaceChainTarget()`, 단타는 `MonsterBase.HandleAttack` 이 **매 틱** `FaceTarget()` | [TwentyThreeBoss.cs:655](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:655) · [MonsterBase.cs:591](Assets/1.Scripts/Monster/MonsterBase.cs:591) |
| `MonsterBase` 는 몹 8종·중간보스 3종이 공유한다 → **직접 수정 금지**, 훅으로 뺀다 | 담당 경계 |
| **Wells 는 이미 `TwentyThree.prefab` 에 중첩**돼 있고 배선도 있다(`GetComponentInChildren<BossWells>`·`ThrowRequested`) | 프리팹 YAML + [TwentyThreeBoss.cs:158](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:158) |
| 투척은 **Wells fbx 클립의 `ThrowBombEvent`** 가 있어야 발동한다(SVN 자산) | [BossWells.cs:135](Assets/1.Scripts/Monster/Boss/BossWells.cs:135) |
| 넉백은 `AttackInfo` 에 이미 있다 — `knockbackStrength`/`knockbackDuration`/`staggerDuration`/`knockbackDirection` | [BaseAttack.cs](Assets/1.Scripts/Unit/Weapon/BaseAttack.cs) · `Player.OnKnockback` |
| 차징 기둥 집합은 `BossChargeSequence._engaged` 가 이미 들고 있다 → **중심 계산 가능** | [BossChargeSequence.cs:36](Assets/1.Scripts/Monster/Boss/BossChargeSequence.cs:36) |
| `jumpAoeRadius` = **3.5m** (H 의 기준값) | No23.asset |

## 접근 — 슬라이스

| # | 슬라이스 | 내용 |
|---|---|---|
| **C1** | 표식 색 고정 | 카운터 창의 색 전환 제거(A). `counterReadyColor` 필드는 **남겨 둔다** — 추후 이펙트 전환 때 쓴다. 후방이 빨강으로 나오는 **별도 결함을 함께 진단**해 파랑이 나오게 고친다 |
| **C2** | 공격 중 회전 금지 | `MonsterBase` 에 `protected virtual bool FaceTargetWhileAttacking => true` 훅 추가(**기본값 = 지금 동작**, 다른 몹 무영향) → 23호만 `false`. 조준은 `StartAttack` 직전 1회(`FaceTarget`)로 확정. 체인 쪽 `FaceChainTarget()` 도 회전을 뺀다. **돌진이 끝나면**(`FinishChain`/`DecideNextAfterAction`) 추격 상태로 돌아가며 자연히 다시 본다 |
| **C3** | Wells 투척 진단 | 폭탄이 안 나오는 지점을 **로그로 가른다**: ① 주기 만료(`ThrowCycleElapsed`) ② 투척 애니 브로드캐스트 ③ 클립 이벤트(`ThrowBombEvent`) ④ 스폰. 원인이 **클립 이벤트 부재**면 저작 도구(`No23ClipEventAuthoring` 방식)로 Wells fbx 에 심는다 — ⚠️ `50.Art` 는 **SVN** 이라 팀장 커밋 필요 |
| **D1** | 폭탄 착지 지점 보장 | **착지 지점을 먼저 뽑고 임펄스를 역산**한다(지금은 임펄스를 랜덤으로 줘서 어디 떨어질지 모른다). 후보 지점 = 보스 주변 링에서 랜덤 → **NavMesh 로 검증**(`SamplePosition` + `FindClosestEdge` 로 가장자리에서 **폭탄 반경 + 여유**만큼 안쪽) → 실패 시 재추첨 N회 → 그래도 실패면 보스 발밑. 벽 기준을 NavMesh 로 잡는 것은 돌진과 **같은 규약**이다 |
| **F1** | 공중 투척 금지 | 점프 체인 동안 `_wells.SetSuppressed(true)`, 착지/체인 종료에 해제. 이미 있는 억제 API 를 쓴다(그로기·사망과 같은 경로) |
| **G1** | 차징 위치 이동 | `BossChargeSequence` 에 **참여 기둥 중심** 게터 추가 → 보스가 `ChargeWait` 진입 **전에** 그 지점으로 이동, 도착 후 차징 애니 재생. 도착 판정·타임아웃은 돌진의 `StartDashMove` 규약 재사용 |
| **H1** | 차징 원형 장판 | 보스 주변 원형 판정. 반경 = SO(`chargeAuraRadius`, 기본 **3.5**), 주기 = SO(`chargeAuraInterval`, 기본 **1.0초**), 데미지 = SO(`chargeAuraDamage`), 넉백 = SO(`chargeAuraKnockbackStrength`/`Duration`). 방향 = **보스 → 대상** 바깥쪽. 차징 시작에 켜고 **끝(성공·실패·중단)에 반드시 끈다** |
| **H2** | 장판 비주얼 | 기존 `AoeTelegraph` 프리팹 재사용으로 범위를 보여 준다(플레이어가 크기를 알아야 피한다) |

## 리스크 / 한계

- 🔴 **C2 가 근접 명중률을 낮춘다.** 회전을 완전히 끊으면 훅·잡기가 움직이는 플레이어를 놓친다.
  확정 스펙이 그것이므로 그대로 가되, **조준 시점(공격 시작 1회)** 은 남긴다. 너무 안 맞으면
  "선딜 동안만 느리게 추적"을 옵션으로 추가하는 것이 다음 후보다.
- 🔴 **C3 의 원인이 SVN 자산(Wells fbx 클립)이면 내가 끝낼 수 없다** — 저작 도구까지 만들고
  팀장 SVN 커밋으로 넘긴다. 그 경우 이번 세션 Play 검증은 폭탄만 미검증으로 남는다.
- 🔴 **D1 은 물리 역산이라 오차가 있다**(경사·소켓 높이·항력). 착지 후 정지 규약이 이미 있어
  구르지는 않지만, 좌클릭으로 밀린 폭탄은 여전히 밖으로 갈 수 있다 — 그건 `InvisibleBoundaries`
  와 벽 반사(`wallBounceLimit`)의 몫으로 남긴다(범위 밖).
- **H1 의 넉백은 플레이어 계통 API 를 호출만 한다**(`AttackInfo.knockback*`). 플레이어 코드는 안 만진다.
- `MonsterBase` 를 만지지만 **추가만** 한다(가상 프로퍼티 1개, 기본값 = 현행). 다른 몹 동작 무변화.

## 범위 밖

잡기 인터럽트 이펙트(추후) · 폭탄이 좌클릭에 밀려 나가는 경계 처리 · 페이즈 밸런스 ·
`chargeZonePrefab` 배선 · 플레이어 평타 진단 로그(은희 경계) · 실제 맵(`MapGenConfig`) 편입.

## 완료 조건

1. 잡기·공격 어느 경우에도 표식 색이 **바뀌지 않는다**. 전방 주황 / **후방 파랑**
2. 공격 중 보스가 **회전하지 않는다**. 돌진은 플레이어를 밀고 **지나가고**, **끝난 뒤에** 다시 본다
3. Wells 가 **폭탄을 던진다**(또는 원인이 SVN 자산임을 로그로 확정하고 저작 도구를 넘긴다)
4. 던져진 폭탄이 **100% room 안**에 떨어진다. 벽에 걸치지 않는다
5. **점프 중에는 폭탄이 한 개도 안 나온다**
6. 차징 시작 시 보스가 **송전탑 4개의 중심으로 이동**한 뒤 차징 애니를 한다
7. 차징 동안 보스 주변 원형 범위가 **주기적으로** 데미지 + 넉백을 준다. 범위가 **눈에 보인다**
8. 차징이 끝나면(성공·실패·중단 전부) 원형 공격이 **반드시 꺼진다**
9. 반경·주기·데미지·넉백이 **SO 에서 조절된다**
10. 컴파일 0에러 0경고

---

# 이전 PLAN — 보스 거동 결함 4건 + 예고·폭탄 스펙 반영 (2026-08-10, 종료)

> 상태: **승인·부분 완료** (2026-08-13 갱신). 브랜치 `feature/Boss23`. 담당: 경석(Claude).
> ✅ 항목은 **기능별 6커밋으로 끊었다**(`019431e`…`c1a7342`, 미푸시). 커밋 목록 = CONTEXT.md 최상단.
> 출처: 팀장 Play 관찰 + 문답 2건(로스트아크식 예고 원 · 폭탄 생애주기). 레퍼런스 스크린샷 제공됨.
>
> | B0 | B1 폭탄 | B2 평타필터 | B3 예고원 | B3b 앞뒤표식 | B4 장판 | B5 돌진 | B6 Grab타임아웃 | B7 잡기소켓 |
> |---|---|---|---|---|---|---|---|---|
> | ✅ | ✅ | ✅ 결함아님 | ✅ | ✅ | ✅ SO 이관까지 | ✅ Play미검증 | ✅ Play미검증 | ✅ (가) |
>
> 🔴 **B2·B6 은 원인이 문서와 달랐다** — B2 는 애초에 결함이 아니고(진단 로그의 거짓 경보),
> B6 는 grab 이 아니라 **dash 체인 예산** 문제였다. 근거는 CONTEXT.md 최상단 표.
>
> ✅ 항목은 **팀장 Play 육안 승인**을 받았다(폭탄 착지정지·당구·벽1회 / 잡기 회전·자세 / 예고 원 2개 /
> 앞뒤 표식 착지 후). 알파는 SO 슬라이더 2개로 노출했다(`jumpTelegraphOuterAlpha` 0.12 ·
> `jumpTelegraphFillAlpha` 0.85). 남은 ▶ 4건의 상세·순서는 **CONTEXT.md 최상단**.

## 목표

Play 에서 관찰된 **거동 결함 4건**을 고치고, 확정된 **점프 예고 / 폭탄 생애주기 스펙**을 반영한다.

## 확정된 스펙 (팀장 문답)

**폭탄 생애주기**
1. Wells 투척 → 비행 → **착지하면 그 자리에 정지**(구르지 않는다. 이전 세션의 "당구" 개념은 **폐기**)
2. 착지 시점부터 **5초 대기** → 폭발 → `FireFloor` 장판
3. **좌클릭(평타)은 밀기만** — 폭발시키지 않는다
4. **밀려 날아가는 중에는 타이머가 만료돼도 폭발을 보류**하고, **도착(정지) 후 폭발**한다
5. **접촉하면 즉시 폭발** — 플레이어가 걸어서 닿음 · 보스와 충돌 · **점프어택 범위 안에 있음**
6. 좌클릭을 제외한 **다른 상호작용은 없다**(스킬로는 반응하지 않는다)

**점프어택 예고 (로스트아크 방식)**
- **큰 원** = 최종 범위. 빨강, **알파 약함**. 고정
- **작은 원** = 0 에서 큰 원까지 **차오른다**. 다 차는 순간 착지
- 보스는 사라졌다 위에서 나타나며, **착지 전에 이 표식이 보인다**

## 확정된 사실 (실측)

| 사실 | 근거 |
|---|---|
| ✅ **`AoeTelegraph` 가 이미 차오름을 지원한다** — `ShowGrowing(fromRadius, toRadius, growTime, holdAfter)` | [AoeTelegraph.cs:55](Assets/1.Scripts/Monster/AoeTelegraph.cs:55) |
| 🔴 **없는 것은 프리팹뿐이다** — 스크립트와 `MA_AoeTelegraph_Red.mat` 만 있고 프리팹이 없어서 `jumpTelegraphPrefab` 이 계속 비어 있었다 | `find` 전수 |
| 🔴 **돌진 이동은 이미 구현돼 있다** — `StartDashMove(dir, speedMul, maxDistance)` · `TickDash()`(`HandleAttack` 에서 호출) · `EndDashMove()` · `_dashBlockedAhead`(벽 감지) | `TwentyThreeBoss.cs:1386`·`:1399`·`:695`·`:1509` |
| → 그래서 **D2 는 미구현이 아니라 작동하지 않는 버그**다. 원인 확정 없이 코드를 더 쓰면 안 된다 | — |
| 폭탄이 플레이어 평타에서 걸러진다 — `후보: Bomb(Clone)(layer 10, hurtbox, **unit없음**)` | Play 로그 |
| 단독 프리팹은 `Wells.prefab` 을 중첩하지 않는다(중첩 1개 = SK_23 모델) | 프리팹 YAML `m_SourcePrefab` |
| `SpawnAndThrowBomb` 의 호출자는 `_wells.ThrowRequested` **하나뿐**이다 | `TwentyThreeBoss.cs:1150` |

## 접근 — 슬라이스

## 🔴 2차 조사 결과 — 요청분 대부분이 **이미 구현돼 있다**

| 요청 | 실물 상태 | 남은 일 |
|---|---|---|
| 좌클릭 시 당구 · **벽 1회 반사** | ✅ `wallBounceLimit = 1` 기본값 + `BounceOff()` 에서 `Vector3.Reflect` | 없음(값 확인만) |
| 비행 거리 = **데미지값 판정** | ✅ 계수가 필드로 노출돼 있다. 주석: *"레거시는 `distance = damage` 로 계수 없이 하드코딩돼 있었다 — 그래서 노출한다"* | 없음 |
| 폭탄이 평타를 받는 경로 | ✅ `BossBomb : IAttackReceiver` + `ReceiveAttack()` 구현. `Hurtbox` → `GetComponentInParent<IAttackReceiver>()` 로 이어진다 | **필터 1곳**(아래) |
| 장판 **생명주기** | ✅ `AreaZone.lifetime` 존재(현재 **6초**) | **10초로** + SO 노출 |
| 장판 **크기 조절** | ✅ `radius`(2) · `maxRadius`(5) 존재 | SO 노출 |
| 장판 **겹치면 합치고 생명주기 리셋** | ✅ **이미 구현** — `maxRadius` + `refreshLifetimeOnGrow = true` + `AreaZone.Active` 정적 레지스트리 | 값 확인만 |
| 잡기 시 손에 붙기 | 🔴 **플레이어 계통이 막고 있다**(아래) | 결정 필요 |

**폭탄 평타 필터**: Play 로그가 `후보: Bomb(Clone)(layer 10, hurtbox, **unit없음**)` 이라 했다. 즉
`Hurtbox` 는 있는데 `ownerUnit` 이 비어서 **플레이어 평타가 `IAttackReceiver` 에 닿기 전에 걸러진다.**
P2 에서 `ownerUnit` 을 의도적으로 비웠는데(폭탄은 `Unit` 이 아니다), 그 대가가 이것이다.

**🔴 잡기 소켓 — 원인 확정**: [PlayerStateController.cs:760](Assets/1.Scripts/Player/PlayerStateController.cs:760) 이
`instigator.GetComponentInChildren<GrabController>()` 로 소켓을 찾는다. `RestraintMode.Carry` 주석도
*"시전자의 `GrabController.GrabSocket` 에 종속된다"* 라고 못 박혀 있다. 그런데 **`GrabController` 는 신형
보스에서 제거된 레거시**(부착 0곳)다 → `followTarget = null` → **손에 붙지 않는다.**
이건 이미 은희 님께 보낼 요청 문서 [request-player-grabsocket-decoupling.md](Docs/tech/request-player-grabsocket-decoupling.md)
의 대상이다(팀장 전달 대기).

## 접근 — 슬라이스 (2차 조사 반영)

| # | 슬라이스 | 내용 |
|---|---|---|
| **B0** | 진단 로그 | 폭탄 스폰 시 **투척 주체 이름**을 남긴다 — "Wells 없는 보스가 던졌다"를 확정/반증 |
| **B1** | `BossBomb` 착지 정지 + 퓨즈 | 착지 감지 → 정지 고정 → **퓨즈 5초** → 폭발. **비행 중 폭발 보류**(도착 후 터짐). 접촉(플레이어·보스·점프 범위) 즉시 폭발. 퓨즈·거리 계수를 SO 로 노출 |
| **B2** | 평타 필터 통과 | 폭탄이 좌클릭에 맞게 한다. 🔴 **플레이어 코드를 건드리지 않는 방법을 먼저 찾는다** — `Hurtbox` 쪽에서 `Unit` 없이도 `IAttackReceiver` 로 넘기는 경로가 있는지. 없으면 요청 문서로 넘긴다 |
| **B3** | 점프 예고 프리팹 | `AoeTelegraph` 프리팹 신규 — **외곽 고정 링(알파 약함) + 내부 차오름** 2중. `ShowGrowing()` 재사용. 차오름 = **체공 시간과 동기**, 큰 원 = `jumpAoeRadius`. **착지 전에는 보스 앞뒤 구분을 보이지 않는다** |
| **B3b** | **착지 후** 앞뒤 표식 | 착지 직후 보스의 **앞/뒤 구분 이미지**를 표시한다. `BossDirectionIndicator`(카운터 방향 표시기)가 이미 있으니 그 계통을 재사용할지 먼저 본다 |
| **B4** | 장판 값 조정 + SO 노출 | `lifetime` **6 → 10**. `radius`·`maxRadius`·`lifetime`·`refreshLifetimeOnGrow` 를 SO 로 뺀다(현재 프리팹 필드). 병합 동작은 이미 있으니 **값 확인만** |
| **B5** | 돌진 이동 **진단 → 수정** | 코드는 있다(`StartDashMove`·`TickDash`·벽 감지). 왜 안 움직이는지 로그로 확정 후 최소 수정 |
| **B6** | Grab Recovery 타임아웃 | 4/4 재현. `grab` 클립의 `OnAttackEnd` 수신 여부부터 |
| **B7** | 잡기 소켓 | 🔴 **결정 필요** — 아래 「열린 결정」 |

## 🔴 열린 결정 — 잡기 소켓을 어떻게 살리나

| | 방법 | 대가 |
|---|---|---|
| (가) | **신형 보스 프리팹에 레거시 `GrabController` 를 붙이고 `GrabSocket` 만 설정** | 즉시 작동. 플레이어 코드 무수정. 대신 **지우려던 레거시를 되살린다**(요청 문서의 명분도 약해진다) |
| (나) | **은희 님 인터페이스 작업을 기다린다** | 설계가 깨끗하다. 대신 **그때까지 잡기 연출이 안 붙는다**(4줄 변경이지만 남의 일정) |
| (다) | 보스가 `GrabController` **파생/대체 컴포넌트**를 제공 | 플레이어 무수정 + 레거시 소스는 유지. 사실상 (가)의 변종 |

## 리스크 / 한계

- 🔴 **접촉 폭발과 좌클릭 밀기가 근접 거리에서 충돌한다.** 평타 사거리 안이면 대개 콜라이더도 닿는다 →
  "닿으면 폭발"이 "때리면 밀기"를 잡아먹을 수 있다. **접촉 판정 주체를 플레이어 콜라이더가 아니라
  이동 접촉으로 좁히거나, 피격 직후 짧은 접촉 무시 창**이 필요하다. 설계 시 명시한다.
- 🔴 **담당 경계**: `PlayerDefaultAttack` 은 Player 계통(은희)이다. B2 를 플레이어 쪽에서 고치면 경계를
  넘는다 — 폭탄 쪽에서 흡수하는 설계를 우선한다. 불가능하면 요청 문서로 넘긴다.
- B4·B5 는 **원인 미확정**이라 공수를 못 박을 수 없다. 진단에서 원인이 플레이어·엔진 쪽으로 나오면 범위가 바뀐다.
- 폭탄 퓨즈 5초를 SO 로 빼면 `BossDataSO` 스키마가 **1필드 늘어난다**(추가만이라 기존 값 무영향).

## 범위 밖

`chargeZonePrefab` 배선 · 페이즈 배수 밸런스 · `WeaponTrailEffect` NRE(민경 VFX) · `arcMaterial` 투명 큐 ·
V4b 실제 맵 편입(`MapGenConfig`, SVN) · 단독 변형의 공격 6종 전수 육안 확인.

## 완료 조건

1. 폭탄이 **착지 지점에 정지**하고, **5초 후** 폭발해 장판을 깐다
2. 비행 중에는 퓨즈가 만료돼도 터지지 않고, **도착 후** 터진다
3. 좌클릭으로 폭탄이 **밀리고 그때는 터지지 않는다**
4. 걸어서 닿음 · 보스 충돌 · 점프 범위 안 — **셋 다 즉시 폭발**한다
5. 점프어택 전에 **큰 원(알파 약함) + 차오르는 작은 원**이 보이고, **다 차는 순간 착지**한다
6. 돌진이 **벽에 부딪히기 전까지 실제로 전진**한다
7. Grab 체인 타임아웃 경고가 **0건**
8. 단독 변형에서 **폭탄이 한 개도 나오지 않는다**(B0 진단으로 확정)
9. 좌클릭으로 밀린 폭탄이 **벽을 한 번 튕긴다**(`wallBounceLimit = 1` 실동작 확인)
10. 장판이 **10초** 유지되고 사라진다. 크기·지속시간이 **SO 에서 조절된다**
11. 장판이 겹치면 **하나의 더 큰 장판**이 되고 생명주기가 **10초로 리셋**된다
12. **착지 전에는** 원 표식만 보이고 **보스 앞뒤 구분은 보이지 않는다**. **착지 후에** 앞뒤 구분 이미지가 나타난다
13. 잡기 시 플레이어가 **보스 손에 붙어 따라간다**

---

# CURRENT PLAN — 보스 변형 2종 분리: `No23 & Wells` / `No23 단독`(중간보스) (2026-08-10)

> 상태: **승인 대기.** 브랜치 `feature/Boss23`. 담당: 경석(Claude).
> 지시(팀장, 2026-08-10 문답): 보스를 두 벌로 만든다 — ① 지금처럼 **No23 + Wells** 붙어 있는 것,
> ② **No23 단독**(Wells·송전탑 없음)을 존에 배치해 **중간보스로 재활용**. 패턴은 지금처럼 SO 로
> 넣고 뺀다. 단독은 **Wells·송전기·레이지 돌진 셋 다 제외**, 나머지는 동일.

## 목표

보스를 **데이터로 갈리는 변형 2종**으로 분리한다. 코드 변경 0을 목표로 한다.

| | 프리팹 | 데이터 | 용도 |
|---|---|---|---|
| **V1 `No23 & Wells`** | 현재 `TwentyThree.prefab` (Wells 중첩) | 현재 `No23.asset` | 최종 보스. **신규 작업 없음** |
| **V2 `No23 단독`** | 신규 (Wells 중첩 제거) | 신규 (패턴 6종) | **중간보스 재활용.** 존에 배치 |

## 확정된 사실 (전부 실측 — 문서 인용 아님)

| 사실 | 근거 |
|---|---|
| 패턴·페이즈·프리팹 참조가 **전부 SO** 에 있다 — `attacks[8]`·`phases[2]`·`bombPrefab`·`chargeZonePrefab`·`jumpTelegraphPrefab` | `BossDataWiring` 진단 출력 |
| 🔴 보스 코드는 Wells 를 **널안전**하게 쓴다 — `_wells = GetComponentInChildren<BossWells>(true)`, 소비는 전부 `_wells?.` | [TwentyThreeBoss.cs:149](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:149)·`:1163`·`:1213` |
| 🔴 **잡기는 Wells 와 무관하다** — `BeginRestrainedByInstigator(gameObject, …)` 의 주체가 보스 자신이다. Wells 가 가진 소켓은 `bombSocket` 하나뿐 | `TwentyThreeBoss.cs:1436` · [BossWells.cs:46](Assets/1.Scripts/Monster/Boss/BossWells.cs:46) |
| 폭탄 투척 주체가 **Wells 의 애니 이벤트**다 → Wells 제거 = 폭탄 자동 제거 | [BossWells.cs:140](Assets/1.Scripts/Monster/Boss/BossWells.cs:140) `ThrowBombEvent` |
| `ValidateContract` 가 강제하는 것 = `archetype=Boss` · `attacks` 비지 않음 · **페이즈 임계 내림차순** · 각 공격의 `animatorStateName` 이 컨트롤러에 실존 | `TwentyThreeBoss.cs:213` 부근 |
| 공격 8종 = `LeftHook`/`RightHook`/`Upper`/`Grab`/`Jump`/`Dash`/`ChargeSequence`/`RageDash`. 카운터 창은 **Grab·Dash 뿐** | 진단 출력 |
| `MonsterSpawner` 는 **프리팹 기반**이다 — `defaultMonsterPrefab` + `MonsterSpawnPoint.monsterPrefabOverride` | [MonsterSpawner.cs:15](Assets/1.Scripts/Monster/MonsterSpawner.cs:15) · [MonsterSpawnPoint.cs:9](Assets/1.Scripts/Monster/MonsterSpawnPoint.cs:9) |
| 실제 맵 경로는 **그룹 ID → 프리팹** 해석이다 — `ResolveMonsterPrefab(gen, monsterGroupID)` | [MapContentSpawner.cs](Assets/1.Scripts/Map/MapContentSpawner.cs) `SpawnGroupAt` |
| 기존 중간보스급 체력 = WallBot **600** · GauntletBot **300** · SpinnerBot **260** (보스 No23 = 2000) | 데이터 애셋 실측 |

## 접근 — 슬라이스

| # | 슬라이스 | 내용 |
|---|---|---|
| **V0** | ✅ **완료** | `No23.asset` 정리 — `bombPrefab` 배선 + `hasSuperArmorWhileAttacking` off(계약 에러 해소, 거동 동일) |
| **V1** | 데이터 신규 | `No23_Solo.asset` — `attacks` **6종**(`ChargeSequence`·`RageDash` 제외) · `phases[].sequence` = **`None`** · `bombPrefab`·`chargeZonePrefab` 비움 · `maxHp` **600**(WallBot 기준) · `archetype=Boss` 유지(ValidateContract 요구) |
| **V2** | 프리팹 신규 | `TwentyThree_Solo.prefab` — 현재 프리팹을 복제해 **Wells 중첩만 제거**하고 `data` = `No23_Solo`. 리그·앵커(`Hand_L`/`Hand_R`/`DashBody`)·Animator(`No23Controller`)는 그대로 |
| **V3** | 네트워크 등록 | `DefaultNetworkPrefabs.asset` 에 `TwentyThree_Solo` 추가 |
| **V4** | 배치 | 🔴 **결정 필요 — 아래 「열린 결정」** |
| **V5** | Play 검증 | `MonsterScene` 에서 단독 변형 1기 + (별도로) 기존 V1 1기. 완료 조건 참조 |

전부 **저작 도구**로 만든다(멱등·재실행 가능) — `Monster/Editor/BossVariantAuthoring.cs` 신규 예정.
데이터·프리팹을 손으로 만들면 다음에 패턴을 넣고 뺄 때 재현이 안 된다.

## 🔴 열린 결정 1건 — 배치 경로

"MonsterSpawner 경로 재사용"으로 확정받았는데, 실물에는 **이름이 비슷한 두 경로**가 있다.

| | 경로 | 성격 |
|---|---|---|
| (a) | `MonsterSpawner` + `MonsterSpawnPoint.monsterPrefabOverride` | `MonsterScene` 등 **테스트 씬**용. 씬에 스포너를 놓는다 |
| (b) | `MapContentSpawner` + 존의 `MonsterGroupID` → `MapGenConfig` 매핑 | **실제 `4.MapScene`** 의 정본 경로 |

중간보스를 실제 맵에 넣는 게 목적이면 **(b)가 정본**이다. 그런데 (b)는 `MapGenConfig` 를 건드려야
하고 그 애셋은 **SVN 관할**이라 커밋이 팀장 손에 있다(메모리에 남은 미커밋 건과 같은 파일).

**권고: (a)로 먼저 `MonsterScene` 에서 동작을 검증하고, 그 다음 (b)로 맵에 편입한다.** V4 를 둘로 쪼갠다.
그렇게 하면 SVN 대기 때문에 검증이 막히지 않는다.

## 리스크 / 한계

- 🔴 **새 프리팹 = 새 GUID.** `DefaultNetworkPrefabs` 등록을 빠뜨리면 클라에서 스폰이 실패한다(V3).
- **복제 방식의 승계**: 기존 프리팹을 복제하면 리그·앵커·콜라이더 튜닝이 그대로 승계된다. 같은 모델·같은
  리그이므로 이번엔 **의도된 승계**지만, 그 값들이 아직 Play 로 검증된 적 없다는 사실은 그대로다(교훈 #68).
- **페이즈를 2개 유지하되 `sequence=None`** 이면 페이즈는 배수만 바꾼다. 지금 배수가 ×1 이라 사실상
  아무 일도 안 한다 — 중간보스에서 페이즈를 의미 있게 만들려면 배수를 정해야 한다(**범위 밖, 별건**).
- 중간보스에 카운터 창(Grab·Dash)이 남는다. 인터럽트 스킬이 없는 원거리(징크스)에게 불리할 수 있다 — 밸런스는 범위 밖.
- `archetype` 을 `Boss` 로 유지해야 한다. 바꾸면 `ValidateContract` 가 첫 줄에서 LogError 를 낸다.

## 범위 밖

`jumpTelegraphPrefab` 프리팹 제작 · `chargeZonePrefab` 배선 · **Grab 체인 Recovery 타임아웃**(2/2 재현, 별건) ·
`WeaponTrailEffect` NRE(민경 VFX) · `arcMaterial` 투명 큐 · 페이즈 배수·밸런스 수치 · V1 프리팹/애셋 개명.

## 완료 조건

1. `No23_Solo` 스폰 시 **`ValidateContract` LogError 0건**
2. **Wells 관련 경고 0건** — 특히 `BossWells 자식이 없어 폭탄 살포가 돌지 않는다` 가 뜨지 않는다
3. 공격 **6종이 실제로 나오고 데미지가 들어간다**(로그로 확인)
4. **송전기·레이지 돌진이 한 번도 발동하지 않는다**
5. 기존 V1(`No23 & Wells`) 경로에 **회귀 없음** — 폭탄 투척·송전기가 그대로 돈다

---

# CURRENT PLAN — 보스 전투 한 사이클: MonsterScene 재구성 + 프리팹 4종 신규 제작 (2026-08-10)

> 상태: **P0~P8 완료 · P9(Play)만 남음** (2026-08-10 갱신). 브랜치 `feature/Boss23`. 담당: 경석(Claude).
>
> | P0 | P1 | P2 | P3 | P4 | P5 | P6 | P7 | P8 | P9 |
> |---|---|---|---|---|---|---|---|---|---|
> | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ▶ |
>
> P7 저작 도구 = `Monster/Editor/MonsterSceneBossSetup.cs`(멱등). P8(NetworkPrefabs 등록)은 P1~P4
> 프리팹 커밋에 이미 포함됐다. **P9 는 사람이 Play 해야 한다** — TestBootStrap 을 비활성 보존했으므로
> 화면 왼쪽 위 **"Start Host" 를 눌러야** 시작된다.
>
> 🔴 P7 에서 이 계획서의 전제 2개가 틀렸던 것으로 확인됐다 — ① `ForProfile` 부재가 곧 "스폰 0" 은
> 아니었다(`MonsterTestBootstrap` 이 자동 StartHost 를 하고 있었다) ② `PlayerPrefab` 은 Paladin 이
> 아니라 **구 `Player.prefab`** 이었다. 경위·실측값 = CONTEXT.md 최상단, 이탈 근거 =
> [IMPLEMENTATION_NOTES.md](IMPLEMENTATION_NOTES.md) 최하단.
>
> P6 은 팀장 지시로 **범위가 늘었다** — 디렉터를 신형 타입으로 "개조"하는 대신, 죽은 레거시를
> 전수 실측해 **삭제**했다(`BossArenaContext` 176줄 · `BossArenaWiring` 136줄 ·
> `BossEncounterDirector` 889→624줄). 연출(하강·임팩트)은 그대로 유지. 상세 = CONTEXT.md 최상단.
> 지시(팀장): 기존 `Assets/2.Prefabs/Wells&No.23/` 3개(`Bomb`·`TwentyThree`·`Wells`)는 **쓰지 않는다.**
> 레거시 승계 없이 **새로 만든다.** 테스트 환경은 `MonsterScene`.
> 확정 2건(2026-08-10 문답): ① **애니 이벤트를 이번에 같이 저작한다** ② **히트박스는 확정된 새 설계로 다시 잡는다.**

## 목표

**`MonsterScene` 에서 Paladin 이 `bossroom` 안에서 → 보스 입장 연출 → 전투 → 처치까지 한 사이클**이 돈다.

## 확정된 사실 (전부 실물에서 확인 — 문서 인용 아님)

| 사실 | 근거 |
|---|---|
| 손 본 이름 = **`hand.l` / `hand.r`** (디폼). 몸통 = `spine_01.x`·`spine_02.x`, 루트 = `root.x` | `SK_23.fbx` 바이너리 노드명 추출(116개). ⚠️ `c_` 접두사는 **컨트롤러**라 붙이면 안 된다 |
| 보스 프리팹은 **`SK_23.fbx` + `Wells.prefab` 2개를 중첩**한다 | `TwentyThree.prefab` 의 `m_SourcePrefab` 2건 |
| `bossroom.prefab` = `Assets/2.Prefabs/Map/Zoneprefab/bossroom.prefab` (6081줄, **git**) | 레거시 `BossArenaContext` ×1 + `ChargingObject` ×4 |
| 🔴 연출 주체가 **레거시 타입에 묶여 있다** — `arena: BossArenaContext`, `chargingObjects: List<ChargingObject>` | [BossEncounterDirector.cs:49](Assets/1.Scripts/Map/BossEncounterDirector.cs:49) |
| 🔴 fbx 클립 이벤트 5개가 **전부 구 이름** — `OnAttackHit` 0개 | `SK_23.fbx.meta` (`TryGrabEvent`·`OnLandedEvent`·`ThrowEvent`·`SetTargetEvent`·`FallEvent`) |
| 🔴 히트는 **이벤트 전용, 타이머 폴백 없음** | [MonsterBase.cs:591](Assets/1.Scripts/Monster/MonsterBase.cs:591) |
| `MonsterScene` 현재 = `MonsterSpawner` + `MonsterSpawnPoint` ×9 + `MonsterTestBootstrap` + Env(Ground·Wall1~4) | 씬 YAML |
| `MonsterScene` 의 `PlayerPrefab` = Paladin 으로 **이미 설정됨**. 단 **`ForProfile`(Start Host) 이 없다** | 씬 YAML |
| `AreaZone` 은 `[RequireComponent(NetworkObject)]` | [AreaZone.cs:29](Assets/1.Scripts/Monster/AreaZone.cs:29) |
| `DefaultNetworkPrefabs.asset` 에 30개 등록됨 | 등록 필요 대상 = 새 `TwentyThree`·`Bomb`·`FireFloor`. **Wells 는 중첩이라 제외** |
| `rig` 스케일 **100배**, 회전 (270.02, 0, 0) | 본 아래 콜라이더는 **1/100** 로 넣어야 의도한 월드 크기 |

## 접근 — 슬라이스 9개

| # | 슬라이스 | 내용 |
|---|---|---|
| **P0** | **애니 이벤트 저작** | `SK_23.fbx.meta` — `TryGrabEvent`→`OnAttackHit`(grab, t=0.354) · `OnLandedEvent`→`OnAttackHit`(landingattack, t=0.206) · 훅L/훅R/어퍼/대시에 `OnAttackHit` 신규 + 각 클립 `OnAttackEnd`. `Boss_23_idle`·`Boss_23_charging` **Loop Time on**. 🔴 SVN 이라 **커밋은 팀장님** |
| **P1** | `FireFloor.prefab` **신규** | `NetworkObject` + `AreaZone` + 비주얼 자식(로컬 XY 지름 1) + 레이어 **`HazardArea(9)`** |
| **P2** | `Bomb.prefab` **신규** | `NetworkObject`+`NetworkTransform` + `BossBomb` + `Rigidbody`(useGravity on / FreezeRotation / **ContinuousDynamic**) + `Hurtbox`(**ownerUnit 비움**). ⚠️ 정지해도 **논키네마틱 유지**(재우면 당구가 안 된다) |
| **P3** | `Wells.prefab` **신규** | `BossWells` + Animator(`WellsBossController`) + **손 소켓**(`BombSocket`). 레거시 `BombLauncher`·`WellsAnimEvents`·`BehaviorGraphAgent` 없음 |
| **P4** | `TwentyThree.prefab` **신규** | `SK_23.fbx` 중첩 + P3 중첩 + 신형 스택만. **히트박스 새 설계**: `hand.l`/`hand.r` 아래 앵커 2개(훅·어퍼·잡기 공용) + `spine_02.x` 아래 돌진용 1개. 점프는 앵커 없음(코드가 `jumpAoeRadius` 로 처리). `No23.asset` 의 `hitboxAnchorName` 재매핑 |
| **P5** | `bossroom` 송전탑 교체 | `ChargingObject` ×4 → `BossChargingPylon` ×4 |
| **P6** | `BossEncounterDirector` 개조 | `arena`·`chargingObjects` 를 신형 타입으로. 연출 자체(강하·임팩트 홀드)는 유지 |
| **P7** | `MonsterScene` 재구성 | 기존 몹 세팅은 **삭제 대신 비활성 보존** · `bossroom` 배치 · `ForProfile` 추가 · **NavMesh 재베이크** |
| **P8** | NetworkPrefabs 등록 | `DefaultNetworkPrefabs.asset` 에 3종 추가 |
| **P9** | Play 한 사이클 검증 | Start Host → Paladin 스폰 → 입장 연출 → 공격 8종 → 카운터 → 페이즈 → 처치 |

## 리스크 / 한계

- 🔴 **새 프리팹은 GUID 가 새로 발급된다** — `PlayerBossTest`·`BossScene`·`4.MapScene` 은 여전히 **구 프리팹을 가리킨다.** 셋 다 재배선하거나, 구 프리팹을 지우는 시점을 따로 정해야 한다. (이번 범위는 `MonsterScene` 우선)
- 🔴 `TwentyThreeBossAuthoring` 은 컨트롤러를 **매번 지우고 새로 만든다.** 애니를 손으로 튜닝하기 시작하면 다시 돌리면 안 된다.
- 🔴 `50.Art` = **SVN**. P0 의 `.fbx.meta` 편집은 내가 하되 **커밋은 팀장님**이 하셔야 한다.
- **상태이상(기절·그로기) 검증은 MPPM 2인**에서만 된다(`CanWrite = IsSpawned && IsServer`). 단독 Play 로 "기절이 안 된다"를 버그로 오진하지 말 것.
- NavMesh 를 안 구우면 보스가 제자리에 선다(`not close enough to the NavMesh`).

## 범위 밖

플레이어 CC 수신 경로(`OnGrabThrowRelease` 변위) · 어퍼 에어본 · 레거시 `Enemy/Boss`·`8.BehaviorTreeGraph` 삭제 · 사운드 · VFX.

## 완료 조건

1. `MonsterScene` Play → Start Host → **`ValidateContract` LogError 0건**
2. 보스가 입장 연출을 마치고 Paladin 을 추격·공격하며, **실제로 데미지가 들어간다**
3. 카운터 창 표시가 화면에 보이고(방향 표시기 머티리얼 배선), 인터럽트로 그로기가 걸린다
4. 페이즈 전환 → 송전기 시퀀스 → 실패 시 레이지 돌진이 돈다
5. 보스 처치까지 도달, 콘솔 에러 0건

---

# CURRENT PLAN — 보스 FSM 지원 2건: 인터럽트 식별자 + 캐리 소켓 일반화 (2026-08-07)
# CURRENT PLAN — 전체 화면 픽셀레이트 + V축 스캔라인 (2026-08-13)

> 상태: **구현 및 기본 플레이 검증 완료**. 브랜치 `fix/pixel`.
> 위아래 기존 CURRENT PLAN 항목들은 이전 작업 기록이며 이번 렌더링 작업과 무관하다.

## 목표

기존 마스크 블러가 소유하던 픽셀레이트를 독립된 화면 효과로 분리한다.

- 블러는 기존처럼 화면공간 마스크 바깥 영역에만 적용한다.
- 픽셀레이트는 블러 영역과 무관하게 월드 화면 전체에 적용한다.
- 화면 V 좌표만 사용하는 가로 스캔라인을 함께 제공한다.
- 픽셀레이트와 스캔라인은 각각 독립적으로 켜고 끌 수 있다.
- UI는 두 효과에서 제외한다.

## 확정 사항

| 항목 | 결정 |
|---|---|
| 픽셀 범위 | 블러 마스크와 완전히 분리된 전체 화면 |
| 스캔라인 방향 | 화면 V 좌표만 사용 — 가로줄 |
| 스캔라인 패턴 | `scanlineThicknessPx`만큼 색 적용 + `scanlineSpacingPx`만큼 원본 구간 반복 |
| 스캔라인 두께 | 픽셀레이트 크기와 독립된 `scanlineThicknessPx` 화면 픽셀 값 |
| 스캔라인 간격 | 두께와 독립된 `scanlineSpacingPx` 화면 픽셀 값 |
| 스캔라인 색 | RGB 색상 필드 |
| 스캔라인 강도 | 색상 Alpha와 분리한 `0~1` 필드 |
| 활성화 | 픽셀레이트 / 스캔라인 독립 토글 |
| UI | Screen Space Overlay UI 제외 |
| 네트워크 | 로컬 카메라 연출이므로 동기화·RPC 없음 |

## 접근

### 1. 마스크 블러에서 픽셀레이트 제거

`MaskBlurSettings`, `MaskBlurFeature`, `MaskBlur.shader`에서 픽셀레이트 설정·파라미터·샘플링을
제거한다. 마스크 블러는 블러와 바깥 영역 톤 합성만 담당하게 되며, 마스크 크기는 더 이상
픽셀레이트 범위에 영향을 주지 않는다.

### 2. 독립 `PixelScanline` 렌더러 피처 추가

새 설정·컨트롤러·렌더러 피처·셰이더를 `Assets/1.Scripts/Rendering/PixelScanline/`에 둔다.

- `PixelScanlineSettings`: 전체 활성화, 픽셀 토글/크기, 스캔라인 토글/두께(px)/간격(px)/색/불투명도
- `PixelScanlineController`: 전투 씬에서만 패스를 허용하는 씬 상주 게이트
- `PixelScanlineFeature`: URP 17 RenderGraph 풀스크린 1패스
- `PixelScanline.shader`: 전체 화면 UV 픽셀 양자화 후 V축 스캔라인 합성
- Editor authoring 도구: 설정 애셋, PC Renderer 피처, 열린 씬 컨트롤러를 안전하게 배선

패스는 `AfterRenderingPostProcessing`에 넣는다. 기존 Volume 후처리가 끝난 월드 화면에 효과를
적용하므로 블록과 스캔라인이 블룸·SMAA에 다시 흐려지지 않고, Screen Space Overlay UI는 그 뒤에
그려져 선명하게 유지된다.

### 3. 해상도 대응

UV 블록 개수를 반올림해 나누는 기존 방식 대신 실제 렌더 타깃 픽셀 좌표를 사용한다.

`pixel = uv * resolution` → `floor(pixel / pixelSize)` → 블록 중심을 다시 UV로 변환한다.

따라서 1920×1080, 2560×1440, 3840×2160처럼 해상도가 바뀌어도 픽셀 블록 크기와 독립된
스캔라인 두께가 각각 지정한 렌더 픽셀 수로 유지된다. 화면 끝에는 해상도가 패턴 주기의
배수가 아닐 때만 정상적인 부분 패턴이 생긴다.

## 변경 예정 파일

- 수정: `Assets/1.Scripts/Rendering/MaskBlur/MaskBlurSettings.cs`
- 수정: `Assets/1.Scripts/Rendering/MaskBlur/MaskBlurFeature.cs`
- 수정: `Assets/1.Scripts/Rendering/MaskBlur/Shaders/MaskBlur.shader`
- 신규: `Assets/1.Scripts/Rendering/PixelScanline/` 아래 런타임·셰이더·Editor 파일과 `.meta`
- 신규: `Assets/99.Settings/PixelScanlineSettings.asset`과 `.meta`
- 수정: `Assets/99.Settings/PC_Renderer.asset` — 새 피처와 셰이더 직렬화 참조
- 수정: 정본 전투 씬 `Assets/0.Scenes/MainFlow/4.MapScene-trensparent.unity` — 컨트롤러 배선

현재 작업트리의 Bootstrap, Addressables, ProjectSettings, 문서 변경은 사용자 작업으로 보고
건드리지 않는다.

## 위험과 대응

- **추가 풀스크린 패스 1회:** 픽셀·스캔라인을 블러에서 독립시키는 비용이다. 두 효과가 모두
  꺼지면 패스를 큐잉하지 않아 비용 0으로 만든다.
- **다중 카메라:** Preview, Reflection, RenderTexture 대상(미니맵 베이크) 카메라는 제외해
  의도치 않은 중복 적용을 막는다.
- **셰이더 빌드 스트립:** 셰이더를 PC Renderer의 피처에 직렬화 참조한다.
- **동적 해상도/Render Scale:** 렌더 타깃 해상도를 기준으로 계산한다. 실제 출력 픽셀과 1:1인지
  여부는 Render Scale 1 기준으로 검증하고, 비정상 배율도 별도 확인한다.
- **씬 게이트 누락:** authoring 도구가 설정·피처·컨트롤러 세 지점을 한 번에 검사·배선한다.

## 범위 밖

- Screen Space Overlay UI 픽셀레이트 또는 스캔라인 적용
- 픽셀 블록과 스캔라인의 시간 애니메이션·스크롤·노이즈·왜곡
- 모바일 Renderer와 PP Renderer 적용
- 네트워크 상태 동기화
- 기존 블러 룩과 포그·디밍 값 변경

## 완료 조건

- 블러 마스크 크기를 바꿔도 픽셀레이트 범위는 화면 전체로 유지된다.
- 픽셀레이트만, 스캔라인만, 둘 다, 둘 다 끔의 네 조합이 정상 동작한다.
- 픽셀 크기를 바꾸면 픽셀 블록만 바뀌고 스캔라인 두께는 유지된다.
- `scanlineThicknessPx`를 바꾸면 픽셀 블록 크기와 무관하게 색 띠 두께만 바뀐다.
- `scanlineSpacingPx`를 바꾸면 두께를 유지한 채 색 띠 사이의 원본 구간만 바뀐다.
- 스캔라인은 V축에만 반응하며 가로 방향으로 끊기거나 반복되지 않는다.
- 스캔라인 RGB와 불투명도를 독립적으로 조절할 수 있다.
- 16:9 두 해상도 이상과 다른 종횡비 하나에서 블록·띠 두께가 픽셀 기준으로 유지된다.
- HUD와 미니맵 UI는 효과 없이 선명하다.
- Unity 컴파일·셰이더 컴파일 오류와 렌더링 콘솔 오류가 없다.

## 검증 계획

1. 정적 검사와 Unity 컴파일/셰이더 임포트 오류 확인.
2. 정본 전투 씬을 Bootstrap 경로로 실행해 블러와 전체 화면 픽셀 분리를 육안 확인.
3. 픽셀/스캔라인 토글 네 조합과 색상·불투명도 실시간 튜닝 확인.
4. Game View 해상도를 최소 1920×1080, 2560×1440, 1024×768로 바꿔 캡처 비교.
5. HUD·미니맵이 선명한지 확인하고 미니맵 RenderTexture에 효과가 들어가지 않는지 확인.
6. 가능하면 MPPM 호스트/클라이언트 양쪽에서 동일한 로컬 화면 결과 확인.

## 구현 및 검증 결과

- 기존 MaskBlur에서 픽셀레이트 설정·파라미터·셰이더 샘플링을 제거했다.
- 독립 PixelScanline 렌더러 피처와 설정 애셋을 추가하고 정본 전투 씬에 연결했다.
- Unity 6000.3.16f1 Play Mode에서 전체 화면 픽셀레이트와 V축 스캔라인을 확인했다.
- 픽셀레이트/스캔라인 독립 토글과 Screen Space Overlay UI 제외를 확인했다.
- 최종 저장 설정은 픽셀 크기 4px, 스캔라인 두께 2px, 간격 4px, 불투명도 0.2다.
- Assembly-CSharp와 Assembly-CSharp-Editor 빌드가 오류 없이 완료됐다.
- 추가 해상도 전환 캡처와 MPPM 비교는 수행하지 않았다. 해상도 대응은 현재 렌더 타깃의 실제 픽셀 해상도를 매 프레임 전달하는 방식으로 구현했다.

---

# PREVIOUS PLAN — DevSceneBooter: 씬 이름 한 줄로 원하는 씬 부팅 (2026-08-10)
# CURRENT PLAN — 피격 이펙트 클라이언트 복제 + 런타임 교체 디버그 HUD (2026-08-11)

> 상태: **grill 완료, 승인 대기**. 브랜치 `feature/VFX`.
> 아래 Wall Occlusion(07-28) 항목은 별개 작업 — 이 계획과 무관.

## 배경 — 작업이 둘로 갈렸다

원래 요청은 "키 입력으로 피격 이펙트를 런타임에 바꿔보는 디버그 툴"이었다. 조사 중
**별개의 실 버그**가 드러났다: 몬스터 피격 이펙트가 **호스트에서만 보인다.**

`BaseAttack.TryResolveHit`가 전부 `IsServer` 게이트라(`BaseAttack.cs:132`) `ReceiveAttack`
자체가 서버에서만 불리고, `EffectManager.Play` 호출이 그 안에 있다(`MonsterBase.cs:680`).
리슨 서버에서 호스트는 곧 서버라 호스트 화면에는 보이지만, **순수 클라이언트는 몬스터를
때려도 피격 이펙트가 아예 안 뜬다.** 디버그 편의가 아니라 출하 품질 문제다.

그래서 **A(버그 수정)와 B(디버그 툴)를 커밋 분리**해서 진행한다. B는 A 위에서만 의미가
있으므로 순서는 A → B.

## 확정 사항 (grill 결과)

| # | 결정 | 기각한 대안과 이유 |
|---|---|---|
| 1 | 이펙트 교체는 `EffectManager`의 **전역 오버라이드** | 몬스터 컨테이너 순회 — 레지스트리를 새로 만들어야 하고, 순회 후 스폰된 몹을 놓치며, 각 몹의 인스펙터 원본값을 파괴한다 |
| 2 | 서버가 **`sourcePosition`만** ClientRpc로 전 피어 브로드캐스트 | 이펙트를 NetworkObject로 — `EffectManager`의 풀링·수명·히트스톱 인프라를 통째로 우회하고 복제 트래픽이 RPC보다 훨씬 크다 |
| 3 | 각 피어가 **자기 로컬 콜라이더**로 `Resolve` + `Play` | 서버가 계산한 `Pose` 전송 — 아래 §A-2 참조 |
| 4 | `MonsterBase` / `Enemy` **각각 구현** (2벌 중복 감수) | `Unit`으로 올리기 — 코어는 은희 담당, 사전 합의 필요. 새 컴포넌트 분리 — `Enemy`가 곧 제거될 예정이라 중복 제거 명분이 사라짐 |
| 5 | IMGUI HUD, `Assets/1.Scripts/Dev/`, `F1`~`F5` 선택 + `F6` 해제 | 씬에 Canvas 배치 — `4.MapScene`은 팀 공용이고 Unity 씬 파일 머지 충돌이 지독하다 |
| 6 | `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`로 릴리스 제외 | — `ProfilerHUD`와 동일한 관례 |

---

## A. 피격 이펙트 클라이언트 복제 (버그 수정)

### A-1. 구조
서버는 **판정만**, 재생은 각 피어가 로컬로. 이 레포에 이미 정착된 패턴이다 —
`AoeTelegraph`(`AoeTelegraph.cs:12`)와 `GauntletBot.ShowTelegraphClientRpc`가 동일 구조.

- 서버: `ReceiveAttack`에서 `hitContext.sourcePosition`(Vector3)만 ClientRpc로 전 피어에 브로드캐스트
- 각 피어: 수신 → 자기 로컬 `hitVFXCollider` / `hitPointMode` / `hitVFXType`로
  `EffectHitPoint.Resolve` → `EffectManager.Play`
- RPC는 **unreliable** — 순수 연출이라 유실이 상태 발산을 만들지 않는다

### A-2. 왜 `Pose`가 아니라 `sourcePosition`인가 (핵심 근거)

`NetworkManager.prefab`의 `TickRate: 30`, 몬스터 프리팹의 `NetworkTransform`은 `Interpolate: 1`.
클라이언트는 스냅샷 사이를 보간하려고 **의도적으로 과거를 그린다.** 렌더 지연 = 보간 버퍼
(1~2틱, 33~66ms) + 편도 지연. 인터넷 대전이면 100ms 안팎 → 몹이 4m/s로 움직일 때 **0.3~0.4m**,
몸통 반쯤 되는 거리만큼 서버 위치와 어긋난다.

서버가 계산한 `Pose`는 **월드 절대 좌표**라 그 어긋남만큼 이펙트가 몸에서 떨어져 허공에 뜬다.
반면 `SurfacePoint(collider, bounds, origin)`를 수신측이 다시 계산하면 콜라이더가 로컬
오브젝트이므로 **결과가 무조건 그 몹 표면 위**다.

비대칭이 이 설계의 근거다:
- **콜라이더 위치가 틀리면** → 이펙트가 몸에서 떨어진다 (치명적)
- **`origin`이 조금 틀리면** → 표면 위에서 점이 옆으로 미끄러질 뿐 (무해)

`origin`은 "표면의 어느 쪽을 고를지"만 결정하지 이펙트를 몸에서 떼어내지 못한다.
그래서 origin은 서버 값을 그대로 쓰고, 콜라이더는 반드시 로컬 것을 쓴다.

부수 이점: 페이로드 12B (Pose+인덱스 29B 대비 절반 이하).

### A-3. `hitVFXCollider` null 가드 (같이 처리)
현재 `MonsterBase.cs:678`이 `hitVFXCollider.transform`을 무방비로 역참조한다. 프리팹 9개에는
전부 배선돼 있어 당장 안 터지지만, **배선을 잊은 몹이 추가되면 맞을 때마다 예외**를 뿜는다.
그리고 이 줄을 RPC 수신부로 옮기면 **터지는 지점이 1개에서 N개(전 피어)로 늘어난다.**
어차피 만지는 줄이므로 가드를 함께 넣는다 — 없으면 경고 1회 후 조용히 스킵(게임은 정상 진행).

⚠️ 세션 중 논의했던 `fallbackAnchor` 재설계(`hitVFXAnchor` 필드 신설)는 **범위 밖**.
프리팹 9개를 다시 건드려야 한다. 가드만 넣고 넘어간다.

### A-4. 대상
| 프리팹 | 클래스 |
|---|---|
| ChompBot · HumanoidBot · MortarBot · PeekABot · TeslaBot · WallBot | `MonsterBase` |
| GauntletBot · SpinnerBot | `MonsterBase` 하위 (자동 커버) |
| **TwentyThree (No.23 보스)** · ModularRobots_R1 | **`Enemy`** |

`Enemy`는 제거 예정이지만 **7월 마일스톤의 보스가 그 위에 올라가 있어** 빼면 안 된다.

---

## B. 런타임 이펙트 교체 디버그 HUD

### B-1. 오버라이드 저장 위치 — `EffectManager` (SO 아님)
`EffectCatalog`는 `ScriptableObject`다. **여기에 오버라이드를 직렬화 필드로 두면 안 된다** —
SO는 씬 오브젝트와 달리 플레이 모드 중 변경이 에셋에 그대로 눌러앉는다. 플레이를 멈춰도
안 돌아오고, `.asset` 변경으로 git에 잡히고, 최악은 그대로 커밋돼 **팀 전체 기본 이펙트가
바뀐다.**

- 오버라이드는 `EffectManager`(MonoBehaviour 싱글톤)의 **런타임 필드** — 플레이 종료 시 확실히 소멸
- `EffectCatalog`는 순수 데이터로 유지
- 호출부는 `Catalog.GetHitEffect(...)` → `EffectManager.Instance.GetHitEffect(...)`로 변경

> 참고: `EnterPlayModeOptions: 0`(= 아무것도 비활성화 안 함) 확인 — 도메인 리로드는 정상
> 동작하므로 static 필드도 안전하지만, 위 이유로 SO 필드만 피하면 된다.

### B-2. 빌드 격리 제약
HUD가 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`면 **릴리스 빌드엔 클래스가 없다.** 따라서
프로덕션 코드(`MonsterBase`/`Enemy`)가 HUD를 직접 참조하면 릴리스 빌드가 깨진다.
저장소를 `EffectManager`(모든 빌드에 존재)에 두면 자동 해결 — **HUD는 쓰기만, 프로덕션은 읽기만.**

### B-3. 입력 / 표시
프로젝트 전체가 Input System(`Keyboard.current`)을 쓴다.

- `F1`~`F5` → `HitEffect1`~`HitEffect5` 직접 선택 (순환보다 원하는 걸 바로 짚는 게 비교에 유리)
- `F6` → 오버라이드 해제, 각 몹 원래 `hitVFXType`으로 복귀
- 현재 적용 중인 이펙트 이름을 화면에 IMGUI로 표시
- **이미 쓰이는 키(피할 것)**: `F8` ProfilerHUD · `F10` 디버그 부활 · `M` 맵 오버뷰 ·
  `F` 다리 상호작용 · `[` `]` 카메라 전환/미니맵 줌 · `ESC` 씬 전환

### B-4. 오버라이드 범위는 **머신별**
A-1에서 각 피어가 로컬로 `GetHitEffect`를 부르므로, 키를 누른 창만 바뀐다.
디버깅엔 오히려 장점 — MPPM 창 두 개를 나란히 놓고 `HitEffect2` vs `HitEffect4`를
**동시에 비교**할 수 있다. 대신 여럿이 같이 볼 때는 "지금 뭘 보고 있는지"를 말로 맞춰야 한다.

---

## 리스크

- **호스트에서는 이 버그가 안 보인다.** 호스트 = 서버라 보간 어긋남이 0이다. 반드시
  **MPPM 클라이언트 창에서, 몹이 이동 중일 때** 때려서 검증해야 한다. 정지한 몹으로
  테스트하면 잘못된 구현도 통과한다.
- `MonsterBase`/`Enemy` 2벌 중복 — `Enemy` 제거 시 자연 해소되므로 부채로 남기지 않는다.
- `EffectCatalog.asset`이 의도치 않게 변경돼 커밋되지 않는지 `git status` 확인.
- 몹이 디스폰된 직후 RPC가 도착하면 수신측 콜라이더가 없다 → A-3 가드가 흡수(이펙트 하나 누락, 무해).

## 완료 조건

- [ ] MPPM 호스트+클라 2인, **몹이 이동 중일 때** 피격 → **양쪽 화면 모두** 이펙트가 몹 몸에 붙어 재생
- [ ] No.23 보스(`Enemy` 경로)에서도 동일하게 확인
- [ ] `F1`~`F5`로 이펙트 전환, HUD에 현재 이름 표시
- [ ] `F6`으로 각 몹 원래 값 복귀
- [ ] `hitVFXCollider` 미배선 몹에서 예외 대신 경고 1회 + 게임 정상 진행
- [ ] 콘솔 0 에러 — **호스트·클라 양쪽 모두** 확인
- [ ] 릴리스 빌드 컴파일 확인 (HUD 클래스 부재 상태에서 `MonsterBase` 참조 안 깨짐)
- [ ] `EffectCatalog.asset` 무변경 확인

## 커밋 분리

1. `fix(fx): 몬스터 피격 이펙트를 전 피어에 복제` — A
2. `feat(dev): 피격 이펙트 런타임 교체 HUD` — B

A는 리뷰 포인트가 네트워크라 팀장이 따로 볼 항목이다.

---

# CURRENT PLAN — Wall Occlusion per-pixel 재설계 (2026-07-28)

> 상태: **승인 대기**. 구현 착수 전.
> 요청자·담당: 은희. `GameManager.cs` 수정 포함 — 2026-08-03 계획서는 이 파일을 팀장 영역이라 봤으나,
> 은희 판단으로 진행한다(추가 4줄·기존 경로 무영향).
> 브랜치 예정: `feature/DevSceneBooter` (base `development`), 레인 `dash`.
> 전신 계획: `git show c98710024:PLAN.md` — "개발 진입점 단일화 + 맵 단독 Play 부팅"(목표 2만 이행됨).

## 목표

**`DevSceneBooter`의 `Scene` 필드에 씬 이름을 적고 Dev_Boot 씬을 Play하면, 그 씬이 정식 흐름과
동일한 상태로 부팅된다** — 호스트 기동, 플레이어 스폰, MainGameReady 발행, 액티브 씬 지정까지.

기존 정식 흐름(BootStrap→Title→Lobby→Loading→MapScene)은 **한 줄도 바뀌지 않는다.**

## 현재 이해 (코드 실측 완료)

| 사실 | 근거 |
|---|---|
| 매니저 4종은 `0.BootStrapScene`에만 있다 | NetworkManager·GameManager·AudioManager·EventSystem 프리팹 인스턴스 |
| `4.MapScene`에 NetworkManager·ForProfile 인스턴스 **0개** (GameManager 참조 1건은 버튼 OnClick이 프리팹 에셋을 가리키는 것) | 씬 GUID 스캔 |
| **강제 타이틀 이동의 정체** = 조건 없는 `LoadScene(titleSceneName)` | [GameManager.cs:59](Assets/1.Scripts/Managers/GameManager.cs:59) |
| ⭐ **로딩 컨트롤러는 `loadingSceneName`이 아니라 `targetSceneName` 기준으로 반응한다** → 로딩씬을 생략해도 스폰·완료 체인이 그대로 돈다 | [:272](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:272), [:296](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:296), [:338](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:338) |
| 타겟 씬 로드 완료 → `SpawnAllPlayersOnce()` → `BroadcastAverageProgress()` → `_phase==LoadingGame && avg>=1` 이면 완료 코루틴 → `NotifyMainGameReady()` | [:376](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:376), [:692](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:692), [:553](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:553) |
| 로비가 씬에 없으면 `CanStartFromLobby()`는 그냥 통과 | [:171](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:171) |
| `SpawnAllPlayers`는 public이고 `PlayerObject != null`이면 스킵 → **중복 호출 안전** | [:384](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:384), [:420](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:420) |
| `NotifyMainGameReady`는 멱등 | [GameManager.cs:81](Assets/1.Scripts/Managers/GameManager.cs:81) |
| 🔴 `MarkMainGameStart()`는 **멱등이 아니다** — 부를 때마다 재스탬프 | [NetworkClock.cs:125](Assets/1.Scripts/Network/NetworkClock.cs:125) |
| `GameManager`는 `4.MapScene`일 때만 자동으로 `MarkMainGameStart` | [GameManager.cs:216](Assets/1.Scripts/Managers/GameManager.cs:216) |
| MainGameReady 실소비자 = 플레이어 **AudioListener 활성화**, InGame BGM | [PlayerAudioListenerActivator.cs:32](Assets/1.Scripts/Player/PlayerAudioListenerActivator.cs:32), [MapSceneManager.cs:58](Assets/1.Scripts/Managers/MapSceneManager.cs:58) |
| 컨트롤러는 **소스 씬 언로드 중에만** `SetActiveScene`을 한다 → 로딩씬 생략 시 `_sourceSceneName`이 비어 액티브 씬이 Dev_Boot에 남는다 | [:961](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:961), [:984](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:984) |
| 🔴 테스트 씬 6종이 빌드 목록 `enabled: 0` → 런타임 `LoadScene` 불가 | `ProjectSettings/EditorBuildSettings.asset` |
| 🔴 빌드 스크립트는 **EditorBuildSettings의 enabled 씬 전부**를 플레이어에 넣는다 | [BuildWindowsPlayer.cs:48](Assets/1.Scripts/Editor/BuildWindowsPlayer.cs:48) |

## 접근

### 1. 새 씬 `Assets/0.Scenes/Dev/Dev_Boot.unity` (빌드 목록에 넣지 않는다)

**`0.BootStrapScene`을 그대로 복제**하고 `DevSceneBooter` 오브젝트 하나만 추가한다.
BootStrap은 매니저 프리팹 인스턴스 4개가 루트에 있는 373줄짜리 단순 씬이라 복제가 깔끔하다
(NetworkManager / GameManager / AudioManager / EventSystem — AudioManager·EventSystem을 빼면
BGM·UI 입력이 죽는다. `c0d4457d3`에서 부팅 씬 단일 소유로 옮겨졌다).

복제이므로 매니저 구성이 BootStrap과 자동으로 일치한다 — 초안에서 우려했던 "5번째 매니저 추가 시
드리프트"는 최초 구성이 동일해지므로 위험이 줄지만, **향후 BootStrap에 매니저가 추가되면 Dev_Boot에도
수동 반영해야 한다**는 점은 남는다.

### 2. `DevSceneBooter.cs` (신규, `Assets/1.Scripts/Dev/`)

```
[SerializeField] string scene = "4.MapScene";   // ← 여기만 바꾼다
[SerializeField] bool  autoBootOnPlay = true;
[SerializeField] GameObject playerPrefabOverride;   // 비우면 NetworkManager 프리팹 기본값
```

`Awake()`: `FindFirstObjectByType<GameManager>()?.SuppressStartupSceneLoad()`
→ `Instance`가 아니라 `Find`를 쓰는 이유 = 두 Awake의 실행 순서는 보장되지 않지만, 오브젝트 존재는
씬 로드 시점에 보장된다. `Start()`는 모든 `Awake` 뒤라 억제가 반드시 선행된다.

`Start()` → 코루틴:
1. `scene`이 빌드 목록에 있고 enabled인지 `SceneUtility`로 검사 → 아니면 **조치 가능한 에러 로그** 후 중단
2. `flow.SetEditorDefaults("2.LoadingScene", scene, 0f, 0f)`
3. `launcher.StartHost()` → 실패 시 에러 후 중단
4. `IsListening && IsServer && SceneManager != null` 까지 대기
5. `nm.SceneManager.LoadScene(scene, Additive)` — `SceneEventInProgress`면 정식 경로와 동일하게 재시도
6. 씬 `isLoaded` 까지 대기 → **`SceneManager.SetActiveScene(scene)`** (컨트롤러가 안 해주는 유일한 일)
7. 한 프레임 뒤 안전망: 호스트에 `PlayerObject`가 없으면 `flow.SpawnAllPlayers()`,
   `NetworkClock.HasMainGameStarted`가 false면 `MarkMainGameStart()` (재스탬프 방지),
   `GameManager.Instance.NotifyMainGameReady()` (멱등)
   → 6·7은 이벤트 순서에 의존하지 않게 만들기 위한 것이고, 전부 중복 호출 안전이 확인된 API다.

8. **Dev_Boot 씬을 로컬 언로드한다** (정식 흐름이 소스=로비 씬을 언로드하는 것과 대칭).
   반드시 7단계의 `SetActiveScene` **뒤에** 해야 한다(액티브 씬을 먼저 옮기지 않으면 언로드 불가).
   Dev_Boot은 NGO가 아니라 로컬 로드된 씬이므로 `SceneManager.UnloadSceneAsync`를 쓴다
   (컨트롤러의 [UnloadLocalScene](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs:932) 과 동일한 방식).

   **매니저 생존 실측 완료** — 언로드해도 4종 전부 살아남는다:
   NetworkManager = NGO가 `OnEnable`에서 `DontDestroyOnLoad`(부모 없을 때. `Library/PackageCache/
   com.unity.netcode.gameobjects@aaabf07f/Runtime/Core/NetworkManager.cs:1087`) /
   [GameManager.cs:51](Assets/1.Scripts/Managers/GameManager.cs:51) /
   [AudioManager.cs:42](Assets/1.Scripts/Sound/AudioManager.cs:42) /
   [PersistentEventSystem.cs:28](Assets/1.Scripts/UI/PersistentEventSystem.cs:28).
   → **전제: Dev_Boot에서 매니저 4종은 반드시 루트 오브젝트로 둔다**(NetworkManager는 부모가 있으면
   `DontDestroyOnLoad`가 걸리지 않는다).

### 3. 기존 파일 수정 1건 (**호출되지 않으면 완전히 무영향**)

- `GameManager.cs` — `SuppressStartupSceneLoad()` + `Start()`의 early-return. 4줄.
- `NetworkLoadingFlowController.cs`는 수정하지 않고 기존 `SetEditorDefaults(...)`를 재사용한다.
  Dev 경로는 `StartGameLoading()`을 호출하지 않으므로 로비 준비 게이트는 사용되지 않는다.

### 4. 빌드 씬 목록 — 테스트 씬 6종 `enabled: 1`

`BossScene`·`MonsterScene`·`PlayerScene`·`PlayerBossTest`·`PlayerDashTest`·`CamaraScene`.
런타임 로드의 전제 조건이라 피할 수 없다.

⚠️ **인수인계 사항 — 빌드 스크립트는 은희가 직접 수정한다(2026-08-10 결정).**
[BuildWindowsPlayer.cs:48](Assets/1.Scripts/Editor/BuildWindowsPlayer.cs:48)이 `EditorBuildSettings`의
enabled 씬 전부를 플레이어에 담으므로, **그 수정 전까지는 테스트 씬 6종이 빌드에 실려 나간다.**
이 계획은 `BuildWindowsPlayer.cs`를 건드리지 않는다.

## 리스크

| 리스크 | 대응 |
|---|---|
| 테스트 씬은 NetworkObject 저작·레이어 표준(`f7fba054c`)이 정식 씬과 달라 전투가 깨져 있을 수 있다 | **전투 정상 동작이 완료조건에 포함됨(2026-08-10 결정).** 씬을 NGO `SceneManager.LoadScene`으로 싣기 때문에 씬 배치 NetworkObject는 자동 스폰된다 — 그게 전제 조건은 충족시킨다. 씬별로 실제 타격까지 Play 검증하고, 저작 누락은 고친다. 씬 콘텐츠 자체를 재설계해야 하는 건이 나오면 별건으로 보고한다 |
| MapScene은 `MapGenerator` 스폰 슬롯이 필요 — 없으면 폴백 위치(0,5,0) | 정식 흐름과 동일 동작이라 회귀 아님 |
| 테스트 씬에 인라인 AudioListener/EventSystem이 있으면 중복 | `RuntimeSceneServiceCoordinator`·`PersistentEventSystem`이 이미 정리함 |
| Dev_Boot이 액티브 씬으로 남는 사고 | 6단계 `SetActiveScene`이 유일한 방어선 → 검증 항목에 포함 |

## 검증 방법

1. 컴파일 0 에러.
2. `Dev_Boot` Play + `Scene = 4.MapScene` → 플레이어 스폰·이동, BGM 재생, 액티브 씬이 `4.MapScene`,
   Dev_Boot 언로드됨, `[NetworkClock] MainGame 시작 스탬프` 로그 **1회만**.
3. **전투 검증 (완료조건)** — 대상 씬마다: 좌클릭 기본공격이 몹에 **데미지 적용**(체력 감소·히트플래시·
   플로팅 데미지), 몹이 사망까지 가고, 몹의 반격도 플레이어 체력을 깎는다.
   대상: `4.MapScene` / `BossScene` / `MonsterScene` / `PlayerScene` / `PlayerBossTest` / `PlayerDashTest`.
   씬별 결과를 표로 기록하고, 깨진 건 원인(레이어·Hurtbox·NetworkObject 저작)까지 규명한다.
4. `Scene = 없는이름` → 빌드 목록 안내 에러 후 조용히 중단(예외 없음).
5. **회귀**: `0.BootStrapScene` Play → 타이틀→로비→로딩→맵이 종전과 동일. 로딩화면 대기시간도 5s/2.5s 유지.
6. MPPM 2인으로 Dev_Boot 호스트 + 클라 접속 1회 확인(정식 경로 재사용이라 되어야 정상).

## 진행 상황 (2026-08-10)

**구현 완료 · 컴파일 0에러 0경고**

| 항목 | 상태 |
|---|---|
| `Assets/1.Scripts/Dev/DevSceneBooter.cs` | ✅ 신규 |
| `Assets/1.Scripts/Dev/Editor/DevBuildSceneList.cs` | ✅ 신규 — 빌드 씬 목록 등록을 Unity가 하게 하는 메뉴. Unity가 열려 있을 때 `EditorBuildSettings.asset`을 파일로 고치면 메모리 값에 덮여 조용히 되돌아가므로 필요했다 |
| `GameManager.SuppressStartupSceneLoad()` + `Start()` 가드 | ✅ |
| `NetworkLoadingFlowController.SetEditorDefaults()` 재사용 | ✅ |
| `Assets/0.Scenes/Dev/Dev_Boot.unity` | ✅ BootStrap 복제 + DevSceneBooter (구조 검증: 프리팹 4개·루트 5개·스크립트 guid 일치) |
| 빌드 씬 목록 테스트 씬 6종 활성화 | ✅ 메뉴로 적용, 디스크 반영 확인(12씬 전부 enabled) |
### Play 검증 결과 (`4.MapScene` · `MonsterScene`)

| 확인 항목 | 결과 |
|---|---|
| 호스트 기동 + NGO 씬 로드 | ✅ `4.MapScene`·`MonsterScene` 모두 로드 |
| 액티브 씬 전환 | ✅ `isActive: true`로 타겟 씬이 잡힘 |
| 플레이어 스폰 | ✅ `Paladin(Clone)` **정확히 1개**. PlayerInput·NetworkObject·NetworkTransform·PlayerDefaultAttack·스킬 4종·HitFlash·FloatingDamagePresenter까지 완비 |
| 씬 배치 NetworkObject 스폰 | ✅ MonsterScene 봇 9종이 `Enemy` 레이어 + Hurtbox + MonsterBase로 살아있음 |
| 게임 화면 | ✅ 톱다운 카메라·팔라딘 렌더링 정상(스크린샷 확인) |
| **몹 → 플레이어 전투** | ✅ **작동.** 근접(`MonsterMeleeAttack.Hit`)·투사체(`MonsterProjectile.OnTriggerEnter`)·폭발(`Detonate`) 3경로 모두 피해 적용, 방어력 경감까지(요청 10 → 실제 8, defense 25). 체력 9881 → 9805 연속 감소 |
| **플레이어 → 몹 전투** | ⏸ 합성 입력으로 발동 실패. 아래 참고 |
| 에러·경고 | 관측 구간에서 0건 |

**플레이어 공격을 자동 검증하지 못한 이유(정직하게)**: MCP 합성 마우스 입력으로 좌클릭 공격을 발동시키지
못했다. 격리 시도에서 플레이어를 (40, 40)으로 텔레포트했는데 그곳이 MonsterScene 바닥 밖이라 낙하 상태가 됐고,
낙하 중엔 공격이 거부된다 — 내 테스트가 스스로를 무효화했다. 헛스윙은 로그를 남기지 않아 "발동 안 됨"과
"빗맞음"도 구분되지 않았다.
`attackSpeed: 0`은 조사 결과 **무혐의**다 — 그 값은 `Unit.AttackSpeed`로 들어가 몹·보스 쿨다운
(`MonsterBase.CooldownReady`)에만 쓰이고, 플레이어 공격 주기는 `DevaultAttackController.attackSteps`가 정한다.
**중요**: 플레이어는 정식 흐름과 **동일한 프리팹·동일한 스폰 코드**(`NetworkLoadingFlowController.SpawnAllPlayers`)로
생성된다. 따라서 플레이어 공격 거동이 개발 부팅과 정식 부팅에서 달라질 수 있는 경로가 없다.
→ 사람이 좌클릭 한 번 하는 것으로 5초 만에 확정된다. 은희님 확인 요청.

### 실측으로 부정된 가설 — 씬 배치 플레이어 중복

정적 분석에서는 테스트 씬들에 활성 `Player.prefab` 인스턴스가 있어 Paladin과 **플레이어 2명**이 될 것으로
봤다. Play 실측 결과 `Player` 컴포넌트는 **1개**뿐이었다(`Paladin(Clone)`). 중복이 발생하지 않으므로
대응 코드는 넣지 않았다.

### 🔴 발견된 결함 2건 — 둘 다 수정 완료

**1. 부팅 씬을 `UnloadSceneAsync`로 언로드하면 에디터가 Play 모드를 종료한다.**
대조 실험으로 확정했다 — 언로드 ON이면 Play가 2~3초 만에 죽고, OFF면 계속 유지된다.
정식 흐름이 로비 씬을 언로드해도 괜찮은 것은, 그전에 타이틀 씬을 **Single 로드로 교체**해서
Play 원본 씬이 이미 바뀌어 있기 때문이다.
→ **수정**: 명시 언로드를 버리고 타겟 씬을 **`LoadSceneMode.Single`로 실어 부팅 씬을 대체**한다
(`replaceBootScene`, 기본 켜짐). 결과는 요청하신 대로 부팅 씬이 하이어라키에서 사라지는 것이고,
Play 모드는 유지된다. Single 로드는 이 오브젝트도 파괴하므로 `DontDestroyOnLoad`로 빼두었다가
부팅이 끝나면 스스로 `Destroy`한다.

**2. Play 진입 시 `Dev_Boot`이 빌드 씬 목록에 잡혔다**(`buildIndex: 12`).
단, `ProjectSettings/EditorBuildSettings.asset` **디스크에는 기록되지 않았다** — 에디터가 Play를 위해
임시로 잡은 인메모리 상태였고 영속되지 않는다. 즉 현재 빌드 유출 위험은 없다.
→ 그래도 안전판으로 `Dev/빌드 씬 목록/Dev 부팅 씬을 목록에서 제거` 메뉴를 추가했다.

### 남은 확인 (Unity 재컴파일 대기 중)

- 수정된 Single 로드 경로로 `4.MapScene` 재검증 + Dev_Boot 씬 필드 재저장(`replaceBootScene` 키로 갱신)
- 정식 흐름 회귀(`0.BootStrapScene` Play → 타이틀→로비→로딩→맵)
- MPPM 2인
- 나머지 테스트 씬 4종(`BossScene`·`PlayerScene`·`PlayerBossTest`·`PlayerDashTest`) 부팅

### ⚠️ 별건 — 게임에 AudioListener가 없다 (내 작업과 무관, 기존 결함)

`Paladin.prefab`에는 `AudioListener`도 `PlayerAudioListenerActivator`도 **없다**(`Player.prefab`에는 둘 다 있다).
MainFlow 전체에서 AudioListener를 가진 것은 `2.LoadingScene` 하나뿐이라, **로딩 씬이 언로드되는 순간
게임에 리스너가 0개가 된다** → 소리가 안 나고 Unity가 매 프레임 경고를 뿜는다(콘솔 버퍼를 도배해서
디버깅도 방해한다).
기본 플레이어를 Player → Paladin으로 바꾼 `0bca7a01c`에서 두 컴포넌트가 함께 넘어오지 않은 것으로 보인다.
**정식 흐름에도 그대로 있는 결함이다.** 이 계획의 범위 밖이라 손대지 않았다 — 별건으로 처리할지 은희님 판단.

## 범위 밖

- 테스트 씬 내부 콘텐츠 수리, `ForProfile` 정리, 로비 진입점 추가 변경
- `4.MapScene` 및 정식 MainFlow 씬 수정 (일절 건드리지 않는다)
