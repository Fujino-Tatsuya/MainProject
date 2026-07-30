# MapScene 몬스터/보스입장 시스템 — 현황·후속작업 핸드오프

> 2026-07-21 세션 산출물. 작성: Claude(경석 세션). 대상: Codex 및 후속 작업자.
> 관련 계획: `PLAN.md` §"MapScene 몬스터 통합" + §6. 용어: `CONTEXT.md`.

## 1. 이번에 구축된 것 (전부 컴파일 0, 플레이 1차 검증됨)

### 1-1. CC(넉백/경직) — Q스킬 ↔ 몬스터
- `AttackInfo`(Unit/Weapon/BaseAttack.cs) 확장: `knockbackStrength/Duration/staggerDuration/knockbackDirection`(zero=방사형 폴백).
- `MonsterBase`: 지속넉백 수신(`MonsterState.Knockback`, 서버틱 이동+NavMesh 클램프) → 종료 후 `Stunned` 경직. 슈퍼아머 가드.
  - **RangedTurret(PeekABot·TeslaBot) = 넉백 무효, 경직만 적용**(아키타입 판정, 갱신형 스턴락 허용 — 팀장 확정).
- `FirstMeleeMainSkill`(Q): 방향=`heading` 명시(서버 안전. raw AimDirection은 오너 전용이라 사용 금지 — serverAim→heading 경유로 이미 마우스 방향 반영됨). 견인 속도 하한=`AdvanceSpeed`(추월로 놓치는 문제 방지).

### 1-2. 플레이어 버그 수정 (은희 도메인 코어 — 공유 필요)
- `PlayerMovement.MoveRoot`: 캡슐 스윕 클램프 추가 — 평타 러시/Q 전진이 벽 MeshCollider 관통하던 문제. 정적 지오메트리(Default/Ground/Wall/Env)만 막고 유닛은 통과.
- `PlayerAimIndicator`: groundMask 레이 미스 시 플레이어 높이 수평면 폴백 — Ground 레이어 없는 씬(생성맵)에서 조준 고정되던 문제.

### 1-3. 맵 콜라이더/NavMesh
- fbx 임포터: `50.Art/MapGen/MapObj/mesh/{floor,wall}` addColliders=1 + `{floor,wall,object}` isReadable=1 (**SVN 커밋 필요** — §4).
- 언팩 사본 대응: `MapColliderAuthoring`(Editor) — floor/wall/hallway 이름 매칭 MeshCollider 부착(Level_wall_hallway에 16개).
- `MapNavMeshBaker`(+씬 NavMeshBaker GO): MapGenerator.OnGenerated → 런타임 베이크(PhysicsColliders·Default만) → 기스폰 몹 agent 재부착(반경 5m).

### 1-4. 존 몬스터 스폰
- `MapMonsterAuthoring`(Editor, 재실행 안전): 카탈로그 기반 ZoneLayout 전존 저작(11개, Size/Role 일치, 마커 L4/M3/S2/Quest2) + MapGenConfig.MonsterGroups 등록(1=Chomp 2=Humanoid 3=Tesla 4=Mortar 5=Gauntlet).
- `MapContentSpawner.SnapToFloor`: 스폰 마커 바닥 레이캐스트 스냅(허공 마커 → 존 중심 폴백+경고 로그).

### 1-5. 보스 입장 (PLAN §6 개정판)
- `BossEnterTrigger`: BossRoom 역할 존에 서버가 동적 부착(존 프리팹 비네트워크 규약). 존 안 생존자 추적(Enter/Exit+사망 정리, 헛박스 깜빡임 가드).
- `BossTeleportManager`(씬 상주 NetworkObject, **위치=텔레포트 지점**, 현재 (500,1,0)=bossroom 인스턴스): 점유→3·2·1(서버시간 복제, OnGUI 임시) / **전원 이탈·전멸→취소·리셋** / 만료→생존자만 황금각 산개 텔레포트(NT.Teleport+오너 RPC 병행). 본인 화면 페이드(만료 0.3s 전 선제 암전→이동→0.5s 밝아짐, 취소 시 복구).
- `BossEnterZoneVisual`: 패드 테두리 표시(전 피어), 대기/진입 색 전환.
- **튜닝 = 전부 BossTeleportManager 인스펙터**: 패드 크기(6×6m)/대기·진입 색/페이드 시간·색.

## 2. 남은 작업 (우선순위순)

| # | 작업 | 메모 |
|---|---|---|
| 1 | **패드 테두리 y 가림 조치** | 테두리 라인 y≈0.15(존 로컬 0 기준)라 보스룸 입구 오브젝트에 가려 안 보이는 각도 존재. 옵션: y 오프셋 상향(+매니저 인스펙터 노출), 항상 위에 그리는 셰이더(ZTest Always), 또는 DecalProjector 전환. `BossEnterZoneVisual.Setup` 참고. |
| 2 | **전원 텔레포트 멀티 검증(MPPM 2~3인)** | 호스트 단독만 검증됨. 확인: 클라 화면 카운트다운 동기, 클라 생존자 이동(NT 권한 — 서버권한이면 Teleport, 오너권한이면 RPC 경로가 실효인지), 사망자 잔류, 클라 페이드. |
| 3 | **설치형(터렛) 스폰 재확인** | SnapToFloor로 부유 수정했으나 실플레이 재확인 필요. 콘솔 "스폰 마커가 허공" 경고 뜨는 존 = 프리팹 마커 수동 조정 대상(`MonsterSpawnPoints` 자식). |
| 4 | **MortarBot 복귀 후 간헐 Idle 이상 — 상세 조사** | §3 참고. |
| 5 | 캐릭터 누워있는 이슈 | **보류** — 다른 팀원이 확인 중. 건드리지 말 것. |
| 6 | ★ **push 전 MapScene 하네스 제거** | `NetworkManager`(프리팹 인스턴스)+`TestBootStrap` 삭제 필수 — MapScene은 빌드 플로우(index 3)라 잔존 시 정식 로딩 플로우와 NetworkManager 중복 충돌. |

## 3. MortarBot 조사 노트 (간헐 재발 — 팀장 실측)

증상: **복귀(Return) 완료 후 간헐적으로, 과거 해결했던 "Idle로 돌아가는" 증상이 재발**. 재현 간헐적.

조사 우선순위(유력 순):
1. **이번 세션 회귀 가능성**: `MonsterState.Knockback` 신설로 상태 전이가 추가됨 — Return 중/직후 넉백 수신 경로(`TryEnterKnockback`는 Return 가드가 있지만, Knockback 종료(`ExitKnockback`)→Hit→복귀 시퀀스가 Return 문맥을 잃는지 확인. `_stateTimer`/`_returnDest` 잔존값.
2. RangedMobile(Mortar) 특유의 `SeekMobile` 거리 유지 이동이 Return 도달 판정(스폰 지점 근접)과 간섭하는지 — stoppingDistance 복원(`ClearReposition`) 누락 경로.
3. `agent.isOnNavMesh` 일시 false(런타임 베이크/Warp 재부착과의 타이밍) → HandleReturn no-op → 타임아웃성 Idle 강제 전이가 있는지.
4. 재현 로그 확보: MonsterBase 상태 전이에 임시 `Edit.Log(state old→new + 사유)` 심고 MortarBot만 스폰(존 그룹 4) 후 리쉬 왕복 반복.

## 4. 커밋/푸시 가이드 (아직 push 안 함)

- **git**(feature/map-player-merge): 스크립트 전부(1.Scripts/Map·Monster·Player·Unit), 존 프리팹 12개+Level_wall_hallway(마커·콜라이더), MapScene.unity(**하네스 제거 후**), PLAN.md/CONTEXT.md/이 문서.
- **SVN**(팀장 TortoiseSVN): `50.Art/MapGen/MapObj/mesh/{floor,wall,object}` .meta 51개(addColliders/isReadable) + `MapGenConfig.asset`(MonsterGroups). 미커밋 시 팀원 씬에서 콜라이더/몬스터 그룹 없음.
- 커밋 분리 권장: ①CC(넉백/경직)+Q ②플레이어 버그 2건 ③맵 콜라이더/NavMesh/스폰 ④보스 입장 ⑤문서.

## 5. 빠른 참조 — 씬/에셋 배선

- MapScene: `NavMeshBaker`(NavMeshSurface+MapNavMeshBaker), `BossTeleportManager`(500,1,0), `bossroom`(500,0,0), [임시]`NetworkManager`+`TestBootStrap`(playerPrefab=Player, fallback=(0,1,-0.9), 디버그공격 off).
- 에디터 메뉴: `Tools/Map/Authoring/…` — ZoneLayout 저작 / MonsterGroups 등록 / 콜라이더 부착 (전부 재실행 안전).
- Stage1.prefab 슬롯: 보스+스폰 후보 S슬롯 2개, 퀘스트 후보 2개 저작돼 있음.
