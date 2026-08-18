# 존 프리팹에 몬스터 스폰 지점 저작하기

> **대상**: 맵/아트 담당 · **작성**: 경석(팀장) · **최종 갱신**: 2026-08-18
> **한 줄**: 존 프리팹 안에 빈 오브젝트로 **마커**를 놓고, `ZoneLayout`의 **마커별 목록**에
> 그 마커와 **몬스터 그룹 번호**를 짝지어 넣으면 그 자리에 그 몬스터가 나온다.

---

## 0. 먼저 알아야 할 것 3가지

**① 몬스터 프리팹을 직접 끌어다 놓는 게 아니다.**
존 프리팹에는 **빈 오브젝트(마커)** 만 놓는다. 실제 몬스터는 게임이 돌 때 서버가 그 자리에
만들어 넣는다. 존 프리팹에 몬스터 프리팹을 자식으로 넣으면 **안 된다** — 네트워크로 복제되지 않아
호스트 화면에만 보이거나 아예 안 나온다.

**② 몬스터 종류는 "번호"로 지정한다.**
마커마다 **그룹 번호(MonsterGroupID)** 를 적는다. 번호 ↔ 몬스터 대응표는 `MapGenConfig` 라는
파일 한 곳에 있고, 그 표는 아래 [3번](#3-몬스터-그룹-번호표)에 그대로 옮겨 뒀다.

**③ 건드릴 파일은 두 종류뿐이다.**

| 무엇 | 어디 | 버전관리 |
|---|---|---|
| 존 프리팹 (마커 배치 + 목록 저작) | `Assets/2.Prefabs/Map/LevelDeliveryV3/Zones/PF_Zone_*_V3.prefab` | **git** |
| 몬스터 그룹 번호표 (번호 추가·변경할 때만) | `Assets/50.Art/MapGen/MapObj/MapZoonSettingObj/MapGenConfig.asset` | 🔴 **SVN** |

🔴 **`Assets/50.Art` 는 SVN이다.** 번호표를 고쳤으면 git이 아니라 **SVN으로 커밋**해야 한다.
존 프리팹만 만졌다면 git만 신경 쓰면 된다.

⚠️ **작업할 프리팹을 헷갈리지 말 것.** `Assets/2.Prefabs/Map/Zoneprefab/` 아래에도 `ZoneL_typeA`
같은 **옛날 존 프리팹**이 남아 있는데, 그쪽엔 `ZoneLayout`이 아예 없다. **`LevelDeliveryV3/Zones/`
아래 `PF_Zone_..._V3` 가 정본이다.**

---

## 1. 작업 순서 (그림으로 치면 5단계)

### 1단계 — 존 프리팹 열기

`Assets/2.Prefabs/Map/LevelDeliveryV3/Zones/` 에서 작업할 프리팹을 더블클릭해 **프리팹 편집 모드**로 연다.

> ⚠️ 열면 **"Missing script"** 경고가 뜰 수 있다. 이 프리팹들이 아직 이 브랜치에 없는 스크립트
> (`OcclusionSection` 등)를 참조하고 있어서다. **이미 파악된 별건이고 스폰 작업과 무관하다.**
> 그 경고 때문에 컴포넌트를 지우거나 하지 말 것.

### 2단계 — 루트에 `ZoneLayout` 이 있는지 확인

프리팹 **맨 위(루트) 오브젝트**를 선택하면 인스펙터에 `Zone Layout` 컴포넌트가 보인다.
현재 V3 존 11개에는 **전부 붙어 있다.** 없다면 `Add Component` → `Zone Layout`.

### 3단계 — 마커(빈 오브젝트) 놓기

1. 프리팹 루트에서 우클릭 → `Create Empty`
2. 이름을 **`Spawn_0`, `Spawn_1`, `Spawn_2` …** 로 짓는다 (기존 존들이 쓰는 이름 규칙)
3. 몬스터를 세우고 싶은 자리로 **이동**시킨다

**위치 규칙 — 이걸 어기면 몬스터가 안 나온다:**

- 🔴 **반드시 바닥 위에 둔다.** 게임은 마커 위치에서 **아래로 광선을 쏴서** 바닥을 찾고 그 지점에 몬스터를 세운다.
  - 찾는 범위 = 마커보다 **5m 위에서 아래로 30m**
  - 바닥으로 인정되는 것 = **`Default` 또는 `Ground` 레이어**를 가진 콜라이더
  - 🔴 **바닥을 못 찾으면 그 몬스터는 「존 한가운데」로 끌려간다.** 안 나오는 게 아니라
    **엉뚱한 자리에 나온다** — 잘못 놓은 마커가 여러 개면 **전부 한 지점에 뭉친다.**
    "몬스터가 존 중앙에 겹쳐 있다"가 보이면 십중팔구 이 경우다 (콘솔에 경고가 함께 뜬다)
- 마커의 **회전(Rotation)** 은 몬스터가 처음 바라볼 방향이 된다. 벽 쪽을 보게 두지 말 것
- 높이(Y)는 대충 바닥 근처면 된다. 정확한 높이는 게임이 알아서 맞춘다
- 마커의 **크기(Scale)는 아무 의미 없다.** 건드리지 않아도 된다

### 4단계 — `ZoneLayout` 목록에 등록 (여기가 핵심)

프리팹 루트를 선택하고 인스펙터의 `Zone Layout` → **`Monster Spawn Entries`** 를 편다.

1. `+` 를 눌러 항목을 하나 추가한다
2. **`Marker`** 칸에 방금 만든 `Spawn_0` 오브젝트를 **하이어라키에서 끌어다 놓는다**
3. **`Monster Group ID`** 칸에 몬스터 **번호**를 적는다 ([3번 표](#3-몬스터-그룹-번호표) 참고)
4. 마커 개수만큼 반복

**입력 예시** (`PF_Zone_L_Type_A_V3` 의 실제 저작 상태):

| # | Marker | Monster Group ID | 실제로 나오는 몬스터 |
|---|---|---|---|
| 0 | `Spawn_0` | `7` | GauntletBot |
| 1 | `Spawn_1` | `0` | ChompBot |
| 2 | `Spawn_2` | `2` | PeekABot |
| 3 | `Spawn_3` | `3` | PeekABot |
| 4 | `Spawn_4` | `1` | MortarBot |
| 5 | `Spawn_5` | `4` | HumanoidBot |

이렇게 **마커마다 다른 몬스터**를 섞을 수 있다.

### 5단계 — 저장하고 눈으로 확인

프리팹을 저장(`Ctrl+S`)한 뒤, 루트를 선택한 채로 **씬 뷰에서 기즈모(동그라미)** 를 본다.

| 동그라미 색 | 뜻 |
|---|---|
| 🟢 **초록** | 번호를 제대로 넣었다 (정상) |
| 🔴 **빨강** | 번호가 `-1` 이라 **존 기본값을 물려받는 중** — 의도한 게 아니면 고칠 것 |

동그라미가 **아예 안 보이면** `Marker` 칸이 비었거나 목록에 등록되지 않은 것이다.

---

## 2. 인스펙터 항목 설명 (`ZoneLayout`)

### 꼭 봐야 하는 것

| 항목 | 무엇 | 어떻게 넣나 |
|---|---|---|
| **`Monster Spawn Entries`** | **마커별 몬스터 지정.** 실제로 쓰는 목록 | 위 4단계 |
| ┗ `Marker` | 몬스터가 설 자리 | 빈 오브젝트를 끌어다 놓기 |
| ┗ `Monster Group ID` | 어떤 몬스터인가 | 번호 입력. `-1` 이면 아래 존 기본값을 씀 |
| **`Monster Group ID`** (존 단위) | 위에서 `-1` 을 넣었을 때 쓰이는 **기본 몬스터** | 번호 하나 |

### 이미 세팅돼 있으니 건드리지 말 것

| 항목 | 현재 값의 뜻 |
|---|---|
| `Size` | 존 크기 — `Large` / `Medium` / `Small`. 맵 생성기가 자리를 고를 때 쓴다 |
| `Role` | 존 역할 — `Combat`(전투) / `Quest` / `BossRoom` / `PlayerSpawn` |
| `Difficulty` | 난이도 밴드. 현재 전부 `0` |
| `ThemeName` | 참고용 이름표 |
| `Nodes` | 존 내부 노드. **몬스터 스폰과 별개 시스템** |

### 🔴 `Monster Spawn Points` — 옛날 칸이다. 새로 쓰지 말 것

`Monster Spawn Entries` **바로 위**에 이름이 비슷한 `Monster Spawn Points` 가 있다. 이건 **구버전**이다.

- `Monster Spawn Entries` 에 항목이 **하나라도 있으면** → `Entries` 만 쓰이고 `Points` 는 **완전히 무시**된다
- `Entries` 가 **비어 있을 때만** → `Points` 의 마커들이 전부 **존 기본 `Monster Group ID`** 하나로 스폰된다

**새로 저작할 때는 `Entries` 에만 넣는다.** `Points` 는 옛 데이터를 잃지 않으려고 남겨 둔 것이다.

⚠️ 아직 `Entries` 로 옮기지 않은 존이 **4개** 있다 (아래 [4번](#4-존별-현재-저작-상태) 표의 ▲ 표시).
그 존들은 지금 **마커 전부가 같은 몬스터**로 나온다.

---

## 3. 몬스터 그룹 번호표

`MapGenConfig.asset` 에 등록된 현재 값이다. **여기 없는 번호를 적으면 그 마커는 스폰되지 않는다.**

| 번호 | 그룹 이름 | 실제 몬스터 |
|---|---|---|
| **0** | Chomp Pack | ChompBot |
| **1** | Mortar Squad | MortarBot (원거리 포격) |
| **2** | PeekA Turret | PeekABot |
| **3** | PeekA Turret (Tesla 임시대체) | PeekABot |
| **4** | Humanoid Duo | HumanoidBot |
| **5** | Spinner Elite | SpinnerBot |
| **6** | Wall Elite | WallBot |
| **7** | Gauntlet Elite | GauntletBot |

> `3` 번은 Tesla 몬스터가 나오기 전까지 PeekABot 으로 **임시 대체**해 둔 자리다.
> 나중에 Tesla 가 들어오면 번호표만 고치면 되고, **존 프리팹은 손댈 필요 없다.**

### 번호를 새로 추가하려면 (프로그래머와 함께)

1. `Assets/50.Art/MapGen/MapObj/MapZoonSettingObj/MapGenConfig.asset` 선택
2. `Monster Groups` 에 `+` → `Group ID`(안 겹치는 새 번호) · `Group Name` · `Monster Prefab` 지정
3. 🔴 **몬스터 프리팹은 `DefaultNetworkPrefabs` 에도 등록돼 있어야 한다.** 안 그러면 호스트에만 보인다
   (현재 위 7종은 전부 등록돼 있다)
4. 🔴 **SVN으로 커밋한다.** git 아님

---

## 4. 존별 현재 저작 상태

| 존 프리팹 | 크기 | 역할 | 존 기본 번호 | 마커 | 마커별 지정 |
|---|---|---|---|---|---|
| `PF_Zone_L_Type_A_V3` | Large | Combat | 1 | 6 | ✅ 6 |
| `PF_Zone_L_Type_B_V3` | Large | Combat | 2 | 8 | ✅ 8 |
| `PF_Zone_L_Type_C_V3` | Large | Combat | 5 | 9 | ✅ 9 |
| `PF_Zone_M_Type_A_V3` | Medium | Combat | 1 | 9 | ✅ 9 |
| `PF_Zone_M_Type_B_V3` | Medium | Combat | 3 | 12 | ✅ 12 |
| `PF_Zone_M_Type_C_V3` | Medium | Combat | 4 | 3 | ▲ **0 (구 경로)** |
| `PF_Zone_Quest_01_V3` | Medium | Quest | 2 | 2 | ▲ **0 (구 경로)** |
| `PF_Zone_Quest_02_V3` | Medium | Quest | 3 | 2 | ▲ **0 (구 경로)** |
| `PF_Zone_S_Type_A_V3` | Small | Combat | 1 | 6 | ✅ 6 |
| `PF_Zone_S_Type_Boss_Enter_V3` | Small | BossRoom | −1 | 0 | — |
| `PF_Zone_S_Type_Start_V3` | Small | PlayerSpawn | −1 | 0 | — |

▲ = 마커는 있는데 `Entries` 가 비어서 **전부 같은 몬스터**로 나오는 존. 섞고 싶으면 4단계대로 옮기면 된다.
보스방·시작존에 몬스터가 없는 것은 **의도된 것**이다.

---

## 5. 몬스터가 안 나올 때 — 확인 순서

| 증상 | 먼저 볼 것 |
|---|---|
| **한 마리도 안 나온다** | 그 존이 이번 맵 생성에 **뽑히지 않았을 수 있다.** 맵은 매번 랜덤 조합이다 |
| 🔴 **몬스터가 존 한가운데 뭉쳐 있다** | 그 마커들이 **바닥 위가 아니다.** 허공·구멍 위 마커는 존 중앙으로 대체된다 |
| **특정 마커만 안 나온다** | `Entries` 의 `Marker` 칸이 비었는지 확인 (칸이 비면 그 항목은 통째로 무시) |
| **엉뚱한 몬스터가 나온다** | 번호를 확인. 기즈모가 **빨강**이면 `-1` 이라 존 기본값을 쓰는 중이다 |
| **번호를 적었는데 안 나온다** | 그 번호가 [3번 표](#3-몬스터-그룹-번호표)에 **없는 번호**일 수 있다 |
| **호스트에만 보이고 다른 사람에겐 안 보인다** | 몬스터 프리팹이 `DefaultNetworkPrefabs` 에 등록 안 된 것 — 프로그래머에게 |

**콘솔 로그도 답을 준다.** Play 중 Console 창에서 이런 줄을 찾으면 된다.

```
[MapContentSpawner] 존 비주얼 N / 몬스터 M 스폰 (서버:True)
```

문제가 있으면 바로 위에 원인이 함께 찍힌다. **셋 다 어느 존·어느 마커인지 이름이 나온다.**

**① 번호가 표에 없을 때** — 그 마커는 스폰되지 않는다

```
[MapContentSpawner] <존이름> 마커 3(Spawn_3)의 몬스터 그룹 9 를 해석하지 못해
스폰하지 않습니다 — MapGenConfig.MonsterGroups 확인 필요.
```

**② 마커가 허공일 때** — 존 중앙으로 대체된다 (= 뭉치는 증상)

```
[MapContentSpawner] 스폰 마커가 허공(x, y, z) — <존이름> 바닥 중앙으로 대체. 마커 위치 조정 필요.
```

**③ 존 자체가 바닥과 어긋났을 때** — 이건 마커 문제가 아니라 **존 배치 문제**다. 프로그래머에게

```
[MapContentSpawner] <존이름>에서 바닥을 찾지 못해 몬스터를 스폰하지 않습니다(x, y, z) —
존이 통로와 어긋난 위치에 배치됐을 가능성이 큽니다.
```

그 줄을 그대로 복사해 전달하면 된다.

---

## 6. 하지 말아야 할 것

- ❌ 존 프리팹에 **몬스터 프리팹을 자식으로 직접 넣기** — 네트워크 복제가 안 된다
- ❌ `Assets/2.Prefabs/Map/Zoneprefab/` 의 **옛 존 프리팹** 수정 — 정본이 아니다
- ❌ `Monster Spawn Points`(구 칸)에 새로 추가 — `Entries` 를 쓴다
- ❌ 마커를 **허공이나 바닥 구멍 위**에 배치
- ❌ `Size` / `Role` 변경 — 맵 생성 규칙이 바뀐다. 필요하면 프로그래머와 상의
- ❌ `MapGenConfig.asset` 을 **git으로 커밋 시도** — SVN 파일이다

---

## 관련 문서 / 코드

| | |
|---|---|
| 스폰 실행 | [MapContentSpawner.cs](../../Assets/1.Scripts/Map/MapContentSpawner.cs) — `SpawnEntriesAt`, `TryResolveSpawnPoint` |
| 저작 데이터 | [ZoneLayout.cs](../../Assets/1.Scripts/Map/ZoneLayout.cs) — `MonsterSpawnEntry`, `ResolveSpawnEntries` |
| 번호표 정의 | [MonsterGroupData.cs](../../Assets/1.Scripts/Map/MonsterGroupData.cs) · [MapGenConfigSO.cs](../../Assets/1.Scripts/Map/MapGenConfigSO.cs) |
| 존 분류 열거형 | [MapEnums.cs](../../Assets/1.Scripts/Map/MapEnums.cs) — `ZoneSize` · `ZoneRole` |
