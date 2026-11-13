# 로비 결과 패널 — 붙이는 법 (할 일 48)

> 5.ResultScene 을 없애고 판 결과를 로비 패널로 보여 준다(PLAN-48 v2 §0-1).
> 이 문서는 **패널을 로비에 붙이는 쪽(Work3)** 이 볼 것만 적는다.
> 붙임 = C7b(`feature/LobbyDemo`): 3.LobbyScene 에 프리팹 인스턴스, `LobbySceneManager.Start` 가 `HasResult` 면 Show, 임시 다시 보기 버튼 `UI/Canvas/Button_ShowLastResult`(C9 에서 UI 루트로 옮김)(`UI.Lobby.ShowLastResult`). 결과 비우기는 기존 두 곳(`SessionStatsTracker.Awake`·`DemoSessionDirector.CleanupAfterDrop`)으로 충분하다고 보고 새로 넣지 않았다.

## 파일
| 무엇 | 경로 |
|---|---|
| 패널 프리팹 | `Assets/2.Prefabs/UI/Result/LobbyResultPanel.prefab` |
| 행 프리팹 | `Assets/2.Prefabs/UI/Result/LobbyResultPlayerRow.prefab` (`ResultPlayerRowView` 재사용) |
| 뷰 | `Assets/1.Scripts/UI/LobbyResult/LobbyResultPanel.cs` |
| 열고 닫기 규칙(순수) | `Assets/1.Scripts/UI/LobbyResult/LobbyResultPanelRules.cs` — 테스트 `Editor/LobbyResultPanelRulesTests.cs` |
| 저작·미리보기 | `Assets/1.Scripts/UI/LobbyResult/Editor/LobbyResultPanelAuthoring.cs` |

- 5.ResultScene·`ResultSceneManager` 는 그대로다. `ResultStatsView` 에 `applyOnStart`(기본 true)와 `Apply(ulong localClientId)` 만 더했다 — 결과 씬 동작은 같다.

## 붙이는 법
1. 로비 씬 아무 곳에 `LobbyResultPanel.prefab` 을 둔다. 루트에 자체 Canvas(Screen Space Overlay, sortingOrder 200)가 있어 부모 Canvas 가 필요 없다. 씬에 **EventSystem** 은 있어야 한다(닫기 버튼).
2. 4.Map 이 끝나고 로비에 들어왔을 때만 연다:
   ```csharp
   if (panel.HasResult) panel.Show();
   ```
   첫 부팅·재접속으로 들어올 때는 결과가 비어 있어야 한다(아래 "결과 비우기").

## API (`LobbyResultPanel`)
| 멤버 | 동작 |
|---|---|
| `bool HasResult` | `SessionResult.HasValue` |
| `void Show()` | 현재 `SessionResult` 정적 값으로 채워서 연다. 결과가 없으면 **아무것도 안 한다**. 열려 있을 때 부르면 다시 채우고 자동 닫힘 타이머를 처음부터 잰다 |
| `void Hide()` | 닫는다. 결과는 그대로 — 다시 `Show()` 가능 |
| `bool IsOpen` | 열려 있는가 |
| `event Action<LobbyResultCloseReason> Closed` | 닫힐 때 한 번(`Api`·`Button`·`Escape`·`AutoClose`) |
| `static bool IsEscapeClaimed` | 패널이 열려 있거나 이 프레임 ESC 로 닫혔다 — 아래 ESC 참고 |

- 표시 내용: 판 결과 라벨(CLEAR/FAILED/ABORTED, `UI.Result.*` 키) · 소요 시간 mm:ss · 플레이어 행(아래 "열·순위") · "나" 행 금색 테두리(맥동, 아래).
- **Show 시점에 값을 글자로 복사한다.** Show 직후 `SessionResult.Clear()` 해도 보이는 내용은 그대로다(단 그 뒤엔 다시 Show 할 수 없다).
- "나" 강조 기준 = `NetworkManager.LocalClientId`(세션 유지 중이어야 한다).
- **"나" 강조 모양(할 일 222)** = 행 프리팹 `Marker_Local`(행 배경 `Backing` 뒤, Bind 가 로컬 행만 켬).
  - 스프라이트 = Unity 내장 `UI/Skin/UISprite.psd`(흰 9-slice 둥근 사각, 프로젝트 다른 저작 메뉴도 씀 — 새 아트·SVN 변경 없음), 색 밝은 금색 `(1, 0.84, 0.3, 1)`, `pixelsPerUnitMultiplier` 0.18(모서리를 키워 행 배경 둥근 모서리에 맞춤), 행 배경 밖으로 **14px**(예전 9).
  - 예전 = `result_row_backing`(회색 88) 위에 금색을 곱해 어두운 갈색 테가 됐다 — 안 보이던 원인. 검토한 후보: ① 내장 UISprite(채택) ② `50.Art/UI/HUD/Tooltip/Skill_Tooltip_Tag_Outline.png`(또렷한 흰 외곽선이지만 Sprite 임포트·9-slice 가 아니라 SVN `.meta` 수정 필요) ③ `50.Art/UI/char_select/card_selected_ring.png`(흰 링, 9-slice 없음 → 가로로 긴 행에 늘리면 테 두께가 찌그러짐). 프로젝트 UI 아트의 9-slice 스프라이트는 모두 어두운 색(28~116)이라 곱하면 금색이 죽는다.
  - 맥동 = `LobbyResultLocalMarkerPulse`(같은 오브젝트): `period` 1.6초마다 투명도 1 ↔ `minAlpha` 0.55, 테 여백 +`outsetBreath` 3px ↔ 0, `ease` InOutSine(EuniTween `PingPongPulse`), unscaled. 켜질 때 가장 밝게 시작. `pulse` 끄면 저작 값 그대로. 자기 Image 알파·자기 여백만 바꿔 카운트업·패널 펀치(부모 크기)와 안 겹친다.
  - 행 프리팹 재생성(`Tools/UI/Authoring/Lobby Result Panel (재생성)`)도 같은 값(`LocalMarkerColor`·`LocalMarkerOutset`·`LocalMarkerCornerScale`, 맥동 컴포넌트)을 붙인다.
  - 크기 `localScale` = **(0.99, 0.9, 1)**(222 후속, 사용자 조정 — 저작 메뉴 `LocalMarkerScale` 도 같은 값). 맥동은 여백(offset)·알파만 바꾸고 `localScale` 은 건드리지 않는다.

## 닫기·모달
- 닫기 버튼, ESC, **열린 뒤 180초**(`autoCloseSeconds`, `Time.unscaledTime` — 슬로모션 영향 없음, 0 이하면 끔) 중 먼저 오는 것. 피어마다 각자(로컬) 닫는다.
- 열려 있는 동안 `Content` 의 `UiModalBlocker` 가 로컬 플레이어 입력을 막고, 반투명 Dim 이 뒤 클릭을 막는다.
- **닫기 버튼 hover** = 버튼 기본 Transition(ColorTint)만 — 마우스를 올리면 아주 살짝(0.96) 어두워지고, 누르면 0.78 로 어두워진다. 할 일 224 의 흰 테두리(`UiHoverBorder`, 버튼 뒤 형제 Image `Button_Close_HoverBorder`)는 **11-05 할 일 228 로 삭제** — 내장 `UISprite` 9-slice 를 `cornerScale` 0.12 로 그려 모서리가 버튼 높이보다 커져 버튼 뒤에 흰 '더하기' 모양으로 겹쳐 보였다. 컴포넌트·테스트·재생성 메뉴 코드 모두 지웠다(셰브론용 `UiHoverOutline`(할 일 109)은 별개라 그대로).

## ESC
- 패널은 열려 있을 때만 ESC 를 쓴다. 지금 로비(`LobbySceneManager`)에는 ESC 처리가 없어 충돌은 없다.
- 🔴 로비에 ESC 를 쓰는 메뉴(일시정지·설정·캐릭터 선택 패널 등)를 붙이면, 그쪽에서 `LobbyResultPanel.IsEscapeClaimed` 가 true 일 때 ESC 를 무시해야 같은 키로 둘이 같이 움직이지 않는다
  (Update 순서와 무관하게 막으려고 "이 프레임 ESC 로 닫힘"도 true 로 본다).
- `PlayerSkillTargeting` 의 ESC(조준 취소)는 플레이어 입력이 막혀도 키보드를 직접 읽는다 — 조준 중에 패널이 열리는 경우는 로비 진입 직후라 없다고 본다.

## 결과 비우기 — 🔴 호출 측 책임
- 패널은 `SessionResult.Clear()` 를 부르지 않는다. 다음 판 시작 때(또는 끊김 정리 때) 흐름 쪽이 비운다.
  비우지 않으면 재접속·다음 로비 진입 때 지난 결과가 다시 열린다.

## 열·순위 (2차, 사용자 결정 10-19)
- 열 = 목업 그대로 7열: **순위 / 플레이어 / 가한피해 / 회복량 / 간파 성공 / 입은 피해 / 사망** (셀 크기·위치 = 목업 manifest).
  회복량·입은 피해 정의는 [result-stats-ui-setup.md](result-stats-ui-setup.md) "통계 정의".
- **처치는 로비 패널에 표시하지 않는다**(목업에 없음). 데이터·페이로드·5.ResultScene 에는 그대로 있다.
- 행은 **순위 순서**로 그린다. 순위 칸 = 1위 행만 왕관(`icon_top_marker`, 90×90 = PNG 180px/2, 칸 가운데), 나머지는 순위 숫자(2, 3 …). 플레이어 칸 = 초상화 + 캐릭터 이름. P 번호 열은 없다.
- 순위 규칙 = `LobbyResultRanking`(순수, 테스트 `Editor/LobbyResultRankingTests.cs`):
  - `score = 가한 피해 × (사망 0회 ? 1.5 : 1.0) × min(1.15 ^ 간파 성공 수, 4.0)` — 피해 0 이면 0. 간파 10회(1.15^10 ≈ 4.05)부터 상한.
  - 점수 내림차순, **동점이면 슬롯 순**(clientId 오름차순 = `SessionResult.Players` 순서). 동점이어도 순위 번호는 다르다(1, 2 …).
  - **최고 점수가 0 이면(아무도 피해를 못 냈다) 왕관 없음** — 1위 칸에 숫자 1.
- 계수 = `LobbyResultPanel` 인스펙터(xlsx 없음):

  | 필드 | 기본값 | 뜻 |
  |---|---|---|
  | `noDeathMultiplier` | 1.5 | 사망 0회 배수 |
  | `deathMultiplier` | 1.0 | 1회 이상 사망 배수 |
  | `counterBase` | 1.15 | 간파 1회마다 곱하는 밑 |
  | `counterMultiplierCap` | 4.0 | 간파 배수 상한 |

- 행 프리팹 자식: `Cell_Rank`(`Text_Rank`·`Image_Crown`) · `Cell_Player`(`PortraitMask`(RectMask2D 84×84)/`Image_Portrait` — 얼굴 크롭, [character-select-panel.md](character-select-panel.md) "초상화 일러스트" · `Text_Character`) · `Text_Damage` · `Text_Healing` · `Text_Counters` · `Text_DamageTaken` · `Text_Deaths`.
  `ResultPlayerRowView` 의 새 참조(`healingText`·`damageTakenText`·`rankText`·`crownImage`)는 비어 있으면 무시된다 — 5.ResultScene 행은 그대로.

## 레이아웃 (목업 `UI_Panel/Mockups~/05_BattleResults_with_text.png` 와 다른 점)
- 판 결과 라벨은 탭 제목 "전투결과" 오른쪽. 푸터 = "소요시간 :" + mm:ss, 오른쪽 큰 버튼 자리에 닫기 버튼 하나(`button_pill_neutral`). 왼쪽 작은 버튼 자리(다시하기)는 패널 그림의 빈 받침만 남는다.
- 보이는 행은 3줄(실사용 3인). 4인 이상이면 행이 표 아래로 넘친다.
- 폰트 = `NotoSansKR-VariableFont_wght SDF`(원본 SeoulAlrimTTF-Heavy 없음) — 목업보다 얇다.
- 탭 제목·열 제목·소요시간 라벨·닫기 글자 = `LocalizedText`(`UI.LobbyResult.*`, 목록 [LobbyResultUiStrings.cs](../../Assets/1.Scripts/UI/LobbyResult/LobbyResultUiStrings.cs)). 직렬화 글자 = 키 ko(시트에 행이 없을 때 폴백). 재생성 메뉴도 같은 키를 붙인다. 키 표는 [lobby-demo-flow.md §8](lobby-demo-flow.md#8-화면-문구-키-strings).

## 열고 닫기·숫자 카운트업 연출 (할 일 215·220)
- `Content` 의 `LobbyResultPanelAnimator`(표시 전용 — 열고 닫기 판단·`Closed`·ESC·네트워크 표시 계약은 그대로).
  - 열기: 창(`Content/Panel`) 크기 `hiddenScale`(0.95)→1 · `Content` 투명도 0→1(Dim 포함), `openDuration` 0.22초.
  - 닫기: 반대로 `closeDuration` 0.14초 뒤 `Content` 를 끈다. **입력 차단은 닫기 시작 때 바로 푼다**(`UiModalBlocker` 끔 + 클릭 막기 해제). `IsOpen`·`Closed` 도 즉시.
  - 숫자: 플레이어 행 숫자(가한 피해·회복량·간파 성공·입은 피해·사망)가 열기 시작 `countUpDelay` 0.15초 뒤 `countUpDuration` **1.5초**에 걸쳐 0→최종값.
  - **끝 감속(할 일 220)**: `countUpCurve` 기본 = 키 3개 `(0,0 · 기울기 4.5)` → `(0.3, 0.82 · 0.5)` → `(1, 1 · 0)`. 처음 30% 시간(0.45초)에 82% 까지 빠르게 올라가고, 남은 18% 를 70% 시간(1.05초) 동안 점점 느리게 — 마지막 자리가 천천히 올라가다 멈춘다. 넘침 없음(테스트로 고정), 0~1 밖은 자른다.
    숫자는 **소수 버림**(220 — 예전 반올림) → 최종값은 진행도 1(= 펀치 순간)에만 나온다. 반올림이면 처치 12 같은 작은 값이 감속 꼬리 동안 미리 최종값에 멈춰 보였다. 시간 0.8 → 1.5초 근거: 빠른 구간은 예전과 비슷한 속도로 두고, 느린 꼬리가 1초쯤은 돼야 "멈출 듯 말 듯"이 보인다. 프리팹에도 같은 값이 저장돼 있다(코드 기본값만 바꾸면 반영 안 됨).
  - **값 크기별 감속 세기(할 일 220 후속)**: 값마다 진행도 = 선형과 곡선을 가중치로 섞은 것(`LobbyResultCountUpFrame` → `CountUpCurveWeight`·`BlendedCountUpProgress`). 단계 수(정수 칸 = 최종값, 소요 시간 = 표시 초 수)가 `linearMaxSteps` **10** 이하면 선형(정수가 끝까지 고르게 — 처치 3 이면 0.5초마다 한 칸), `curveFullSteps` **1000** 이상이면 곡선 그대로(피해량 수만~수백만은 극적 감속), 사이는 자릿수(log10)로 보간(100 = 반반). 소요 시간 12:34(754초)는 가중치 ≈0.94.
    근거: 10 이하는 1.5초에 칸당 0.15초 이상 — 감속 꼬리(1.05초)에 넣으면 한두 칸이 한참 멈췄다 한 번에 바뀐다. 1000 이상은 꼬리 18% 에도 180칸 이상이 남아 계속 굴러간다. **모든 값은 선형 진행도 1(= 펀치 순간)에 동시에 최종값**(선형·곡선 둘 다 1 에서 1), 섞어도 넘침·되돌아감 없음(테스트로 고정).
  - **카운트업 끝 패널 펀치(할 일 220)**: 카운트업이 끝나는 순간(`countUpDelay + countUpDuration`, 기본 열기부터 1.65초) 창(`Content/Panel` = `scaleTarget`)이 `punchScale` 1.06 까지 커졌다 1 로 돌아온다. `punchDuration` 0.28초, **EuniTween** `Easing.Punch(punchEase=OutQuad)`(커지는 절반 이징, 줄어드는 절반 거울), unscaled. 크기 = 열기 크기 × 펀치(곱) — 열기(0.22초)와는 시간상 안 겹치고, 겹쳐도 튀지 않는다. 지속 0 또는 배율 1 = 펀치 없음(왕관도 기다리지 않음). 카운트업 시간 0(즉시)이면 펀치도 없다.
    닫기 시작·Content 꺼짐·다시 열기 때 펀치를 멈추고 배율 1 로(닫힘 크기 연출이 이어받음, 다시 열면 처음부터).
  - **등급 도장 '쾅'(할 일 223)**: 소요시간 칸 오른쪽 빗금 칸(닫기 버튼 왼쪽)의 `Content/Panel/GradeStamp`(깔끔한 빨간 원 120px — git 의 흰 원 PNG `Assets/2.Prefabs/UI/Textures/ResultGradeCircle.png`(512px, 가장자리 안티에일리어싱)에 Image 색 빨강(205,32,36) 틴트, 기본 Knob 은 확대 시 흐려서 안 씀. 예전 도장 스프라이트는 11-05 삭제 + 가운데 크림색 TMP 글자 `Text_Grade` S/A/B/C, 결과창 다른 글자와 같은 서울알림 Medium `Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset`). 로비 결과창에만 있다(5.ResultScene 없음).
    규칙 = `LobbyResultGrade.Evaluate`(순수, 테스트 `Editor/LobbyResultGradeTests.cs`): 소요 시간 ≤ `gradeSMaxMinutes` 6분 = S(4분보다 빠른 경우 포함), ≤ `gradeAMaxMinutes` 9분 = A, ≤ `gradeBMaxMinutes` 12분 = B, 그 위 C. 경계 포함, **화면 표기와 같은 정수 초**(06:00.4 = "06:00" = S). 경계 3칸 = `LobbyResultPanel` 인스펙터(xlsx 노출 안 함). **도장은 클리어 판에만** — 전멸(패배)·중단·결과 없음이면 끈다(`LobbyResultGrade.ShowsStamp`, 사용자 결정 11-05). 등급 자체는 시간만 본다.
    연출(`LobbyResultPanelAnimator`, **EuniTween**, unscaled): **패널 펀치가 끝나면**(`stampDelay` 0 — 기본 열기부터 1.93초) 투명도 0·크기 `stampStartScale` 2.5·기울기 `stampStartAngle` -12° 에서 `stampDropDuration` 0.12초 만에 투명도 1·크기 1·`stampEndAngle` -8° 로 내려찍는다(`stampDropEase` InQuad). 착지 순간 창 본체(`scaleTarget`)가 `stampShakeDuration` 0.12초 흔들리고(`stampShakeAmplitude` 8px 에서 줄어듦, `stampShakeCycles` 3번 왕복, 무작위 없음) 도장이 `stampBounceDuration` 0.16초 동안 `stampBounceScale` 1.08 로 튀었다 자리 잡는다(`Easing.Punch(stampBounceEase=OutQuad)`). 착지 순간 `stampSoundKey`(비우면 무음, 지금 비어 있음).
    **순서 조정**: 왕관 첫 점프는 도장이 자리 잡은 뒤(펀치 0.28 + 도장 0.12 + 0.16 + `crownDelay` 0.05 = 기본 열기부터 2.26초, 예전 1.98초). 카운트업 시간 0(즉시)이면 도장은 처음부터 최종 모습. 닫기 시작·다시 열기 때 흔들림을 멈추고 도장은 최종 모습(다시 열면 처음부터). 도장 투명도용 CanvasGroup 은 프리팹에 있다(클릭 막기 없음).
  - 소요 시간(`Text_PlayTime`, mm:ss)도 숫자와 **같은 진행도**로 00:00→최종값(`LobbyResultPanel.SetPlayTimeCountUp`, 표기 = `SessionResult.FormatTime` — 초 아래 버림이라 최종값을 넘지 않음). 패널은 애니가 부모에서 찾는다(프리팹 연결 없음).
  - 1위 왕관(`Image_Crown`, 켜진 것): **패널 펀치와 등급 도장이 끝나고**(220·223 — 펀치·도장 없으면 카운트업 끝) `crownDelay` 0.05초 뒤 첫 점프(기본 열기부터 2.26초), 그 뒤 **`crownRepeatInterval` 3초 주기**(시작→다음 시작, 0 = 1회만)로 반복. 한 번 = `crownDuration` 0.6초 동안 `crownJumpHeight` 28 만큼 점프하며 `crownSpinDegrees` 360° 회전. 행 밖으로 튀어나가도 자르지 않는다(마스크 없음). **EuniTween** 이징 — 점프 `Easing.Punch(crownJumpEase=OutQuad)`(오를 때 느려지고 떨어질 때 빨라짐), 회전 `Easing.Evaluate(crownSpinEase=InOutCubic)`.
    회전 축 `crownSpinAxis` 기본 **Y** — 동전 뒤집듯 좌우로 돌아 왕관이 늘 서 있다(Z 는 화면 안 바퀴 회전이라 중간에 거꾸로 섬). UI 셰이더가 양면이라 뒷면도 보인다. 끝나면 원래 위치·회전으로 정확히 돌린다.
  - 왕관 반짝임: **착지마다** `sparkleOffsets`(왕관 크기 대비 3곳)에 `sparkleStagger` 0.08초 간격으로 크게 한 번씩, 그 뒤 다음 점프 전까지 `sparkleRepeatInterval`(코드 기본 0.7, 프리팹 1.6)초마다 하나씩 번갈아 은은하게(`sparkleRepeatScale` 0.7 — 3초 주기 안에 2번, 다음 점프와 안 겹침). 간격 0 = 착지 때만. 점프 중엔 반짝임을 끈다(Y 회전에 같이 납작해지지 않게).
    스프라이트 = 인스펙터 **`sparkleSprite` 한 칸**(교체는 여기만) — 기본은 기존 에셋 `Assets/50.Art/VFX/Textures/Flare06.png`(4갈래 별빛, 원래 Sprite 임포트, `FX_Heal` 이 쓰는 것). 연결 메뉴가 비어 있을 때만 넣는다.
    구현 = 왕관 자식 Image 3개(파티클 아님 → Overlay 캔버스에 그대로 보임, 런타임 텍스처·메시 생성 없음, 행이 다시 그려지면 같이 사라짐). 색 `sparkleColor`·크기 `sparkleSize`·회전 `sparkleSpinDegrees`·이징 `sparkleEase`.
  - 곡선 3개(`openCurve`·`closeCurve`·`countUpCurve`)와 시간 = 인스펙터. 시간은 `Time.unscaledDeltaTime`(슬로모션 무관). 지속 0 = 즉시.
  - CanvasGroup 은 프리팹에 없고 런타임에 붙인다. 컴포넌트가 없으면 패널은 예전처럼 즉시 켜고 끈다.
- 계산 = `LobbyResultPanelAnimMath`(순수, 테스트 `Editor/LobbyResultPanelAnimMathTests.cs`). 카운트업 진입점 = `ResultStatsView.SetCountUpProgress` → `ResultPlayerRowView.SetCountUpProgress`(5.ResultScene 은 부르지 않음).
- 테스트 씬 = `Assets/0.Scenes/Debug/DebugLobbyResultPanelLab.unity`(빌드 목록 없음, 씬 열고 그냥 ▶ — 네트워크 없음). 다시 만들기 = `Tools/Debug/215 로비 결과창 테스트 씬 만들기`.
  가짜 결과(최대 1,234,567)로 바로 열린다. 키: 1/2/3 = 플레이어 수 바꿔 열기 · O 열기 · C 닫기 · ESC 패널 닫기 · R 닫는 도중 다시 열기 · V 값 바꿔 다시 열기 · T 슬로모션 0.1 토글. G = 등급 도장 소요 시간 예시 순환(S 03:50 → S 06:00 → A 06:01 → A 09:00 → B 12:00 → C 12:01, 버튼도 있음 — 값 세트를 바꿔도 그 시간 유지). X = 판 결과 순환(클리어 → 전멸 → 중단, 버튼도 있음 — 도장은 클리어에만). 배경 도형이 천천히 돌아 201 블러 확인에도 쓴다.
- 연결 = `Tools/UI/로비 결과창 애니 연결` — 멱등(이미 붙어 있으면 저장 안 함, 튜닝값 보존). 프리팹 병합 충돌로 빠지면 다시 돌린다. 재생성 메뉴도 같이 붙인다.
  반짝임 스프라이트가 비어 있으면 `Flare06` 을 넣는다(바꾼 스프라이트는 그대로). 시간 카운트업·왕관 점프는 런타임에 찾아 쓰므로 따로 잇지 않는다.
- 테스트 씬 V(값 바꿔 다시 열기)로 숫자·시간 카운트업(끝 감속·값 크기별 세기)·패널 펀치·왕관 점프·반짝임이 처음부터 다시 재생된다. 닫힘 연출 끝(Content 꺼짐)·다시 열기·행 다시 그림 때 왕관 반복은 멈추고 원위치(다시 열면 첫 점프부터)

## 다시 만들기·미리보기
- `Tools/UI/Authoring/Lobby Result Panel` — 없는 프리팹만 만든다(있으면 손대지 않음, 다시 돌려도 바이트 동일).
- `Tools/UI/Authoring/Lobby Result Panel (재생성)` — 두 프리팹을 처음부터 덮어쓴다(GUID 유지, 내부 fileID 는 바뀜). 열 폭·위치·색은 스크립트 상단 상수.
- `Tools/UI/Authoring/Lobby Result Panel 등급 도장 추가·갱신 (223)` — 기존 패널 프리팹에 `GradeStamp` 를 붙인다(이미 있으면 원 스프라이트·색만 다시 맞추고 위치·글자는 그대로). 원 PNG 를 Sprite(Single·밉맵 없음)로 맞춘다. 글자 폰트 = 스크립트 `GradeFontPath`(서울알림 Medium — 상단 `FontPath` NotoSansKR 는 처음 저작 값이라 도장에 안 씀). 재생성 메뉴도 같은 도장을 만든다(위치·크기·글자색 = 스크립트 `GradeStamp…` 상수).
- `Tools/UI/Preview/Lobby Result Panel 캡처` — 프리뷰 씬에서 가짜 결과(3인·P2 강조·CLEAR·12:34, 순위 P3→P1→P2 로 슬롯과 다르게)로 1920×1080 PNG 와 목업 비교 PNG 를
  `C:/UnityProject/_lane-artifacts/MainProject-WorkTree/48-result-panel/` 에 저장하고 `SessionResult.Clear()` 로 되돌린다. 열린 씬은 건드리지 않는다.
- 테스트: `Tools/Tests/결과 통계 EditMode 테스트 실행`(`LobbyResultPanelRulesTests`·`LobbyResultRankingTests`·`LobbyResultGradeTests` 포함).

## ⏳ 사람 Play 확인
- 판 끝 → 로비 복귀 때 패널이 뜨고 값이 결과와 같은가(호스트·클라 각각, "나" 강조가 자기 행인가).
- 닫기 버튼·ESC·180초 자동 닫힘, 열린 동안 이동·공격이 막히고 닫으면 풀리는가. 슬로모션 중에도 180초가 실제 시간인가.
- 첫 부팅·재접속 로비에서는 안 뜨는가(호출 측 Clear 확인).
- 행이 순위 순서인가, 1위만 왕관인가(아무도 피해 0 이면 왕관 없음). 회복량·입은 피해 값이 호스트·클라에서 같은가.
- 등급 도장(223): 펀치 직후 도장이 크게·기운 채로 '쾅' 내려찍히고 창이 짧게 흔들리는가, 소요 시간에 맞는 글자(6분 이하 S·9분 이하 A·12분 이하 B·그 위 C)인가, 전멸·중단 판에서는 도장이 안 보이는가, 왕관 첫 점프가 도장 뒤에 오는가.
