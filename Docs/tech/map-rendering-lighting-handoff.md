# 맵 렌더링·라이팅 인수인계

작성일: 2026-08-13

작업자: 이지원 (`leejiwon`)

작업 브랜치: `leejiwon`

기준 브랜치: `development` (`16ec8ef`)

## 1. 작업 범위

이 브랜치는 맵 아트 프리팹 수정과 전투 맵의 렌더링·라이팅 작업을 한 곳에서 인수인계하기 위한 통합 브랜치다.

- Zone 아트, 계단 충돌 메시, 컨베이어, 무빙 플랫폼 수정
- HDR 컬러 그레이딩과 URP 포스트프로세싱 기준값
- 벽 투명화 범위·디더·AA 설정
- 화면공간 마스크 블러와 외곽 픽셀레이트
- F8 렌더링 성능 HUD와 F9 룩 A/B 비교
- 포그, 층 디밍, 시야 차폐(LoS), 어비스 물안개
- Stage 환경광·주광·스카이박스·Volume Profile
- 12개 Zone 프리팹의 로컬 조명 리그
- Bootstrap에서 실제 검증 장면으로 진입하는 경로

## 2. 정본 장면과 실행 방법

- 정본 전투 맵: `Assets/0.Scenes/MainFlow/4.MapScene-trensparent.unity`
- 실행 시작점: `Assets/0.Scenes/MainFlow/0.BootStrapScene.unity`
- Bootstrap의 `targetSceneName`과 `mainGameSceneName`은 `4.MapScene-trensparent`를 가리킨다.
- `4.MapScene` 단독 Play에서는 런타임 카메라가 준비되지 않아 정상 비교가 어렵다.
- F8: `ProfilerHUD` 표시
- F9: 룩 A/B 전환

`4.MapScene.unity`는 기존 렌더링 실험 이력이 남은 보조 장면이다. 최종 검수와 후속 튜닝은 `4.MapScene-trensparent`에서 진행한다.

## 3. 렌더링 시스템

### 3.1 포스트프로세싱

- 카메라 Post Processing 활성화
- AA는 SMAA를 최종 기준으로 사용
- Tonemapping은 ACES
- DoF는 비활성화하고 배경 디포커스는 마스크 블러로 대체
- 벽 투명화는 `Stage1` 계층을 대상으로 제한
- 벽 디더 애니메이션과 AA 설정을 독립적으로 제어

### 3.2 화면공간 마스크 블러·픽셀레이트

관련 경로:

- `Assets/1.Scripts/Rendering/MaskBlur/`
- `Assets/99.Settings/MaskBlurSettings.asset`
- `Assets/99.Settings/PC_Renderer.asset`

현재 주요 값:

- 선명 영역 크기: `(0.45, 0.28)`
- Roundness: `8.59`
- Feather: `0.495`
- Blur Strength: `1.42`
- Pixel Block Size: `8`
- Pixelate Region Scale: `1.146`
- Pixelate Mode: `OutsideOnly`

픽셀레이트 범위와 블러 범위는 독립적으로 계산한다. `pixelateRegionScale`을 낮추면 픽셀레이트가 플레이어 쪽으로 가까워지고, `size`를 바꾸면 선명 영역과 블러 경계가 함께 바뀐다.

### 3.3 어비스 물안개

어비스 렌더링을 일반 Fog/Dim 활성 여부와 분리했다.

- `FogManager`가 `abyssEnabled`를 별도로 전역 셰이더 값에 전달
- 일반 포그가 꺼져 있어도 어비스 물안개 패스 유지
- `HasActiveInstance`가 어비스 활성 상태까지 검사
- 매니저 비활성화 시 `_AbyssEnabled`가 남지 않도록 초기화

## 4. Stage 라이팅

정본 장면의 현재 기준값:

- Ambient Sky: `(0.471, 0.604, 0.710)`
- Ambient Equator: `(0.314, 0.420, 0.514)`
- Ambient Ground: `(0.149, 0.216, 0.275)`
- Ambient Intensity: `1.15`
- Reflection Intensity: `0.6`
- Directional Light Intensity: `0.3`
- Directional Shadow Strength: `0.8`
- Shadow Bias: `0.035`
- Shadow Normal Bias: `0.28`
- 스카이박스: `Skybox_IndustrialFoundry`

전용 Volume Profile:

- 파일: `Assets/0.Scenes/MainFlow/4.MapScene/VP_CombatLevel_Target.asset`
- Tonemapping: ACES
- Post Exposure: `0.2`
- Contrast: `-8`
- Saturation: `-10`
- White Balance Temperature: `10`
- Bloom Threshold/Intensity: `1.25 / 0.12`
- Chromatic Aberration: `0.015`
- DoF/Vignette: 비활성

## 5. Zone 로컬 라이팅

12개 Zone 프리팹에 `Lighting_Target_CombatLevel` 루트가 추가됐다.

- 전체 라이트: 82개
- Spot Light: 74개
- Point Light: 8개
- 실시간 그림자: 전부 OFF
- Intensity 범위: `1.35 ~ 400`
- Range 범위: `6.6 ~ 32.3`

Zone은 `MapContentSpawner`에서 회전되어 배치되므로 모든 로컬 라이트는 Zone 프리팹 로컬 좌표로 저장했다. 후속 검증 시 모든 `YawSteps` 조합에서 조명 위치와 방향을 확인해야 한다.

대상 프리팹:

- `ZoneL_typeA/B/C`
- `ZoneM_typeA/B/C`
- `ZoneS_typeA`
- `ZoneS_typeBossEnter`
- `ZoneS_typeStart`
- `Zone_typeQuest01/02`
- `bossroom`

URP의 오브젝트당 추가 광원 제한과 겹치는 라이트 수를 고려해 그림자는 껐지만, 1440p/4K와 MPPM GPU 검증은 남아 있다.

## 6. Fog·Dim·LoS 스타일

일반 거리/층 디밍과 벽 뒤 LoS 차폐를 분리했다.

- 일반 디밍: `Dim_Apply`
- LoS 차폐: `Los_Style`
- LoS는 원본 luminance를 보존하면서 밝기·채도·색조를 별도 적용

현재 `FogProfile.asset` 저장값:

- Dim Saturation: `1`
- Dim Brightness: `0.497`
- LoS Darken: `0.814`
- LoS Brightness: `0.201`
- LoS Saturation: `0`
- LoS Tint: `(0, 1, 0.395)`
- LoS Tint Strength: `0.232`

주의: C# 기본값은 청색 계열이지만 실제 ScriptableObject 값은 녹색 계열이다. 런타임에는 ScriptableObject 값이 우선하므로 최종 색상 판단은 `FogProfile.asset` 기준으로 한다.

## 7. 스카이박스와 VCS 의존성

Git 대상 머티리얼:

- `Skybox_AircraftWorkshop.mat`
- `Skybox_IndustrialFoundry.mat`
- `Skybox_IndustrialPipe.mat`

원본 HDRI는 SVN 대상이다.

- `Assets/50.Art/HDRI/aircraft_workshop_01_4k.hdr`
- `Assets/50.Art/HDRI/industrial_workshop_foundry_4k.hdr`
- `Assets/50.Art/HDRI/industrial_pipe_and_valve_01_4k.hdr`

다른 작업자가 브랜치를 받을 때 SVN에서 HDRI와 각 `.meta`를 함께 받아야 한다. 현재 정본 장면은 `IndustrialFoundry`만 직접 참조한다.

## 8. 검증 현황과 남은 작업

기존 작업 중 확인된 항목:

- Unity 컴파일 오류·콘솔 오류 없이 에디터 재생 기록
- 1920×1080 렌더 타깃 확인 기록
- F8/F9 입력과 마스크 블러 패스 확인 기록
- 어비스 셰이더 재임포트 후 렌더 확인 기록

이번 인수인계 커밋에서 별도로 재실행하지 못한 항목:

- MPPM 호스트/클라이언트 동시 확인
- 1440p/4K GPU Frame Time 측정
- 12개 Zone의 모든 `YawSteps` 회전 조합
- 실제 전투·보스 연출 중 추가 광원 겹침
- Reflection Probe Atlas 변경의 최종 유지 여부

## 9. 커밋에서 제외할 생성 파일

- `MainProject-git.slnx`: IDE가 생성한 로컬 솔루션 파일
- 내용 차이가 없는 Addressables/ProjectSettings 파일: 줄바꿈 또는 Unity 재직렬화로 상태만 변경됨

이 파일들은 기능 변경으로 간주하지 않는다.
