# CURRENT PLAN — Relay 로비 신설 (Phase 2) (2026-08-07)

> 상태: **승인 대기**. 브랜치 `feature/SessionTransport` 이어서 사용(Phase 1 이 base).
> grill 완료 — 확정된 결정만 담는다.

## 목표

**기존 IPv4 로비를 그대로 두고**, 같은 씬 안에 **Relay 연결 통로를 신설**한다.
호스트는 조인코드를 발급받아 화면에 표시하고, 참가자는 그 코드로 들어온다.

## 확정된 결정 (grill)

| 항목 | 결정 |
|---|---|
통로 | **기존 `3.BeaverLobby` 안에 Relay 패널 신설.** 새 씬을 만들지 않는다 |
IPv4 | **그대로 유지**(교체 아님). 출시 전 제거 예정 |
라우팅 | `GameManager.lobbySceneName` · 빌드 목록 **무수정** — 로비 슬롯이 하나라 새 씬을 만들면 팀장 담당 영역(부팅 라우팅)을 건드려야 한다 |
Steam | 나중에 **토글 하나 더** 추가하는 형태로 확장 (Phase 3) |

## 현재 이해 (조사 완료)

| 사실 | 근거 |
|---|---|
Relay API 가 패키지에 **이미 있다** | `com.unity.services.multiplayer@…/Runtime/Relay/SDK/IRelayService.cs` (`CreateAllocationAsync`·`JoinAllocationAsync`) |
`UnityTransport.SetRelayServerData(RelayServerData)` 오버로드 존재 | `UnityTransport.cs:815` |
UGS 프로젝트 연결 완료 | `cloudProjectId c5d06f51-…` / `organizationId rangspam` |
Phase 1 추상화가 이미 있다 | `ISessionConnectionProvider` · `BeginHost/BeginClient` · `SessionStartCompleted` |
로비 매니저의 사용자 피드백 창구는 `SetErrorMessage` 하나 | `BeaverLobbySceneManager` 전역에서 사용 |
로비 버튼은 OnClick → `ApplyConnectionData`/`StartHost`/`StartClient`/`ToggleReady`/`StartGameLoading` | `SerializeField` Button 5개 |

## 접근

### A. 고전 Relay API 를 쓴다 (Sessions API 아님)

패키지에 상위 **Sessions API**(`MultiplayerService.CreateSessionAsync`)도 있지만 **쓰지 않는다**.
그쪽은 로비·Relay·NGO 시작을 한꺼번에 소유하려 들어서, 이미 있는
`NetworkSessionLauncher` + `NetworkLoadingFlowController` 계약과 충돌한다.

고전 API 는 Phase 1 프로바이더 모양과 정확히 맞는다 — **Prepare 단계에서 연결 데이터만 채우고,
`NetworkManager.StartHost()` 호출은 런처가 계속 소유**한다.

```
호스트: CreateAllocationAsync(maxConnections: 2)     // 3인 협동 = 호스트 + 2
        → GetJoinCodeAsync(allocation.AllocationId)
        → SetRelayServerData(new RelayServerData(allocation, "dtls"))
        → ShareCode = 조인코드
참가:   JoinAllocationAsync(joinCode)
        → SetRelayServerData(new RelayServerData(joinAllocation, "dtls"))
```

### B. UGS 초기화·로그인 부트스트랩 (신규)

Relay 호출 전에 `UnityServices.InitializeAsync()` + **익명 로그인**이 끝나 있어야 한다.
`UnityServicesBootstrap`(신규)이 **멱등**하게 처리하고, 상태를 프로바이더가 읽는다.

- `RelayConnectionProvider.IsAvailable(out reason)` 이 미초기화·미로그인·`cloudProjectId` 부재를
  **각각 다른 사유 문자열**로 반환한다. "접속 실패"로 뭉개지 않는다.
- 🔴 **MPPM 프로필 분리**: 익명 로그인은 자격증명을 프로젝트 단위로 캐시한다. MPPM 클론들이
  같은 것을 물면 **같은 플레이어로 취급돼 충돌**한다. 로그인 전에
  `AuthenticationService.Instance.SwitchProfile(<클론별 고유값>)` 을 호출한다.

### C. 로비 UI — 패널 추가, 기존 것은 무수정

`BeaverLobbySceneManager` 에 Relay 전용 필드·메서드를 **추가**한다. 기존 IPv4 필드·메서드는 손대지 않는다.

- 추가 `SerializeField`: `relayPanel` · `directPanel` · `joinCodeInputField` ·
  `joinCodeDisplayText` · `relayHostButton` · `relayJoinButton` · `modeToggleButton`
- 추가 public 메서드(OnClick 대상, 전부 `void`): `SelectDirectMode()` · `SelectRelayMode()` ·
  `StartRelayHost()` · `StartRelayJoin()`
- **비동기 결과는 `SessionStartCompleted` 구독으로 받는다** — Phase 1 이 만든 경로다.
  성공 시 `joinCodeDisplayText` 에 `ShareCode` 를 띄우고, 실패 시 `FailureReason` 을
  `SetErrorMessage` 로 흘린다.
- 연결 중에는 버튼을 잠근다(중복 클릭이 Allocation 을 두 번 만든다).

⚠️ 프리팹·씬 배선(패널·입력칸·버튼 생성 및 참조 연결)은 **은희가 Unity 에서** 한다.
코드는 참조가 비어 있어도 예외 없이 동작해야 한다(전부 null 안전).

## 변경 파일

| 파일 | 변경 |
|---|---|
`Assets/1.Scripts/Network/Session/RelayConnectionProvider.cs` | **신규** |
`Assets/1.Scripts/Network/UnityServicesBootstrap.cs` | **신규** — 멱등 초기화 + 익명 로그인 + MPPM 프로필 분리 |
`Assets/1.Scripts/Network/NetworkSessionLauncher.cs` | Relay 프로바이더 등록(`TryGetProvider` 분기 확장) |
`Assets/1.Scripts/Managers/BeaverLobbySceneManager.cs` | Relay 필드·메서드 **추가**(기존 무수정) |

씬·프리팹·`.meta` 무수정(코드만). `GameManager` 무수정.

## 완료 조건

1. **IPv4 경로 회귀 0** — 기존 IP/Port 접속이 이전과 동일.
2. Relay 호스트 → 조인코드 화면 표시 → 다른 인스턴스가 그 코드로 참가 → 게임 진행.
3. 실패 경로가 **사유와 함께** 표시된다: UGS 미초기화 / 로그인 실패 / 잘못된 조인코드 /
   방 정원 초과. 조용한 실패 0.
4. 카메라 리그·프로바이더·패널 참조가 없어도 예외 0.
5. 컴파일 0 에러 / 0 경고. 신규 `.cs` UTF-8(BOM).
6. MPPM 2~3인에서 Relay 경로 정상(프로필 분리 확인).

## 리스크

- ❓ **대시보드에서 Relay 서비스가 활성화됐는지 미확인.** 안 돼 있으면 코드는 돌아도
  Allocation 생성이 실패한다. Phase 2 검증 전 확인 필요.
- Relay 는 **과금·할당량**이 있는 서비스다. 무료 티어 한도를 넘기면 개발 중에도 막힌다.
- `"dtls"` 연결 타입이 플랫폼·버전에 따라 `"udp"`/`"wss"` 로 달라질 수 있다. 실패 시 사유 로그로 판별한다.
- MPPM 프로필 분리 값을 어떻게 얻을지는 구현 중 확정한다(클론 식별자 API 또는 경로 해시).

---

# PLAN (완료) — 세션 연결 방식 추상화 (Phase 1) (2026-08-07)

> 상태: **완료·검증됨.** 커밋 `3c6be4fc0`, 브랜치 `feature/SessionTransport` 푸시.
> Unity 컴파일 + MPPM 검증 통과(IPv4 동작 무변경 확인).
> 이 문서는 **Phase 1(추상화 + 기존 IPv4 이식)** 만 다룬다. Relay·Steam 구현은 Phase 2·3.
> grill 완료 — 확정된 결정만 담는다.

## 목표

세션 연결 방식을 **세 가지(직접 IPv4 / Unity Relay / Steam)** 로 갈아끼울 수 있게 만든다.
Phase 1의 산출물은 **추상화 계층 + 기존 IPv4 구현을 그 위로 이식**하는 것까지다.
**IPv4 동작은 1바이트도 바뀌지 않는다** — 이게 Phase 1의 합격 기준이다.

## 확정된 결정 (grill)

| 항목 | 결정 |
|---|---|
진행 순서 | **추상화 먼저**, Relay·Steam은 그 위에 건씩 얹는다 |
IPv4 직접연결 | **잠정 유지, 출시 전 제거.** 개발 중 디버깅·랜 환경에서 제일 빠르다 |
비-Steam 로비 | Unity **Relay** (기존 IPv4 직접통신 방침을 대체) |
Steam SDK | 미확정 — Notion 문서(`SteamSDK`)가 인증 걸려 읽지 못했다. Phase 3에서 확정 |

## 현재 이해 (조사 완료)

| 사실 | 근거 |
|---|---|
연결 설정이 **한 곳으로 모여 있다** | `NetworkSessionLauncher.OnSetConnectionData` → `UnityTransport.SetConnectionData` |
`NetworkSessionLauncher`는 `NetworkManager.prefab`의 컴포넌트 | `NetworkClock`·`NetworkLoadingFlowController`와 동거 |
호출자는 로비 매니저 2개 | `BeaverLobbySceneManager`(ip+port) · `LobbySceneManager`(ip only, 1인자 오버로드) |
`CamaraScene.unity`도 이 컴포넌트를 참조 | GUID 스캔 |
**Relay는 트랜스포트를 바꾸지 않는다** | `UnityTransport`가 `SetRelayServerData`로 처리 |
Relay SDK는 **이미 설치돼 있다** | `com.unity.services.multiplayer 2.2.3` + `authentication`·`core`·`qos`·`wire` 해석 완료 |
🔴 **UGS 프로젝트 미연결** | `ProjectSettings.asset`의 `cloudProjectId`·`organizationId`·`projectName` 전부 빈 값 |

## 🔴 핵심 구조 문제 — 동기 API로는 Relay를 표현할 수 없다

현재 계약은 **동기 `bool`** 이다:

```csharp
public bool StartHost()     // 즉시 성공/실패
public bool StartClient()
public void OnSetConnectionData(string ip, ushort port)
```

Relay는 호스트가 **Allocation 생성 → 조인코드 발급**, 클라가 **조인코드로 Allocation 조회** 를 해야 하고
둘 다 **await 가 필요한 원격 호출**이다. Steam도 로비 생성/입장이 콜백 기반이다.
그래서 Phase 1의 본질은 **계약을 비동기로 바꾸고 "연결 중" 상태를 만드는 것**이다.

## 접근

### A. 연결 방식을 인터페이스로 분리

```csharp
public enum SessionConnectionMode { DirectIPv4, UnityRelay, Steam }

/// 사용자에게 보여줄 결과. 실패 사유를 문자열로 들고 온다 —
/// 조용한 실패를 만들지 않는다(이 레포에서 반복해 당한 부류).
public readonly struct SessionStartResult
{
    public readonly bool Success;
    public readonly string FailureReason;
    public readonly string ShareCode;   // 호스트가 남에게 알려줄 값
                                        // IPv4="192.168.0.5:7777" / Relay=조인코드 / Steam=lobbyId
}

public interface ISessionConnectionProvider
{
    SessionConnectionMode Mode { get; }

    /// 쓸 수 있는 상태인지 미리 검사한다. UGS 미연결·Steam 미실행을
    /// "접속 실패"로 뭉개지 말고 이유를 반환한다.
    bool IsAvailable(out string unavailableReason);

    /// 호스트: 트랜스포트에 연결 데이터를 채우고 공유용 코드를 만든다.
    Task<SessionStartResult> PrepareHostAsync(CancellationToken ct);

    /// 클라이언트: 사용자 입력(IP·조인코드·lobbyId)을 해석해 연결 데이터를 채운다.
    Task<SessionStartResult> PrepareClientAsync(string joinInput, CancellationToken ct);
}
```

`Prepare*Async` 는 **트랜스포트 설정까지만** 한다. `NetworkManager.StartHost()` 호출은
`NetworkSessionLauncher` 가 그대로 소유한다 — 시작 순서와 로딩 흐름 콜백 등록을 한 곳에 남긴다.

### B. Phase 1 구현체는 하나뿐 — `DirectIPv4ConnectionProvider`

지금 `OnSetConnectionData` 가 하는 일을 **그대로** 옮긴다. 특히 이 주석의 함정을 보존한다:

> `SetConnectionData` 를 2인자로 부르면 `ServerListenAddress = ip` 가 되어 호스트가 입력값에
> 바인딩된다. 기본값 `127.0.0.1` 이면 루프백만 듣고 다른 PC 가 접속 못 한다.
> → 바인딩은 항상 `0.0.0.0` 고정.

`IsAvailable` 은 항상 true(로컬 전용이라 외부 의존이 없다).
`PrepareClientAsync` 는 `IPAddress.TryParse` 검증을 여기로 **가져온다** — 지금은 로비 매니저에
있는데, 입력 형식 해석은 방식별로 다르므로(조인코드는 IP 가 아니다) 프로바이더 책임이다.

### C. `NetworkSessionLauncher` — 비동기 계약 + 기존 호출자 보호

```csharp
public SessionConnectionMode Mode { get; set; }   // 기본 DirectIPv4
public Task<SessionStartResult> StartHostAsync(CancellationToken ct)
public Task<SessionStartResult> StartClientAsync(string joinInput, CancellationToken ct)
```

- 내부 순서: 프로바이더 `IsAvailable` → `Prepare*Async` → `NetworkManager.Start*()` →
  `RegisterLoadingFlowCallbacks()`. 기존 `Register...` 호출 시점을 바꾸지 않는다.
- **기존 동기 메서드는 남긴다.** `StartHost()`/`StartClient()`/`StartServer()`/`OnSetConnectionData()` 는
  `[Obsolete]` 표시 + 내부에서 DirectIPv4 경로를 동기로 수행하는 얇은 래퍼로 유지한다.
  이유: **UnityEvent OnClick 은 `Task` 반환 메서드를 바인딩하지 못한다.** 씬·프리팹 배선
  (`CamaraScene`, `NetworkManager.prefab`)이 조용히 끊기는 것을 막는다.
- 로비 매니저용으로 `void` 진입점(`BeginHost()` / `BeginClient(string)`)을 추가한다 —
  내부에서 async 를 시작하고 결과를 이벤트로 흘린다:
  `event Action<SessionStartResult> SessionStartCompleted`.

### D. 로비 UI는 Phase 1에서 건드리지 않는다

`BeaverLobbySceneManager` 의 IP/Port 입력 필드는 그대로 둔다. 조인코드 UI 는 **Relay 가 실제로
붙는 Phase 2** 에 함께 바꾼다. Phase 1 은 배관 교체이므로 화면 변화가 0 이어야 검증이 쉽다.

단, `_sessionLauncher.StartHost()` 의 즉시 `bool` 분기는 **"연결 중" 상태를 표현할 수 없다**.
Phase 1 에서는 기존 동기 래퍼를 계속 쓰게 두고, Phase 2 에서 이벤트 기반으로 바꾼다.
(지금 바꾸면 IPv4 동작 무변경을 보장하기 어려워진다.)

## 변경 파일 (Phase 1)

| 파일 | 변경 |
|---|---|
`Assets/1.Scripts/Network/Session/SessionConnectionMode.cs` | **신규** — enum |
`Assets/1.Scripts/Network/Session/SessionStartResult.cs` | **신규** — 결과 struct |
`Assets/1.Scripts/Network/Session/ISessionConnectionProvider.cs` | **신규** — 인터페이스 |
`Assets/1.Scripts/Network/Session/DirectIPv4ConnectionProvider.cs` | **신규** — 기존 동작 이식 |
`Assets/1.Scripts/Network/NetworkSessionLauncher.cs` | 프로바이더 경유 + 비동기 API 추가. **기존 메서드 시그니처 유지** |

프리팹·씬·`.meta` 무수정. 로비 매니저 무수정.

## 스코프 밖 (Phase 2·3)

- **Phase 2 — Relay**: UGS 연결(대시보드·계정 작업, 사용자 몫) → `UnityServices.InitializeAsync` +
  익명 인증 → `RelayConnectionProvider` → 로비 UI 를 조인코드로 교체 → MPPM 프로필 분리.
- **Phase 3 — Steam**: SDK·트랜스포트 확정 → `NetworkConfig.NetworkTransport` 교체 스위처 →
  `SteamConnectionProvider`. **MPPM 으로 검증 불가**(프로세스당 1회 초기화) → 빌드 2개·계정 2개.
- AGENTS.md 의 "공모전 제출 = IPv4" 문구 갱신 — Phase 2 확정 후.
- `LobbySceneManager` 삭제(구 로비 정리) — 별건. `PLAN.md` 2026-08-03 계획에 있다.

## 완료 조건

1. **IPv4 동작 무변경.** `3.BeaverLobby` 에서 IP·Port 입력 → Host/Client 접속이 변경 전과 동일.
   MPPM 2인 정상. `[SceneFlow]` 로그 시퀀스 동일.
2. 씬·프리팹의 `NetworkSessionLauncher` 배선이 유지된다(OnClick 끊김 0).
3. C# 컴파일 0 에러 / 0 경고 (`[Obsolete]` 래퍼를 내부에서 호출하면 경고가 나므로
   호출 지점에 `#pragma warning disable` 대신 **내부 구현을 공유 private 메서드로 분리**한다).
4. `DirectIPv4ConnectionProvider` 가 바인딩을 `0.0.0.0` 으로 고정한다(회귀 시 다른 PC 접속 불가).
5. 신규 `.cs` 는 UTF-8(BOM).

## 리스크

- 🔴 **UGS 미연결이 Phase 2 의 하드 블로커다.** Phase 1 은 영향 없지만, Relay 검증을 시작하려면
  대시보드 작업이 선행돼야 한다. Relay 는 과금·할당량이 있는 서비스다.
- ⚠️ **Steam 은 MPPM 으로 검증할 수 없다.** 지금까지의 검증 습관이 Phase 3 에서 통하지 않는다.
- NGO 는 활성 트랜스포트가 하나다(`NetworkConfig.NetworkTransport` 단일 참조) → Phase 3 에서
  런타임 교체 스위처가 필요하다. Phase 1 인터페이스는 프로바이더가 "어느 트랜스포트를 쓸지"를
  소유할 수 있게 열어 둔다.
- 비동기 도입으로 **취소·중복 클릭** 경로가 생긴다. `CancellationToken` 을 계약에 넣어두고,
  진행 중 재요청은 Phase 2 UI 에서 막는다(Phase 1 은 동기 래퍼만 쓰므로 노출되지 않는다).
