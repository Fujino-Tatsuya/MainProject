---
description: Agent-Bridge로 Codex와 협업 시작 — 공용 컨텍스트/메시지 확인, 필요하면 작업 위임(handoff)
---

이 레인(`MainProject`)에서 Codex와 협업하기 위한 시작 절차. Agent-Bridge MCP 도구
(`get_shared_context`, `send_agent_message`, `read_agent_messages`, `wait_for_agent_message`)를 쓴다.

## 1. 공용 컨텍스트 확인 (항상 먼저)

`get_shared_context`를 호출해서 AGENTS.md/CONTEXT.md/PLAN.md와 현재 git 브랜치·HEAD·상태를 읽는다.

## 2. 밀린 메시지 확인

`read_agent_messages(recipient:"claude", last_n:10)`으로 최근 메시지를 확인한다.
- `work_completed`나 `[WATCHER]`로 시작하는 메시지가 있는데 아직 반영이 안 됐으면(예: 커밋 해시가
  CONTEXT.md에 없음) → 사용자에게 `/coop-agent-reload`를 먼저 돌리자고 제안한다.
- `[WATCHER] ... 실패` / `... 보고 없음` 메시지가 있으면 → 위임이 실패했을 수 있다는 뜻이니
  사용자에게 그대로 보여주고 재위임할지 물어본다.

## 3. `$ARGUMENTS`가 있으면 — Codex에게 위임(handoff)

1. 워처가 켜져 있는지 먼저 확인한다:
   `powershell -File C:\Users\user\Desktop\Co_Working_for_Agents\agent-context-bridge\codex-lane.ps1 -Lane MainProject -Action status`.
   꺼져 있으면 먼저 알리고, 사용자 승인 하에 `-Action start`를 실행한다 — **handoff를 보내기 전에
   반드시 워처가 `READY`인지 확인할 것** (안 그러면 그 handoff가 기준점이 되어 워처가 영원히 못 본다).
2. `send_agent_message(sender:"claude", recipient:"codex", type:"handoff", content:$ARGUMENTS, files:[관련 파일 경로들])`
   호출. **content에 소스 코드나 비밀 값을 그대로 넣지 않는다** — 무엇을 왜 하라는지 자연어로,
   파일은 경로로만 알려준다.
3. 보낸 뒤 결과(메시지 id)를 사용자에게 보여준다. **`wait_for_agent_message`로 계속 기다리는 건
   사용자가 명시적으로 요청했을 때만** 한다 — 기본은 여기서 끝내고 사용자에게 넘긴다.

## 4. `$ARGUMENTS`가 없으면 — 상태 보고만

1·2번만 하고, 지금 상태(브랜치/최근 메시지)를 요약해서 사용자에게 보여준다. 위임은 하지 않는다.
