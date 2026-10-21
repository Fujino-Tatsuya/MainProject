# handoff — 플레이어 상태이상 루프 VFX (기절 → FX_Faint)

> 2026-10-20 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 「플레이어 상태이상 루프 VFX」
> **은희 님 영역(`StatusEffectController`)에 추가만 한 변경이 있다** — 기존 함수·동작은 그대로다. 아래 §1 만 확인하면 된다.

## 1. 코어 변경 — `Unit/StatusEffectController.cs` (추가만)
- `public event Action EffectsChanged` — **전 피어**에서 상태이상 목록이 바뀔 때마다(추가·갱신·해제·만료) 난다.
- `OnNetworkSpawn` / `OnNetworkDespawn` 오버라이드 **신설** — 내부 `effects.OnListChanged` 를 구독·해제하고 위 이벤트로 넘긴다. 이 클래스에 원래 두 오버라이드가 없었다.
- `Apply`·`Remove`·만료 스윕·차단 표·`IStatusEffectFacade` **무변경**. 이벤트 구독자가 없으면 아무 일도 안 한다.
- ⚠️ 처음 동기화(늦은 접속)에는 `OnListChanged` 가 나지 않는다 → 구독하는 쪽이 켜질 때 한 번 직접 읽어야 한다(아래 Presenter 가 그렇게 한다).
- 이 클래스에 나중에 `OnNetworkSpawn` 을 직접 넣게 되면 위 두 줄(구독·해제)만 같이 유지하면 된다.

## 2. VFX 쪽(민경 영역, 참고)
- **SO** `1.Scripts/Effects/StatusEffectVfxSet.cs` — 상태 → 루프 이펙트 표. 행 = 상태 종류(플래그 1개) · 엔트리 · 자리(Overhead/Body/Feet) · 오프셋 · 배율 · 우선순위.
  에셋 `9.ScriptableObject/Effects/PlayerStatusEffectVfxSet.asset`(플레이어 공용 1개). 첫 줄 = **기절 → `FX_Faint_Entry`**, 머리 위, 오프셋 (0, 0.5, 0), 우선순위 50.
  **새 상태 이펙트는 이 SO 에 한 줄 추가로 끝난다.** 우선순위 기본 순서 = 머리 위 상태 글자(`OverheadStatusLabelRules`): 공중 60 > 기절 50 > 속박 40 > 침묵 30 > 약화 20 > 둔화 10.
- **Presenter** `1.Scripts/Effects/StatusEffectVfxPresenter.cs` — 각 Variant(`Player_Assassin`·`Player_Gunner`·`Player_Paladin`)의 `VFX/States` 에 부착.
  - 자리: Overhead = `TopHead`(캐릭터별 높이 — 어쌔신·팔라딘은 Armature 인스턴스 직하, 거너는 그 아래 `VFX/`), Body 비움(그 줄 건너뜀), Feet 비움 = 플레이어 루트.
  - `EffectsChanged` + `PlayerLifeCycleController.LifeStateChanged` 구독, 켜질 때 직접 1회 계산. **살아 있을 때만**, **자리마다 우선순위 최고 1개**.
  - `EffectManager.PlayLooping` 핸들을 직접 든다 — 바뀐 자리만 `Release`(엔트리 outro 0.3초), `OnDisable` 반납, `OnDestroy` 즉시 반납. **추가 네트워크 없음**(목록이 이미 복제됨).
- `VFX/States`(Presenter)와 `TopHead`(Overhead 자리)는 **이번에 신설**이다 — 세 Variant 에 상태이상 연출 오브젝트가 원래 없었다. 거너는 흩어져 있던 추가 오브젝트를 `VFX` 컨테이너 둘로 묶는 정리가 함께 들어갔다(`m_AddedGameObjects` 가 여러 줄에서 한 줄로 접힘).
- 에셋 `50.Art/VFX/Common/States/FX_Faint.prefab`·`FX_Faint_Entry.asset`(루프, outro 0.3) + `_Materials/Mat_fx_HCFX_Star_01.mat`·`Textures/Tex_fx_HCFX_Star_01.tga`(🔴 SVN add). 카탈로그 `Player_Faint`(prewarm).

## 3. 지금 플레이어에게 기절이 걸리는 곳 (참고)
| 위치 | 언제 | 지속 | 현재 |
|---|---|---|---|
| `Monster/Boss/TwentyThreeBoss.cs` `ReleaseDashCarry` | 23호 대쉬가 밀고 가다 맵 가장자리(벽)에 도착 | `BossDataSO.dashStunDuration` = 1초 | ✅ 실제로 남 |
| `Player/PlayerLandingProtection.cs` `BeginProtection(applyStun: true)` | Airborne 착지 | 1초 | ⏸ 플레이어를 띄우는 공격이 없음 |
| `Monster/AreaZone.cs` | 장판 틱 | `statusDuration` | ⏸ `FireFloor` 의 상태가 None |
- `BossDataSO.cs` 의 "거리 소진 = 기절만" 주석과 달리 코드는 벽에 안 닿으면 기절이 없다(참고).

## 4. 확인 부탁
- Play/MPPM: 23호 대쉬 벽꽝 → 머리 위 별 1초 루프 후 0.3초에 사라짐(호스트·클라·다른 플레이어 화면), 기절 중 쓰러지면 즉시 꺼짐, 늦게 들어온 클라도 맞음.
