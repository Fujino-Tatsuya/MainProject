# 로비 데모 흐름 — 부스 운영 (할 일 48)

> 대상: 부스 스태프·팀원. **PC 를 켜고 설정 파일을 두는 법, 화면 문구의 뜻, 막혔을 때 볼 것**만 적는다.
> 설계·규칙 원본: [networking.md "Lobby Demo"](networking.md#lobby-demo-target-2026-10-19) · [SceneFlow.md "Target Flow"](../design/SceneFlow.md#-target-flow--lobby-demo-2026-10-19-implementation-in-progress) · 결과 패널 [lobby-result-panel.md](lobby-result-panel.md) · 계획 `_lane-artifacts/common/48-lobby/PLAN-48-lobby-demo-flow-v2.md`.
> 기준: `feature/LobbySession` C13(2026-10-19). 문서와 코드가 다르면 코드가 맞다.

## 1. 흐름
```text
exe 실행 → (호스트) 바로 호스팅 / (클라) 자동 접속 — 버튼 없음
  → 걷는 로비(3.LobbyScene) → (원하면 캐릭터 선택 오브젝트 앞에서 F → 패널에서 교체) → 준비 오브젝트 앞에서 F
  → 접속자 1~3명 전원 준비 → 5초 카운트다운 → 4.MapScene
  → 판 끝(클리어·전멸·나가기) → 로비 복귀 + 결과 패널(180초 뒤 자동 닫힘, 닫기·ESC)
  → 다시 준비 → … (exe 재실행 없이 반복)
```
- 타이틀은 건너뛴다. 5.ResultScene 은 없다(Build Settings 에서도 뺐다 — 결과는 로비 패널).
- 판이 끝나도 연결과 고른 캐릭터는 그대로, **준비 상태만 풀린다**.
- 새 사람이 들어오면 전원의 준비가 풀린다. 한 명이라도 준비를 풀거나 나가면 카운트다운이 취소된다.
- 수치(카운트다운 5초·상호작용 반경 2m) = `Assets/9.ScriptableObject/Lobby/LobbyRules.asset`.

## 2. PC 별 설정 — 호스트 1대 + 클라 최대 2대
1. **모든 PC 에 같은 빌드**를 깐다(§3).
2. 빌드 폴더(exe 옆)에 샘플 두 개가 들어 있다 — 빌드 메뉴가 넣는다([BuildWindowsPlayer.cs](../../Assets/1.Scripts/Editor/BuildWindowsPlayer.cs), 원본 [BuildSamples/](../../BuildSamples/)).
   - `demo-connection.host.sample.json`
   - `demo-connection.client.sample.json`
3. PC 마다 **하나를 복사해 `demo-connection.json` 으로 이름을 바꾼다.**
   - 호스트 PC: host 샘플.
   - 클라 PC: client 샘플 → `hostAddress` 를 **호스트 PC 의 유선 IPv4** 로 고친다(`ipconfig`).
   - **`playerId` 는 PC 마다 달라야 한다**(예: `booth-1`·`booth-2`·`booth-3`). 같으면 "같은 플레이어 ID 가 이미 접속 중" 으로 거절된다. 재접속해도 캐릭터를 이어 받는 열쇠라서 **행사 중엔 바꾸지 않는다.** 비우면 PC 이름을 쓴다.
4. 파일이 없으면 **호스트**(127.0.0.1 · 7777)로 뜨고 "설정 파일 없음 — 호스트 모드" 가 3초 뜬다. 클라 PC 에서 이 문구가 보이면 파일 이름·위치가 틀린 것이다.

| 필드 | 뜻 | 샘플 값 |
|---|---|---|
| `role` | `host` / `client` | — |
| `hostAddress` | 클라가 붙을 호스트 IPv4(호스트는 무시하고 0.0.0.0 에서 듣는다) | `192.168.0.10`(자리표시) |
| `port` | 모든 PC 가 같아야 한다. 방화벽에서 UDP 허용 | `7777` |
| `playerId` | PC 고유 ID | `booth-1` / `booth-2` |
| `retryIntervalSeconds` | 접속 실패 뒤 다시 시도까지 | `3` (0 초과 60 이하) |
| `connectTimeoutSeconds` | 한 번 시도의 제한 시간 | `5` (0 초과 60 이하) |
| `disconnectTimeoutSeconds` | 응답이 끊긴 뒤 "끊김" 으로 볼 때까지 | `10` (0 초과 300 이하) |

- 틀린 값은 기본값으로 바꾸고 계속 돈다(JSON 이 깨졌으면 통째로 호스트). 파서: [DemoConnectionConfig.cs](../../Assets/1.Scripts/Network/Session/DemoConnectionConfig.cs).
- **R5 끊김 타임아웃:** 랜선이 빠지면 `disconnectTimeoutSeconds`(10초) 안에 대기 화면으로 간다. 너무 짧으면 순간 렉에도 끊기고, 길면 복구가 늦다 — **부스 리허설에서 조정**한다.
- 에디터는 **프로젝트 루트**의 `demo-connection.json` 을 읽는다. 개인 파일이라 `.gitignore` 에 있다 — 커밋하지 않는다.

## 3. 같은 빌드 필수 (R6)
- 접속 승인을 켜서, 빌드가 다르면(NetworkConfig 해시 불일치) 호스트가 사유 없이 끊고 클라는 **"버전 불일치 — 호스트와 같은 빌드인지 확인"** 을 띄우며 재시도한다.
- 대기 화면 맨 아래 줄 `v{버전} · cfg {해시}` 로 비교한다. 해시는 NetworkManager 를 한 번 시작한 뒤에야 나온다(그 전엔 `?`). 접속·호스팅이 되면 대기 화면이 사라지므로 **호스트는 부팅 직후 잠깐만** 보인다 — 의심되면 그냥 같은 폴더를 다시 복사한다.
- 레이드 빌드(`BOSS_RAID_BUILD`)와 일반 빌드도 섞으면 안 된다.

## 4. 대기 화면(`6.DemoConnectScene`) 문구
| 문구 | 뜻 · 할 일 |
|---|---|
| 호스트 대기 중 / 접속 시도 중 (n회째) | 클라가 붙는 중. 호스트가 켜지면 자동 입장 |
| n초 후 재시도 (n회째) | 직전 시도 실패. 그 아래 줄이 사유 |
| 응답 없음 — 호스트를 기다리는 중 | 호스트가 꺼졌거나 `hostAddress`·`port`·방화벽이 틀림 |
| 게임 진행 중 — 판이 끝나면 자동 입장 | 호스트가 판 진행 중이거나 카운트다운 중. **기다리면 된다** |
| 인원 가득 (3/3) | 이미 3명. 4번째 PC 는 들어갈 수 없다 |
| 버전 불일치 — 호스트와 같은 빌드인지 확인 | §3 |
| 같은 플레이어 ID 가 이미 접속 중 | 두 PC 의 `playerId` 가 같다. 끊긴 직후라면 서버가 이전 연결을 정리한 뒤 들어간다 |
| 호스트 시작 중 | 호스트 PC — 포트를 못 열면 재시도한다(같은 PC 에서 exe 두 개를 켰는지 확인) |
| 연결 끊김 — 정리 중 | 접속 중 끊김. 곧 재시도로 넘어간다 |

- 문구 원본: [DemoConnectionOverlayText.cs](../../Assets/1.Scripts/Network/Session/DemoConnectionOverlayText.cs) · [DemoJoinRejectReason.cs](../../Assets/1.Scripts/Network/Session/DemoJoinRejectReason.cs). 판단: [DemoSessionRules.cs](../../Assets/1.Scripts/Network/Session/DemoSessionRules.cs) · [DemoJoinRule.cs](../../Assets/1.Scripts/Network/Session/DemoJoinRule.cs).

## 5. 재접속
- 판 도중 클라가 끊기면 대기 화면 → "게임 진행 중" 으로 계속 재시도 → **호스트가 로비로 돌아오면 자동 입장**. 판 도중 합류는 안 된다.
- 다시 들어오면 **마지막 캐릭터가 유지**된다(`playerId` 기준).
- 호스트가 꺼지면 클라는 전부 "호스트 대기 중" 으로 간다. 호스트 exe 를 다시 켜면 전원 자동 재입장.

## 6. MPPM 에서 검증 (에디터 1개)
- 데모 흐름은 **0.BootStrapScene 에서 Play 할 때만** 켜진다. Dev 부팅·디버그 씬 Play 는 예전 흐름(타이틀 → 로비)이다.
- 메인 에디터 = 호스트(루트에 `demo-connection.json` 이 없거나 host).
- 가상 플레이어는 MPPM 태그 **`Client`** 를 줘야 데모 흐름이 켜진다 → 클라 · 127.0.0.1 로 덮어쓰고 `playerId` 뒤에 인스턴스 이름을 붙인다. 태그가 없으면 예전 수동 흐름이다(같은 포트로 호스트를 시도해 무한 재시도하는 걸 막으려는 것).
- 시나리오 시작 씬은 `0.BootStrapScene`. 체크 목록은 PLAN §7(솔로·MPPM 3인·끊김).

## 7. 알려진 한계 (2026-10-19)
- **캐릭터 선택(C12)** — 로비의 캐릭터 선택 오브젝트(`Environment/LobbyInteractables/CharacterSelectStation`, 스폰 지점 왼쪽) 앞에서 F → 선택 패널 → 고르면 그 자리에서 캐릭터가 바뀐다(디스폰 → 같은 자리 새 Variant 스폰, 교체 VFX 없음 — 1~2 프레임 깜빡일 수 있다).
  - **준비 중·카운트다운 중에는 바꿀 수 없다**(패널은 열리지만 잠금 문구). 교체 뒤 0.5초 쿨다운(`LobbyRules.asset`). 오브젝트에서 멀어진 채 보낸 요청·연타는 서버가 거부하고 **로그만** 남긴다(화면 피드백 없음).
  - 고른 캐릭터는 다음 판 스폰·판 사이·재접속(`playerId`)에 유지된다. 코드: [LobbyInteractor.cs](../../Assets/1.Scripts/Lobby/LobbyInteractor.cs)(패널 연결) · [LobbySession.cs](../../Assets/1.Scripts/Lobby/LobbySession.cs)(`RequestCharacterServerRpc` 검증) · [LobbyPlayerSpawner.cs](../../Assets/1.Scripts/Lobby/LobbyPlayerSpawner.cs)(`TrySwapCharacter`).
- **증강(특성)은 재접속·판 사이에 유지되지 않는다** — 미결. [networking.md "Reconnect identity"](networking.md#reconnect-identity).
- 해시는 대기 화면에서만 보인다(§3).
- 무입력 리셋·스태프 키·코스 스킵·전멸 상한·관람객 디버그 키 차단은 범위 밖(별도 작업). 디버그 단축키(`DEV_HOTKEYS`)는 빌드에 들어 있다 — [debug-hotkeys.md](debug-hotkeys.md).
- Unity Relay 는 쓰지 않는다. **유선 직접 IPv4 만.**
