---
description: Agent-Bridge로 Codex와 협업 시작 — 공용 컨텍스트/메시지 확인, 필요하면 작업 위임(handoff)
---

Codex와 협업하기 위한 시작 절차. Agent-Bridge MCP 도구
(`get_shared_context`, `send_agent_message`, `read_agent_messages`, `wait_for_agent_message`)를 쓴다.

## 0. 레인을 먼저 확정한다 — 🔴 **프로젝트 이름 ≠ 레인 이름**

**레인 이름을 추측하거나 하드코딩하지 마라.** 프로젝트가 `MainProject`라고 해서 레인이
`MainProject`인 것이 아니다. 레인은 *체크아웃(워크트리)* 단위이고, 같은 프로젝트에 여러 레인이 있다.

실제로 이렇게 갈린다 (2026-09-21 기준 예시 — 브랜치는 수시로 바뀐다):

| 레인 | 워크트리 | 비고 |
|---|---|---|
| `MainProject` | `C:\UnityProject\MainProject` | 본 체크아웃 |
| `MainProject-WorkTree` | `C:\UnityProject\MainProject-Worktree` | 워크트리 |
| `MainProject-MLAgent` | ML 에이전트용 | |

**엉뚱한 레인에 handoff를 보내면 Codex가 다른 브랜치에서 작업한다.** 그래서 매번:

1. `-Lane` 없이 상태를 찍어 레인 전체와 각 레인의 브랜치를 본다:
   `powershell -File C:\Users\user\Desktop\Co_Working_for_Agents\agent-context-bridge\codex-lane.ps1 -Action status`
2. **지금 세션의 작업 디렉터리·브랜치와 일치하는 레인**을 고른다. 출력의 `branch=` 를 현재
   `git branch --show-current` 결과와 대조할 것.
3. 어느 레인인지 확실하지 않으면 **추측하지 말고 사용자에게 묻는다.**

아래에서 `<LANE>` 은 여기서 고른 레인, `<WORKTREE>` 는 그 레인의 워크트리 경로다.

## 1. 공용 컨텍스트 확인 (항상 먼저)

`get_shared_context`를 호출해서 AGENTS.md/CONTEXT.md/PLAN.md와 현재 git 브랜치·HEAD·상태를 읽는다.

## 2. 밀린 메시지 확인

`read_agent_messages(recipient:"claude", last_n:10)`으로 최근 메시지를 확인한다.
- `work_completed`나 `[WATCHER]`로 시작하는 메시지가 있는데 아직 반영이 안 됐으면(예: 커밋 해시가
  CONTEXT.md에 없음) → 사용자에게 `/coop-agent-reload`를 먼저 돌리자고 제안한다.
- `[WATCHER] ... 실패` / `... 보고 없음` 메시지가 있으면 → 위임이 실패했을 수 있다는 뜻이니
  사용자에게 그대로 보여주고 재위임할지 물어본다.

## 3. `$ARGUMENTS`가 있으면 — Codex에게 위임(handoff)

1. **0번에서 고른 `<LANE>`** 의 워처가 켜져 있는지 확인한다:
   `powershell -File C:\Users\user\Desktop\Co_Working_for_Agents\agent-context-bridge\codex-lane.ps1 -Lane <LANE> -Action status`.
   꺼져 있으면 먼저 알리고, 사용자 승인 하에 `-Action start`를 실행한다 — **handoff를 보내기 전에
   반드시 워처가 `READY`인지 확인할 것** (안 그러면 그 handoff가 기준점이 되어 워처가 영원히 못 본다).
   시작한 뒤 `branch=` 가 지금 작업 브랜치와 같은지 한 번 더 대조한다.
2. `send_agent_message(sender:"claude", recipient:"codex", type:"handoff", content:$ARGUMENTS, files:[관련 파일 경로들])`
   호출. **content에 소스 코드나 비밀 값을 그대로 넣지 않는다** — 무엇을 왜 하라는지 자연어로,
   파일은 경로로만 알려준다.
3. **handoff를 보냈으면 곧바로 완료 감시를 건다** — 아래 4번. 빼먹지 말 것.
4. 보낸 뒤 결과(메시지 id)와 **어느 레인으로 보냈는지**를 사용자에게 보여준다.
   **`wait_for_agent_message`로 계속 기다리지 마라** — 그건 대화를 막는다. 감시는 4번의
   백그라운드 프로세스가 한다.

## 4. 위임했으면 완료 이벤트를 백그라운드로 감시한다 (handoff 직후 필수)

Codex가 끝나도 **아무도 알려주지 않는다.** 사용자가 직접 물어보거나 `/coop-agent-reload`를
돌려야 알게 되는데, 그러면 놓치거나 한참 뒤에 안다. 그래서 handoff를 보낸 직후
`Monitor` 도구로 백그라운드 감시를 건다.

감시 대상은 레인 폴더의 파일 2개다 (`<BRIDGE_HOME>` = `C:\Users\user\Desktop\Agent-Bridge`):

| 파일 | 내용 |
|---|---|
| `<BRIDGE_HOME>/<LANE>/conversation.jsonl` | 메시지 원본. 한 줄에 JSON 하나. 완료 보고는 `"type":"work_completed"` |
| `<BRIDGE_HOME>/<LANE>/watcher.log` | 워처 로그. `DONE id=... (exit=N)` · `[RELOAD]` · 실패 메시지 |

`Monitor` 호출 예 (`<LANE>` 만 바꿔 쓴다):

```
H="/c/Users/user/Desktop/Agent-Bridge/<LANE>"
tail -n0 -F "$H/conversation.jsonl" "$H/watcher.log" 2>/dev/null \
  | grep -E --line-buffered '"type":"(work_completed|review)"|DONE id=|\[RELOAD\]|보고 없음|실패|exit=[1-9]' \
  | sed -u 's/^\(.\{0,300\}\).*/\1/'
```

- 🔴 **성공만 잡으면 안 된다.** Codex가 크래시하거나 사용량 한도에 걸리면 `work_completed`가
  영영 안 온다. `exit=[1-9]` · `보고 없음` · `실패` 를 같은 필터에 넣어야 침묵과 실패가 구분된다.
- `timeout_ms` 는 최대치(1800000)로 잡는다. 만료 통지가 오면 작업이 아직이면 **다시 건다.**
- `sed -u` 로 줄을 잘라라. `conversation.jsonl` 한 줄이 handoff 본문 전체라 안 자르면 알림이 거대해진다.
- 이벤트가 오면 그때 `/coop-agent-reload` 를 제안한다.

## 5. `$ARGUMENTS`가 없으면 — 상태 보고만

0·1·2번만 하고, 지금 상태(레인/브랜치/최근 메시지)를 요약해서 사용자에게 보여준다.
위임하지 않으므로 4번 감시도 걸지 않는다.
