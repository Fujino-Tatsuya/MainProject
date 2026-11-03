# 핸드오프 — 한계 돌파(GUN_2_OVERDRIVE) 과열 단계 색 + 공용 "색 지정 재생"

> 2026-11-03 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).
> 판정·피해·과열 규칙은 무변경. **은희 영역(거너 스킬·RPC)에 연출용 단계 인자만 추가.** 새 아트 에셋 없음.

## 동작
- 한계 돌파를 가진 거너의 **기본 공격·Q·R 공격 이펙트**가 피해에 쓴 과열 단계 색으로 바뀐다 — 1 주황 · 2 주홍 · 3 진홍. 0단계·특성 없음 = 원래 파랑.
- 범위: 기본 공격(빔·끝 착탄·몬스터 적중) · Q(충전·충전 완료·과충전·배기·빔·끝·적중) · R(시전 섬광·영역·낙뢰). **제외**: R 조준 표식(이미 주황) · E · 간파 · 보호막 · 잔류 포격 폭발.
- 색 = 색상(hue)만 교체 — 원래 밝기·알파·HDR 세기 그대로(흰 심은 흰 채로). 색 표 = `9.ScriptableObject/Effects/GunnerOverdriveVfxColors.asset`.
- 🔴 **버그 수정(11-03)**: `EffectRecolor` 가 수명별 색 그라데이션을 새로 만들 때 `colorSpace` 를 안 복사해 `Uninitialized` 가 됐다 → 감마로 저작한 키를 선형 값처럼 읽어 중간 밝기 키가 2~3배 밝아졌다(원래와 같은 색상으로 칠해도 심이 더 밝고 글로우가 짙어 보이던 원인). 이제 원래 `colorSpace` 를 그대로 쓴다. 번짐 보정(`glowIntensity`·`EffectTintGlow`)은 이 버그가 섞인 상태에서 넣었던 것이라 지웠다(11-03 민경).
- 🔴 **재질 색 블록(11-03)**: 흰·회색 재질 색(HDR 흰 `_Color` 16 등 — 색상을 바꿔도 그대로)엔 MaterialPropertyBlock 을 **걸지 않는다**. 같은 값이라도 블록을 걸면 이 파티클 셰이더들이 원래와 다르게 그려졌다(Play 측정 — 같은 값 블록만 건 빔이 화면에서 사라짐). 채도 있는 재질 색만 블록으로 칠한다.

## 은희 님께 — 바뀐 RPC·서버 코드(연출 인자만)
| 파일 | 변경 |
|---|---|
| `GunnerBasicAttack.cs` | `ShotRpc(origin, end, hit, byte overdriveStage)` — 서버 `ServerFire` 가 이 발 피해 단계(증가 전 `stage`)를 한계 돌파일 때만 싣는다(아니면 0). `PlayShot`·`ShowBeam` 도 인자 하나 |
| `GunnerBeamView.cs` | `ServerChargeLaserFired(..., int overdriveStage = 0)` · `ChargeLaserFiredRpc(..., byte overdriveStage)` |
| `GunnerChargeLaserSkill.cs` | 발사 때 `shotHeatMode == Overdrive ? shotStage : 0` 을 넘긴다(시전 시작 저장 단계 — 피해와 같은 값) |
| `GunnerTrackingLaserSkill.cs` | `OnServerStart` 에 `pendingOverdriveStage` 저장 → 레이저 **스폰 전** `GunnerTrackingLaserView.ServerSetOverdriveStage` |
| `GunnerTrackingLaserView.cs` | `NetworkVariable<byte> netOverdriveStage`(스폰 전에 써서 스폰 메시지에 실림 — 첫 낙뢰 RPC 보다 먼저 닿는다) · 영역·낙뢰 색 |
- Q 충전·R 시전 섬광(시전 순간 연출, `OnClientPlay`)은 RPC 없이 각 피어가 복제된 과열(`GunnerHeat` NetworkVariable)로 같은 순간 단계를 계산한다(`GunnerBeamView.LocalOverdriveStage`).

## 공용 "색 지정 재생"(이펙트 시스템 — 다른 연출도 쓸 수 있다)
- `EffectTint`(`Effects/EffectTint.cs`) — 색상 교체 규칙(곱하기 아님: 파랑 × 빨강 ≈ 검정이라서).
- `EffectManager.SetTint(handle, tint)` — 재생 중 인스턴스를 칠하고, 뒤에 터지는 파트(delay·outro)도 같은 색. 원샷은 `PlayRecallable` 로 핸들을 받아 칠한다.
- `EffectSocketPlayer.SetTint(tint)` — `SetScale`·`SetPlayRate` 와 같은 패턴(재생 전에 밀어 넣음, 값은 남으므로 공유 소켓은 매번 정한다). `PlayOnce` 도 색이 있으면 핸들로 칠한다.
- `EffectRecolor`(`Effects/EffectRecolor.cs`) — 인스턴스의 원래 색(파티클 startColor·colorOverLifetime·colorBySpeed·trails, 재질 `_TintColor`·`_Color`·`_BaseColor`)을 처음 한 번 기억 → 칠함(재질은 MaterialPropertyBlock, 공유 재질 안 건드림) → **반납 직전 `EffectManager.Recycle` 이 자동 복구**(풀 오염 없음).
- 한계: 이펙트 자체 스크립트가 런타임에 색을 바꾸는 것·같은 렌더러에 MaterialPropertyBlock 을 쓰는 것(TelegraphRing 등)엔 쓰지 말 것.

## 확인(DebugTraitLab Play — 로그)
- 거너 + 한계 돌파: 기본 공격(2단계) 빔 색상 0.61(파랑) → 0.04(주홍) · Q 발사(3단계) 빔·끝 0.99(진홍) · R 영역(2단계) 0.04 · R 낙뢰 재질(3단계) 0.57 → 0.99, HDR 세기 유지 · Q 충전 재질(1단계) 0.60 → 0.07(주황).
- 반납 후 전부 원래 색(파티클 원래 색상, 재질 블록 비움).
- [ ] 민경 Play: 화면 색감 · MPPM 클라 화면 · 단계 1/2/3 체감.

## git
- 코드 위 표 + `Effects/EffectTint.cs`·`EffectRecolor.cs`·`Player/Gunner/GunnerOverdriveVfxColors.cs`(+meta) · SO 에셋(+meta) · `Player_Gunner.prefab`·`GunnerTrackingLaser.prefab`(색 표 연결 1줄씩) · 문서.
