# MCP 확장 1·2·3 구현 및 실서버 검증

패키지 커밋: `d5adddf9a0e3c511d444a0988e3e756d5a2f7a80`
브랜치: `codex/mcp-audit-reliability`, 버전 `2.3.0-dev.0.0.8`.
기존 신뢰성 수정 커밋 db29827 / 69d494e / f16c698을 포함한다.

## 구현

1. 로컬 도구 성공·오류와 Unity 도구 응답에 공통 evidence를 붙였다. 관측 시각,
   원래 인덱스 빌드 시각, 캐시 여부, 마지막 DLL/에셋 메타데이터 검사 시각과
   coverage를 구분한다. 저장 소스와 DLL의 일치 여부는 증거가 없으므로 unknown이다.
2. 저장된 C#를 Roslyn syntax로 조회하는 `unity_get_source_declarations`와
   Unity 직렬화 최종 값·출처·오버라이드를 읽는 `unity_inspect_prefab_values`를 추가했다.
   전자는 파일별 nested/partial 선언, 속성, 물리적 줄 범위, 구문 오류, 콘텐츠/define
   해시를 제공한다. 후자는 asset/instance, 타깃·컴포넌트·propertyPath 필터와 페이지 조회를 지원한다.
3. 개발용 30개와 holdout 10개 작업, 실제 도구 호출 trace, 정답·증거·최종 파일 상태
   grader를 추가했다. 미실행/실패/불완전/잘못된 기록은 통과로 세지 않는다.

실서버 테스트에서 새 프리팹 도구의 `object`·Dictionary 값이 JsonUtility에 의해
빠지는 문제를 발견했다. 해당 도구가 명시적으로 JToken을 반환하도록 하고 서버에
JToken 전용 직렬화 경로를 추가했다. 기존 typed 도구 응답은 기존 형식을 유지한다.
프리팹 시험도 내부 함수 직접 변환에서 실제 JsonRpcHandler 경로로 강화했다.
Unity 6000.3에서 발생한 새 obsolete 경고는 EntityIdToObject 분기로 해결했다.

## 실측 결과

Unity **6000.3.16f1**, 실제 `MainProject`, 인증된 localhost:3000 서버에서 실행했다.
GitHub 설치 전 검증은 manifest를 잠시 후보 file: 경로로 지정해 후보 C#를 직접
컴파일한 상태에서 수행했다. 원래 manifest/lock은 output에 백업했다.

| 검증 | 결과 |
|---|---|
| Node 회귀시험 | 84/84 — transport 30, index 13, launcher 9, probe support 2, diagnostics 6, evidence 5, source AST 9, eval harness 10 |
| 기존 프로브 | 9개 실행 모두 각 프로브의 합격 기준 충족 |
| 실제 프리팹 JSON-RPC | 23개 검증 통과, 기존 씬 active/dirty 상태 보존 |
| 실제 도구 목록 | 87개, 새 도구 2개 포함 |
| Play 2회 진입 | 5,581 / 5,718 ms |
| Play 2회 이탈 | 1,633 / 1,701 ms |
| 실제 임시 Editor 어셈블리 컴파일 | 8,048 ms, observedAssemblyCount=1, DLL 4,608 bytes |
| 임시 어셈블리 제거 후 컴파일 | 7,269 ms, DLL 제거 확인 |
| 최종 Editor | 0.BootStrapScene, 편집 모드, 컴파일 중 아님 |
| 재조회 지연 | 소스 139 ms, 프리팹 97 ms — 단일 표본, 보장값 아님 |

프리팹은 base BoxCollider 값 1/4, variant 8, instance 12, 상속 값의 base 출처,
추가 Rigidbody·제거 SphereCollider, 중복 이름·컴포넌트, 페이지 경계를 대조했다.
임시 preview scene과 소유 fixture 에셋은 제거했다.

실패 기록도 유지한다. 첫 live 실행은 테스트 드라이버의 상대 preload 경로 문제로
실패했고 절대경로 처리 후 통과했다. 기존 캐시 프로브 2개는 Unity 컴파일·fixture
변경과 동시에 실행한 첫 회차에 실패했다. 로그의 metadata/DLL 변경을 확인했고,
변경을 멈춘 재실행과 최종 전체 회귀시험에서 통과했다.
프로젝트 맵의 질문 커버리지는 여전히 6000 추정 토큰에서 **6/7**이다.
프로브 exit 0을 7/7 또는 전수 정확도로 바꾸어 보고하지 않는다.

## 실제 모델 작업과 토큰

개발 30개는 정답을 아는 deterministic reference solver로 harness 동작을 검증했다.
모델 정확도 점수로 쓰지 않는다. 별도 holdout 10개는 현재 주 에이전트가 정답 파일을
읽지 않고 공개 작업·도구 결과를 보고 해결했고, 답변과 최종 파일 상태 10/10을 통과했다.
도구 호출 12회, 읽기 10회와 fixture 수정 2회였다.

보조 에이전트가 workspace credits 부족으로 중단되어 독립 새 에이전트의 blind trial은
확보하지 못했다. 주 에이전트가 구현에 참여한 사실과 테스트 노출을 감안해야 한다.
작은 합성 source/YAML 작업이며 실제 게임의 전반적 정확도나 기존 버전 대비 개선율을
입증하지 않는다. 반복 trial·더 넓은 실제 작업·독립 검증은 남은 평가 과제다.

`tiktoken 0.14.0 / o200k_base`로 캡처한 텍스트를 측정했다.

| 캡처 대상 | 토큰 |
|---|---:|
| 87개 도구 목록 wire JSON | 10,427 |
| project-map wire JSON | 6,115 |
| source lookup wire JSON | 973 |
| prefab lookup wire JSON | 711 |
| 모델 holdout 전체 공개 trace 텍스트 | 3,319 |
| deterministic dev trace 텍스트 | 12,023 |

공통 인덱스 evidence는 응답 payload에 약 150–151토큰, Editor evidence는 47토큰을
추가한다. 소스 evidence에는 콘텐츠/컴파일러/define 해시와 분석 범위 설명도 포함된다.
이번 확장 비용을 절감률로 포장하지 않는다. 숨은 reasoning·시스템 포장·청구 토큰은
측정하지 못했으며, 에이전트 전체 비용과 위 trace 비용은 다르다.

## 배포 상태

**2026-09-11 배포·프로젝트 적용 완료.** 앞서 자동 승인 검토가 정확한 목적지의 명시적
승인 부재로 push를 차단했지만, 사용자가 저장소·브랜치 push 및 적용을 승인한 뒤 성공했다.

- 원격: `https://github.com/Seoki2000/unity-mcp.git`, `codex/mcp-audit-reliability`.
- 최종 SHA: `930124007dd42095eed64735d6f431b5df8940f5` — 구현 d5adddf에 영문·한글
  README의 새 기능, 실측 검증, 평가·토큰 한계 설명을 추가한 커밋이다.
- `git ls-remote`로 원격 브랜치와 로컬 HEAD 일치를 확인했다. main/optimized는 변경하지 않았다.
- 현재 프로젝트 manifest·lock을 위 SHA로 고정했고 Unity가 GitHub에서 받은
  `Library/PackageCache/com.community.unity-mcp@930124007dd4`로 해석했다.
- `.mcp.json`이 실행하는 고정 런처를 검증 버전으로 갱신했다. 런처 로그에서 새 캐시
  선택을 확인했고, 설치된 핵심 파일 10개의 정규화 SHA-256이 검증 소스와 일치했다.
- 적용 전 회귀시험 84/84와 기존 프로브 9개를 다시 실행해 각 기준을 통과했다.
- 첫 설치 조회에서는 Node는 새 버전이지만 Editor 어셈블리 재로드 전이라 새 프리팹
  도구가 없었다. 패키지 해석 후 재컴파일을 수행했고 관측 어셈블리 3개, 오류 0개를
  확인했다. 경고 4개는 기존 gameplay 코드의 deprecated RPC, OnDestroy 숨김, 미사용 필드다.
- 재컴파일 후 **설치된 고정 런처**로 87개 도구, 실제 소스 hash, 실제 프리팹 최종 값,
  프로젝트 맵·영향·진단 근거를 확인했다. `installation-verification.json`의 ok=true.
- 최종 씬은 사용자가 열어 둔 **Dev_Boot**, 편집 모드다. 이번 적용에는 Play를 다시
  실행하지 않았고, 앞선 동일 구현의 Play 2회·실제 fixture 컴파일 검증을 유지한다.

오늘의 원본 결과는 `output/unity-mcp-install-2026-09-11/`의 regressions/,
before-domain-reload-live-results.json, live-results.json, installation-verification.json에 있다.
기존 설정과 런처도 같은 폴더에 백업했다. 이전 9월 8~10일 output 자료는 이후 루트 정리
작업에서 이동되었을 수 있으므로 당시 보고서와 정리 커밋 기록을 함께 확인한다.

## 재현 파일과 남은 범위

- `scripts/audit_unity_mcp_fixed.cjs <candidate-root>`: 회귀시험·기존 프로브.
- `AUDIT_PACKAGE`, `AUDIT_OUTPUT`, `AUDIT_EXTENDED=1`을 지정한
  `scripts/audit_unity_mcp_live.cjs`: 실제 서버·브릿지·새 도구 조회.
- `AUDIT_FORCE_COMPILE_FIXTURE=1`과 `scripts/audit_unity_mcp_play.cjs`: Play/실제 컴파일.
- `scripts/audit_unity_mcp_vnext_tokens.py`: 캡처 응답과 trace 토큰 측정.
- `scripts/verify_unity_mcp_install.cjs <SHA> <candidate-root>`: 최종 설치 SHA/핵심 파일/런처 검증.
- `C:/Users/user/Projects/TeamVault/MainProejectVault/04-report/mcp-audit-output/unity-mcp-vnext/`: 원본 실패·성공 로그, prefab-wire-results.json,
  extended/play-results.json, final-regressions/, final-transport.log, final-eval.log,
  model-holdout/result.json, reference-dev/result.json, token-results.json. Git에는 원본
  실행 캐시와 로그 전체를 넣지 않고 재현 스크립트·검증 보고서를 커밋한다.

미저장 버퍼, 자동 Unity define 추론, 파일 간 semantic binding, 모든 동적/reflection 호출,
일반 SSE 프레임 제한·클라이언트 cancellation, 동기식 전체 인덱스의 CPU 대기는 남은 범위다.
새 프리팹 도구의 캡과 unknown/truncated를 무시하면 안 된다. NGO/MPPM의 새 기능은
수정하지 않았으므로 이번 시험을 멀티플레이 동작 검증으로 확대하지 않는다.

공식 자료 교차 확인: [MCP tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools),
[MCP cancellation](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation),
[Unity Prefab modifications](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PrefabUtility.GetPropertyModifications.html),
[Unity 6000.3 API 변경](https://unity.com/releases/editor/whats-new/6000.3.0f1),
[실제 작업 eval 설계](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents).
NarshaADK의 공개 개발자 설명을 참고했으며 발표 영상 전체 분석·기능 동등성을 주장하지 않는다.

## 소유권 확인용 짧은 질문

1. 컴파일 오류 0개면 저장 소스와 DLL의 일치가 확정되는가?
2. source AST에서 defines를 생략하면 현재 Unity의 조건부 컴파일 구성이 자동 적용되는가?
3. 프리팹 내부 함수 결과가 맞으면 실제 MCP 응답의 필드 보존도 확정되는가?

정답: 모두 아니오. 1은 독립 세대 근거가 필요하고, 2는 unknown으로 표시하며,
3은 실제 직렬화 경로까지 검증해야 한다.
