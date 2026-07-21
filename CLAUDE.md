# CLAUDE.md — AI 작업 규칙 (팀 공용)

> Claude Code/AI 에이전트가 이 레포에서 작업할 때 따르는 규칙.
> 팀원은 그대로 따라도 되고, 개인 오버라이드는 `CLAUDE.local.md`(gitignore됨)에 두면 됨.

## 매 세션 — 작업 시작 전
1. 다음 문서를 **있으면 먼저 읽고** 컨텍스트를 맞춘다: `AGENT.md`, `CONTEXT.md`, `PLAN.md`, `AIRULE.md`.
   - `AGENT.md`는 프로젝트 공용 규칙, `CONTEXT.md`는 Claude/Codex 간 현재 작업 인수인계, `PLAN.md`는 합의된 상세 작업 계획이다.
   - 작업 대상과 관련된 `Docs/` 문서도 함께 확인한다.
   - MCP의 `agent-context-bridge`가 보이면 `get_shared_context`와 `read_agent_messages(recipient: claude)`로 Git 상태와 Codex의 최근 메시지도 확인한다.

## Codex와 컨텍스트 공유
2. 채팅에서만 확정된 중요한 결정은 구현 전에 `CONTEXT.md`, `PLAN.md` 또는 관련 `Docs/` 문서에 반영한다.
3. 의미 있는 작업 단위가 끝나거나 다른 에이전트에게 넘길 때 `CONTEXT.md`의 현재 상태·다음 작업·최근 인수인계를 간결하게 갱신한다.
   - 작업 시작 시 `CONTEXT.md`의 작업 세션에 작업자와 수정 예정 파일을 적어 Codex와 동시 수정을 피한다.
   - 브릿지가 보이면 수정 전 Codex에게 `work_started`, 종료 시 `work_completed` 또는 `handoff` 메시지를 보낸다.
   - 영구 규칙과 상세 설계를 `CONTEXT.md`에 중복 작성하지 말고 원본 문서로 연결한다.
   - 문서와 코드가 충돌하면 실제 코드를 확인한 뒤 문서를 바로잡는다.
   - 메시지 브릿지가 실패해도 작업을 중단하지 않으며, 코드는 별도 공유 폴더에 복제하지 않고 이 저장소에서 직접 수정한다.
   - `wait_for_agent_message`는 사용자가 상시 협업이나 대기를 요청한 경우에만 사용한다.

## 큰 작업일 때
4. 구현 전에 **grill 단계**로 이해도를 맞춘다 — 요구사항·범위·엣지케이스·완료조건을 질문으로 캐물어 확정한다. 애매하면 선택지를 제시.
5. 합의된 내용을 **`PLAN.md`로 작성**하고 → **승인을 받은 뒤** 구현에 들어간다.

> 작은/단순 작업(오타·1줄 수정·단순 조회 등)은 grill·PLAN 생략 가능. "큰 작업"이면 위 4~5를 반드시 따른다.

@AGENTS.md
