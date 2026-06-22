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
- In the Result Scene