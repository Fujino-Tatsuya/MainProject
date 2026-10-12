# 튜토리얼 특성 복도 1~4 · 존2 흐름 (할 일 15 · S8 → v8 V2·V3)

> 원본 시나리오: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §3.2·§3.3·§3.4·§3.6·§3.8·§4·§5(복도·존2) — **v8**(2026-10-12).
> v8 변경: **복도1 첫 진입 쿨타임 초기화**(아래 '복도1 쿨타임 초기화') · **존2 = 전멸만**(Q·E 체크·캐릭터별 Q/E 안내·슬롯 강조·넛지 제거).
> 디렉터 API: [tutorial-director.md](tutorial-director.md) §10 · 특성: [tutorial-traits.md](tutorial-traits.md) · 안내 UI: [tutorial-guide-ui.md](tutorial-guide-ui.md) · 존1 선례: [tutorial-zone1.md](tutorial-zone1.md).

## 파일
| 무엇 | 경로 |
|---|---|
| 복도 흐름(런타임, 모든 피어) | `Assets/1.Scripts/Map/Tutorial/Flow/TutorialCorridorFlow.cs` (+ 공용 `TutorialGuideFlowUtil.IsGuideEmpty`) |
| 복도 순수 규칙 | `Flow/TutorialCorridorModel.cs` — `TutorialCorridorRules`(게이트·체크·단계·창 표시 매핑, 진행 중 복도) · `TutorialCorridorRounds`(복도당 라운드 1번, 라운드→복도) · `TutorialCorridorProgress`(목표 장면) |
| 복도1 쿨 초기화(v8, 모든 피어) | `Flow/TutorialCorridor1CooldownFlow.cs` · 순수 `Flow/TutorialCooldownResetModel.cs`(`TutorialCooldownResetModel`·`TutorialCooldownResetScope`) · 실행 `Flow/TutorialCooldownReset.cs`(존3 과 공용) |
| 존2 흐름 | `Flow/TutorialZone2Flow.cs` |
| 존2 순수 규칙 | `Flow/TutorialZone2Model.cs` — `TutorialZone2Rules`(기상 4초) · `TutorialZone2Goal`(목표 줄 — 처치 n/total) |
| 테스트 | `Map/Tutorial/Editor/TutorialCorridorModelTests.cs` · `TutorialZone2ModelTests.cs` · `TutorialCooldownResetModelTests.cs`(튜토리얼 러너) |
| 씬 | `4.MapScene` 루트 `TutorialTraitDirector`(NetworkObject + `TutorialTraitDirector`, pool = `TutorialTraitPool.asset`, hash 3847683974) · `TutorialTraitUI.prefab` 인스턴스 — 추가만. EventSystem 은 부트스트랩 `PersistentEventSystem`(DontDestroyOnLoad) |

붙는 방식은 존1 과 같다 — `TutorialDirector.StateChanged` 에서 `IsTutorialActive` 면 디렉터 GameObject 에 `AddComponent`(모든 피어).
**TutorialDirector.cs 는 고치지 않았다**(기존 `ShowSubtitleServer`·`ReportCheckServer`·`SetStepServer`·`WakeZoneServer` 만 사용).

## 복도 1~4
| 항목 | 피어 | 방법 |
|---|---|---|
| 라운드 시작 | 서버 | `CorridorMidReached(client, n)` 첫 번째 → `StartOfferRoundServer({client}, display)`(복도1 Intro · 복도4 LastNotice · 2·3 None). **복도당 1번** — 출구 열린 뒤·타이머 만료 뒤엔 안 연다 |
| 제시 대상 | 서버 | **중간 지점에 온 사람에게만**(S12 판단 1). 이후 도착자는 `AddParticipantServer(client)` — 같은 라운드, 기존 제시·선택 보존, 이미 참가자면 무시. 앞 존에 있는 사람에겐 창이 안 뜬다 |
| 개인 체크 | 서버 | `ServerTraitChosen`(수동·자동) → `ReportCheckServer(TraitCorridorN)`. 0장 받은 사람·특성 디렉터 없음 = 도착 순간 바로 체크 |
| 출구 게이트 | 서버 | 디렉터 모델 그대로(전원 체크 / 첫 완료자 뒤 60초) — G2·G4·G7·G10 |
| 자동 획득 | 서버 | 출구가 **어떤 이유로든** 열리면 다음 Update 에 미도착 접속자를 `AddParticipantServer` 로 넣고 `AutoChooseAllPendingServer()`(창 닫힘·토스트·무적 해제). 그 복도 라운드가 지금 라운드일 때만 |
| 타이머 만료·보스방 도착 | 서버 | `BossTimerManager.State` 가 Expired 로 바뀐 프레임·`BossRoomArrived` 에 진행 중 복도 라운드를 같은 방식(미도착자 포함)으로 자동 획득 — 강제 이동은 3초 경고 뒤라 연출 잠금보다 먼저 |
| G10 | 서버 | `ShowSubtitleServer(TUT_B_PAD_WARN)` 전원 · 단계 BossEnter |
| 단계 | 서버 | 입구 G3/G6/G9 → Corridor2/3/4(Corridor1 은 존1 흐름). 게이트 이벤트는 디렉터 Commit 안이라 다음 Update 에서 |
| 목표 | 로컬 | 창이 떠 있으면 `TUT_C_PICK`, 골랐으면 `TUT_C_WAIT` + 대기 줄(`TUT_WAIT_PARTY`, 지금 차례 게이트가 이 복도 출구일 때). 라운드 전엔 앞 존 흐름의 "복도로" 유지 |
| 출구 열림 | 로컬 | 이 흐름이 목표를 쓰던 클라만 `TUT_C_NEXT`(복도4 = `TUT_C4_GO_PAD`) 로 바꾸고 대기 줄을 지운다 |

## 복도1 쿨타임 초기화 (v8 C1-0 · V2)
| 항목 | 피어 | 방법 |
|---|---|---|
| 조건 | 서버 | G1 열림 ∧ 그 플레이어의 서버 구간이 **복도1**(`GetPlayerAreaServer` — Alive 만, 입구 배리어 안쪽 0.5m 히스테리시스) ∧ 아직 안 받음. 0.1초 주기(실제 시간) |
| 초기화 | 서버 | 그 플레이어의 등록 스킬 중 Q·E·R·우클릭(**기본·대체 모두** — 어쌔신 변신 Q/E 등 따로 쿨을 가진 것 포함)을 `ReduceCooldownServer(skill, 남은 시간 + 0.05)` 로 0(쿨 장부 **공개 API 만**) + `PlayerDashController.ServerResetChargeToFull()`. 자원(분노·과열)·체력·패시브 주기는 건드리지 않는다 |
| 기록 | 서버 | `ReportCheckServer(client, TutorialCheck.Corridor1CooldownReset)`(복제 — 게이트 조건 아님). 이미 기록이 있으면(흐름이 다시 붙어도) 반복 없음 |
| 예약 | 서버 | 초기화 순간 그 사람이 **실행 중 스킬**(거너 Q 집중 포함)·**어쌔신 변신**·**어쌔신 E 강화 대기** 중이면 예약 → 그 행동이 모두 끝난 첫 순간 한 번 더 스킬 초기화(`TutorialCooldownResetModel.TickReservation`). 변신을 강제로 끝내거나 자원을 채우지 않는다 |
| 오너 | 로컬 | 내 복제 기록이 꺼짐→켜짐으로 바뀌는 것을 본 순간 `OwnerResetChargeToFull()`(대시 예측) + 자막 `TUT_C1_COOLDOWN_RESET`(그 사람 화면만). 처음 볼 때 이미 켜져 있으면(늦게 그린 클라) 아무것도 안 함 |
- 첫 진입한 1명 때문에 동료를 미리 초기화하지 않는다(각자 자기 첫 진입 때). 왕복·부활 재진입 반복 없음.
- 대시: 서버 장부(`PlayerDashValidationManager.ForceReset(…, int.MaxValue)` — 최대치로 잘림)와 오너 예측을 따로 채운다(추락 복구의 `ResetChargeToOne` 짝과 같은 방식). 복제 지연 동안 오너 예측이 잠깐 어긋날 수 있다.

## 존2 (v8 V3 — 전멸만)
| 항목 | 피어 | 방법 |
|---|---|---|
| Z2-1 | 서버 | `AreaFirstEntered(Zone2)` → 단계 Zone2 → **서버 실제 시간 4초 뒤** `WakeZoneServer(Zone2)` |
| | 로컬 | 단계 Zone2 를 본 순간 자막 `TUT_Z2_INTRO`(공통 — "적이 깨어납니다! 모든 적을 처치하세요.") · 목표 `TUT_Z2_GOAL` |
| Z2-2 | 로컬 | 목표 줄 = "적 처치 {n}/{total}" — n·total = 디렉터 카운터 Zone2Kills / 목표(장부 수) |
| Z2-5 | 로컬 | G3(존 장부가 전멸 때 연다 — 이 흐름은 열지 않는다) → 자막 `TUT_ZONE_CLEAR` · 목표 "복도로"(`TUT_Z1_GO`) → 손을 뗀다 |
- **없는 것(v8 §2.4·§3.3·§7.2-7)**: Q·E 사용 체크(`ServerSkillUsed` 구독 제거)·캐릭터별 `TUT_Z2_Q_*`/`TUT_Z2_E_*` 자막·Q/E 칸 강조·미사용 넛지 `TUT_Z2_NUDGE`. 스킬 교육은 존1 에서 끝났다.

## 늦은 클라
- 처음 그릴 때 안내 UI 에 목표가 하나도 없으면(= 앞 단계를 못 본 클라) `RebuildFromState`(자막 없음). 목표가 이미 있으면 `SetObjective` — Rebuild 는 대기 중 자막을 지우기 때문.
- 존2 늦은 클라는 처치 목표만.
- 복도는 디렉터 게이트·개인 체크·특성 디렉터 제시 행(복제)으로 다시 계산한다.

## 문구 (`TutorialGuideTexts.asset`)
- 채움: `TUT_Z2_INTRO`(4초) · `TUT_Z2_GOAL` · `TUT_C1_COOLDOWN_RESET`(5초) · `TUT_B_PAD_WARN`(8초). v8 문구는 메뉴 `Tools/UI/튜토리얼 안내 문구 v8 적용`.
- 더 이상 호출하지 않는 행(SO 에 남김): `TUT_Z2_NUDGE` · `TUT_Z2_Q`/`TUT_Z2_E` + `_PALADIN/_GUNNER/_ASSASSIN`. 상수는 `TutorialGuideIds` 에서 지웠다.
- 새 id(목표 줄, 시나리오 §5 에 없음): `TUT_C_PICK` · `TUT_C_WAIT` · `TUT_C_NEXT` · `TUT_C4_GO_PAD`. 상수는 `TutorialCorridorFlow`(TutorialGuideIds 는 다른 레인 소유).
- 창 안 문구(`TUT_C1_TRAIT_INTRO`·`TUT_C4_TRAIT_LAST`·획득 토스트)는 특성 쪽 `TutorialTraitTexts.asset` 그대로.

## 판단
1. **(S12 결정) 창 = 본인이 중간 지점에 왔을 때**(시나리오 §3.2). 라운드는 복도 첫 도착 때 그 사람만으로 열고, 이후 도착자는 특성 디렉터 `AddParticipantServer` 로 같은 라운드에 참가한다.
   교착 60초·타이머 만료·보스방 도착의 자동 획득은 미도착자도 참가자로 넣은 뒤 처리한다(창을 못 본 채 획득하면 창 판정 모델이 토스트만 띄운다).
2. 게이트용 개인 체크는 `ServerRoundCompleted` 가 아니라 `ServerTraitChosen` 에서 건다 — 대기 줄(n/total)이 한 명씩 오르고, 완전 사망자(디렉터 '전원'에서 빠짐)가 라운드 완료를 막아도 게이트는 열린다.
3. 출구가 열린 뒤 자동 획득은 열린 이유와 무관(전원 완료면 대상 0명).
4. 만료 뒤 복도4 중간 지점 도착은 라운드를 열지 않고 체크도 하지 않는다(강제 이동 중 G10·패드 경고 소음 방지).
5. 단계: 존 진입 단계(Zone3Practice·Zone4Lore)는 존3·존4 흐름 몫이라 이 레인은 Corridor2~4·Zone2·BossEnter 만 쓴다(앞으로만이라 겹쳐도 무해).

## Play 확인 (사람 — MPPM 호스트+클라)
- `[Tutorial] 복도 흐름 켜짐 (서버:True) — 특성 디렉터 있음` 로그(클라도 서버:False 로 1줄).
- 복도1 중간 지점 첫 도착 → 모든 피어에 창(상단 설명) · `복도1 특성 라운드 1 시작` 로그 · 왕복해도 다시 안 뜸 · 목표 "특성을 하나 고르세요".
- 한 명이 고르면 그 클라 목표 "동료를 기다립니다" + 대기 줄(1/2 · 카운트다운) → 전원 선택 시 G2 · 60초면 미선택자 창 닫힘 + 자동 획득 토스트.
- 복도1 입구 첫 진입 → 쿨 0 · 대시 가득 · `TUT_C1_COOLDOWN_RESET` · 로그 `복도1 첫 진입 쿨타임 초기화 — client n …`(어쌔신 변신 중이면 `예약` → 변신 끝에 `예약 실행`).
- 존2 첫 진입 → 진입 자막(공통) · 4초 뒤 `존2 기상 9마리` 로그 · 목표 줄 "적 처치 n/9" 만(캐릭터별 Q/E 자막·칸 펄스·넛지 없음).
- 9마리 처치 → `구역 확보` 자막 · G3 · 목표 "복도로".
- 복도2·3 창(설명 없음) · 복도4 창 아래 한 줄 · G10 순간 모든 피어 `TUT_B_PAD_WARN` · 목표 "엘리베이터 발판으로".
- 복도4 선택 중 타이머 만료(디렉터 `bossTimerLimitSeconds` 를 짧게) → 만료 즉시 창 닫힘·자동 획득 로그 → 3초 뒤 강제 이동, 배율 특성 거부 경고가 없어야 함.
- 늦게 붙은 클라(복도·존2 도중)도 목표가 현재 상태로 보인다.

## 남은 것
- (S12 해결) 복도 낙오자 창 위치(판단 1) · 출구 목표(`TUT_C_NEXT`)는 다음 존 단계가 이미 보이면 쓰지 않는다(`TutorialCorridorRules.ShouldShowExitObjective`) · 복도4 에서 존4 "보스방으로"가 복도 목표를 덮지 않는다 · 단계 Finished 뒤 복도 안내 중지.
- 문구는 초안 — 기획 확정 필요(특히 `TUT_C_*` 새 id).
