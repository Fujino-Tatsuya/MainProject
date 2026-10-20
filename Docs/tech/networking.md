# 네트워크

> Listen server cooperative PvE (host-client). **Unity Netcode for GameObjects based (base version 2.11.2; competition/debugging: direct IPv4 connection; release: Steamworks API).**

## 권한 모델
- **Player**: movement, input, and animation are owner-authoritative.
- **Server/Host**: every system except player-owned movement, input, and animation is authoritative on the host.
- Anti-cheat is unnecessary for cooperative PvE, so this split keeps player response fast while keeping the network model simple.

## Lobby Demo (target, 2026-10-19)
> Status: **implementation in progress** on `feature/LobbyDemo` (PLAN-48 v2, commits C1~C13). The code may still differ from this section until those commits land.
> Where this section conflicts with the sections below marked **(old / current code)**, this section is the decision for the booth demo.
> Scene flow details: [SceneFlow.md](../design/SceneFlow.md#-target-flow--lobby-demo-2026-10-19-implementation-in-progress).

### Flow
- `Bootstrap → 3.LobbyScene → 4.MapScene → 3.LobbyScene → …` with the session kept alive. No `Shutdown` per game. Title is skipped.
- `3.LobbyScene` is a walkable **NGO scene**. `5.ResultScene` is removed; results open as a lobby result panel only after returning from 4.MapScene.
- All transitions are host-driven `NetworkManager.SceneManager.LoadScene`. Clients never load gameplay scenes locally.
- Map → Lobby: `GoToResult` captures and broadcasts the result payload → `DespawnDynamicNetworkObjects()` (log remaining count) → `LoadScene("3.LobbyScene", Single)`.
- ✅ Implemented: C7a (Map → Lobby NGO direct) · C7b (lobby result panel — opens on lobby entry when `SessionResult.HasValue`; the payload reaches clients before the lobby scene event on the same reliable pipeline). Panel: [lobby-result-panel.md](lobby-result-panel.md).

### Connection config (`demo-connection.json`)
- Location: `Path.GetDirectoryName(Application.dataPath)/demo-connection.json` — next to the exe in builds, project root in the Editor.
- Fields: `role` (`host` / `client`), `hostAddress`, `port`, `playerId` (stable player ID), retry interval / connect timeout / disconnect timeout seconds.
- Missing file → `host` · `127.0.0.1` · `7777`, and a 3-second notice "설정 파일 없음 — 호스트 모드".
- The Host starts hosting on boot; Clients connect automatically. No connection buttons.
- Clients run an **auto-connect / auto-reconnect loop**. While waiting or after a drop they sit in the local scene `6.DemoConnectScene` with the "호스트 대기 중" overlay.
- **Wired direct IPv4 only.** Unity Relay is excluded: the lobby connection panel, Relay UI, and the `RelayConnectionProvider` runtime path are removed. The Relay package stays (no `Packages/manifest.json` change). Steamworks for release is a separate task.

### Connection approval
- `ConnectionApproval` is enabled. Approve only when: the host is in the lobby, the countdown is not running, and fewer than 3 players are connected.
- During a game the request is rejected as "게임 진행 중". The Client keeps retrying and enters automatically once the Host returns to the lobby.
- This keeps "no rejoining / no backfilling during InGame" (see [InGame Leave](#ingame-leave)) — **rejoining is allowed only in the Lobby.**
- Full lobby → rejected as full (3/3).

### Reconnect identity
- A reconnecting Client gets a new `ClientId`. The server store for the last character selection and traits (augments) is keyed by `playerId`, so both are kept across reconnects.

### Lobby rules
- All input is allowed in the lobby (movement, attack, skills).
- Character select object (`F`) → panel → despawn + respawn the new Variant at the same position.
- Ready object (`F`) → when all connected players (1~3) are Ready, a 5-second server-time countdown → `4.MapScene`.
- No character change while Ready or during the countdown. A new connection resets all Ready states. No Host "always Ready" exception.

## 연결 흐름 (old / current code)
> Superseded for the demo by [Lobby Demo (target)](#lobby-demo-target-2026-10-19): Title is skipped and Result is a lobby panel.

1. **Title / Lobby / Loading / InGame / Result**
2. If the Host leaves during InGame, the session ends. The remaining players transition to the Result Scene locally.

## Lobby
- The Lobby always maintains 3 fixed player slots, including empty slots.
- The Lobby UI always displays all 3 player slots.
- Empty player slots are displayed as `Empty`.
- If the Host leaves the Lobby, no scene transition occurs, and all slots except the local player's character slot become `Empty`.
- The game can start only when all connected players are Ready.
- Solo, duo, and trio play are all supported.
- (old / current code) When entering the Lobby Scene, the `NetworkManager` instance must be inactive or in a local-only state, not running as Host.
- (old / current code) The Lobby Scene is always treated as a local scene and is not loaded through `NetworkSceneManager`.
- **Target (lobby demo):** the two rules above are replaced — the Host is already running when the lobby loads, and `3.LobbyScene` is loaded through `NetworkSceneManager`. See [Lobby Demo (target)](#lobby-demo-target-2026-10-19).

## Lobby Ready / Start
- The Lobby start button is bound to `Ready()` for both solo and multiplayer.
- When all connected players are in the Ready state, the game starts after a 5-second countdown.
- If a player becomes Unready or disconnects during the countdown, the countdown is canceled.
- After the countdown starts because all players are Ready, if one player cancels their Ready state, only that player becomes Unready.
- If the countdown is canceled and all players become Ready again, the countdown restarts from 5 seconds.
- Character selection and build selection cannot be changed while the player is in the Ready state.
- If a client disconnects before the countdown starts, all remaining players' Ready states are reset.
- If a new client joins the Lobby, all players' Ready states are reset.
- Clients only send a `RequestReady()` call, and the server validates whether the player can become Ready before changing the Ready state.
- Lobby player data such as Ready state, character selection, and build selection is stored and synchronized using `NetworkVariable` or `NetworkList`.

## Solo Flow (old / current code)
> Superseded for the demo: solo = `host` with no config file. The Host is started on boot, so the lobby countdown always uses server time.

- Solo flow: `Ready()` -> 5-second countdown -> `StartHost()` -> Loading Scene -> InGame Scene.
- In Solo play, the countdown is handled by a local timer because the Host has not started yet.
- In Solo play, if the player becomes Unready during the countdown, the countdown is canceled and the player remains in the Lobby.
- If `StartHost()` fails after the Solo countdown finishes, the player remains in the Lobby, an error message is shown, and the Ready state is reset.
- After `StartHost()` succeeds in Solo play, scene transitions use `NetworkSceneManager`.
- In Solo play, both the Loading Scene and InGame Scene are loaded through `NetworkSceneManager`.

## Multiplayer Flow
- Multiplayer flow 1: connect by entering an invite code.
- Multiplayer flow 2: connect through matchmaking.
- Multiplayer flows will be discussed after gathering information about the Steamworks API.
- Clients cannot join the Lobby while the countdown is in progress.
- Clients cannot join after the game has already started.
- The countdown is managed and synchronized by the server.

## Result Scene (old / current code)
> Superseded for the demo: `5.ResultScene` is removed. The same result data (`SessionResult` + payload named message) is shown in the lobby result panel. "Result Scene" below reads as "lobby result panel" for the demo.

- The Result Scene requires network synchronization on normal game completion.
- On normal game completion, category-based result ratios are calculated by the Host before entering the Result Scene.
- On normal game completion, all players see the full team result in the Result Scene.
- On normal game completion, the Host transitions all players to the Result Scene through `NetworkSceneManager`.
- Team contribution totals, such as total team damage, are not tracked.
- Only individual player contribution is measured.
- Individual contribution categories are deferred.
- The detailed synchronization flow for normal game completion is deferred.

## InGame Leave
- This section only covers Client leave cases where the Host remains connected.
- If a Client leaves during InGame and the Host remains connected, the session continues.
- If a Client leaves during InGame, that Client returns to the Title Scene.
- A manual Client leave and an unexpected Client disconnect are handled the same way, but they display different messages.
- The Host sees different messages for a manual Client leave and an unexpected Client disconnect.
- When a Client leaves during InGame, the remaining players are shown a `Player disconnected` notification.
- During InGame, if a Client leaves, the remaining players briefly see that player as `Disconnected`, then the player is removed from the combat UI.
- When a Client leaves during InGame, that Client's character is removed from the session if possible. If immediate removal is not feasible, the character is disabled.
- When a Client's character is removed from the session, summons created by that character are killed immediately.
- If the leaving Client's character owns a bomb or another special interaction object, it is removed with the character, or immediately dropped and transferred to server ownership if required by game rules.
- Projectiles and area effects created by the leaving Client's character remain active and are destroyed automatically when their duration expires.
- A disconnected Client's remaining projectiles and area effects continue to apply damage until they expire.
- Damage caused by a disconnected Client's remaining projectiles or area effects after disconnection still affects gameplay, but is excluded from Result contribution data.
- If a Client leaves during InGame, boss difficulty and HP scaling are not recalculated.
- If only one player remains after a Client leaves during InGame, the session continues.
- Rejoining the running game is not allowed after a Client leaves during InGame. The Client may rejoin once the Host is back in the Lobby (connection approval, see [Lobby Demo (target)](#connection-approval)).
- Backfilling is not allowed after a Client leaves during InGame.
- (old / current code) A Client that leaves during InGame returns to the Title Scene. **Target (lobby demo):** it goes to `6.DemoConnectScene` and keeps retrying.
- When a Client leaves during InGame, the Host finalizes that player's individual contribution snapshot at the moment of leaving.
- If a Client leaves during InGame, that player's Result data is included up to the point of leaving.
- For normal game completion, disconnected Clients' Result data is based only on authoritative data stored by the Host up to the point of leaving.
- If a Client leaves during InGame, that player remains listed in the Result Scene with a `Disconnected` or `Left` status.
- In the Result Scene, a manually leaving Client is displayed as `Left`, and an unexpectedly disconnected Client is displayed as `Disconnected`.
- `Left` and `Disconnected` Result entries are shown only to the players who remain in the session until Result Scene.
- If the Host leaves after a Client has already left, the abnormal session end rules take priority and all Result data is treated as invalid.

## Abnormal Session End
- All Host leave cases are handled under `Abnormal Session End`.
- If the session ends abnormally because the Host leaves, players transition to the Result Scene locally and the result is treated as invalid.
- If the Host manually leaves or forfeits before normal game completion, the session is treated as abnormal and invalid.
- If the Host leaves after becoming the only remaining player, the session is treated as an abnormal session end.
- In an abnormal Result Scene, all gameplay result values are set to 0.
- The Client only stores and displays the local play time.
- In an abnormal Result Scene, a session invalid message is displayed, such as `Host disconnected`.

## Network Time Management
- During InGame, Host/Server time is the only authoritative network time.
- `ServerTime` and gameplay `GameTime` are separated.
- Clients use synchronized server time only for UI display and visual correction.
- Gameplay decisions are not finalized by client-local time.
- Combat starts only after all connected players finish loading the InGame Scene.
- After all connected players finish loading, the Host sets and synchronizes `CombatStartServerTime`.
- Combat intro, player control unlock, and the first boss pattern are driven by `CombatStartServerTime`.
- `GameTime` starts from the player-controllable combat start point.
- Loading Scene time and non-controllable intro time are not included in play time.
- Result play time is calculated from `CombatGameTime`.
- Gameplay timers use absolute `GameTime` timestamps such as `expiresAtGameTime`, `nextCastAtGameTime`, and `phaseChangeAtGameTime`.
- Timer values are synchronized only when they are created, changed, resumed by object-specific rules, or canceled.
- Remaining time is not synchronized every frame.
- Skill cooldowns, status effects, bombs, area effects, projectile lifetimes, boss pattern schedules, and phase transition timings all use server-authoritative `GameTime`.
- Client UI displays remaining gameplay time as `expiresAtGameTime - synchronizedGameTime`.
- If a Client UI timer reaches 0 before receiving the server-confirmed gameplay event, only the UI display reaches 0.
- Actual gameplay state changes occur only when confirmed by the server.
- `GameTime` is not synchronized every frame. Clients calculate display-only `GameTime` from synchronized baseline values, pause/resume state, and slow-motion events.
- Solo host-only play uses the same clock manager after `StartHost()` succeeds.
- In Solo host-only play, opening the option panel with `Esc` pauses `GameTime`.
- In multiplayer, pressing `Esc` opens only the local option panel and does not pause `GameTime`.
- While a multiplayer option panel is open, that player's gameplay input is blocked, but the character remains in the world and can still be hit by server-authoritative gameplay.
- When `GameTime` is paused, gameplay objects, Ability flow, gameplay UI timers, combat animations/VFX, cooldowns, status effects, bombs, area effects, projectiles, boss patterns, and phase timings are paused.
- `GameTime` also runs slower during an interrupt slow motion (session-wide, not per player). In code, `GameTime` = `NetworkClock.GameNow` = `ServerTime` − paused total − time lost to slow motion.
  - Only the server triggers or fails a slow motion (when it approves an interrupt predicted to hit). It applies the event to its own timeline and sends it to all clients. Each peer then sets `Time.timeScale` through the global `GlobalTimeScale` layer (NetworkManager prefab), using the same closed-form timeline.
  - Gameplay that reads `GameNow` or scaled `Time.deltaTime` slows together: cooldowns, status effects, the boss time limit (`BossTimerManager`), combat VFX/animation, and combat HUD effects.
  - Clients receive the start event one-way-latency late. `GameNow` is clamped so it never runs backward, so client display may differ from the server briefly. This is accepted.
  - Cutscene locks and scene transitions refuse slow motion or reset it to 1.0 immediately. Session end restores `Time.timeScale` = 1.
  - Details, values, and the commit plan are in [PLAN-interrupt-slowmo.md](../../PLAN-interrupt-slowmo.md). Tuning values are in the `SlowMotion` sheet of the data table ([data-table.md](data-table.md)).
- NetworkManager time, transport timeouts, connection state, menu UI input, option panel UI, loading spinners, and other non-gameplay UI continue using real time. They are unaffected by both pause and slow motion (use `Time.unscaledDeltaTime` / `ServerTime`).
- The project does not use a generic individual timer pause system.
- Special mechanics that stop their own countdown, such as bomb fuse timing while flying, are handled as object-specific state rules instead of generic timer pause.
- Lobby countdowns are separate from combat `GameTime`.
- (old / current code) Solo Lobby countdowns use local real time because the Host has not started yet. **Target (lobby demo):** the Host always runs in the lobby, so every lobby countdown uses server time.
- Multiplayer Lobby countdowns use server real time.
- Matchmaking timeouts, connection timeouts, UI fades, loading spinners, and transport/heartbeat timeouts use real time.
- NGO `NetworkConfig.TickRate` starts at 30 ticks per second and may be changed later.
- Unity `Time.fixedDeltaTime` is matched to `1 / TickRate`.
- Server gameplay simulation and gameplay decisions run on fixed ticks.
- Rendering and animation presentation are interpolated separately from fixed tick simulation.
- Ability requests received by the server are queued and processed in the server fixed tick pipeline.
- Ability requests are judged by the server state at the time they are processed, without client input-time compensation.
- If a Client's local display state says an Ability slot is not usable, the Client does not send `RequestUseSlotRpc`.
- The local usability gate is only for responsiveness and traffic reduction. The server still performs final authoritative validation.
- Ability execution is not locally predicted by the Client.
- No gameplay animation, VFX, cooldown, damage, projectile, or area effect starts until the server approves the Ability request.
- When the server approves an Ability request, it assigns an `abilityStartGameTime`.
- `abilityStartGameTime` is the `GameTime` of the server fixed tick that processed and approved the request.
- Server approval events are sent to all clients.
- Owner and non-owner clients play Ability animation and VFX from the same server approval event.
- Cooldown start, cast delay, damage timing, projectile spawning, and area effect spawning use `abilityStartGameTime` as their timing reference.
- Client visual correction using `abilityStartGameTime` is deferred and may be added later if needed.
- If the server rejects an Ability request, no failure UI is shown and the request is treated as if the skill was not used.
- Server-side debug logs may be recorded for rejected Ability requests.
- Input buffering is not used when the local usability gate fails.
- If cooldown expiration and an Ability request occur on the same fixed tick, cooldown expiration is processed first and the request can be approved.
- If status effect expiration and an Ability request occur on the same fixed tick, status expiration is processed first and the request can be approved.
- A player cannot use another Ability while already casting an Ability.
- If Active and Passive Ability processing requests occur on the same fixed tick, Active processing takes priority over Passive processing.
- Multiple players' Ability requests on the same fixed tick are processed in ascending `ClientId` order.
- If boss HP reaches 0 during a fixed tick, combat ends immediately at that point.
- The server fixed tick pipeline should be centralized so event ordering can be changed by reordering pipeline steps.

## Play 종료 후 포트 점유 진단 ([PortLeak])
에디터 전용 진단(`Assets/1.Scripts/Dev/Editor/PlayModePortLeakProbe.cs`). 동작은 바꾸지 않고 로그만 남긴다.
- `ExitingPlayMode` 에 스냅샷(씬의 NetworkManager 수, Singleton 의 IsListening/IsServer/IsClient/ShutdownInProgress,
  UnityTransport 별 포트·드라이버 생성 여부, 에디터 pid)을 `SessionState` 에 보관 → `EnteredEditMode` 2프레임 뒤
  7777 + 종료 시 본 Transport 포트(DevBoot port++ 포함)를 `DevSceneBooter.IsUdpPortInUse` 로 탐침한다.
- 모두 비어 있으면 **로그 없음**. 하나라도 잡혀 있으면 `[PortLeak] Play 종료 후 포트 N 아직 점유` 경고 1개.
- 읽는 법:
  - `소유:` — `netstat -ano -p UDP` 로 찾은 그 포트의 pid. **`이 에디터`면 진짜 누수 후보**,
    `다른 프로세스`면 다른 에디터(Main·Work 레인)나 빌드가 호스트 중인 것이라 **오탐**. netstat 실패면 `알 수 없음`.
  - `종료 시 스냅샷` — Play 를 끝내는 순간 상태. IsListening=True·드라이버 생성=예 였는데 지금도 점유면
    종료 과정에서 소켓이 안 닫힌 것.
  - `편집 모드 스냅샷` — 편집 모드로 돌아온 뒤 상태. 보통 NetworkManager 0개·Singleton=null 이어야 한다.
- 재현되면 받을 것: 경고 전문, Play 전에 한 일(DevBoot 사용 여부·로비/호스트 경로), MPPM 여부,
  같은 PC 에서 다른 에디터가 호스트 중이었는지.

### Play 중 어셈블리 리로드 잠금 (할 일 56.2)
- NGO 종료 순서: `NetworkManager.OnApplicationQuit` → `OnDestroy` → `ShutdownInternal` —
  Despawn → `ConnectionManager.Shutdown`(여기서 transport 종료) → `SceneManager.Dispose` → `IsListening=false`.
- Play 도중 스크립트 컴파일 뒤 도메인 리로드가 일어나면 깨진다. `NetworkSceneManager` 는 `[Serializable]` 이라
  생성자 없이 복원되고, Dictionary 인 `SceneEventDataStore` 는 직렬화가 안 돼 null 로 남는다 → 정지 때
  `NetworkSceneManager.Dispose` NRE 로 `ShutdownInternal` 이 중간에 끊기고, 리로드로 UnityTransport 의 네이티브
  드라이버 핸들도 잃어 UDP 포트가 에디터 프로세스에 남는다(48.3 QA: NRE 난 정지 3회 = 그 Play 중 리로드 3회, 나머지 18회는 NRE·누수 0).
- 가드 `Assets/1.Scripts/Dev/Editor/PlayModeReloadGuard.cs`: `EnteredPlayMode` 에서 `LockReloadAssemblies`,
  `ExitingPlayMode` 에서 해제(`EnteredEditMode` 에 아직 잠겨 있으면 안전망 해제). Play 중 Refresh 는 **컴파일까지만** 하고
  `[ReloadGuard] Play 중 스크립트 컴파일 — 리로드는 Play 정지 후` 경고 1줄을 남긴다. 리로드는 Play 정지 뒤 자동으로 한다.
- 그래서 **에이전트가 Play 중에 Refresh(MCP `AssetDatabase.Refresh`)를 해도 된다** — 바뀐 코드는 정지 뒤 반영된다.
  사용자별 EditorPrefs 인 "Script Changes While Playing" 은 코드로 바꾸지 않는다.

## 패키지 주의
- NGO/Steamworks 패키지를 추가하면 **반드시 `Packages/manifest.json` + `packages-lock.json` 커밋**
  (현재 미추적 — [../../AGENT.md](../../AGENT.md) §3).
