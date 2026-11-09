# 핸드오프 — 거너 R 다른 클라 화면 알파 + EffectManager 머티리얼 알파 재생

> 2026-11-06 · 민경(Claude) · 브랜치 `feature/VFX` · 계획 [PLAN.md](../../PLAN.md) 맨 위(이 작업이 최신일 때).
> **은희 님 영역(`GunnerTrackingLaserSkill`)에 스폰 전 한 줄** — 아래 "은희 님께".

## 동작
- 거너 R(추적 레이저)의 **낙뢰 빔**(`M_GUlt_StrikeBeam` · `FX_GunnerUlt_Strike`)·**장판 바닥 선**(`GunnerUlt_FloorLine` · `FX_GunnerUlt_Zone2`)이
  **R 을 쏜 거너 본인 화면**에선 원래대로, **나머지 화면(호스트 포함)**에선 알파(장판은 밝기도)를 낮춰 보인다. 조준 표식은 그대로.
- 오프라인(테스트 씬)·시전자 미지정 = 원래대로. 시전자는 스폰 순간 정해지고 끊김·사망에도 안 바뀐다.
- 한계 돌파 단계 색은 그대로 칠한다(색 = 파티클 색·블록, 알파 = 머티리얼 — 서로 안 덮음).

## 조정
`2.Prefabs/Player/Gunner/GunnerTrackingLaser.prefab` → `GunnerTrackingLaserView` · **다른 클라 화면 알파**(`otherClientAlpha`) 목록 = {머티리얼, 속성, 성분, 배율}.
속성 칸에는 인스펙터 표시 이름이 아니라 **셰이더 속성 이름**을 쓴다(둘 다 인스펙터에선 "Color"로 보이지만 이름이 다르다).
성분(`channel`)은 Color 속성에만 쓰인다: `Alpha` = a 만 · `Rgb` = 밝기(r·g·b)만. Float 속성은 값 자체에 곱한다.
- `M_GUlt_StrikeBeam` · **`_TintColor` Alpha**(인스펙터 "Color") — `Shader_IntegratedEffect` 의 실제 알파. 셰이더가 알파를 **제곱**하므로 배율 0.6 → 체감 0.36. (`_BaseColor` 는 이 셰이더가 안 쓰는 옛 속성 — 처음에 이걸 지정해 효과가 없었다)
- `GunnerUlt_FloorLine` · **`_Color` Rgb**(인스펙터 "Color") + `_Opacity`(Float, 알파) — `HS_Blend_CG` 는 `_Color` 의 RGB 만 BaseColor·Emission 으로 보내고 **a 는 안 쓴다**. 알파 출력은 `_Opacity` 하나. `_Color` HDR ≈24 × `_Emission` 3 ≈ 72 라 알파만 낮추면 여전히 하얗게 타고 블룸이 남으므로 `_Color` 밝기도 같이 낮춘다(`_Emission` 을 낮추는 것과 같은 결과).
- `M_GUlt_StrikeSpark`(낙뢰 불꽃, 같은 `FX_GunnerUlt_Strike`) · 셰이더가 빔과 같다 → **`_TintColor` Alpha + `_TintColor` Rgb** 두 줄로 알파·밝기를 따로 낮춘다(같은 속성 두 줄 OK — 복사본 값에 차례로 곱함).
- 같은 머티리얼의 여러 줄은 한 복사본에 함께 걸린다. 새 머티리얼 추가 = 줄 하나(셰이더가 실제로 쓰는 속성 이름이어야 함). 속성이 없으면 경고 1회 후 그 줄만 건너뜀.
- 현재 프리팹 값(민경 조정): 낙뢰 빔 `_TintColor` Alpha 0.4 · 장판 `_Color` Rgb 0.2 · `_Opacity` 0.1 · 낙뢰 불꽃 `_TintColor` Alpha 0.4 + Rgb 0.4 — Play 로 조정. 속성 이름 앞뒤 공백·탭은 무시한다.
- 🔴 에디터가 열린 채 프리팹 파일을 밖에서 고치면 Unity 가 낡은 값으로 되써 버릴 수 있다(이 작업 중 실제로 한 번 `_BaseColor` 로 되돌아갔다). 고친 뒤 인스펙터에서 줄 3개가 보이는지 확인할 것.

## 구조
| 파일 | 내용 |
|---|---|
| `Effects/EffectMaterialAlpha.cs`(새) | `EffectMaterialAlphaRule`(직렬화 규칙) · `EffectMaterialAlpha`(인스턴스에 붙어 슬롯을 알파 곱한 **복사본**으로 교체·원복). 복사본은 (원본, 속성, 배율)마다 1번 캐시 |
| `Effects/EffectManager.cs` | `SetMaterialAlpha(handle, rules)` — `SetTint` 와 같은 수명(지금 파트·이후 delay/outro 파트·반납 원복). 순서 = 색 먼저, 알파 나중(`EffectRecolor` 가 원래 머티리얼 색을 기억하므로). `SetTint` 를 나중에 불러도 잠깐 원복 후 칠하고 다시 건다 |
| `Player/Gunner/GunnerTrackingLaserView.cs` | `otherClientAlpha` · `netCasterClientId`(NetworkVariable, 기본 `NoCaster`) · `ServerSetCaster` · 순수 판정 `IsOtherClientView` · 장판·낙뢰에 적용(낙뢰는 알파가 있으면 핸들 재생) |

**왜 MaterialPropertyBlock 이 아닌가** — 한계 돌파 색이 같은 렌더러에 블록을 걸고 반납 때 빈 블록으로 지운다(서로 덮어씀). 흰 재질에 블록을 걸면 이 셰이더들이 다르게 그려진 기록([handoff-overdrive-vfx.md](handoff-overdrive-vfx.md)) — `M_GUlt_StrikeBeam` 이 흰색.

## 은희 님께 — 바뀐 서버 코드(연출 인자만)
| 파일 | 내용 |
|---|---|
| `Player/Gunner/GunnerTrackingLaserSkill.cs` | 스폰 전 `laserView.ServerSetCaster(owner.OwnerClientId)`(시전자가 스폰됐을 때만). `ServerSetOverdriveStage` 바로 옆 — 같은 이유(스폰 메시지에 실려 첫 낙뢰보다 먼저 닿음). 판정 무변경, 새 RPC 없음, NetworkVariable 1개(ulong) |

## 테스트
`Player/Editor/GunnerUltOtherClientAlphaTests.cs` — 시전자/다른 클라/호스트/오프라인/미지정 판정 · 배율 · Color 성분(Alpha/Rgb) · 슬롯 교체(다른 슬롯·원본 에셋 불변) · 원복 · 복사본 캐시.

## 확인
- [ ] 컴파일 0 · EditMode(위 테스트)
- [ ] MPPM 2인: 거너 클라 = 원래 · 다른 클라·호스트 = 흐림(0.6) · 호스트가 거너면 반대
- [ ] 한계 돌파 단계 색 + 흐림 함께 · R 연속 사용·다른 거너 R 에 흐린 머티리얼이 남지 않는지(반납 원복)
- [ ] `HS_Blend_CG` 의 `_Opacity` 가 실제 알파에 반영되는지(안 되면 속성 이름만 바꾸면 됨)
