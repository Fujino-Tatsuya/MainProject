# 로딩 화면 (2.LoadingScene) — v02 배치 (할 일 290)

시안: `ReC_Loading_UI_Images_20261113_v02/unity_preview_1920.png` (1920×1080).

## 구성 (LoadingCanvas 아래, 뒤 → 앞)
| 오브젝트 | 내용 |
|---|---|
| `Background` | 전체 화면 일러스트(`50.Art/UI/Loading/Loading_1110.png`, 275) |
| `BottomShade` | 아래 410px 검은 그늘 — 글자·막대가 그림 위에서 읽히게 |
| `Brand` | 왼쪽 위 Re:C / 시공간관리국 (2x 이미지를 약 0.53배) |
| `TipCapsule` (+`Radio`·`Label`) | "Tip" 캡슐과 무전기 아이콘 |
| `Text_Tooltip` | 팁 문구(뷰 `tooltipText`) — 서울알림 Medium Bold 28 |
| `Text_Status` | 단계 문구(뷰 `statusText`) — 필름 릴 왼쪽, 작게(22, 알파 0.7) |
| `ProgressTrack` → `FillArea` → `ProgressFill` → `Halftone` | 둥근 진행 막대. 채움은 Sliced 라 뷰가 **`anchorMax.x = 진행률`** 로 오른쪽 끝을 늘린다(Filled 로 자르면 둥근 끝이 잘림). 하프톤은 막대 전체 폭에 고정, 채움의 `RectMask2D` 로 채운 만큼만 보인다 |
| `Reel` | 필름 릴 — `LoadingReelSpin` 이 로딩 중 계속 회전(기본 −180°/초, unscaled) |
| `Text_Percentage` | 큰 숫자 + 작은 % (뷰 `percentSuffix` = `<size=50%> %</size>`) — 서울알림 Heavy 46 |

이미지 원본: `Assets/50.Art/UI/Loading/v02/Loading_*.png` (SVN). 9-slice 테두리(트랙 32·채움 12·캡슐 50)는 텍스처 임포트 설정.

## 다시 배치하기
`Tools/Loading/로딩 화면 v02 배치 (290)` — 텍스처 임포트 설정 + 위 배치를 다시 적용하고 씬을 저장한다(이름으로 찾아 맞춤, 여러 번 돌려도 같음).
씬에서 직접 옮긴 위치는 이 메뉴를 다시 돌리면 메뉴 값으로 돌아간다. 배치 모드: `-executeMethod LoadingScreenLayoutAuthoring.ApplyBatch`.

## 디버그 랩 — `Assets/0.Scenes/Debug/DebugLoadingScreenLab.unity`
빌드 목록에 없는 확인 씬. 씬을 열고 ▶ — 실제 `2.LoadingScene` 을 덧붙여 열고(이미 열려 있으면 그대로) 네트워크 없이 뷰를 구동한다.
- 기본 = 자동 재생(0→100% 6초 + 1.5초 머묾 반복, 단계도 진행률 따라 씬→게임→대기→시작→전원 준비).
- Space 자동 재생 켜기/끄기 · R 처음부터 · ←/→ 진행률 ±5% · 1~5 단계 고정 · H 도움말 창 숨기기(깨끗한 화면 확인). 오른쪽 위 창에 슬라이더·버튼.
- 배치(하프톤 등)는 `2.LoadingScene` 을 고쳐 저장한 뒤 랩에서 ▶. 두 씬을 같이 열어 두고(멀티 씬) ▶ 해도 된다 — Play 중 수정은 사라진다.
- 구동기 `Dev/LoadingScreenLab/DebugLoadingScreenLab.cs`, 씬 다시 만들기 `Tools/Debug/290 로딩 화면 테스트 씬 만들기`.

## 알려진 점
- 팁 문구에는 `Tip.` 을 쓰지 않는다 — 캡슐이 "Tip" 을 보여 준다(290: `UI.Loading.TipDashInterrupt` ko/en·코드 기본값·씬 값에서 뺌, 시트 Strings C78·D78).
- 팁 문구는 서울알림 Medium(정적 아틀라스)을 쓴다 — Heavy(451자 정적)는 임의 문구의 글자가 빠질 수 있어 숫자·"Tip" 에만 쓴다.
