# 몬스터 호버 빨간 외곽선 (할 일 58)

내 마우스 커서 아래 적 한 마리에 빨간 외곽선을 그린다. **표시만, 로컬 전용** — 네트워크·서버 권한·판정 변경 없음.

## 구성

| 파일 | 역할 |
|---|---|
| `Assets/1.Scripts/Player/PlayerEnemyHoverOutline.cs` | `Player.prefab`(base) 루트 컴포넌트. 오너(오프라인 포함 `IsInputSource`)만 돈다. **색·두께 튜닝은 여기 한 곳.** |
| `Assets/1.Scripts/Player/Skill/Targeting/CursorUnitPick.cs` | 커서 레이 판정 공용 — 레이 → 마스크 안 첫 콜라이더 → Hurtbox 우선 Unit 해석 → 자기 자신·HP 0 제외. 스킬 조준(SingleTarget)도 이걸 쓴다. |
| `Assets/1.Scripts/Rendering/HoverOutline/HoverOutlineOverlaySet.cs` | 대상 렌더러마다 **같은 메시를 그리는 자식 겹 렌더러**(`__HoverOutline`)를 얹었다 뗀다. |
| `Assets/1.Scripts/Rendering/HoverOutline/HoverOutlineTracker.cs` | 대상 전환 규칙(한 번에 하나, 바뀐 프레임에만 떼기/붙이기). |
| `Assets/3.Materials/MA_MonsterHoverOutline.mat` | `Zone/InteractOutline` 셰이더('Outline' 패스만). 컴포넌트가 런타임 사본에 색·두께를 덮어 쓴다. |

- 대상 마스크: `GameLayers.EnemyTargetMask` = Enemy + EnemyHurtBox (단일 대상 스킬 데이터의 TargetableLayers 와 같은 조합).
- 대상: 위 마스크에서 해석된 **살아 있는 Unit** — 일반 몹·중간보스·23호·훈련 허수아비·보스 충전기(Unit 인 기믹) 포함. HP 0(사망 처리 중)·자기 자신 제외.
- 레이는 **프레임당 1회.** 단일 대상 조준 중엔 `PlayerSkillTargeting` 이 쏜 레이의 판정을 `ReportAimedTarget` 으로 넘기고 외곽선 쪽은 쏘지 않는다(LateUpdate 에서 판정).
- 벽은 레이를 막지 않는다(스킬 조준과 같음 — 마스크에 벽이 없다).

## 왜 겹 렌더러인가 (머티리얼 슬롯 덧붙이기가 아니라)

몬스터 렌더러의 머티리얼 배열은 몬스터 쪽 연출이 스스로 바꾸고 복원한다 — `MonsterAttackTell`(슬롯 덧붙임 후 찍어 둔 원본으로 복원),
`DissolveDeath`(배열 교체), `MonsterCollapseDebris`, `BossChargingPylon`. 우리가 슬롯을 덧붙이면 그들이 찍은 "원본"에 외곽선이 섞여 남거나,
우리가 떼면서 그들의 교체를 되돌린다. 또 덧붙인 슬롯은 **마지막 서브메시만** 그린다 — 23호 몸체는 서브메시 6개다.
겹 렌더러는 배열을 건드리지 않고(원본·공유 머티리얼 무수정, `.material` 인스턴스 생성 없음) 서브메시마다 외곽선을 둔다.

- `OutlineOnly.shader`(UV3 스무스 노멀)는 쓰지 않았다 — 몬스터 메시에 UV3 가 없다(2026-10-19 전수 확인). `Zone/InteractOutline` 은 일반 법선 + 화면 공간 두께라 그대로 맞는다.
- 몬스터 대부분은 이미 FlatKit 검정 외곽선(같은 Outline 피처)이 있다. 빨간 겹이 더 두꺼우면(기본 4) **검정 바깥에 빨간 테**로 보인다. 둘 다 Cull Front 헐이라 깊이로 정리된다.

## 연출과의 관계

- `HitFlash`·사망 틴트(MPB `_BaseColor`/`_Color`): 외곽선 셰이더는 `_OutlineColor` 만 읽어 영향 없음. HitFlash 가 겹을 캐시해도 색 프로퍼티가 없어 건너뛴다.
- 23호 체공 숨김·붕괴 사망(`enabled=false`): 겹은 매 프레임 원래 렌더러의 `enabled` 를 따라간다.
- 사망 디졸브: HP 0 이 되면 다음 LateUpdate 에 뗀다. 디졸브가 같은 프레임에 먼저 돌면 겹이 한 프레임 디졸브 사본으로 바뀔 수 있으나 곧 파괴된다(무해).
- 겹은 몬스터 하위에 런타임으로만 생기고(`HideFlags.DontSave`) 대상이 디스폰되면 함께 사라진다.
- 몬스터 코드는 반드시 몬스터 하위 렌더러를 **null 검사** 해야 한다(호버가 끝나면 겹이 파괴된다) — 현재 코드는 전부 검사한다.

## 알려진 한계 / 확인 필요

- **Play 확인 필요(사람)**: 실제 두께·색감, 23호 큰 메시, 검정 외곽선과의 겹침.
- 마우스가 HUD 위에 있어도 레이는 나간다(스킬 조준과 같은 동작).
