# PLAN — 인터럽트 슬로우 모션 (할 일 6)

> 상태: **승인 대기** · 브랜치 `feature/InterruptSlowMotion`(development `778bbb87` 기준) · 담당 은희
> grill: 2026-10-09 (사용자 확정). 구현 = claude-alt 에 커밋 단위 handoff, Play/MPPM 확인 = 사용자.

## 1. 목표
인터럽트가 **유효타가 될 입력**이 들어온 순간부터 세션 구성원 전원(호스트·모든 클라)의 **전역 게임 시간**을
느리게 하는 연출. 오너는 자기 슬로우 동안 카메라가 줌인된다. 이번 범위의 대상은 **허수아비만**.

## 2. 현재 이해 (조사 결과)
- 인터럽트 흐름: 오너 입력 → `RequestUseSkillRpc` → 서버 `PlayerSkillController` 승인 →
  `PlayerInterruptSkillBase.OnServerStart` → `HitDelay`(0.15s) 뒤 `ResolveHit`(서버) → 대상 `ReceiveAttack`.
  클라 예측 없음(networking.md).
- 성공 판정: 몬스터 4곳(23호·Gauntlet·Spinner·WallBot)은 `CombatStatsEvents.RaiseServerCounterSucceeded`,
  허수아비는 `TrainingDummyInterrupt.ServerTryInterrupt` → 자체 이벤트. 판정 규칙 `CounterWindow`(창당 1회 소비).
- `Time.timeScale` 대입은 현재 0곳. 히트스톱은 몬스터 animator.speed + `EffectManager.SetPlayRateForTarget`.
- 시간 도메인 혼재:
  - NGO 네트워크 시간·틱 = **unscaled**(실시간 30Hz). `NetworkTransform` 보간 = **scaled** deltaTime.
  - `NetworkClock.GameNow`(= ServerTime − 일시정지 누적, 실시간): 상태이상·Unit·무적·착지보호·거너 열·어쌔신·대시검증·세이프포인트.
  - `Time.time`(scaled): 스킬 쿨다운 ledger, 몬스터 FSM, 허수아비 사이클/CounterWindow.
  - unscaled: `CameraFeedback` 쉐이크 간격, `Health/ShieldVignetteHUD`, `BossTimerHUD`, 거너 열 점멸, 로딩·타이틀 UI, 사운드.
- `NetworkClock.Pause()/Resume()` 는 있으나 호출처가 아직 없다(솔로 Esc 일시정지 미배선).
- 컷신 판별: `PlayerEncounterLock.IsCinematicLocked`(NetworkVariable) / `BossEncounterDirector`.
- 카메라: `CameraTargetSwitcher` → 플레이어 vcam `CinemachineFollow`.

## 3. 확정 사항
| # | 결정 |
|---|---|
| D1 | **트리거 = 서버, 스킬 승인 시점(`OnServerStart`)**에 유효타 예측 검증 → 통과하면 슬로우 시작. 오너 화면은 스킬 애니와 같은 서버 신호로 시작(체감 즉시). |
| D2 | 허수아비 유효타 조건 = **거리 + 상태(인터럽트 가능)**. 앞뒤 없음(각도 검사 X). |
| D3 | 실제 `ResolveHit` 에서 빗나가면 **즉시 실패 복귀 단계**로 넘긴다. |
| D4 | **최상위 전역 시간 레이어가 `Time.timeScale` 을 정한다.** 애니메이터·VFX·Cinemachine·물리·`Time.time` 소비자는 자동으로 따라온다. |
| D5 | **단계 길이는 엔진 실시간(unscaled) 기준.** 단계: 진입 → 유지 → 복귀, 빗나감 시 실패 복귀. |
| D6 | 동시 발동에 추가 효과 없음. **슬로우 중 새 슬로우 = 마지막 것을 따른다**(현재 배율에서 진입부터 다시). |
| D7 | `NetworkClock.GameNow`·`GameLocalNow` 도 **같이 느려진다** — "슬로우로 덜 흐른 시간"을 일시정지 누적처럼 뺀다. 슬로우 상태 `{단계, 서버 시작 시각}` 에서 전 피어가 같은 식으로 계산 → 별도 동기화 없음. |
| D8 | 동기화 = 서버 → 전원 **CustomMessaging**(NetworkClock 선례), `{발동 번호, 프로필/단계 종류, 서버 시작 시각}`. 발동 번호로 last-wins·중복·순서 처리. 클라는 서버 시작 시각부터 거슬러 현재 배율 계산. |
| D9 | 전투용 unscaled → scaled 전환: `HealthVignetteHUD`, `ShieldVignetteHUD`, `CameraFeedback` 쉐이크 간격, `BossTimerHUD`, 거너 열 점멸. 로딩·타이틀·옵션·메뉴 UI 는 실시간 유지. |
| D10 | **오너 카메라 줌인**: 내가 일으킨 슬로우에서만, 유지 단계에서 최대(기본 현재 거리의 85%). 다른 플레이어의 슬로우가 덮어쓰면 내 카메라는 원거리로 줌아웃. 남의 슬로우에선 줌 없음. |
| D11 | 동작 X: 컷신·연출 중(`IsCinematicLocked`), 씬·화면 전환 중. 진행 중이던 슬로우는 즉시 1.0 복귀. |
| D12 | 솔로 일시정지: 현재 슬로우 상태를 저장하고 정지, 재개 시 이어서. (일시정지 배선 자체가 아직 없으므로 레이어 쪽 API 만 준비) |
| D13 | 사운드 피치 = **별도 브랜치·별도 커밋**(BroAudio/AudioManager, 은희 영역 밖). |
| D14 | 수치는 최종 `GameData.xlsx` — `[DataTableSheet]` 로 노출. |
| D15 | 몬스터(경석 영역)는 이번 범위 밖. 권한이 넘어오면 진행, 아니면 `Docs/tech/handoff-interrupt-slowmo.md` 로 전달. |

## 4. 기본 수치 (xlsx 이전 초안)
| 단계 | 배율 | 길이(실시간) | 곡선 |
|---|---|---|---|
| 진입 | 현재 → 0.3 | 0.05s | linear |
| 유지 | 0.3 | 0.35s | — |
| 복귀 | 0.3 → 1 | 0.25s | ease-out |
| 실패 복귀 | 현재 → 1 | 0.1s | ease-out |
| 카메라 줌 | 거리 ×0.85 | 진입~유지 도달, 복귀와 함께 원복 | ease |

## 5. 접근 — 커밋 단위 (handoff 1개 = 커밋 1개)
1. **S1 슬로우 산식(순수 C#)** — `SlowMotionTimeline`(가칭): 단계 평가 `ScaleAt(realElapsed)`, last-wins 재시작(현재 배율에서 진입),
   실패 복귀, 완료 판정, **덜 흐른 시간 적분 `LostTimeUntil(t)`**(GameNow 용, 해석적·결정적). EditMode 테스트.
2. **S2 전역 시간 레이어** — `GlobalTimeScale`(가칭, NetworkManager 프리팹, NetworkClock 옆):
   서버 발동 API(`ServerTrigger(profile)`, `ServerFail(triggerId)`), CustomMessaging 배포, `Time.timeScale` 적용(+ `fixedDeltaTime` 비율 유지 여부는 아래 R3),
   `NetworkClock.GameNow/GameLocalNow` 에 슬로우 누적 차감, 컷신·씬 전환 시 강제 1.0, 일시정지 저장/재개 API, 세션 종료 시 1.0 복구.
3. **S3 허수아비 연결** — `PlayerInterruptSkillBase` 서버 승인 시 유효타 예측 훅(대상 `IInterruptSlowTarget`(가칭)이 거리·상태로 응답),
   통과 시 트리거 / `ResolveHit` 에서 빗나가면 실패 복귀. `TrainingDummyInterrupt` 가 상태 응답 구현.
4. **S4 연출 scaled 전환 + 오너 카메라 줌** — D9 대상 전환, `CameraTargetSwitcher` 의 `CinemachineFollow` 거리 배율(오너·내 발동일 때만).
5. **S5 (별도 브랜치) 사운드 피치** — 전역 배율을 피치에 반영.
6. **S6 데이터·문서** — 수치 `[DataTableSheet]` 노출 + xlsx 열 추가, `Docs/tech/networking.md` Network Time Management 절 갱신
   (GameTime 은 일시정지 + 슬로우로 덜 흐른다), 필요 시 몬스터 handoff 문서.

## 6. 네트워크 권한
- 트리거·실패 판정 = 서버. 클라는 메시지를 받아 같은 산식으로 재현만 한다.
- `Time.timeScale` 은 피어마다 로컬 적용(서버 메시지 기준). 클라는 편도 지연만큼 늦게 진입하지만 GameNow 는 서버 시작 시각 기준으로 수렴.
- 카메라 줌은 오너 로컬 연출(메시지에 발동자 clientId 포함).

## 7. 리스크 / 열린 질문
- **R1 NetworkTransform 보간**: 틱은 실시간, 보간 delta 는 scaled → 슬로우 종료 시 원격 객체 스냅 가능. MPPM 확인 항목. 문제 시 보간 쪽 보정은 별도 작업.
- **R2 피어 간 진입 시차**: 클라는 RTT/2 늦게 느려진다. 화면상 허용 범위인지 Play 확인.
- **R3 FixedUpdate**: timeScale 을 낮추면 실시간당 Fixed 스텝 수가 줄어든다(`fixedDeltaTime` 고정 시 물리 움직임은 같이 느려짐 — 의도에 맞음). `fixedDeltaTime` 은 건드리지 않는 것을 기본으로 한다.
- **R4 GameNow 역행 금지**: 슬로우 누적 차감은 단조 증가해야 한다(산식 테스트로 고정).
- **R5 서버 판정 시간 연장**: `HitDelay` 가 scaled `Time.time` 이라 슬로우 중 판정이 실시간으로 늦어진다(의도: 전역 시간 슬로우).
- **R6 Unity 공용 상태**: `Time.timeScale` 은 정적 전역 — 세션 종료·씬 언로드·에디터 Play 종료 시 1.0 복구 보장.
- **R7 5번(인터럽트 강조)과 공유**: 성공/유효 신호·비네팅 HUD 를 같이 만진다. 5번은 이 레이어의 이벤트를 재사용.

## 8. 범위 밖
- 몬스터(23호·중간보스 3종) 연결 — D15.
- 클라 예측 트리거, 보간 보정.
- 솔로 Esc 일시정지 배선 자체(레이어 API 만 준비).

## 9. 완료 조건
- 허수아비가 인터럽트 가능 상태이고 사거리 안일 때 인터럽트 입력 → 호스트·모든 클라가 함께 느려졌다가 복귀.
- 사거리 밖·취약/idle 상태면 슬로우 없음. 예측은 통과했지만 빗나가면 즉시 실패 복귀.
- 슬로우 중 재발동 시 마지막 것을 따른다. 오너만 줌인, 덮어쓰이면 줌아웃.
- VFX·애니·UI·비네팅·쉐이크·쿨다운·상태이상 지속시간이 같이 느려진다. 로딩·메뉴 UI 는 실시간.
- 컷신·씬 전환 중엔 발동하지 않고, 진행 중이면 1.0 으로 복귀. 세션 종료 후 `Time.timeScale == 1`.

## 10. 검증
- EditMode: S1 산식(단계·last-wins·실패 복귀·누적 단조성) — 내가 직접 실행.
- 컴파일/콘솔: 각 커밋 후 Refresh + 콘솔 확인.
- MPPM(사용자): 호스트 + 클라 2 — 동시 진입·복귀, 원격 캐릭터 스냅 여부(R1), 진입 시차(R2), 오너 줌, 컷신 중 무발동.
