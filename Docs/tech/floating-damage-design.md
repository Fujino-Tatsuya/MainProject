# 플로팅 데미지(Floating Damage) — 무엇을 기반으로 만들 것인가

작성 2026-08-05 / 브랜치 `feature/FloatingDamage` (base `Convayor-V2` c256a5d21) / 워크트리 `MainProject-WorkTree`

목적: 구현 착수 전에 **이 레포에 이미 있는 구조 중 무엇에 얹는 게 맞는지**를 실측 근거로 확정한다.
아직 미결정인 항목은 §6에 모아 뒀다. 구현(Codex 위임)은 §6이 닫힌 뒤에 시작한다.

---

## 1. 결론 요약

| 항목 | 결론 | 근거 |
|---|---|---|
| 데이터 소스 | 기본은 **`Unit.ClientDamaged` 복제 기반 시임 확장**(RPC 0건), 공격자 식별이 필요한 필터에서만 서버 RPC 경로 | 이미 같은 목적(HitFlash)으로 검증된 경로. 서버가 감산을 끝낸 **실제 수치**가 전 피어에 복제됨 |
| 표시 방식 | **World-space TMP + 오브젝트 풀** | `OverheadHealthBar.prefab` / `UnitOverheadHealthBar`가 이미 월드스페이스 빌보드로 동작 중 → 같은 장치 재사용 |
| 부착 방식 | Unit 계열 공통 자동 부착 (HitFlash와 동일 패턴) | 플레이어·몹·보스 전부 `Unit` 파생 → 프리팹 개별 배선 불필요 |
| 신규 필요 | 텍스트 팝업 프리팹 + 풀 + 스포너, `Unit`에 델타 포함 이벤트 1개 | 레포에 데미지 텍스트 코드 0건, 범용 오브젝트 풀 0건 |

`Assets` 전체에 `DamageText` / `FloatingText` / `DamagePopup` 계열 코드는 **0건** — 백지에서 시작한다.

---

## 2. 실측한 기존 구조

### 2-1. 서버 피해 파이프라인 (권위 = 서버)

```
공격자 → Hurtbox → IAttackReceiver.ReceiveAttack(AttackInfo, AttackHitContext)
       → Unit.TakeDamage(AttackInfo)  →  Unit.ApplyHealthDamage(damage, ignoreDefenseAndShield)
```

- [Unit.cs:74](Assets/1.Scripts/Unit/Unit.cs:74) `ApplyHealthDamage` — `if (!IsServer) return;` 서버 전용. `CanApplyHealthDamage`로 무적/차단 판정.
- [Unit.cs:111](Assets/1.Scripts/Unit/Unit.cs:111) `ApplyMitigatedHealthDamage` — **경감 공식**
  - 방어력: `최종 = 피해 × 100 / (100 + 방어력)` (방어력 100 = 50% 경감)
  - 실드 우선 흡수 → 잔여만 HP로. 즉 **HP 감소량 ≠ AttackInfo.damage**
- [Unit.cs:100](Assets/1.Scripts/Unit/Unit.cs:100) 결과를 `_currentHp.Value`(NetworkVariable, Write=Server)에 반영.
- 특수 진입점: `ApplyDirectHealthDamage`(방어/실드 무시), `ApplyMaxHealthPercentDamage`, `ApplyCurrentHealthPercentDamage` — Vent 등 해저드가 사용.

**함의:** 클라이언트가 `AttackInfo.damage`로 숫자를 추정하면 방어력·실드 때문에 실제와 어긋난다. 숫자는 서버 결과에서 와야 한다.

### 2-2. 이미 있는 "로컬 연출" 시임 — 이게 핵심

[Unit.cs:449-483](Assets/1.Scripts/Unit/Unit.cs:449)

```csharp
public event System.Action ClientDamaged;          // 델타 없음(현재)

public override void OnNetworkSpawn() {
    _currentHp.OnValueChanged += OnHpReplicated;
    _currentShield.OnValueChanged += OnShieldReplicated;
    if (GetComponent<HitFlash>() == null)
        gameObject.AddComponent<HitFlash>();        // Unit 계열 전체 자동 부착
}
void OnHpReplicated(int previous, int next)      { if (next < previous) ClientDamaged?.Invoke(); }
void OnShieldReplicated(int previous, int next)  { if (next < previous) ClientDamaged?.Invoke(); }
```

- **RPC 0건**: NetworkVariable 복제만으로 전 피어가 감소를 안다. [HitFlash.cs:10](Assets/1.Scripts/Unit/HitFlash.cs:10) 주석이 이 정책을 명시("데미지 판정과 무관한 순수 로컬 연출 — RPC/추가 트래픽 없음").
- **자동 부착 패턴**: `HitFlash`는 프리팹 배선 없이 `OnNetworkSpawn`이 붙인다. 플로팅 데미지도 동일하게 하면 몹/보스/플레이어 전부 공짜로 커버된다.
- **현재 한계 3가지** (구현 시 반드시 다뤄야 함)
  1. `ClientDamaged`가 `Action` — **감소량을 안 넘긴다**. 델타는 `OnHpReplicated(previous, next)` 안에만 존재.
  2. NetworkVariable은 tick 단위 델타 전송 → **같은 tick 내 연타 2히트가 1개로 합쳐질 수 있다**(숫자 1개, 합계값).
  3. **공격자 정보가 없다** → "내가 준 딜만 표시" 필터는 이 경로만으로는 불가.

### 2-3. UI 자산 현황

| 자산 | 위치 | 성격 |
|---|---|---|
| `CombatHUD` | [CombatHUD.cs](Assets/1.Scripts/UI/Combat/CombatHUD.cs), `2.Prefabs/UI/CombatHUD.prefab`, `Paladin.prefab` 자식 | Screen-space. `Player.LocalPlayerChanged` 구독 → 위젯 Bind |
| `UnitOverheadHealthBar` | [UnitOverheadHealthBar.cs](Assets/1.Scripts/UI/Combat/UnitOverheadHealthBar.cs), `2.Prefabs/UI/OverheadHealthBar.prefab`, `Paladin.prefab` 자식 | **World-space 빌보드** — `LateUpdate`에서 `transform.rotation = Camera.main.transform.rotation` |
| TMP | `PlayerHealthHUD` / `StatusEffectHUD` / `PassiveHUD` / `ResultStatsView` 등에서 이미 사용 | 한글 폰트 에셋 확보됨 |
| 범용 오브젝트 풀 | **없음** (`BroAudio` 내부 풀만 존재) | `UnityEngine.Pool.ObjectPool<T>` 또는 자체 풀 신설 |

주의: 현행 플레이어 프리팹은 `Paladin.prefab`이다(`2.Prefabs/Player/Player.prefab`에는 오버헤드바 미부착).

---

## 3. 데이터 소스 — 3안 비교 (옵션화 요청 반영)

세 안 모두 하나의 SO 설정(`FloatingDamageSettings`)의 enum으로 전환 가능하게 만들 수 있다. 단 **비용이 같지 않다.**

| | A. 복제 기반 (권장 기본값) | B. 서버 RPC 브로드캐스트 | C. 로컬 추정 |
|---|---|---|---|
| 수치 정확도 | ◎ 서버 감산 결과 그대로 | ◎ 서버가 명시 전송 | ✗ 방어력·실드 경감 반영 못 함 |
| 네트워크 비용 | 없음(기존 복제 재사용) | 히트당 1 RPC | 없음 |
| 연타 분리 | △ 같은 tick 합산 가능 | ◎ 히트별 분리 | ◎ |
| 공격자 식별("내 딜만") | ✗ 불가 | ◎ 가능 | △ 자기 공격만 아는 수준 |
| 크리티컬/타입 구분 | ✗ 채널(HP/실드)만 | ◎ 임의 메타 실을 수 있음 | ✗ |
| 추가 작업량 | 소 (이벤트 시그니처 확장) | 중 (RPC + AttackInfo에 공격자 전달 확인 필요) | 소 |

**확정(2026-08-05):** 표시 필터를 SO enum으로 전환 가능하게 하기로 했으므로 **A와 B를 모두 1차에 구현**한다.
- 기본값 = **A(복제 기반)**. 필터가 `AllDamage`인 동안에는 RPC가 전혀 흐르지 않는다.
- 필터가 공격자 식별을 요구하는 값(`OwnDealtOnly`, `AllWithOwnEmphasis`)으로 바뀌면 **B(서버 RPC)** 경로가 켜진다. 즉 데이터 소스는 필터 설정에서 파생되며, 사용자가 별도로 고르지 않는다.
- C(로컬 추정)는 채택하지 않는다 — A가 이미 무비용이고 더 정확하다. 옵션 값으로도 만들지 않는다(죽은 분기 금지).

---

## 4. 표현 방식 — 3안 비교

| | ㄱ. World-space TMP + 풀 (권장) | ㄴ. Screen-space Canvas + 월드→스크린 투영 | ㄷ. VFX Graph / 셰이더 |
|---|---|---|---|
| 기존 구조 재사용 | ◎ `OverheadHealthBar`와 동일 장치 | △ `CombatHUD`(Screen-space) 얹기 | ✗ 신규 |
| 거리에 따른 크기 | 원근 축소(원하면 스케일 보정) | 일정 | 원근 |
| 프레임 비용 | 팝업당 캔버스 갱신, 풀로 억제 | 매 프레임 투영 + 오클루전 직접 처리 | 최저(대량 유리) |
| 글자 렌더 난이도 | 낮음(TMP 그대로) | 낮음 | 높음(글리프 아틀라스 직접) |
| 구현량 | 소~중 | 중 | 대 |

**권장:** ㄱ. 근거 — ①레포에 이미 월드스페이스 빌보드 선례가 있어 카메라 정렬 코드를 그대로 따를 수 있고, ②쿼터뷰 고정 카메라라 투영 보정의 이점이 작으며, ③숫자 수백 개를 동시에 띄우는 게임이 아니라 ㄷ의 성능 이점이 회수되지 않는다.

구성안:
```
FloatingDamageSpawner (씬 상주 1개, 풀 소유)
  └─ FloatingDamagePopup (프리팹: World-space Canvas + TMP_Text) — §4-1 상태머신으로 동작
FloatingDamagePresenter (Unit에 자동 부착 — HitFlash 패턴)
        - Unit의 델타 포함 이벤트 구독 → 이 유닛의 Active 팝업에 누적 요청
        - 표시 필터 판정(자기 피격 제외 규칙 포함)
FloatingDamageSettings (SO: 색/폰트크기/체류·애니·페이드 시간/산포 각도·속도·중력/동시표시 상한/표시필터 enum)
```

### 4-1. 팝업 상태머신 (확정)

숫자는 "뜨자마자 날아가는" 방식이 아니라, **대상별로 하나가 체력바 옆에 붙어 피해를 모으다가 떨어져 나가는** 방식이다.

| 상태 | 동작 | 이탈 조건 |
|---|---|---|
| **Active** | 대상 오버헤드 체력바 **옆에 위치 고정**. 새 피해가 들어오면 **값을 합산**하고 스케일 펀치. 대상·채널당 1개만 존재 | 마지막 피격 후 `stayTimeout`(SO, 기본 0.3초) 동안 새 피해가 없으면 → Animating |
| **Animating** | SO로 지정한 **산포 각도 범위 안에서 랜덤 방향**으로 이동(속도·중력 SO). 진행에 따라 **색이 점점 어두워짐** | `animateDuration` 경과 → FadingOut |
| **FadingOut** | 이동 감쇠를 유지하며 **알파가 0으로** | `fadeDuration` 경과 → 풀 반납 |

- Active 상태의 팝업만 값을 받는다. Animating/FadingOut 중에 새 피해가 오면 **새 Active 팝업이 생성**되고, 기존 팝업은 자기 수명대로 끝난다.
- Active 앵커는 대상의 오버헤드 체력바 옆(월드 오프셋). **로컬 플레이어 자신이 받은 피해는 표시하지 않는다**(아래) → 오버헤드 체력바가 오너에게 숨겨지는 문제는 발생하지 않는다.
- 빌보드 회전은 세 상태 모두 `UnitOverheadHealthBar`와 동일하게 `LateUpdate`에서 `Camera.main` 회전을 추종한다.

### 4-2. 표시 필터 (확정)

- **고정 규칙: 로컬 플레이어가 받은 피해는 숫자로 띄우지 않는다.** 자기 피격은 HitFlash와 화면 HUD가 이미 담당한다.
- 그 외 대상에 대해 SO enum으로 전환:
  - `AllDamage` (기본) — 모든 유닛의 모든 피해. 복제 기반, RPC 없음
  - `OwnDealtOnly` — 내가 준 딜만. RPC 경로 사용
  - `AllWithOwnEmphasis` — 전부 표시 + 내 딜만 색/크기 강조. RPC 경로 사용

### 4-3. 추상화 수준 (확정)

**팝업 유형 타입 + 채널 계약만** 추상화한다. 어셈블러 전면 교체형 인터페이스 조합은 하지 않는다(간접성 과다).

- `PopupKind { Damage, Heal, ShieldDamage, Status, Text }` — 1차는 **Damage만 실구현**, 나머지는 enum 값과 계약만 존재
- 채널 계약: `struct FloatingPopupRequest { Unit target; PopupKind kind; int amount; bool fromLocalPlayer; }` 수준의 단일 진입점 → 힐·상태이상은 나중에 이 진입점만 호출하면 붙는다
- 색·서식은 `PopupKind`별 SO 테이블에서 조회 → 새 유형 추가 시 코드 분기 증식 없음

---

## 5. 코드 접점 (변경 예정 지점)

| 파일 | 변경 | 비고 |
|---|---|---|
| [Unit.cs:449~483](Assets/1.Scripts/Unit/Unit.cs:449) | 델타 포함 이벤트 추가 — 예: `event Action<int /*amount*/, DamageChannel /*Hp\|Shield*/> ClientDamagedAmount` | 기존 `ClientDamaged`는 유지(HitFlash 무수정 원칙) |
| `Unit.OnNetworkSpawn` | `FloatingDamagePresenter` 자동 부착 | HitFlash와 동일 위치·동일 조건 |
| 신규 `FloatingDamagePresenter.cs` | 이벤트 → 스포너 요청 | 표시 필터 판정 위치 |
| 신규 `FloatingDamageSpawner.cs` | 풀 + 동시표시 상한 | 씬 상주. MapScene 배치 필요 |
| 신규 `FloatingDamagePopup.cs` + 프리팹 | 상승/페이드/빌보드 | `2.Prefabs/UI/` |
| 신규 `FloatingDamageSettings.cs` + `.asset` | 수치·정책 SO | 프로젝트 관례상 수치는 SO로 (보스/스킬 전례) |

원칙: **판정 코드는 건드리지 않는다.** `ApplyHealthDamage` / `ApplyMitigatedHealthDamage` 로직 무수정, 읽기 전용 소비만 추가.

검증 환경: `MapScene`이 런타임 씬. 멀티 확인은 MPPM 2인(호스트+클라)에서 서로의 피격 숫자가 각자 화면에 뜨는지 본다.

---

## 6. 결정 사항 (2026-08-05 확정)

| # | 항목 | 결정 |
|---|---|---|
| 1 | 표시 범위 (1차) | **데미지 숫자만** 실구현. 힐·실드·상태이상은 `PopupKind` 계약만 뚫어 두고 미구현 |
| 2 | 추상화 수준 | 팝업 유형 타입 + 채널 계약만 (§4-3) |
| 3 | 표시 필터 | SO enum 3종 전환 가능 + **자기 피격 미표시** 고정 규칙 (§4-2) |
| 4 | 데이터 소스 | A(복제) 기본, 필터가 공격자 식별을 요구하면 B(RPC) 경로 (§3) |
| 5 | 연타 처리 | 상태머신으로 해결 — Active 동안 **합산**, 무피격 `stayTimeout` 후 이탈 (§4-1) |
| 6 | Animating 방향 | SO 산포 각도 범위 내 랜덤 + 속도·중력 SO 조정 |
| 7 | 크리티컬 | **도입하지 않음** — 현재 전투 코드에 크리티컬 판정이 없고, 넣으면 데미지 계산식까지 번진다 |

여전히 열려 있는 값(구현 중 기본값으로 넣고 에디터에서 튜닝):
- `stayTimeout` 기본 0.3초 / `animateDuration` 기본 0.5초 / `fadeDuration` 기본 0.3초
- 동시표시 상한 기본 32 — 초과 시 가장 오래된 팝업부터 즉시 반납
- 산포 각도 기본 = 수직 기준 ±35°

---

## 부록: 이번 세션 인프라 변경 (코드 외)

- `feature/ConveyorBelt` 로컬 삭제(원격 `MainProject/feature/ConveyorBelt`는 백업으로 유지). 해당 작업은 `Convayor-V2`에 전부 포함됨 확인.
- Codex 위임 레인 `soul`(경로 `MainProject-BeaverLobby`, 폴더 삭제됨)을 `fd`(`MainProject-WorkTree`)로 교체 — `agent-context-bridge/codex-tray.ps1`, `codex-monitor-fd.bat` 신설. 워처 `-ProjectRoot`를 자기 워크트리로 한정해 다른 브랜치 폴더 오염 방지.
