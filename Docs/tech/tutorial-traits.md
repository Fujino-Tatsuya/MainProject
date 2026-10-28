# 튜토리얼 특성 선택 (할 일 15)

> **🟦 할 일 125 (1/5, 2026-10-28) — 아래 본문보다 우선.** 모든 `TutorialTrait…` 타입이 **`Trait…`** 로 바뀌었다(.meta GUID 그대로).
> 코드 `Assets/1.Scripts/Traits/` · 데이터 `Assets/9.ScriptableObject/Traits/`(`TraitPool.asset`·`TraitTexts.asset`, 옛 placeholder 6개는 `Traits/Legacy/` — 풀에서 뺐다) · 프리팹 `2.Prefabs/Managers/TraitDirector.prefab`·`2.Prefabs/UI/Tutorial/TraitUI.prefab`.
> - **영구 ID = 명세 문자열 ID**(`PAL_1_Q_DAMAGE` 등). 네트워크 행·재합류 보관의 특성 키 = ID 의 FNV-1a 32 해시(`TraitIdHash.Of`) — 풀 배열 인덱스는 더 이상 식별에 쓰지 않는다.
> - **후보 = 캐릭터·차수별 고정 3개**(`TraitCatalog` 명세표 순서, 차수 = 획득 수 + 1). 랜덤 없음, 자동 선택·제시 없는 획득 = 첫 번째. 캐릭터 판별 = `TutorialLocalPlayerInfo.ResolveCharacter`(오브젝트 없으면 선택 저장소 → 로스터). 정의가 등록되지 않은 후보는 빠지고, 0장이면 기존 Empty 흐름.
> - 풀 검증(`TraitCatalogIndex.Errors`) — 빈 ID·해시 0·중복·해시 충돌·명세표에 없는 ID 는 오류 로그 + 제시 안 함.
> - **목록 버전**: 서버가 풀 순서 ID 해시 목록의 버전을 `NetworkVariable` 로 복제 → 클라가 자기 값과 다르면 오류 로그(접속은 끊지 않는다).
> - 36개 정의 에셋은 캐릭터 레인(126~128) 몫. 그 전까지 풀이 비어 복도 특성 라운드는 0장으로 지나간다.
> - 테스트: `TraitCatalogTests`·`TraitFixedSelectionTests`(맵씬 EditMode 메뉴에 포함). 옛 무작위 모드(`TraitSelectionModel(int poolSize, …)`)는 기존 테스트용으로 남아 있다.

> **🟪 할 일 107 (2026-10-27)** — 특성 모듈(`Assets/1.Scripts/Tutorial/Traits`)은 이름 그대로. 맵씬에서 라운드를 여는 복도 흐름만 `TutorialCorridorFlow` → **`MapSceneTraitCorridorFlow`** 로 이름이 바뀌었다(동작 그대로). 디렉터는 `MapSceneDirector`.

> 튜토리얼 복도에서 구역을 돌파할 때마다 특성 1장을 고른다. 특성은 **이번 작전 동안만** 유지된다.
> 코드 폴더: `Assets/1.Scripts/Tutorial/Traits/` · 데이터: `Assets/9.ScriptableObject/Tutorial/Traits/` · UI 프리팹: `Assets/2.Prefabs/UI/Tutorial/TutorialTraitUI.prefab`.
> 씬 배치: S8 에서 `4.MapScene` 루트에 `TutorialTraitDirector`·`TutorialTraitUI` 를 놓았다. 복도 트리거는 `MapSceneTraitCorridorFlow`([tutorial-corridors-zone2.md](tutorial-corridors-zone2.md)). 디렉터를 씬에 두지 않으면 아무 일도 일어나지 않는다.

## 1. 구조

```
TutorialTraitPool (SO) ── TutorialTraitDefinition (SO) × N        문구: TutorialTraitTextTable (SO) ← 폴백 TutorialTraitTexts(상수)
        │                                                                        │
        ▼                                                                        ▼
TutorialTraitDirector (씬 상주 NetworkBehaviour, 서버 권한) ──NetworkList──▶ TutorialTraitSelectionView (로컬 UI)
   ├ 규칙: TutorialTraitSelectionModel (순수)                              ├ 판정: TutorialTraitWindowModel (순수)
   ├ 효과: 수치 = PlayerTraitModifiers(획득 행 읽음, player-stats.md §4) / 이벤트 = Player.ServerAttackLanded 구독   └ 카드·아이콘: TutorialTraitCardView · TutorialTraitIconView
   └ 무적: PlayerInvulnerability (InvulnerabilityCause.TraitSelection)
```

| 조각 | 역할 |
|---|---|
| `TutorialTraitDefinition` | 특성 1장 — `id`·`displayName`·`description`·`icon`·`effectKind`·`value`·`triggerAttackType`·`cooldownSlot`. 배율은 1 이상만(1 미만이면 1로 올림) |
| `TutorialTraitPool` | 후보 배열 + `offerCount`(제시 장 수, 기본 3). **배열 인덱스 = 런타임 특성 id** — 세션 도중 순서를 바꾸지 않는다 |
| `TutorialTraitSelectionModel` | Unity 비의존 규칙. 후보 뽑기(획득 특성 제외·남은 만큼만)·선택 검증·자동 획득 대상·라운드 완료·이탈자 제외·선택 창 무적 계획(`PlanSelectionShields`) |
| `TutorialTraitWindowModel` | Unity 비의존 창 판정. 입력은 **내** 제시 번호(`OfferSeq` — §9). 제시 있음 → 열기, 제시 사라짐·내 제시 번호 바뀜 → 닫기, 한 번 닫힌 제시 재표시 금지, 획득 수 증가 → 토스트(창을 못 본 채 자동 획득돼도 — S12) |
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
| `TutorialTraitJoinResult AddParticipantServer(ulong clientId)` | (S12) 지금 라운드에 참가자 1명 추가 — 그 사람에게만 제시, 기존 제시·선택 보존. `Offered`·`Empty`(0장)·`AlreadyInRound`(무시)·`NoRound`. 새 미선택자가 생기면 `ServerRoundCompleted` 가 같은 라운드에 다시 날 수 있다 |
| `void StartRoundWithoutOfferServer(TutorialTraitRoundDisplay display)` | (할 일 34 구간 스킵) 아무에게도 제시하지 않고 새 라운드만 연다. 참가자는 아래 `GrantParticipantServer` 로 |
| `TutorialTraitJoinResult GrantParticipantServer(ulong clientId)` | (할 일 34 구간 스킵) 지금 라운드에 **제시 없이** 참가 + 바로 무작위 1장 획득 — 제시 행·제시 번호(OfferSeq)가 안 생겨 **클라 창이 열리지 않는다**. 효과·`ServerTraitChosen`(Auto)·라운드 완료는 자동 획득과 같다. `Granted`·`Empty`(0장)·`AlreadyInRound`(무시)·`NoRound` |
| `bool IsInRoundServer(ulong clientId)` | (S12) 지금 라운드 참가자인가(제시 중·고름·0장) |
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

### 호출 순서 (한 라운드)

1. 서버: `StartOfferRoundServer(display)` → 모델이 후보를 뽑고 행 복제 → 미선택자에게 무적 토큰.
2. 클라: `ClientStateChanged` → 창이 열림 → 카드 클릭 → `RequestChoose(i)`.
3. 서버: RPC 검증 → 효과 적용 → 행 재작성(제시 행 삭제·획득 행 추가) → 토큰 해제 → `ServerTraitChosen`.
4. 전원 완료 시 `ServerRoundCompleted(round)`. 교착이면 서버가 `AutoChooseAllPendingServer()` 로 마무리(3과 같은 경로, `Auto = true`).

## 3. 배치 순서 (1·2 는 S8 에서 완료 — 실제 배선은 [tutorial-corridors-zone2.md](tutorial-corridors-zone2.md))

1. ✅ 씬에 빈 오브젝트 + `NetworkObject` + `TutorialTraitDirector` — `pool` 에 `TutorialTraitPool.asset` 연결(`4.MapScene` 루트, hash 3847683974).
2. ✅ `TutorialTraitUI.prefab` 인스턴스 **1개**. 씬에 `EventSystem` 이 있어야 카드 클릭이 된다(부트스트랩 `PersistentEventSystem`).
3. 복도 트리거(서버)가 **라운드당 1번만** `StartOfferRoundServer` 를 부른다(S12: 첫 도착자만 지정, 이후 도착자는 `AddParticipantServer`):
   - 복도1 = `TutorialTraitRoundDisplay.Intro`, 복도4 = `TutorialTraitRoundDisplay.LastNotice`, 복도2·3 = `None`.
4. 출구 게이트(G2·G4·G7·G10)는 **특성 디렉터가 아니라 튜토리얼 디렉터 모델**이 연다 — 복도 흐름이 `ServerTraitChosen`(수동·자동)마다
   `ReportCheckServer(TraitCorridorN)` 을 보내고, 모델이 '전원 체크 / 첫 완료자 뒤 60초'로 연다. 0장 받은 사람(`Empty`)·특성 디렉터 없음은 도착 순간 바로 체크한다.
   `ServerRoundCompleted` 는 게이트에 쓰지 않는다(완전 사망자가 라운드 완료를 막아도 게이트는 열리게).
5. 교착 방지 60초(출구가 어떤 이유로든 열림)·타이머 만료·보스방 도착 때 미도착자를 `AddParticipantServer` 로 넣고 `AutoChooseAllPendingServer()` —
   🔴 **반드시 강제 이동·연출 잠금(`PlayerEncounterLock.IsCinematicLocked`) 전에** 호출한다(만료는 `Expired` 로 바뀐 프레임에 처리 — 강제 이동은 3초 경고 뒤라 그보다 먼저).
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
- **효과(할 일 125 3/5)**: 옛 효과 enum 은 폐지. 특성 정의 = 효과 클래스(`TraitEffect`) 목록(`[SerializeReference]`, 인스펙터 '효과 추가') + 수치 — §4.1.
  소비처가 없는 스탯은 넣지 않는다(§6).

### 4.1 효과 클래스(할 일 125 3/5)

코드: `Assets/1.Scripts/Traits/Effects/` · 서버 허브 `Player/Stats/PlayerTraitEffects.cs` · 테스트 `TraitEffectTests`(맵씬 EditMode 메뉴).

| # | 클래스 | 형 | 스킬(캐릭터 레인)이 쓰는 곳 |
|---|---|---|---|
| ① | `TraitDamageEffect` | 수치 | 이미 연결 — Dref(`Player.ResolveDamage` 키) |
| ② | `TraitCooldownEffect` | 수치 | 이미 연결 — `GetEffectiveCooldownTime` |
| ③ | `TraitCooldownRefundEffect` | 사건 | 사건 발행만 필요(아래) |
| ④ | `TraitHealEffect` | 수치+등록 | 팔라딘 패시브 회복이 `PlayerTraitEffects.HealServer` 로 연결됨(배율·초과 전달·Healed 사건) |
| ⑤ | `TraitShieldEffect` | 사건 | 특성 채널 `ShieldType.Trait`(출처 = 특성 해시, 큰 값 유지) |
| ⑥ | `TraitKnockbackEffect` | 등록 | 적중 직전 `PlayerTraitEffects.ModifyOutgoingAttackServer(key, castId, target, dir, ref info)` |
| ⑦ | `TraitAreaEffect` | 수치 | `PlayerTraitModifiers.AreaMultiplier(key, axis)` |
| ⑧ | `TraitDerivedAttackEffect` | 등록 | `PlayerTraitEffects.GetDerivedAttacks(key, list)` → `DamageFrom(Dref)`·`Counter`, 판정 `TraitHitFlags.Derived` |
| ⑨ | `TraitNextAttackBuffEffect` | 사건 | 첫 원래 피해 판정에서 `PlayerTraitEffects.ConsumeNextAttackMultiplierServer(key, castId)` |
| ⑩ | `TraitSkillValueEffect` | 수치 | `PlayerTraitModifiers.ResolveSkillValue(이름, 데이터 값)` |
| — | `TraitStatEffect` | 수치 | PlayerStats 기본%·전체%·가하는 피해% |

- **모드 변경 7개**는 효과 클래스가 아니다 — 스킬이 `PlayerTraitModifiers.HasTrait(TraitModeIds.…)` 로 묻고 자기 코드 안에서 분기한다.
- **사건**: `AttackLanded` 는 `Player.ServerAttackLanded` 에서 자동(시전 ID·형태 포함). `GuardSucceeded`·`PassiveProc`·`SkillCompleted`·`BasicAttackFired` 는
  스킬이 `PlayerTraitEffects.RaiseServer(trigger, skill)` 로 알린다(지금은 아무도 안 낸다 — 캐릭터 레인 몫). 거름 = `TraitTriggerFilter`(시전당 1회·간격).
- **시전 ID**: `PlayerAttackLanded.CastId` — 스킬은 서버 승인 때 `PlayerSkillBase.ServerCastId` 를 받고, 그 스킬을 출처로 낸 적중은 자동으로 묶인다.
  평타 스윙·투사체 1발은 발행마다 새 값(= 동작 1회). 여러 판정을 한 동작으로 묶어야 하면 `RaiseServerAttackLanded(..., castId)` 로 직접 준다.
- 🔴 **밀치기**는 지금 `AttackInfo` 넉백+경직 필드 경로(23호에서 명세 §4 와 맞는 유일한 경로). 경석의 보스 쪽 수정(23호 `OnKnockback`·상태별 슈퍼아머) 뒤
  `TraitKnockbackEffect.ApplyCrowdControl` 한 곳만 `Unit.Knockback`·상태이상 직접 걸기로 바꾼다. 경직은 0 보다 커야 23호 취약 밀림이 걸린다.
- **되돌리기 장부**(`TraitEffectLedger`): 이벤트형 효과의 핸들을 특성 해시 단위로 기록 — 재선택 [예] = 전부 되돌림(구독·등록 해제, 다음 공격 강화 삭제) 뒤 다시 고른 것만 다시 건다.
  '획득 1회' 동작(`HasOnceAction`)은 표시가 남아 재선택·재합류(`TraitRejoinSnapshot.UsedOnce`)에서 다시 하지 않는다. 이미 지급된 보호막은 되돌리지 않고 만료에 맡긴다.
- 사망(살아 있음 → 그 밖)이면 허브가 대기 상태(다음 공격 강화·파생 카운터·대상별 1회 기억)를 지운다. 선택한 특성은 유지.
- `Unit.HealHp` 는 실제 회복량(int)을 돌려준다 — 초과 회복 = 요청 − 반환값.

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
- **끊긴 플레이어의 획득 기록은 모델에서 삭제**된다 — 판 명단 안 사람(데모 흐름)은 §10 재합류로 되돌아온다. 데모 흐름 밖(명단 없음)은 재접속하면 처음부터.
- **추락 피해는 무적을 우회**한다 — 선택 중에도 떨어지면 맞는다.
- **같은 특성의 플레이어 간 중복 허용**. 중복 제외는 개인 기준(내가 얻은 것만 다시 안 나온다).
- **공격속도·방어·최대체력 배율은 소비처가 없어** 효과 종류에 두지 않았다.
- **배율은 1 이상만** — 1 미만은 디버프로 분류돼 보스방 연출(`ClearDebuffsServer`)이 지운다.
- 선택 중 무적은 플레이어 오브젝트 기준이다. 미선택 중 플레이어 오브젝트가 새로 바뀌면(디스폰 후 재스폰) 새 오브젝트엔 토큰이 다시 걸리지 않는다 — 튜토리얼 흐름엔 그런 경우가 없다.

## 7. 테스트

`Tools/Tests/맵씬 EditMode 테스트 실행` 에 등록(I0 에서 플레이어 러너에서 이동): `TutorialTraitSelectionModelTests`(규칙·무적 계획) · `TutorialTraitWindowModelTests`(창 판정·문구 테이블 폴백) ·
`TutorialTraitDeadlineModelTests`(교착 방지 게이지 — 비율·10초 강조·로컬 감소·재동기화). 게이지 표시 조건은 `MapSceneTraitCorridorModelTests`.

## 8. 교착 방지 게이지 (할 일 15 후속)

선택 창 패널 맨 아래(`Window/Panel/Deadline`) — 가로 게이지 + `n초 뒤 자동 선택` + `선택 완료 n/전체`. 기본 꺼짐이라 안 보일 땐 창 레이아웃이 전과 같다.

- **표시 조건**: 기다리는 개인 게이트가 진행 중 복도의 출구(G2/G4/G7/G10)이고 첫 완료자 뒤(자동 진행 시각이 정해짐)일 때만 —
  `MapSceneTraitCorridorRules.ShouldShowTraitDeadline`. 첫 완료자 전·솔로 즉시 완료엔 숨김. 게이지는 창 안이라 창이 닫힌(이미 고른) 사람에겐 안 보인다.
- **값**: `MapSceneDirector.TryGetWait`(복제 대기 상태 — done/total·자동 진행까지 남은 초) · 분모 = `MapSceneDirector.DeadlockSeconds`(인스펙터 `deadlockSeconds`).
- **의존 방향**: 특성 모듈(`Assets/1.Scripts/Tutorial/Traits/**`)은 튜토리얼 디렉터를 모른다. 복도 흐름(`MapSceneTraitCorridorFlow`, **모든 피어**에 붙음)이
  `Render`(디렉터 `StateChanged`·특성 `ClientStateChanged`) 때마다 `TutorialTraitSelectionView.Instance.SetDeadline(visible, remaining, total, done, count)` 로 넣는다.
  창이 늦게 깨어나면 흐름 `Update` 가 다시 넣는다.
- **시간**: 창이 `Time.unscaledDeltaTime` 으로 로컬 감소(0 에서 멈춤 — 만료 처리는 서버), 다음 `SetDeadline` 이 서버 시각 기준 값으로 다시 맞춘다.
  남은 `TutorialTraitDeadlineModel.UrgentSeconds`(10초) 이하면 게이지·초 텍스트가 `deadlineUrgentColor`.
- 순수 계산 = `TutorialTraitDeadlineModel`(Unity 비의존). 문구는 아직 문구 테이블(`TutorialTraitTexts.AllIds`)에 넣지 않고 모델 상수(`SecondsFormat`·`DoneFormat`) — 테이블 행 추가는 데이터 테이블 이관(§5) 때 함께.
- 프리팹은 `Tools/Tutorial/특성 선택 UI 프리팹 생성` 으로 재생성(루트 GameObject·RectTransform fileID 보존 → `4.MapScene` 인스턴스 오버라이드 유지).

## 9. 전멸 리스폰 뒤 특성 초기화 · 재선택 (할 일 30 3단계)

> 사양·흐름 원본: [party-lives-respawn.md §4](party-lives-respawn.md#4-특성-초기화--재선택-3단계--구현됨). 여기는 특성 모듈 쪽 API·구조만.

| 조각 | 역할 |
|---|---|
| `TutorialTraitResetModel`(순수) | 플레이어별 없음 → 팝업 대기(세대) → (예) 재선택 중(남은 라운드, `ReselectRounds` = 4) → 없음. 응답 검증 `Answer(clientId, generation, reset)` = `Kept`·`Reset`·`NotPrompted`(대상 아님·중복·재선택 중)·`StaleGeneration`(다시 전멸 전 팝업). `IsBusy` = 패드 잠금 |
| `TutorialTraitEffectLedger<T>`(순수) | (플레이어, 특성 인덱스) → 원복 핸들 장부. `TakeAll(clientId)` 가 그 플레이어 몫만 꺼낸다 — 다른 플레이어 것은 남는다 |
| `TutorialTraitSelectionModel` 추가 | `StartPersonalRound(clientId, offerCount, next)`(그 사람만 — 전역 라운드 번호는 그대로, 그 사람 `OfferSeq` 만 올라감, 남의 제시·선택은 그대로) · `GetOfferSeq` · `IsPersonalRound` · `ResetPlayer`(획득·제시 비움, 등록 유지) · `CancelOffer`(열린 제시 거둠) |
| `TutorialTraitDirector` 추가 | `PartyWipeRespawner.PartyWipeRespawned` 구독(서버, 튜토리얼일 때만) · 복제 행 `EntryKind.ResetPrompt`(`TraitIndex` = 세대) · `NetworkVariable<bool> traitResetBusy` |
| `TutorialTraitSelectionView` 추가 | 팝업을 창과 같은 캔버스에 **런타임 생성**(프리팹·저작 메뉴 무수정) — 루트 `UiModalBlocker` + 딤 + 질문 + [예]/[아니오] |

API:

| 시그니처 | 의미 |
|---|---|
| `bool TryGetResetPrompt(ulong clientId, out int generation)` | [전 피어] 그 플레이어가 팝업에 답해야 하나 + 세대 |
| `void RequestResetAnswer(int generation, bool reset)` | [로컬 플레이어] 응답 → `AnswerResetPromptServerRpc`(`SendTo.Server`, 보낸 사람 = `SenderClientId`) |
| `bool IsTraitResetInProgress` | [전 피어] 누군가 팝업 대기·재선택 중 — `MapSceneBossPath.IsBossPadEnabled` 가 패드를 잠근다 |

- **효과 원복**: 쿨감 특성 = 걸 때 장부에 구독한 핸들러를 남기고 원복 때 `Player.ServerAttackLanded -=`. 기존 `cooldownHooks` 목록을 이 장부로 바꿨다.
  배율 특성은 장부에 없다 — 획득 행이 비면 `PlayerTraitModifiers` 가 스스로 1 로 돌아간다(할 일 125 2/5, 상태이상 출처 `SourceIdOf` 폐지).
- **재선택 라운드**: `ContinueReselect` — 남은 라운드를 하나 빼고 `StartPersonalRound`(0장이면 다음 라운드로 넘어감). 고를 때(`CommitChoice`) 재선택 중이면
  행·무적 동기화 **전에** 다음 라운드를 열어 무적 토큰을 풀었다 다시 걸지 않는다. 표시 옵션은 `None`(소개·마지막 문구 없음).
  재선택은 전역 라운드 번호(`Round`)를 올리지 않고, 그 획득 통지(`TutorialTraitChosen.Round`)는 **0** 이라 복도 흐름의 체크 보고·자동 획득이 반응하지 않는다(`IsPersonalRound`). `ServerRoundCompleted` 도 다시 나지 않는다(완료 플래그를 건드리지 않음).
- **창 열림 = 내 제시 번호(OfferSeq)**: 플레이어별 `OfferSeq` 는 그 플레이어에게 새 제시가 들어갈 때만(복도 라운드·늦은 참가·재선택) 모델 전체 단조 증가 값으로 바뀌고,
  제시·획득 행(`Entry.OfferSeq`)에 실려 복제된다(`GetOfferSeq(clientId)`). 창 판정은 전역 라운드가 아니라 이 번호로 열고 닫으므로 다른 사람의 재선택이 내 창을 닫지 않는다.
- **문구**: `TUT_TRAIT_RESET_PROMPT`("특성을 초기화하고 다시 선택할까요?") · `TUT_TRAIT_RESET_YES`("예") · `TUT_TRAIT_RESET_NO`("아니오") — `TutorialTraitTexts` 상수 + `AllIds`.
  `TutorialTraitTexts.asset` 에는 행을 넣지 않았다(행 없음 = 상수 폴백, 로컬라이징 브랜치와 에셋 충돌 회피) — 기획이 문구를 바꿀 때 행을 추가한다.
- 테스트: `TutorialTraitResetModelTests`(튜토리얼 러너).

## 10. 판 도중 재합류 (할 일 48 C14 R4c)

- **퇴장**: `HandleClientDisconnected` 가 모델에서 지우기 직전 `TutorialTraitRejoin.Capture` → `RejoinMemory.Server.SaveTraits(playerId)`(playerId = `MatchRoster.TryGetPlayerIdByLastClient`).
  담는 것 = 획득 목록(순서) · 지금 복도 라운드에서 이미 골랐나 · 초기화 팝업 세대 / 재선택 남은 라운드·열린 후보.
- **재스폰**: `MatchRoster.PlayerRejoined` → `TutorialTraitRejoin.Restore`(새 clientId) → 장부에 없는 획득 특성만 `ApplyEffect`(멱등) → `RebuildEntries`.
  팝업 대기였으면 같은 세대 팝업, 재선택 중이었으면 같은 후보 창(퇴장 중 다시 전멸했으면 지금 세대 팝업). 고르던 복도 제시는 되살리지 않는다(문 열림 자동 획득).
- `MapSceneDirector` 는 같은 이벤트로 `MapSceneFlowModel.RebindPlayer`(체크·완전 사망 이어 받기, 뒤 배리어 0). 테스트: `TutorialTraitRejoinTests`.
