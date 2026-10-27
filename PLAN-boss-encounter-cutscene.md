# PLAN — 보스 등장 연출 시퀀스 및 다인 협동 스킵 시스템

> 상태: 초안 작성 완료 (2026-10-22). Claude 작업 지시 및 구현용 계획서.  
> 대상: `Assets/1.Scripts/Map/BossEncounterDirector.cs`, `BossTeleportManager.cs`, `TwentyThreeBoss.cs`, `BossWells.cs`, `CameraTargetSwitcher.cs` 및 관련 UI/VFX.  
> 톤앤매너 레퍼런스: **Shape of Dreams**, **Ravenswatch** (어두운 외곽 비네팅, 시네마틱 레터박스, 위압적인 보스 독백/자막).

---

## ★ 결정·정정 (2026-10-23 · 경석 결정 + Claude·Codex 아이디어 회의) — 아래 본문보다 이 절이 우선한다

### 경석 결정
| 항목 | 결정 |
|---|---|
| 워크인 | **엘리베이터 보행을 그대로 재사용**(`BossTeleportManager.ElevatorWalkOwnerClientRpc` → 오너 `OwnerWalk` 가 경로·`ServerTime` 시작 시각으로 걷는다 — 오너 권위 이동과 맞는 유일한 기존 경로). 대열 = **가로 일자** 또는 **3인 삼각형 꼭짓점**으로 **동시에** 걸어오다가 보스가 나온다(한 줄로 순서대로 X → 엘리베이터 `WalkInterval` 시차 = 0) |
| 카메라 | 새 카메라 시스템을 만들지 않는다 — **시네머신 컴포넌트로 처리 가능한지 먼저 본다**(아래 조사 결과) |
| 웰즈 지시 | **`Boss_welz_throwing` 클립을 쓴다**(경석 10-23 확정 — 10-23 앞선 "다른 클립" 기재는 오독, ⚠️ 정정). 부수 효과 실측: 클립 이벤트 `ThrowBombEvent` 는 손폭탄을 숨기고 서버에서 `ThrowRequested` 를 내지만 **구독자가 없다**(폭탄 투척 제거 2026-09-16, `BossWells.cs:146-157`) → 투사체는 안 나간다. 남는 건 `PlayState(Throw)` 가 켜는 **손에 든 폭탄 표시**(`ShowHeldBomb(true)`, `:120-122`)뿐 — 연출용은 트리거만 당기고 폭탄 표시는 켜지 않는 진입점(예: `PlayCommandGesture()`)을 따로 둔다. 웰즈는 네트워크가 없으니 재생 신호는 23호 ClientRpc 로 보낸다 |
| 스킵 키 | **Space 유지**. **1초 동안 눌러야 스킵 동의**. Shape of Dreams 처럼 **버튼 아이콘의 외곽선(outline)이 채워지는** 연출로 동의 진행·완료가 분명히 보이게 |
| 재도전 짧은 판 | **안 만든다** — 재도전도 같은 연출(스킵 가능) |
| 레터박스·비네팅 강도 | 로스트아크·Shape of Dreams·Ravenswatch 보스 등장 영상을 분석해 **가장 무난한 값**으로 시작 → Play 로 조금씩 조정 (분석 결과 = 아래 절: 레터박스 상하 각 4% · 비네팅 0.606 → 0.70) |
| `BossLandingPoint` +0.8m | **기본안에서 뺀다**(아래 정정 6) — 구도는 카메라로 잡는다 |

### 레터박스·비네팅 레퍼런스 분석 (2026-10-23 · YouTube 프레임 실측)
방법: 브라우저에서 영상 프레임을 캔버스(320×180)로 떠서 ① 위·아래 **완전 검정 행**(행 최대 휘도 < 12) 높이 = 레터박스, ② 네 모서리(가로 12%·세로 15%) 평균 휘도 ÷ 중앙(30%) 평균 휘도 = 외곽 어두움 비율(낮을수록 비네팅 강함, 장면 밝기에 따라 흔들리므로 **중앙값**만 본다).

| 레퍼런스(영상) | 샘플 | 레터박스(상하 각) | 외곽/중앙 비율 |
|---|---|---|---|
| Ravenswatch — All Bosses(24:32, 4초 간격) | 365 | **보스 등장 구간에만 2.8~3.7%**(11개 구간, 행 단위로 진짜 검정 띠 확인) | 평소 0.46 → 등장 구간 0.21~0.51(중앙값 ≈ 0.35) |
| Shape of Dreams — All Bosses + Ending(13:30, 3초) | 268 | **없음**(아래쪽만 3~5% = HUD 하단 바로 보임) | 0.75 |
| 로스트아크 — 군단장 컷신 모음(35분, 6초) | 348 | **없음**(이 캡처에서는 16:9 꽉 참) | 0.81 |

- 우리 맵에는 **이미 비네팅이 켜져 있다**: `4.MapScene/Global Volume Profile.asset` Vignette `intensity 0.606` · `smoothness 0.421`(Ravenswatch 평소 수준). 연출 비네팅은 0 에서 새로 까는 게 아니라 **이 값에서 조금 올린다**.
- **시작값(가장 무난한 쪽)**: 레터박스 **상하 각 4%**(유일하게 띠를 쓰는 Ravenswatch 값 — 나머지 둘은 0 이라 이보다 두껍게 갈 근거가 없다), 페이드 0.4초 · 비네팅 **0.606 → 0.70**(smoothness 그대로, 등장 구간 Ravenswatch 처럼 외곽을 한 단계 더 누르는 정도) → 이후 Play 로 조금씩 조정.
- 한계: 유튜브 캡처(480~720p)라 업로더가 자른 화면일 수 있다(로스트아크 실제 게임 컷신의 띠 유무는 이 영상으로 확정 못 함). 비네팅 비율은 장면 밝기 영향이 커서 "강/약" 비교용이다.

- ⚠️ **정정(10-23 구현 중)**: 맵 볼륨의 0.606 은 실제 화면에 안 나온다 — `CameraFeedback` 이 HP 비네팅용 런타임 볼륨(우선순위 100, intensity 항상 override, 만피 0)으로 덮어쓴다. 그래서 연출 비네팅은 **우선순위 110 런타임 볼륨, intensity 0.35 · smoothness 0.42** 를 시작값으로 했다(평소 0 → 연출 0.35, HP 비네팅 최대 0.32 와 비슷한 세기).

### 구현 현황 (2026-10-23 · ✅ 승인 → 구현 · 컴파일 0 · 전투 EditMode 291/0 · ⏳ 경석 Play·MPPM)
| 부분 | 어디 | 비고 |
|---|---|---|
| 대열·스킵 규칙(순수) | `Map/BossIntro/BossIntroRules.cs` + 테스트 `Map/Editor/BossIntroRulesTests.cs`(10건, 전투 묶음 등록) | 1인 중앙 · 2인 가로 일자 · 3인 삼각형(끄면 일자) · 표 유지·이탈자 제거·만장일치 |
| 서버 흐름 | `BossEncounterDirector` | Preparing(워크인 — 보스는 걷기 끝에 스폰) → Descending → Impact(밀림·흔들림) → Dialogue(웰즈 지시) → `BeginCombatServer`. 스킵 = 보스 없으면 스폰 후 같은 함수. `introEnabled` 끄면 예전 흐름 |
| 도착 자리 | `BossTeleportManager.GetArrivalPose` 다음 | 디렉터 `TryGetIntroStartPose` = 워크인 출발 대열(높이는 원래 도착 지점) |
| 오너 이동 | 디렉터 `IntroWalkOwnerClientRpc`/`IntroSkidOwnerClientRpc` + `FixedUpdate` | 엘리베이터 보행과 같은 Motor 채널, **회차 번호**로 늦은 지시 버림. 연출이 끝나면 그 자리에서 멈춘다(스킵 시 최종 위치로 순간이동하지 않음) |
| 밀림 리액션 | 같은 곳 | 캐릭터 Animator 에서 있는 상태 하나를 CrossFade(`SubSkillGuard`/`Gunner_E_Backstep`/`Dodge`) → 0.35초 뒤 `Idle_Combat`/`Idle`. `Player.CharacterAnimator` 접근자 추가 |
| 웰즈 지시 | `IBossEntranceAnimation.PlayEntranceCommandServer` → `TwentyThreeBoss` ClientRpc → `BossWells.PlayCommandGesture` | `Boss_welz_throwing`, 손폭탄 표시 없음 |
| 화면 | `Map/BossIntro/BossIntroPresentation.cs`(디렉터가 런타임 부착, 모든 피어) | 런타임 vcam(`CameraTargetSwitcher.EnterCinematic/ExitCinematic` 신설, 우선순위 200) · 레터박스 4% · 비네팅 볼륨 · 독백(`BOSS_INTRO_WellsCommand`) · 스킵 위젯(Space 1초, 외곽선 Radial 채움, n/m) |
| 흔들림 | `CameraFeedback.PlayImpactShake` 신설 | 설정창 화면 흔들림 배율 적용 |
| HP바·튜토리얼 팁 | `BossHealthHUD`(워크인~착지 숨김) · `TutorialZone4Flow`(연출 중 정면 팁 보류) | |
| 문구 | `TutorialGuideTexts.asset` 행 2개 + `TutorialGuideUI.TryResolveText/SubtitleFont` | 독백 "23호. 분쇄해라." · "길게 눌러 건너뛰기" |
| 씬·프리팹 | **무수정** | 연출 카메라 구도·타이밍은 전부 디렉터 인스펙터 칸(`등장 시네마틱 — …` 헤더) |

**경석 Play(10-23, 1인)**: 웰즈 독백 자막은 **없어도 된다** → 디렉터 `showMonologue` = false(기본, 코드·문구 행은 남겨 둠 — 켜면 다시 나온다). 나머지는 "나쁘지 않다". 스킵 안내 문구("길게 눌러 건너뛰기")는 유지.

**아트 피드백 반영(10-23)**:
- 연출 카메라 구도 = 경석이 준 bossroom 씬 뷰 스크린샷 **실측**(임시 에디터 로그로 씬 뷰 카메라 읽음 — 측정 도구는 삭제): pos (8.79, 16.44, −9.76) · euler (47.7, 314.2, 0) · FOV 60, 착지점 (0, 0.725, 0) → 디렉터 기본값 `cameraPitch 47.7` · `cameraDistance 21.2` · `cameraFocusSouth −1.2`(착지점보다 화면 위쪽) · `cameraFocusHeight 0` · `cameraFov 60`. 요는 게임 카메라(−45°)를 따른다(씬 뷰 314.2° 와 0.8° 차).
- 보스 HP바 = **연출이 끝나자마자(전투 시작)** 뜨고, **1/4 에서 0.8초 동안 최대까지 빠르게 차오른다**(감속 곡선, 그동안 잔상 바도 같이). `BossHealthHUD` 인스펙터 `introFillEnabled`·`introFillStart 0.25`·`introFillSeconds 0.8` · 모델 `UI/Combat/BossHealthIntroFill.cs` + 테스트 4건. 재도전(새 보스)마다 다시 한다. 전투 EditMode 295/0.

**경석 Play 2차(10-23)**:
- "카메라 구도가 안 바뀌었다" — 원인: 연출 카메라가 요를 **렌더 카메라 transform** 에서 읽었는데 연출 중엔 그 transform 을 연출 카메라 자신이 움직여 게임 카메라(−45°)가 아닌 0° 쪽으로 잡혔다(방 벽이 화면과 나란히 보임). → `CameraTargetSwitcher.TryGetGameplayViewRotation`(팔로우 오프셋 고정 회전)으로 바꿈. 대열·보스 방향도 같은 기준(`BossIntroPresentation.GameplayScreenSouth`).
- 스킵 위젯이 HUD 와 겹침 → **연출 중 HUD 전부 끔, 스킵 위젯만**(경석 결정). Screen Space 루트 캔버스를 `Canvas.enabled=false` 로 끄고 연출이 끝나는 순간(전투·스킵·중단) 다시 켠다. 남김 = 연출 오버레이 · ESC 게임 메뉴 · 접속 오버레이 · 로딩 화면. 월드 공간 캔버스는 그대로. 보스방 도착 암전은 OnGUI 라 무관.

**스킵 버튼 아트(경석 10-23)**: 배경 = `button_pill_neutral`(9-슬라이스, `pixelsPerUnitMultiplier 2` 로 72px 높이에 반원 끝) · 글꼴 = SeoulAlrim SDF(동적) · 글자 크림 `#F5EEDB`, 키 = **HUD 키 안내(`keyguide.png`)와 같은 "Space" 키캡**(밝은 회색 몸통·아랫단 어둡게·진한 테두리·진한 굵은 글씨, 런타임 생성 — 경석 10-23: 글자만으론 누를 키로 안 읽힘. 다른 게임 조사 = 구석 + 키 글리프 + 둘레 채움), 홀드 외곽선 금색 → 완료 초록, 동의 수는 알약 밖이라 검은 외곽선. 문구 = Strings 키 **`UI.BossIntro.Skip`**(ko `건너뛰기` / en `Skip`, `UiStrings.BossIntroSkip`) — 시트 행은 다음 Export 때 생긴다(그 전엔 ko 원문, en 초안은 `localization-en-draft.md`). 디렉터 프리팹에 `introFont`·`skipButtonSprite` 참조 추가. 옛 안내 문구 행 `BOSS_INTRO_SkipHint` 삭제.

**지시 구간 타이밍(경석 Play 10-23 — "좀 빠르다")**: 23호 착지 → 웰즈 `Boss_welz_throwing` **끝까지**(길이는 실행 중 웰즈 애니메이터에서 읽음, 144프레임 — 못 읽으면 `commandSeconds` 2.4) → **`commandHoldSeconds` 0.8초 대기** → 카메라 복귀 블렌드 **`cameraReturnSeconds` 1.0초** → 카메라가 플레이어에 다 도착한 순간 전투(HUD·HP바 차오름). 서버가 복귀 시작 시각(`cameraReturnAt`)을 복제해 클라도 같은 순간에 블렌드한다. `IBossEntranceAnimation.EntranceCommandSeconds` 추가.

**스킵 = 카메라 복귀는 보여 준다(경석 10-23 — 스킵하면 바로 시작돼 툭 끊긴다, 다른 게임도 스킵 뒤 카메라 전환은 자연스럽게)**: 전원 동의 → 보스 착지점에 세움(등장 포즈 해제) · 웰즈 지시 건너뜀 · 회차를 올려 오너 걷기·밀림 정지 → **지시 구간의 복귀 단계로 바로**(`cameraReturnAt` = 지금) → 블렌드 1.0초 → 도착하면 전투. 복귀가 시작되면 스킵 버튼은 걷히고 표도 더 받지 않는다. ⚠️ 정정: 앞선 구현(즉시 `BeginCombatServer`)은 폐기.

**경석 Play 3차(10-23) 반영**:
- HP바 = **Shape of Dreams 식**: 1/2 에서 출발, 잔상 바를 흰색으로 덮어 **흰 구간이 0.45초 만에 앞서 끝까지**, 빨강이 1초 동안 뒤따라 차고, 다 차면 흰색이 0.35초 사그라든다(`BossHealthHUD` `introFillStart 0.5 · introLeadSeconds · introFillSeconds · introFadeSeconds · introLeadColor`, 모델 `BossHealthIntroFill.Frame`, 테스트 5건).
- 착지 연출 프로토타입(정식 VFX 는 김민경 몫 — 코드 파티클로 느낌 확인용, **풍압 없음**): ① 먼지 흩날림(`M_FX_Alpha_Smoke`, 착지점 고리에서 바닥 따라 퍼지다 떠다님) ② 23호 몸 전기 **1초**(돌진 팔다리 전기 펄스 재사용, `IBossEntranceAnimation.PlayEntranceImpactServer`, 디렉터 `impactElectricSeconds`) ③ 전기 잔여물(`M_Pylon_Spark` 작은 푸른 불티가 주위를 떠다니다 깜빡이며 사라짐). 머티리얼은 디렉터 프리팹 `introDustMaterial`·`introSparkMaterial`.
- 🔴 버그 수정: 페이즈 차징으로 **중앙에 점프할 때 이륙 클립(Leap) 대신 차징 자세(팔 뻗음)로 떠오르던 것** — `StartAttack` 이 같은 프레임에 `Charging` 을 먼저 틀어 이륙 크로스페이드가 그 자세에서 출발했고, 배속도 Charging 길이로 계산됐다. 차징 시퀀스는 시작 때 상태를 틀지 않게(실제 차징 시작이 다시 튼다) + 이륙 배속은 전환 중이면 다음 상태 길이.

**경석 Play 4차(10-23)**: 먼지 과함 → 디렉터 프리팹 `introDustMaterial` 비움(코드는 남김 — 넣으면 다시 나온다) · 몸 전기·전기 잔여물 1 → **1.5초**(`impactElectricSeconds`, 잔여물 방출 시간도 같은 값) · HP바 차오름이 안 보였다 → 바가 뜬 뒤 **1/2 에서 0.6초 머문 뒤** 차오른다(`introHoldSeconds`, 전투 UI 가 켜지는 걸 눈이 따라온 다음) + 시작 로그 `[BossHealthHUD] 등장 차오름 시작`(안 보이면 이 줄이 Logs/PlaySessions 에 찍혔는지부터 본다). 전투 EditMode 297/0.

**경석 Play 5차(10-23)**: 흰 구간이 안 보였다 — 원인 = `HP_Fill`·`HP_DelayedFill` 이 **같은 빨간 그라데이션 스프라이트**라 잔상 바 `Image.color` 를 흰색으로(곱하기) 바꿔도 빨강이 나왔다(로그상 차오름은 시작됐음). → 바 모양 Mask(같은 스프라이트) 안의 **순수 흰 막대**를 HP_Fill 바로 뒤에 런타임 생성, 차오르는 동안 잔상 바는 빨강 뒤로 숨김. 몸 전기 1.5 → **2.5초**, 전기 잔여물 범위·양 **2배**(반경 6.4m · 초당 44 · 첫 분출 36). 전투 EditMode 297/0.

**경석 Play 6차(10-23 — Shape of Dreams 레퍼런스 맞춤)**: 흰 막대가 밋밋했다 → **흰 구간 자체가 발광**(바 안 고른 흰 띠 + 흰 띠에서 14px 번지는 9-슬라이스 소프트 빛, `introGlowSpread`) — 번짐도 **바 모양 Mask 안**(정정: 마스크 없이 두니 사각형이 바 밖으로 삐져나왔다). ⚠️ 정정(경석 10-23): 처음엔 오른쪽 끝 반짝임·머리 글로우를 넣었는데 "끝에 반짝이가 아니라 흰 부분 자체가 빛나야 한다"로 뺐다 · HP 색 빨강 한 가지 → `HP_Fill` 을 Mask 로 쓰고 그 안에 **빨강→주황 그라데이션**(`hpGradient`, 위 밝고 아래 어두운 입체감, `hpGradientEnabled` 끄면 원래 스프라이트) · 반에서 끊겼다 차오르던 것 → **`introHoldSeconds` 삭제**, 빨강 0.45·흰 구간 +0.08 앞에서 첫 프레임부터 바로 달린다(흰 0.55초·빨강 1.15초·사그라듦 0.4초). 프리팹 무수정, 전기·먼지 무변경. 전투 EditMode 298/0. ⏳ 경석 Play.
  - 같은 판에 함께 확인: 보스 B 30% **북진 압박** 탑 전부 파괴 → 무력화 자세를 피격 리액션(뒤 가만히 서 있음) 대신 **그로기 클립(`GroggyStart` = `Boss_23_.groggy`)을 틀고 끝 직전(0.94, exitTime 0.95 앞)에서 쓰러진 자세로 멈춘 채** 간파를 기다린다(정정: 처음엔 끝까지 재생돼 GroggyEnd 로 다시 일어섰다). 멈춤 = 카운터 선딜과 공용 자세 홀드 래치(animator.speed 0) — 간파 성공·3초 경과·기믹 종료에서 `ReleasePushNorthDownPose`, 사망·중단은 기존 `AbortAttackChain` 이 푼다. 간파 창·3초·밀림 규칙은 그대로(`TwentyThreeBoss.RaidRelay.cs`).

**⚠️ 알려진 문제 — 워크인 중 한 명이 안 보임(경석 MPPM 3인 10-23, 모든 창에서 안 보였고 전투 시작 뒤 다시 보임)**:
- 처음엔 "캐릭터가 모두 같을 때"로 보였으나 호스트 로그로 확인한 차이는 **판 도중 재합류**다. 그 판에서 두 클라 모두 `ProtocolTimeout` 으로 끊겼다가 재합류(client 1→4, 2→3)했고, 둘 다 맵 첫 스폰 10m 위에 다시 생긴 뒤 **약 1초 만에 보스 엘리베이터가 시작**됐다. 안 보인 쪽 = client 4(슬롯 2, 왼쪽 뒤). 각자 다른 캐릭터로 한 다음 판(끊김 없음)은 정상.
- 서버 기준 슬롯·도착 ACK·엘리베이터 숨김 해제·생명 상태(사망·영혼 없음)·위치(대열 좌표 그대로)는 모두 정상 — **그 캐릭터의 렌더링만 안 됐다.** 정적 확인으로 배제: 착지 보호 깜빡임(안 켜짐)·시각 보정 스무더(1m 초과 즉시)·오클루전 데이터(맵에 없음)·색 구분(주석 처리)·HUD 끄기(캔버스만).
- 원인 미확정 — 클론 로그는 Play 마다 새로 써져 그 판 클라 기록이 없다. 재현 = MPPM 3인에서 **엘리베이터 직전 클라 하나를 끊었다 재합류**시킨 뒤 보스방 도착. 재합류(C14)는 lobby-demo-flow.md §7 에 "사람이 MPPM 으로 아직 확인하지 않았다"로 남아 있는 영역.
- 덤 정리: 워크인 시작 때 대시 중이 아니어도 `EndDash` 를 불러 클라 로그에 "EndDash 무효" 가 반복되던 것 → 대시 중일 때만.

**안 한 것 / 다음**: 23호 포효(클립·SFX 미지정) · 독백 타이핑/글꼴 스타일 · 캐릭터별 리액션 상태명 Play 확인 · 데이터 테이블 노출 안 함(연출 정렬 값).
**Play 확인 항목**: ① 1·2·3인 대열·동시 출발·정지 자리 ② 원격 클라 화면(워크인·밀림·리액션 애니 복제 — NetworkAnimator) ③ 스킵: 1인 1초 · 3인 전원(한 명 먼저 채우고 손 떼도 유지) · 각 단계(워크인/강하/착지/지시)에서 스킵해도 전투가 정상 시작 ④ 스킵 직후 Space 대시 오발 없음 ⑤ 카메라 컷 진입·블렌드 복귀·낙하 카메라와 충돌 없음 ⑥ 레터박스·비네팅 세기 조정 ⑦ HP바가 지시 구간에 뜨는지 ⑧ 튜토리얼 정면 팁이 전투 시작 뒤에 뜨는지 ⑨ 재도전(전멸 리스폰) 때 연출 다시 재생.

### 카메라 조사 (2026-10-23 · 코드 실측)
- 이미 **Cinemachine 3**(`Unity.Cinemachine`): `MainCamera.prefab` 에 `CinemachineBrain`(DefaultBlend = EaseInOut 2초, 원근 FOV 26), `CameraTargetSwitcher` 가 플레이어 vcam·낙하 vcam 두 대를 **Priority(100/0)** 로 전환한다(`CameraTargetSwitcher.cs:21-22, 110-124`).
- ⇒ **연출 카메라 = 보스방 프리팹에 `CinemachineCamera` 하나 추가**(위치·회전·렌즈를 씬에서 저작)하고 Priority 를 100 위로 올렸다 내리면 브레인이 알아서 블렌드한다. 보스 추적·로우 앵글이 필요하면 같은 vcam 에 `CinemachineFollow`/`RotationComposer` 컴포넌트, 착지 흔들림은 기존 `CameraFeedback`(Impulse) 를 쓴다.
- 코드가 필요한 곳은 **한 군데**: 스위처가 전환마다 `DefaultBlend` 시간을 덮어쓰므로(`ApplyBlendDuration`, `:619-633`) 연출 진입·복귀 블렌드 시간을 넘기는 작은 API(예: `EnterCinematic(cam, blendIn)` / `ExitCinematic(blendOut)`)를 스위처에 둔다. `SetTemporaryFollow` 는 **컷 전환**이라 쓰지 않는다(`:293-330`).
- 각 피어가 자기 브레인·자기 vcam 을 갖고 있으므로 네트워크는 "언제 켜고 끄나"(연출 회차·시작 `ServerTime`)만 맞추면 된다.

### PLAN 본문 정정 (Claude 코드 대조 + Codex 교차검증)
1. 등장 흐름은 **이미 있다** — `BossEncounterDirector`(Preparing → Descending 1.2초 → Impact 0.9초 → Combat, `Dialogue` 는 enum 에만 있고 미진입). 새 비트는 기존 단계에 얹는다(Preparing=워크인, Descending=강하, Impact=충격·정비, Dialogue=지시·복귀).
2. 보스방 안 워크인은 새로 만든다(지금은 엘리베이터 암전 → `PlayerArrivalPoints` 텔레포트 도착).
3. 플레이어 이동은 **오너 권위** — 워크인·충격파 밀림은 오너가 돌린다. 명령에 **연출 회차 번호**를 붙여 스킵 뒤 늦게 온 이동이 다시 적용되지 않게 한다.
4. 자연 종료와 스킵은 **하나의 종료 처리** → 기존 `BeginCombatServer()`(착지 보정·잠금 해제·장판/드론 시작이 묶여 있다). 실패 복구(`FailedSafe`)와는 구분.
5. §1.3 애니 표는 **상태명과 클립명을 섞었다** — 팔라딘은 `Garen_*` 클립을 쓰지만 상태는 `Walk`/`Idle`/`SubSkillGuard`, 어쌔신 `Run_Loop`/`Dodge`/`Idle_Combat`, 거너 `Walk`/`Gunner_E_Backstep`/`Idle`. 표를 "캐릭터 / 상태명 / 클립명"으로 다시 쓴다.
6. `BossLandingPoint` = **차징(송전탑) 위치**(`TwentyThreeBoss.FindChargeLandingPoint`)다. 타일 그리드는 방 경계로 계산되므로 착지점만 옮기면 보스 차징 위치가 그리드와 어긋난다.
7. 기본 빌드 보스는 **B**(`TwentyThree_Raid.prefab` → `No23_B`). 검증은 B 기준, A 도 같이.
8. 튜토리얼 도착 1초 뒤 `TUT_B_BOSS_FRONT_TIP` 자막·보스 HP바(스폰 즉시 표시)가 독백과 겹친다 → 표시 순서를 정한다(HP바 = 전환 구간부터, 정면 팁 = 전투 개시 뒤).
9. 스킵 입력: Space = **Dash** 바인딩. 스킵 홀드 뒤 대시는 **키를 뗐다가 새로 눌러야** 인정(지금 입력 차단이 대시 버퍼를 지우지만 회귀 검증 항목으로 둔다). 투표 대상 = 도착 참가자 중 영구 사망 제외(Soul 포함), 이탈자 제거, 기존 `eligibleCount` 재사용, 발신자·회차 서버 검증. ⚠️ 뒤집음(10-26 경석): 투표자 = **접속자 전원(관전자 포함)**, 분모 `SkipVoterCount` — [PLAN-zone4-intro-cinematic.md](PLAN-zone4-intro-cinematic.md) D5. **1초 홀드를 채운 표는 유지**(떼도 취소 안 됨 — 3명 동시 홀드 퍼즐 방지, ✅ 경석 10-23 확정). 1초 전에 떼면 그 사람 게이지만 0 으로.
10. 워크인 7.5m/1.5초 = 5m/s 는 달리기다 — 거리를 줄인다. 정비 1초는 웰즈 지시와 겹쳐 전체 4.5~5초(상한 6초).
11. 수용 기준에 "**어느 단계에서 스킵해도 결과 동일**"을 추가(1~3인 원격 클라·Soul·이탈/재접속·중복 요청·재도전·연출 중 남은 투사체 피해, 착지 AoE 없음, 개전 타이머 1회).

---

## 0. 목표 및 핵심 요구사항

1. **시네마틱 보스 인트로 연출**:
   * 1~3인 가변 파티가 화면 6시에서 걸어 들어옴 (Walk-in).
   * 상공 18m에서 23호 + 웰즈(탑승)가 중앙 원형 제단(`BossLandingPoint`)으로 급강하 착지.
   * 지면 강타 충격파 후폭풍으로 캐릭터들이 1.5~2m 뒤로 미끄러지며 가드/회피 자세로 버팀.
   * 자세를 추스르고 전열을 가다듬음 (Combat Ready Idle).
   * 23호 위에 탑승한 웰즈가 아군을 향해 공격 지시(`Boss_welz_throwing` with `IsThrow`) + 23호 포효 호응.
   * 카메라 묶임이 풀리며 각 플레이어 본인의 고정 줌(인게임) 뷰로 매끄럽게 복귀, 보스 HP바 등장 및 전투 시작.

2. **Shape of Dreams / Ravenswatch 스타일 비주얼**:
   * 연출 시작과 함께 화면 상/하단 시네마틱 레터박스 및 강한 외곽 비네팅(Vignette)으로 몰입감 집중.
   * 웰즈의 위압적이고 차가운 독백/대사 자막 표시.
   * 연출 종료(전투 돌입) 시 비네팅과 레터박스가 부드럽게 걷히며 전장 조명/HUD 활성화.

3. **다인 협동 스페이스바 홀드(Hold) 스킵 시스템**:
   * 참가자 각자가 `Space` 키를 꾹 누르고 있으면 게이지 충전 (약 1.0초 홀드).
   * 화면 우측 하단에 스킵 투표 UI 표시: `스페이스바 아이콘 + 게이지 + 스킵 투표 (1/3, 2/3, 3/3)`.
   * 현재 생존 인원 전원(1인=1/1, 2인=2/2, 3인=3/3) 스킵 완료 시 즉시 연출 종료 $\rightarrow$ 카메라 복귀 및 전투 즉시 시작.

---

## 1. 기존 에셋 및 시스템 실측 데이터 (불변식 & Seams)

### 1.1 맵 환경 및 연출 최적화 좌표 (`bossroom.prefab`)
> 💡 **연출 자유도 확정**: 도착 슬롯과 보스 착지점 위치는 연출 구도(원근감·위압감·넉백 거리) 최적화를 위해 조정 가능하다.

* **보스 착지점 (`BossLandingPoint`)**:
  * 기존 실측: `(0, 0.725, 0)`
  * **★ 연출 최적화 권장 좌표**: **`(0, 0.725, +0.8)`**
  * *이유*: 원형 플랫폼 중앙보다 +0.8m 위쪽(12시 방향)에 착지시켜 23호의 육중한 상체가 플랫폼 중심을 꽉 채우고, 6시 방향 플레이어 시야를 가리지 않는 최상의 로우 앵글 확보.
* **플레이어 진입 및 도착 슬롯 (`PlayerArrivalPoints`)**:
  * **6시 진입 시작점**: `(0, 0, -12.0)` 부근 (화면 밖 6시 하단에서 전진 시작).
  * **1차 정지선 (Walk-in 정렬 대열 — 보스를 올려다보는 위치)**:
    * 1인: `(0, 0, -4.5)` (중앙)
    * 2인: `(-1.8, 0, -4.5)`, `(+1.8, 0, -4.5)` (좌우 대칭)
    * 3인 (V자 삼각편대 권장):
      * P1: `(0, 0, -4.5)` (선두 중앙)
      * P2: `(-2.2, 0, -5.2)` (좌측 반보 뒤)
      * P3: `(+2.2, 0, -5.2)` (우측 반보 뒤)
  * **충격파 넉백 후 최종 전투 시작 위치 (1.8m 스키드 후)**:
    * P1: `(0, 0, -6.3)`
    * P2: `(-2.2, 0, -7.0)`
    * P3: `(+2.2, 0, -7.0)`
    * *장점*: 넉백 후 보스와의 교전 거리가 약 7.1m로 자연스럽게 확보되어 시작 직후 불합리한 피격을 방지하고, 고정 줌 카메라 안착 시 가장 안정적인 쿼터뷰 구도 완성.

### 1.2 보스 구조 및 마운트 본
* 23호: `Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab`
* 웰즈: `Assets/2.Prefabs/Monster/Boss/Wells.prefab` (23호 내부 `TwentyThree/rig/c_pos/c_traj/c_root_master.x`에 마운트)
* 웰즈는 `MonoBehaviour`이며 네트워크 상태 복제는 부모인 `TwentyThreeBoss`가 담당.

### 1.3 사용 확정 애니메이션 클립 매핑
| 대상 | 단계 | 클립 이름 / 상태 | 파라미터 / 트리거 |
| :--- | :--- | :--- | :--- |
| **23호** | 하강 체공 | `JumpHover` (`Boss_23_jumping`) | `CrossFadeJumpStateClientRpc(false, true)` |
| **23호** | **착지 (랜딩)** | **`EntranceLanding` (`Boss_23_landingattack_entrance`)** | `CrossFadeJumpStateClientRpc(true, true)` |
| **웰즈** | 체공/착지 | `Idle` (`Boss_welz_idle`) | 기본 대기 |
| **웰즈** | **공격 지시** | **`Throw` (`Boss_welz_throwing`)** | `IsThrow` 트리거 (재생 후 자동 Idle 복귀) |
| **가렌** | 걸어오기 / 넉백 / 대기 | `Garen_Walk` / `Garen_E_GuardStart` / `Garen_Idle` | `IsMoving`, `Interrupt` |
| **어쌔신** | 걸어오기 / 넉백 / 대기 | `Run_Combat_Fast_Loop` / `Dodge_Combat_F_0` / `Idle_Combat` | 이동/회피 |
| **거너** | 걸어오기 / 넉백 / 대기 | `gunner_walk` / `gunner_skill_E_cool_backstep` / `gunner_idle` | 이동/백스텝 |

### 1.4 카메라 및 진입 시스템 Seam
* **카메라**: `CameraTargetSwitcher.Active.SetTemporaryFollow(Transform target)` $\rightarrow$ 연출 종료 시 `ClearTemporaryFollow()`.
* **트리거 지점**: `BossTeleportManager.AlivePlayersArrived` 이벤트 수신 $\rightarrow$ `BossEncounterDirector`가 인계.

---

## 2. 세부 연출 타임라인 (Timeline Beats)

전체 연출 시간: 약 **6.0초** (스킵 시 0초 즉시 전환)

```
[0.0s] ────────────────────────── [1.5s] ───────────────────── [2.2s] ───────────── [3.0s] ───────────────────── [5.2s] ──────── [6.0s]
  │                                 │                             │                    │                             │              │
  ├─ 6시 파티 Walk-In               ├─ 23호+웰즈 상공 강하         ├─ 지면 쾅! 착지     ├─ 전열 정비 (무기 파지)      ├─ 카메라 복귀 ├─ 전투 시작
  ├─ 시네마틱 비네팅/레터박스 ON    │  (그림자 급확대, 체공 모션) │  후폭풍 넉백(1.5m) ├─ 웰즈 공격 지시              ├─ 비네팅 걷힘 ├─ 조작권 인계
  └─ 웰즈 독백 자막 시작            └─ 쇠사슬 흔들림/천장 먼지    └─ 화면 쉐이크       └─ 23호 위협 포효 호응        └─ HP바 차오름 └─ 보스 AI ON
```

| 시각 (초) | 단계 (Phase) | 카메라 | 보스 (23호 & 웰즈) | 플레이어 파티 (1~3인) | 비주얼 & 사운드 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **0.0 ~ 1.5s** | `PartyWalkIn` | 중앙 고정 시네마틱 뷰 | 상공 대기 | 6시 입구에서 대열 맞춰 Walk-in 전진 | 비네팅+레터박스 페이드인, 웰즈 독백 출력 |
| **1.5 ~ 2.2s** | `BossDescent` | 중앙 제단 로우 앵글 | 18m 상공에서 급강하 (`JumpHover`) | 지정 정지선 도착 후 올려다봄 | 바닥 그림자 급확대, 급강하 윈드 컷 SFX |
| **2.2 ~ 3.0s** | `BossImpact` | 강한 수직 쉐이크 | **중앙 발판 강타 (`EntranceLanding`)** | **후폭풍에 밀려 1.5m 스키드 + 가드 모션** | 지면 크랙+충격파 먼지 링, 헤비 베이스 SFX |
| **3.0 ~ 4.0s** | `PartyRecover` | 보스 중심 풀샷 | 착지 반동 흡수 후 기립 | 자세 바로잡고 무기 쥐며 전투 대기 | 흙먼지 흩날림, BGM 인트로 빌드업 |
| **4.0 ~ 5.2s** | `WellsCommand` | 웰즈 상체 살짝 줌 | **웰즈 손 뻗어 공격 지시 + 23호 포효** | 보스 노려보며 대치 | 웰즈 눈빛/안광 이펙트, 23호 포효 SFX |
| **5.2 ~ 6.0s** | `Transition` | **인게임 카메라로 블렌드** | 전투 자세 전환 | 조작 입력 대기 | 비네팅/레터박스 페이드아웃, 보스 HP바 채움 |
| **6.0s ~** | `Combat` | **각자 고정 줌 뷰** | **전투 AI 가동** | **조작권 활성화** | 전투 메인 BGM 비트 드롭, 전투 개시 |

---

## 3. 기능별 상세 설계

### 3.1 Shape of Dreams / Ravenswatch 스타일 시네마틱 연출
* **UI Canvas 오버레이 (`BossCinematicOverlay`)**:
  * **Cinematic Letterbox**: 상단/하단 블랙 바 (각 높이 10~12%) 부드러운 슬라이드 인/아웃.
  * **Dark Vignette**: 화면 외곽을 어둡게 누르는 부드러운 원형 그라디언트 텍스처 (알파 블렌딩).
* **보스 독백 시스템 (`BossMonologuePresenter`)**:
  * 화면 하단 레터박스 위쪽에 스타일리시한 폰트로 웰즈의 독백 출력.
  * 예시 대사:
    > *"흥, 여기까지 기어 올라왔나... 23호, 저 쓸모없는 것들을 분쇄해라."*
  * 한 글자씩 타이핑되거나 페이드인되는 시네마틱 텍스트 효과.

### 3.2 다인 협동 스페이스바 홀드 스킵 시스템
* **클라이언트 로컬 입력 (`BossEncounterSkipInput`)**:
  * `InputSystem`: `Keyboard.current.spaceKey.isPressed` 감지.
  * 0.0s $\rightarrow$ 1.0s 동안 누르고 있으면 로컬 게이지(0~100%) 차오름.
  * 1.0s 도달 시 서버로 `SendSkipVoteServerRpc(true)` 전송.
  * 키를 떼면 게이지 리셋 및 취소 전송.
* **서버 권한 집계 (`BossEncounterDirector`)**:
  * `NetworkVariable<int> skipVoteCount`: 현재 스킵 완료 인원 수.
  * `NetworkVariable<int> eligibleCount`: 전체 참가자 수 (1~3명).
  * `skipVoteCount == eligibleCount` 만족 시 즉시:
    ```csharp
    // 컷씬 강제 조기 종료
    AbortCutsceneAndStartCombat();
    ```
* **스킵 UI (`BossSkipVoteUI`)**:
  * 위치: 화면 우하단.
  * 구성: `[SPACE] 꾹 눌러서 스킵` + 원형 프로그레스 바 + `(1/3)` 카운터.
  * 스킵 완료 시 초록색 체크 아이콘 및 인원수 즉시 갱신.

---

## 4. 작업 배치 계획 (Claude Code 실행 순서)

### Batch 1 — 스킵 시스템 및 네트워크 동기화 (Core Logic)
* **1-1**: `BossEncounterDirector.cs`에 스킵 투표 RPC(`SendSkipVoteServerRpc`) 및 카운트 동기화 구현.
* **1-2**: `AbortCutsceneAndStartCombat()` 구현 — 진행 중인 모든 연출 코루틴 중단, 카메라 즉시 복귀, 입력 즉시 해제, 보스 FSM 활성화.
* **1-3**: `BossSkipVoteUI` 프리팹 및 프레젠터 스크립트 작성 (스페이스바 홀드 게이지 + 1/3 카운터).

### Batch 2 — 시네마틱 비주얼 & 독백 오버레이 (Art & Visuals)
* **2-1**: `BossCinematicOverlay` 제작 — 레터박스(상/하) + 외곽 다크 비네팅 UI 생성 및 페이드 인/아웃 로직.
* **2-2**: `BossMonologuePresenter` 제작 — 웰즈 독백 텍스트 연출 (대사 출력, 타이밍 동기화).
* **2-3**: `BossEncounterPhase`와 비주얼 오버레이 상태 연동.

### Batch 3 — 캐릭터 시퀀스 연출 (플레이어 Walk-in + 후폭풍 넉백)
* **3-1**: 플레이어 6시 스폰 및 지정 슬롯(`PlayerArrivalPoints`)으로 걸어오기 보행 시퀀스 구현 (`BossElevatorRig` 보행 패턴 참조).
* **3-2**: 23호 착지 순간(`Impact`) 플레이어 1.5m 넉백 물리 보간 + 캐릭터별 리액션 클립(`Guard`/`Dodge`/`Backstep`) 트리거.
* **3-3**: 웰즈 지시 모션 트리거 (`Boss_welz_throwing` with `IsThrow`) 및 23호 포효 연계.

### Batch 4 — 카메라 트랜지션 및 전투 진입 검증 (Polish & Integration)
* **4-1**: `CameraTargetSwitcher.Active.SetTemporaryFollow(중앙/보스 뷰)` $\rightarrow$ 연출 종료 시 `ClearTemporaryFollow()` 부드러운 전환 검증.
* **4-2**: 1인, 2인, 3인 가변 스폰 및 스킵 동작 PlayMode 검증 (1인 즉시 스킵, 3인 전원 찬성 시 스킵).
* **4-3**: 컴파일 0 에러 및 기존 FSM 회귀(Regression) 없음 확인.

---

## 5. 수용 기준 (Definition of Done)
1. **정상 재생 흐름**: 6시 진입 $\rightarrow$ 웰즈 독백+비네팅 $\rightarrow$ 착지 $\rightarrow$ 충격파 밀림 $\rightarrow$ 웰즈 지시 $\rightarrow$ 카메라 복귀 $\rightarrow$ 전투 시작이  끊김 없이 6초 내에 매끄럽게 연결된다.
2. **스킵 검증**: 1인 플레이 시 스페이스바 1초 홀드로 즉시 스킵되어 전투가 시작된다. 3인 플레이 시 3명 모두 스페이스바를 누르는 순간 즉시 연출이 종료되고 전투가 시작된다.
3. **카메라 정렬**: 연출이 끝나거나 스킵되었을 때 각 플레이어의 로컬 카메라가 본인 캐릭터의 고정 줌 시점으로 오차 없이 정확히 복귀한다.
4. **기존 보스 FSM 보존**: `TwentyThreeBoss`의 기본 공격 테이블, 기믹 및 동기화에 어떠한 사이드 이펙트도 주지 않는다.
