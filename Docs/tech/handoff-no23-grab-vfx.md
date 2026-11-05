# 핸드오프 — 23호 잡기 대상 표식·끌림 VFX

> 2026-11-05 · 민경 확정 · Codex 파일 제작·연결 · 경석 영역 변경 공유.
> 계획: `PLAN.md` 맨 위 「23호 잡기 — 끌려갈 대상 표식 + 끌려갈 때 VFX」.
> Unity를 열지 않았다. **판정·이동·공격 시간·기존 RPC 시그니처·NetworkVariable 무변경**.

## 경석 확인 — 보스 코드 변경 전체

`Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs`에 아래 알림 호출 **6줄만** 추가했다. Hide RPC는 기존 표현식 본문을 블록으로 펼쳤다. 알림 호출을 제거하고 Hide를 원래 표현식으로 돌리면 작업 전 코드와 일치함을 검사했다. 이벤트 선언·대상 계산은 보스 코드에 추가하지 않았다.

| 기존 RPC | 현재 알림 위치(줄) | 추가한 호출 | 의미 |
|---|---:|---|---|
| ShowAttackConeClientRpc(slot, growTime) | 1989 | `No23GrabVfxSignals.Telegraph(this, e, growTime)` | Grab 행·양수 growTime만 예고 시작 이벤트. 콤보별 실제 예고 시간도 기존 인자로 전달 |
| HideAttackConeClientRpc() | 2025 | `No23GrabVfxSignals.ConeHidden(this)` | 예고 종료/취소 후보. 즉시 끌림을 확정하지 않고 VFX가 LateUpdate까지 대기 |
| StartGrabbedElectricClientRpc(target) | 2513 | `No23GrabVfxSignals.Acquired(this, target)` | 기존 NetworkObjectReference로 Player 조회 후 획득 알림. 해석 실패해도 null로 정리 알림 |
| StopGrabbedElectricClientRpc() | 2532 | `No23GrabVfxSignals.Ended(this)` | 전부 정리 |
| StopGrabPulseClientRpc() | 4055 | `No23GrabVfxSignals.Ended(this)` | 전부 정리 |
| StopMagneticPullClientRpc() | 4088 | `No23GrabVfxSignals.Ended(this)` | 전부 정리(AbortAttackChain 조기 반환 앞 경로 포함) |

- **새 RPC 0, 새 NetworkVariable 0**. 기존 ClientRpc 52개 유지. 기존 전송 인자·호출부·게임플레이 코드는 변경하지 않았다. 새로운 런타임 컴포넌트도 NetworkBehaviour가 아닌 MonoBehaviour다.
- 시작/정지 알림은 기존 이펙트 참조의 null 조기 반환보다 앞에 있다. 기존 연출이 미배선이어도 새 연출을 정리할 수 있다. VFX 이벤트 리스너 예외는 브리지에서 로그 처리해 기존 RPC 본문을 막지 않는다.
- **StartMagneticPullClientRpc를 끌림 시작으로 쓰지 않았다.** 실제 코드는 예고 시작에 StartGrabPulse와 함께 호출한다. ReleaseTelegraph의 HideAttackCone RPC가 실제 BeginGrabPull 직전에 호출되므로 그 신호를 사용했다.
- Hide는 정상 종료와 취소가 공유한다. VFX는 Hide 때 PendingPull로 바꾸고, 같은 프레임에 뒤따르는 Stop 신호가 있으면 취소한다. 남은 PendingPull만 LateUpdate에서 재생한다. 보스 코드를 고쳐 취소 이유를 새 네트워크 인자로 보내지 않는다.

## 컴포넌트·대상·원래 위치

- 새 `Assets/1.Scripts/Effects/No23GrabTelegraphVfx.cs` 안에 로컬 static C# 이벤트 브리지 `No23GrabVfxSignals`, 순수 기하 규칙 `No23GrabVfxRules`, 프리팹 부착 컴포넌트가 있다. static 이벤트는 SubsystemRegistration 때 초기화, OnEnable/OnDisable에서 대칭 구독/해제한다. 보스 인스턴스로 필터링한다.
- 플레이어 조회는 **각 피어에 등록되는 `PlayerLifeCycleController.Active`**를 사용한다. `BossPatternTargets.AllPlayers()`는 IsServer 가드가 있으므로 쓰지 않았다. 스폰 상태 + `BossPatternTargets.IsAlive`(생명 상태 Alive)를 검사하며, 예고 동안 매 프레임 들어오면 고리/자기장 선을 만들고 나가거나 죽으면 즉시 회수한다.
- 반경/각도는 ShowAttackCone RPC가 이미 조회한 Grab 행에서 읽는다(현재 No23_B/No23_Raid 반경 8·각도 90). 평면 거리 + 보스 forward 정규화 + dot≥cos(반각), 발밑·360°·forward가 0일 때의 예외도 보스 InAttackCone과 같다. InAttackCone이 무시하는 coneOffsetAngle을 여기서 새로 적용하지 않았다.
- 고리는 **1.15m→0.43m**로 조이며, 실제 RPC growTime(단타 0.7초·콤보 0.6/1.0초)에 맞춘다. 손→몸 선은 알파 0.12→0.85. 손은 프리팹 GrabController의 grabSocket 참조를 그대로 배선했다. 플레이어 몸 중심은 기존 `BossPatternTargets.BodyCenter`를 사용한다.
- 각 표식 대상의 `lastFoot`·`lastBody`를 예고 프레임마다 기록한다. Hide가 오면 **다시 현재 위치를 조회하지 않고 마지막 완료 프레임의 집합·좌표를 유지**한다. 이미 서버/오너의 Push 이동이 적용된 뒤 원래 위치를 읽는 문제를 피한다.
- 끌림 궤적은 고정 원래 몸 위치→보스 정면 도착 위치로 **0.4초** 동안 소멸. 발 위치에는 3줄 긁힘 + 먼지 14개(**0.65초**), 손↔현재 몸에는 전기 테더를 유지한다. 도착점은 서버 GrabPullFrontOffset과 같은 `max(0.5, grabRadius×0.6)`(현재 grabRadius 3.74이면 2.244m). 도착 반경 0.55m 안에 들어오면 1회 착지 스파크(20개 입자 + 방사형 줄기, **0.3초**).
- GrabAcquired 때 모든 고리/자기장 선/테더를 회수한다. 선택된 대상의 전기는 기존 GrabbedElectric이 이어받는다. 아직 도착 스파크를 내지 않은 선택 대상은 이때 1회 낸다. 다른 사람의 도착 스파크는 중복 재생하지 않는다.

## 본인 화면 경고

- 대상이 **`Player.LocalPlayer`일 때만** 이 피어에서 Canvas를 지연 생성한다. 다른 사람의 표식 때문에 내 화면에 경고를 켜지 않는다.
- Screen Space Overlay, sortingOrder 50, UI 레이어 5, CanvasGroup/이미지 raycast 차단. 기존 HUD나 Volume/Post Processing 설정은 변경하지 않았다.
- 512² RGBA 마스크의 가장자리 약 4.5%만 붉게 표시하고 가운데는 알파 0. 예고 동안 3회 맥동, CanvasGroup 알파 0.16~0.34. 범위 이탈·끌림 시작·종료 시 즉시 0으로 한다. Sprite는 FullRect로 생성해 CPU texture readback이 필요 없고, 보스 파괴 시 해제한다.

## 풀링·아트

- 새 `No23GrabFxView.cs`: EffectManager 소유 프리팹 위의 절차적 LineRenderer(고리/조준 틱·전기 줄기·긁힘·스파크). 새로운 서비스나 EffectManager 드라이버는 추가하지 않았다. 각 프리팹의 Shuriken을 기존 드라이버가 재생/반납한다.
- 루프는 `PlayLooping`, 궤적/긁힘/스파크는 기존 `PlayRecallable` 원샷(자동 수명 + 강제 취소 가능)을 사용한다. 매 프레임 `GetInstances(handle, scratch)`로 그 프레임의 view만 조회하고 풀 GameObject를 장기 보관하지 않는다. 정지 때 `ReleaseImmediate`로 취소 잔상을 즉시 지운다. 직접 Instantiate/Destroy로 이펙트를 별도 관리하지 않는다.
- 루프 캐리어의 파티클 방출은 꺼져 있고 LineRenderer만 그린다. 먼지/착지에는 실제 Shuriken 버스트도 있다. 풀 재대출 때 OnEnable/OnDisable이 선의 위치 개수를 0으로 초기화해 이전 대상의 전기가 새지 않는다.
- 청백색 `(0.60,0.94,1)`은 기존 `FX_Magnetic_Pull` 입자의 `(0.5990566,0.9409136,1)` 계열을 따랐다. 기존 GrabbedElectric의 흰 전기와 이어진다. 새 셰이더는 모두 **Piloto UberFXSG**. 전기 재질은 FuzzAdd 계열, 먼지/긁힘은 FuzzAlpha 계열이며 IntegratedEffect 미사용. 바닥과 선이 깊이 소프트 페이드 때문에 사라지지 않도록 새 재질에만 SoftAlpha를 껐다(ZTest는 유지).
- 텍스처: 전기 리본 256×64, 본인 경고 512×512를 기하 알파 마스크로 제작. 먼지 512×512는 기존 FogNoise PNG 복사본에 새 GUID를 부여했다. 선/고리 메시를 런타임에 생성하므로 별도 Mesh .asset은 없다.
- 엔트리는 프리팹 부착 컴포넌트에서 직접 참조한다(카탈로그 enum/core 인터페이스 변경 없음, 첫 사용 시 풀 지연 생성, prewarmCount 0). 루프 엔트리는 해제가 수명을 소유하고 원샷은 0.4/0.65/0.3초를 명시했다.

## 정리 경로

1. 예고 범위 이탈·플레이어 사망/디스폰 → 해당 대상 루프 회수.
2. Stop RPC 3곳·보스 Died·보스 HP 0·IsSpawned false·컴포넌트 OnDisable → 대상/원샷 핸들 전부 회수·경고 0.
3. GrabAcquired → 대상 루프 회수, 기존 붙잡기 연출에 인계.
4. 신호 누락 안전망: 예고 시간+2초가 지나도 종료가 없으면 정리(**타이머로 끌림을 만들지는 않음**), 끌림은 데이터 pullDuration/cycleSpeed +0.75초 뒤 정리.

## 파일 목록

### 변경

- `Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs`: 기존 RPC 안 알림 6곳(+10/-1줄), 위 표가 전부.
- `Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab`: 루트 m_Component 한 항목 + 새 MonoBehaviour(28줄)만 추가. 기존 164객체 중 루트 컴포넌트 목록 외 동일. 기존 GUID 보존.
- `PLAN.md`: 구현/검증 상태 갱신.

### 신규 코드·문서

- `Assets/1.Scripts/Effects/No23GrabTelegraphVfx.cs` + `.meta`.
- `Assets/1.Scripts/Effects/No23GrabFxView.cs` + `.meta`.
- `Assets/1.Scripts/Effects/Editor/No23GrabVfxRulesTests.cs` + `.meta`.
- 본 문서 `Docs/tech/handoff-no23-grab-vfx.md`.

### 신규 아트

기준 경로 `Assets/50.Art/VFX/Common/Boss/No23/Grab/`. 모든 새 에셋에 새 `.meta`, 폴더에도 `.meta`를 추가했다.

- 프리팹 6개 + 각각 `_Entry.asset`: `FX_No23Grab_TargetRing`, `FX_No23Grab_MagneticLine`, `FX_No23Grab_PullTrail`, `FX_No23Grab_GroundScuff`, `FX_No23Grab_PullTether`, `FX_No23Grab_LandingSpark`.
- `Materials/M_No23Grab_Electric.mat`, `M_No23Grab_Scuff.mat`, `M_No23Grab_Dust.mat`.
- `Textures/T_No23Grab_ElectricRibbon.png`, `T_No23Grab_WarningVignette.png`, `T_No23Grab_Dust.png`.

기존 23호 아트는 변경하지 않았다. `No23_B.asset`·`No23_Raid.asset`·보스 `.meta`는 작업 전 바이트와 동일. **Raid Variant는 TwentyThree를 상속하므로 추가 편집 없이 연결된다.** `TwentyThree_Solo.prefab`은 Variant가 아닌 별도 복제본이며 이번 요청 대상이 아니므로 연결하거나 수정하지 않았다.

## 검증·알려진 한계

- **신규 런타임 C#와 테스트를 실제 Unity 6000.3.16f1 DLL·현재 Library/ScriptAssemblies·NUnit에 참조해 Roslyn으로 컴파일: 오류 0.** SerializeField 미대입 경고 8개는 프리팹 YAML 참조 배선을 따로 확인했다. 전체 프로젝트의 Unity import·NGO ILPP 컴파일을 돌렸다는 뜻은 아니다.
- Unity에 포함된 Mono로 실제 Unity 수학 타입을 사용해 기하 규칙 테스트 **9/9 통과**(반경 경계·양쪽 각도 경계·회전/높이·0 forward/발밑·360°·도착 거리). 테스트 소스는 EditMode 재실행용으로 저장했다.
- 새 아트 YAML 15파일/48객체 해석(미해석·잘림 0), 각 파일 fileID 충돌 0, 로컬 참조/GO 컴포넌트/엔트리 연결 검사. 보스 164→165객체, 기존 객체 대조 통과, 손 소켓·6개 엔트리·경고 텍스처 참조 및 Raid 상속 확인.
- 대상·원래 위치는 **각 피어의 로컬 복제본 추정**이다. 서버만 아는 실제 _pulledPlayers나 슈퍼아머 면역을 새로 복제하지 않는다. 슈퍼아머로 안 끌리는 사람에게도 예고/짧은 궤적/테더가 보일 수 있으며, 도착 스파크는 도착을 관측한 사람에게만 낸다. 피어 간 경계·좌표·시각은 네트워크 보간만큼 다를 수 있다.
- Host가 Push를 같은 프레임에 적용해도 직전 프레임 캐시를 사용한다. 첫 예고와 종료 사이에 완전한 표시 프레임이 없을 정도의 큰 hitch는 최신 표식 갱신이 생략될 수 있다.
- Hide/Stop 취소는 같은 프레임에 전달되면 끌림을 만들지 않는다. 두 Reliable RPC가 서로 다른 렌더 프레임에 도착하면 Stop 전 한 프레임의 궤적이 보일 수 있다. 새 네트워크 없이 종료 이유를 완전히 구별할 수 없는 한계다.
- 중도 접속자는 이미 지나간 시작 RPC를 재생받지 않는다. 다음 잡기부터 표시된다. 전 피어의 이벤트 경로에 IsServer 가드를 넣지 않았다.
- **Unity import/Play·MPPM·빌드·실제 색/밝기/가림/경고 UI·급격한 간파·사망·디스폰 연타는 미실행.** 특히 정상 Grab / 전원 회피 / 슈퍼아머 / 도중 진입·이탈 / 예고 간파 / 끌림 중 사망 / GrabAcquired 인계 / 다음 패턴 재진입을 Host·Client 각각 확인해야 한다.
- 임시 스크립트·컴파일 DLL/EXE·테스트 실행기·검증 JSON·기준 프리팹은 **OS Temp/no23-grab-codex/**에만 저장했다.

## 후속 조정(2026-11-05 민경 Play 피드백 — Claude)
- 손 → 플레이어 선이 붙는 몸 높이 = 발밑 + `bodyHeight`(1m, 콜라이더 중심이 바닥 쪽이라 바닥에 붙어 보였다).
- **예고용 `FX_No23Grab_MagneticLine` 은 쓰지 않는다** — 예고부터 `FX_No23Grab_PullTether` 하나가 나오고(예고 동안 0.12 → 0.85 로 진해짐) 끌어오기까지 끊김 없이 이어진다. 프리팹 칸 `magneticLine`·`lineWidth` 삭제, 굵기 = `tetherWidth` 0.1.
- 전기 줄 3가닥: `No23GrabFxView.Segment` 가 가닥마다 [심, 번짐] 두 줄(가닥 = `lineCount / 2`), PullTether·MagneticLine 프리팹 `lineCount` 6. 휘는 정도·지글거림은 프리팹 인스펙터 칸(`centerBend` 0.05 · `outerBend` 0.14 · `jitterAmount` 0.07 · `jitterSpeed` 37).
