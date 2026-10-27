# 보스 제압 칸 HUD 이미지 교체 (할 일 113, 2026-10-27)

> BossHealthHUD 는 경석 영역이라 PR 대신 이 문서로 공유한다. 제압 **규칙**(BossCounterProgress·TwentyThreeBoss)은 손대지 않았다.

## 무엇이 바뀌었나
- 칸 하나 = **frame(바닥) + fill(위)** 두 겹. 남은 칸 = fill 켬, 빈 칸 = fill 끔(frame 만 보임).
  예전처럼 회색 이미지 하나에 색(`suppressSegmentEmptyColor`)을 곱하는 방식은 없앴다.
- 이미지: `Assets/50.Art/UI/HUD/gauge_detection_cell_frame.png`·`gauge_detection_cell_fill.png`(64×16).
  두 PNG 가 SVN 에서 **Default 텍스처**(Sprite 아님)로 임포트돼 있어 `Image` 대신 **`RawImage`** 로 그린다(.meta 무수정).
  아트가 Sprite 로 바꾸면 `Image` 로 갈아 끼워도 코드는 그대로 동작한다(필드 타입이 `Graphic`).
- 칸 크기 = **원본 사각형 크기**(이미지 비율 유지). 예전처럼 806px 영역을 칸 수로 나누지 않는다 — 칸 수가 바뀌어도 칸 모양이 찌그러지지 않는다.
  칸 줄은 원본 자리를 가운데로 좌우 대칭으로 놓인다.
- 칸 수 변화·갱신 경로는 그대로(`Update` → `SetSuppressSegments(total, remaining)` → 칸 수가 바뀔 때만 `BuildSuppressSegments`).
- 배치·표시 규칙은 순수 함수 `VeyTrace.UI.BossSuppressCellLayout`(`Assets/1.Scripts/UI/Combat/SuppressCells/`, 별도 asmdef)로 뺐고
  EditMode 테스트 `Assets/Tests/EditMode/BossSuppressCells/` 가 있다.

## 프리팹에서 조절하는 곳 — `Assets/2.Prefabs/UI/BossHealthHUD.prefab`
| 조절 | 위치 |
|---|---|
| 칸 크기 | `BossInfoSlot/SuppressSegmentTemplate` 의 RectTransform **Width·Height**(지금 64×16 = 원본 픽셀). 비율 4:1 을 지키면 안 찌그러진다 |
| 칸 줄 위치 | 같은 오브젝트의 **Pos X·Pos Y**(줄 가운데. 지금 0, -22.5) |
| 칸 사이 간격 | 루트 `BossHealthHUD` 컴포넌트 **Suppress Segment Gap**(px, 지금 8) |
| frame 이미지 | `SuppressSegmentTemplate` 의 RawImage **Texture** |
| fill 이미지 | `SuppressSegmentTemplate/Fill` 의 RawImage **Texture**(부모에 꽉 차게 stretch) |
| 연결 | `BossHealthHUD` 의 **Suppress Segment Template**(= frame RawImage), **Suppress Segment Fill**(= Fill RawImage, 원본의 **직속 자식**이어야 한다) |

- 원본은 꺼 둔 채 둔다(런타임 복제본 `SuppressSegment_0..n` 만 보인다).
- Fill 을 원본 직속 자식이 아닌 곳으로 옮기면 경고 로그 후 frame 만 보인다.

## Play 확인 항목
- 23호 전투에서 칸 3/3 → 2/3 → 0/3 이 오른쪽부터 fill 이 꺼지는지.
- 칸 줄이 HP 바와 겹치지 않는지, 크기·간격이 보기 좋은지(필요하면 위 표의 값만 고치면 된다).
