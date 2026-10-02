# PLAN-boss-timer.md — 보스 제한시간 타이머 (2026-09-19)

> 상태: **승인 대기.** 담당 경석. 브랜치 `feature/Boss23`.
> 그릴 확정: 2026-09-19. 미니맵 룩 작업은 [PLAN-minimap.md](PLAN-minimap.md) 와 **같은 슬라이스로 묶어** 진행한다
> (둘 다 `CombatHUD.prefab` 슬롯을 쓰므로 프리팹 저작을 한 번에 끝낸다).
>
> 🔴 **`PLAN-minimap.md` §7 의 "보스 타이머 = 범위 밖(팀장 지시)" 은 이 문서로 뒤집혔다.**

## 1. 목표

맵 시작 시점부터 도는 **제한시간**을 HUD 에 게이지로 보여주고, 만료되면 **완전 사망(PermanentDead)한
사람을 뺀 전원을 보스룸으로 강제 이동시켜 보스 전투를 시작**한다. 제한시간 안에 스스로 보스방에 들어가면 게이지는 **0(빈 상태)**
으로 두고 멈춘다.

플레이어가 보는 것 = 우하단 게이지가 서서히 차오름 → 꽉 차면 3·2·1 → 보스룸으로 끌려감 → 등장 연출 → 전투.

## 2. 현재 이해 (2026-09-19 실측)

**만들 게 아니라 이어 붙이는 작업이다.** 강제 이동·연출·전투 진입 경로가 이미 전부 서 있다.

| 조각 | 위치 | 하는 일 |
|---|---|---|
| `BossEnterTrigger` | `Map/BossEnterTrigger.cs` | 진입 패드 점유(생존자 유무) 감지 → `SetOccupied` |
| `BossTeleportManager` | `Map/BossTeleportManager.cs` | 점유 시 **3초 카운트다운**(`_teleportAt` = 서버시간 만료시각, NetworkVariable) → 생존자 산개 텔레포트 → 도착 ACK |
| `BossEncounterDirector` | `Map/BossEncounterDirector.cs` | 등장 연출 → 전투 (`BossEncounterPhase`) |
| `DevBossEntranceWarp` | `Dev/` | F5 로 진입 패드까지 워프 (검증용) |

- **타이머 로직은 코드에 전무하다** — `grep -i timer` 로 HUD/게임플레이 관련 0건.
- 아트 3종은 SVN 에 있고 `.meta` 도 전부 있다. **프리팹 참조는 0건**(아직 아무도 안 붙임):
  `50.Art/UI/HUD/slot_timer.png`(3칸 틀) · `gauge_timer.png`(보라→핑크 채움) · `icon_timer_boss.png`(해골 캡).
- 🔴 **`CombatHUD.prefab` 은 씬에 없다. `Player.prefab` 의 자식**이다
  (`Player.prefab` · `Paladin.prefab` · `Paladin_VFX.prefab` 3곳이 참조, 씬 배치 0건).

## 3. 확정 결정 (그릴 2026-09-19)

| # | 결정 | 근거 |
|---|---|---|
| T1 | **제한시간 = 맵 생성 완료부터 5분.** 인스펙터 `LimitSeconds` 로 조절 | 서버의 `MapGenerator.OnGenerated` 는 전 피어가 공유하는 확정 시점이고 서버시간 기준이라 클라 간 오차가 없다. 전원 스폰 판정은 새로 만들어야 해서 비용 대비 이득이 없다 |
| T2 | **게이지는 차오른다**(0 → 1). `InvertFill` 토글로 줄어듦 전환 | 🔴 **기획 미확정.** 팀장 판단으로 "차오른다"로 가되, 주말 이후 기획과 합의하면 뒤집힐 수 있다 → **코드 수정 없이 인스펙터 한 칸으로 반전**되게 만든다 |
| T3 | 타이머 상태는 **서버 권한 `NetworkVariable`**, 클라는 표시만 | AGENTS.md §4 "보스 = 서버 권한". `BossTeleportManager._teleportAt` 과 **같은 패턴** — 만료 시각(서버시간)만 복제하고 매 프레임 복제는 안 한다(대역폭 0) |
| T4 | 정지 시점 = **도착 확정**(`AlivePlayersArrived`), 패드 밟은 순간이 아님 | 패드 카운트다운은 **이탈하면 취소**된다(로아식). 밟자마자 멈추면 이탈 후 타이머가 죽은 채로 남는다 |
| T5 | 만료 → **경고 3초 후** 강제 이동 | 갑자기 끌려가는 느낌을 줄인다. 기존 `_teleportAt` 3·2·1 표시를 **그대로 재사용**하므로 새 UI 가 필요 없다 |
| T6 | 강제 이동은 `BossTeleportManager` 에 **서버 전용 `ForceStartEncounter(warnSeconds)`** 를 새로 열어 기존 텔레포트 경로를 재사용 | 산개 좌표·ACK·페이드·연출 진입이 전부 그 안에 있다. 복제하면 두 경로가 갈린다 |
| T7 | 🔴 **개정(Codex 교차검증 2026-09-20).** 만료는 **"발동"과 "소멸"을 분리한다.** 이미 진행 중이면 **발동만 미루고 만료 사실은 유지**하고, 그 진행이 취소되면 **강제 경고로 이어받는다** | 원안(`Busy 면 타이머만 종료`)은 **제한시간을 통째로 우회시키는 구멍**이었다: 만료 직전에 패드를 밟으면 타이머가 종료되고, 그 사람이 패드에서 나가면 카운트다운도 취소돼 **보스 이동도 제한시간도 사라진다** |
| T8 | 🔴 **이동 대상 = `PermanentDead` 가 아닌 전원** (Alive · DeadPresentation · Soul). 목숨이 남아 부활 가능한 사망자는 **강제 이동·패드 진입 양쪽 모두에서 같이 데려간다** | 팀장 확정 2026-09-20. 근거(팀장 원문): **"유령인데 목숨이 있으면 이동해야 한다 — 보스방에서 목숨을 써서 살아나 싸운다"**. 즉 Soul 은 '탈락자'가 아니라 **자원을 들고 합류 대기 중인 참가자**다. 옛 맵에 두고 가면 부활해도 합류할 길이 없다. `PermanentDead` 만 제자리 |
| T9 | 이동 가능한 인원이 0명이면 **아무것도 하지 않는다** | 전멸은 `PartyWipeWatcher` 영역이다. 완전 사망한 시체를 보스룸으로 옮기지 않는다 |
| T10 | UI 는 **`CombatHUD.prefab` 안 슬롯**(팀장 확정) | 키가이드·스킬바와 한 프리팹에서 같이 보며 배치. 미푸시 충돌 위험은 해소됨 |
| T11 | 프리팹 저작은 **에디터 메뉴 스크립트**로 한다 — YAML 직접 편집 금지 | `CombatHUD.prefab` 은 5165줄이다. 손으로 쓰면 HUD 전체가 조용히 깨진다. 기존 선례: `BossRoomAuthoring` · `MonsterSceneBossSetup` · `BossVariantAuthoring` 전부 이 방식 |
| T12 | **숫자 텍스트 없음.** 게이지만 | 레퍼런스(`minimap.png` 목업)에 숫자가 없다 |
| T13 | 🔴 **카운트다운 실행 원인을 `Pad` / `Forced` 로 구분해 저장한다. 패드 이탈은 `Pad` 만 취소한다** | 지금은 `_pending` 하나를 두 경로가 공유해서, 강제 경고 중 **패드에 들어왔다 나가기만 해도** `SetOccupied(false)` 가 강제 코루틴을 `StopCoroutine` 하고 `_teleportAt=0` 으로 만든다. 팀장·Codex 양쪽이 독립적으로 같은 결론 |
| T14 | 🔴 **만료 시 Soul 은 강제 부활시킨 뒤 이동한다** — Soul 전원을 **각자 목숨 1개씩 소모**해 Alive 로 올리고, 그 다음 텔레포트 | 팀장 확정 2026-09-20. 이유는 T15 를 보라 — 안 그러면 **보스가 아예 안 나온다**. 목숨이 없어 부활에 실패한 사람은 `PermanentDead` 취급(제자리) |
| T15 | **`BossEncounterDirector` 의 참가자 판정을 `State == Alive` → `State != PermanentDead` 로 넓힌다** | `IsAliveParticipant` 가 Alive 만 통과시켜 **도착자가 전부 Soul 이면 `_eligibleClientIds` 가 0 → "생존 참가자가 없어 연출을 시작하지 않습니다" → Idle 복귀**. 게다가 **부활해도 Director 를 다시 깨우는 구독이 없어 영구 교착**이다. 패드 경로에서도 Soul 이 시네마틱 잠금을 못 받는 문제가 같이 풀린다 |
| T16 | 🔴 **기존 버그 동반 수정 — 호스트 ACK 조기 확정** (팀장 확정: 이번에 같이 고친다) | 상세는 아래 별도 절 |
| T17 | **강제 이동 시 `PlayerFallRecovery` 의 지연 복귀를 취소한다** | 추락 복귀는 안전지점을 저장해 **지연 코루틴으로 되돌리는** 구조다. 대기 중에 보스룸으로 옮겨지면 **도착 후 옛 안전지점으로 끌려간다** |

### 🔴 T16 — 호스트 ACK 조기 확정 (기존 버그, 내 작업과 무관하게 이미 있음)

`TeleportAlivePlayers` 는 루프 안에서 `_awaitingArrival.Add(clientId)` **직후에** `TeleportOwnerClientRpc` 를 보낸다.
NGO 는 호스트가 대상인 ClientRpc 를 **동기로 로컬 실행**한다
(`NetworkBehaviour.cs` 의 `clientRpcMessage.Handle(ref context)` — 2026-09-20 소스 확인).

→ 호스트가 slot0 이면: `add(호스트)` → RPC 동기 실행 → `ArrivalAppliedServerRpc` 동기 실행 → `remove` →
`_awaitingArrival.Count == 0` → **원격 참가자를 등록하기도 전에 `CompleteArrival()`**.

**왜 9/18 3인 세션에서 안 보였나** — 보스는 뜨고 전투도 굴러가기 때문이다. 실제 피해는
`_eligibleClientIds` 가 **호스트 1명으로만 스냅샷**되는 것이다(나머지는 시네마틱 잠금을 못 받는다).
뒤늦은 원격 ACK 가 `CompleteArrival` 을 재발화해 Director 가 "이미 진행 중이라 무시" 경고를 남긴다.
**증상이 조용해서 통과했다** — 로그에 그 경고가 있었는지 확인할 것.

**수정** — 순서를 바꾼다: ① 대기 명단 **전원 등록** → ② ACK 타임아웃 준비 → ③ 그 다음 RPC 전송,
그리고 `CompleteArrival` 은 **시퀀스당 1회**로 제한한다. 기존 시퀀스 검사는 그대로 둔다.

### 🔴 T10 의 알려진 대가 — 사망하면 타이머가 사라진다

`PlayerCombatUiLifecyclePolicy.ApplyState()` 가 `hudCanvas.enabled = (Alive || Soul)` 로 캔버스를
통째로 끈다. 즉 **완전 사망(PermanentDead) 하면 보스 타이머·미니맵도 같이 사라진다.**
자식 Canvas 로 빼도 소용없다(부모 Canvas 가 꺼지면 하위 트리 전체가 안 그려진다).

팀장에게 브리핑했고 **그대로 간다**는 판단을 받았다. 완화안은 두 가지이며 **이번 범위에는 넣지 않는다**:

- (a) `PlayerCombatUiLifecyclePolicy` 에 예외 목록을 둔다 — **플레이어 UI 수명 코드는 은희 영역**이라 합의 필요.
- (b) 런타임에 슬롯만 상주 Canvas 로 리페어런트 — 은희 코드 무수정. 저작은 프리팹에서 그대로.
  **디자이너가 보는 프리팹 계층과 런타임 계층이 달라지는** 대가가 있다.

## 4. 접근 — 슬라이스

- **T-S1. 상태**(UI 없음) — `BossTimerManager : NetworkBehaviour` 를 `BossTeleportManager` 와 **같은 씬 상주
  GameObject** 에 붙인다(같은 NetworkObject 에 NetworkBehaviour 여러 개는 정상).
  - `NetworkVariable<double> _expiresAt` (서버시간, 0 = 비활성) · `NetworkVariable<byte> _state`
    (`Running` / `Stopped` / `Warning`).
  - 서버: `MapGenerator.OnGenerated` → `_expiresAt = ServerTime.Time + LimitSeconds`.
  - 검증은 로그로만. **UI 가 없어도 만료가 도는지 먼저 본다.**
- **T-S2. 만료 → 강제 이동** — `BossTeleportManager.ForceStartEncounter(float warnSeconds)` 추가
  (서버 전용). `_teleportAt` 을 세팅해 기존 3·2·1 표시를 태우고 기존 `TeleportAfter` → `TeleportAlivePlayers`
  경로로 합류한다. **실행 원인을 `Pad`/`Forced` 로 저장**하고(T13), 패드 이탈은 `Pad` 만 취소한다.
  진행 중이면 발동을 미루되 **만료 사실은 유지**하고, 그 진행이 취소되면 이어받는다(T7).
  이동 직전에 **Soul 강제 부활**(T14) → `PlayerFallRecovery` 지연 복귀 취소(T17) 순서로 처리한다.
  - `BossTimerManager` 는 `AlivePlayersArrived` 구독 → `Stopped`.
  - 🔴 **이동 대상 판정을 바꾼다**(T8). 현재 `TeleportAlivePlayers` 는 `unit.CurrentHealth <= 0` 이면 건너뛰어
    **Soul 을 두고 간다**. 이것을 `PlayerLifeCycleController.State != PermanentDead` 로 바꾼다
    (`State` 는 이미 서버 write NetworkVariable 이라 서버가 그냥 읽으면 된다).
    ⚠️ **이 파일은 패드 진입 경로와 공유된다 — 평소 보스방 입장 때도 Soul 이 따라온다. 이것이 의도다**
    (팀장 확정 2026-09-20). 기존 주석의 "생존자만 이동(팀장 확정)" 규칙은 **이 계획이 개정한다.**
    구현 시 그 주석도 함께 고쳐, 다음 사람이 옛 규칙을 근거로 되돌리지 않게 한다.
- **T-S3. 프리팹 저작** — 에디터 메뉴 `Tools/UI/Authoring/CombatHUD — 미니맵·보스타이머 슬롯 생성`.
  - `BossTimerSlot`: `slot_timer`(틀) + `gauge_timer`(Image, `Filled`/`Horizontal`) + `icon_timer_boss`(캡).
  - `MinimapSlot`: RectTransform 만 (내용은 `MinimapController` 가 채운다 — PLAN-minimap S4).
  - **멱등**하게 만든다 — 다시 돌려도 중복 생성되지 않고 위치만 갱신. 아트 배선이 끊기면 다시 돌려 복구.
- **T-S4. 표시** — `BossTimerHUD : MonoBehaviour`(표시 전용, 네트워크 미접촉).
  `fillAmount = (state == Stopped) ? 0 : 남은시간 → 0..1` , `InvertFill` 적용.
  `BossTimerManager` 가 없으면 슬롯을 숨긴다(로비·보스 없는 씬).
- **T-S5. 미니맵 합류** — PLAN-minimap S4 를 이 슬롯에 붙인다.

## 5. 네트워크 권한

- 새 `NetworkVariable` **2개**(서버 write / 전원 read), 새 RPC **0개**.
- 강제 이동은 **기존 서버 경로 재사용** — 새 권한 경계를 만들지 않는다.
- T14 의 강제 부활도 **서버에서만** 돈다(`TryCompleteReviveOnServer` 는 `IsServer` 가드가 이미 안에 있다).
- T16 은 **RPC 전송 순서만** 바꾼다. 메시지 형식·시퀀스 검사는 그대로라 프로토콜 호환성 영향 없음.
- 클라는 `ServerTime.Time` 과 복제된 만료 시각의 **차이만 계산**해 그린다. 클라 로컬 시간은 안 쓴다.

## 6. 리스크

> 🟢 2026-09-20 Codex 교차검증으로 1~3 은 **결정 항목으로 승격**되어 T7·T13~T17 로 흡수됐다.
> 아래는 **아직 안 닫힌 것**만 남긴다.

1. 🔴 **Soul 텔레포트가 유지되는지 정적 분석으로 판정 불가** — Soul 은 같은 NetworkObject 를 쓰고
   `SoulAccess.allowsMovement = true` 라 Teleport 가 거부되는 구조는 아니다. 다만 기존 RPC 는
   **Motor 의 내부 속도·예약 이동을 정리하지 않는다.** 다음 틱에 옛 좌표로 되돌아가는지는 **MPPM 실측**해야 안다.
   (만료 경로는 T14 로 전원 Alive 가 되므로 이 리스크는 **패드 경로의 Soul 동반 이동**에 남는다.)
2. **`DeadPresentation` 은 "목숨이 남은 사람" 과 동의어가 아니다** — 목숨 검사는 **연출이 끝날 때** 한다.
   즉 곧 `PermanentDead` 가 될 사람도 현재 조건이면 끌려간다. 위치 변경이 사망 전환 타이머를 초기화하진
   않지만, **연출이 시각적으로 깨지는지는 미확인**이다.
3. **Soul 은 이동 전 암전(사전 페이드)에서 빠진다** — 기존 페이드가 HP 조건을 쓰기 때문이다.
   Soul 은 암전 없이 이동 후 페이드인만 받는다. 만료 경로는 T14 로 Alive 가 되므로 해당 없음.
4. **씬 NetworkObject 스폰 순서는 보장되지 않는다** — `MapNetworkSync` 와 `BossTeleportManager` 는
   서로 다른 씬 오브젝트이고 NGO 는 **정렬 없는 검색**으로 모아 순회한다. 따라서
   "`OnNetworkSpawn` 이 `OnGenerated` 보다 먼저" 라고 가정하면 안 된다.
   🔴 또한 **보류분을 `스폰시각 + 300` 으로 계산하면 T1(생성 시점 기준)을 어긴다** —
   **실제 생성 시각을 보관**했다가 `IsSpawned && IsServer` 에서 **한 번만** 적용한다.
5. **사망 시 UI 소실**(T10) — 기능 결함이 아니라 합의된 대가. 다음 세션에 은희와 논의.
6. **아트 `.meta` 인양 사고 재발** — 9/19 에 `.meta` 누락으로 스프라이트 참조 7건이 끊긴 전례가 있다.
   저작 스크립트가 **멱등**이라 다시 돌리면 복구된다(T-S3).
7. **ACK 실패 복구 정책이 없다** — 현재 `AbortArrival` 은 `ArrivalAborted` 만 쏜다. 강제 이동이 ACK
   타임아웃으로 취소되면 **만료 상태를 유지해 다시 시도**해야 한다(T7 과 같은 이유).

## 7. 범위 밖

- 제한시간을 난이도/스테이지별로 바꾸는 데이터화 — 지금은 인스펙터 한 값.
- **부활 조건 자체의 설계** — 현재 `PlayerReviveController` 는 F10 디버그 트리거뿐이고 실제 조건이 미정이다.
  T14 의 강제 부활은 **서버 승인 진입점(`TryCompleteReviveOnServer`)을 그대로 호출**할 뿐, 조건 설계는 건드리지 않는다.
- `BossEncounterDirector` 의 **연출 내용·페이즈 구조** — 참가자 판정(T15) 한 줄만 손댄다.
- 만료 시 페널티(보스 강화 등) — 팀장 확정 = **강제 이동·전투 시작만**.
- 숫자 카운트다운 텍스트 · 사운드 · 경고 연출.
- `PlayerCombatUiLifecyclePolicy` 수정(은희 영역).
- 보스 FSM 백로그 B1~B10.

## 8. 완료 기준

1. 맵 생성 후 게이지가 **0 → 꽉 참**까지 `LimitSeconds` 에 걸쳐 진행한다.
2. 만료 → 3·2·1 → **Alive·Soul 전원** 보스룸 이동 → 등장 연출 → 전투.
   **완전 사망자만 제자리**. Soul 이 보스룸 도착 후 부활하면 그대로 전투에 합류한다.
3. 제한시간 안에 스스로 진입 → **도착 확정 시점**에 게이지가 0 으로 비고 멈춘다. 만료가 안 뜬다.
4. 패드 카운트다운 중에 만료가 와도 **중복 발동이 없고, 제한시간도 사라지지 않는다**(T7·T13).
   🔴 구체적으로: **강제 경고 중 패드 진입 → 이탈** 해도 강제 이동이 그대로 진행된다.
   **만료 직전 패드 진입 → 만료 → 패드 이탈** 해도 강제 경고로 이어받는다.
4b. **전원 Soul 상태로 만료** → 목숨 1개씩 소모해 전원 부활 → 이동 → 보스 등장 → 전투(T14·T15).
4c. **호스트가 첫 ACK 를 보내도 `CompleteArrival` 이 조기 발화하지 않는다** — 3인 세션에서
   `eligibleCount` 가 **3** 으로 찍히고 "이미 진행 중이라 무시" 경고가 **안 뜬다**(T16).
5. 호스트·클라 게이지가 **같은 값**을 가리킨다 (MPPM 2인).
6. `InvertFill` 을 켜면 줄어드는 방향으로 바뀐다 — 코드 수정 없이.
7. 컴파일 에러 0 — `observedAssemblyCount` 확인(0 이면 아무것도 검증하지 않은 것 · 교훈 #106).

## 9b. 구현 중 실제로 터진 것 (2026-09-20)

**타이머가 아예 시작되지 않았다.** `HandleMapGenerated` 의 첫 줄이

```cs
if (NetworkManager == null || !NetworkManager.IsServer) return;
```

였는데, `NetworkManager` 는 `NetworkBehaviour` 의 프로퍼티라 **스폰 전에는 null** 이다.
씬 NetworkObject 의 스폰 순서가 보장되지 않으므로(리스크 4) 생성 이벤트가 먼저 오는 경우가 실제로
발생했고, 그때 **보류 플래그조차 세우지 못하고 리턴**해서 게이지가 영원히 빈 상태로 남았다.

- 수정: `NetworkManager.Singleton` 으로 판정하고, 생성 시각을 `Time.realtimeSinceStartup` 으로
  보관했다가 스폰 후 **경과분을 빼서** 적용한다(T1 준수).
- 🔴 **Codex 1차 교차검증이 이 함정을 정확히 지적했었다**("초기화 전 `IsServer` 가드로 이벤트를
  버리는 구현"). 계획에는 "보류했다가 적용" 만 적고 **가드 자체가 null 을 만나는 경우**를 안 적어서,
  구현할 때 절반만 막혔다. → **지적을 계획에 옮길 때는 "무엇을 하라" 뿐 아니라 "무엇 때문에 실패하는가"
  까지 옮겨야 한다.**

## 9. 검증 계획

- `LimitSeconds = 20` 으로 낮춰 만료 경로를 빠르게 돌린다(5분 기다리지 않는다).
- **F5 워프**(`DevBossEntranceWarp`)로 "제때 진입" 경로를 검증 — 3번.
- 만료 직전 F5 로 패드 진입 → 4번(가드) 검증.
- MPPM 2인으로 5번. **2인 중 한 명을 죽여 Soul 로 만든 뒤** 만료시켜 T14 를 검증하고,
  **패드 진입 경로로도** 한 번 더 — 이쪽은 강제 부활이 없으므로 **리스크 1(Motor 되돌림)의 실측 지점**이다.
  Soul 이 보스룸에 도착해 **그 자리에 머무는지** 를 눈으로 확인한다.
- 🔴 **T16 검증은 3인이 필요하다** — 호스트 + 원격 2. `eligibleCount` 와 경고 로그로 판정한다.
- 기존 로그 확인: 과거 3인 세션 기록에 **"이미 진행 중이라 무시"** 경고가 있었는지 찾아본다
  (있었다면 T16 이 그때도 터져 있었다는 증거다).
- 🔴 Play 모드 중이면 **먼저 정지**(CLAUDE.md §6). 프리팹 저작(T-S3)은 Play 중에 돌리지 않는다.
