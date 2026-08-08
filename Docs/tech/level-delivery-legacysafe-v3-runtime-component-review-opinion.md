# LegacySafeV3 런타임 컴포넌트 수정 요청 검토 의견서

작성일: 2026-08-08  
검토 대상: `LevelDelivery_LegacySafeV3_RUNTIME_COMPONENT_REVISION_REQUEST.md`  
검토 범위: 런타임 컴포넌트의 Unity 직렬화 문제와 제안된 파일 분리안  
최종 판정: **수정 요청은 타당하며 승인 권고. 단, 일부 표현과 인계 절차는 보정 필요**

## 1. 결론

현재 `OcclusionAuthoringComponents.cs` 한 파일 안에 다음 세 `MonoBehaviour`가 함께 선언되어 있다.

- `ElevationStack`
- `ElevationLevel`
- `OcclusionSection`

파일명과 일치하는 `MonoBehaviour` 클래스가 하나도 없는 현재 구조는 Unity 컴포넌트 직렬화에 적합하지 않다. 세 타입을 각각 클래스명과 동일한 `.cs` 파일로 분리하는 요청은 문제 원인에 직접 대응하는 최소 수정이며 기술적으로 타당하다.

따라서 다음 분리안은 승인하는 것이 적절하다.

```text
Assets/1.Scripts/Rendering/Occlusion/
├─ ElevationStack.cs
├─ ElevationStack.cs.meta
├─ ElevationLevel.cs
├─ ElevationLevel.cs.meta
├─ OcclusionSection.cs
└─ OcclusionSection.cs.meta
```

다만 이 판단은 런타임 컴포넌트 직렬화 차단 문제에 한정한다. 이 수정만으로 LegacySafeV3 Stage·Zone 프리팹 전체가 기존 반려 조건을 충족했다고 판단할 수는 없다.

## 2. 판단 근거

### 2.1 현재 MainProject 코드 구조

확인한 파일:

`Assets/1.Scripts/Rendering/Occlusion/OcclusionAuthoringComponents.cs`

확인 결과:

- `VeyTrace.Rendering.Occlusion` namespace를 사용한다.
- 56행에 `ElevationStack : MonoBehaviour`가 선언되어 있다.
- 86행에 `ElevationLevel : MonoBehaviour`이 선언되어 있다.
- 257행에 `OcclusionSection : MonoBehaviour`이 선언되어 있다.
- `OcclusionAuthoringComponents`라는 파일명 일치 클래스는 없다.
- 통합 파일의 현재 GUID는 `6b2ac01f279b4ce999b6336430dcd6e1`이다.
- 통합 `.cs`와 `.meta`는 현재 MainProject Git 작업 폴더에서 모두 미추적 상태다.

Unity 공식 문서의 스크립트 명명 규칙에 따르면 하나의 스크립트 파일에 여러 클래스가 있을 경우 Unity는 파일명과 일치하는 클래스를 선택한다. `MonoBehaviour`와 `ScriptableObject`는 특히 클래스명과 파일명을 일치시키는 구조가 요구된다.

참고: <https://docs.unity3d.com/kr/6000.0/Manual/naming-scripts.html>

### 2.2 기존 직렬화 참조 영향

현재 MainProject의 프리팹·씬·에셋에서 통합 스크립트 GUID `6b2ac01f279b4ce999b6336430dcd6e1`을 참조하는 직렬화 데이터를 찾지 못했다.

따라서 현재 시점에는 통합 파일을 제거하고 세 파일로 분리해도 깨질 정상 컴포넌트 참조가 없는 것으로 판단된다. 다만 실제 수정 직전에는 동일한 GUID 검색을 다시 수행해야 한다. 검토 이후 새 프리팹이 저장되었다면 상황이 달라질 수 있기 때문이다.

### 2.3 LegacySafeV3 중간 산출물 상태

레벨 검증 프로젝트에도 동일한 통합 스크립트와 GUID가 복사되어 있다.

확인 경로:

`D:/unity/p_MT/level_project/Assets/level/ValidationRuntimeV3/OcclusionAuthoringComponents.cs`

현재 저장된 V3 Prop Wrapper 59개에는 `OcclusionSection` MonoBehaviour가 정상적으로 직렬화되어 있지 않다. 저장된 Wrapper 자체에는 `m_Script: {fileID: 0}` 컴포넌트가 남은 것이 아니라 해당 런타임 컴포넌트가 빠져 있다.

이는 요청서가 지적한 직렬화 차단 문제와 부합한다. 다만 요청서에 기재된 오류 메시지와 저장 실패 순간의 임시 YAML은 이번 검토에서 Unity Editor 로그로 독립 재현하지 못했으므로, 오류 문구 자체는 제출자의 재현 기록으로 취급한다.

## 3. 타당한 요청 사항

다음 내용은 그대로 유지해도 적절하다.

1. 세 `MonoBehaviour`를 클래스명과 동일한 개별 `.cs` 파일로 분리한다.
2. namespace, public 타입명, 직렬화 필드명과 타입, 공개 메서드 및 런타임 동작을 유지한다.
3. 각 `.cs.meta`가 서로 다른 GUID를 가지게 한다.
4. `.cs`와 대응 `.meta`를 같은 Git 커밋으로 관리한다.
5. 임시 GameObject에 세 컴포넌트를 각각 추가하고 Prefab 저장·재로드를 검증한다.
6. 저장된 Prefab의 `m_Script.fileID`가 0이 아니며 GUID가 해당 `.cs.meta`와 일치하는지 확인한다.
7. 기존 Wall Occlusion EditMode 테스트와 `Register-Wire Selected Prefabs` 저장을 다시 검증한다.
8. 수정 커밋과 확정 GUID를 레벨 제작 담당자에게 전달한다.

`LocalXZArea`는 `[Serializable]` 보조 데이터 타입이므로 반드시 동일명 파일로 분리할 필요는 없다. `ElevationLevel.cs`에 함께 두는 방식이 현재 책임 관계상 충분히 타당하다.

## 4. 요청서 보정 권고

### 4.1 저장 실패 표현

현재 요청서의 다음 표현은 범위가 지나치게 넓다.

> 이 상태의 프리팹은 Missing Script로 판정되어 저장할 수 없다.

다음과 같이 고치는 것이 정확하다.

> 해당 런타임 컴포넌트를 정상 참조한 상태로 프리팹을 저장할 수 없다. 현재 저장된 중간 Wrapper에는 런타임 컴포넌트가 누락되어 있으므로 최종 산출물로 사용할 수 없다.

Wrapper 파일 자체는 생성되어 있기 때문에 “프리팹이 전혀 저장되지 않는다”와 “필요 컴포넌트를 포함한 유효한 프리팹을 저장할 수 없다”를 구분해야 한다.

### 4.2 레벨 프로젝트 전달 방식

레벨 프로젝트에서 세 스크립트나 `.meta`를 별도로 다시 만들거나 GUID만 수동으로 복사해서는 안 된다.

다음 조건을 인계 절차에 명시해야 한다.

- MainProject에서 확정·커밋한 동일한 `.cs/.meta` 파일 쌍을 사용한다.
- 가능하면 해당 Main Git 커밋을 기준으로 검증 환경을 구성한다.
- namespace와 타입명뿐 아니라 에셋 GUID 및 컴파일되는 어셈블리 경계도 동일하게 유지한다.
- GUID만 같고 코드나 어셈블리가 다른 복제본을 별도 관리하지 않는다.

### 4.3 기존 GUID 재검사

현재는 통합 GUID의 직렬화 참조가 발견되지 않았으므로 신규 GUID 세 개를 발급하는 방식이 안전하다. 그러나 실제 파일 분리 직전에 다음 검사를 한 번 더 수행해야 한다.

- 통합 GUID를 참조하는 Prefab·Scene·Asset 유무
- 새로 저장된 V3 또는 테스트 Prefab 유무
- 작업 중인 다른 팀원의 미커밋 프리팹 유무

참조가 새로 생겼다면 단순 삭제 전에 해당 프리팹을 새 스크립트 GUID로 재등록해야 한다.

### 4.4 V3 재생성 범위

V3 Stage·Zone·Wrapper를 처음부터 재생성하는 방법은 보수적이고 안전하며 자동화 재현성을 확인하는 데 유리하다. 다만 기술적으로 반드시 전면 재생성만 가능한 것은 아니다. 현재 Wrapper에 컴포넌트를 다시 등록하여 복구할 수도 있다.

레벨 제작 측 자동 생성 결과를 다시 검증하려는 목적이라면 전면 재생성을 권고한다.

## 5. 권고 검증 조건

수정 승인 시 프로그래머 측 완료 조건은 다음과 같이 잠그는 것이 적절하다.

- [ ] 세 `MonoBehaviour`가 각각 동일명 `.cs` 파일에 존재한다.
- [ ] 각 `.cs.meta` GUID가 서로 다르다.
- [ ] 기존 통합 파일 GUID를 참조하는 에셋이 0개임을 수정 직전에 재확인한다.
- [ ] MainProject가 컴파일 오류 없이 열린다.
- [ ] 세 컴포넌트를 각각 Add Component 할 수 있다.
- [ ] 각 컴포넌트를 포함한 임시 Prefab의 저장과 재로드가 성공한다.
- [ ] 저장 YAML의 `m_Script.fileID`가 0이 아니다.
- [ ] 저장 YAML의 스크립트 GUID가 대응하는 `.cs.meta` GUID와 일치한다.
- [ ] 기존 Wall Occlusion EditMode 테스트가 통과한다.
- [ ] `Register-Wire Selected Prefabs`가 등록된 Prefab을 정상 저장한다.
- [ ] 동일 커밋의 `.cs/.meta` 쌍과 GUID 목록이 레벨 제작 담당자에게 전달된다.

가능하면 임시 Prefab 생성·저장·재로드 검사를 EditMode 회귀 테스트로 남기는 것을 권장한다. 향후 파일 병합이나 이동으로 동일 문제가 다시 발생하는 것을 자동으로 탐지할 수 있다.

## 6. 수정 여부별 영향

### 수정하는 경우

- 세 런타임 컴포넌트가 Unity에서 독립적인 MonoScript로 직렬화될 수 있다.
- Stage·Zone·Prop Wrapper에 실제 컴포넌트를 등록하고 저장하는 검증을 진행할 수 있다.
- 기존 미추적 상태에서 구조를 확정하므로 마이그레이션 부담이 현재는 낮다.

### 수정하지 않는 경우

- LegacySafeV3 제작 과정에서 Stack·Level·Section 런타임 컴포넌트를 신뢰할 수 있게 저장하기 어렵다.
- 현재 중간 Wrapper처럼 필수 컴포넌트가 누락된 산출물이 만들어질 위험이 남는다.
- 등록·검증 도구가 의도한 구조를 생성하더라도 최종 Prefab의 직렬화 무결성을 보장할 수 없다.
- 따라서 V3의 MainProject 반입은 계속 차단하는 것이 안전하다.

## 7. 최종 의견

`ElevationStack`, `ElevationLevel`, `OcclusionSection`을 각각 동일명 파일로 분리하라는 수정 요청은 **원인에 맞는 최소 수정이며 승인할 타당성이 충분하다.**

다만 승인은 다음 의미로 제한해야 한다.

> 런타임 컴포넌트 파일 분리와 GUID 확정 작업을 진행하도록 승인한다. 이 승인은 LegacySafeV3 패키지 전체의 최종 승인이나 기존 반려 사유 해소 판정이 아니다. 분리 완료 후 새 V3 산출물을 대상으로 하이어라키, 층별 Content 분류, XZ Areas, Section, Wrapper, 작명 규칙, 재질·GUID 연결 및 Play Mode·MPPM 동작을 다시 검증한다.

본 의견서는 수정 필요성과 제안안의 타당성만 판단하며, 코드 수정 또는 V3 산출물 변경을 승인 없이 수행하지 않는다.
