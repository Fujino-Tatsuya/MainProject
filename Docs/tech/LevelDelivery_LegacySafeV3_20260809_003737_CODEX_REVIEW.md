# LegacySafeV3 Stage·Zone 패키지 검토 의견서

- 검토일: 2026-08-09
- 검토자: Codex
- 대상 패키지: `LevelDelivery_LegacySafeV3_20260809_003737.zip`
- 대상 인계서: `LevelDelivery_LegacySafeV3_20260809_003737_PROGRAMMER_HANDOFF.md`
- 기준 Main 커밋: `bb35c9283d0194e68416454a0057e92068edec91`

## 1. 최종 의견

이번 LegacySafeV3 패키지는 **파일 무결성, GUID 계약, 프리팹 구조 및 런타임 컴포넌트 직렬화 기준을 충족하므로 Main 검증 작업 폴더에 반입할 후보로는 적절하다.**

그러나 **현재 상태로 기존 Stage·Zone을 교체하거나 배포하는 것은 승인하지 않는다.** 자동 생성된 XZ Area 중 19개 Level이 바닥·경사로 범위가 아닌 전체 Level Collider 범위를 사용하고 있으며, Main에서의 Register-Wire·Validate·Play Mode·MPPM 검증도 아직 완료되지 않았기 때문이다.

따라서 판정은 다음과 같다.

```text
PackageImportSafe: true
StructureAccepted: true
GuidIntegrityAccepted: true
FinalRuntimeApproval: false
Decision: 조건부 반입 승인 / 런타임 최종 승인 보류
```

## 2. 독립 검증 결과

### 2.1 ZIP 및 파일 무결성

- 실제 ZIP SHA-256이 인계서의 `8E1014BF6159CA5A80025A1167FEF4BB5F5464F865AAA9E41B72EF14B8499AF7`과 일치했다.
- ZIP 엔트리는 591개였다.
- 패키지 에셋 294개와 `.meta` 294개가 모두 일대일로 대응했다.
- 누락 `.meta`, 고아 `.meta`, 중복 ZIP 경로 및 위험한 상대 경로는 모두 0건이었다.
- Manifest가 선언한 Payload 파일 588개와 실제 `git-assets`, `svn-assets` 파일 588개가 정확히 일치했다.
- 패키지 내부 중복 GUID와 현재 Main 에셋 GUID와의 충돌은 모두 0건이었다.

### 2.2 Main 런타임 계약

다음 GUID가 현재 Main 커밋의 `.meta`와 일치했다.

```text
ElevationStack.cs:
90be40df8f94dcf43a5d9aab5923965d

ElevationLevel.cs:
5caf0a47863f037419a872370a7d1807

OcclusionSection.cs:
010b78ca8837feb48bc4bc32f6ff5804

VeyTrace.Rendering.Occlusion.asmdef:
b48cf9ebd1f04b2cbe536575d8b12c2c
```

- 패키지는 검증 프로젝트의 런타임 스크립트 복사본을 포함하지 않았다.
- `m_Script: {fileID: 0}`은 0건이었다.
- 이전 통합 스크립트 GUID `6b2ac01f279b4ce999b6336430dcd6e1` 참조는 0건이었다.
- 처음 미해결로 보였던 머티리얼 GUID 2개는 Unity Shader Graph 및 URP 패키지의 정상적인 버전 관리 스크립트 참조로 확인되었다.

### 2.3 Stage·Zone 구조

Stage 1개와 Zone 11개를 실제 Prefab YAML 오브젝트 그래프로 검사했다.

모든 최상위 프리팹이 다음 직속 구조를 지켰다.

```text
PrefabRoot
└─ Occlusion
   └─ ElevationStack_* [ElevationStack]
      └─ Level_* [ElevationLevel]
         └─ Content
            ├─ OccludableProps
            └─ LevelOnlyProps
```

검사 결과:

- `ElevationStack`: 12개
- `ElevationLevel`: 36개
- XZ Area 직렬화 항목: 111개
- Stack이 `Occlusion`의 직속 자식이 아닌 경우: 0건
- Level이 Stack의 직속 자식이 아닌 경우: 0건
- 등록된 `Content`가 Level의 직속 자식이 아닌 경우: 0건
- `OccludableProps` 또는 `LevelOnlyProps` 직속 컨테이너 누락·중복: 0건
- Stack·Level 스케일 `(1,1,1)` 위반: 0건
- Stack·Level X/Z축 회전 위반: 0건
- `LevelOnlyProps` 아래 로컬 `OcclusionSection` 배치: 0건
- 비 ASCII 하이어라키 이름: 0건

### 2.4 Prop Wrapper

공용 Prop Wrapper 112개를 검사했다.

- 투명화 대상 Wrapper 98개는 각각 `OcclusionSection`을 정확히 1개 포함했다.
- LevelOnly Wrapper 14개는 `OcclusionSection`을 포함하지 않았다.
- Wrapper 루트명과 파일명이 일치했다.
- `_LevelOnly_` 이름 분류와 Manifest의 `HasOcclusionSection` 값이 일치했다.
- Wrapper 중첩 및 이름 규칙 위반은 0건이었다.

### 2.5 Renderer·Collider 및 Section 수

- 빌드 보고서는 원본 대비 Renderer 6,220개와 Collider 6,173개의 증감이 0이라고 기록한다.
- 실제 패키지에는 `OcclusionSection` 컴포넌트 블록 512개가 직렬화되어 있었다.
- 보고된 Section 총수 936개와의 차이는 Stage·Zone이 공용 Wrapper 프리팹을 중첩 인스턴스로 재사용하기 때문에 발생하며 구조적으로 타당하다.

Renderer·Collider의 **원본 대비 보존 수치 자체는 이번 검사에서 원본 제작 프로젝트를 다시 빌드해 재현한 값이 아니라 생성 보고서의 결과**다. 패키지 내부의 배열과 참조가 비어 있지 않은 것은 별도로 확인했다.

## 3. 최종 승인을 막는 항목

### 3.1 XZ Area Scene 검토

19개 Level은 바닥·경사로 루트를 찾지 못해 전체 Level Collider 범위를 단일 XZ Area 후보로 사용했다.

특히 다음 Stage 영역은 약 `151.9 × 122.3` 크기의 단일 박스다.

```text
PF_Stage_01_V3 / Level_L02
PF_Stage_01_V3 / Level_B01
PF_Stage_01_V3 / Level_B02
```

이 박스는 실제 이동 가능한 바닥보다 넓을 가능성이 크다. 그대로 사용하면 플레이어가 해당 층 바닥에 있지 않아도 층 영역에 포함되거나, 서로 떨어진 공간과 낙하 구간을 같은 층으로 판정할 수 있다.

전체 Level Collider fallback을 사용한 Level은 다음과 같다.

```text
PF_Stage_01_V3: Level_L02, Level_B01, Level_B02
PF_Zone_L_Type_A_V3: Level_B01, Level_B02
PF_Zone_L_Type_B_V3: Level_B01, Level_B02
PF_Zone_L_Type_C_V3: Level_L02
PF_Zone_M_Type_A_V3: Level_B01, Level_B02
PF_Zone_M_Type_B_V3: Level_B01, Level_B02
PF_Zone_Quest_01_V3: Level_B02
PF_Zone_S_Type_A_V3: Level_B02
PF_Zone_S_Type_Boss_Enter_V3: Level_L01, Level_L02
PF_Zone_S_Type_Start_V3: Level_L02, Level_B01, Level_B02
```

위 19개 Level은 Scene View에서 실제 이동·접지·낙하 가능 영역을 기준으로 검토해야 한다. 필요한 경우 하나의 큰 박스를 여러 로컬 XZ Area로 분할해야 한다.

### 3.2 Main Unity 검증

다음 결과가 실제 Main 검증 작업 폴더에서 아직 기록되지 않았다.

```text
Occludable Wrapper Register-Wire: 미실행
Stage/Zone Register-Wire: 미실행
Stage/Zone Validate: 미실행
Play Mode: 미실행
MPPM Host/Client: 미실행
ZoneLayoutCatalog registration: 미실행
```

이 항목은 정적 YAML 검사로 대체할 수 없다. Unity가 실제 프리팹을 임포트한 후 저장·재로드했을 때 참조가 유지되는지와 런타임 층 판정 및 투명화가 정상인지 확인해야 한다.

## 4. 경미한 개선 권고

검증 프로젝트의 `WallOcclusionDither.shader` 복사본과 현재 Main 셰이더는 줄바꿈 형식 차이 때문에 바이트 SHA-256이 달랐다. 정규화된 텍스트 내용과 `.meta` GUID는 동일하므로 기능 결함이나 반입 차단 사유는 아니다.

다만 이후 패키징 도구는 다음 중 하나를 적용하는 것이 좋다.

- 텍스트 파일의 줄바꿈을 정규화한 뒤 해시를 계산한다.
- Source 해시와 Main Target 해시를 각각 Manifest에 기록한다.
- 바이트 해시 불일치와 의미상 텍스트 불일치를 구분해 보고한다.

## 5. 권장 반입 및 검증 순서

1. 현재 dirty Main이 아닌 별도의 깨끗한 검증 작업 폴더를 준비한다.
2. 기준 Main 커밋과 최신 SVN 상태를 확인한다.
3. `svn-assets`의 에셋과 `.meta`를 먼저 동일 경로에 반입한다.
4. SVN 추가·변경 목록과 GUID를 확인한다.
5. `git-assets`의 프리팹과 `.meta`를 동일 경로에 반입한다.
6. Unity 임포트 후 Missing Script·Missing Material·Shader 오류를 확인한다.
7. 19개 fallback Level을 포함한 XZ Area 111개를 Scene View에서 검토한다.
8. 투명화 대상 Wrapper 98개만 Register-Wire한다. LevelOnly Wrapper 14개는 제외한다.
9. Stage·Zone 최상위 프리팹 12개를 Register-Wire한다.
10. Stage·Zone 12개를 Validate하고 `errors=0`을 확인한다.
11. Play Mode에서 층 판정, 낙하, 카메라 가림 및 복귀를 검증한다.
12. MPPM Host/Client에서 시각 결과와 네트워크 부작용 여부를 확인한다.
13. 수동 승인 후 ZoneLayoutCatalog를 연결한다.

## 6. 승인 범위

이번 의견서가 승인하는 것은 **격리된 Main 검증 작업 폴더로의 조건부 반입**까지다.

다음 작업은 아직 승인 범위에 포함하지 않는다.

- 기존 Stage·Zone 교체
- 실제 게임 씬 및 ZoneLayoutCatalog 연결
- SVN/Git 배포 또는 PR 제출
- XZ Area 수동 검토를 생략한 최종 승인
- Play Mode 및 MPPM 검증을 생략한 런타임 승인

## 7. 결론

인계서의 핵심 주장인 `PackageImportSafe: true`는 타당하다. V2에서 문제가 되었던 GUID 충돌과 런타임 컴포넌트 누락은 이번 V3에서 해결되었고, 프리팹 구조도 현재 Main 고도·투명화 시스템의 계약과 맞는다.

반면 `FinalRuntimeApproval: false` 역시 타당하다. 특히 19개 fallback XZ Area는 실제 게임 판정을 바꿀 수 있는 데이터이므로 반드시 Scene 검토를 거쳐야 한다.

**종합 의견: 패키지는 반려 대상이 아니라 조건부 반입 승인 대상이며, XZ Area 수정과 Main Unity 검증 결과가 모두 통과된 이후에만 최종 교체·배포를 승인한다.**
