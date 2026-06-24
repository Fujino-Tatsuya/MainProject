# 네트워크

> Listen server cooperative PvE (host-client). **Unity Netcode for GameObjects based (base version 2.11.2; competition/debugging: direct IPv4 connection; release: Steamworks API).**

## 권한 모델
- **Player**: movement, input, and animation are owner-authoritative.
- **Server/Host**: every system except player-owned movement, input, and animation is authoritative on the host.
- Anti-cheat is unnecessary for cooperative PvE, so this split keeps player response fast while keeping the network model simple.

## 연결 흐름
1. **Title / Lobby / Loading / InGame / Result**
2. If the Host leaves during InGame, the session ends. The remaining players transition to the Result Scene locally.

## Lobby
- The Lobby always maintains 3 fixed player slots, including empty slots.
- The Lobby UI always displays all 3 player slots.
- Empty player slots are displayed as `Empty`.
- If the Host leaves the Lobby, no scene transition occurs, and all slots except the local player's character slot become `Empty`.
- The game can start only when all connected players are Ready.
- Solo, duo, and trio play are all supported.
- When entering the Lobby Scene, the `NetworkManager` instance must be inactive or in a local-only state, not running as Host.
- The Lobby Scene is always treated as a local scene and is not loaded through `NetworkSceneManager`.

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

## Solo Flow
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

## Result Scene
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
- Rejoining is not allowed after a Client leaves during InGame.
- Backfilling is not allowed after a Client leaves during InGame.
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
- `GameTime` is not synchronized every frame. Clients calculate display-only `GameTime` from synchronized baseline values and pause/resume state.
- Solo host-only play uses the same clock manager after `StartHost()` succeeds.
- In Solo host-only play, opening the option panel with `Esc` pauses `GameTime`.
- In multiplayer, pressing `Esc` opens only the local option panel and does not pause `GameTime`.
- While a multiplayer option panel is open, that player's gameplay input is blocked, but the character remains in the world and can still be hit by server-authoritative gameplay.
- When `GameTime` is paused, gameplay objects, Ability flow, gameplay UI timers, combat animations/VFX, cooldowns, status effects, bombs, area effects, projectiles, boss patterns, and phase timings are paused.
- NetworkManager time, transport timeouts, connection state, menu UI input, option panel UI, loading spinners, and other non-gameplay UI continue using real time.
- The project does not use a generic individual timer pause system.
- Special mechanics that stop their own countdown, such as bomb fuse timing while flying, are handled as object-specific state rules instead of generic timer pause.
- Lobby countdowns are separate from combat `GameTime`.
- Solo Lobby countdowns use local real time because the Host has not started yet.
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

## 패키지 주의
- NGO/Steamworks 패키지를 추가하면 **반드시 `Packages/manifest.json` + `packages-lock.json` 커밋**
  (현재 미추적 — [../../AGENT.md](../../AGENT.md) §3).
