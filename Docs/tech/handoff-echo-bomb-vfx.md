# 핸드오프 — 잔류 포격(GUN_4_ECHO_Q) 에너지 폭탄 VFX

> 2026-11-03 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).
> 판정·피해·폭발 시각 무변경. 복제 상태에 **연출 전용 칸 2개**(단계·빈 폭발)를 더했다 — 은희 님 영역 확인 부탁.

## 기획 vs 구현
- 기획 문구는 "Q 가 끝난 뒤 같은 방향으로 레이저 한 번 더", 구현(할 일 149)은 **맞은 대상마다 몸에 폭탄 → Q 정상 종료 0.15초 뒤 그 대상 하나에만 피해**. VFX 는 구현(폭탄)을 따른다. 툴팁 문구 어긋남은 기획에 알릴 것.

## 동작
- **구체** `FX_EchoBomb_Orb`(`GunnerBeamView.echoBombVfxPrefab`): Q 적중 순간 예비 구체 → 서버 부착 때 이어받음. 대상 **몸 콜라이더 가운데**를 따라간다(크기 고정).
  - Glow(숨쉬는 번짐) · Core(흰빛 심) · Ring_A/B(기울어진 점선 궤도 링) · ChargeIn(빨려드는 입자).
  - 자식 **`Arm`**(꺼둠) — 서버 부착 순간 켠다(`GunnerEchoBombVisuals.ArmChildName`): 심이 부풀며 깜빡 · 고리가 조여듦 · 번짐 밝아짐(폭발 0.15초 전 고조).
- **폭발** `FX_EchoBomb_Burst`(칸 `echoBombBurst`) — 섬광·별빛·충격 링·파편·잔광. **빈 폭발**(대상이 먼저 사라짐) `FX_EchoBomb_Fizzle`(칸 `echoBombFizzle`) — 0.6배·흐리게. 칸이 비면 예전 Q 몬스터 적중으로.
- **한계 돌파 색**: 그 Q 시전의 단계 색 — 예비 구체 = `ChargeLaserFiredRpc` 의 단계, 부착 = 상태 `Stage`, 폭발 = 구체 색 그대로.
- **깊이 무시**: 재질 전부 `Shader_IntegratedEffect` `_ZTest2 = Always` — 몸 가운데여도 안 묻힌다(벽 뒤에서도 보임). 색은 파티클 색, 재질 `_TintColor` 는 무채색 HDR 세기만(색 지정 재생의 블록 문제 회피 — [handoff-overdrive-vfx.md](handoff-overdrive-vfx.md)).

## 은희 님께 — 바뀐 서버·복제 코드(연출 인자만)
| 파일 | 내용 |
|---|---|
| `Player/Gunner/GunnerEchoBombVisuals.cs` | `GunnerEchoBombState` 에 **`Stage`(byte)·`Fizzled`(bool)** 직렬화 칸 · `BodyPoint` = 콜라이더 **가운데**(윗면 → 가운데, 표시 전용) · 구체별 색·`Arm` 켜기(`ApplyLook`) · `AddPreview`/`Add`/`Sync` 색 오버로드 · `TryGetTint` |
| `Player/Gunner/GunnerBeamView.cs` | `ServerAttachEchoBomb(id, target, overdriveStage)` · `ServerDetonateEchoBomb(id, fizzled)` — 빈 폭발이면 **항목에 `Fizzled` 를 실어 둔 뒤 RemoveAt**(RemoveAt 사건 Value 로 전 피어 구분, 새 RPC 없음) · `Value` 사건은 무시 · 칸 `echoBombBurst`·`echoBombFizzle` · 폭발 = 전용 엔트리(구체 색) |
| `Player/Gunner/GunnerChargeLaserSkill.cs` | 부착에 이 시전 단계(`shotHeatMode == Overdrive ? shotStage : 0`) · 빈 폭발에 `fizzled: true` |
| `Effects/EffectCatalog.cs`·`.asset` | `Gunner_EchoBomb_Burst`·`_Fizzle` |
| `Player_Gunner.prefab` | 구체 프리팹·폭발 2칸 연결 |

## 원본 제작(MyProject)
- `Tools/VFX/Echo Bomb (거너 잔류 포격) 빌드`(`Assets/1.Scripts/Editor/EchoBombVfxBuilder.cs`) → `Export/Player2/EchoBomb/` · 미리보기 `Scenes/EchoBombPreview.unity`(`EchoBombTester` — 일반·이동·보스 대상, F 빈 폭발, V 집속 연사).
- MainProject SVN: `50.Art/VFX/Common/Players/Player2/Q_ChargeLaser/EchoBomb/`(프리팹 3 · 엔트리 2 · 재질 7 · 텍스처 5 · 메시 1).

## 확인
- DebugTraitLab(Play, 호스트): 부착(단계 2) → 허수아비 몸 가운데 구체 · Arm 켜짐 · 단계 색 → 폭발 = Burst 그 자리 · 빈 폭발 = Fizzle — 로그.
- [ ] 민경 Play: 실제 Q 적중(예비 구체 → 부착 → 폭발) · 집속 연사 · MPPM 클라.

## 알려진 점
- `Player_Gunner.prefab` 의 중첩 NetworkObject `GlobalObjectIdHash` 한 줄(816596077 → 1153219596)을 Unity(NGO)가 다시 계산해 저장했다 — 우리가 고친 값이 아니다.
- 저장 순서 주의: 새 칸이 있는 스크립트가 컴파일되기 전에 프리팹이 다시 저장되면 새 칸이 비워진다(이번에 한 번 겪어 다시 연결).
