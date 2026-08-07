# MapContentSpawner — Zone_typeQuest02 바닥 미발견 (추후 수정 예정)

> 발견 경로: ML-Agents QA 자동화 (`MainProject-MLAgent` 워크트리, `QA/qa-t0`)
> 최초 관측 2026-08-06 · **매 사이클 재현** · 우선순위: 낮음(스폰 누락, 진행 불가 아님)
> 이 문서는 "나중에 고친다"는 기록용이다. 지금 조치는 필요 없다.

## 증상

맵 로딩 중(`2.LoadingScene`) 다음 에러가 **항상 2건** 발생한다.

```
[MapContentSpawner] Zone_typeQuest02(Clone)에서 바닥을 찾지 못해 몬스터를 스폰하지 않습니다((-22.84, 0.10, -39.46))
  — 존이 통로와 어긋난 위치에 배치됐을 가능성이 큽니다(Validate Slot Authoring 확인).
[MapContentSpawner] Zone_typeQuest02(Clone)에서 바닥을 찾지 못해 몬스터를 스폰하지 않습니다((-29.32, 0.10, -39.46))
  — 동일
```

두 좌표만 실패한다. y = 0.10으로 동일하고 x만 6.5 차이 — **같은 존의 인접 슬롯 2개**로 보인다.

## 영향

- 해당 슬롯의 몬스터가 스폰되지 않는다. 그 외 진행에는 영향이 없다(맵 로딩·전투 정상 진행 확인).
- `LogType.Error`로 출력되므로 **QA 자동화가 매 런 Err 2건으로 집계**한다. 24/7 롤업에서 상시 노이즈가 된다.

## 재현

QA 하네스로 `0.BootStrapScene` Play → 맵 로딩 시 100% 재현. 사람이 직접 플레이해도 같은 맵이면 재현된다.

관측된 런 (호스트 기준, 리포트 경로 `C:\Users\user\Documents\QATest\runs`):

| 런 | 커밋 |
|---|---|
| `20260806_145827_e5d7` (iteration 1) | `2b2295a3e` |
| `20260806_174248_7e91` (iteration 3) | `f362ab309` |

## 조치 방향

에러 메시지 자체가 원인과 확인 지점을 알려준다 — **`Validate Slot Authoring`** 확인.
코드 결함이 아니라 **존 배치(오서링) 데이터** 문제로 보인다. `Zone_typeQuest02` 프리팹이
통로와 어긋나게 배치돼 바닥 레이캐스트가 실패하는 것으로 추정된다.

관련 코드: `Assets/1.Scripts/Map/MapContentSpawner.cs`, `Assets/1.Scripts/Map/Editor/` 의 슬롯 오서링 도구.

## QA 쪽 처리

수정 전까지 QA 리포트가 이 에러로 덮이지 않도록, ML-Agents QA에서 **무시 패턴에 넣을지는 보류**한다.
무시하면 실제로 고쳐졌는지 알 수 없게 되므로, 지금은 그대로 잡되 트리아지에서
`deferred`(추후 수정 예정)로 분류한다.
