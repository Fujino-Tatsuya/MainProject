# 로비 훈련장 (할 일 86)

> 로비(3.LobbyScene) 훈련실에서 하는 TRAIN_01~03 진행. 이 문서는 T8 에서 흐름·단계별 내용으로 확장한다 — 지금은 NavMesh 절만 있다.
> 흐름·출격·접속 승인은 [lobby-demo-flow.md](lobby-demo-flow.md) §1.

## NavMesh 사전 베이크

TRAIN_02 일반 전투 몬스터(ChompBot·HumanoidBot·MortarBot, NavMeshAgent 타입 `Humanoid`)가 걸을 NavMesh 는
**에디터에서 미리 구워 git 에 넣는다.** 로비에는 런타임 베이크 코드가 없다(MapScene 의 `MapNavMeshBaker` 와 별개).

- **오브젝트**: `3.LobbyScene` › `LobbyTraining/TrainingNavMesh` 의 `NavMeshSurface`.
- **범위**: Collect Objects = **Volume**(훈련실만) — 중심 (-16.15, 0.75, 0), 크기 (8.3, 2.5, 16.6) → x -20.3~-12.0, z -8.3~8.3.
  벽(x -20·-12, z ±8)을 걸쳐 넣어 장애물로 깎이게 했고, 동쪽 문 밖 복도는 넣지 않아 몹이 훈련실을 나가지 않는다.
  Agent Type = Humanoid(0), Use Geometry = Physics Colliders, Include Layers = **Ground + Wall**(바닥 = Ground, 벽·소품 = Wall).
  Default 는 뺐다 — 집결 구역 트리거(BoxCollider, Default)가 바닥처럼 구워지지 않게.
- **결과**(2026-10-22): 걷는 면 x -19.33~-12.67, z -7.33~7.33(벽에서 에이전트 반경 0.5 만큼 깎임). 정점 19, 넓이 약 96 m².
  빈 곳은 `Training_Storage` 소품 자리뿐.
- **이 볼륨은 끼임 복구의 훈련실 경계로도 쓴다**(`LobbyTrainingCombat.roomNavMesh`). 볼륨을 바꾸면 경계도 같이 바뀐다.
- **결과 에셋**: `Assets/0.Scenes/MainFlow/3.LobbyScene/NavMesh-TrainingNavMesh.asset`(+ `.meta`, 폴더 `3.LobbyScene.meta`).
  바이너리 에셋이라 **git 에 커밋해야** 다른 PC·빌드에 들어간다. 커밋하지 않으면 몹이 서 있기만 한다.

### 다시 구워야 할 때

로비 아트 중 **훈련실 바닥·벽·소품 배치가 바뀌거나 로비 전체가 회전·이동되면** 다시 굽는다(SVN 아트 교체 포함).
볼륨은 월드 좌표로 고정돼 있어 아트만 옮겨 가면 따라가지 않는다 — 그때는 볼륨 중심·크기도 같이 맞춘다.

1. Play 모드가 아닌 상태에서 `3.LobbyScene` 을 연다.
2. Hierarchy 에서 `LobbyTraining/TrainingNavMesh` 선택 → 인스펙터 `NavMeshSurface` 의 **Bake** 버튼.
   같은 경로(`3.LobbyScene/NavMesh-TrainingNavMesh.asset`)에 다시 만들어지고 씬 참조도 바뀐다. 다른 서피스를 고르지 말 것.
3. Scene 뷰에서 파란 NavMesh 가 훈련실 바닥을 덮는지, `Train02Spawns` Gizmo 의 초록 상자 9개가 그 위에 있는지 본다.
4. 씬 저장 → `NavMesh-TrainingNavMesh.asset` 과 `3.LobbyScene.unity` 를 함께 커밋.
