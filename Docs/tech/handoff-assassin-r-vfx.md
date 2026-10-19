# handoff — 어쌔신 R 변신 VFX 정식 교체 (Blade Vortex)

> 받는 사람: **은희**(플레이어 영역 소유). 작성: 민경(Claude), 2026-10-14, 브랜치 `feature/VFX`. 계획 = [PLAN.md](../../PLAN.md) 맨 위.
> 요지: `AssassinSkillView` 의 **변신 루프만** 임시 Instantiate 경로에서 EffectManager 정식 VFX 로 바꿨다.
> 평타·Q 궤적·E·간파·백어택 임시 연출은 그대로다. 게임플레이 코드(AssassinState·스킬 판정)는 손대지 않았다.

## 1. 바뀐 동작
| 시점 | 전 | 후 |
|---|---|---|
| R 시전(`OnClientPlay`) | `Poof_electric` 1회 | **없음** — 루프의 차오름(0.5초)이 시전 연출을 겸한다 |
| 변신 중(`TransformChanged(true)`) | `CharacterCirclePurple` 를 `transformSocket` 에 Instantiate | `EffectCatalog.Assassin_R_BladeVortex` 를 **플레이어 루트**에 `PlayLooping` |
| 변신 종료(`TransformChanged(false)`) | StopEmitting + 2초 뒤 Destroy | `Release` — End 원샷 + 남은 입자 ×8 배속으로 **0.5초에** 흩어짐 |

- 종료는 시간 만료·2초 뒤 수동 해제·쓰러짐/사망 **모두 같은 이벤트**(`ResetForDownServer` → 상태 쓰기 → `TransformChanged(false)`)라 같은 연출이 나간다.
- 늦은 접속은 지금처럼 `SyncLoops`(Start·OnNetworkSpawn)가 현재 상태로 맞춘다. 이미 켜져 있으면 다시 켜지 않는다(핸들 확인).
- 디스폰·씬 언로드(`OnDestroy`)는 아웃트로 없이 `ReleaseImmediate`.

## 2. 코드
- `Assets/1.Scripts/Player/Assassin/AssassinSkillView.cs`
  - 삭제: `transformLoopPrefab` · `transformCastPrefab` · `transformSocket` 필드, `PlayTransformCast()`, `SetLoop()`.
  - 추가: `transformEffectOffset`(Vector3, 기본 0 — 프리팹 피벗이 발밑) · `transformEffectScale`(기본 1), `EffectHandle transformLoop`.
  - 엔트리가 비거나 씬에 EffectManager 가 없으면 **경고 후 연출만 빠진다**(게임플레이 영향 없음).
- `Assets/1.Scripts/Player/Assassin/AssassinTransformSkill.cs` — `OnClientPlay` 본문을 비웠다(추상 메서드라 빈 override 유지).
- `Assets/1.Scripts/Player/Editor/AssassinShellAuthoring.cs` — 지운 세 필드를 채우던 줄·경로 상수 제거. 다시 돌려도 변신 루프는 건드리지 않는다.
- `Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab` — 위 필드 직렬화 줄 정리(세 줄 삭제, 두 줄 추가). `Armature/VFX/Transformation` 소켓 오브젝트는 이제 참조가 없지만 **남겨 뒀다**(지워도 된다).

## 3. VFX 쪽(민경 영역, 참고)
- `EffectEntry.outroPlayRate`(기본 1) 신설 — `EffectManager.Release` 가 기존 루프 파트에 StopEmitting 과 함께 이 배속을 건다. 반납 시 1 로 복귀. 기본 1 이라 다른 엔트리 동작 불변.
- 에셋: `Assets/50.Art/VFX/Common/Players/Player3/R_Transform/` — `FX_Assassin_BladeVortex(.prefab|_End.prefab|_Entry.asset)`. Piloto Studio 의존 23개는 `50.Art/VFX/{ShaderGraph,_Materials,Textures,Models}/PilotoStudio/`. **전부 SVN**(민경 svn add).

## 3-1. 변신 E 원형 5타 루프 (2026-10-15 추가)
- `AssassinCircleStrikeSkill.OnClientPlay` → `view.BeginCircleStrike(원 중심, AreaRadius, MaxActiveDuration)`, `OnEnd`(완료·중단 공통) → `view.EndCircleStrike()`.
  원 중심은 기존과 같은 근사(플레이어 + 승인 방향 × CenterDistance). 임시 `Burst_rings` 1회(`circleStrikePrefab`)는 **유지**(민경 결정) — 루프와 함께 터진다.
- `AssassinSkillView`: `PlayCircleStrike` → `BeginCircleStrike`/`EndCircleStrike` + `circleStrikeScaleFactor`(기본 1, 배율 = 원 반경 × 이 값).
  클라에서 종료 RPC 가 막혀 `OnEnd` 가 안 오면 최대 지속 + 1초 뒤 스스로 해제(안전망). 재시작 시 남은 루프를 먼저 해제.
- 엔트리 `Players/Player3/E_CircleStrike/FX_Assassin_MultiSlashes_Entry` · 카탈로그 `Assassin_E_MultiSlashes`. `outroDuration` 1.1 — Release 가 방출만 멈추고 남은 입자가 제 곡선대로 사라진다(0 이면 즉시 지워져 뚝 끊김).

## 3-2. 변신 Q 돌진 루프 (2026-10-15 추가)
- `AssassinDashStrikeSkill.OnClientPlay` → 데이터가 `AssassinTransformedDashStrikeSkillData` 일 때만 `view.BeginStormSlash(MaxActiveDuration)`, `OnEnd`(완료·벽 조기 종료·중단 공통) → `view.EndStormSlash()`. 일반 Q 는 변화 없음.
- 임시 돌진 궤적(`dashTrailPrefab`)은 **삭제**(10-16) — 변신 Q 는 Storm Slash, 일반 Q 는 아래 §3-3.
- `AssassinSkillView`: `BeginStormSlash`/`EndStormSlash` + `stormSlashOffset`(0)·`stormSlashScale`(1). 플레이어 루트 추종, 안전망(최대 지속 + 1초) 동일.
- 엔트리 `Players/Player3/Q_TransformedDash/FX_Assassin_StormSlash_Entry` · 카탈로그 `Assassin_Q_StormSlash` · `outroDuration` 1.1. 입자는 World + Rate over Distance — 움직인 만큼 지나간 자리에 남는다.

## 3-3. 일반 Q 관통 일섬 (2026-10-16 추가)
- `AssassinDashStrikeSkill.OnClientPlay`(일반 데이터) → `view.BeginPierce(시작 위치, 돌진 방향)` — 기억만 한다. `OnEnd`(사유 무관) → `view.EndPierce()`.
- `EndPierce`: 길이 = 돌진 방향으로 실제 나아간 거리. `Assassin_Q_PierceLine` 을 시작점에 축별 배율 `(s, s, 길이)`로, `Assassin_Q_PierceBurst` 를 끝점에 배율 `s` 로 원샷. 길이 < `pierceMinLength`(0.3m)면 일섬 생략.
- `AssassinSkillView`: `dashTrailPrefab`·`BeginDashTrail`/`EndDashTrail`·`Release(ref)` **삭제**, `pierceScale`(1)·`pierceMinLength`(0.3) 추가. `AssassinShellAuthoring` 의 해당 줄도 삭제.
- `EffectManager.Play(entry, pos, rot, Vector3 axisScale)` 오버로드 신설(VFX 코드) — localScale 에만 축별로 곱한다. 기존 float 배율 경로는 동작 불변.
- 에셋 `Players/Player3/Q_Dash/FX_Assassin_PierceLine`·`_PierceBurst`(+Entry, duration 0.6·1.0).

## 3-4. 일반 E 단검 강화 — 날 붉은 발광 + 오라 + 차지 (2026-10-16 추가)
- ✅ **공용 셰이더 `ToonLit`·`MaterialFadeEffect` 는 수정 없음**(10-16 2차 — 처음 넣었던 발광·날 마스크·Color 모드는 되돌렸다). 칼날 발광 = **팔라딘 패시브와 같은 방식**:
  양손 단검 렌더러 `m_Materials` = [0] 원래 `Assassin_Toon.mat` + [1] 오버레이 `50.Art/VFX/_Materials/Dagger_Blade_Glow.mat`(`Shader_IntegratedEffect`, 마스크 `50.Art/VFX/Textures/Dagger_BladeMask.png` = 단검 UV0 의 칼날 폴리곤만).
- 신규 `AssassinEnhanceReadyView`(Assassin_Armature `VFX/EnhancedReady` 에 부착, 같은 오브젝트에 MaterialFadeEffect `_ColorFactor` 0 ↔ 1, 0.5초 인 / 0.3초 아웃): `AssassinState.IsEnhancedReady` 로 켜고 끈다. 차지는 `EnhancedReadyChanged(true)` 순간만, 늦은 접속·Armature 재활성은 Update 가 차지 없이 맞춘다. **추가 RPC 없음.**
- 단검 소켓 `Dagger_Weapon_R/L/VFX_EnhanceAura`(날 중간 로컬 Y 0.33, Assassin_Armature 에서 추가) — 오라·차지가 붙는다.
- `Player_Assassin` 의 `enhanceCastPrefab`(임시 Flash_star) 비움 — 차지가 대신한다. `AssassinSkillView.PlayEnhanceCast` 호출은 그대로(빈 필드 = 연출 없음).
- 강화 일격 슬래쉬는 `enhancedSlashPrefab` 교체만(코드 무변경) — 반입 때.

## 4. 확인 부탁
- Play: R → 0.5초에 소용돌이가 차고 따라다님 / 만료·수동 해제·쓰러짐 → 0.5초에 흩어짐.
- MPPM: 다른 피어·변신 중 늦게 들어온 클라에서도 보이는지.
- 변신 E: 원 표시 중심에서 루프가 나오고, 5타 끝(또는 중단) 뒤 1초쯤에 걸쳐 흩어지는지.
- 변신 Q: 돌진 경로에 자국이 남고, 출발 순간 엉뚱한 곳에서 한 줄로 쏟아지지 않는지(풀 인스턴스 위치 이동).
- 일반 Q: 돌진이 끝나고 한 박자 뒤 경로 위로 일섬 + 끝점 버스트, 벽에 막히면 일섬이 그만큼 짧은지. 다른 피어에서도 경로와 맞는지.
- 일반 E: 누르면 양손 단검 날만 붉게 + 검붉은 오라, 강화 일격·변신·쓰러짐에 꺼짐. 다른 피어·늦은 접속에서도 같은지. 다른 캐릭터(팔라딘·거너·어쌔신 몸)가 그대로인지.
