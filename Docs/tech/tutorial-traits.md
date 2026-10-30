# 특성 선택 (할 일 15 → 125)

> 맵씬 복도에서 구역을 돌파할 때마다 특성 1장을 고른다. 특성은 **이번 작전 동안만** 유지된다.
> 할 일 125(2026-10-28, 5단계)에서 튜토리얼 placeholder 6개를 **캐릭터별 36개 특성 구조**로 바꿨다 — 명세 `Re_C_특성_개발명세_20261016.md`(이하 '명세'), 계획 `_lane-artifacts/common/125/125-특성-계획.md`.
> 36개 정의·특성별 스킬 분기는 캐릭터 레인(126 팔라딘·127 거너·128 어쌔신) 몫이다 — 붙는 자리·금지 목록은 `_lane-artifacts/common/125/125-캐릭터레인-가이드.md`.

| 무엇 | 어디 |
|---|---|
| 코드 | `Assets/1.Scripts/Traits/` — 공용(`Trait*.cs`·`Effects/`·`Content/`) + 캐릭터 폴더 `Paladin/`·`Gunner/`·`Assassin/`(레인 전용) |
| 데이터 | `Assets/9.ScriptableObject/Traits/` — `TraitPool.asset`(공용 풀) · `TraitTexts.asset`(창 문구) · `<캐릭터>/TraitPool_<캐릭터>.asset`(캐릭터 풀) · 옛 placeholder 6개 `Legacy/`(풀에서 뺐다) |
| 프리팹 | `2.Prefabs/Managers/TraitDirector.prefab` · `2.Prefabs/UI/Tutorial/TraitUI.prefab` |
| 씬 | `4.MapScene` 루트에 디렉터·UI 인스턴스. 라운드를 여는 복도 흐름 = `MapSceneTraitCorridorFlow`([tutorial-corridors-zone2.md](tutorial-corridors-zone2.md)), 디렉터 없으면 아무 일도 없다 |
| 이름 | 할 일 125 1/5 에서 모든 `TutorialTrait…` → **`Trait…`**(.meta GUID·직렬화 필드 유지). 아래 표기는 새 이름이다 |

## 1. 구조

```
TraitPool (SO) ─ traits(공용, 비움) + TraitCharacterPool × 3 (팔라딘·거너·어쌔신 순)
                       └ <캐릭터>TraitDefinition (SO) × 12  ── effects: [SerializeReference] TraitEffect 목록 + 수치
        │                                         문구: Strings 키 Trait.<ID>.* → 정의 인라인 ko / 창 문구 TraitTextTable → TraitTexts 상수
        ▼
TraitDirector (씬 상주 NetworkBehaviour, 서버 권한) ──NetworkList──▶ TraitSelectionView (로컬 UI)
   ├ 규칙: TraitSelectionModel (순수, 고정 후보)                         ├ 창 판정: TraitWindowModel (순수)
   ├ 후보·해시·버전: TraitCatalog / TraitCatalogIndex                     ├ 카드: TraitCardView(이름/분류/설명 — TraitTooltip)
   ├ 수치: PlayerTraitModifiers (획득 ID 목록 → 각 피어 계산)             └ 2차 상충 확인창: TraitPickConfirmModel (순수)
   ├ 사건: PlayerTraitEffects (서버 허브) ← 효과 장부 TraitEffectLedger
   └ 무적: PlayerInvulnerability (InvulnerabilityCause.TraitSelection)
```

| 조각 | 역할 |
|---|---|
| `TraitDefinition` | 특성 1장 — `id`(영구 문자열 ID) · `displayName`·`category`·`description`(인라인 ko 폴백) · `icon` · `effects`(효과 클래스 목록). 새 정의는 캐릭터 하위 타입(`PaladinTraitDefinition` 등 — §11) |
| `TraitPool` / `TraitCharacterPool` | 정의 목록 = 공용 칸 뒤에 캐릭터 풀 3개를 이은 것. **식별은 ID 해시**라 배열 순서는 식별에 쓰지 않는다(순서는 목록 버전에만). `offerCount` 기본 3 |
| `TraitCatalog` | 명세 고정표 — 캐릭터·차수(1~4)별 3개 ID(36칸). `TraitCatalogIndex` = 풀 ID 목록 → 해시↔칸·고정 후보·목록 버전·검증 |
| `TraitIdHash` | ID → FNV-1a 32 해시(0 = 없음 예약). 네트워크 행·재합류 보관 키 |
| `TraitSelectionModel` | Unity 비의존 규칙 — 고정 후보 제시·선택 검증·자동 획득·라운드 완료·이탈자 제외·선택 창 무적 계획 |
| `TraitWindowModel` | Unity 비의존 창 판정. 입력은 **내** 제시 번호(`OfferSeq` — §9) |
| `TraitDirector` | 상태 소유자. 라운드 제시·선택 RPC 검증·효과 적용(장부)·무적 토큰·목록 버전 복제 |
| `TraitSelectionView` | 로컬 UI — 중앙 모달(창 루트 `UiModalBlocker`) · 카드 · 도움말 줄 · 상충 확인창 · 획득 토스트 · 좌상단 획득 아이콘 + 호버 툴팁 · 초기화 팝업(§9) |
| `TraitTextTable` | 창 문구 ID → 문자열(`TraitTexts.asset`). 에셋·행 없으면 `TraitTexts` 상수 |

### 1.1 ID·후보·버전(할 일 125 1/5)

- **영구 ID = 명세 문자열 ID**(`PAL_1_Q_DAMAGE` 등). 네트워크 행·재합류 보관의 특성 키 = `TraitIdHash.Of(id)`.
- **후보 = 캐릭터·차수별 고정 3개**(`TraitCatalog` 표 순서, 차수 = 획득 수 + 1). 랜덤 없음. 자동 선택·제시 없는 획득(`GrantParticipantServer`) = 첫 번째.
  캐릭터 판별 = `TutorialLocalPlayerInfo.ResolveCharacter`(오브젝트 없으면 선택 저장소 → 로스터). 정의가 등록되지 않은 후보는 빠지고, 0장이면 기존 Empty 흐름.
- 풀 검증(`TraitCatalogIndex.Errors`) — 빈 ID·해시 0·중복·해시 충돌·명세표에 없는 ID 는 오류 로그 + 제시 안 함. 캐릭터 풀 검증(`TraitPool.Validate`) — 빈 칸·다른 캐릭터 정의·같은 캐릭터 풀 둘.
- **목록 버전**: 서버가 풀 순서 ID 해시 목록의 버전을 `NetworkVariable` 로 복제 → 클라가 자기 값과 다르면 오류 로그(접속은 끊지 않는다).
- 플레이어 간 같은 특성 중복 허용(캐릭터별 후보라 사실상 안 겹친다).

### 1.2 무적 토큰 동기화

- 제시·선택·자동 획득·이탈 때마다 `SyncSelectionShields` 가 모델의 "미선택" 집합과 토큰을 맞춘다(계획 `TraitSelectionModel.PlanSelectionShields`).
- **제시 순간 플레이어 오브젝트가 없던 미선택자**: 스폰되면 `PlayerLifeCycleController.ServerLifeStateChanged`(스폰 때 (s,s) 초기 통지)를 받아
  다음 `Update` 에서 다시 건다 — 스폰 통지 시점엔 NGO 가 아직 `NetworkClient.PlayerObject` 를 채우지 않아서 한 프레임 미룬다. 이미 고른 뒤 스폰되면 걸지 않는다.
- 디렉터 디스폰 시 전부 해제.

## 2. 외부 API

### 서버 (`IsServer`, 디렉터 스폰 뒤)

| 시그니처 | 의미 |
|---|---|
| `bool StartOfferRoundServer()` | 접속 중인 전원에게 새 라운드 제시, 표시 옵션 None |
| `bool StartOfferRoundServer(TraitRoundDisplay display)` | 위 + 창 표시 옵션(`Intro` = 상단 설명, `LastNotice` = 아래 한 줄) |
| `bool StartOfferRoundServer(IEnumerable<ulong> clientIds)` | 지정한 플레이어에게만 제시, None |
| `bool StartOfferRoundServer(IEnumerable<ulong> clientIds, TraitRoundDisplay display)` | 지정 + 표시 옵션 |
| `bool AutoChooseServer(ulong clientId)` | 그 플레이어의 제시 후보 중 **첫 번째** 자동 획득. 대상 아님(이미 고름·제시 없음) = false |
| `TraitJoinResult AddParticipantServer(ulong clientId)` | 지금 라운드에 참가자 1명 추가 — 그 사람에게만 제시, 기존 제시·선택 보존. `Offered`·`Empty`(0장)·`AlreadyInRound`(무시)·`NoRound` |
| `void StartRoundWithoutOfferServer(TraitRoundDisplay display)` | (할 일 34 구간 스킵) 아무에게도 제시하지 않고 새 라운드만 연다 |
| `TraitJoinResult GrantParticipantServer(ulong clientId)` | (할 일 34) 지금 라운드에 **제시 없이** 참가 + 바로 첫 후보 획득 — 클라 창이 열리지 않는다. `Granted`·`Empty`·`AlreadyInRound`·`NoRound` |
| `bool IsInRoundServer(ulong clientId)` | 지금 라운드 참가자인가(제시 중·고름·0장) |
| `int AutoChooseAllPendingServer()` | 이번 라운드 미선택 전원 자동 획득. 처리 인원 수 반환 |
| `bool IsRoundCompleteServer` | 이번 라운드 참가자 전원이 골랐나(라운드 없음 = true) |
| `bool HasPendingChoiceServer(ulong clientId)` | 그 플레이어가 아직 안 골랐나 |
| `event Action<TraitChosen> ServerTraitChosen` | 1장 획득(수동·자동). `ClientId`·`TraitHash`·`Trait`·`Auto`·`Round` |
| `event Action<int> ServerRoundCompleted` | 라운드 참가자 전원 완료(0장 제시·이탈 포함). 인자 = 라운드 번호. 라운드당 1번 |

`StartOfferRoundServer` 반환값: **false = 아무에게도 1장도 제시하지 못했다**(후보 0·미스폰·참가자 없음) → 호출자는 선택 단계를 건너뛴다.
false 일 때 `ServerRoundCompleted` 는 같은 호출 안에서 이미 불렸을 수 있으므로 기다리지 말 것.

### 클라·전 피어

| 시그니처 | 의미 |
|---|---|
| `static TraitDirector Instance` | 씬의 디렉터(없으면 null) |
| `TraitPool Pool` · `TraitCatalogIndex Catalog` · `TraitDefinition GetTrait(int traitHash)` | 해시 → 정의 조회 |
| `int Round` · `TraitRoundDisplay RoundDisplay` | 현재 라운드 번호(0 = 없음) · 표시 옵션 |
| `void RequestChoose(int traitHash)` | [로컬 플레이어] 제시 후보 중 하나 선택 요청. 판정은 서버(위반 = 무시 + 로그) |
| `void GetOffered(ulong clientId, List<int> result)` · `void GetAcquired(ulong clientId, List<int> result)` | 제시 후보(안 골랐을 때만) · 획득 특성(획득 순서) — 값은 해시 |
| `bool HasPendingChoice(ulong clientId)` | 제시 행이 있나 |
| `event Action ClientStateChanged` | 복제 상태 변경 — UI 는 프레임당 한 번 모아 평가 |

### 호출 순서 (한 라운드)

1. 서버: `StartOfferRoundServer(display)` → 모델이 고정 후보를 고르고 행 복제 → 미선택자에게 무적 토큰.
2. 클라: `ClientStateChanged` → 창이 열림 → 카드 클릭 → (상충이면 확인창 — §11.3) → `RequestChoose(hash)`.
3. 서버: RPC 검증 → 효과 적용(장부) → 행 재작성(제시 행 삭제·획득 행 추가) → 토큰 해제 → `ServerTraitChosen`.
4. 전원 완료 시 `ServerRoundCompleted(round)`. 교착이면 서버가 `AutoChooseAllPendingServer()` 로 마무리(3과 같은 경로, `Auto = true`, 확인창 없음).

## 3. 배치 순서 (실제 배선은 [tutorial-corridors-zone2.md](tutorial-corridors-zone2.md))

1. ✅ `4.MapScene` 루트에 `TraitDirector`(NetworkObject) — `pool` 에 `TraitPool.asset`.
2. ✅ `TraitUI.prefab` 인스턴스 **1개**. 씬에 `EventSystem` 이 있어야 카드 클릭이 된다(부트스트랩 `PersistentEventSystem`).
3. 복도 트리거(서버)가 **라운드당 1번만** `StartOfferRoundServer` 를 부른다(첫 도착자만 지정, 이후 도착자는 `AddParticipantServer`):
   복도1 = `TraitRoundDisplay.Intro`, 복도4 = `TraitRoundDisplay.LastNotice`, 복도2·3 = `None`.
4. 출구 게이트(G2·G4·G7·G10)는 **특성 디렉터가 아니라 맵씬 디렉터 모델**이 연다 — 복도 흐름이 `ServerTraitChosen`(수동·자동)마다
   `ReportCheckServer(TraitCorridorN)` 을 보내고, 모델이 '전원 체크 / 첫 완료자 뒤 60초'로 연다. 0장 받은 사람(`Empty`)·특성 디렉터 없음은 도착 순간 바로 체크한다.
5. 교착 방지 60초·타이머 만료·보스방 도착 때 미도착자를 `AddParticipantServer` 로 넣고 `AutoChooseAllPendingServer()` —
   🔴 **반드시 강제 이동·연출 잠금(`PlayerEncounterLock.IsCinematicLocked`) 전에** 호출한다(만료는 `Expired` 로 바뀐 프레임에 처리).

## 4. 수치 출처 — `PlayerTraitModifiers`(할 일 125 2/5)

- 특성 수치는 **상태이상(StatusEffect)을 쓰지 않는다**. 서버가 고른 특성 ID(해시) 목록을 복제하고, 서버·클라가 같은 SO 에서 각자 계산한다(서버 권한 + 클라 1차 검증).
- `PlayerStats`(기본%·전체%·가하는 피해%) · 피해 기준(Dref, `Player.ResolveDamage` 키) · 스킬 쿨(`GetEffectiveCooldownTime`)이 이 출처를 읽는다 —
  보스 입장 디버프 정리(`ClearDebuffsServer`)·연출 잠금과 무관하고, **1 미만 배율(페널티)도 그대로** 유지된다. 상세 [player-stats.md §4](player-stats.md).
- 같은 ID 는 한 번만, 다른 특성 배율은 곱한다. 쿨 배율이 바뀌면 진행률 보존(명세 §6).
- 질의 API: `DamageMultiplier(key)` · `CooldownMultiplier(slot, form)` · `HealMultiplier` · `AreaMultiplier(key, axis)` · `ResolveSkillValue(이름, 데이터 값)` · **`HasTrait(id)`**(모드 변경 스위치).

## 5. 효과 클래스 10종 · 장부 · 시전 ID(할 일 125 3/5)

코드: `Assets/1.Scripts/Traits/Effects/` · 서버 허브 `Player/Stats/PlayerTraitEffects.cs` · 테스트 `TraitEffectTests`. 특성 정의 = 효과 목록(`[SerializeReference]`, 인스펙터 '효과 추가') + 수치. 옛 효과 enum 은 폐지.

| # | 클래스 | 형 | 스킬(캐릭터 레인)이 쓰는 곳 |
|---|---|---|---|
| ① | `TraitDamageEffect` | 수치 | 이미 연결 — Dref |
| ② | `TraitCooldownEffect` | 수치 | 이미 연결 — `GetEffectiveCooldownTime` |
| ③ | `TraitCooldownRefundEffect` | 사건 | 사건 발행만 필요(아래) |
| ④ | `TraitHealEffect` | 수치+등록 | 팔라딘 패시브 회복이 `PlayerTraitEffects.HealServer` 로 연결(배율·초과 전달·Healed 사건) |
| ⑤ | `TraitShieldEffect` | 사건 | 특성 채널 `ShieldType.Trait`(출처 = 특성 해시, 큰 값 유지·지속 갱신) |
| ⑥ | `TraitKnockbackEffect` | 등록 | 적중 직전 `PlayerTraitEffects.ModifyOutgoingAttackServer(key, castId, target, dir, ref info)` |
| ⑦ | `TraitAreaEffect` | 수치 | `PlayerTraitModifiers.AreaMultiplier(key, axis)` |
| ⑧ | `TraitDerivedAttackEffect` | 등록 | `PlayerTraitEffects.GetDerivedAttacks(key, list)` → `DamageFrom(Dref)`·`Counter`, 판정 `TraitHitFlags.Derived` |
| ⑨ | `TraitNextAttackBuffEffect` | 사건 | 첫 원래 피해 판정에서 `PlayerTraitEffects.ConsumeNextAttackMultiplierServer(key, castId)` |
| ⑩ | `TraitSkillValueEffect` | 수치 | `PlayerTraitModifiers.ResolveSkillValue(이름, 데이터 값)` — 칸 = `key`·`op`·`scale`(Raw / OnePlus 1+값 / OneMinus 1−값)·`value`·`name`(카드 토큰). 캐릭터 전용 수치 칸(거너 7종·돌파의 방패)은 할 일 133 에서 이걸로 합쳤다 — 새 '스킬 수치 바꾸기' 특성도 새 클래스 말고 이 칸으로 |
| — | `TraitStatEffect` | 수치 | PlayerStats 기본%·전체%·가하는 피해% |

- **모드 변경 7개**(돌파의 방패·집속 연사·그림자 회귀·한계 돌파·축열 포격·정온 운용·절제된 분노)는 효과 클래스가 아니다 — 스킬이 `PlayerTraitModifiers.HasTrait(TraitModeIds.…)` 로 묻고 자기 코드 안에서 분기한다.
- **사건**: `AttackLanded` 는 `Player.ServerAttackLanded` 에서 자동(시전 ID·형태 포함). `GuardSucceeded`·`PassiveProc`·`SkillCompleted`·`BasicAttackFired` 는
  스킬이 `PlayerTraitEffects.RaiseServer(trigger, skill)` 로 알린다(캐릭터 레인 몫). 거름 = `TraitTriggerFilter`(시전당 1회·간격, `TraitProcGate`).
- **시전 ID**: `PlayerAttackLanded.CastId` — 스킬은 서버 승인 때 `PlayerSkillBase.ServerCastId` 를 받고, 그 스킬을 출처로 낸 적중은 자동으로 묶인다.
  평타 스윙·투사체 1발은 발행마다 새 값. 여러 판정을 한 동작으로 묶어야 하면 `RaiseServerAttackLanded(..., castId)` 로 직접 준다.
- **되돌리기 장부**(`TraitEffectLedger`): 이벤트형 효과의 핸들을 특성 해시 단위로 기록 — 재선택 [예] = 전부 되돌림 뒤 다시 고른 것만 다시 건다.
  '획득 1회' 동작(`HasOnceAction`)은 표시가 남아 재선택·재합류(`TraitRejoinSnapshot.UsedOnce`)에서 다시 하지 않는다. 이미 지급된 보호막은 되돌리지 않고 만료에 맡긴다.
- 사망(살아 있음 → 그 밖)이면 허브가 대기 상태(다음 공격 강화·파생 카운터·대상별 1회 기억)를 지운다. 선택한 특성은 유지.
- `Unit.HealHp` 는 실제 회복량(int)을 돌려준다 — 초과 회복 = 요청 − 반환값.

### 5.1 밀치기·뇌진탕(할 일 125 3/5·4/5)

- 🔴 **밀치기는 `AttackInfo` 넉백(+경직) 필드 경로 한 곳** — CC 를 실제로 거는 코드는 `TraitKnockbackEffect.ApplyCrowdControl` 하나다.
  보스 쪽(할 일 134)은 들어왔다: 23호 넉백 입구 = `Unit.Knockback`(`OnKnockback` override) · 보스·중간보스 상태별 슈퍼아머(평상·제압에서 밀치기·기절·뇌진탕만 거부, 취약 허용 — 둔화·속박·침묵·약화·띄우기는 상태와 무관하게 걸린다) — 표는 [monster-aggro-hitreact.md §2.2](monster-aggro-hitreact.md).
  **경직은 0 이어도 된다**(기본값 0, '경직 0 금지' 검증 제거) — 23호도 넉백 세기만으로 취약 밀림이 걸린다. 직접 걸기로 바꿀 때도 그 메서드 한 곳만 고친다. 보스 질의 인터페이스는 없다.
- **뇌진탕**(`StatusEffectType.Concussion`, 순수 규칙 `ConcussionState`): 맞을 때마다 남은 시간을 0.75초로 **재설정**(가산 아님) → 넉백이 끝나는 순간 기절(Stunned)로 전환
  (넉백 중이 아니면 즉시). 기존 기절과 겹치면 남은 시간 중 큰 값. 상태가 제압으로 바뀌면 대기 중인 '밀치기 후 기절'은 취소. 차단류 CC(`BlockedCrowdControl`)라 슈퍼아머 대상은 거부(보스·중간보스 상태별 슈퍼아머도 거부 — 거부 여부는 수신측이 정해 `ConcussionState` 에 넘긴다).
  효과·머리 위 표시는 기절과 같다(새 VFX 없음). 플레이어 쪽은 즉시 기절 변환.
- 특성 입구 = `TraitKnockbackEffect.concussionDuration`(0 = 없음) → `AttackInfo.concussionDuration`. 팔라딘 Q 매 틱 적용(제압의 진격)은 캐릭터 레인이 쓴다. 테스트 `ConcussionTests`.

## 6. placeholder 6종(Legacy)

옛 튜토리얼 placeholder 6개(`TutorialTrait_AttackUp10`·`AttackUp20`·`MoveUp10`·`MoveUp15`·`HitCdrQ`·`HitCdrE`)는 `9.ScriptableObject/Traits/Legacy/` 에 보관만 한다 — 풀에서 뺐고 효과 목록도 비었다(옛 enum 폐지).
새 36개가 들어오기 전까지 풀이 비어 복도 특성 라운드는 0장으로 지나간다. 데이터 테이블·Strings 키 수집도 `Legacy/` 를 뺀다.

## 7. 알려진 제약

- **틱 판정 공격**(거너 빔 등)은 틱마다 `ServerAttackLanded` 가 난다 — 같은 시전 ID 로 묶이므로 '시전당 1회' 거름을 쓰면 한 번.
- **끊긴 플레이어의 획득 기록은 모델에서 삭제**된다 — 판 명단 안 사람(데모 흐름)은 §10 재합류로 되돌아온다. 명단 밖은 재접속하면 처음부터.
- **추락 피해는 무적을 우회**한다 — 선택 중에도 떨어지면 맞는다.
- 선택 중 무적은 플레이어 오브젝트 기준이다. 미선택 중 오브젝트가 바뀌면(디스폰 후 재스폰) 새 오브젝트엔 토큰이 다시 걸리지 않는다 — 맵씬 흐름엔 그런 경우가 없다.
- 파생 공격·밀치기·뇌진탕은 판정만 — VFX 는 별도 할 일(계획 §1). 예외: 거너 잔류 포격 에너지 폭탄 구체(§7.1).
- 강화 스킬·추가 공격 연출 이벤트(이펙트 구독용, 할 일 145) → [trait-effect-events.md](trait-effect-events.md).

### 7.1 거너 잔류 포격 = 몸에 붙는 에너지 폭탄 (할 일 149)

- **붙기**: Q 가 정상 종료(End 이벤트·그 안전망)되면 이번 시전 원래 발에 **실제로 맞은 대상마다** 폭탄 1개(`GunnerEchoPlan.AddHitTarget` — 집속 연사로 여러 번 맞혀도 시전당 대상 1개). 아무도 안 맞혔으면 없음, 중단·사망·발사 전 종료면 없음(자격은 예전 그대로). 보스에게도 붙는다. 이전 시전 폭탄은 덮어쓰지 않는다(같은 대상에 둘 붙을 수 있다).
- **터지기**: 파생 명세 `Delay`(xlsx `Traits_Gunner`, 지금 0.15초) 뒤 **붙은 그 대상 하나에게만** 서버 피해(`GunnerBeamAttack.HitUnit` — 도형 질의 없음). 피해 = 폭탄마다 원래 발 예정 단일 대상 합계 × `DamageRatio`(나누지 않는다), `TraitHitFlags.Derived`·집중 포화만(밀치기·보호막·자원·적중 사건 없음).
- **대상이 먼저 죽거나 디스폰**되면 시각과 무관하게 바로 그 자리 빈 폭발(표시만, 피해 없음). **사수 사망·씬 종료**면 터뜨리지 않고 취소(구체도 사라짐).
- **동기화**: 서버 = `GunnerEchoBombs`(순수 규칙, 피해·시각). 복제 = `GunnerBeamView` 의 `NetworkList<GunnerEchoBombState>`(폭탄 번호·대상 NetworkObjectId) — 새 RPC 없음, 늦게 합류한 클라도 붙은 구체가 보인다. 목록 RemoveAt = 폭발 연출, Clear = 조용히 지움.
- **겉모습**(149 후속 6): **월드 VFX 프리팹 + 깊이 무시 머티리얼** — 폭탄마다 `GunnerBeamView.echoBombVfxPrefab`(Player_Gunner Variant, `[DataTableIgnore]`)을 복제해 대상 몸 콜라이더 윗면 가운데(`GunnerEchoBombTargets.BodyPoint`) + 오프셋을 매 프레임(LateUpdate) 따라가게 한다(이동·넉백). 겉모습·애니·파티클·블룸은 **프리팹 안에서** 꾸민다 — 이펙트 교체 = 이 칸만 바꾼다. 런타임 텍스처·메시 생성 없음(프리팹 복제만).
  - **ZTest Always 가 필요한 이유**: 붙는 점이 몸 메시 바로 위·안이라 보통 머티리얼(ZTest LessEqual)은 불투명 몸에 묻혀 안 보인다(149 후속 3까지의 3D 구체가 그랬다). 그래서 프리팹 렌더러 머티리얼은 깊이 무시여야 한다. URP 기본 Unlit/Particles Unlit 에는 ZTest 칸이 없어 작은 셰이더 `Effects/OverlayBillboard`(`1.Scripts/Effects/Shaders/EffectOverlayBillboard.shader` — 텍스처 × HDR 색, 블렌드·ZTest 칸, 기본 ZTest Always·ZWrite Off·Transparent 큐, 정점 빌보드)를 뒀다. 파티클을 쓰면 그 머티리얼도 이 셰이더(또는 Depth Test Always 셰이더 그래프)로.
  - **블룸**: 투명 큐라 후처리 전에 그려져 HDR 색(강도 > 1)이면 Bloom 이 먹는다. 맵씬 Volume `VP_CombatLevel_Toon` 임계값 1.1 — 최대 채널이 1.1 을 넘게. 카메라(`MainCamera.prefab`) HDR·후처리 켬, URP 에셋 HDR 켬.
  - **부작용**: 깊이를 안 보니 **벽·지형 뒤 대상에도 보인다**. 팀원 휘도 암순응(간파 A2)은 휘도 0.8 아래 픽셀을 낮춰서 어두운 색 구체는 대부분 어두워진다(다른 어두운 색 월드 이펙트와 같은 규칙). 오너 마스크 암전·적중 섬광·시네마틱 덮개는 화면 전체 덮개라 다른 월드 이펙트처럼 덮인다(렌더링 레이어 비트 4 없음 = 남김 아님).
  - **칸은 비어 있음** — 이펙트 담당이 프리팹을 연결한다. 머티리얼은 `Effects/OverlayBillboard` + ZTest Always 권장(이유: 몸에 묻히지 않게). 비어 있으면 구체 표시만 생략(경고·예외 없음, 서버 판정·피해·폭발 연출은 그대로 — `GunnerEchoBombVisuals` 가 프리팹이 있을 때만 복제). 149 후속 4~5 의 화면 공간 스프라이트(`echoBombSprite`/`Size`/`Color`, `OverheadHealthBarCanvas`)는 블룸·애니를 못 써서 걷어냈다.
  - Attached·Exploded 사건 위치도 같은 점(오프셋 포함), DebugTraitLab 표식은 오프셋 없는 몸 점.
  - **예비 구체(149 후속 3)**: 구체는 Q 가 대상에 **맞은 순간** 전 피어에 먼저 뜬다 — 기존 `ChargeLaserFiredRpc` 에 맞은 대상 NetworkObjectId 배열 한 칸(`echoTargetIds`, 새 RPC 없음)을 실어, 받는 피어가 `GunnerEchoBombVisuals.AddPreview` 로 대상마다 1개(같은 대상 재적중·집속 연사 여러 발도 1개). 서버는 잔류 포격 파생 공격이 있을 때만 채운다(특성 없으면 빈 배열 → 예비 구체 없음). 서버 부착(NetworkList Add)이 오면 같은 대상의 미채택 예비 구체를 **같은 인스턴스·같은 표시 시각** 그대로 이어받는다(새로 안 만듦). 그 뒤 폭발·Clear 는 예전과 같다. 채택 안 된 예비 구체(중단·사망 → 폭탄 없음)는 `echoBombPreviewTimeout`(초, 기본 1.5 — 그 대상 마지막 적중부터) 뒤 폭발 연출 없이 지운다 — 정리 경로는 이것 하나. 기본값 근거: 정상 부착 = 마지막 발 `Gunner_Q_Fire`(클립 `gunner_skill_Q_fire` 0.8초·상태 속도 1) 시작 + End 이벤트 0.4초 → 약 4배 여유. End 를 놓쳐 안전망(`maxActiveDuration` 5초, 시전 시작부터)으로 끝나면 1.5초를 넘길 수 있고, 그때는 예비가 먼저 지워지고 부착 때 구체가 새로 뜬다(비정상 경로라 감수). 서버 판정·부착·폭발 시각은 그대로.
  - 조절 칸(연출 전용, `[DataTableIgnore]` — xlsx 아님): `echoBombShowDelay`(초, 기본 0) = **Q 가 그 대상에 맞은 순간(예비 구체 생성)** 뒤 구체를 띄우기까지 지연. 이어받은 구체는 그 시각 그대로, 예비 없이 붙은 폭탄(늦은 합류 등)은 받은 시각부터 같은 지연. `echoBombPreviewTimeout` = 위 예비 구체 대기 시간. 서버 피해·폭발 시각(`Delay`)은 그대로라 지연이 더 길면 구체 없이 폭발 연출만 난다. `echoBombOffset`(월드 기준 m, 기본 0) = 몸 점에 더하는 오프셋 — 월드 기준이라 대상이 돌아도 위·옆 방향이 일정. 판정과 무관. 폭발 연출 = Q 몬스터 적중 연출(`chargeMonsterHitPlayer`) 재사용. 이펙트 교체는 프리팹 칸만 바꾼다.
- 특성 랩 기즈모 = 붙어 있는 폭탄마다 대상 몸통에 표식 구(호스트 창, '대상 1체·도형 아님'). 테스트 `GunnerTraitTier34Tests`(폭탄 수명·단일 대상·대상 사망), `GunnerEchoBombPreviewTests`(예비 구체 생성·이어받기·만료).
- 🟨 카드 문구(Strings `Trait.GUN_4_ECHO_Q.*`, xlsx)는 아직 "같은 방향으로 레이저를 한 번 더" — 기획이 xlsx 에서 고친다.

## 8. 교착 방지 게이지 (할 일 15 후속)

선택 창 패널 맨 아래(`Window/Panel/Deadline`) — 가로 게이지 + `n초 뒤 자동 선택` + `선택 완료 n/전체`. 기본 꺼짐이라 안 보일 땐 창 레이아웃이 전과 같다.

- **표시 조건**: 기다리는 개인 게이트가 진행 중 복도의 출구(G2/G4/G7/G10)이고 첫 완료자 뒤일 때만 — `MapSceneTraitCorridorRules.ShouldShowTraitDeadline`.
- **값**: `MapSceneDirector.TryGetWait`(복제 대기 상태) · 분모 = `MapSceneDirector.DeadlockSeconds`.
- **의존 방향**: 특성 모듈은 맵씬 디렉터를 모른다. 복도 흐름(`MapSceneTraitCorridorFlow`, **모든 피어**)이 `TraitSelectionView.Instance.SetDeadline(visible, remaining, total, done, count)` 로 넣는다.
- **시간**: 창이 `Time.unscaledDeltaTime` 으로 로컬 감소(0 에서 멈춤 — 만료 처리는 서버), 다음 `SetDeadline` 이 다시 맞춘다. `TraitDeadlineModel.UrgentSeconds`(10초) 이하면 `deadlineUrgentColor`.

## 9. 전멸 리스폰 뒤 특성 초기화 · 재선택 (할 일 30 3단계)

> 사양·흐름 원본: [party-lives-respawn.md §4](party-lives-respawn.md#4-특성-초기화--재선택-3단계--구현됨). 여기는 특성 모듈 쪽 API·구조만.

| 조각 | 역할 |
|---|---|
| `TraitResetModel`(순수) | 플레이어별 없음 → 팝업 대기(세대) → (예) 재선택 중(남은 라운드, `ReselectRounds` = 4) → 없음. 응답 검증 `Answer(clientId, generation, reset)` = `Kept`·`Reset`·`NotPrompted`·`StaleGeneration` |
| `TraitEffectLedger<T>`(순수) | (플레이어, 특성 해시) → 되돌리기 핸들 장부. `TakeAll(clientId)` 는 그 플레이어 몫만 |
| `TraitSelectionModel` 추가 | `StartPersonalRound` · `GetOfferSeq` · `IsPersonalRound` · `ResetPlayer` · `CancelOffer` |
| `TraitDirector` 추가 | `PartyWipeRespawner.PartyWipeRespawned` 구독(서버) · 복제 행 `EntryKind.ResetPrompt` · `NetworkVariable<bool> traitResetBusy` |
| `TraitSelectionView` 추가 | 팝업을 창과 같은 캔버스에 **런타임 생성** — 루트 `UiModalBlocker` + 딤 + 질문 + [예]/[아니오] |

| 시그니처 | 의미 |
|---|---|
| `bool TryGetResetPrompt(ulong clientId, out int generation)` | [전 피어] 그 플레이어가 팝업에 답해야 하나 + 세대 |
| `void RequestResetAnswer(int generation, bool reset)` | [로컬 플레이어] 응답 → `AnswerResetPromptServerRpc` |
| `bool IsTraitResetInProgress` | [전 피어] 누군가 팝업 대기·재선택 중 — `MapSceneBossPath.IsBossPadEnabled` 가 패드를 잠근다 |

- **재선택**은 1차부터 다시(고정 후보). 이미 쓴 '획득 1회' 효과는 다시 실행하지 않는다(§5 장부). 재선택 획득 통지(`TraitChosen.Round`)는 **0** 이라 복도 흐름이 반응하지 않는다.
- **창 열림 = 내 제시 번호(OfferSeq)** — 다른 사람의 재선택이 내 창을 닫지 않는다.
- 문구: `TUT_TRAIT_RESET_PROMPT`·`TUT_TRAIT_RESET_YES`·`TUT_TRAIT_RESET_NO`(`TraitTexts` 상수). 테스트 `TraitResetModelTests`.

## 10. 판 도중 재합류 (할 일 48 C14 R4c)

- **퇴장**: `HandleClientDisconnected` 가 모델에서 지우기 직전 `TraitRejoin.Capture` → `RejoinMemory.Server.SaveTraits(playerId)`.
  담는 것 = 획득 목록(해시, 순서) · 지금 복도 라운드에서 이미 골랐나 · 초기화 팝업 세대 / 재선택 남은 라운드·열린 후보 · 쓴 '획득 1회' 표시.
- **재스폰**: `MatchRoster.PlayerRejoined` → `TraitRejoin.Restore`(새 clientId) → 장부에 없는 획득 특성만 효과 적용(멱등) → `RebuildEntries`.
- 테스트: `TraitRejoinTests`.

## 11. 카드 UI · 문구 · 시트 (할 일 125 5/5)

### 11.1 카드 — 이름 / 분류 / 설명 + 도움말 한 줄

- 카드(`TraitCardView`) = 아이콘 · 이름 · **분류 줄**(`Category`, 이름 바로 아래) · 설명(명세 §2 '이름 / 짧은 분류 / 설명'). 배치는 아래 C1 아트(할 일 165).
- 창 패널의 카드 줄 아래 **도움말 한 줄**(`Panel/Help`) = `UI.Trait.Help`("보스는 취약할 때만 밀치기와 기절이 적용됩니다.") — CC 공통 안내를 카드마다 반복하지 않는다.
- 프리팹 반영 = `Tools/Tutorial/특성 선택 UI — 분류·도움말 줄 추가 (배치 유지)` — 지금 프리팹에 빠진 줄만 더한다(글꼴은 옆 글자에서 복사, 이미 있으면 저장 안 함).
  전체 재생성 메뉴(`특성 선택 UI 프리팹 생성`)는 손으로 다듬은 배치를 덮어쓰므로 쓰지 않는다(글꼴 경로는 서울알림으로 고쳐 두었다). 카드 아트만 바꿀 땐 아래 §11.1.1 메뉴.
- 설명 = 템플릿 → `TraitTooltip.FormatDescription`(`SkillTooltipFormatter`). 실패하면 `[오류: …]`(`UI.SkillTooltip.Error`) + 경고 — 스킬 툴팁과 같은 관례. 획득 아이콘 호버 툴팁도 같은 문구.

#### 11.1.1 카드 C1 아트 (할 일 165)

- 아트(SVN, 읽기만) = `Assets/50.Art/UI/TraitCard_C1/Sprites/`(카드판·스킬 프레임·마스크·키캡 4종·마우스 2종, PNG 2배·PPU 200) + 특성 아이콘 `Assets/50.Art/UI/HUD/TraitIcon/<캐릭터>/`.
  배치 원본 = 납품 `배치표.csv`(카드 560x413, 좌상단 X/Y·UI 크기 — 투명 여백 포함).
- `CardTemplate`(560x413) 자식 = 그리는 순서대로:

| 오브젝트 | 내용 | 배치(X, Y, 가로x세로) |
|---|---|---|
| (루트 Image) | 투명 클릭 판정 — `Button`·`TraitCardView` 는 루트 그대로 | 0, 0, 560x413 |
| `Panel` | `TraitCard_C1_Panel` · 버튼 색 변화 대상(`targetGraphic`) | -2, -2, 564x417 |
| `TraitIcon` | 특성 아이콘(`TraitDefinition.Icon`, 비면 `placeholderIcon` = `Trait_Empty`) — 피드백 1 로 캐릭터 심볼(`Trait_<캐릭터>`) 대신 | 19, 10, 118x118 |
| `Name` | 서울알림 Heavy 34(자동 축소 24~34), 왼쪽 정렬 한 줄 | 142, 38, 340x40 |
| `Category` | 서울알림 Medium 17, 한 줄(넘치면 …) | 144, 84, 340x22 |
| `SkillMask` → `SkillIcon` | `TraitCard_C1_SkillMask` + `Mask`(마스크 그림 숨김), 안에 **강화하는 스킬 아이콘** 132x132(아래 출처 규칙) | 45, 182, 136x136 (아이콘 47, 184) |
| `SkillFrame` | `TraitCard_C1_SkillFrame` — 프레임·마스크·아이콘 중심 (113, 250) 동일 | 37, 174, 152x152 |
| `Keycap` | 키캡/마우스 하나 — 스프라이트만 바꿈(비율 유지 38x41 상자 → 키캡 38x40·마우스 30x41). 키 입력이 아닌 특성은 숨김 | 94, 335, 38x41 |
| `Description` | 서울알림 Medium 22, 줄 높이 34px(글꼴 기본 줄 높이에서 `lineSpacing` 계산), 자동 축소 16~22 | 224, 170, 288x170 |

- 키캡 판정 = `TraitKeycapRule.FromIconName`(순수 함수 한 곳, 테스트 `TraitKeycapTests`): 아이콘 스프라이트 이름을 `_` 로 나눈 토큰 중 `Q`/`E`/`R`/`LMB`/`RMB` 와 **정확히 같은 첫 토큰**의 키. 키 뒤 접미사(`_Q_Change`·`_E_Trans`·`_R_Remove`)도 그 키, 키 토큰이 없으면(`Passive`·`Heat`·`Heat_Remove`·`Passive_Change`) `Blank` = **키캡 숨김**(피드백 1 — `Keycap_Blank` 는 카드에서 안 쓴다). 아이콘이 없으면 `Blank`.
- 스킬 슬롯 그림 출처(피드백 1) = `TraitCardSkillRule.SourceOf(키캡)`(순수 함수 한 곳, 테스트 `TraitKeycapTests`): Q → `PlayerSkillSlot.Main`, E → `Sub`, R → `Ultimate`, RMB → `Interrupt`, `Blank` → 패시브, LMB → **기본 공격 아이콘**(`BasicAttack` — 카드 칸 `basicAttackIcon` = `Assets/2.Prefabs/UI/SkillTooltipGenerated/tooltipIcon_Sword.png`, 흰색 검).
  기본 공격 아이콘만 `Image.color` = 보는 사람 캐릭터 색 — 뷰어 `NetworkObject.PrefabIdHash` → `CharacterRosterLookup.FindIndex`(`SpawnedCharacterRoster.TryGetEntry(뷰어, 카드 칸 characterRoster)`) → `CharacterRoster.Entry.themeColor`, 못 찾으면 흰색. 다른 슬롯은 늘 흰색이고 `Bind` 마다 색을 덮어 카드 재사용 때 남지 않는다(색 규칙 `TraitCardSkillRule.SlotTint`, 테스트 `TraitKeycapTests`). 칸 연결 메뉴 `특성 선택 UI — 기본 공격 슬롯 연결 (검 아이콘·캐릭터 색)`(멱등, 카드 C1 아트 적용도 같은 함수를 부름).
  검 그림은 투명 배경이라 `SkillMask` 안 `SkillIcon` 바로 앞 형제 `SkillBackdrop`(검은 Image #000000, 132x132, 틴트 없음 — 카드 칸 `skillBackdrop`)을 깐다. 마스크 안이라 실제 스킬 아이콘(정사각 불투명 그림)과 같은 둥근 사각형으로 잘린다. 켬 = 최종 출처가 `BasicAttack` 일 때만(`TraitCardSkillRule.ShowsBackdrop`, 폴백·다른 슬롯은 끔, `Bind` 마다 덮음). 메뉴 `특성 선택 UI — 기본 공격 슬롯 검은 배경 (검 아이콘 뒤)`(멱등, 카드 C1 아트 적용도 같은 함수를 부름).
  스킬 아이콘은 전투 HUD 와 같은 경로로 뷰어(`Bind` 의 `viewer` = 로컬 Player)에서 읽는다 — 슬롯 = `PlayerSkillController.GetSkill(슬롯).Data.Tooltip.Icon`(SkillCooldownHUD), 패시브 = `IPlayerPassive as ISkillTooltipSource` → 없으면 `GetComponentInChildren<IPassiveTooltipProvider>(true).PassiveTooltip`(PassiveHUD).
  어쌔신 변신 특성(`E_Trans`·`R_Trans`·`R_Remove`)도 그 슬롯의 **지금** 스킬 그림. 못 얻으면(뷰어 없음·아이콘 비어 있음 — 예: 간파 슬롯은 툴팁 아이콘이 없다, 지금 RMB 특성은 없음) 특성 아이콘 → `Trait_Empty`. 아이콘 없는 옛 임시 특성은 스킬 조회를 건너뛴다.
- 창 폭 = 카드 3장 + 간격 24 + 여백 32×2 = **1792**, 카드 줄 높이 413. 창 `Panel` 은 레이아웃만(어두운 판·노란 Outline 없음 — 피드백 1). 배경 흐림은 아래 '배경 블러'.
- **배경 블러 = 열 때 1회 캡처**(할 일 165 후속 A안): 창이 열리는 프레임 끝(`WaitForEndOfFrame`)에 화면 전체(월드 + Screen Space Overlay HUD)를 `ScreenCapture.CaptureScreenshotIntoRenderTexture` 로 한 번 찍고(창은 아직 꺼져 있어 자기 자신은 안 찍힌다) →
  축소 복사(`SystemInfo.graphicsUVStartsAtTop` 이면 상하 뒤집기 보정) → MaskBlur 셰이더 0번(가로)·1번(세로) 패스를 `iterations` 번 → 채도 패스 1회(`Hidden/Traits/TraitBackdropSaturate`, `saturation` < 1 일 때만 — 축소 RT 두 장을 맞바꿔 RT 를 더 빌리지 않음)(`TraitBackdropBlur`) → 창 바로 앞 형제 전체화면 RawImage `Backdrop` 에 깔고 **다음 프레임에** 창을 보인다.
  그 위 어둡게 막(창 루트 `Image`)은 설정 `dimAlpha`(0.25), 블러를 못 쓰면(설정·셰이더 비었음·캡처 실패) 프리팹 값 0.55 로 예전처럼 연다.
  비용 = 여는 순간 캡처 1회 + 축소 블러 몇 번 + 채도 블릿 1회, **창이 열린 동안 매 프레임 0**. 화면 크기 캡처 RT 는 블러 직후, 축소 블러 RT 는 창이 닫힐 때(비활성·디렉터 해제 포함) 반납 — 수명 규칙 `TraitBackdropLifetime`(테스트 `TraitBackdropLifetimeTests`).
  한계: 열린 동안 배경은 **정지 그림**(뒤에서 움직이는 적·HUD 변화가 안 보인다), 창이 **1프레임 늦게** 뜬다. 열린 채 새 제시가 오면 다시 찍지 않고 지난 그림을 쓴다.
  튜닝 = `Assets/9.ScriptableObject/Traits/TraitBackdropBlurSettings.asset`(코드 기본값과 같음): 세기 1.42·**축소 1/4(`downsampleShift` 2)·반복 3** — 기준 "HUD 글자 윤곽이 거의 안 읽힐 정도"(반복 2 는 글자 덩어리 모서리가 남아 3 으로; 처음 값 1/2·1회는 `MaskBlurSettings.asset` 에서 옮긴 것) ·
  **`saturation` 0**(0 = 완전 흑백, 1 = 원색이며 채도 패스 건너뜀, Range 0~1; 루마 = **Rec.709** 가중치 0.2126·0.7152·0.0722, MaskBlur 합성 패스 탈채도와 같은 값) · `dimAlpha` 0.25.
  블러·채도 셰이더 둘 다 설정 SO 의 직렬화 참조(`blurShader`·`saturationShader`)라 빌드 스트립 안 됨 — 채도 셰이더가 비면 원색 블러. 테스트 `TraitBackdropBlurTests`(흑백·원색·채도 패스 RT 반납). 데이터 테이블 시트 없음(연출 기술 값).
  프리팹 반영 = `Tools/Tutorial/특성 선택 UI — 배경 블러 연결 (창 뒤 RawImage)` — `Backdrop` 과 설정 칸만 더한다(멱등, 창·카드·씬 오버라이드 유지).
- 프리팹 반영 = `Tools/Tutorial/특성 선택 UI — 카드 C1 아트 적용 (카드만 교체)` — 카드 템플릿 자식만 다시 만들고(루트 오브젝트·컴포넌트 유지 → 씬 오버라이드 안전) 창 폭·`placeholderIcon` 을 맞추고 창 `Panel` 의 `Image`·`Outline` 을 뗀다. 전체 생성 메뉴도 같은 카드 빌더를 쓰고 창 판을 만들지 않는다.
- 아이콘 연결(데이터 주도) = `Tools/Tutorial/특성 아이콘 연결 (TraitIconMap.csv)` → `TraitIconMapAuthoring.Apply(csv, iconRoot)`. 기본 csv = `Assets/50.Art/UI/HUD/TraitIcon/TraitIconMap.csv`(`icon` 열 = 그 폴더 기준 상대 경로) → 각 `TraitDefinition.icon`. 아트가 csv 를 바꾸면 메뉴를 다시 돌린다. `TraitKeycapTests.ProjectDefinitions_IconsMatchIconMap` 이 어긋남을 잡는다.
- 아직 안 쓰는 것: csv 의 `inactive_icon`(광란 강화 비활성 아이콘), `Common/Trait_ProcGlow`, `Assassin/Trait_Assassin_Trans`, 캐릭터 심볼 `Trait_<캐릭터>`·`Keycap_Blank`(피드백 1 로 카드에서 뺌).

### 11.2 설명 템플릿 토큰

| 표기 | 값 |
|---|---|
| `{이름}` · `{이름:%}` · `{이름:0.0}` | `TraitTooltipValueSource` 가 첫 칸을 ① 등록부 별칭 → ② 정의 자신의 필드(`{effects[0].damageIncrease}` 경로 포함) → ③ 효과 목록 안 필드 이름 또는 ⑩ 스킬 수치 칸의 `name`(해석 전 값 — 감소율 0.25 그대로) 순으로 푼다. ③ 은 그 이름을 가진 효과가 **정확히 하나**여야 한다(둘 이상 = 오류 → 경로나 별칭) |
| `{dmg}` | 특성별 계산식(`TraitContentRegistry.Registrar.Damage` — 캐릭터 레인). 계산식이 없으면 **검증 오류**. 로컬 플레이어가 없으면 `UI.Trait.Card.DamageUnknown`("-") — 0 으로 보이지 않는다. **표시(할 일 171)**: 평소 = 피해 색 글자 `공격력의 150%`(설명 ko 는 `{dmg} 피해`·`{dmg} 추가 피해` — `{dmg}` 뒤에 '의' 를 쓰지 않는다 → `공격력의 450% 피해`, Shift `⚔123 피해`. 계수 범위 `100~150%`, 고정 추가 피해가 있으면 `20 + 공격력의 150%`, 검 아이콘 없음 — `TraitTooltip.FormatDamagePercent`), **Shift(왼/오른쪽)를 누르는 동안만** 현재 공격력으로 계산한 숫자(검 아이콘 + 숫자, 계산식 없음 — `SkillTooltipFormatter.DamageValue`). 두 표기 모두 `<nobr>` 로 감싸 줄 끝에서 중간에 끊기지 않고 통째로 다음 줄로 간다(`TraitTooltip.NoBreak`). 카드(`TraitCardView`)·획득 아이콘 호버 툴팁(`TraitSelectionView`)이 매 프레임 Shift 상태를 보고 바뀐 프레임에만 설명을 다시 쓴다. 안내 문구 없음. 전투 HUD 스킬 툴팁은 이 경로가 아니다(평소 숫자·Shift 계산식 그대로 — `TraitDamagePercentTests.SkillTooltip_OutputUnchanged`) |
| `<style=heal>` 등 | 스킬 툴팁과 같은 Style Sheet([data-table.md §9](data-table.md#9-프로그래머기획--툴팁-문구와-자리표시자)). 특성 카드·호버 툴팁은 TMP 기본 시트(`TextMesh Pro/Resources/Style Sheets/Default Style Sheet`)로 풀리는데, 거기 **`heal` 만** 같은 색 + 검은 아웃라인 머티리얼 프리셋(`<material="SeoulAlrimTTF-Medium SDF - Heal Outline">`, 파일 `Assets/Fonts/SeoulAlrim/Resources/Fonts & Materials/` — 폰트와 같은 아틀라스, 아웃라인 0.3·FaceDilate 0.3)이다(밝은 카드 위 초록 가독성, 171 후속). 전투 HUD 툴팁 시트 `SkillTooltipStyles` 는 색만. 서울알림 Medium 이 아닌 TMP 에 기본 시트 `heal` 을 쓰면 아틀라스가 달라 글자가 깨지니 쓰지 말 것 |
| 본문 굵게 | 카드·호버 툴팁 설명 전체를 `TraitTooltip.Bold` 가 `<b><cspace=-0.07em>…</cspace></b>` 로 감싼다(`TraitCardView.DescriptionOf`, 171 후속) — cspace 는 폰트 boldSpacing(7 = 0.07em)을 상쇄해 자간·줄바꿈을 굵게 전과 같게. 프리팹 fontStyle·시트 Normal 은 그대로, 전투 HUD 스킬 툴팁은 굵게 없음 |

- 비율은 0.20 = 20% 로 보관하고 `{x:%}` 로 표시한다. 효과 필드 이름은 명세 제안 이름과 맞춰 두었다(`damageIncrease`·`cooldownChange`·`healIncrease`·`refundSeconds`·`pushDistance`…).
- 검증 = `TraitTooltip.Validate`/`ValidateDefinition` — 문법·풀리지 않는 토큰·계산식 없는 `{dmg}`·빈 이름. EditMode `TraitTooltipTests.ProjectDefinitions_AllCardTextsResolve` 가 프로젝트 정의 전부를 본다.

### 11.3 2차 상충 확인창(명세 §2)

- 고른 특성이 **보유 특성의 효과를 없애면** 확인창 — "변신을 사용할 수 없어 ‘광란 강화’의 효과를 받을 수 없습니다. 선택할까요?" [확인] = 그대로 선택 요청, [취소] = 아무것도 안 보냄. 자동 교체·환불·숨김 없음.
- 규칙 = 등록부 `r.Conflict(고른 ID, 효과를 잃는 ID, ko 문구)` — 지금은 `ASN_2_RESTRAINT` → `ASN_1_TRANS_E_DAMAGE` 하나(어쌔신 파일에 기반 레인이 미리 등록).
- 순수 상태 = `TraitPickConfirmModel`(테스트 `TraitPickConfirmTests`). **60초 출구 자동 선택은 서버가 첫 후보를 고르므로 확인창이 없다** — 창이 닫히거나 새 제시가 오면 열린 확인창은 보내지 않고 닫힌다.
- 확인창은 초기화 팝업과 같이 런타임 생성(프리팹 무수정).

### 11.4 Strings 키

| 키 | 내용 | ko 폴백 |
|---|---|---|
| `Trait.<ID>.Name` | 이름 | 정의 `displayName` |
| `Trait.<ID>.Category` | 짧은 분류(명세 '분류:') | 정의 `category` |
| `Trait.<ID>.Desc` | 설명 템플릿(§11.2 토큰) | 정의 `description` |
| `Trait.<ID>.Conflict` | 상충 확인창(`{traits}` = 효과를 잃는 보유 특성 이름) | 등록부 ko |
| `UI.Trait.Card.Category` | 분류 줄 형식 `{category}` | 코드 |
| `UI.Trait.Help` | 도움말 한 줄 | 코드 |
| `UI.Trait.Conflict.Default` | 상충 문구가 없을 때 기본(`{traits}`) | 코드 |
| `UI.Trait.Conflict.Confirm` · `.Cancel` | 확인창 버튼 | 코드 |
| `UI.Trait.Conflict.NameSeparator` | 이름 여럿 구분자(", ") | 코드 |
| `UI.Trait.Card.DamageUnknown` | `{dmg}` 계산 불가 자리("-") | 코드 |
| `UI.Trait.Card.DamagePercent` | `{dmg}` 평소 표시 `공격력의 {percent}`(`{percent}` = `150%`/`100~150%`, % 포함) | 코드 |
| `UI.Trait.Card.DamagePercentFlat` | 고정 추가 피해가 있을 때 `{flat} + 공격력의 {percent}` | 코드 |

- 규칙 = `TraitStringKeys`(ID 기준 — 에셋 이름을 바꿔도 번역이 따라간다. ID 에 공백·`.`·`/`·중괄호 금지). 시트에 행이 없으면 인라인 ko — `#키` 를 내지 않는다.
- Export: `UI.Trait.*` 는 `UiStrings.All`(`TraitUiStrings`), `Trait.<ID>.*` 는 `TraitStringKeyCollector`(에디터 `StringKeyRegistry`)가 새 행으로 붙인다(ko = 인라인, en 빈 칸). 시트 반영은 병합 때 메인.
- en 초안은 `_lane-artifacts/common/125/125-캐릭터레인-가이드.md` §6. ‘ ’ 같은 새 기호는 병합 뒤 `Tools/UI/Fonts/Refill SeoulAlrim (Static)` 로 아틀라스 확인([localization.md §10](localization.md#10-글꼴--서울알림체-static-아틀라스)).

### 11.5 데이터 테이블 시트 `Traits_Paladin` / `Traits_Gunner` / `Traits_Assassin`

- 캐릭터마다 한 장, **세로 표** — 열 = 특성 정의 에셋 파일 이름, 행 = 직렬화 경로(`effects[0].damageIncrease`, `effects[1].filter.minInterval`). 비율 0.20 = 20%, 시간 초, 거리 m.
- 연결 = 캐릭터 정의 하위 타입의 `[DataTableSheet(TraitSheets.X)]`(`Traits/<캐릭터>/<캐릭터>TraitDefinition.cs`). 기반 `TraitDefinition`(Legacy)은 테이블 대상이 아니다.
- 데이터 테이블이 `[SerializeReference]` 효과 칸을 따라간다(`DataTableFields.Resolve(target, path)` — 실제 값의 런타임 타입으로 필드·`[Range]`·`[Min]`·`[DataTableIgnore]` 를 찾는다). 원소 이름표는 `effects[0] · TraitDamageEffect`.
- ⑩ 스킬 수치 칸은 `effects[i].entries[j].value` 행으로 나간다(원소 안쪽 배열 — 할 일 133 에서 Export 누락 수정). 같은 행 이름도 특성(열)마다 뜻이 달라 칸의 `key`·`name` 은 인스펙터에서 본다. 133 행 변경 목록 = `_lane-artifacts/common/133/xlsx-columns-c.md`.
- 문자열(`id`·이름·분류·설명)·enum 배열(공격 종류 거름 등)·bool 은 수치 시트에 나가지 않는다 — 문구는 Strings, 거름은 인스펙터.
- **시트가 xlsx 에 없으면 아무것도 적용하지 않는다**(오류·경고 없음 → 인스펙터 값). 빈 표·머리글만 있는 표도 통과(`TraitSheetBindingTests`). 효과 칸 번호가 정의에 없으면(배열 밖) 오류 — 테이블은 효과를 늘리지 않는다.
- 🔴 시트 추가·Export 는 **병합 때 메인이** 최신 development 워크트리에서 한다 — 이 브랜치 코드가 development 에 들어가기 전에 xlsx(SVN)에 시트가 생기면 다른 워크트리는 '시트 이름에 맞는 타입이 없다' 오류로 테이블 Play·빌드가 멈춘다.

### 11.6 캐릭터 레인 확장 지점(요약 — 상세 가이드는 `_lane-artifacts/common/125/125-캐릭터레인-가이드.md`)

| 캐릭터 레인이 고치는 파일 | 용도 |
|---|---|
| `Traits/<캐릭터>/TraitContent.<캐릭터>.cs` | `{dmg}` 계산식·툴팁 별칭·상충 규칙 등록(partial `Register<캐릭터>`) |
| `Traits/<캐릭터>/` 새 `.cs` | 캐릭터 전용 효과·도우미(필요하면) |
| `9.ScriptableObject/Traits/<캐릭터>/` | 정의 에셋 12개(`<캐릭터>TraitDefinition`, 생성 메뉴 `Traits/<캐릭터> Trait`) + `TraitPool_<캐릭터>.asset` 에 넣기 |
| `Traits/Editor/<캐릭터>/<캐릭터>Trait…Tests.cs` | EditMode — 이름만 맞으면 맵씬 메뉴가 자동으로 돈다 |

공용 등록부(`TraitContentRegistry.cs`)·시트 이름(`TraitSheets`)·풀(`TraitPool.asset`)·테스트 메뉴 목록(`MapSceneEditModeTestRunner`)에는 세 캐릭터 자리가 이미 있다 — 캐릭터 레인은 고치지 않는다.

## 12. 테스트

`Tools/Tests/맵씬 EditMode 테스트 실행`: `TraitSelectionModelTests` · `TraitWindowModelTests` · `TraitDeadlineModelTests` · `TraitResetModelTests` · `TraitRejoinTests` ·
`TraitCatalogTests` · `TraitFixedSelectionTests` · `TraitModifierSetTests` · `TraitEffectTests` ·
`TraitTooltipTests`·`TraitSheetBindingTests`·`TraitPickConfirmTests`·`TraitStringKeysTests`(5/5) · `Paladin|Gunner|AssassinTrait…Tests`(캐릭터 레인). `ConcussionTests` 는 전체 실행에서 돈다.
게이지 표시 조건은 `MapSceneTraitCorridorModelTests`.
특성 디버그 랩(§13): `DebugTraitLabTests` · `DebugTraitLabIsolationTests` · `TraitLabAttackShapeTests`.

## 13. 특성 디버그 랩 `DebugTraitLab` (할 일 135)

- **진입** = 툴바 Dev Boot 드롭다운 → `DebugTraitLab` → Play. 빌드 목록에 없다(Dev Boot 가 Play 동안만 넣고 뺀다 — DebugInterruptLab 과 같다). 호스트 1인 기준.
- **씬** = 원점 스폰 · 허수아비 일렬 3개(z 5·7·9, 관통·연사) + 따로 1개(6, 0, 4) · `TraitDirector.prefab`(4.MapScene 과 같은 프리팹) · 카메라·대시 검증·피해 숫자.
- **왼쪽 OnGUI 패널**(`Dev/TraitLab/DebugTraitLab.cs`):
  - 캐릭터 = 팔라딘·거너·어쌔신 버튼 또는 **F1** 순환(`DebugCharacterCycle`, 같은 자리 재스폰). 바꾸기 직전 보유 특성을 비운다.
  - 지금 캐릭터 12개를 1~4차로 나열 — 버튼 = 넣기/빼기(■ 보유). **차수당 하나**(실게임과 같다): 같은 차수 다른 것을 넣으면 기존 것을 뺀다. `규칙 무시` 를 켜면 같은 차수 동시 보유.
  - `전부 빼기` · `차수별 랜덤`(차수마다 하나) · 보유 목록 · 2차 상충 경고(§11.3 등록부 규칙 — 막지 않고 "효과 없음" 만 표시).
  - 배속 슬라이더 0.05~2(`DebugTimeScale`) · `쿨 0`(`DebugZeroCooldown` — Q·E·R·간파·대시, 기본 꺼짐) · `허수아비 HP 리셋`.
  - `공격 범위 기즈모`(할 일 152, 기본 꺼짐, 에디터 전용 — `Dev/TraitLab/TraitLabAttackGizmos.cs`) — 씬의 모든 플레이어마다 지금 위치·바라보는 방향에서 판정 형상을 분류별 색(■ 기본공격 하늘 · 스킬 주황 · 특성 추가 공격 분홍)·이름 라벨로 그린다. 판정 도형 공격은 각 공격이 서버 판정에 넘기는 그 (도형, 포즈) 를 판정과 같은 공용 함수로 받아 `AttackShapeDrawing` 으로만 그린다(할 일 153 6단계 — 바닥 진하게·높이 범위 옅게·도넛 안쪽 고리, 특성 배율·반경·폭이 도형 값으로 바로 반영, 기즈모 쪽 수치 계산 없음). 도형이 아닌 판정(거너 평타 캡슐·R 추적 구·간파 앵커 상자)은 콜라이더 형상 + 라벨 '(도형 아님)'. Game 뷰는 Gizmos 버튼을 켜야 보이고, 특성 추가 공격(파생 명세가 서버 전용)·거너 R 은 호스트 창에서만 나온다. 공용 함수 목록·사용법은 [attack-shapes.md](attack-shapes.md) §7·§8 단계 6. 테스트 `TraitLabAttackShapeTests`.
- **정식 경로**: 넣기/빼기는 `TraitDirector.ReplaceAcquiredServer`(획득 목록 교체 → 재합류와 같은 장부 맞추기) — 수치형은 `PlayerTraitModifiers` 가 획득 행으로, 이벤트형은 장부로 걸고 되돌린다. 뺐다 다시 넣으면 '획득 1회' 동작도 다시 돈다(처음 얻는 것과 같게). 순수 규칙 = `TraitLabModel`.
- 클라 창은 보기 전용(버튼 비활성, 새 RPC 없음). 패널을 클릭하면 게임 입력(기본 공격)도 같이 들어갈 수 있다.
