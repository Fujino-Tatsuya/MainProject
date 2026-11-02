# 핸드오프 — 보호막 VFX 공용화(금색·흰색) · 수호의 잔광/불굴의 가호 금색

> 2026-11-02 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).

## 바뀐 것 한눈에
| 전 | 후 |
|---|---|
| 팔라딘 `VFX/HolyShield`·`HolyShield_Break`·`AllyShield`·`AllyShield_Break`, 거너 `VFX/AllyShield`·`AllyShield_Break` 소켓(EffectSocketPlayer) | **삭제.** 공용 SO `9.ScriptableObject/Effects/PlayerShieldVfxSet.asset` 가 엔트리 4개·자리(몸)·배율 0.5 를 든다 |
| 금색 = `ShieldType.HolyShield`(옛 기본 E — 지금은 아무도 안 검) | 금색 = `ShieldType.HolyShield` + **특성 보호막 중 SO 목록**(`PAL_3_E_SHIELD` 수호의 잔광 · `PAL_4_HEAL_SHIELD` 불굴의 가호). 나머지(거너 Q 등) 흰색 |
| 수호의 잔광·불굴의 가호 보호막이 흰색으로 떴음 | 금색(본인·아군 모두) |
| 어쌔신 = `PlayerShieldVfx` 없음(받은 보호막 안 보임) | 어쌔신 루트에 `PlayerShieldVfx` 추가 |
| `StatusEffectVfxPresenter.body` 3캐릭터 비어 있음 | 팔라딘 = `Armature/VFXSocket/Middle` · 거너·어쌔신 = 새 `VFX/Body`(y 1) |

에셋(SVN, 민경 이동 완료): `50.Art/VFX/Common/Players/Shared/Shield/` — `FX_HolyShield(_Break)(_Entry)` · `FX_AllyShield(_Break)(_Entry)`·`_Barrier`·`_Motes`.

## `PlayerShieldVfx` (은희·민경 공동 — 보호막 부분만 고침)
- 받는 쪽이 주인 — 복제된 보호막 목록(`Unit.ShieldInstance` type·sourceId)을 매 프레임 보고 배리어 하나(금색 우선)를 켜고 끈다. 시작·종료 RPC 없음.
- 루프 핸들을 직접 든다(`EffectManager.PlayLooping` → 걷힐 때 `Release`, 파괴 시 `ReleaseImmediate`). 옛 소켓 safetyTimeout 15초 제약 없음.
- 깨짐 = 서버 `ServerShieldEnded`(Depleted, 마지막 하나) → `BreakBurstRpc(holy)` → 몸 자리에 원샷. 색 판정 = `PlayerShieldVfxSet.IsHoly`.
- 피격 파문 = 지금 배리어 핸들의 인스턴스에 `ShieldRippleEffect.AddHit`(기존과 같음).
- 단죄의 방패 충격파(`interruptWave`)·반격 자세 RPC(`ServerPlayGuardCounterPose`)는 **안 건드림**.
- 삭제: `PlayLocal()`·`ServerEnd(bool)`(오프라인 전용, 호출하는 곳 없었음).
- **새 금색 보호막 특성 추가** = SO `holyTraitIds` 에 특성 ID 한 줄.

## DebugTraitLab
- 아군 허수아비 `AllyDummy` 에도 `PlayerShieldVfx` + `VFX/States/StatusEffectVfxPresenter`(몸 = `VFX/Body` y 1) — 불굴의 가호가 아군에게 걸리면 금색이 보인다.
- 확인한 것(Play): 수호의 잔광 채널 → 팔라딘 금색, 불굴의 가호 채널 → 아군 허수아비 금색, 거너 Q 채널 → 흰색 판정 · 두 배리어 재생 핸들 정상.

## Play 체크리스트 (민경)
- [ ] 수호의 잔광: E 로 막은 뒤 피해 무시가 끝나면 팔라딘 금색 4초 · 허수아비 공격으로 깨면 금색 깨짐
- [ ] 불굴의 가호: 패시브 회복 → 팔라딘 금색 · 나누는 불굴 받은 아군(허수아비)도 금색
- [ ] 거너 Q 맞은 아군 = 흰색 · 흰색 위에 금색이 걸리면 금색으로 바뀜
- [ ] 크기·높이 — 팔라딘 Middle / 거너·어쌔신 `VFX/Body` y 1 (어쌔신 높이는 보고 조정)
- [ ] MPPM: 클라 화면에서도 같은 색

## SVN / git
- SVN: `Shared/Shield/`(이동분 — 옛 `Players/Shield/` 삭제 + 금색 4종 `Player1/ESkill/` → 이동) 커밋.
- git: `Effects/PlayerShieldVfxSet.cs`(+meta) · `Player/PlayerShieldVfx.cs` · `9.ScriptableObject/Effects/PlayerShieldVfxSet.asset`(+meta) · `Player_Paladin/Gunner/Assassin.prefab` · `0.Scenes/Debug/DebugTraitLab.unity` · 문서.
