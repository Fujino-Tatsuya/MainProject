# 맵 포드 착륙 — 날개 접기 · 자유낙하 (할 일 150, 106 후속) · 2026-10-30

로비 → `4.MapScene` 진입 때 플레이어 포드 착륙 연출(`MapPodArrival`)의 흐름을 바꿨다. 수치 원본은 `GameData.xlsx` Lobby 시트(`PodDropSettings`).

## 흐름 (전 → 후)

| 구간 | 전 | 후 |
|---|---|---|
| 빠른 낙하 | 날개 접힌 채(`DP_WingDeploy` 0프레임) | **날개 펴진 채**(`DP_WingFold` 0프레임 = `DP_WingDeploy` 끝 자세, 클립 실측 일치) |
| 급감속 시작 | `DP_WingDeploy`(펴기) + `DP_GearDeploy` | **`DP_WingFold`(접기)** + `DP_GearDeploy` |
| 연출 높이 대기 | 펴기·다리 끝 이벤트 둘 다 | 접기·다리 끝 이벤트 둘 다(`DeployStates` = 접기·다리) |
| 연출 높이 → 지면 | `landSeconds` 동안 `landEase` 로 천천히 내려앉기 | **추진력 없는 자유낙하** — 높이 h − ½·g·t², 속도 g·t, 낙하 시간 √(2h/g) |
| 착지 순간 | 해치 열기 + 날개 접기 | **해치 열기만**(`OpenStates` = 해치) |
| 디졸브 대기 | 해치·접기 끝 이벤트 둘 다 | 해치 끝 이벤트 |

착지 순간 처리(이펙트·흔들림 → 해치 열기 → 끝 이벤트 → 디졸브 지연 → 디졸브 끝에 조작·실루엣·카메라 해제)는 순서 그대로, 새 착지 시각에 맞춰 돈다.

- 기본값: 연출 높이 5m · g 9.81 → 낙하 ≈1.01초, 착지 속도 ≈9.9m/s. 더 세게 내리꽂히게 하려면 g 를 키운다(20 → 0.71초, 40 → 0.5초).
- 날개·다리 타이밍은 기존 클립 이벤트(`PodClipEvent` — `DP_WingFold` 180f, `DP_GearDeploy` 280f) 그대로. SO 타이머 칸 없음.

## 멀티 동기

서버는 지금처럼 호스트 포드의 접기·다리 끝 이벤트 시각만 `_deployedAt`(서버시간)으로 복제한다. 모든 피어가 같은 `PodDropMapTimeline` 공식(`LandStartAt` = max(이벤트, 연출 높이 도착), `TouchdownAt` = + √(2h/g))으로 높이·착지 시각을 계산한다 — 새 복제 값·RPC 없음.

## 데이터 칸

- 새 칸: `PodDropSettings.freefallGravity`(m/s², 기본 9.81). 에셋엔 아직 없어 코드 기본값으로 읽힌다.
- 안 쓰게 된 칸 `PodDropSettings.landSeconds`·`landEase`·`wingDeployState` 는 2026-10-30 삭제(병합 Export 로 Lobby `map.landSeconds` 행도 제거) — 대체 칸 = `freefallGravity`(착륙)·`wingFoldState`(날개).

## 코드

- 순수 계산: `PodDropTimeline.FreefallHeight/FreefallSpeed/FreefallSeconds/SafeGravity`, `PodDropMapTimeline.FreefallSeconds/TouchdownSpeed` (`Assets/1.Scripts/Lobby/PodDrop/PodDropTimeline.cs`).
- 재생: `MapPodArrival.UpdatePodAnimation`. 시각표: `PodDropScheduleView`. 이벤트 저작: `PodAnimEventAuthoring`(맵 날개 펴기 스펙 뺌 — 클립 이벤트는 그대로 남는다).
- 테스트: `PodDropTimelineTests`(자유낙하 정적·가속·중력·기본 시간표).
