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
- **겉모습**(149 후속 4): 3D 구체가 아니라 **화면 공간 스프라이트** — 폭탄마다 공용 오버레이 `OverheadHealthBarCanvas`(Screen Space Overlay, sortingOrder -10, 머리 위 체력바와 같은 캔버스) 위 `Image` 하나. 몸에 가려지지 않고, 화면 밖·카메라 뒤면 숨긴다(배치 = `OverheadHealthBarScreenPlacement.TryGetCanvasPosition`, 반영 시점 = `Canvas.willRenderCanvases`, 체력바와 같음). 정렬: CombatHUD(0)·플로팅 데미지(50)·시네마틱 덮개(450)·간파 암전/섬광(short.MaxValue-2/-1) 전부의 **아래**, 같은 캔버스 안에서는 나중에 생긴 형제라 보통 체력바 위. 칸(`GunnerBeamView`, Player_Gunner Variant, 전부 `[DataTableIgnore]`): `echoBombSprite`(임시 `3.Materials/~Others/Sprite/Circle` 의 `Circle_0`, 비우면 안 보임 — 런타임 텍스처 생성 없음), `echoBombSize`(캔버스 단위 = 세로 1080 기준 px, 기본 40 — 카메라 거리와 무관), `echoBombColor`(곱하기 색, 기본 보라 0.294/0/0.51). 옛 `echoBombPrefab`·`GunnerEchoBomb_Temp.prefab` 은 **미사용**(삭제 여부 미정). 스프라이트는 대상 몸 콜라이더 윗면 가운데(`GunnerEchoBombTargets.BodyPoint`) + 오프셋의 화면 위치를 매 프레임 따라간다(이동·넉백). Attached·Exploded 사건 위치도 같은 점(오프셋 포함), DebugTraitLab 표식은 오프셋 없는 몸 점.
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

- 카드(`TraitCardView`) = 아이콘 · 이름 · **분류 줄**(`Category`, 이름 바로 아래) · 설명. 배치는 그대로(명세 §2 '이름 / 짧은 분류 / 설명').
- 창 패널의 카드 줄 아래 **도움말 한 줄**(`Panel/Help`) = `UI.Trait.Help`("보스는 취약할 때만 밀치기와 기절이 적용됩니다.") — CC 공통 안내를 카드마다 반복하지 않는다.
- 프리팹 반영 = `Tools/Tutorial/특성 선택 UI — 분류·도움말 줄 추가 (배치 유지)` — 지금 프리팹에 빠진 줄만 더한다(글꼴은 옆 글자에서 복사, 이미 있으면 저장 안 함).
  전체 재생성 메뉴(`특성 선택 UI 프리팹 생성`)는 손으로 다듬은 배치를 덮어쓰므로 쓰지 않는다(글꼴 경로는 서울알림으로 고쳐 두었다).
- 설명 = 템플릿 → `TraitTooltip.FormatDescription`(`SkillTooltipFormatter`). 실패하면 `[오류: …]`(`UI.SkillTooltip.Error`) + 경고 — 스킬 툴팁과 같은 관례. 획득 아이콘 호버 툴팁도 같은 문구.

### 11.2 설명 템플릿 토큰

| 표기 | 값 |
|---|---|
| `{이름}` · `{이름:%}` · `{이름:0.0}` | `TraitTooltipValueSource` 가 첫 칸을 ① 등록부 별칭 → ② 정의 자신의 필드(`{effects[0].damageIncrease}` 경로 포함) → ③ 효과 목록 안 필드 이름 또는 ⑩ 스킬 수치 칸의 `name`(해석 전 값 — 감소율 0.25 그대로) 순으로 푼다. ③ 은 그 이름을 가진 효과가 **정확히 하나**여야 한다(둘 이상 = 오류 → 경로나 별칭) |
| `{dmg}` | 특성별 계산식(`TraitContentRegistry.Registrar.Damage` — 캐릭터 레인). 계산식이 없으면 **검증 오류**. 로컬 플레이어가 없으면 `UI.Trait.Card.DamageUnknown`("-") — 0 으로 보이지 않는다 |
| `<style=heal>` 등 | 스킬 툴팁과 같은 Style Sheet([data-table.md §9](data-table.md#9-프로그래머기획--툴팁-문구와-자리표시자)) |

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
