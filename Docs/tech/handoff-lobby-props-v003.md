# 로비 프랍 v003 엔진 반영 — 2026-10-14

사용자 요청에 따라 v003 UV/베이스컬러 프랍 18종(전화기 독립 버전 v005)을 기존 로비 배치에 65개 배치했다. 기존 건축·포드·플랫폼 1,233렌더러의 위치와 외형 및 로비 루트 Y180도를 유지했다. 기존 v015와 원본 Blender/텍스처는 보존했다.

## 사용 파일

- 씬: `Assets/0.Scenes/Art/lobby.unity`
- 전체 조립: `Assets/2.Prefabs/Environment/Layouts/Lobby/PF_Lobby_Complete_v016.prefab`
- 프랍 배치: `Assets/2.Prefabs/Environment/Layouts/Lobby/PF_Lobby_Props_v003.prefab`
- 검수 씬: `Assets/0.Scenes/Art/Review/Lobby_Props_v003_PlayReview.unity`
- 로컬 검수 갤러리: `D:/p_MT/lobby/aiworkspace/90_조립검수/v016/preview/Lobby_Props_Engine_v016_Review.html`

## 분류

FBX는 `Assets/50.Art/Environment/Models/`의 `Architecture/Doors/Lobby`, `Props/Furniture/Lobby`, `Props/Office/Lobby`, `Machinery/Industrial/Lobby`에 분류했다. 개별 프리팹은 `Assets/2.Prefabs/Environment/`에서 같은 분류를 쓴다. 모델·MAT·PNG와 해당meta는 SVN 영역, 프리팹·씬·Editor 검수 도구와 meta는 Git 영역이다. 커밋·푸시는 하지 않았다.

공유 베이스컬러는 `Assets/50.Art/Environment/Textures/Shared/Atlases/Lobby/lobby_floor_plates_basecolor_v005.png`, 머티리얼은 `Assets/50.Art/Environment/Materials/UsedInMap/Shared/Atlases/MA_generic_Lobby_Props_v003.mat`이다. 4096px/sRGB/DXT1/mipmap/Clamp. 모든 새 프랍은 베이스컬러 한 장만 사용하며 메탈릭·러프니스·노멀 맵은 없다. v016의 기존 로비 아틀라스 슬롯도 같은 재질을 공유한다. Hbeam과 다른 기존 아틀라스는 유지했다.

## 배치와 검증

기존 v015 배치 기록에서 중심·방향을 복원하고 상판 소품은 새 휴먼스케일 크기에 맞췄다. 소품 간 AABB 교차 0. 진단 콘솔 2개 바닥은 Y1.962m(데크보다1mm 위)로 조정했다. 각 콘솔 바닥15지점 지지 확인. 리뷰 씬도 v016 프리팹을 직접 참조한다.

18 FBX 치수/삼각형/UV 정상, 65부품 프리팹/재질 연결 정상, 누락 참조0. 기존 렌더러 경계 변경0. 실제 Paladin·NGO 단일 로컬호스트·기존 게임카메라로 구역별 GameView를 확인하고 지상/플랫폼 접지를 확인했다. 오류/예외0. 플레이어 SoulVisual 누락·kinematic 속도 경고, 검수 씬의 대시검증/추락설정 부재에 따른 경고7건은 보고서에 보존했다.

초기 Table 데이터 모드에서는 기존 `GameData.xlsx / Player!C50 / MultiGameRule.defaultLifeCount` 불일치가 Play를 막았다. 게임 데이터는 수정하지 않고 검수 중에만 기존 Inspector 데이터 모드를 사용했다. 검수 종료 후 Table 모드·Dev Boot 개인 설정·Play 시작 씬·빌드 씬 목록을 복원했다. MPPM·원격클라이언트·문 상호작용·전체 동선/계단 이동·발진 연출·메인 게임 흐름 검증은 이번 시각 검수 범위 밖이다.

재검수는 `Tools/Lobby Props v003/Play User Layout` 메뉴로 실행하고 `Stop Review`로 종료한다. 검수 제어·로그는 위 v016 워크스페이스에 저장한다. 임포트 메뉴는 최초 v015→v016 반영용이므로 완성본에 재실행하지 않는다.


## 2026-10-14 — v004 볼트 색상 수정 반영

최신 `Lobby_Props_LobbyAtlas_v004.blend`의18종을 새 FBX(v004, 전화기v006)로 추가하고 기존 개별 프리팹18개의 MeshFilter/MeshCollider 참조를 갱신했다. 프리팹 GUID와65배치의 위치·회전·크기는 유지했다. 기존 FBX·PNG·MAT는 보존했다. 열린 Art/lobby의 저장 전 조정은 v017/backup에 복사 후 저장했다. 현재 조립 프리팹명 v016은 유지하며, 현재 참조 모델만 최신으로 갱신됐다. Blender의 신규 미분류 선반 편집 오브젝트들은 이번18종 교체에 포함하지 않았다. 실제게임화면·접지·오류0 확인, DevBoot/데이터출처/BuildSettings 복원. 보고서: `D:/p_MT/lobby/aiworkspace/90_조립검수/v017/preview/Lobby_Props_v004_Unity_Review.html`. 커밋·푸시는 하지 않았다.


## 2026-10-14 — v004 최신 사용자 수정본 재반영

SHA ad7e1347인 최신 `Lobby_Props_LobbyAtlas_v004.blend`의18종을 새 FBX(v006, 전화기v008)로 추가하고 기존 개별 프리팹18개의 MeshFilter/MeshCollider 참조를 갱신했다. 프리팹 GUID와65배치의 위치·회전·크기는 유지했다. 기존 FBX·PNG·MAT는 보존했다. 현재 Art/lobby 배치를 백업하고 위치·회전·크기 변경 없이 모델 참조만 갱신했다. 현재 조립 프리팹명 v016은 유지하며, 현재 참조 모델만 최신으로 갱신됐다. Blender의 신규 미분류 선반 편집 오브젝트들은 이번18종 교체에 포함하지 않았다. 실제게임화면·접지·오류0 확인, DevBoot/데이터출처/BuildSettings 복원. 보고서: `D:/p_MT/lobby/aiworkspace/90_조립검수/v019/preview/Lobby_Props_v004_Refreshed_Unity_Review.html`. 커밋·푸시는 하지 않았다.
