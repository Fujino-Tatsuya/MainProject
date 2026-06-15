# 팀 워크플로우 / 협업 세팅

## 브랜치 전략
- `main`(보호) ← `development` ← `feature/<area>-<요약>`
- 현재 브랜치: `feature/Player`, `feature/Wells&No.23`(보스), `feature/Camera`, `Hotfix`, `development`
- 작은 PR 권장. `development`에 모으고, 안정화 시점에 `main`으로.

## PR 규칙
- **팀장(경석)이 리뷰 후 머지.** 별도 템플릿 파일 없이, PR 설명에 아래만 적으면 충분:
  - 목적/변경 요약, 테스트 방법(멀티면 MPPM 인원), 필요 시 스크린샷/영상
  - 체크: 씬/프리팹 단독 편집 · 네트워크 권한 준수 · `Packages/` 변경 시 manifest+lock 포함 · 대용량 아트는 SVN
- 코어 인터페이스(UnitBase/상태이상/네트워크 권한) 변경은 **사전 합의**.

## Unity 협업 세팅 (필수)
- **Edit > Project Settings > Editor**
  - Asset Serialization = **Force Text**
  - Version Control Mode = **Visible Meta Files**
- **UnityYAMLMerge(Smart Merge)** 를 git에 등록 (씬/프리팹 머지):
  - `.gitattributes` 에 `*.unity merge=unityyamlmerge` / `*.prefab merge=unityyamlmerge` 등
  - `git config merge.unityyamlmerge.driver` 에 Unity의 `UnityYAMLMerge` 실행 경로 지정
- **씬/프리팹 동시편집 금지**: 소유 영역 분리. 공용은 프리팹/프리팹 변형 + 추가(additive) 씬으로 쪼갬.

## Packages 추적
- `Packages/manifest.json` + `packages-lock.json` **커밋 유지**(공유 의존성).
- MCP/NGO/Addressables 추가와 함께 **반드시 커밋** → 팀 전원 동일 패키지 보장.

## 버전 관리 — SVN / Git 하이브리드
- **대용량 아트 + 오디오 = SVN** / **코드·씬·프리팹·소형 에셋 = GitHub**.
- `.meta` 는 **에셋과 같은 VCS** 로 함께(아트 .meta는 SVN). GUID 깨지면 프리팹 참조 전부 깨짐.
- `.gitignore` 에 SVN 관리 아트 폴더 제외 / SVN ignore 에 Git 관리물 제외 → 경계 명확히.

## 소유 영역
| 담당 | 영역 |
|------|------|
| 경석 |  부팅 씬 | Player 2 | 보스 | 
| 은희 | Player 1 | 입력 | 플레이어 프리팹 / Core | Networking |
| 민경 | Boss | 기믹 | 보스 프리팹 (경석 페어링) |

## 도구
- **Unity MCP / CLI** 적극 활용(에디터 자동화). 
