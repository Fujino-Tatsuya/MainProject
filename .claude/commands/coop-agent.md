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
돌려야 알게 되는데, 그러면 놓치거나 한참 뒤에 안다. 그래서 handoff를 보낸 직후 백그라운드
감시를 건다.

감시 대상은 레인 폴더의 파일 2개다 (`<BRIDGE_HOME>` = `C:\Users\user\Desktop\Agent-Bridge`):

| 파일 | 내용 |
|---|---|
| `<BRIDGE_HOME>/<LANE>/conversation.jsonl` | 메시지 원본. 한 줄에 JSON 하나. 완료 보고는 `"type":"work_completed"` |
| `<BRIDGE_HOME>/<LANE>/watcher.log` | 워처 로그. `DONE id=... (exit=N)` · `[RELOAD]` · 실패 메시지 |

### 🔴 `tail -F` 를 쓰지 마라 — 워처를 망가뜨린다

Git MSYS 의 `tail`(`tail -f` / `-F`)은 Windows 에서 파일을 **잠근다.** 그 상태에서는 워처의
PowerShell `Add-Content` 가 "다른 프로세스에서 사용 중" 으로 **실패한다.**
`Write-Log` 는 5회 재시도 후 **에러를 삼키고 조용히 포기**하므로
([`codex-watcher.ps1`](file:///C:/Users/user/Desktop/Co_Working_for_Agents/agent-context-bridge/codex-watcher.ps1) 의 `Write-Log`),
증상이 "로그가 그냥 멈춤" 으로만 보인다.

2026-09-21 에 실제로 이 사고가 났다. `tail -F` 를 붙인 6초 뒤부터 `watcher.log` 가 한 줄도
안 찍혔고, `DONE id=` 와 `[WATCHER] 실패/보고 없음` 알림이 통째로 사라졌다.
`conversation.jsonl` 의 메시지는 MCP 서버(Node)가 써서 살아남았기 때문에
"메시지는 오는데 로그만 죽은" 혼란스러운 상태가 됐다.

**대신 짧게 열고 닫는 폴링을 쓴다.** 파일을 붙들지 않으므로 워처의 쓰기를 막지 않고,
순간 겹쳐도 워처의 5회 재시도가 흡수한다.

### 어떻게 걸까

완료는 **이벤트 스트림이 아니라 "끝나면 한 번"** 이다. 그러니 `Monitor`(30분 상한이 있어
긴 작업에서 먼저 죽는다)가 아니라 **`Bash` + `run_in_background`** 로, 조건이 서면
**종료하는** 스크립트를 돌린다. 종료 시 자동으로 알림이 온다.

스크립트 뼈대 — 15초마다 두 파일의 줄 수만 보고, 늘었으면 그 구간만 읽는다:

```bash
H="/c/Users/user/Desktop/Agent-Bridge/<LANE>"
base_conv=$(wc -l < "$H/conversation.jsonl"); base_wlog=$(wc -l < "$H/watcher.log")
while :; do
  n=$(wc -l < "$H/conversation.jsonl")
  if [ "$n" -gt "$base_conv" ]; then
    new=$(sed -n "$((base_conv+1)),\$p" "$H/conversation.jsonl" | grep '"sender":"codex"')
    [ -n "$new" ] && { echo "$new" | cut -c1-500; exit 0; }
    base_conv=$n
  fi
  w=$(wc -l < "$H/watcher.log")
  if [ "$w" -gt "$base_wlog" ]; then
    d=$(sed -n "$((base_wlog+1)),\$p" "$H/watcher.log" | grep -E 'DONE id=|보고 없음|실패|\[RELOAD\]')
    [ -n "$d" ] && { echo "$d" | cut -c1-500; exit 0; }
    base_wlog=$w
  fi
  sleep 15
done
```

- 🔴 **성공만 잡으면 안 된다.** Codex가 크래시하거나 사용량 한도에 걸리면 `work_completed`가
  영영 안 온다. `DONE id=` · `보고 없음` · `실패` 도 종료 조건에 넣어야 침묵과 실패가 구분된다.
  `"sender":"codex"` 로 잡으면 `type:"message"`(질문)도 걸려서 Codex가 막혔을 때도 깨어난다.
- `cut -c1-500` 으로 줄을 잘라라. `conversation.jsonl` 한 줄이 handoff 본문 전체다.
- 무한 루프이므로 **시간 안전장치**(예: 3시간)를 넣고 초과 시 종료 코드를 달리해라.
- 깨어나면 `/coop-agent-reload` 를 제안한다.

### 조용할 때 생사 확인하는 법

로그가 멈춰도 Codex는 대개 살아 있다. 물어보기 전에 직접 확인한다:

- `Get-Process | Where-Object { $_.ProcessName -match 'codex' }` — 시작 시각이 handoff
  발송 직후면 워처가 정상적으로 띄운 것이다
- 작업 대상 폴더의 파일 수정 시각 (`find <dir> -newermt '-5 minutes'`)
- `git log`/`git status` — 커밋까지 갔는지

프로세스가 살아 있고 파일이 최근에 바뀌었으면 **그냥 작업 중**이다.

## 5. `$ARGUMENTS`가 없으면 — 상태 보고만

0·1·2번만 하고, 지금 상태(레인/브랜치/최근 메시지)를 요약해서 사용자에게 보여준다.
위임하지 않으므로 4번 감시도 걸지 않는다.
