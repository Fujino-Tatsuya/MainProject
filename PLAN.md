# CURRENT PLAN — 회전·표식·Wells 폭탄·차징 (2026-08-13)

> 상태: **승인·구현 완료 (Play 미검증)**. 브랜치 `feature/Boss23`. 담당: 경석(Claude).
> 출처: 팀장 Play 스크린샷 + 지시 8건 + 문답 3건(표식 색 기준 · 차징 위치 · 차징 공격 방식).
>
> | C1 표식색 | C2 회전 | C3 Wells | D1 폭탄착지 | F1 점프억제 | G1 차징이동 | H1/H2 오라 |
> |---|---|---|---|---|---|---|
> | ✅ | ✅ | ▶ 진단만 | ✅ | ✅ | ✅ | ✅ |
>
> 커밋 `03c2966` · `1cfad5d` · `5122e34` · `2348ef8` · `acf129c` (미푸시). 컴파일 0/0.
> 🔴 **전부 Play 미검증**이다. C3 는 원인을 못 찾아 **진단 로그만** 넣었다(아래).

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

> 상태: **구현 진행 중**(사용자 지시 = PLAN 작성 후 바로 착수).
> 브랜치 `feature/InterruptSkill-CarrySocket` (base `MainProject/development` `6dbc1c34a`).
> 요청 출처: 경석 → 은희 인계문 `C:\Users\user\Desktop\handoff-player-carry-socket.md` (기한 8/7 17:00).
> 담당: 은희(플레이어 계통). 보스 쪽 소비 코드는 경석이 작성한다.

## 목표

보스 FSM 재작성이 플레이어 쪽에 요구하는 두 계약을 **기존 동작 회귀 없이** 제공한다.

- **A.** 보스가 서버에서 "이 히트 = 인터럽트 스킬"을 판별할 수 있다. ✅ 구현·컴파일 검증 완료
- **B'.** 서버가 플레이어를 잠시 구속할 수 있다 — 잡기(소켓 종속)와 돌진 밀기(정면 추종)를
  `Restrained` 한 상태의 두 모드로. ✅ 구현·컴파일 검증 완료
  (원 요청 B "캐리 소켓 일반화"는 폐기. 경위는 §접근 B 참고)

## 현재 이해 (코드 확인 완료)

| 사실 | 근거 |
|---|---|
| `AttackType`에 우클릭 슬롯 값이 없다 | [BaseAttack.cs:4](Assets/1.Scripts/Unit/Weapon/BaseAttack.cs:4) |
| 🔴 **플레이어 스킬은 `BaseAttack`을 타지 않는다** — `AttackInfo`를 직접 만들어 `ReceiveAttack` 호출 | [FirstMeleeMainSkill.cs:114](Assets/1.Scripts/Player/Skill/FirstMeleeMainSkill.cs:114), [FirstMeleeUltimateSkill.cs:58](Assets/1.Scripts/Player/Skill/FirstMeleeUltimateSkill.cs:58) |
| 🔴 **단죄의 방패가 미구현** — `interruptSkill` 슬롯이 비어 있다 | [PlayerSkillController.cs:22](Assets/1.Scripts/Player/Skill/PlayerSkillController.cs:22) |
| `InterruptAttack` 앵커 노드는 컴포넌트 0개의 맨 Transform | [Paladin.prefab:5806](Assets/2.Prefabs/Player/Paladin/Paladin.prefab:5806) |
| 현재 우클릭 = `PlayerInterruptState`(전방 돌진 + Animator 트리거), **데미지 0** | [PlayerStateController.cs:604](Assets/1.Scripts/Player/PlayerStateController.cs:604) |
| 슬롯에 스킬이 배정되면 위 상태 경로는 자연 대체된다 | [PlayerStateController.cs:516-520](Assets/1.Scripts/Player/PlayerStateController.cs:516) |
| `AttackType`은 **2곳**에서 직렬화된다 → 값은 반드시 끝에 추가 | [BaseAttack.cs:73](Assets/1.Scripts/Unit/Weapon/BaseAttack.cs:73), [Bomb.cs:6](Assets/1.Scripts/Enemy/Boss/Bomb.cs:6) (`Bomb.cs:12`에서 값 비교) |
| `PlayerGrabbedState`가 `GrabController` 구체 타입으로 소켓을 찾는다 | [PlayerStateController.cs:700-703](Assets/1.Scripts/Player/PlayerStateController.cs:700) |
| `BeginGrabbedByInstigator` 호출부는 1곳뿐 | [GrabController.cs:208](Assets/1.Scripts/Enemy/Boss/GrabController.cs:208) |
| 🟡 잡기와 캐리가 **상태 하나(`Grabbed`)를 공유**한다 — `CanReceiveGrab`이 이미 Grabbed면 거부하고, `EndGrabbed()`는 시작 주체를 안 본다 | [PlayerStateController.cs:344-346](Assets/1.Scripts/Player/PlayerStateController.cs:344), [:166](Assets/1.Scripts/Player/PlayerStateController.cs:166) |
| 스킬 애니 이벤트 릴레이는 존재하나 **Hit 이벤트를 쓰는 스킬이 아직 없다**(Q=홀드틱, E=판정없음, R=채널) | [PlayerAnimationEventRelay.cs:43](Assets/1.Scripts/Player/PlayerAnimationEventRelay.cs:43) |
| Garen Animator에 `Interrupt` 상태가 존재 | `Assets/4.Animations/Player/Garen/PlayerAnimatorController.controller` |

## 명시적 가정 (설계 문서 부재 — 확정되면 SO 수치만 조정)

단죄의 방패의 거동 스펙은 `Docs/design/`에 없다(`character_garen.md`는 전부 TBD, 인계문이 참조한
`boss-fsm-design.md`는 존재하지 않음). 아래를 가정하고 진행한다.

1. **단죄의 방패 = 짧은 전방 방패 강타.** 데미지 1회 + `AttackType.Interrupt` 태그. 카운터 창·정면 각도·
   그로기 전이는 **전부 보스 쪽 책임**(인계문 §A 명시)이므로 플레이어는 태그만 싣는다.
   - `Docs/design/level-system.md:64-65`의 "가붕이 = 패링" 해석은 채택하지 않는다 — 방어 창 기반 패링은
     "히트를 보스에 보낸다"는 인계문 계약과 어긋나고 범위가 훨씬 크다. 기획 확정 시 재논의.
2. **애니메이션 클립을 수정하지 않는다.** 판정 타이밍은 SO의 `hitDelay` 타이머로 잡고,
   `Hit` 애니 이벤트가 나중에 심어지면 그쪽이 우선하도록 **둘 다 받되 1회만 발동**한다.
   (클립은 아트/SVN 관할이고, 현재 어떤 스킬도 Hit 이벤트를 안 쓴다 = 미검증 경로.)
3. 수치는 전부 임시값. `attackDamageMultiplier`·쿨타임은 기획 확정 전 placeholder.

## 접근

### A. 인터럽트 식별자 + 단죄의 방패

1. **인터럽트는 `AttackType`이 아니라 `AttackInfo.isInterruptAttack`(기존 `isGroggyAttack` 개명)이 싣는다.**
   - 이유: `AttackType`은 "어느 출처가 쐈나"이고 인터럽트는 그와 **직교한 능력**이다. enum 값으로 넣으면
     "Q 슬롯인데 인터럽트인 스킬"을 표현할 수 없고, 같은 사실을 두 곳에서 말하게 된다.
   - **플래그는 하나만 둔다.** `AttackInfo`는 공격자가 아는 사실("인터럽트다")만 싣고,
     **소비 방식은 수신측이 정한다** — 몬스터/중간보스는 `maxGroggyCount`까지 누적→그로기
     ([MonsterBase.cs:775](Assets/1.Scripts/Monster/MonsterBase.cs:775) · [BossBase.cs:375](Assets/1.Scripts/Monster/Boss/BossBase.cs:375)),
     보스 No.23은 카운터 창·정면 각도 판정(경석). `Docs/design/level-system.md:70`의 분담과 일치한다.
   - 곁들여 **`AttackType`을 `{None, Default, Skill}`로 축소** — Q/E/R을 구분해 읽는 코드가 없었다.
     정수값 `None=0`·`Default=1`은 **고정**(기존 에셋 25곳과 `Bomb.attackType=1`이 여기 걸려 있다).
   - **`BaseAttack`의 저작 토글은 삭제.** 인터럽트를 켜는 주체는 스킬뿐이고 스킬은 `BaseAttack`을 안 탄다 —
     켤 수 없는 체크박스는 거짓 약속이다.
2. `FirstMeleeInterruptSkillData : PlayerSkillData` 신설 — `hitDelay`, `skillDuration`, `maxHitResults`.
3. `FirstMeleeInterruptSkill : PlayerInstantSkill` 신설 — `Slot => Interrupt`.
   서버가 `hitDelay` 경과(또는 `Hit` 애니 이벤트) 시 `HitboxAnchor` 기준 Overlap →
   `new AttackInfo(damageSnapshot, AttackType.Interrupt)` → `ReceiveAttack`. `skillDuration`에 자체 종료.
   판정은 **1회만**(`hasResolvedHit` 래치).
4. SO 에셋 `Assets/9.ScriptableObject/Player/Garen/FirstMeleeInterruptSkillData.asset`.
5. `Paladin.prefab` 배선: `InterruptAttack` 노드에 `BoxCollider`+`ColliderInfo`,
   루트에 `FirstMeleeInterruptSkill`, `PlayerSkillController.interruptSkill` 연결.

### B. ~~캐리 소켓 일반화~~ → **폐기.** 돌진이 넉백+기절 방식으로 바뀌며 불필요해졌다 (2026-08-07)

`ICarrySocketProvider` 코드는 원복했다. 소켓 제공자가 `GrabController` 하나뿐이라 `Kind` 탐색 자체가 무의미했다.

### B'. `Restrained` — 서버 구속의 단일 상태 (경석 개정 1판 승인)

넉백+기절만으로는 "밀고 가기"가 안 된다(§리스크 C-1 — `Unit.Knockback`은 임펄스 1회, duration 개념이 없다).
그래서 잡기와 밀기를 **한 상태의 두 모드**로 묶는다.

6. `PlayerActionState.Grabbed` → **`Restrained`** 개명(정수값 유지). `PlayerGrabbedState` → `PlayerRestrainedState`.
   `GrabInteractionContext` → `RestraintContext`, `IGrabInteractionReceiver` → `IRestraintReceiver`.
7. `enum RestraintMode : byte { Carry = 0, Push = 1 }` — `Tick()`의 목표 자세만 갈린다:

   | 모드 | 추종 대상 |
   |---|---|
   | `Carry` | `GrabController.GrabSocket` (**기존과 동일**) |
   | `Push` | `instigator.position + instigator.forward × frontOffset` |

   **Push는 소켓도 방향/속도 동기화도 안 쓴다.** 시전자 루트가 `NetworkTransform`으로 복제되므로 오너
   클라에서 월드 위치가 저절로 맞는다 — "돌진 소켓은 애니메이션으로 안 움직이는 고정 자식이어야 한다"는
   제약이 사라진다. Y는 **진입 시점 값으로 고정**한다(캐리 중 `isKinematic`이라 중력이 없고,
   시전자 Y를 그대로 쓰면 피벗 높이 차이로 플레이어가 조용히 뜨거나 잠긴다).
8. **`Push`만 슈퍼아머를 거부한다** — `Unit.Knockback`과 같은 규칙(슈퍼아머면 안 밀린다)을
   플레이어 쪽 한 곳에 둔다. `Carry`는 원래 슈퍼아머와 무관하게 걸렸고 바꾸면 보스 Grab 체인이 회귀한다.
   판정은 **서버 진입(`TryReceiveRestraint`)에서만** 한다 — 오너가 다시 판정하면 복제 지연 시 상태가 갈린다.
9. **`BeginRestrainedByInstigator`가 `bool`을 반환한다.** 시전자는 이 값으로 후처리를 가른다
   (돌진이 벽에 닿았을 때 **실제로 밀린 대상만** 기절). 데미지는 이 값과 무관한 별도 경로.
10. `BeginGrabbedByInstigator`/`EndGrabbedByInstigator`는 **`Carry` 호환 래퍼로 유지** —
    [GrabController.cs:208](Assets/1.Scripts/Enemy/Boss/GrabController.cs:208)·[:228](Assets/1.Scripts/Enemy/Boss/GrabController.cs:228)은 무수정이다.
11. 🔴 **`followTarget == null` 널 허용을 유지한다** — 위치 추종만 건너뛰고 물리 위임·입력 차단은 그대로 간다.
    보스에 잡기 소켓이 아직 없는 동안 "제자리에 붙잡힘"이 이 성질로 성립 중이다(경석 요청).

## 네트워크 권한 가정

- 단죄의 방패: 오너 입력 → 서버 승인(`PlayerSkillController`) → 서버만 판정. 기존 스킬 계약과 동일.
- `Restrained`: **상태 전이·해제·슈퍼아머 판정 = 서버**, **위치 추종 실행 = 오너**(`IsMovementAuthority`).
  서버가 위치를 직접 쓰지 않으므로 "플레이어 이동은 오너 권한"(networking.md) 원칙이 유지된다.
  `Transform`은 복제 불가이므로 RPC엔 **모드(byte) + offset(float)만** 싣는다.
  입력 차단은 별도 계통이 아니라 **상태 진입만으로** 성립한다(`CanMove`/`CanUseSkill`이 `Idle|Move` 한정).

## 리스크 / 한계

- 🟡 **잡기와 돌진 밀기가 `PlayerActionState.Restrained`를 공유한다.** 구속 중 재진입은 조용히 거부되고
  (`CanReceiveRestraint`), `End`는 시작 주체를 구분하지 않는다. 보스 1기 기준 무해 — **계약으로 명시**한다.
- 🔴 **C-1: `Unit.Knockback`은 임펄스 1회다.** `AttackInfo`의 `knockbackDuration`·`staggerDuration`은
  `MonsterBase`만 소비하고 플레이어 수신 경로는 무시한다. 이 사실 때문에 보스 돌진이 넉백이 아니라
  `Restrained.Push`로 간다(경석 확인·설계 변경 완료).
- 🟡 구속 중 `detectCollisions = false`라 플레이어가 벽을 통과한다.
  **벽 판정은 보스 책임** — 캐리 중이면 벽에서 떨어진 지점에서 돌진을 정지한다(경석 수용).
- 🟡 단죄의 방패 판정 타이밍이 애니 클립과 어긋날 수 있다(타이머 기반). SO 수치로 맞추고,
  정밀 타이밍이 필요하면 클립에 `HandleSkillEvent(0)` 이벤트를 심는다.
- 🔴 프리팹 배선은 Unity 재임포트가 필요하다 — 인스펙터에서 컴포넌트가 보이는지 육안 확인 필수.

## 범위 밖

- 보스 쪽 카운터 창·정면 각도·그로기/Break 전이·시각 피드백 (경석).
- 돌진 소켓 자체의 생성/배치 (경석).
- 패링 해석의 방어 창/반사 메커니즘 (기획 확정 후 별건).
- `PlayerInterruptState` 제거 — 스킬 미배정 시의 폴백으로 남긴다.

## 완료 조건

1. C# 컴파일 0 에러. ✅ (경고는 전부 기존 파일)
2. 직렬화된 `attackType` 값이 변하지 않는다 — `0`×22 / `1`×3 유지, 특히 `Bomb.attackType=1`(평타 반응). ✅
3. 우클릭 → 단죄의 방패 발동 → 적중 시 수신측이 `attackInfo.isInterruptAttack == true`를 본다. ⏳ Play 필요
4. 중간보스(GauntletBot·SpinnerBot·WallBot)가 인터럽트 누적으로 그로기에 들어간다
   (`maxGroggyCount` 3/3/4 — **이전엔 켜는 주체가 없어 사실상 죽은 경로였다**). ⏳ Play 필요
5. 기존 잡기(Grab → Hold → Throw) 회귀 없음 — `GrabController` 무수정, `Carry` 래퍼 경유. ⏳ Play 필요
5-1. `BeginRestrainedByInstigator(boss, Push, offset)` → 플레이어가 보스 정면을 따라간다. ⏳ Play 필요
5-2. 슈퍼아머(Q 홀드) 중이면 `Push`가 **false를 반환**하고 밀리지 않는다. `Carry`는 그대로 걸린다. ⏳ Play 필요
6. MPPM 2인(호스트/클라) 검증. ⏳

> ⚠️ 프리팹·SO에 `isGroggyAttack` YAML 키 24개가 고아로 남는다(전부 값 0). Unity가 해당 에셋을
> 재직렬화할 때 자연 소멸한다 — 의미 손실은 없고 diff 노이즈로만 나타난다.

---

# CURRENT PLAN — 카메라 쉐이크 + HP 비네트 (2026-08-06)

---

# CURRENT PLAN — Relay 로비 신설 (Phase 2) (2026-08-07)

> 상태: **승인 대기**. 브랜치 `feature/SessionTransport` 이어서 사용(Phase 1 이 base).
> grill 완료 — 확정된 결정만 담는다.

## 목표

**기존 IPv4 로비를 그대로 두고**, 같은 씬 안에 **Relay 연결 통로를 신설**한다.
호스트는 조인코드를 발급받아 화면에 표시하고, 참가자는 그 코드로 들어온다.

## 확정된 결정 (grill)

| 항목 | 결정 |
|---|---|
통로 | **기존 `3.BeaverLobby` 안에 Relay 패널 신설.** 새 씬을 만들지 않는다 |
IPv4 | **그대로 유지**(교체 아님). 출시 전 제거 예정 |
라우팅 | `GameManager.lobbySceneName` · 빌드 목록 **무수정** — 로비 슬롯이 하나라 새 씬을 만들면 팀장 담당 영역(부팅 라우팅)을 건드려야 한다 |
Steam | 나중에 **토글 하나 더** 추가하는 형태로 확장 (Phase 3) |

## 현재 이해 (조사 완료)

| 사실 | 근거 |
|---|---|
Relay API 가 패키지에 **이미 있다** | `com.unity.services.multiplayer@…/Runtime/Relay/SDK/IRelayService.cs` (`CreateAllocationAsync`·`JoinAllocationAsync`) |
`UnityTransport.SetRelayServerData(RelayServerData)` 오버로드 존재 | `UnityTransport.cs:815` |
UGS 프로젝트 연결 완료 | `cloudProjectId c5d06f51-…` / `organizationId rangspam` |
Phase 1 추상화가 이미 있다 | `ISessionConnectionProvider` · `BeginHost/BeginClient` · `SessionStartCompleted` |
로비 매니저의 사용자 피드백 창구는 `SetErrorMessage` 하나 | `BeaverLobbySceneManager` 전역에서 사용 |
로비 버튼은 OnClick → `ApplyConnectionData`/`StartHost`/`StartClient`/`ToggleReady`/`StartGameLoading` | `SerializeField` Button 5개 |

## 접근

### A. 고전 Relay API 를 쓴다 (Sessions API 아님)

패키지에 상위 **Sessions API**(`MultiplayerService.CreateSessionAsync`)도 있지만 **쓰지 않는다**.
그쪽은 로비·Relay·NGO 시작을 한꺼번에 소유하려 들어서, 이미 있는
`NetworkSessionLauncher` + `NetworkLoadingFlowController` 계약과 충돌한다.

고전 API 는 Phase 1 프로바이더 모양과 정확히 맞는다 — **Prepare 단계에서 연결 데이터만 채우고,
`NetworkManager.StartHost()` 호출은 런처가 계속 소유**한다.

```
호스트: CreateAllocationAsync(maxConnections: 2)     // 3인 협동 = 호스트 + 2
        → GetJoinCodeAsync(allocation.AllocationId)
        → SetRelayServerData(new RelayServerData(allocation, "dtls"))
        → ShareCode = 조인코드
참가:   JoinAllocationAsync(joinCode)
        → SetRelayServerData(new RelayServerData(joinAllocation, "dtls"))
```

### B. UGS 초기화·로그인 부트스트랩 (신규)

Relay 호출 전에 `UnityServices.InitializeAsync()` + **익명 로그인**이 끝나 있어야 한다.
`UnityServicesBootstrap`(신규)이 **멱등**하게 처리하고, 상태를 프로바이더가 읽는다.

- `RelayConnectionProvider.IsAvailable(out reason)` 이 미초기화·미로그인·`cloudProjectId` 부재를
  **각각 다른 사유 문자열**로 반환한다. "접속 실패"로 뭉개지 않는다.
- 🔴 **MPPM 프로필 분리**: 익명 로그인은 자격증명을 프로젝트 단위로 캐시한다. MPPM 클론들이
  같은 것을 물면 **같은 플레이어로 취급돼 충돌**한다. 로그인 전에
  `AuthenticationService.Instance.SwitchProfile(<클론별 고유값>)` 을 호출한다.

### C. 로비 UI — 패널 추가, 기존 것은 무수정

`BeaverLobbySceneManager` 에 Relay 전용 필드·메서드를 **추가**한다. 기존 IPv4 필드·메서드는 손대지 않는다.

- 추가 `SerializeField`: `relayPanel` · `directPanel` · `joinCodeInputField` ·
  `joinCodeDisplayText` · `relayHostButton` · `relayJoinButton` · `modeToggleButton`
- 추가 public 메서드(OnClick 대상, 전부 `void`): `SelectDirectMode()` · `SelectRelayMode()` ·
  `StartRelayHost()` · `StartRelayJoin()`
- **비동기 결과는 `SessionStartCompleted` 구독으로 받는다** — Phase 1 이 만든 경로다.
  성공 시 `joinCodeDisplayText` 에 `ShareCode` 를 띄우고, 실패 시 `FailureReason` 을
  `SetErrorMessage` 로 흘린다.
- 연결 중에는 버튼을 잠근다(중복 클릭이 Allocation 을 두 번 만든다).

⚠️ 프리팹·씬 배선(패널·입력칸·버튼 생성 및 참조 연결)은 **은희가 Unity 에서** 한다.
코드는 참조가 비어 있어도 예외 없이 동작해야 한다(전부 null 안전).

## 변경 파일

| 파일 | 변경 |
|---|---|
`Assets/1.Scripts/Network/Session/RelayConnectionProvider.cs` | **신규** |
`Assets/1.Scripts/Network/UnityServicesBootstrap.cs` | **신규** — 멱등 초기화 + 익명 로그인 + MPPM 프로필 분리 |
`Assets/1.Scripts/Network/NetworkSessionLauncher.cs` | Relay 프로바이더 등록(`TryGetProvider` 분기 확장) |
`Assets/1.Scripts/Managers/BeaverLobbySceneManager.cs` | Relay 필드·메서드 **추가**(기존 무수정) |

씬·프리팹·`.meta` 무수정(코드만). `GameManager` 무수정.

## 완료 조건

1. **IPv4 경로 회귀 0** — 기존 IP/Port 접속이 이전과 동일.
2. Relay 호스트 → 조인코드 화면 표시 → 다른 인스턴스가 그 코드로 참가 → 게임 진행.
3. 실패 경로가 **사유와 함께** 표시된다: UGS 미초기화 / 로그인 실패 / 잘못된 조인코드 /
   방 정원 초과. 조용한 실패 0.
4. 카메라 리그·프로바이더·패널 참조가 없어도 예외 0.
5. 컴파일 0 에러 / 0 경고. 신규 `.cs` UTF-8(BOM).
6. MPPM 2~3인에서 Relay 경로 정상(프로필 분리 확인).

## 리스크

- ❓ **대시보드에서 Relay 서비스가 활성화됐는지 미확인.** 안 돼 있으면 코드는 돌아도
  Allocation 생성이 실패한다. Phase 2 검증 전 확인 필요.
- Relay 는 **과금·할당량**이 있는 서비스다. 무료 티어 한도를 넘기면 개발 중에도 막힌다.
- `"dtls"` 연결 타입이 플랫폼·버전에 따라 `"udp"`/`"wss"` 로 달라질 수 있다. 실패 시 사유 로그로 판별한다.
- MPPM 프로필 분리 값을 어떻게 얻을지는 구현 중 확정한다(클론 식별자 API 또는 경로 해시).

---

# PLAN (완료) — 세션 연결 방식 추상화 (Phase 1) (2026-08-07)

> 상태: **완료·검증됨.** 커밋 `3c6be4fc0`, 브랜치 `feature/SessionTransport` 푸시.
> Unity 컴파일 + MPPM 검증 통과(IPv4 동작 무변경 확인).
> 이 문서는 **Phase 1(추상화 + 기존 IPv4 이식)** 만 다룬다. Relay·Steam 구현은 Phase 2·3.
> grill 완료 — 확정된 결정만 담는다.

## 목표

세션 연결 방식을 **세 가지(직접 IPv4 / Unity Relay / Steam)** 로 갈아끼울 수 있게 만든다.
Phase 1의 산출물은 **추상화 계층 + 기존 IPv4 구현을 그 위로 이식**하는 것까지다.
**IPv4 동작은 1바이트도 바뀌지 않는다** — 이게 Phase 1의 합격 기준이다.

## 확정된 결정 (grill)

| 항목 | 결정 |
|---|---|
진행 순서 | **추상화 먼저**, Relay·Steam은 그 위에 건씩 얹는다 |
IPv4 직접연결 | **잠정 유지, 출시 전 제거.** 개발 중 디버깅·랜 환경에서 제일 빠르다 |
비-Steam 로비 | Unity **Relay** (기존 IPv4 직접통신 방침을 대체) |
Steam SDK | 미확정 — Notion 문서(`SteamSDK`)가 인증 걸려 읽지 못했다. Phase 3에서 확정 |

## 현재 이해 (조사 완료)

| 사실 | 근거 |
|---|---|
연결 설정이 **한 곳으로 모여 있다** | `NetworkSessionLauncher.OnSetConnectionData` → `UnityTransport.SetConnectionData` |
`NetworkSessionLauncher`는 `NetworkManager.prefab`의 컴포넌트 | `NetworkClock`·`NetworkLoadingFlowController`와 동거 |
호출자는 로비 매니저 2개 | `BeaverLobbySceneManager`(ip+port) · `LobbySceneManager`(ip only, 1인자 오버로드) |
`CamaraScene.unity`도 이 컴포넌트를 참조 | GUID 스캔 |
**Relay는 트랜스포트를 바꾸지 않는다** | `UnityTransport`가 `SetRelayServerData`로 처리 |
Relay SDK는 **이미 설치돼 있다** | `com.unity.services.multiplayer 2.2.3` + `authentication`·`core`·`qos`·`wire` 해석 완료 |
🔴 **UGS 프로젝트 미연결** | `ProjectSettings.asset`의 `cloudProjectId`·`organizationId`·`projectName` 전부 빈 값 |

## 🔴 핵심 구조 문제 — 동기 API로는 Relay를 표현할 수 없다

현재 계약은 **동기 `bool`** 이다:

```csharp
public bool StartHost()     // 즉시 성공/실패
public bool StartClient()
public void OnSetConnectionData(string ip, ushort port)
```

Relay는 호스트가 **Allocation 생성 → 조인코드 발급**, 클라가 **조인코드로 Allocation 조회** 를 해야 하고
둘 다 **await 가 필요한 원격 호출**이다. Steam도 로비 생성/입장이 콜백 기반이다.
그래서 Phase 1의 본질은 **계약을 비동기로 바꾸고 "연결 중" 상태를 만드는 것**이다.

## 접근

### A. 연결 방식을 인터페이스로 분리

```csharp
public enum SessionConnectionMode { DirectIPv4, UnityRelay, Steam }

/// 사용자에게 보여줄 결과. 실패 사유를 문자열로 들고 온다 —
/// 조용한 실패를 만들지 않는다(이 레포에서 반복해 당한 부류).
public readonly struct SessionStartResult
{
    public readonly bool Success;
    public readonly string FailureReason;
    public readonly string ShareCode;   // 호스트가 남에게 알려줄 값
                                        // IPv4="192.168.0.5:7777" / Relay=조인코드 / Steam=lobbyId
}

public interface ISessionConnectionProvider
{
    SessionConnectionMode Mode { get; }

    /// 쓸 수 있는 상태인지 미리 검사한다. UGS 미연결·Steam 미실행을
    /// "접속 실패"로 뭉개지 말고 이유를 반환한다.
    bool IsAvailable(out string unavailableReason);

    /// 호스트: 트랜스포트에 연결 데이터를 채우고 공유용 코드를 만든다.
    Task<SessionStartResult> PrepareHostAsync(CancellationToken ct);

    /// 클라이언트: 사용자 입력(IP·조인코드·lobbyId)을 해석해 연결 데이터를 채운다.
    Task<SessionStartResult> PrepareClientAsync(string joinInput, CancellationToken ct);
}
```

`Prepare*Async` 는 **트랜스포트 설정까지만** 한다. `NetworkManager.StartHost()` 호출은
`NetworkSessionLauncher` 가 그대로 소유한다 — 시작 순서와 로딩 흐름 콜백 등록을 한 곳에 남긴다.

### B. Phase 1 구현체는 하나뿐 — `DirectIPv4ConnectionProvider`

지금 `OnSetConnectionData` 가 하는 일을 **그대로** 옮긴다. 특히 이 주석의 함정을 보존한다:

> `SetConnectionData` 를 2인자로 부르면 `ServerListenAddress = ip` 가 되어 호스트가 입력값에
> 바인딩된다. 기본값 `127.0.0.1` 이면 루프백만 듣고 다른 PC 가 접속 못 한다.
> → 바인딩은 항상 `0.0.0.0` 고정.

`IsAvailable` 은 항상 true(로컬 전용이라 외부 의존이 없다).
`PrepareClientAsync` 는 `IPAddress.TryParse` 검증을 여기로 **가져온다** — 지금은 로비 매니저에
있는데, 입력 형식 해석은 방식별로 다르므로(조인코드는 IP 가 아니다) 프로바이더 책임이다.

### C. `NetworkSessionLauncher` — 비동기 계약 + 기존 호출자 보호

```csharp
public SessionConnectionMode Mode { get; set; }   // 기본 DirectIPv4
public Task<SessionStartResult> StartHostAsync(CancellationToken ct)
public Task<SessionStartResult> StartClientAsync(string joinInput, CancellationToken ct)
```

- 내부 순서: 프로바이더 `IsAvailable` → `Prepare*Async` → `NetworkManager.Start*()` →
  `RegisterLoadingFlowCallbacks()`. 기존 `Register...` 호출 시점을 바꾸지 않는다.
- **기존 동기 메서드는 남긴다.** `StartHost()`/`StartClient()`/`StartServer()`/`OnSetConnectionData()` 는
  `[Obsolete]` 표시 + 내부에서 DirectIPv4 경로를 동기로 수행하는 얇은 래퍼로 유지한다.
  이유: **UnityEvent OnClick 은 `Task` 반환 메서드를 바인딩하지 못한다.** 씬·프리팹 배선
  (`CamaraScene`, `NetworkManager.prefab`)이 조용히 끊기는 것을 막는다.
- 로비 매니저용으로 `void` 진입점(`BeginHost()` / `BeginClient(string)`)을 추가한다 —
  내부에서 async 를 시작하고 결과를 이벤트로 흘린다:
  `event Action<SessionStartResult> SessionStartCompleted`.

### D. 로비 UI는 Phase 1에서 건드리지 않는다

`BeaverLobbySceneManager` 의 IP/Port 입력 필드는 그대로 둔다. 조인코드 UI 는 **Relay 가 실제로
붙는 Phase 2** 에 함께 바꾼다. Phase 1 은 배관 교체이므로 화면 변화가 0 이어야 검증이 쉽다.

단, `_sessionLauncher.StartHost()` 의 즉시 `bool` 분기는 **"연결 중" 상태를 표현할 수 없다**.
Phase 1 에서는 기존 동기 래퍼를 계속 쓰게 두고, Phase 2 에서 이벤트 기반으로 바꾼다.
(지금 바꾸면 IPv4 동작 무변경을 보장하기 어려워진다.)

## 변경 파일 (Phase 1)

| 파일 | 변경 |
|---|---|
`Assets/1.Scripts/Network/Session/SessionConnectionMode.cs` | **신규** — enum |
`Assets/1.Scripts/Network/Session/SessionStartResult.cs` | **신규** — 결과 struct |
`Assets/1.Scripts/Network/Session/ISessionConnectionProvider.cs` | **신규** — 인터페이스 |
`Assets/1.Scripts/Network/Session/DirectIPv4ConnectionProvider.cs` | **신규** — 기존 동작 이식 |
`Assets/1.Scripts/Network/NetworkSessionLauncher.cs` | 프로바이더 경유 + 비동기 API 추가. **기존 메서드 시그니처 유지** |

프리팹·씬·`.meta` 무수정. 로비 매니저 무수정.

## 스코프 밖 (Phase 2·3)

- **Phase 2 — Relay**: UGS 연결(대시보드·계정 작업, 사용자 몫) → `UnityServices.InitializeAsync` +
  익명 인증 → `RelayConnectionProvider` → 로비 UI 를 조인코드로 교체 → MPPM 프로필 분리.
- **Phase 3 — Steam**: SDK·트랜스포트 확정 → `NetworkConfig.NetworkTransport` 교체 스위처 →
  `SteamConnectionProvider`. **MPPM 으로 검증 불가**(프로세스당 1회 초기화) → 빌드 2개·계정 2개.
- AGENTS.md 의 "공모전 제출 = IPv4" 문구 갱신 — Phase 2 확정 후.
- `LobbySceneManager` 삭제(구 로비 정리) — 별건. `PLAN.md` 2026-08-03 계획에 있다.

## 완료 조건

1. **IPv4 동작 무변경.** `3.BeaverLobby` 에서 IP·Port 입력 → Host/Client 접속이 변경 전과 동일.
   MPPM 2인 정상. `[SceneFlow]` 로그 시퀀스 동일.
2. 씬·프리팹의 `NetworkSessionLauncher` 배선이 유지된다(OnClick 끊김 0).
3. C# 컴파일 0 에러 / 0 경고 (`[Obsolete]` 래퍼를 내부에서 호출하면 경고가 나므로
   호출 지점에 `#pragma warning disable` 대신 **내부 구현을 공유 private 메서드로 분리**한다).
4. `DirectIPv4ConnectionProvider` 가 바인딩을 `0.0.0.0` 으로 고정한다(회귀 시 다른 PC 접속 불가).
5. 신규 `.cs` 는 UTF-8(BOM).

## 리스크

- 🔴 **UGS 미연결이 Phase 2 의 하드 블로커다.** Phase 1 은 영향 없지만, Relay 검증을 시작하려면
  대시보드 작업이 선행돼야 한다. Relay 는 과금·할당량이 있는 서비스다.
- ⚠️ **Steam 은 MPPM 으로 검증할 수 없다.** 지금까지의 검증 습관이 Phase 3 에서 통하지 않는다.
- NGO 는 활성 트랜스포트가 하나다(`NetworkConfig.NetworkTransport` 단일 참조) → Phase 3 에서
  런타임 교체 스위처가 필요하다. Phase 1 인터페이스는 프로바이더가 "어느 트랜스포트를 쓸지"를
  소유할 수 있게 열어 둔다.
- 비동기 도입으로 **취소·중복 클릭** 경로가 생긴다. `CancellationToken` 을 계약에 넣어두고,
  진행 중 재요청은 Phase 2 UI 에서 막는다(Phase 1 은 동기 래퍼만 쓰므로 노출되지 않는다).
