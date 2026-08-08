# Stage·Zone 프리팹 GUID/Meta 안전 반입 매뉴얼

> 대상: 새 Stage·Zone 프리팹을 `MainProject-git`에 추가하거나 기존 프리팹을 갱신하는 작업자  
> 기준 엔진: Unity `6000.3.16f1`  
> 핵심 원칙: **에셋과 그 에셋의 `.meta`는 한 쌍이며, 반드시 같은 VCS에서 함께 관리한다.**

## 1. 먼저 알아야 할 것

Unity의 에셋 참조는 파일 이름이나 폴더 경로가 아니라 주로 `.meta` 안의 `guid`로 연결된다.
프리팹·씬 YAML의 참조는 보통 다음 두 값으로 구성된다.

- `guid`: 어떤 에셋인지 식별한다.
- `fileID`: 그 에셋 안의 어떤 GameObject·Component·Mesh 같은 하위 오브젝트인지 식별한다.

따라서 다음 두 종류의 파손을 구분해야 한다.

1. `.meta`를 삭제하거나 새로 만들면 GUID가 바뀌어 외부 참조가 끊긴다.
2. GUID를 유지해도 프리팹 내부 구조를 통째로 재생성하면 내부 `fileID`가 바뀌어 씬의 Prefab Override 등이 끊길 수 있다.

파일 이름 변경이나 경로 이동 자체는 GUID를 바꾸지 않는다. 단, **Unity Project 창 안에서 이동·이름 변경**하거나 에셋과 `.meta`를 반드시 함께 옮겨야 한다.

## 2. 이 프로젝트의 VCS 경계

| 대상 | 저장 위치 | 관리 VCS | 같이 관리할 파일 |
|---|---|---|---|
| Stage 프리팹 | `Assets/2.Prefabs/Map/` | Git | `.prefab` + `.prefab.meta` |
| Zone 프리팹 | `Assets/2.Prefabs/Map/Zoneprefab/` | Git | `.prefab` + `.prefab.meta` |
| 씬 | `Assets/0.Scenes/` | Git | `.unity` + `.unity.meta` |
| 코드·소형 Unity 에셋 | Git 관리 폴더 | Git | 에셋 + `.meta` |
| FBX·텍스처 등 대용량 아트 | `Assets/50.Art/` | SVN | 원본 에셋 + `.meta` |
| Zone 카탈로그 | `Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset` | SVN | `.asset` + `.asset.meta` |

`Assets/50.Art/`는 Git에서 제외되어 있으며 실제 SVN 작업 사본이다. Zone 프리팹은 Git 에셋이지만, 그 프리팹을 참조하는 `ZoneLayoutCatalog.asset`은 SVN 에셋이라는 점에 특히 주의한다.

새 Stage·Zone 프리팹을 단지 아트 에셋이라는 이유로 `Assets/50.Art/`에 만들지 않는다. 현재 프로젝트 규칙대로 **완성된 Unity 프리팹은 Git 경로**, 프리팹이 참조하는 대용량 원본 아트만 SVN 경로에 둔다.

## 3. 작업 시작 전 체크

1. Unity Hub에서 반드시 `6000.3.16f1`로 연다.
2. Git 최신 변경과 `Assets/50.Art`의 SVN 최신 변경을 모두 먼저 받는다.
3. Git 작업 폴더와 SVN 작업 폴더에 예상하지 못한 수정·충돌이 없는지 확인한다.
4. 다른 사람이 `4.MapScene`, `Stage1.prefab`, `ZoneLayoutCatalog.asset`을 동시에 수정 중이지 않은지 확인한다.
5. Unity 설정을 확인한다.
   - Asset Serialization: `Force Text`
   - Version Control Mode: `Visible Meta Files`
6. 기존 파일을 교체하는 작업이면 먼저 현재 `.meta`를 VCS에서 복구할 수 있는 상태인지 확인한다.

공용 씬, Stage 프리팹, Zone 카탈로그는 충돌 비용이 크므로 한 명이 맡아 순서대로 반영하는 것을 권장한다.

## 4. 상황별 안전한 반입 방법

### 4.1 이 프로젝트에서 완전히 새 프리팹 만들기

가장 안전한 방식이다.

1. 필요한 FBX·텍스처 등 원본 아트를 `Assets/50.Art/`의 정해진 SVN 폴더에 넣는다.
2. Unity가 원본 아트의 `.meta`를 생성하도록 한 뒤 원본과 `.meta`를 함께 SVN 추가 대상으로 잡는다.
3. Unity Project 창에서 새 프리팹을 만든다.
4. Zone은 `Assets/2.Prefabs/Map/Zoneprefab/`, Stage는 `Assets/2.Prefabs/Map/`에 저장한다.
5. Unity가 새 프리팹에 새 GUID를 발급하도록 둔다.
6. 생성된 `.prefab`과 `.prefab.meta`를 함께 Git 추가 대상으로 잡는다.

새 프리팹에 기존 프리팹의 `.meta`를 복사해서는 안 된다. 새 에셋은 새 GUID를 가져야 한다.

### 4.2 이 프로젝트의 기존 프리팹을 복제해 새 프리팹 만들기

1. OS 파일 탐색기가 아니라 Unity Project 창에서 `Duplicate`한다.
2. 복제본의 이름과 저장 위치를 정한다.
3. Unity가 복제본에 새 GUID를 발급했는지 `.meta` 생성 여부로 확인한다.
4. 복제본의 Inspector 참조와 Prefab Variant 관계가 의도와 맞는지 확인한다.

복제본이 별도 에셋이라면 원본과 GUID가 달라야 정상이다.

### 4.3 다른 Unity 프로젝트에서 가져오기

외부 프로젝트의 프리팹만 복사하면 머티리얼·메시·텍스처·스크립트 참조가 빠질 수 있다. 반드시 의존성을 한 묶음으로 다룬다.

권장 절차:

1. 원본과 현재 프로젝트가 같은 Unity 버전인지 확인한다.
2. 외부 원본 프로젝트에서 프리팹과 `Dependencies`를 포함해 내보낸다.
3. 바로 MainProject에 덮어쓰지 말고 임시 Unity 프로젝트에서 먼저 열어 Missing 참조가 없는지 확인한다.
4. 반입할 모든 에셋의 `.meta` GUID가 MainProject의 다른 에셋 GUID와 충돌하지 않는지 확인한다.
5. 충돌이 없을 때만 에셋과 `.meta`를 함께 반입한다.
6. GUID가 같지만 실제 내용이 다른 에셋이 하나라도 있으면 반입을 중단한다.
7. 충돌 에셋은 임시 프로젝트의 Unity Project 창에서 복제해 새 GUID를 발급받고, 프리팹 참조를 그 복제본으로 다시 연결한 다음 재반입한다.

GUID 문자열을 텍스트 편집기로 임의 변경해 충돌을 해결하지 않는다. GUID만 바꾸고 프리팹 내부 참조를 함께 갱신하지 않으면 더 찾기 어려운 파손이 생긴다.

외부 에셋이 기존 MainProject의 머티리얼·텍스처와 같은 자산이라면 중복 반입하지 말고, 임시 프로젝트 또는 MainProject의 Inspector에서 **기존 MainProject 에셋으로 다시 연결**한다.

### 4.4 기존 프리팹을 업데이트하면서 GUID 유지하기

기존 프리팹을 참조하는 씬과 카탈로그를 유지하려면 새 파일로 교체하지 말고 기존 프리팹을 수정한다.

1. 기존 `.prefab`과 `.meta`를 삭제하지 않는다.
2. Prefab Mode에서 기존 프리팹을 열어 하이어라키와 컴포넌트를 수정한다.
3. 기존 씬 인스턴스에 Override가 걸린 GameObject·Component는 가능하면 삭제 후 재생성하지 않는다.
4. 루트, `Slots`, `ZoneSlot`처럼 씬 데이터가 연결되는 오브젝트를 통째로 재생성하지 않는다.
5. 저장 후 씬의 Prefab Override와 Inspector 참조를 확인한다.

FBX를 새 버전으로 갱신할 때도 기존 FBX의 `.meta`는 유지하고 같은 경로의 원본 파일 내용만 갱신한다. 다만 FBX 내부 노드·메시 이름이나 구조가 바뀌면 하위 `fileID`가 달라질 수 있으므로 MeshFilter, Collider, 애니메이션 참조까지 다시 확인한다.

### 4.5 이동·이름 변경

- Unity Project 창에서 이동·이름 변경한다.
- OS에서 처리해야 한다면 에셋과 `.meta`를 한 쌍으로 같은 작업에서 이동한다.
- 이동 후 참조가 유지되는지 Unity에서 확인한다.
- `git mv` 또는 SVN Move로 기록되는 것이 이상적이지만, 최종 기준은 에셋과 기존 `.meta`가 함께 이동했는지다.

## 5. 새 Zone 프리팹 연결 순서

### 5.1 프리팹 자체 구성

1. 신규/전환 프리팹은 프로젝트 작명 규칙에 맞춰 `PF_Zone_*` 루트 이름을 사용한다.
2. 루트에 `ZoneLayout` 컴포넌트를 두고 다음 값을 지정한다.
   - `Size`
   - `Role`
   - `Difficulty`
   - 필요한 Monster Spawn Marker와 `Nodes`
3. 가림 투명화 구조는 아래 문서를 따라 구성한다.
   - `Docs/tech/occlusion-prefab-authoring-manual.md`
   - `Docs/tech/occlusion-prefab-naming-rules.md`
4. 원본 프랍 프리팹을 먼저 선택해 다음 메뉴를 실행한다.
   - `Tools/Rendering/Wall Occlusion/Register-Wire Selected Prefabs`
   - `Tools/Rendering/Wall Occlusion/Validate Selected Prefabs`
5. 그 프랍을 포함한 Zone 프리팹도 같은 순서로 등록·검증한다.

### 5.2 ZoneLayoutCatalog에 등록

현재 카탈로그 경로:

`Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset`

1. Inspector에서 `Entries`에 항목을 추가한다.
2. `Prefab` 필드에는 Project 창의 새 Zone 프리팹을 드래그해 넣는다.
3. `Size`, `Role`, `Difficulty`를 `ZoneLayout` 값과 동일하게 맞춘다.
4. YAML에 GUID나 fileID를 직접 입력하지 않는다.
5. 저장 후 이 카탈로그 변경은 SVN 수정으로 잡혀야 한다.

### 5.3 슬롯별 배치값 작성

새 Zone이 카탈로그에 들어가면 그 Zone을 선택할 수 있는 각 `ZoneSlot`마다 회전·위치 데이터가 필요할 수 있다.

1. `Assets/0.Scenes/MainFlow/4.MapScene.unity`를 연다.
2. `Tools/MapGen/Zone Rotation Authoring`에서 새 Zone과 도달 가능한 Slot 조합을 확인한다.
3. 각 조합을 Spawn하고 위치·90도 단위 회전을 맞춘 뒤 저장한다.
4. 생성 결과를 이용해 일괄 기록할 때만 `Tools/MapGen/Save Placements (clones -> slots)`를 사용한다.
5. `Tools/Map/Authoring/Validate Slot Authoring`을 실행한다.
6. Missing 조합이 0인지 확인하고 `4.MapScene`을 저장한다.

중요: 슬롯 배치 데이터의 정본은 `Stage1.prefab` 자체가 아니라 **열린 `4.MapScene` 안의 Stage1 인스턴스**다. 슬롯 배치값을 Stage1 프리팹에 무심코 Apply하지 않는다.

## 6. 새 Stage 프리팹 연결 순서

Stage 프리팹도 Zone과 동일하게 Git 경로에 저장하고, 프리팹과 `.meta`를 함께 관리한다. 가림 투명화용 하이어라키와 검증 규칙도 Zone과 동일하다.

다만 현재 프로젝트에는 다음과 같은 Stage1 고정 가정이 남아 있다.

- 활성 스테이지 루트 이름: `Stage1`
- 슬롯 루트: `Stage1/Slots`
- 공용 복도 참조: `Stage1/Level_wall_hallway`
- 미니맵·맵 오버뷰·일부 에디터 도구가 위 이름 또는 경로를 직접 찾는다.

따라서 새 Stage 프리팹 파일을 폴더에 추가하는 것만으로 런타임에서 새 Stage가 자동 사용되지는 않는다.

### 기존 Stage1을 갱신하는 경우

1. 가능하면 기존 `Stage1.prefab`을 Prefab Mode에서 수정해 GUID를 유지한다.
2. `Slots`와 각 `ZoneSlot`의 기존 오브젝트·컴포넌트를 보존한다.
3. `4.MapScene`의 Stage1 인스턴스 Override를 확인한다.
4. `Level_wall_hallway` 경로를 유지하거나, 이름을 바꾼다면 그 경로를 직접 찾는 코드도 별도 변경 대상으로 합의한다.
5. 슬롯 배치 검증을 다시 실행한다.

### 별도의 새 Stage를 추가하는 경우

1. 새 프리팹은 별도 GUID를 가진 Git 에셋으로 만든다.
2. 활성 씬에서 어떤 Stage를 선택·생성할지 먼저 설계한다.
3. `Stage1`과 `Level_wall_hallway` 하드코딩 제거, 슬롯 소유권, 맵 생성기 연결, 네트워크 스테이지 진행도 연동을 별도 코드 작업으로 처리한다.
4. 코드 작업 전에는 새 Stage를 “에셋 추가 완료, 런타임 미연결” 상태로 명확히 표시한다.

Stage 교체는 씬·프리팹·코드가 함께 바뀔 수 있으므로 단순 아트 반입 작업으로 간주하지 않는다.

## 7. 현재 실행하면 안 되는 자동 연결 메뉴

**다음 메뉴는 현 상태에서 실행하지 않는다.**

`Tools/MapGen/Wire Slots + Catalog + Refs`

현재 `ZoneWiring.cs`는 Zone 프리팹을 다음의 옛 경로에서 찾는다.

`Assets/50.Art/MapGen/MapObj/Zoneprefab`

하지만 실제 Zone 프리팹 경로는 다음이다.

`Assets/2.Prefabs/Map/Zoneprefab`

도구는 `ZoneLayoutCatalog.Entries`를 먼저 비운 뒤 하드코딩된 목록과 옛 경로로 다시 채운다. 따라서 현재 실행하면 프리팹을 찾지 못해 SVN 카탈로그가 비거나, 신규 Zone이 누락될 수 있다. 이 도구가 실제 경로와 신규 카탈로그 정책에 맞게 수정·검증되기 전까지는 5장의 Inspector 수동 등록 절차를 사용한다.

## 8. 반입 후 필수 검증

### 8.1 파일·VCS 검증

- 새 Git 에셋마다 같은 위치에 `.meta`가 있다.
- 새 SVN 에셋마다 같은 위치에 `.meta`가 있다.
- 에셋만 추가되고 `.meta`가 빠진 항목이 없다.
- `.meta`만 남고 본체가 사라진 항목이 없다.
- `Assets/50.Art` 변경은 Git이 아니라 TortoiseSVN의 `Check for modifications`에서 확인한다.
- Git 상태에는 Stage·Zone `.prefab`과 `.prefab.meta`, 필요한 씬 변경이 함께 보인다.
- 기존 에셋의 `.meta`가 이유 없이 수정·삭제된 항목이 없다.
- 머지 충돌 표시가 `.prefab`, `.unity`, `.meta` 안에 남아 있지 않다.

### 8.2 Unity 참조 검증

- Console의 Missing Script, Missing Prefab, Missing Material, Missing Mesh 오류가 없다.
- Prefab Mode에서 모든 Inspector Object 필드가 올바른 MainProject 에셋을 가리킨다.
- 새 Zone의 `ZoneLayout` 분류값과 카탈로그 항목이 일치한다.
- 모든 도달 가능한 Slot 조합의 회전·위치 작성이 완료됐다.
- `Tools/Map/Authoring/Validate Slot Authoring` 결과에 Missing 조합이 없다.
- `Tools/Rendering/Wall Occlusion/Validate Selected Prefabs` 결과를 확인한다.
- 여러 랜덤 Seed로 맵 생성을 반복해 Zone의 위치·회전·연결을 확인한다.
- Stage를 변경했다면 미니맵, 맵 오버뷰, 복도 표시까지 확인한다.
- 최종적으로 MPPM Host/Client에서 같은 Zone·Stage가 생성되는지 확인한다.

### 8.3 다른 작업 환경 검증

가능하면 작업자 로컬 캐시가 없는 다른 팀원 환경에서 다음 순서로 확인한다.

1. 필요한 SVN 리비전을 먼저 Update한다.
2. Git 브랜치를 받는다.
3. Unity를 열어 전체 Import가 끝날 때까지 기다린다.
4. Missing 참조와 Console 오류를 확인한다.
5. MapScene 생성 테스트를 실행한다.

로컬에서는 `Library` 캐시 때문에 우연히 보이는 에셋이 다른 팀원에게는 없을 수 있다. 새 환경 검증이 GUID·의존성 누락을 가장 확실히 찾는다.

## 9. 제출 순서

Git 프리팹이 SVN 아트 에셋을 참조한다면 다음 순서를 지킨다.

1. 원본 아트와 `.meta`, 필요한 `ZoneLayoutCatalog.asset` 변경을 SVN에 먼저 제출한다.
2. SVN 리비전 번호를 기록한다.
3. Stage·Zone 프리팹과 `.meta`, 필요한 씬 변경을 Git PR에 올린다.
4. PR 설명에 먼저 받아야 하는 SVN 리비전을 적는다.

이 순서면 Git 변경을 받은 팀원이 존재하지 않는 SVN GUID를 참조하는 시간을 줄일 수 있다. 카탈로그와 Git 프리팹을 동시에 맞춰야 할 때는 팀원에게 “SVN 리비전 → Git 브랜치” 적용 순서를 명확히 공지한다.

## 10. 금지 사항

- 기존 `.meta` 삭제 후 Unity가 새로 만들게 하기
- 새 에셋에 다른 에셋의 `.meta` 복사하기
- `.meta`의 GUID를 텍스트로 직접 수정하기
- 프리팹·씬 YAML의 `guid` 또는 `fileID`를 손으로 붙여 넣기
- 에셋만 이동하고 `.meta`를 원래 위치에 남기기
- 외부 프로젝트의 프리팹만 복사하고 의존성·`.meta`를 빼먹기
- `Assets/50.Art` 에셋을 Git에 억지로 추가하기
- 카탈로그 항목을 만들기 위해 현재의 `Wire Slots + Catalog + Refs` 실행하기
- `4.MapScene` 슬롯 배치 Override를 검토하지 않고 Stage1에 Apply하기

## 11. GUID/Meta 사고 복구

### 기존 `.meta`를 잃어버렸을 때

1. 즉시 Unity 작업과 저장을 멈춘다.
2. 가능하면 Unity를 닫아 새 `.meta` 생성과 재직렬화를 막는다.
3. Git 또는 SVN 이력에서 **그 에셋의 원래 `.meta`**를 복구한다.
4. 에셋과 원래 `.meta`를 같은 위치에 둔다.
5. Unity를 다시 열고 Reimport 후 참조를 확인한다.

Unity가 이미 새 `.meta`를 만들었더라도 그 GUID를 기준으로 참조를 고치기 시작하지 않는다. 새 `.meta`를 제거하고 VCS의 원래 `.meta`를 복구하는 것이 우선이다.

### GUID 충돌을 발견했을 때

1. MainProject의 기존 에셋과 `.meta`는 수정하지 않는다.
2. 충돌하는 반입 에셋 묶음을 제거하고 Unity를 닫는다.
3. 임시 프로젝트에서 충돌 에셋을 Unity Project 창으로 복제해 새 GUID를 받는다.
4. 외부 프리팹이 그 새 에셋을 참조하도록 다시 연결한다.
5. 의존성 묶음을 재검증한 뒤 다시 반입한다.

### Inspector에 Missing 참조가 생겼을 때

1. Git/SVN 변경 내역에서 삭제·이동·`.meta` 변경을 먼저 찾는다.
2. 원래 에셋과 `.meta`를 복구할 수 있으면 복구한다.
3. 원본이 실제로 제거된 경우에만 Inspector Object 필드에서 대체 에셋을 명시적으로 다시 지정한다.
4. YAML 텍스트 치환으로 대량 수정하지 않는다.

## 12. 최종 체크리스트

- [ ] Unity `6000.3.16f1` 사용
- [ ] Git과 SVN 모두 최신 상태에서 시작
- [ ] Stage·Zone 프리팹은 Git 경로에 저장
- [ ] 대용량 원본 아트는 SVN 경로에 저장
- [ ] 모든 신규 에셋과 `.meta`가 같은 VCS에 한 쌍으로 등록
- [ ] 기존 에셋 갱신 시 기존 `.meta`와 GUID 유지
- [ ] 외부 반입 에셋의 GUID 충돌과 의존성 확인
- [ ] ZoneLayout과 ZoneLayoutCatalog 값 일치
- [ ] Zone 카탈로그는 Inspector Object 필드로 등록
- [ ] `Wire Slots + Catalog + Refs` 실행하지 않음
- [ ] 4.MapScene의 모든 Slot 배치 조합 작성·검증
- [ ] 가림 투명화 등록·검증 완료
- [ ] Missing 참조 및 Console 오류 없음
- [ ] 여러 Seed와 MPPM Host/Client 테스트 완료
- [ ] SVN 리비전을 먼저 제출하고 Git PR에 해당 리비전 명시

