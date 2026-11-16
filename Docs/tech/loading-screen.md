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

## 알려진 점
- 팁 문구 키 `UI.Loading.TipDashInterrupt` 의 ko/en 이 `Tip.` 으로 시작한다 — 이제 캡슐이 "Tip" 을 보여 주므로 시트 문구에서 `Tip.` 을 빼는 게 맞다(시트 수정 대기).
- 팁 문구는 서울알림 Medium(정적 아틀라스)을 쓴다 — Heavy(451자 정적)는 임의 문구의 글자가 빠질 수 있어 숫자·"Tip" 에만 쓴다.
