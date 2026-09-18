# Unity MCP 추가 조사·교차검증

대상: `69d494e` 후보와 후속 진단 freshness 수정. 2026-09-09.
후속 수정 커밋: `f16c698` (`codex/mcp-audit-reliability`, 로컬 보존).

## 결론과 범위

필요한 기능이 전부 구현됐다는 결론은 아니다. 이번 패치는 기존 MCP·해석기의 신뢰성 결함을 줄인다. 소스, 빌드 산출물, 저장 에셋, 실제 실행 상태를 모두 연결해 정확한 수정까지 보장하는 평가는 아직 없다.
전수 검사라는 표현에는 대상 목록·개수·제외 사유·검사한 질문을 붙여야 한다. 모든 파일을 읽는 것만으로 모든 참조/실행 경로를 검증했다고 할 수 없다.

## 추가로 재현하고 수정한 문제

`Bridge/index/errorimpact.js`는 `hadErrors:false`만 받으면 `freshness.state=current`로 표시했다. `compilationGeneration=0`이어도 같았다.
그러나 실제 서버는 컴파일을 관측하지 않은 경우에도 `hasErrors:false`를 반환하며, `diagnosticsState=unobserved`와 별도 설명으로 이를 구별한다. 세대 값 자체도 현재 인덱스/DLL/소스에 연결된 증명은 아니다.

독립 입력 3종으로 무관측/미보고/오류 보고를 대조하고, 별도 리뷰어도 해당 분기의 모순을 확인했다. 이 문제는 이번 패치가 새로 만든 오류가 아니라 기존 계약의 결함이다.

수정: 명시적 오류는 `last-good`, 다른 경우는 `unknown`으로 표시한다. `reportedHasErrors`에 보고된 boolean/null을 보존한다. ‘최신 여부를 모른다’가 ‘컴파일에 실패했다’는 뜻은 아니다.
`current`를 다시 허용하려면 소스·성공 빌드·인덱스의 세대 일치를 실제로 확인하는 계약이 필요하다.

회귀시험: 무관측 0세대, 세대 미제공, 양수 세대만 제공, 명시 오류, 진단 미보고, 잘못된 문자열 힌트의 6경우. 원본 실패를 확인하고 수정 후 6/6, 기존 진단 프로브 33/33을 확인했다.
최종 통합 실행에서는 새 회귀시험 총 57개와 기존 프로브 9개가 각 통과 기준을 충족했다. 지도 자체의 기준 질문은 여전히 6/7이다.
실제 서버와 연결한 stdio 경로에서도 합성 진단 입력에 `unknown`이 반환되는 것을 확인했다. 합성 진단은 실제 게임 컴파일 오류가 아니며 게임 소스에 오류를 삽입하지 않았다.

## 남은 항목과 우선순위

| 순서 | 필요한 작업 | 현재 상태 / 완료 판정 |
|---|---|---|
| 1 | 모든 로컬 응답의 근거 시점 통일 | 진단·index status에는 일부 표시가 있지만 전체 지도/영향/검색 결과의 source·assembly·index 세대 계약은 없다. 소스 변경 직후, 컴파일 실패, reload, 캐시 재시작을 같은 정답으로 비교해야 한다. |
| 2 | 현재 소스 AST와 마지막 성공 DLL을 함께 조회 | 기존 Roslyn 감사의 필드 누락 120줄 중 중첩 타입 관련 96줄이었다. AST를 보강하되 활성 전처리기·partial·nested·동명 타입·source generator를 따로 평가한다. |
| 3 | Prefab/Variant/Instance 최종 값과 출처 | GUID 참조와 실제 최종 필드 값은 다르다. base→variant→instance override, 추가/제거 컴포넌트까지 독립 Unity API 결과와 대조한다. |
| 4 | 게임 작업 단위 경로 평가 | InputAction→skill→AnimationEvent→Hitbox→damage→RPC/권한을 연결한다. 정적 연결과 실제 실행 관측을 구분하며 동적 호출을 확정적으로 추측하지 않는다. |
| 5 | 장시간·취소·큰 응답 경계 | HTTP deadline/EOF는 수정했지만 SSE 버퍼 상한, cancellation, 실제 지원 버전 협상, 동기 index CPU 작업의 worker 분리는 남았다. 자원 회수·중복 부작용·p95/p99·RSS를 측정한다. |
| 6 | 실제 AI 작업의 정답률 대비 토큰 | 도구 목록 축약 7.7%와 지도 6/7만으로 작업 능력을 입증하지 않는다. 근거 있는 정답/수정 결과를 정한 평가셋과 별도 holdout, 복수 실행을 사용한다. |

## 공식 문서와의 대조

- Unity `GetDependencies`는 에셋 의존성을 반환하지만 GameObject 자체나 빌드에 실제 필요한 모든 항목과 동치가 아니다. 따라서 GUID scanner, Unity API, 런타임 사용 여부를 한 정답 목록으로 단순 합치면 안 된다. [Unity 6.3 GetDependencies](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.GetDependencies.html)
- Prefab override API는 기본/비기본 override, 자식의 변경, 무효해진 변경까지 반환할 수 있고 null도 가능하다. API 호출 하나로 최종 값이 모두 검증됐다고 할 수 없다. [Unity 6.3 GetPropertyModifications](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PrefabUtility.GetPropertyModifications.html)
- MCP의 `outputSchema`/`structuredContent`는 선택 기능이다. 도입하면 구조 검증에 유용하지만 호환성을 위한 text 병기는 토큰을 늘릴 수도 있다. 자동 토큰 절감 기능으로 취급하지 않는다. [MCP tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- 요청 취소는 timeout과 다르다. 취소 불가 작업과 이미 완료한 작업의 경합을 처리해야 하며, 게임에 이미 적용한 부작용의 롤백까지 자동 보장하지 않는다. [MCP cancellation](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation)
- 에이전트 평가는 응답 문장뿐 아니라 최종 환경 상태와 여러 번의 실행을 평가해야 한다. 이 프로젝트에서는 ‘찾았다’는 답보다 실제 rename/수정 후 컴파일·참조·Play 결과가 중요하다. [Anthropic agent evaluations](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents)
- concise/detailed, pagination과 선택 필드는 후보 최적화다. 정답률·호출 수·지연을 함께 비교해야 한다. [Anthropic tool design](https://www.anthropic.com/engineering/writing-tools-for-agents)

## 다음 구현에 사용할 평가 틀

초기 제안은 작업 질문 30개와 구현 튜닝에 쓰지 않을 holdout 10개다. 아직 실행한 평가셋이 아니며 개수도 설계 제안이다.
정답에는 근거 파일/GUID/fileID/소스 또는 빌드 세대를 붙이고, 답변 정확도·거짓 확정·누락·수정 후 검증 결과·총 토큰·도구 호출 수·지연을 따로 기록한다.
오프라인 AST/에셋 평가, Unity Editor 검증, 실제 모델 작업 평가를 분리한다. 일부 fixture에 맞춰진 프로브나 동일 구현끼리의 대조를 독립 오라클로 부르지 않는다.

큰 기능을 한 번에 추가하기보다 1번 공통 근거 계약 → 2번 AST 보강과 평가 → 3~4번 데이터/실행 경로 연결 순으로 진행하는 것이 타당하다.
현재 원격 push와 프로젝트 핀 적용은 정확한 GitHub 목적지 승인이 필요한 자동 승인 검토 때문에 보류된 상태다. 이 문서는 배포 완료를 의미하지 않는다.
