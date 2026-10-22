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

- 표시 내용: 판 결과 라벨(CLEAR/FAILED/ABORTED, `UI.Result.*` 키) · 소요 시간 mm:ss · 플레이어 행(아래 "열·순위") · "나" 행 금색 테두리.
- **Show 시점에 값을 글자로 복사한다.** Show 직후 `SessionResult.Clear()` 해도 보이는 내용은 그대로다(단 그 뒤엔 다시 Show 할 수 없다).
- "나" 강조 기준 = `NetworkManager.LocalClientId`(세션 유지 중이어야 한다).

## 닫기·모달
- 닫기 버튼, ESC, **열린 뒤 180초**(`autoCloseSeconds`, `Time.unscaledTime` — 슬로모션 영향 없음, 0 이하면 끔) 중 먼저 오는 것. 피어마다 각자(로컬) 닫는다.
- 열려 있는 동안 `Content` 의 `UiModalBlocker` 가 로컬 플레이어 입력을 막고, 반투명 Dim 이 뒤 클릭을 막는다.

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

- 행 프리팹 자식: `Cell_Rank`(`Text_Rank`·`Image_Crown`) · `Cell_Player`(`Image_Portrait`·`Text_Character`) · `Text_Damage` · `Text_Healing` · `Text_Counters` · `Text_DamageTaken` · `Text_Deaths`.
  `ResultPlayerRowView` 의 새 참조(`healingText`·`damageTakenText`·`rankText`·`crownImage`)는 비어 있으면 무시된다 — 5.ResultScene 행은 그대로.

## 레이아웃 (목업 `UI_Panel/Mockups~/05_BattleResults_with_text.png` 와 다른 점)
- 판 결과 라벨은 탭 제목 "전투결과" 오른쪽. 푸터 = "소요시간 :" + mm:ss, 오른쪽 큰 버튼 자리에 닫기 버튼 하나(`button_pill_neutral`). 왼쪽 작은 버튼 자리(다시하기)는 패널 그림의 빈 받침만 남는다.
- 보이는 행은 3줄(실사용 3인). 4인 이상이면 행이 표 아래로 넘친다.
- 폰트 = `NotoSansKR-VariableFont_wght SDF`(원본 SeoulAlrimTTF-Heavy 없음) — 목업보다 얇다.
- 탭 제목·열 제목·소요시간 라벨·닫기 글자 = `LocalizedText`(`UI.LobbyResult.*`, 목록 [LobbyResultUiStrings.cs](../../Assets/1.Scripts/UI/LobbyResult/LobbyResultUiStrings.cs)). 직렬화 글자 = 키 ko(시트에 행이 없을 때 폴백). 재생성 메뉴도 같은 키를 붙인다. 키 표는 [lobby-demo-flow.md §8](lobby-demo-flow.md#8-화면-문구-키-strings).

## 다시 만들기·미리보기
- `Tools/UI/Authoring/Lobby Result Panel` — 없는 프리팹만 만든다(있으면 손대지 않음, 다시 돌려도 바이트 동일).
- `Tools/UI/Authoring/Lobby Result Panel (재생성)` — 두 프리팹을 처음부터 덮어쓴다(GUID 유지, 내부 fileID 는 바뀜). 열 폭·위치·색은 스크립트 상단 상수.
- `Tools/UI/Preview/Lobby Result Panel 캡처` — 프리뷰 씬에서 가짜 결과(3인·P2 강조·CLEAR·12:34, 순위 P3→P1→P2 로 슬롯과 다르게)로 1920×1080 PNG 와 목업 비교 PNG 를
  `C:/UnityProject/_lane-artifacts/MainProject-WorkTree/48-result-panel/` 에 저장하고 `SessionResult.Clear()` 로 되돌린다. 열린 씬은 건드리지 않는다.
- 테스트: `Tools/Tests/결과 통계 EditMode 테스트 실행`(`LobbyResultPanelRulesTests`·`LobbyResultRankingTests` 포함).

## ⏳ 사람 Play 확인
- 판 끝 → 로비 복귀 때 패널이 뜨고 값이 결과와 같은가(호스트·클라 각각, "나" 강조가 자기 행인가).
- 닫기 버튼·ESC·180초 자동 닫힘, 열린 동안 이동·공격이 막히고 닫으면 풀리는가. 슬로모션 중에도 180초가 실제 시간인가.
- 첫 부팅·재접속 로비에서는 안 뜨는가(호출 측 Clear 확인).
- 행이 순위 순서인가, 1위만 왕관인가(아무도 피해 0 이면 왕관 없음). 회복량·입은 피해 값이 호스트·클라에서 같은가.
