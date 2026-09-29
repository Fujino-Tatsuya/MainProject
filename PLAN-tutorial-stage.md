# PLAN — 튜토리얼 스테이지로 갈아끼우기

> 상태: **구현 완료 · Play 확인 완료(09-29 180° 회전 포함)** (승인 2026-09-29 "이상없으면 작업") · 작업자: 경석(Claude) · 브랜치 `feature/Boss23`

## 확정 (팀장 09-29)
| # | 결정 |
|---|---|
| T1 | 별도 씬을 만들지 않는다. `4.MapScene` 의 `Stage1` 을 튜토리얼로 **갈아끼운다**. Start → 항상 튜토리얼 |
| T2 | 튜토리얼 끝나면 결과 화면 |
| T3 | 보스 타이머(제한시간 → 보스방 강제 이동) **그대로 적용** |
| T4 | 퀘스트 슬롯 없음 |
| T5 | 벽 = **BoxCollider**. 구석(ㄱ자 `corner`)은 ㅡ·ㅣ 방향 **2개**. 콜라이더는 경석이 붙인다(지원 확인) |

## 접근
- `Level_wall_hallway_tutorial.prefab`(지원 저작, 콜라이더 0)에 벽 메시별 BoxCollider 를 **추가 컴포넌트**로 넣는다 — SVN 원본 벽 프리팹 무수정. 레이어는 본맵 벽과 같은 Default(0)(플레이어 장애물 마스크 ∩ NavMesh 베이크 Default+Ground).
- `StageTutorial.prefab` = 튜토리얼 벽 인스턴스 + `Slots` 5개(아트 씬 `all_mesh.unity` 배치 좌표, 회전 0).
  각 슬롯 `FixedPrefab` 고정 + `Rotations` 위치 저작. Start 슬롯만 `IsSpawnCandidate`, BossEnter 슬롯만 `IsBossCandidate` → RNG 무관 결정적.
- `4.MapScene`: 기존 `Stage1` 비활성(이름 `Stage1_Procedural`) + `StageTutorial` 인스턴스를 이름 **`Stage1`**, 벽 자식 **`Level_wall_hallway`** 로 —
  이름으로 찾는 3곳(`MinimapController.cs:110` · `MapOverviewUI.cs:34` · `WallOcclusionDriver.cs:18`)이 코드 수정 없이 동작. `GameObject.Find` 는 비활성을 안 잡는다.
- 저작은 에디터 메뉴로(재실행 가능): `Tools/Map/Authoring/Tutorial/…`

## 리스크
- 구석 메시의 ㄱ 방향 판정(정점 분포) — 메뉴가 판정 결과를 로그로 남긴다.
- 복도 바닥 틈 → 추락. 메뉴가 복도 격자 하향 레이캐스트로 바닥 없는 지점을 로그한다.
- ✅ T2 는 이미 있다 — `BossEncounterDirector.HandleBossDefeated` → 사라짐 +1초(안전망 12초) → `MapSceneManager.GoToResult`. 같은 MapScene 이라 그대로 적용된다. (처음엔 Exit 버튼만 보고 "없다"고 오판)

## 검증
1. 메뉴 로그: 콜라이더 수 · 구석 판정 · 바닥 틈 0.
2. Play: 스폰이 Start 존 · 벽 통과 불가 · 몬스터 스폰 · BossEnter → 보스방 · 미니맵/오버뷰/벽 투명화.

## 결과 (09-29 에디터)
- ① 벽 콜라이더: 곧은 벽 213(1개) + 구석 40(ㅡ·ㅣ 2개) = BoxCollider 293. 구석 판정 40개 전부 동일(메시 로컬 ㅣ=x+ · ㅡ=z−, 정점 34,740 vs 4,686 수준으로 명확). 프리팹 PrefabInstance 256 · 오버라이드 2,864 불변 · guid 불변.
- ② `StageTutorial.prefab`: 슬롯 5(Start 스폰 후보 · BossEnter 보스 후보 · FixedPrefab + Rotations 위치).
- ③ `4.MapScene`: `Stage1_Procedural` 비활성 + `Stage1`(튜토리얼). diff +66/−2 — −1 은 내가 09-28 에 뺀 `defeatResultDelaySeconds` 의 잔존 직렬화 정리(무해). 옛 Stage1 내부를 참조하는 씬 오브젝트 0.
- ④ 바닥 지도(2m 격자): 존 사이 연결 4곳 모두 벽-바닥-벽, 복도 추락 구멍 없음.
- 되돌리기: MapScene 에서 `Stage1`(튜토리얼) 삭제 + `Stage1_Procedural` 활성·이름 `Stage1`.

## 09-29 저녁 — 스테이지 180° 회전 + 은희 투명화 존 반영
- 은희 `fix/stage_tutorial260929`(95063bce 투명화 존 9 · 그룹 9) 머지 — 튜토리얼 벽 프리팹 충돌은 **은희 버전 채택** 후 ①번 메뉴로 콜라이더 재생성.
- **180° 회전**(팀장 09-29: 투명화가 반대로 나와서). `StageYawSteps = 2`, 축 = 벽 인스턴스 위치 (0,0,−160.6). 존 위치·회전은 월드 값이라(`MapContentSpawner` — `TryGetPosition`·`YawSteps`) 슬롯 좌표를 같이 돌리고 `YawSteps = 2`.
- 🔴 SVN **r340 필요**(이지원 r339·r340 `walll_brick_cornerCOM_*` — 구석 40곳이 이 프리팹으로 교체됨). r338 이면 구석 벽이 **빠진 채로** 열린다(missing prefab, 에러 없음). 핀 `art-svn.json` = 340.
- 툴 수정: ① 투명화 존(`WallTransparencyZone`, 기본 큐브 MeshFilter)을 벽에서 제외 — 안 빼면 존 자리가 보이지 않는 실체 벽이 된다. ② 구석 판정을 이름 + **모양**(가로·세로 둘 다 두께 1.5배 초과)으로 — `cornerCOM` 은 곧은 판 2(1×4m) + 기둥 1(1.5×1.5m)이라 ㄱ 자 0개.
- 결과(배치모드): BoxCollider 333(이름만 corner 120 → 박스 1개) + 은희 트리거 9 유지 · 슬롯 5 YawSteps 2 · 바닥 지도 존 자리 5곳이 벽 윤곽과 일치, 복도 4곳 벽-바닥-벽. 회전 후 Start 존 서쪽 벽 없음 = 존 프리팹이 자기 벽을 가짐(회전 전과 같은 모양).
- ✅ Play 확인(팀장 09-29 "문제없음"): 투명화 방향 · 스폰 Start · 런타임 NavMesh(`MapNavMeshBaker` 가 `MapGenerator.OnGenerated` 에서 굽는다) · BossEnter → 보스방.
