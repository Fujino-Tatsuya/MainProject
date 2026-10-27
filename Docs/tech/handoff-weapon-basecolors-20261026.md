# 무기 베이스컬러 적용 — 2026-10-26

팔라딘 검·방패 v02(낮은 채도의 샴페인 골드), 어쎄신 좌우 단검 v01을 로컬 메인 프로젝트에 적용한다. 게임 로직·네트워크·소켓·무기 Transform은 바꾸지 않는다.

- 아트(SVN): `Assets/50.Art/Char/Weapons/Handpainted_20261026/Models`와 `Textures`. 원본 FBX와 GUID는 보존하고, 실제 스폰 프리팹의 MeshFilter만 새 모델의 메시로 연결한다.
- 프리팹(Git): `Player/Paladin/Player_Paladin.prefab`, `Player/Assassin/Assassin_Armature.prefab`. 어쎄신 플레이어 Variant는 중첩 프리팹 변경을 상속한다.
- 베이스 재질: 팔라딘 기존 검·방패 Variant의 Base Map을 연결하고 tint를 흰색으로 설정. 단검은 `Assassin/FK_Assassin_Dagger_R.mat`, `FK_Assassin_Dagger_L.mat`으로 몸과 분리한다.
- 발광: 기존 효과 재질과 공유 셰이더는 보존한다. `WeaponOverlays/*_UV1.mat`과 `1.Scripts/Effects/Shaders/WeaponOriginalUv1.shader` 복사본이 새 메시의 UV1에 보존된 기존 효과 UV를 읽는다. 기존 슬롯 [1]과 MaterialFadeEffect 참조를 유지한다.
- 저작 도구: FlatKitPlayerVariants는 무기 Base Map을 보존하고 몸 재질 슬롯을 3개로 센다. AssassinShellAuthoring으로 단검을 새로 만들 때 새 메시·재질이 있으면 사용한다.
- 재적용/검증 메뉴: `Tools/Art/Weapon Textures/Apply Handpainted Weapons`, `Validate Applied Weapons`.

텍스처는 각각 2048×2048 sRGB, RGB 한 장이다. 노멀·메탈릭·러프니스 맵은 사용하지 않으며 음영과 하이라이트가 베이스컬러에 들어 있다. 팔라딘 공용 부모 셰이더와 셀 조명 설정은 바꾸지 않는다.

최초 원본 백업과 검사 결과: `D:/p_MT/char/Weapon_Texturing_20261026/MainProject_Integration/`. 저작 메뉴로 새로 생성하는 검사 결과는 각 프로젝트의 `Generated/WeaponTexturing/`에 저장한다.
검증 완료: 교체 전후 무기 정점 위치·기존 발광 UV 최대 오차 0, 모든 삼각형 면 방향 일치. 기존 AssassinVisualPrefabTests의 7개 검사와 실제 플레이어 프리팹의 무기 연결 4개 검사 통과(총 11개). 새 발광 셰이더 오류 0, Unity 스크립트 컴파일 오류 0. 실제 FlatKit 재질을 Unity PreviewRenderUtility로 렌더하여 텍스처 표시도 확인했다. 현재 로비 씬·편집 모드를 유지했으며, 전투 Play/스킬 조작은 사용자가 이어서 확인한다.
공유 아트는 **SVN r527**에 모델·텍스처와 `.meta`를 함께 커밋했다. Git의 프리팹·재질·도구 변경은 `feature/weapon-basecolors-20261026`에서 `development`로 병합하며, `art-svn.json`도 r527로 올린다. r527의 Art 전체 `.meta` 수는 원격 목록 실측 3,627개다.
