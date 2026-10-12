# 프리팹 에셋의 `GlobalObjectIdHash` 가 불안정하다 — 씬 타입 오염 · 0 · 재현 불가

| 항목 | 내용 |
|---|---|
| 발견 | 2026-10-12, `GauntletBot.prefab` 의 해시가 작업과 무관하게 바뀐 것을 커밋 전 점검에서 잡음 |
| 상태 | **원인 확정 · 미수정** (영향 범위 미측정 — Play 재현은 아직 안 해봤다) |
| 영향 | 해당 프리팹이 **특정 피어에서만 스폰되지 않을 수** 있다. 지금 당장은 전원이 같은 값을 받아 쓰고 있어 조용하다 |
| 확인 환경 | Unity 6000.3.16f1, NGO `com.unity.netcode.gameobjects` 2.12.0, 브랜치 `feature/VFX`(HEAD `b6c04f2f`) |
| 담당 | 발견·분석 민경 / 판단 필요 = 경석(몬스터·보스), 은희(네트워크) |
| 관련 | [2026-10-07-ngo-inscene-hash-stale.md](2026-10-07-ngo-inscene-hash-stale.md) — **같은 값의 씬 배치 쪽 사고.** 이 문서는 프리팹 에셋 쪽이다 |

## 1. 이 값이 무엇인가

NGO 가 "이 프리팹을 스폰해라" 를 네트워크로 말할 때 쓰는 식별자다. 서버가 스폰 메시지에
이 `uint` 를 싣고, 클라이언트는 자기 쪽 `NetworkPrefabs` 목록에서 같은 숫자를 가진 프리팹을
찾아 생성한다. 못 찾으면 그 오브젝트만 조용히 안 뜬다.

🔴 **이 숫자는 프리팹 파일 안에만 있다.** `Assets/DefaultNetworkPrefabs.asset` 은 guid + fileID
로만 참조하고 해시를 들고 있지 않다(확인함). 즉 **각 피어가 자기 디스크의 프리팹에서 읽는다** —
두 사람의 파일 값이 다르면 그 프리팹만 한쪽에서 스폰되지 않는다.

에디터가 `OnValidate` 에서 만든다 (`NetworkObject.cs:284-285`):

```csharp
var globalId = GlobalObjectId.GetGlobalObjectIdSlow(this);
GlobalObjectIdHash = globalId.ToString().Hash32();   // XXHash32, seed 0, UTF-8
```

`GlobalObjectId.ToString()` 형식은
`GlobalObjectId_V1-{identifierType}-{assetGUID}-{targetObjectId}-{targetPrefabId}` 이고,
`identifierType` 은 NGO 가 상수로 들고 있다 (`NetworkObject.cs:119-122`):

| 값 | 뜻 |
|---|---|
| 0 | Null |
| **1** | **ImportedAsset — 프리팹 에셋. 여기 들어가야 할 값** |
| 2 | SceneObject — 씬에 놓인 오브젝트 |
| 3 | SourceAsset |

## 2. 확정 근거 — 해시를 역산했다

XXHash32(seed 0) 를 직접 구현해 **저장된 해시로부터 원본 `GlobalObjectId` 문자열을 복원**했다.
추정이 아니라 비트 단위로 일치한다.

계기가 된 `GauntletBot.prefab` 의 두 값:

```
2119361380  ←  GlobalObjectId_V1-1-da3f41ab…-6648705861914832438-0
3927606487  ←  GlobalObjectId_V1-2-da3f41ab…-6648705861914832438-0
                                 ↑ identifierType 만 다르다
```

guid 도 `NetworkObject` 의 fileID 도 그대로다. **프리팹 에셋인데 "씬 오브젝트(2)" 로 분류된 채
`OnValidate` 가 돌아서 그 값이 에셋 파일에 박힌 것**이다. 이 프리팹은 `MonsterScene`·
`TrashMobScene` 에 놓여 있고 `ZoneL_typeA.prefab` 안에도 중첩돼 있어 그런 상황이 자주 생긴다.

그래서 히스토리가 **내용 변화 없이 같은 두 값 사이를 네 번 왕복**했다:

| 커밋 | 변화 |
|---|---|
| `6defc71c` | 3927606487 → 2119361380 |
| `5092995b` | 2119361380 → 3927606487 |
| `dd71c59a` | 3927606487 → 2119361380 |
| (2026-10-12 작업 트리) | 2119361380 → 3927606487 → **되돌림** |

> `GauntletBot` 은 10-12 에 type 1 로 되돌려 두었다. 아래 목록에는 포함하지 않는다.

## 3. 전수 조사 결과

`Assets/2.Prefabs` 아래 `NetworkObject` 를 가진 프리팹 **21개** 전부를 같은 방법으로 역산했다.

### A. 씬 타입(2) 오염 — 3개 · **이미 커밋돼 있다**

| 프리팹 | 현재 해시 | 목록 등록 | 마지막으로 이 줄을 바꾼 커밋 |
|---|---|---|---|
| `Monster/ChompBot.prefab` | 3936025811 | O | `47a93f7f` 잡몹 8종 붕괴 사망 배선 |
| `Monster/P_MonsterProjectile.prefab` | 2141990396 | O | `fa9b8d5a` 투사체 궤적 scale |
| `Environment/Layouts/Zones/bossroom.prefab` | 4130406446 | – | `8dbb04fa` 보스룸 수정함 |

셋 다 씬이나 다른 프리팹 안에 **놓여 있는** 프리팹이다 — `ChompBot` 은 `MonsterScene`·
`TrashMobScene` 과 Zone 프리팹 3종, `bossroom` 은 `4.MapScene` 을 포함한 씬 4개.
`GauntletBot` 과 같은 조건이다.

`P_MonsterProjectile` 은 씬에 안 놓여 있고 데이터 SO 5개에서만 참조된다. 왜 씬 타입이
찍혔는지는 **미확인** — 과거에 씬에 꺼내 두고 작업한 흔적일 수 있다(추정).

### B. 해시가 `0` — 1개

| 프리팹 | 현재 해시 | 목록 등록 | 마지막 커밋 |
|---|---|---|---|
| `TrainingDummy.prefab` | **0** | O | `c76796a7` 연습장 허수아비 추가 |

`OnValidate` 가 유효한 값을 한 번도 못 쓴 상태다. `DefaultNetworkPrefabs` 에 **등록은 돼 있다.**
NGO 는 0 을 유효한 키로 보지 않으므로 **네트워크 스폰이 필요하다면 지금 상태로는 안 된다.**
단독 로컬 배치용이면 문제없다 — 어느 쪽인지 확인이 필요하다.

### C. 현재 에셋 상태로 재현되지 않는 해시 — 4개

| 프리팹 | 현재 해시 | 마지막 커밋 |
|---|---|---|
| `Camera/CameraTestPlayer.prefab` | 4014219241 | `f9b002da` Cinemachine hard look at 해제 |
| `Monster/Boss/Bomb.prefab` | 1878152757 | `6ac669ca` 보스 프리팹 4종 신규 |
| `Monster/Boss/TwentyThree_Solo.prefab` | 3079088629 | `ba66e1bc` No23 단독 변형 |
| `Player/GameRule.prefab` | 2493109305 | `d87eeeb7` GameRule.prefab 분리 |

현재 guid 와 **이 파일 안의 모든 fileID 조합**(`identifierType` 0~3 × 모든 블록 ×
`targetPrefabId` 전수)으로 시도했는데 하나도 안 맞는다. 나머지 13개는 전부 type 1 로 깔끔히
복원됐으므로 역산 방법 자체는 맞다.

**해석(추정)**: 지금 에셋 상태에서 계산한 값이 아니다 — 프리팹을 복제·이름 변경했거나
`NetworkObject` 를 떼었다 붙여 fileID 가 바뀐 뒤 재검증이 안 된 **낡은 값**으로 보인다.
이게 맞다면 10-07 문서의 씬 배치 사고와 **같은 실패 형태**다: 에디터는 파일에 저장된 값을
쓰고 빌드는 다시 계산한 값을 쓰므로, 둘이 붙으면 어긋난다.

### D. 정상 — 13개

`FireFloor` · `TwentyThree` · `GauntletBot` · `HumanoidBot` · `MortarBot` · `PeekABot` ·
`SpinnerBot` · `TeslaBot` · `WallBot` · `GunnerTrackingLaser` · `Paladin` · `Paladin_VFX` ·
`Player` — 전부 `identifierType 1` 로 복원된다.

## 4. 왜 지금은 조용한가 (그리고 언제 터지나)

**값이 틀려서가 아니라 값이 달라서 터진다.** A·C 의 해시도 "동작하는 숫자" 이긴 하다 —
전원이 같은 파일을 받아 쓰는 한 스폰은 맞는다. 위험한 건 **불안정하다는 것**이다:

- A(씬 타입): 누가 그 프리팹이나 그게 놓인 씬을 열기만 해도 `OnValidate` 가 type 1 로 다시
  써서 커밋 대상이 된다. 그 커밋을 한쪽만 받은 상태로 붙으면 그 몬스터만 안 뜬다.
- C(재현 불가): 에디터와 빌드가 서로 다른 값을 쓸 수 있다 — 10-07 사고와 같은 구조.
- B(0): 네트워크 스폰이 필요하다면 지금도 안 된다.

증상이 **에러 없이 "그 오브젝트만 안 보인다"** 로 나타나는 게 가장 나쁜 점이다.

## 5. 수정 방향

🔴 **코어·몬스터 영역이라 경석·은희 판단이 필요하다. 아래는 제안이다.**

1. **A 3개**: 각 프리팹을 에디터에서 열어 `OnValidate` 가 type 1 값을 쓰게 한 뒤, **그 3개만
   담은 단독 커밋**으로 올린다. 역산으로 type 1 인지 확인하고 올릴 것.
2. **B**: `TrainingDummy` 가 네트워크 스폰 대상인지 먼저 정한다. 맞으면 열어서 해시를 받게
   하고, 아니면 `DefaultNetworkPrefabs` 에서 뺀다.
3. **C 4개**: 열어서 재계산시킨 뒤 값이 바뀌면 그게 낡은 값이었다는 뜻이다. 같은 커밋으로 올린다.
4. 🔴 **어느 경우든 "전원이 같은 커밋을 받은 뒤에 붙는다" 를 공지해야 한다.** 해시 변경은
   받은 사람과 안 받은 사람 사이에서만 깨진다.

## 6. 재발 감지

역산 검사는 Unity 없이 파일만으로 돌릴 수 있다. 커밋 전에 "이 프리팹의 해시가 지금 에셋
상태와 맞는가" 를 기계적으로 답할 수 있으므로, `DevTools/` 에 넣어 두면 같은 함정을 미리 잡는다.

- 방법: `XXHash32(seed 0)` 로 `GlobalObjectId_V1-{1}-{guid}-{NetworkObject fileID}-0` 를 해시해
  파일의 `GlobalObjectIdHash` 와 비교. 다르면 type 2·3 과 0 을 차례로 시도해 무엇으로 찍혔는지 판정.
- 10-12 조사에 쓴 스크립트가 있다. 넣을지는 미정 — 필요하면 민경에게 요청.

## 7. 경과

- **2026-10-12** 민경(Claude): `GauntletBot` 커밋 전 점검에서 발견 → 역산으로 원인 확정 →
  `GauntletBot` 은 type 1 로 되돌림 → 21개 전수 조사 → 이 문서 작성. **나머지 8개는 손대지 않았다.**
