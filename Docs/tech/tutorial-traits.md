# 튜토리얼 특성 선택 (할 일 15)

> 튜토리얼 복도에서 구역을 돌파할 때마다 특성 1장을 고른다. 특성은 **이번 작전 동안만** 유지된다.
> 코드 폴더: `Assets/1.Scripts/Tutorial/Traits/` · 데이터: `Assets/9.ScriptableObject/Tutorial/Traits/` · UI 프리팹: `Assets/2.Prefabs/UI/Tutorial/TutorialTraitUI.prefab`.
> 씬 배치: S8 에서 `4.MapScene` 루트에 `TutorialTraitDirector`·`TutorialTraitUI` 를 놓았다. 복도 트리거는 `TutorialCorridorFlow`([tutorial-corridors-zone2.md](tutorial-corridors-zone2.md)). 디렉터를 씬에 두지 않으면 아무 일도 일어나지 않는다.

## 1. 구조

```
TutorialTraitPool (SO) ── TutorialTraitDefinition (SO) × N        문구: TutorialTraitTextTable (SO) ← 폴백 TutorialTraitTexts(상수)
        │                                                                        │
        ▼                                                                        ▼
TutorialTraitDirector (씬 상주 NetworkBehaviour, 서버 권한) ──NetworkList──▶ TutorialTraitSelectionView (로컬 UI)
   ├ 규칙: TutorialTraitSelectionModel (순수)                              ├ 판정: TutorialTraitWindowModel (순수)
   ├ 효과: 영구 상태이상 / Player.ServerAttackLanded 구독                   └ 카드·아이콘: TutorialTraitCardView · TutorialTraitIconView
   └ 무적: PlayerInvulnerability (InvulnerabilityCause.TraitSelection)
```

| 조각 | 역할 |
|---|---|
| `TutorialTraitDefinition` | 특성 1장 — `id`·`displayName`·`description`·`icon`·`effectKind`·`value`·`triggerAttackType`·`cooldownSlot`. 배율은 1 이상만(1 미만이면 1로 올림) |
| `TutorialTraitPool` | 후보 배열 + `offerCount`(제시 장 수, 기본 3). **배열 인덱스 = 런타임 특성 id** — 세션 도중 순서를 바꾸지 않는다 |
| `TutorialTraitSelectionModel` | Unity 비의존 규칙. 후보 뽑기(획득 특성 제외·남은 만큼만)·선택 검증·자동 획득 대상·라운드 완료·이탈자 제외·선택 창 무적 계획(`PlanSelectionShields`) |
| `TutorialTraitWindowModel` | Unity 비의존 창 판정. 제시 있음 → 열기, 제시 사라짐·라운드 바뀜 → 닫기, 한 번 닫힌 라운드 재표시 금지, 획득 수 증가 → 토스트 |
| `TutorialTraitDirector` | 상태 소유자. 라운드 제시·선택 RPC 검증·효과 적용·무적 토큰. 플레이어별 제시/획득 행을 `NetworkList<Entry>` 로, 라운드 번호·표시 옵션을 `NetworkVariable` 로 복제 |
| `TutorialTraitSelectionView` | 로컬 플레이어 UI. 중앙 모달(창 루트 `UiModalBlocker` → 로컬 이동·대시·공격 차단, 일시정지 없음) · 카드 클릭 → `RequestChoose` · 획득 토스트 · 좌상단 획득 아이콘 줄 + 호버 패널 |
| `TutorialTraitUI.prefab` | 별도 Screen Space Overlay 캔버스(sort 60, CombatHUD 와 무관). 저작 메뉴 `Tools/Tutorial/특성 선택 UI 프리팹 생성` — 다시 돌리면 덮어쓴다 |
| 무적 토큰 | 제시받고 아직 안 고른 플레이어에게 `InvulnerabilityCause.TraitSelection` 무기한 토큰. 원인별 토큰이라 대시·연출 무적과 서로 해제하지 않는다 |
| `TutorialTraitTextTable` | 문구 ID → 문자열 에셋(`TutorialTraitTexts.asset`). 에셋이 없거나 그 ID 행이 없으면 `TutorialTraitTexts` 상수 |

### 무적 토큰 동기화

- 제시·선택·자동 획득·이탈 때마다 `SyncSelectionShields` 가 모델의 "미선택" 집합과 토큰을 맞춘다(계획은 `TutorialTraitSelectionModel.PlanSelectionShields`).
- **제시 순간 플레이어 오브젝트가 없던 미선택자**: 그 플레이어가 스폰되면 `PlayerLifeCycleController.ServerLifeStateChanged`(스폰 때 (s,s) 초기 통지)를 받아
  다음 `Update` 에서 다시 건다 — 스폰 통지 시점엔 NGO 가 아직 `NetworkClient.PlayerObject` 를 채우지 않아서 한 프레임 미룬다. 이미 고른 뒤 스폰되면 걸지 않는다.
- 디렉터 디스폰 시 전부 해제.

## 2. 외부 API

### 서버 (`IsServer`, 디렉터 스폰 뒤)

| 시그니처 | 의미 |
|---|---|
| `bool StartOfferRoundServer()` | 접속 중인 전원에게 새 라운드 제시, 표시 옵션 None |
| `bool StartOfferRoundServer(TutorialTraitRoundDisplay display)` | 위 + 창 표시 옵션(`Intro` = 상단 설명, `LastNotice` = 아래 한 줄) |
| `bool StartOfferRoundServer(IEnumerable<ulong> clientIds)` | 지정한 플레이어에게만 제시, None |
| `bool StartOfferRoundServer(IEnumerable<ulong> clientIds, TutorialTraitRoundDisplay display)` | 지정 + 표시 옵션 |
| `bool AutoChooseServer(ulong clientId)` | 그 플레이어의 제시 후보 중 무작위 1장 자동 획득. 대상 아님(이미 고름·제시 없음) = false |
| `int AutoChooseAllPendingServer()` | 이번 라운드 미선택 전원 자동 획득. 처리 인원 수 반환 |
| `bool IsRoundCompleteServer` | 이번 라운드 참가자 전원이 골랐나(라운드 없음 = true) |
| `bool HasPendingChoiceServer(ulong clientId)` | 그 플레이어가 아직 안 골랐나 |
| `event Action<TutorialTraitChosen> ServerTraitChosen` | 1장 획득(수동·자동). `ClientId`·`TraitIndex`·`Trait`·`Auto`·`Round` |
| `event Action<int> ServerRoundCompleted` | 라운드 참가자 전원 완료(0장 제시·이탈 포함). 인자 = 라운드 번호. 라운드당 1번 |

`StartOfferRoundServer` 반환값: **false = 아무에게도 1장도 제시하지 못했다**(풀 소진·미스폰·참가자 없음) → 호출자는 선택 단계를 건너뛴다.
false 일 때 `ServerRoundCompleted` 는 같은 호출 안에서 이미 불렸을 수 있으므로 기다리지 말 것.

### 클라·전 피어

| 시그니처 | 의미 |
|---|---|
| `static TutorialTraitDirector Instance` | 씬의 디렉터(없으면 null) |
| `TutorialTraitPool Pool` | 특성 인덱스 → 정의 조회용 |
| `int Round` | 현재 라운드 번호(0 = 없음) |
| `TutorialTraitRoundDisplay RoundDisplay` | 현재 라운드 표시 옵션 |
| `void RequestChoose(int traitIndex)` | [로컬 플레이어] 제시 후보 중 하나 선택 요청. 판정은 서버(위반 = 무시 + 로그) |
| `void GetOffered(ulong clientId, List<int> result)` | 그 플레이어의 현재 제시 후보(안 골랐을 때만) |
| `void GetAcquired(ulong clientId, List<int> result)` | 그 플레이어의 획득 특성(획득 순서) |
| `bool HasPendingChoice(ulong clientId)` | 제시 행이 있나(지금 골라야 하나) |
| `event Action ClientStateChanged` | 복제 상태(제시·획득·라운드·표시 옵션) 변경 — UI 는 프레임당 한 번 모아 평가 |
| `static ulong SourceIdOf(int traitIndex)` | 특성 상태이상의 출처 id(디버그·조회) |

### 호출 순서 (한 라운드)

1. 서버: `StartOfferRoundServer(display)` → 모델이 후보를 뽑고 행 복제 → 미선택자에게 무적 토큰.
2. 클라: `ClientStateChanged` → 창이 열림 → 카드 클릭 → `RequestChoose(i)`.
3. 서버: RPC 검증 → 효과 적용 → 행 재작성(제시 행 삭제·획득 행 추가) → 토큰 해제 → `ServerTraitChosen`.
4. 전원 완료 시 `ServerRoundCompleted(round)`. 교착이면 서버가 `AutoChooseAllPendingServer()` 로 마무리(3과 같은 경로, `Auto = true`).

## 3. S12 배치 순서

1. 복도 씬에 빈 오브젝트 + `NetworkObject` + `TutorialTraitDirector` — `pool` 에 `TutorialTraitPool.asset` 연결.
2. `TutorialTraitUI.prefab` 인스턴스 **1개**. 씬에 `EventSystem` 이 있어야 카드 클릭이 된다.
3. 복도 트리거(서버)가 **라운드당 1번만** `StartOfferRoundServer` 를 부른다:
   - 복도1 = `TutorialTraitRoundDisplay.Intro`, 복도4 = `TutorialTraitRoundDisplay.LastNotice`, 복도2·3 = `None`.
4. 반환 **false** → 게이트 즉시 개방. **true** → `ServerRoundCompleted` 에서 개방.
5. 교착 방지 60초·타이머 만료 시 `AutoChooseAllPendingServer()` — 🔴 **반드시 강제 이동·연출 잠금(`PlayerEncounterLock.IsCinematicLocked`) 전에** 호출한다.
   잠금 중엔 상태이상 `Apply` 가 거부돼 배율 특성이 획득만 되고 효과가 빠진다(경고 로그).

## 4. placeholder 특성 6종

기획서(D8)가 오기 전 임시 값. 이름 앞 `(임시)`.

| 에셋 | id | 이름 | 효과 | 값 |
|---|---|---|---|---|
| `TutorialTrait_AttackUp10` | `trait_attack_10` | (임시) 날카로운 감각 | 공격력 배율 | 1.1 |
| `TutorialTrait_AttackUp20` | `trait_attack_20` | (임시) 전투 본능 | 공격력 배율 | 1.2 |
| `TutorialTrait_MoveUp10` | `trait_move_10` | (임시) 가벼운 발걸음 | 이동속도 배율 | 1.1 |
| `TutorialTrait_MoveUp15` | `trait_move_15` | (임시) 질주 | 이동속도 배율 | 1.15 |
| `TutorialTrait_HitCdrQ` | `trait_hit_cdr_q` | (임시) 연계 숙련 | 평타(`Default`) 적중 시 Q(`Main`) 쿨 감소 | 0.5초 |
| `TutorialTrait_HitCdrE` | `trait_hit_cdr_e` | (임시) 호흡 조절 | 평타(`Default`) 적중 시 E(`Sub`) 쿨 감소 | 0.5초 |

### 기획서가 오면 고칠 곳

- **값·이름·설명·아이콘만** 바뀌면: 정의 에셋(`Assets/9.ScriptableObject/Tutorial/Traits/TutorialTrait_*.asset`)만 고친다.
- **특성 추가·삭제**: 정의 에셋을 만들고 `TutorialTraitPool.asset` 의 `traits` 배열에 넣는다(인덱스 = 런타임 id). 제시 장 수는 `offerCount`.
- **새 효과 종류**: `TutorialTraitEffectKind` 에 값 추가 → `TutorialTraitDirector.ApplyEffect` 의 `switch` 에 적용 코드 추가.
  배율류는 `ApplyStatModifier`(영구 상태이상, 특성별 출처 id)를, 이벤트류는 `HookCooldownReduction` 처럼 서버 이벤트 구독을 따라 한다. 소비처가 없는 스탯은 넣지 않는다(§6).

## 5. 데이터 테이블로 옮길 때

지금은 어트리뷰트를 달지 않았다 — 새 시트라 병합 Export 가 필요하고, Export 는 development 병합 뒤 Main 에서만 한다([data-table.md](data-table.md) §8).

| 대상 | 열(필드) | 비고 |
|---|---|---|
| 특성 `TutorialTraitDefinition` | `id`·`displayName`·`description`(→ `[DataTableText]`) · `value` · `effectKind`·`triggerAttackType`·`cooldownSlot` | enum 은 템플릿 대상 아님 — 기획이 바꾸려면 int 열이나 인스펙터 유지 중 결정 |
| 풀 `TutorialTraitPool` | `offerCount` | 이미 `[Min(1)]` — 테이블도 같은 범위 |
| 문구 `TutorialTraitTextTable` | `entries[i].id`·`entries[i].text`(→ `[DataTableText]`) | 배열 크기(행 수)는 인스펙터. 🔴 테이블 문자열은 `SkillTooltipFormatter` 자리표시자 검증을 거쳐 `{location}` 이 "없는 필드" 오류가 된다 → 옮길 때 `TutorialTraitTexts.LocationToken` 표기를 바꿀 것 |

문구 ID: `TUT_C1_TRAIT_TITLE`(창 제목) · `TUT_C1_TRAIT_INTRO`(복도1 상단 설명) · `TUT_C4_TRAIT_LAST`(복도4 아래 한 줄) ·
`TUT_C1_TRAIT_PICKED`(획득 토스트, `{location}` 포함) · `TUT_C1_TRAIT_PICKED_LOCATION`(다시 보기 위치 = "화면 왼쪽 위").

## 6. 알려진 제약

- **틱 판정 공격**(거너 빔 등)은 틱마다 `ServerAttackLanded` 가 나와 쿨감이 틱마다 반복된다.
- **연출 잠금 중** 상태이상 `Apply` 가 거부된다 → 배율 특성 효과 없음(획득은 기록). 자동 획득은 잠금 전에(§3-5).
- **끊긴 플레이어의 획득 기록은 삭제**된다(재접속하면 처음부터).
- **추락 피해는 무적을 우회**한다 — 선택 중에도 떨어지면 맞는다.
- **같은 특성의 플레이어 간 중복 허용**. 중복 제외는 개인 기준(내가 얻은 것만 다시 안 나온다).
- **공격속도·방어·최대체력 배율은 소비처가 없어** 효과 종류에 두지 않았다.
- **배율은 1 이상만** — 1 미만은 디버프로 분류돼 보스방 연출(`ClearDebuffsServer`)이 지운다.
- 선택 중 무적은 플레이어 오브젝트 기준이다. 미선택 중 플레이어 오브젝트가 새로 바뀌면(디스폰 후 재스폰) 새 오브젝트엔 토큰이 다시 걸리지 않는다 — 튜토리얼 흐름엔 그런 경우가 없다.

## 7. 테스트

`Tools/Tests/튜토리얼 EditMode 테스트 실행` 에 등록(I0 에서 플레이어 러너에서 이동): `TutorialTraitSelectionModelTests`(규칙·무적 계획) · `TutorialTraitWindowModelTests`(창 판정·문구 테이블 폴백).
