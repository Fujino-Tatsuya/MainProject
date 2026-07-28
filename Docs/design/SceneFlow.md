# Scene Flow

Last updated: 2026-07-01

## Scene Manager Direction

`GameManager` is kept as the global flow manager. Scene managers are responsible for receiving scene-local UI events and forwarding those events to global managers when global state or scene flow policy is involved.

The current scene manager structure is:

- `NemoSceneManager`: common base class for scene managers.
- `TitleSceneManager`: title UI flow, options panel, start/exit buttons.
- `LobbySceneManager`: lobby scene entry behavior for now.
- `MapSceneManager`: map scene UI bridge, including result transition.
- `ResultSceneManager`: result scene UI bridge, including lobby transition.

## Decisions

- Scene managers can initiate scene flow from their own UI events.
- Global state, network session state, result data, and cross-scene policy remain owned by global managers.
- Scene managers call global manager functions after local transition effects when needed.
- `GameManager` and `Unity.Netcode.NetworkManager` references are retrieved through helper functions on `NemoSceneManager`.
- `GetGameManager()` and `GetNetworkManager()` log a warning immediately if the requested global manager is missing.
- `NemoSceneManager` is an abstract `MonoBehaviour` base class.
- Common fade and transition behavior lives in `NemoSceneManager`.
- Button fields stay in each concrete scene manager because each scene has different UI.
- `NemoSceneManager` only provides a helper such as `SetButtonsInteractable(...)`.
- Inspector references are preferred. Name-based lookup is used only as fallback.
- Scene names remain on the scene manager or existing global manager flow instead of being centralized into a shared constants class.
- Enter fade is played by each scene manager on scene start only when a `blackImage` exists.

## Current Scene Flow Direction

The scene flow is split between local UI scenes and NGO-managed network gameplay scenes.

```text
Bootstrap -> Title -> Lobby -> Loading -> MainGame -> Result
```

- `Bootstrap`, `Title`, `Lobby`, and `Result` are local UI scenes.
- `Loading` and `MainGame` are network scenes loaded through `Unity.Netcode.NetworkManager.SceneManager`.
- `Lobby` and `Result` do not own scene-placed `NetworkObject` state.
- `Lobby` and `Result` read synchronized session data from runtime network state objects and only render local UI.
- The network session remains alive when moving from `Result` back to `Lobby`.

## Session Data Direction

The session uses server-authoritative synchronized data instead of making `LobbyScene` or `ResultScene` network scenes.

- On entering `Lobby`, the player becomes Host by default and creates a solo network session.
- When joining another session through Steam invite, invite code, or matchmaking, the current Host/network state is shut down and the client reconnects as a Client.
- The local `LobbyScene` may remain loaded while the network session is replaced.
- Client-local selection data is not directly referenced by Host objects.
- A Client submits local selection values to the Host through RPC.
- The Host validates or accepts the submitted values and writes them into synchronized network data.
- UI scenes subscribe to synchronized network data and redraw themselves when values change.

## Local Selection Cache

Local player selection values are cached on `GameManager` for the initial implementation. This cache is not the source of truth for gameplay.

- The cache is limited to local-only player selection data such as character id, signature trait id, and fallback display name.
- Character selection and signature trait selection are kept when switching sessions.
- Ready state is not kept when switching sessions.
- After joining a new session, the Client resubmits its cached character and signature trait to the Host.
- Only Host-approved synchronized data is used by Lobby, MainGame, and Result.
- `GameManager` must not own session-wide player lists, ready validation, result aggregation, ServerRpc handling, or synchronized network state.

## Session Network Object Direction

Session-wide network state is owned by a runtime-spawned network object, not by local UI scenes.

- `NetworkSessionRoot` is a `NetworkObject` spawned by Host after `StartHost` succeeds.
- `NetworkSessionRoot` is kept alive for the current NGO session and is destroyed when the session shuts down.
- `NetworkSessionRoot` holds session-scoped `NetworkBehaviour` components such as `NetworkSessionController`, `NetworkSessionCommonData`, and `NetworkResultData`.
- `NetworkSessionLauncher` remains responsible for session boundaries: `StartHost`, `StartClient`, `StartServer`, shutdown, and transport connection data.
- `NetworkSessionController` lives inside the active session and handles session-internal rules such as player registration, player removal, ready/game-start requests, common data updates, and result data coordination.
- `NetworkSessionController` does not search for local UI scene managers.

## Session Registry

`NetworkSessionRegistry` is a local static registry used to bind local UI scenes to the current session object.

- `NetworkSessionRegistry` is not a `NetworkObject`.
- `NetworkSessionRegistry` is not synchronized over the network.
- Each running game process has its own local registry.
- The registry initially stores only `CurrentController`.
- `NetworkSessionController.OnNetworkSpawn()` registers itself in the local registry.
- `NetworkSessionController.OnNetworkDespawn()` unregisters itself.
- UI scene managers subscribe to registry events and bind to `CurrentController` when available.
- If a UI scene loads after the session object already exists, it reads `CurrentController` and binds immediately.
- Static registry state must be reset on play-mode start to avoid stale editor state.

## Common Player Data

Common player data is keyed by NGO `ClientId`.

```text
CommonData.Player[ClientId]
ResultData.Result[ClientId]
```

- `ClientId` is the primary key inside the current NGO session.
- `ClientId` is not a persistent user identity across sessions.
- Steam ID or platform ID may be stored as optional supplemental information later.
- `PlayerSlotIndex` is only a UI display order and must not be used as player identity.

Common player data should include session-wide player information such as:

- `ClientId`
- display name
- selected character
- selected signature trait
- ready state
- connected state
- slot index

`PlayerSessionData` uses explicit field types for readability and debugging rather than bit packing.

```csharp
public struct PlayerSessionData
{
    public ulong ClientId;
    public FixedString128Bytes DisplayName;
    public int CharacterId;
    public int SignatureTraitId;
    public bool IsReady;
    public bool IsConnected;
    public int SlotIndex;
}
```

- `ClientId` stays `ulong` because NGO APIs use `ulong` for client identity.
- `DisplayName` uses `FixedString128Bytes` for Steam persona-name compatibility.
- Steam display names are sanitized and truncated before assignment.
- `CharacterId` and `SignatureTraitId` use `int`.
- `CharacterId` and `SignatureTraitId` are ids, not enums, string keys, or ScriptableObject references.
- `0` should be reserved for `None` where applicable.

Steam display-name ingestion pipeline:

```text
Steam persona name
-> null/empty fallback
-> remove control characters and line breaks
-> normalize or trim whitespace as needed
-> truncate to fit FixedString128Bytes UTF-8 payload
-> store in FixedString128Bytes
-> synchronize through NGO
```

## Ready Policy

Ready means that a player agrees to start the next game with the current lobby configuration.

- Host also has an internal ready state.
- Host UI shows `Start` instead of a separate `Ready` button.
- When Host presses `Start`, Host ready is set to true before start conditions are checked.
- Ready players cannot open character or signature trait selection UI.
- New Clients enter as not ready and do not reset existing players' ready state.
- When a Client leaves during Lobby, that player data is removed and all remaining players become not ready.
- When MainGame entry is confirmed after GameStart, all player ready states are reset to false because the ready agreement has been consumed.

## Character And Trait Policy

- Character duplication is allowed.
- Character duplication is not a GameStart rejection reason.
- Signature trait duplication is allowed.
- Signature trait equip compatibility is handled by local UI masking for now.
- Host-side signature trait validation is deferred until online hardening is needed.

## GameStart Conditions

GameStart is accepted only when:

- the requester is Host;
- minimum player count is satisfied;
- all currently connected players are ready;
- the session is in a state where starting a game is allowed.

If GameStart fails or is cancelled, ready state is preserved and all players receive a message explaining the reason.

## Disconnect Policy

- In Lobby, disconnected player slot and player data are removed immediately.
- In Lobby, any Client disconnect resets all remaining players' ready states to false.
- In MainGame, disconnected players keep their data and are marked `IsConnected=false`.
- In Result, disconnected player data is synchronized to Result UI first, then can be removed.

## Result Data Direction

Result data is matched to common player data by `ClientId`.

- Result data is initialized when MainGame entry is confirmed.
- Host clears previous result data.
- Host creates one result entry for each currently connected player in common player data.
- MainGame systems collect and update result data by `ClientId`.
- Boss death or game end causes Host to finalize result aggregation.
- ResultScene subscribes to result data and common player data to render the final UI.

## End Game To Result Flow

`ResultScene` is a local UI scene, but Result entry timing is synchronized by Host.

Recommended flow:

```text
Boss defeated
-> Host confirms game end
-> Host aggregates NetworkResultData
-> Host sets game end state such as Clear or Fail
-> Host sends ClientRpc to play the end sequence
-> Clients play the local clear/fail sequence
-> Clients report sequence completion to Host
-> Host waits for all connected Clients or a timeout
-> Host sends LoadResultSceneClientRpc
-> Each Client locally loads ResultScene with Unity SceneManager
-> ResultScene displays synchronized result data
```

`LoadResultSceneClientRpc` is used because all connected Clients should enter ResultScene at nearly the same time.

## Result To Lobby Flow

Result to Lobby return is not synchronized as a network scene transition.

- Each Client presses its own `Return To Lobby` button.
- Each Client locally loads `LobbyScene` with `UnityEngine.SceneManagement.SceneManager`.
- `NetworkManager.Shutdown` is not called.
- The existing network session remains alive.
- `LobbyScene` re-subscribes to the current synchronized session data.
- A Client may be in Lobby while another Client is still in Result.
- Host may already be in Lobby and still process valid Result-related RPCs from Clients that remain in Result.

## Session Phase Meaning

Session phase is kept, but it does not directly drive scene changes.

- Session phase is a server-authoritative request gate.
- Session phase defines which RPC/request types the Host accepts.
- Session phase is not the same thing as each Client's current local scene.
- Client local scene and session phase may temporarily differ.

## Pending Grill Decisions

These items are intentionally not finalized yet and should be revisited before implementation.

- Decide the exact `SessionPhase` values and which ServerRpc or ClientRpc requests each phase allows.
- Decide the exact `NetworkSessionController` public API and which requests are ServerRpc, ClientRpc, or local methods.
- Decide how `NetworkSessionRoot` prefab spawning is triggered after `NetworkSessionLauncher.StartHost()` succeeds.
- Decide the exact player join flow: when Host creates the player entry, when Client submits cached selection, and how duplicate submissions are handled.
- Decide how `GameManager` stores local character id, signature trait id, and fallback display name without becoming the owner of synchronized session state.
- Decide the display-name sanitizer implementation, including rich-text handling for TextMeshPro UI.
- Decide minimum player count rules for solo, local demo, and multiplayer.
- Decide timeout duration and failure handling for end-sequence completion before `LoadResultSceneClientRpc`.
- Decide the concrete fields for `PlayerResultData` and which MainGame systems are allowed to write result data.
- Decide whether Result disconnected-player data is removed immediately after Result UI synchronization or kept until returning to Lobby for debugging.

## Transition API

`NemoSceneManager` supports two transition paths:

```csharp
protected IEnumerator TransitionToScene(string sceneName)
```

Use this for simple fade-then-load transitions.

```csharp
protected IEnumerator FadeThenInvoke(System.Action action)
```

Use this when a scene manager should fade first, then call a global manager function such as `GameManager.GoToLobby()` or `GameManager.GoToResult()`.

## Current Implementation Notes

- `TitleSceneManager.StartGame()` fades first, then calls `GameManager.GoToLobby()`.
- `TitleSceneManager.ExitGame()` fades first, then quits play mode or the application.
- `MapSceneManager.GoToResult()` fades first, then calls `GameManager.GoToResult()`.
- `ResultSceneManager.GoToLobby()` fades first, then calls `GameManager.GoToLobby()`.
- Existing persistent button events that still call `GameManager.GoToLobbyButton()` or `GameManager.GoToResultButton()` are bridged in `TempGameManager.cs` so they delegate to the current scene manager when one exists.
- `LobbySceneManager` currently only plays scene enter fade. More lobby-specific UI forwarding can be added as lobby buttons are finalized.

## Files

- `Assets/1.Scripts/Managers/NemoSceneManager.cs`
- `Assets/1.Scripts/Managers/TitleSceneManager.cs`
- `Assets/1.Scripts/Managers/LobbySceneManager.cs`
- `Assets/1.Scripts/Managers/MapSceneManager.cs`
- `Assets/1.Scripts/Managers/ResultSceneManager.cs`
- `Assets/1.Scripts/Managers/TempGameManager.cs`
- `Assets/2.Prefabs/Managers/LobbySceneManager.prefab`
- `Assets/2.Prefabs/Managers/MapSceneManager.prefab`
- `Assets/2.Prefabs/Managers/ResultSceneManager.prefab`

## Verification

`dotnet build MainProject.sln` passed after the implementation.

Remaining warnings are unrelated to the scene manager change:

- `System.Net.Http` assembly version conflict warning from Unity/editor references.
- Existing unused local variable warning in `Assets/1.Scripts/Map/MapOverviewUI.cs`.
