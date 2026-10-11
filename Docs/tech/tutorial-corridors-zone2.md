# 튜토리얼 특성 복도 1~4 · 존2 흐름 (할 일 15 · S8)

> 원본 시나리오: [../design/tutorial-scenario.md](../design/tutorial-scenario.md) §3.2·§3.3·§3.4·§3.6·§3.8·§4·§5(복도·존2).
> 디렉터 API: [tutorial-director.md](tutorial-director.md) §10 · 특성: [tutorial-traits.md](tutorial-traits.md) · 안내 UI: [tutorial-guide-ui.md](tutorial-guide-ui.md) · 존1 선례: [tutorial-zone1.md](tutorial-zone1.md).

## 파일
| 무엇 | 경로 |
|---|---|
| 복도 흐름(런타임, 모든 피어) | `Assets/1.Scripts/Map/Tutorial/Flow/TutorialCorridorFlow.cs` (+ 공용 `TutorialGuideFlowUtil.IsGuideEmpty`) |
| 복도 순수 규칙 | `Flow/TutorialCorridorModel.cs` — `TutorialCorridorRules`(게이트·체크·단계·창 표시 매핑, 진행 중 복도) · `TutorialCorridorRounds`(복도당 라운드 1번, 라운드→복도) · `TutorialCorridorProgress`(목표 장면) |
| 존2 흐름 | `Flow/TutorialZone2Flow.cs` |
| 존2 순수 규칙 | `Flow/TutorialZone2Model.cs` — `TutorialZone2Rules`(기상 4초) · `TutorialZone2Guide`(E 안내·넛지·강조 시계) · `TutorialZone2Goal`(목표 줄 인자) |
| 테스트 | `Map/Tutorial/Editor/TutorialCorridorModelTests.cs` · `TutorialZone2ModelTests.cs`(튜토리얼 러너) |
| 씬 | `4.MapScene` 루트 `TutorialTraitDirector`(NetworkObject + `TutorialTraitDirector`, pool = `TutorialTraitPool.asset`, hash 3847683974) · `TutorialTraitUI.prefab` 인스턴스 — 추가만. EventSystem 은 부트스트랩 `PersistentEventSystem`(DontDestroyOnLoad) |

붙는 방식은 존1 과 같다 — `TutorialDirector.StateChanged` 에서 `IsTutorialActive` 면 디렉터 GameObject 에 `AddComponent`(모든 피어).
**TutorialDirector.cs 는 고치지 않았다**(기존 `ShowSubtitleServer`·`ReportCheckServer`·`SetStepServer`·`WakeZoneServer` 만 사용).

## 복도 1~4
| 항목 | 피어 | 방법 |
|---|---|---|
| 라운드 시작 | 서버 | `CorridorMidReached(client, n)` 첫 번째 → `StartOfferRoundServer(display)`(복도1 Intro · 복도4 LastNotice · 2·3 None). **복도당 1번** — 두 번째 도착자·왕복·출구 열린 뒤·타이머 만료 뒤엔 안 연다 |
| 제시 대상 | 서버 | **접속 중인 전원에게 한 번에**(특성 디렉터 규칙 — 새 라운드는 이전 제시를 지운다). 아직 중간 지점에 안 온 사람도 그 순간 창이 뜬다(아래 판단 1) |
| 개인 체크 | 서버 | `ServerTraitChosen`(수동·자동) → `ReportCheckServer(TraitCorridorN)`. 0장 받은 사람·특성 디렉터 없음 = 시작 순간 바로 체크 |
| 출구 게이트 | 서버 | 디렉터 모델 그대로(전원 체크 / 첫 완료자 뒤 60초) — G2·G4·G7·G10 |
| 자동 획득 | 서버 | 출구가 **어떤 이유로든** 열리면 다음 Update 에 `AutoChooseAllPendingServer()`(창 닫힘·토스트·무적 해제). 그 복도 라운드가 지금 라운드일 때만 |
| 타이머 만료 | 서버 | `BossTimerManager.State` 가 Expired 로 바뀐 프레임에 `AutoChooseAllPendingServer()` — 강제 이동은 3초 경고 뒤라 연출 잠금보다 먼저 |
| G10 | 서버 | `ShowSubtitleServer(TUT_B_PAD_WARN)` 전원 · 단계 BossEnter |
| 단계 | 서버 | 입구 G3/G6/G9 → Corridor2/3/4(Corridor1 은 존1 흐름). 게이트 이벤트는 디렉터 Commit 안이라 다음 Update 에서 |
| 목표 | 로컬 | 창이 떠 있으면 `TUT_C_PICK`, 골랐으면 `TUT_C_WAIT` + 대기 줄(`TUT_WAIT_PARTY`, 지금 차례 게이트가 이 복도 출구일 때). 라운드 전엔 앞 존 흐름의 "복도로" 유지 |
| 출구 열림 | 로컬 | 이 흐름이 목표를 쓰던 클라만 `TUT_C_NEXT`(복도4 = `TUT_C4_GO_PAD`) 로 바꾸고 대기 줄을 지운다 |

## 존2
| 항목 | 피어 | 방법 |
|---|---|---|
| Z2-1 | 서버 | `AreaFirstEntered(Zone2)` → 단계 Zone2 → **서버 실제 시간 4초 뒤** `WakeZoneServer(Zone2)` |
| | 로컬 | 단계 Zone2 를 본 순간 자막 `TUT_Z2_INTRO` → `TUT_Z2_Q`(캐릭터 변형) · Q 칸 펄스 · 목표 `TUT_Z2_GOAL` |
| Q·E 체크 | 서버 | `PlayerSkillController.ServerSkillUsed` Main/Sub → `ReportCheckServer(SkillQ/SkillE)` — 존2 첫 진입 뒤·G3 전만(게이트 아님 D13) |
| Z2-2 | 로컬 | 내 SkillQ 체크(복제) 또는 기상 10초 뒤(로컬 시계 = 단계 Zone2 를 본 순간 + 4초) → `TUT_Z2_E`(변형) · Q 펄스 끄고 E 칸 펄스(E 를 쓸 때까지) |
| Z2-3 | 로컬 | 목표 줄 = "Q 사용 {q} · E 사용 {e} · 적 처치 {n}/{total}" — {q}{e} = □/■, n·total = 디렉터 카운터 Zone2Kills / 목표(장부 수) |
| Z2-4 | 로컬 | 기상 15초 시점에 안 쓴 키만 `TUT_Z2_NUDGE`({키} = Q / E / Q · E, {스킬명} = 툴팁 이름) — 1번 |
| Z2-5 | 로컬 | G3(존 장부가 전멸 때 연다 — 이 흐름은 열지 않는다) → 자막 `TUT_ZONE_CLEAR` · 목표 "복도로"(`TUT_Z1_GO`) · 강조 해제 → 손을 뗀다 |

## 늦은 클라
- 처음 그릴 때 안내 UI 에 목표가 하나도 없으면(= 앞 단계를 못 본 클라) `RebuildFromState`(자막 없음). 목표가 이미 있으면 `SetObjective` — Rebuild 는 대기 중 자막을 지우기 때문.
- 존2 늦은 클라는 시간 안내(E 문구·넛지)를 건너뛰고 E 를 아직 안 썼으면 E 칸만 강조.
- 복도는 디렉터 게이트·개인 체크·특성 디렉터 제시 행(복제)으로 다시 계산한다.

## 문구 (`TutorialGuideTexts.asset`)
- 채움: `TUT_Z2_INTRO`(4초) · `TUT_Z2_GOAL` · `TUT_Z2_NUDGE` · `TUT_Z2_Q`/`TUT_Z2_E`(기본 = 캐릭터 미식별 폴백) + `_PALADIN/_GUNNER/_ASSASSIN` · `TUT_B_PAD_WARN`(8초).
- 새 id(목표 줄, 시나리오 §5 에 없음): `TUT_C_PICK` · `TUT_C_WAIT` · `TUT_C_NEXT` · `TUT_C4_GO_PAD`. 상수는 `TutorialCorridorFlow`(TutorialGuideIds 는 다른 레인 소유).
- 존2 는 목표 줄과 자막 변형 이름이 겹치지 않는다(TUT_Z2_Q/E 는 자막 전용) → 분리한 id 없음.
- 창 안 문구(`TUT_C1_TRAIT_INTRO`·`TUT_C4_TRAIT_LAST`·획득 토스트)는 특성 쪽 `TutorialTraitTexts.asset` 그대로.

## 판단
1. **라운드 = 첫 도착 때 전원 제시.** 시나리오 §3.2 는 "그 플레이어가 중간 지점에 왔을 때"지만, 특성 디렉터의 `StartOfferRoundServer` 는 새 라운드가 이전 제시를 지워 플레이어별로 나눠 열 수 없다.
   위임 지시(라운드당 1번)와 특성 문서 §3 대로 했다. 늦은 사람은 앞 존 어디에 있든 창이 뜨지만 선택 중 무적(`TraitSelection`)이라 맞지 않는다. 플레이어별로 열려면 특성 모델에 "현재 라운드에 참가자 추가" API 가 필요하다.
2. 게이트용 개인 체크는 `ServerRoundCompleted` 가 아니라 `ServerTraitChosen` 에서 건다 — 대기 줄(n/total)이 한 명씩 오르고, 완전 사망자(디렉터 '전원'에서 빠짐)가 라운드 완료를 막아도 게이트는 열린다.
3. 출구가 열린 뒤 자동 획득은 열린 이유와 무관(전원 완료면 대상 0명).
4. 만료 뒤 복도4 중간 지점 도착은 라운드를 열지 않고 체크도 하지 않는다(강제 이동 중 G10·패드 경고 소음 방지).
5. 단계: 존 진입 단계(Zone3Practice·Zone4Lore)는 존3·존4 흐름 몫이라 이 레인은 Corridor2~4·Zone2·BossEnter 만 쓴다(앞으로만이라 겹쳐도 무해).

## Play 확인 (사람 — MPPM 호스트+클라)
- `[Tutorial] 복도 흐름 켜짐 (서버:True) — 특성 디렉터 있음` 로그(클라도 서버:False 로 1줄).
- 복도1 중간 지점 첫 도착 → 모든 피어에 창(상단 설명) · `복도1 특성 라운드 1 시작` 로그 · 왕복해도 다시 안 뜸 · 목표 "특성을 하나 고르세요".
- 한 명이 고르면 그 클라 목표 "동료를 기다립니다" + 대기 줄(1/2 · 카운트다운) → 전원 선택 시 G2 · 60초면 미선택자 창 닫힘 + 자동 획득 토스트.
- 존2 첫 진입 → 진입 자막 + 캐릭터별 Q 자막 · Q 칸 펄스 · 4초 뒤 `존2 기상 9마리` 로그 · Q 를 쓰면 E 자막 + E 칸 · 15초에 안 쓴 키 넛지 · 목표 줄 체크/처치 n/9.
- 9마리 처치 → `구역 확보` 자막 · G3 · 목표 "복도로".
- 복도2·3 창(설명 없음) · 복도4 창 아래 한 줄 · G10 순간 모든 피어 `TUT_B_PAD_WARN` · 목표 "엘리베이터 발판으로".
- 복도4 선택 중 타이머 만료(디렉터 `bossTimerLimitSeconds` 를 짧게) → 만료 즉시 창 닫힘·자동 획득 로그 → 3초 뒤 강제 이동, 배율 특성 거부 경고가 없어야 함.
- 늦게 붙은 클라(복도·존2 도중)도 목표가 현재 상태로 보인다.

## 남은 것
- 복도 낙오자 창 위치 문제(판단 1) — 특성 모델 API 추가 여부는 메인/특성 레인 결정.
- `TUT_B_PAD_COUNT`(패드 카운트다운 중) · 보스방 도착 안내 정리는 S12.
- 문구는 초안 — 기획 확정 필요(특히 `TUT_C_*` 새 id).
