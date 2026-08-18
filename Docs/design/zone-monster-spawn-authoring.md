# 존 프리팹에 몬스터 스폰 저작하기

> **대상**: 맵/아트 담당 · **작성**: 경석(팀장) · **최종 갱신**: 2026-08-18
> **한 줄**: 존 프리팹 안에 **`NodeMarker` 스크립트**를 붙이고, 거기에 **몬스터 번호 + 스폰 위치들**을
> 넣은 뒤 **`ZoneLayout`의 `Nodes` 목록에 등록**하면 그 자리에 몬스터가 나온다.

---

## 0. 먼저 알아야 할 것 3가지

**① 몬스터 프리팹을 직접 끌어다 놓는 게 아니다.**
존 프리팹에는 **빈 오브젝트(마커)** 만 놓는다. 실제 몬스터는 게임이 돌 때 서버가 그 자리에 만들어 넣는다.
존 프리팹에 몬스터 프리팹을 자식으로 넣으면 **안 된다** — 네트워크로 복제되지 않아 호스트 화면에만
보이거나 아예 안 나온다.

**② 몬스터 종류는 "번호"로 지정한다.**
`Monster Group ID` 라는 칸에 **번호**를 적는다. 번호 ↔ 몬스터 대응표는 `MapGenConfig` 라는 파일
한 곳에 있고, 아래 [3번](#3-몬스터-그룹-번호표)에 그대로 옮겨 뒀다.

**③ 건드릴 파일은 두 종류뿐이다.**

| 무엇 | 어디 | 버전관리 |
|---|---|---|
| 존 프리팹 (노드 배치 + 스폰 위치 저작) | `Assets/2.Prefabs/Map/LevelDeliveryV3/Zones/PF_Zone_*_V3.prefab` | **git** |
| 몬스터 번호표 (번호를 새로 추가할 때만) | `Assets/50.Art/MapGen/MapObj/MapZoonSettingObj/MapGenConfig.asset` | 🔴 **SVN** |

🔴 **`Assets/50.Art` 는 SVN이다.** 번호표를 고쳤으면 git이 아니라 **SVN으로 커밋**해야 한다.
존 프리팹만 만졌다면 git만 신경 쓰면 된다.

⚠️ **작업할 프리팹을 헷갈리지 말 것.** `Assets/2.Prefabs/Map/Zoneprefab/` 아래에도 `ZoneL_typeA`
같은 **옛 존 프리팹**이 남아 있는데 거기엔 `ZoneLayout`이 아예 없다.
**`LevelDeliveryV3/Zones/` 의 `PF_Zone_..._V3` 가 정본이다.**

---

## 1. 작업 순서 — 5단계

### 1단계 — 존 프리팹 열기

`Assets/2.Prefabs/Map/LevelDeliveryV3/Zones/` 에서 작업할 프리팹을 더블클릭해 **프리팹 편집 모드**로 연다.

> ⚠️ 열면 **"Missing script"** 경고가 뜰 수 있다. 이 프리팹들이 아직 이 브랜치에 없는 스크립트
> (`OcclusionSection` 등)를 참조해서다. **이미 파악된 별건이고 스폰 작업과 무관하다.**
> 그 경고 때문에 컴포넌트를 지우지 말 것.

### 2단계 — 노드 오브젝트 만들고 `NodeMarker` 붙이기

한 **노드** = 「몬스터 한 종류가 나오는 한 무리」다.

1. 프리팹 루트에서 우클릭 → `Create Empty` → 이름을 **`Node_0`** 처럼 짓는다
2. 그 오브젝트를 몬스터 무리를 놓고 싶은 **대략의 중심**으로 옮긴다
3. `Add Component` → **`Node Marker`** 추가
4. 인스펙터에서 **`Content Type` 을 `CombatNode` 로 둔다** (기본값이 그것이다)
   - 🔴 **`CombatNode` 가 아니면 몬스터가 한 마리도 안 나온다.** 스포너가 전투 노드만 본다

### 3단계 — 스폰 위치 마커 놓기

노드 오브젝트의 **자식**으로 빈 오브젝트를 몬스터 마리 수만큼 만든다.

1. `Node_0` 우클릭 → `Create Empty` → 이름 **`Spawn_0`, `Spawn_1` …**
2. 몬스터를 세울 자리로 각각 옮긴다

**위치 규칙 — 이걸 어기면 엉뚱한 데 나온다:**

- 🔴 **반드시 바닥 위에 둔다.** 게임은 마커에서 **아래로 광선을 쏴** 바닥을 찾고 그 지점에 세운다
  - 찾는 범위 = 마커보다 **5m 위에서 아래로 30m**
  - 바닥으로 인정 = **`Default` 또는 `Ground` 레이어** 콜라이더
  - 🔴 **바닥을 못 찾으면 그 몬스터는 「존 한가운데」로 끌려간다.** 안 나오는 게 아니라 **엉뚱한 자리에
    나온다** — 잘못 놓은 마커가 여러 개면 **전부 한 지점에 뭉친다.** "몬스터가 존 중앙에 겹쳐 있다"가
    보이면 십중팔구 이 경우다 (콘솔에 경고가 함께 뜬다)
- 마커의 **회전(Rotation)** 이 몬스터가 처음 바라볼 방향이 된다. 벽 쪽을 보게 두지 말 것
- 높이(Y)는 바닥 근처면 된다. 정확한 높이는 게임이 맞춘다
- 마커의 **크기(Scale)는 아무 의미 없다**

### 4단계 — `NodeMarker` 에 번호와 위치 넣기

`Node_0` 을 선택하고 인스펙터의 `Node Marker` 에서:

1. **`Monster Group ID`** 에 몬스터 **번호** 입력 ([3번 표](#3-몬스터-그룹-번호표))
2. **`Monster Spawn Points`** 목록을 열고, `+` 로 칸을 늘린 뒤
   **`Spawn_0`, `Spawn_1` … 을 하이어라키에서 끌어다 놓는다**

> **한 노드 = 한 종류다.** `Monster Group ID` 는 노드마다 **하나**이고, 그 노드의 스폰 위치 전부에
> **같은 몬스터**가 나온다. 여러 종류를 섞고 싶으면 **노드를 여러 개** 만든다
> (`Node_0` = 번호 1, `Node_1` = 번호 5 …).

### 5단계 — 🔴 `ZoneLayout.Nodes` 에 등록 (이걸 빠뜨리면 아무 일도 안 일어난다)

프리팹 **루트**를 선택 → 인스펙터의 `Zone Layout` → **`Nodes`** 목록을 편다.

1. `+` 로 칸을 늘린다
2. 방금 만든 **`Node_0` 오브젝트를 끌어다 놓는다**
3. 노드를 여러 개 만들었으면 **전부** 등록한다

🔴 **자동 수집이 없다.** 스포너는 `Nodes` **목록에 등록된 것만** 읽는다. `NodeMarker` 를 붙이고
스폰 위치까지 다 채워도 **이 목록에 안 넣으면 그 노드는 존재하지 않는 것과 같다.**
자동으로 채워 주는 에디터 메뉴도 **없다** — 손으로 끌어다 놓아야 한다.

마지막으로 저장(`Ctrl+S`). `Node_0` 을 선택하면 씬 뷰에 **빨간 네모(노드)** 와 **분홍 동그라미(스폰 위치)** 가 보인다.

---

## 2. 인스펙터 항목 설명

### `NodeMarker` (노드 오브젝트에 붙인다)

| 항목 | 무엇 | 어떻게 |
|---|---|---|
| **`Content Type`** | 노드 종류 | 🔴 몬스터를 내려면 **`CombatNode`** |
| **`Monster Group ID`** | 어떤 몬스터인가 | 번호 하나 ([표](#3-몬스터-그룹-번호표)). **`-1` 이면 안 나온다** |
| **`Monster Spawn Points`** | 몬스터가 설 자리들 | 자식 빈 오브젝트를 끌어다 놓기 |
| `Tier` | 노드 티어 분류 | 스폰과 무관. 기본값 유지 |
| `Behavior` | 스폰 후 행동(대기/정찰) | ⚠️ **아직 코드에 반영 안 됨.** 지금은 바꿔도 동작이 같다 |
| `Clear` | 클리어 조건 | 스폰과 무관 |

### `ZoneLayout` (프리팹 루트)

| 항목 | 무엇 |
|---|---|
| **`Nodes`** | 🔴 **이 존이 쓸 노드 목록.** 여기 등록된 것만 스폰된다 (5단계) |
| `Size` / `Role` / `Difficulty` / `ThemeName` | 존 분류. **이미 세팅돼 있으니 건드리지 말 것** |
| `Monster Group ID` (존 단위) | 아래 폴백 경로에서만 쓰인다 |
| `Monster Spawn Entries` / `Monster Spawn Points` | 아래 [부록](#부록--지금-실제로-돌고-있는-것-폴백-경로) 참조 |

---

## 3. 몬스터 그룹 번호표

`MapGenConfig.asset` 의 현재 값이다. **여기 없는 번호를 적으면 그 노드는 통째로 안 나온다.**

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
> 나중에 Tesla 가 들어오면 **번호표만** 고치면 되고 존 프리팹은 손댈 필요 없다.

### 번호를 새로 추가하려면 (프로그래머와 함께)

1. `Assets/50.Art/MapGen/MapObj/MapZoonSettingObj/MapGenConfig.asset` 선택
2. `Monster Groups` 에 `+` → `Group ID`(안 겹치는 번호) · `Group Name` · `Monster Prefab` 지정
3. 🔴 그 몬스터 프리팹이 `DefaultNetworkPrefabs` 에도 등록돼 있어야 한다 (현재 위 7종은 전부 등록됨)
4. 🔴 **SVN으로 커밋**

---

## 4. 존별 현재 저작 상태

🔴 **아직 `NodeMarker` 를 쓰는 존이 하나도 없다.** 11개 전부 아래 부록의 **폴백 경로**로 돌고 있다.
앞으로의 저작은 이 문서의 1~5단계(노드 방식)로 한다.

| 존 프리팹 | 크기 | 역할 | 노드 | 지금 나오는 방식 |
|---|---|---|---|---|
| `PF_Zone_L_Type_A_V3` | Large | Combat | 0 | 폴백 (마커별 지정 6) |
| `PF_Zone_L_Type_B_V3` | Large | Combat | 0 | 폴백 (마커별 지정 8) |
| `PF_Zone_L_Type_C_V3` | Large | Combat | 0 | 폴백 (마커별 지정 9) |
| `PF_Zone_M_Type_A_V3` | Medium | Combat | 0 | 폴백 (마커별 지정 9) |
| `PF_Zone_M_Type_B_V3` | Medium | Combat | 0 | 폴백 (마커별 지정 12) |
| `PF_Zone_M_Type_C_V3` | Medium | Combat | 0 | 폴백 (구 마커 3, 전부 같은 몬스터) |
| `PF_Zone_Quest_01_V3` | Medium | Quest | 0 | 폴백 (구 마커 2) |
| `PF_Zone_Quest_02_V3` | Medium | Quest | 0 | 폴백 (구 마커 2) |
| `PF_Zone_S_Type_A_V3` | Small | Combat | 0 | 폴백 (마커별 지정 6) |
| `PF_Zone_S_Type_Boss_Enter_V3` | Small | BossRoom | 0 | 몬스터 없음(의도) |
| `PF_Zone_S_Type_Start_V3` | Small | PlayerSpawn | 0 | 몬스터 없음(의도) |

🔴 **전환할 때 주의**: 어떤 존에 **전투 노드를 하나라도 만들어 등록하면**, 그 존의 기존
`Monster Spawn Entries` 는 **그 순간부터 통째로 무시된다.** 절반만 옮기면 **나머지 절반이 사라진다.**
한 존을 건드리기 시작하면 **그 존의 몬스터를 전부 노드로 옮겨야 한다.**

---

## 5. 몬스터가 안 나올 때 — 확인 순서

| 증상 | 먼저 볼 것 |
|---|---|
| **그 존의 몬스터가 통째로 안 나온다** | ① `ZoneLayout.Nodes` 에 노드를 **등록**했나 (5단계) ② `Content Type` 이 `CombatNode` 인가 ③ `Monster Group ID` 가 `-1` 이 아닌가 |
| **한 마리도 안 나온다 (다른 존은 정상)** | 그 존이 이번 맵 생성에 **뽑히지 않았을 수 있다.** 맵은 매번 랜덤 조합이다 |
| 🔴 **몬스터가 존 한가운데 뭉쳐 있다** | 그 스폰 마커들이 **바닥 위가 아니다** |
| **일부만 안 나온다** | `Monster Spawn Points` 목록에 **빈 칸(None)** 이 있는지 |
| **엉뚱한 몬스터가 나온다** | 번호 확인. 노드 하나엔 **한 종류만** 나온다 |
| **호스트에만 보인다** | 몬스터 프리팹이 `DefaultNetworkPrefabs` 미등록 — 프로그래머에게 |

🔴 **노드 경로는 실패해도 조용하다.** 번호가 표에 없거나 스폰 위치 목록이 비면 **로그 한 줄 없이**
그 노드를 건너뛴다. 그래서 **위 3가지(등록·CombatNode·번호)를 눈으로 확인하는 것이 가장 빠르다.**

Play 중 Console 에서 이 줄을 찾으면 전체 결과를 알 수 있다.

```
[MapContentSpawner] 존 비주얼 N / 몬스터 M 스폰 (서버:True)
```

마커가 허공이면 이 경고가 함께 뜬다.

```
[MapContentSpawner] 스폰 마커가 허공(x, y, z) — <존이름> 바닥 중앙으로 대체. 마커 위치 조정 필요.
```

존 자체가 바닥과 어긋났으면 이 에러다. **마커 문제가 아니라 존 배치 문제**이니 프로그래머에게.

```
[MapContentSpawner] <존이름>에서 바닥을 찾지 못해 몬스터를 스폰하지 않습니다(x, y, z) —
존이 통로와 어긋난 위치에 배치됐을 가능성이 큽니다.
```

---

## 6. 하지 말아야 할 것

- ❌ 존 프리팹에 **몬스터 프리팹을 자식으로 직접 넣기** — 네트워크 복제가 안 된다
- ❌ `NodeMarker` 만 붙이고 **`ZoneLayout.Nodes` 등록을 빠뜨리기** — 가장 흔한 실수다
- ❌ `Content Type` 을 `CombatNode` 가 아닌 값으로 두기
- ❌ `Assets/2.Prefabs/Map/Zoneprefab/` 의 **옛 존 프리팹** 수정 — 정본이 아니다
- ❌ 스폰 마커를 **허공이나 바닥 구멍 위**에 배치
- ❌ 한 존에서 **노드 방식과 기존 방식을 반씩 섞기** — 기존 것이 통째로 죽는다
- ❌ `Size` / `Role` 변경 — 맵 생성 규칙이 바뀐다. 필요하면 프로그래머와 상의
- ❌ `MapGenConfig.asset` 을 **git으로 커밋 시도** — SVN 파일이다

---

## 부록 — 지금 실제로 돌고 있는 것 (폴백 경로)

노드가 **하나도 없을 때만** 동작하는 옛 경로다. 현재 존 11개가 전부 여기에 해당한다.
**새로 저작할 때는 쓰지 않는다.** 어떻게 돌아가는지만 알아 두면 된다.

`ZoneLayout` 루트의 두 칸을 쓴다.

- **`Monster Spawn Entries`** — 마커 하나하나에 번호를 따로 붙이는 목록. 항목이 하나라도 있으면 이게 쓰인다
- **`Monster Spawn Points`** — 더 옛날 칸. `Entries` 가 비었을 때만 쓰이고, **마커 전부가 존 단위
  `Monster Group ID` 하나**로 나온다

우선순위는 이렇다.

```
전투 노드(CombatNode)가 1개 이상 있나?
   ├─ 예  → Nodes 만 쓴다.  Entries / Points 는 완전히 무시
   └─ 아니오 → Entries 가 비었나?
                ├─ 아니오 → Entries (마커별 번호)
                └─ 예     → Points (전부 같은 몬스터)
```

---

## 관련 코드

| | |
|---|---|
| 스폰 실행 | `Assets/1.Scripts/Map/MapContentSpawner.cs` — `SpawnMonstersFor` · `SpawnGroupAt` · `TryResolveSpawnPoint` |
| 노드 저작 데이터 | `Assets/1.Scripts/Map/NodeMarker.cs` |
| 존 저작 데이터 | `Assets/1.Scripts/Map/ZoneLayout.cs` |
| 번호표 정의 | `Assets/1.Scripts/Map/MonsterGroupData.cs` · `MapGenConfigSO.cs` |
| 분류 열거형 | `Assets/1.Scripts/Map/MapEnums.cs` — `NodeContentType` · `ZoneSize` · `ZoneRole` |
