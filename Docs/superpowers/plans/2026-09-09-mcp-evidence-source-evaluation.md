# MCP 근거·현재 소스·프리팹·작업 평가 확장

승인: 사용자가 후속 제안 1, 2, 3의 추가 구현과 검증 후 push·프로젝트 적용을 지시했다. 기존 포크 후보 f16c698에서 계속한다.
게임 PLAN.md와 gameplay 변경은 다른 작업이므로 보존한다.

## 설계 결정

현재의 Node 인덱스와 Unity 도구를 확장한다. 독립 DB 서비스 전면 교체는 운영 부담이 크고, 모든 질의를 Editor 안에서 처리하면 컴파일 중 조회가 막힌다. Node 오프라인 조회 + 필요한 Unity API 조회를 유지한다.

1. **근거 계약:** 로컬 도구 성공/오류에 evidence 객체를 공통 부착한다. 관측 시각, index build/cache, assemblies 메타데이터 서명과 검사 시각, assets 검사 간격·coverage, source와 assembly 일치 여부 unknown을 구분한다. Unity 응답에는 editor 관측 근거, source AST에는 콘텐츠 해시·define 조건을 붙인다. 모르는 정보를 current/verified라고 만들지 않는다. 비용은 매 응답 전체 재스캔 없이 기존 검증 결과를 활용한다.
2. **현재 소스:** 신규 `unity_get_source_declarations`는 파일 단위 Roslyn syntax AST를 비동기 child로 읽는다. nested/partial/필드/메서드/속성·줄 범위, 활성 define 조건·구문 오류를 제공한다. IL 호출 관계와 정적 의미 해석을 사칭하지 않는다. Unity 설치의 Roslyn/.NET runtime으로 helper를 profile cache에 빌드하고 소스·defines hash로 캐시한다. 타임아웃/출력 상한/경로 제한/컴파일러 부재 오류가 필요하다. 저장하지 않은 Editor 버퍼는 대상이 아니다.
3. **Prefab 최종 값:** 신규 `unity_inspect_prefab_values`는 assetPath 또는 scene instance ID를 받아 SerializedObject로 실제 저장/현재 값을 읽고 prefab source chain과 property override 근거를 붙인다. propertyPath/타깃/컴포넌트 필터·pagination을 지원한다. Prefab을 씬에 생성하거나 save/apply/revert하지 않는다. 불완전한 유형/무효 override는 명시한다.
4. **작업 평가:** 개발 평가와 holdout을 분리한 작업 정의·정답/최종 상태 grader·trace 기록기를 추가한다. 회귀시험, 도구를 정해진 순서로 호출한 시험, 실제 모델이 선택한 작업 실행을 별도 집계한다. 현재 사용 가능한 에이전트로 실제 trial을 수행하고, 입력·도구 응답·최종 답변의 공개 tokenizer 비용만 보고한다. 청구/숨은 reasoning 토큰은 측정하지 못했음을 표시한다. 모델이나 비용 한도를 임의로 설정해 외부 유료 API를 호출하지 않는다.

## 순서와 파일 경계

- source AST helper/adapter/tests, Prefab C# tool/tests, 평가 harness/fixtures는 독립 구현한다.
- root는 evidence wrapper와 tools registry/비동기 dispatch를 연결하고 전체 통합·문서를 담당한다.
- 각 기능은 먼저 독립 반례와 회귀시험을 작성한다. 기존 57개 시험과 9개 프로브도 유지한다.
- C# 코드의 실제 검증에는 manifest를 잠시 후보 file: 경로로 지정해 Unity가 후보를 컴파일하도록 할 수 있다. 원본 manifest/lock/런처는 이미 백업되어 있다. 이 임시 로컬 검증은 GitHub push의 대체 수단이 아니며 최종 설치는 승인된 Git SHA 핀이다.
- 임시 prefab base/variant/scene fixture와 Editor 컴파일 fixture는 유일한 소유 폴더에 만들고 제거한다. 기존 씬의 저장되지 않은 변경은 저장/폐기하지 않는다.
- 검증 완료 후 codex/mcp-audit-reliability 검증 브랜치를 기존 origin인 Seoki2000/unity-mcp에 push하고 프로젝트 manifest/lock·고정 설치 런처를 갱신한다. 기존 main/optimized 브랜치를 merge하지 않는다. 원격 작업이 자동 승인 검토에 다시 차단되면 우회하지 않는다.

## 수용 기준

- 로컬 성공/오류와 실제 Unity 응답에 각 근거가 표시되며 캐시가 원래 빌드 시점을 잃지 않는다.
- source를 바꾸고 DLL을 빌드하지 않아도 AST 조회에서 변경이 보이고, 해당 결과가 IL graph와 같은 세대라는 거짓 주장을 하지 않는다.
- nested/partial/전처리기/잘못된 구문·경로 경계와 timeout을 fixture로 확인한다.
- prefab base/variant/instance 저장 값과 override 표기를 독립 Unity 직렬화 결과와 대조한다. 제거/추가 컴포넌트도 scope를 명시한다.
- 평가 실행은 task ID, 입력/도구/답변 trace, 시간·호출 수, 채점 근거, 실제 모델/결정적 실행 구분을 남긴다. 실패와 미실행을 성공으로 세지 않는다.
- 최종 실제 설치 런처 경로·manifest/lock SHA·PackageCache 핵심 파일 해시가 모두 후보와 일치한다. Play/재컴파일 후 편집 모드와 임시 파일 제거를 확인한다.

## 위험과 제한

Roslyn은 source syntax를 제공하며 모든 cross-assembly semantic analysis를 추가하는 범위는 아니다. Prefab 값은 저장/Editor 관측 시점의 값이며 런타임 스크립트의 모든 변경을 예측하지 않는다. 전체 RPC/Animator 실행 경로 분석, 공유 daemon, 모든 SSE/cancellation 개선은 별도 과제다. 테스트의 제외 조건과 모델 실행을 확보하지 못한 항목은 명시적으로 남긴다.
