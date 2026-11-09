# 핸드오프 — 거너 좌클릭·Q 벽까지 거리 = 구 표면 접촉 거리 + 부서지는 프롭 Hurtbox 여유

> 2026-11-06 · 민경(Claude) · 브랜치 `feature/VFX`.
> **은희 님 영역(거너 판정 `GunnerBeamAttack`)을 고쳤다** — 아래 "바뀐 판정"만 보면 된다.

## 증상
거너 우클릭은 상자·팔레트(`Breakable`)를 늘 부수는데, 좌클릭·Q 는 부서질 때도 있고 아무리 쏴도 안 부서지는 프롭이 있었다.

## 원인
| 공격 | 원인 |
|---|---|
| 좌클릭 `Fire` | 단일 프롭은 **차단 박스(Default) = Hurtbox 박스(EnemyHurtBox)** 가 같은 크기·중심. SphereCast 거리가 같아 정렬 순서가 매번 달라 — 차단 박스가 먼저면 "벽에 막힘"으로 끝났다 |
| Q `CastLength` → 판정 사각형 | `RaycastHit.distance` 는 **구 중심** 이동 거리. 그걸 벽까지 길이로 써서 판정 사각형·빔·조준선이 벽 표면보다 반경(Q 0.6m)만큼 앞에서 끝났다 → Hurtbox 가 차단 박스와 같으면 정면에서 절대 못 맞음 |
| 좌클릭 (팔레트) | 레이저 높이(총구 ≈1m ± 반경 0.3) 아래 0.7m 미만은 못 훑는다 — `SM_Prop_Pallet_01`(0.33m) 은 **그대로 안 닿음**(미해결, 기획 판단) |

우클릭(간파)은 벽 검사 없는 상자 판정이라 영향 없었다.

## 바뀐 판정 (은희 님 영역)
`Player/Gunner/GunnerBeamAttack.cs`
- 새 `ContactDistance(sweep, point, origin, dir, radius)` — 접촉점을 빔 축에 투영, `[중심 거리, 중심 거리 + 반경]` 으로 자름(정면 = +반경, 모서리 스침 = 그보다 짧음). 시작부터 겹침(distance 0) = 0.
- `CastLength` — 가장 가까운 벽의 **접촉 거리**를 돌려준다(최대 range). → Q 판정 사각형(`ShotArea`)·조준선(`ShowCharge`)·Q 미리보기 클립(`TryGetPreview`)·빔 끝(`ServerChargeLaserFired` end)이 모두 벽 표면까지.
- `Fire` — 끝점(`end`, 연출) = 접촉 거리. **피해 판정 순서는 그대로**(구 중심 거리 정렬).
- `FireThrough`(관통 탄도) — 후보가 "벽 앞인가"는 여전히 **구 중심 거리끼리** 비교(`wall`), 끝점만 접촉 거리(`wallContact`).

영향: Q 가 벽에 바짝 붙은 얇은 대상·차단 콜라이더를 가진 상자를 정면에서 맞힌다. 빔·착탄 연출이 벽 반경만큼 더 나아간다(표면). 일반 몬스터(차단 레이어 아님) 판정은 거의 무변화.
모서리 접촉에서 판정 사각형이 투영 접촉점까지라 얇은 벽 모서리를 아주 조금 넘을 수 있다(최대 반경 미만).

## 연출·도구
- `GunnerBeamView.SurfaceFacing` — 주석만(끝점이 이제 면 위라 0.05m 뒤에서 쏘는 기존 탐색 그대로 동작).
- `Dev/TraitLab/TraitLabAttackGizmos` — 평타 캡슐은 구가 쓸고 간 부피라, 벽에 막혔으면 `접촉 − 반경` 까지 그린다.

## 프롭 (Hurtbox 여유)
`2.Prefabs/Environment/Props/Containers/` 8개 — Hurtbox 박스 옆면 각 +0.1m, 윗면 +0.1m(바닥 그대로). 차단 박스는 무변경. 좌클릭 동률 순서 문제 해결용.
- 단일 프롭 6개: 자식 `Hurtbox` 박스만 키움(중심 y +0.05).
- `PF_box_3stack`·`PF_box2stack`: 단일 프롭과 같은 구조로 변환 — 루트 레이어 `Default`, 루트의 판정 박스·`Hurtbox` 를 새 자식 `Hurtbox`(EnemyHurtBox, 트리거)로 옮김(fileID 유지), 크기 = 자식 상자 콜라이더 합집합 + 옆·위 0.1m, 자식 상자 레이어 `Default`(차단). 2stack 은 예전 루트 박스가 트리거가 아니고 상자보다 좁았다.

## 테스트
`Player/Editor/GunnerBeamContactDistanceTests.cs` — 순수 함수(정면·스침·자름·시작 겹침) + 실제 SphereCast `CastLength`(벽 앞면 z=5 → 5, 예전 4.4 · 벽 없음 → range).

## 남은 것
- [ ] 컴파일·EditMode(`GunnerBeamContactDistanceTests`, `GunnerSkillAreaTests`) — 작성 시점 Unity 꺼져 있어 미실행
- [ ] Play: Q 정면으로 상자·팔레트 더미 파괴 · 좌클릭 여러 번 매번 파괴 · 빔/착탄이 벽 표면에서 멈추는지 · 조준선 길이
- [ ] `SM_Prop_Pallet_01` 좌클릭 파괴 여부 — 기획 판단(원하면 Hurtbox 높이 ≥ 0.9m)
