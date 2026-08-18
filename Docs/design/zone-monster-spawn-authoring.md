# 존 프리팹에 몬스터 스폰 넣기 — `MonsterSpawner` 저작 가이드

> **대상**: 맵/아트 담당 · **작성**: 경석(팀장) · **최종 갱신**: 2026-08-18
> **한 줄**: 존 프리팹에 **`MonsterSpawner`** 를 붙이고 몬스터 프리팹만 지정하면 끝.
> 위치를 잡고 싶으면 **빈 오브젝트에 `MonsterSpawnPoint`** 를 붙여 자식으로 두면 **자동으로 수집**된다.

---

## 0. 3분 요약

| | |
|---|---|
| **어디에 붙이나** | 존 프리팹 **루트** — `Assets/2.Prefabs/Map/Zoneprefab/` |
| **뭘 붙이나** | `MonsterSpawner` **+ `NetworkObject`** (둘 다 필요, 아래 [1단계](#1단계--존-프리팹에-monsterspawner-붙이기)) |
| **최소 저작** | `Default Monster Prefab` 칸에 몬스터 프리팹 하나 |
| **위치 지정** | 빈 오브젝트 + `MonsterSpawnPoint` 를 **자식으로** 두면 끝. 목록에 등록할 필요 **없다** |
| **몬스터 프리팹** | `Assets/2.Prefabs/Monster/` — ChompBot · MortarBot · PeekABot · HumanoidBot · SpinnerBot · WallBot · GauntletBot |

⚠️ **존 프리팹 위치를 헷갈리지 말 것.** 정본은 `Assets/2.Prefabs/Map/Zoneprefab/` 이다
(맵 생성이 쓰는 `ZoneLayoutCatalog` 가 이 11개를 가리킨다).
`LevelDeliveryV3/Zones/` 아래 `PF_Zone_*_V3` 는 **카탈로그에 없어 맵에 안 나온다.**

---

## 1단계 — 존 프리팹에 `MonsterSpawner` 붙이기

1. `Assets/2.Prefabs/Map/Zoneprefab/` 에서 프리팹을 더블클릭해 **프리팹 편집 모드**로 연다
2. **루트** 오브젝트를 선택
3. `Add Component` → **`Monster Spawner`**
4. `Add Component` → **`Network Object`** ← 🔴 **이걸 빠뜨리면 몬스터가 한 마리도 안 나온다**

> **왜 `NetworkObject` 가 필요한가**
> `MonsterSpawner` 는 네트워크 스크립트다. 서버가 이 오브젝트를 네트워크에 올려 줘야
> 스폰 코드가 돌기 시작한다. `NetworkObject` 가 그 표식이다.
> 몬스터는 **서버만** 만들고 다른 사람 화면엔 자동 복제된다 — 그래서 이 규약이 필요하다.

### 인스펙터 값

| 칸 | 무엇 | 권장 |
|---|---|---|
| **`Default Monster Prefab`** | 기본으로 스폰할 몬스터 | `Assets/2.Prefabs/Monster/` 에서 끌어다 놓기 |
| `Spawn Points` | 스폰 지점 목록 | 🔵 **비워 둔다** — 비우면 **자식에서 자동 수집**한다 |
| `Auto Spawn On Start` | 시작하자마자 스폰 | 켜 둔다(기본값) |
| `Max Alive` | 동시 생존 상한 | `0` = 무제한(기본값) |

**여기까지만 해도 동작한다.** 자식에 스폰 지점이 하나도 없으면 스폰도 없으니, 최소 한 개는 아래 2단계로 만든다.

---

## 2단계 — 스폰 위치 만들기 (`MonsterSpawnPoint`)

1. 프리팹 루트에서 우클릭 → `Create Empty` → 이름 **`SpawnPoint_0`** 처럼
2. 몬스터를 세울 자리로 **이동**시킨다
3. `Add Component` → **`Monster Spawn Point`**
4. 필요한 개수만큼 반복

🔵 **`MonsterSpawner` 의 `Spawn Points` 목록에 등록하지 않아도 된다.**
비어 있으면 스포너가 **자식 계층 전체에서 자동으로 찾아 쓴다.** 손이 덜 간다.
(목록에 하나라도 넣으면 **그 목록만** 쓰이니, 자동 수집을 쓸 거면 계속 비워 둔다.)

### 위치 규칙

- **바닥 위에 둔다.** 공중에 두면 몬스터가 떨어지거나 낀다
- 마커의 **회전(Rotation)** 이 몬스터가 처음 바라볼 방향이다. 벽 쪽을 보게 두지 말 것
- **크기(Scale)는 아무 의미 없다.** 건드릴 필요 없다
- 씬 뷰에 **빨간 동그라미 + 정면 선**으로 표시된다. 선택 안 해도 보인다

### `MonsterSpawnPoint` 인스펙터

| 칸 | 무엇 | 기본 |
|---|---|---|
| **`Monster Prefab Override`** | 이 자리에만 **다른 몬스터**를 세우고 싶을 때 | 비움 = 스포너의 기본 몬스터 |
| **`Count`** | 이 지점에서 스폰할 **마리 수** | `1` |
| **`Scatter Radius`** | 여러 마리일 때 **원형으로 흩어지는 반경** | `1.5` |

- `Count` 가 2 이상이면 그 수만큼 **원형으로 균등 배치**된다
- `Count > 1` 이면 씬 뷰에 **주황 원**으로 분산 반경이 보인다
- 몬스터를 섞고 싶으면 → 스폰 지점을 여러 개 만들고 각각 `Monster Prefab Override` 를 다르게

---

## 3단계 — 확인

프리팹 저장(`Ctrl+S`) 후 씬 뷰에서:

| 보이는 것 | 뜻 |
|---|---|
| 🔴 **빨간 동그라미 + 선** | 스폰 지점 1개 (선 = 몬스터가 볼 방향) |
| 🟠 **주황 원** | `Count > 1` 일 때의 분산 반경 |

Play 후 Console 에서 아래 에러가 없으면 정상이다.

```
[MonsterSpawner] 스폰할 프리팹이 없습니다. (point=SpawnPoint_0)
  → Default Monster Prefab 이 비었고 Override 도 비었다

[MonsterSpawner] 프리팹 'XXX'에 NetworkObject가 없습니다.
  → 몬스터 프리팹을 잘못 지정했다. Assets/2.Prefabs/Monster/ 것을 쓸 것
```

---

## 🔴 반드시 알아야 할 제약 — 맵 생성으로 깔리는 존에서는 아직 안 돈다

**존 프리팹을 씬에 직접 놓고 쓰면 위 방법 그대로 동작한다.** (`MonsterScene` 이 그렇게 되어 있다.)

그런데 **맵 생성기가 자동으로 깔아 주는 존**에서는 지금 상태로는 **동작하지 않는다.**

- 맵 생성기(`MapContentSpawner`)는 존 프리팹을 **일반 오브젝트로만** 만든다 —
  네트워크에 올리지(`Spawn()`) 않는다
- 코드 주석에 **"존 프리팹은 비네트워크 규약"** 이라고 명시돼 있다
- 그래서 존에 `NetworkObject` + `MonsterSpawner` 를 붙여도 **스폰 코드가 시작되지 않는다**

**즉 지금 당장 쓸 수 있는 경우 / 없는 경우:**

| 상황 | 동작 |
|---|---|
| 존 프리팹을 **씬에 직접 배치** | ✅ 위 1~3단계 그대로 동작 |
| **테스트 씬**(`MonsterScene` 등)에 스포너 배치 | ✅ 동작 (지금 그렇게 쓰고 있다) |
| **맵 생성기가 깔아 주는 존** | ❌ **프로그래머 작업 필요** (존을 네트워크로 올리는 처리) |

**아트 작업 자체는 지금 해 두셔도 된다.** 마커 배치와 값 세팅은 그대로 남고,
맵 생성 경로 연결은 프로그래머가 붙이면 그때부터 그대로 살아난다.

---

## 하지 말아야 할 것

- ❌ 존 프리팹에 **몬스터 프리팹을 자식으로 직접 넣기** — 네트워크 복제가 안 돼 호스트에만 보인다
- ❌ `MonsterSpawner` 만 붙이고 **`NetworkObject` 를 안 붙이기** — 가장 흔한 실수
- ❌ `Spawn Points` 목록에 **일부만** 등록 — 목록이 비어 있지 않으면 **자동 수집이 꺼진다**
- ❌ `Assets/2.Prefabs/Map/LevelDeliveryV3/Zones/` 프리팹 수정 — 카탈로그에 없어 맵에 안 나온다
- ❌ `Assets/2.Prefabs/Monster/` 밖의 아무 프리팹이나 지정 — `NetworkObject` 가 없으면 에러

---

## 쓸 수 있는 몬스터 프리팹

전부 `Assets/2.Prefabs/Monster/` 에 있고 네트워크 등록까지 되어 있다.

| 프리팹 | 성격 |
|---|---|
| `ChompBot` | 근접 |
| `MortarBot` | 원거리 포격 |
| `PeekABot` | 포탑형 |
| `HumanoidBot` | 근접 |
| `SpinnerBot` | 중간보스급 |
| `WallBot` | 중간보스급 |
| `GauntletBot` | 중간보스급 |

---

## 관련 코드

| | |
|---|---|
| 스포너 | `Assets/1.Scripts/Monster/MonsterSpawner.cs` |
| 스폰 지점 | `Assets/1.Scripts/Monster/MonsterSpawnPoint.cs` |
| 맵 생성 존 배치 | `Assets/1.Scripts/Map/MapContentSpawner.cs` |
| 존 카탈로그 | `Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset` (SVN) |
