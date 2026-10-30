# 특성 강화 스킬 연출 이벤트 (할 일 145)

> 이펙트 담당은 **정적 이벤트 하나만 구독**하면 특성으로 강화된 스킬·추가 공격이 나가는/맞는 순간을 모든 클라에서 받는다.
> 이펙트 파사드 구조 개선은 지스타 이후(범위 밖) — 여기는 최소 이벤트만 있다. 판정·피해·권한은 바뀌지 않았다(연출 알림만).

| 무엇 | 어디 |
|---|---|
| 이벤트·인자 | `Assets/1.Scripts/Traits/Effects/TraitSkillFx.cs` — `TraitSkillFx.Raised`(static event) · `TraitFxEvent`(인자 구조체) · `TraitFxTrait`(특성 종류) · `TraitFxKind`(사건 종류) |
| 서버 → 전 피어 경로 | `Assets/1.Scripts/Player/Stats/TraitSkillFxNetwork.cs` + `Player.TraitSkillFxRpc`(**특성 공용 RPC 1개**, `SendTo.ClientsAndHost`) |
| 테스트 | `Traits/Editor/TraitSkillFxTests.cs`(ID 매핑·구독자 예외 격리·RPC 메시지 직렬화) — 맵씬 EditMode 메뉴에 포함 |

## 1. 구독 예시

```csharp
using UnityEngine;

public sealed class TraitSkillFxListener : MonoBehaviour
{
    [SerializeField] private EffectSocketPlayer echoBombBurst; // 예시

    private void OnEnable()  => TraitSkillFx.Raised += HandleTraitFx;
    private void OnDisable() => TraitSkillFx.Raised -= HandleTraitFx;

    private void HandleTraitFx(TraitFxEvent e)
    {
        switch (e.Trait)
        {
            case TraitFxTrait.GunnerEchoBomb when e.Kind == TraitFxKind.Exploded:
                // e.Position = 폭발 위치(구체 마지막 자리), e.Target = 붙었던 대상(없으면 null)
                break;

            case TraitFxTrait.PaladinAdvanceImpact when e.Kind == TraitFxKind.Activated:
                // e.Shape + e.Pose = 서버 판정 도형 그대로. 크기는 도형 값에서 읽는다.
                if (e.HasShape)
                    Debug.Log($"진격의 충격 at {e.Pose.Position}");
                break;
        }
    }
}
```

- 사건은 **피어마다 1번** 난다(호스트도 1번 — 서버 쪽에서 따로 내지 않는다). 내 화면 시전자만 거르려면 `e.Caster != null && e.Caster.IsInputSource`.
- 구독자에서 예외가 나도 다음 구독자·스킬 흐름은 계속된다(`Debug.LogException` 만).
- 도메인 리로드를 끈 Play 진입에서도 이전 구독이 남지 않게 Play 시작 때 비운다 — **OnEnable 에서 구독**할 것.

## 2. 인자 `TraitFxEvent`

| 칸 | 의미 |
|---|---|
| `Trait` / `TraitId` | 특성 종류 enum / 명세 ID(`PAL_4_Q_BLAST` 등) |
| `Kind` | `Activated`(발동) · `Hit`(대상에 들어감) · `Attached`(붙음) · `Exploded`(터짐) |
| `Caster` | 시전한 `Player` |
| `Position` · `Direction` · `EndPosition` | 연출 기준점 · 바닥 방향 · 선형 공격 끝점(없으면 Position) — 특성마다 뜻은 아래 표 |
| `Target` | 맞은·붙은 `Unit`. 없거나 이 피어에서 못 찾으면 null(상자 같은 비유닛 대상도 null) |
| `Shape` · `Pose` · `HasShape` | 공격 판정 도형(할 일 153, [attack-shapes.md](attack-shapes.md)) — 서버 판정과 **같은 공용 함수·같은 데이터·같은 특성 수치**로 만든 것. 그리기는 `AttackShapeDrawing`(에디터) 또는 도형 칸(`Radius`·`Length`·`Width` 등)을 읽어 VFX 크기로 |
| `Variant` | 특성별 보조 값(아래 표). 보통 0 |

## 3. 이벤트 목록

발화 위치 열: **전 피어 경로** = 원래부터 모든 피어에서 도는 코드(PlaySkillClientRpc→OnClientPlay·기존 ClientRpc·NetworkList 변경 사건)에서 바로 냄 / **공용 RPC** = 서버 판정 뒤 `TraitSkillFxNetwork.ServerSend` → `Player.TraitSkillFxRpc`. 둘 다 모든 클라(+호스트 1번)에서 받는다.

| 특성 | Trait | 사건 | 발화 시점 | 발화 위치 | 인자 |
|---|---|---|---|---|---|
| 진격의 충격 `PAL_4_Q_BLAST` | `PaladinAdvanceImpact` | Activated | Q 종료 + 파생 지연 뒤 **판정 순간**(맞은 대상 없어도) | 공용 RPC | Pos/Dir = 판정 포즈(Q 끝 지점·마지막 진행 방향), Shape = 특성 정의 파생 hitArea(부채꼴) |
| | | Hit | 추가 피해가 들어간 대상마다 | 공용 RPC | Pos = 맞은 콜라이더 중심, Target |
| 돌파의 방패 `PAL_2_DASH` | `PaladinShieldDash` | Activated | Q 시작(돌진 준비 시작) | 전 피어 경로(`FirstMeleeMainSkill.OnClientPlay`) | Pos = 시작 위치, Dir = 돌진 방향, End = 시작 + 방향 × 돌진 거리(안전 정지 전 값), Shape = 경로 표본마다 훑는 원래 Q hitArea(Pose = 시작 지점) |
| | | Hit | 돌진에 맞은 대상마다(시전당 대상 1회) | 공용 RPC | Pos = 콜라이더 중심, Dir = 돌진 방향, Target |
| 수호의 파동 `PAL_4_E_PUSH` | `PaladinGuardWave` | Activated | E 반격 판정 순간(보장 반격 포함, 밀린 대상 없어도) | 공용 RPC | Pos = 반격 위치, Dir = 방어 방향, Shape = 밀치기 원(`FirstMeleeSubSkill.TraitPushArea`, 반경 = 특성 수치) |
| | | Hit | 밀치기가 실린 대상마다(반격 피해와 함께·밀치기만 둘 다) | 공용 RPC | Pos = 콜라이더 중심, Dir = 밀리는 방향, Target |
| 광폭 레이저 `GUN_1_WIDTH` | `GunnerWideLaser` | Activated | Q 한 발 발사(집속 연사면 발마다) | 전 피어 경로(`GunnerBeamView.ChargeLaserFiredRpc`) | Pos = 발사 원점(총구 높이), End = 빔 끝, Shape = 그 발의 판정 도형(길이 = 이번 발, 폭 = 특성 폭) |
| | | Hit | 그 발이 피해를 준 몬스터마다(최대 8) | 전 피어 경로(같은 RPC) | Pos = 빔 위 적중 지점. **Target 없음**(기존 RPC 가 지점만 싣는다) |
| 잔류 포격 `GUN_4_ECHO_Q` | `GunnerEchoBomb` | Attached | 폭탄이 대상에 붙음(Q 정상 종료 뒤, 맞은 대상마다) | 전 피어 경로(`GunnerBeamView` NetworkList Add) | Pos = 대상 몸 콜라이더 윗면 가운데(`GunnerEchoBombTargets.BodyPoint`), Target, Variant = 폭탄 번호 |
| | | Exploded | 폭탄이 터짐(피해) 또는 대상이 먼저 사라진 **빈 폭발** | 전 피어 경로(NetworkList RemoveAt) | Pos = 구체 마지막 자리, Target(빈 폭발이면 null 또는 죽은 유닛), Variant = 폭탄 번호 |
| 이중 출력 `GUN_4_EXTRA_BASIC` | `GunnerDualOutput` | Activated | 추가 레이저 1발 판정 순간 | 공용 RPC | Pos = 발사 원점, Dir, End = 원래 평타 ShotRpc 와 같은 끝점(명중점·지형·최대 사거리). **Shape 없음**(굵은 직선 — 도형 아님) |
| | | Hit | 추가 레이저가 맞혔을 때 1번 | 공용 RPC | Pos = 끝점. **Target 없음**. 관통 탄도와 함께면 끝점은 벽/최대 사거리(맞은 지점 아님) |
| 칼날의 영역 `ASN_3_E_AREA` | `AssassinBladeDomain` | Activated | 일반 E 강타 시작 — **Variant 0** | 전 피어 경로(`AssassinBasicAttack` StartServer·StartRpc) | Pos = 시작 발밑, Dir = 강타 방향, Shape = 넓어진 강타 도형(Pose = 시작 순간 — 실제 판정은 클립 Hit 순간 위치, 전진만큼 앞) |
| | | Activated | 변신 E 원 베기 시작 — **Variant 1** | 전 피어 경로(`AssassinCircleStrikeSkill.OnClientPlay`) | Pos = 원 중심, Shape = 넓어진 원(서버 확정과 같은 식) |
| 끝없는 칼날 `ASN_4_COMBO_EXTRA` | `AssassinEndlessBlade` | Activated | 추가 베기 판정 순간(빗나가도) | 공용 RPC | Pos/Dir = 판정 포즈(예약 때 발밑·조준), Shape = 그 타 판정 도형(칼날의 영역 배율 포함), Variant = 모드 × 16 + 단 번호 |
| | | Hit | 맞은 유닛마다 | 공용 RPC | Pos = 유닛 위치, Target |
| 추격의 궤적 `ASN_4_Q_TRAIL` | `AssassinPursuitTrail` | Activated | 궤적 판정 순간(빗나가도) | 공용 RPC | Pos = 선분 시작, End = 선분 끝, Shape = 지나온 길이로 맞춘 Q 경로 도형, Pose = 판정 포즈, Variant = 돌진 스킬 순서(내부용) |
| | | Hit | 맞은 대상마다 | 공용 RPC | Pos = 콜라이더 중심, Target |

### 3.1 이벤트가 없는 특성(전수 확인 결과)

- **수치만 바꾸는 특성**(피해·쿨·회복·스탯·스킬 수치 — 진격 강화·수호 재정비·불굴의 회복·고속 집속·출력 강화·집중 포화·광란/강타 강화·완전 해방·돌파 가속 등): 기존 스킬 연출 그대로라 사건 없음.
- **모드 변경**(한계 돌파·축열 포격·정온 운용·집속 연사·관통 탄도·그림자 회귀·절제된 분노): 바뀐 동작이 원래 스킬 연출 경로(발수·과열 표시 등)를 그대로 탄다. 집속 연사는 발마다 Q 발사 연출이 이미 나고, 광폭 레이저와 함께면 발마다 위 Activated 가 난다.
- **사건형 보조**(수호의 순환·불굴의 순환·사냥의 박자 쿨 환급, 수호의 잔광·불굴의 가호 보호막, 나누는 불굴, 돌파의 여세): 보호막은 받는 쪽 `PlayerShieldVfx` 가 복제 목록으로 띄운다. 나머지는 눈에 보이는 공격이 아니다.
- **밀치기·뇌진탕만 붙는 특성**(제압의 진격·충격 레이저·급소 파괴): 원래 공격 적중 연출 + 대상 넉백·기절 표시로 보인다 — 이번 범위에서 따로 사건을 두지 않았다. 필요하면 수호의 파동 Hit 처럼 공용 RPC 로 더한다.

## 4. 잔류 포격 임시 구체 교체

붙어 있는 동안의 구체는 이벤트가 아니라 **프리팹 칸**이 그린다 — `Player_Gunner` Variant 의 `GunnerBeamView.echoBombPrefab`(지금 임시 `2.Prefabs/Player/Gunner/GunnerEchoBomb_Temp.prefab`). 이펙트 교체는 이 칸만 바꾸면 된다(폭탄마다 하나 복제, 대상 몸 콜라이더 윗면 가운데를 매 프레임 따라감 — 몸 중심이면 몸 메시 안에 묻혀 안 보인다). 폭발 연출은 지금 Q 몬스터 적중 연출(`chargeMonsterHitPlayer`) 재사용 — 따로 만들려면 `Exploded` 를 구독한다.
구체는 **Q 가 대상에 맞은 순간** 예비로 먼저 뜨고(`ChargeLaserFiredRpc` 의 맞은 대상 ID 칸, 잔류 포격 특성이 있을 때만), 서버 부착(`Attached`)이 같은 구체를 이어받는다 — 그래서 `Attached` 시점에는 구체가 이미 보이고 있을 수 있다. 부착 안 된 예비 구체는 `echoBombPreviewTimeout` 뒤 사건 없이 조용히 사라진다(`Exploded` 없음). 상세 [tutorial-traits.md §7.1](tutorial-traits.md#71-거너-잔류-포격--몸에-붙는-에너지-폭탄-할-일-149).

## 5. 동작 규칙 · 알려진 한계

- **호스트 중복 없음**: 전 피어 경로는 서버(승인/판정 경로)와 클라(NotServer RPC·`IsServer` 거름)가 이미 나뉘어 있고, 공용 RPC 는 `ClientsAndHost` 하나로만 낸다(서버 쪽에서 직접 `Raise` 하지 않는다). 오프라인(네트워크 없음)은 바로 낸다.
- **도형은 네트워크로 보내지 않는다** — 공용 RPC 는 포즈·지점·Variant 만 싣고, 받는 피어가 같은 데이터·같은 특성 수치(획득 행은 전 피어 같은 값)로 다시 만든다(`TraitSkillFxNetwork.ResolveShape`). 진격의 충격 도형은 `TraitDirector`(특성 정의) 가 없는 씬이면 null.
- **공용 RPC 는 Reliable**(연출 전용이지만 Hit 수가 적다 — 파생 공격·밀치기 대상 수만큼). 늦게 합류한 클라는 지난 사건을 받지 않는다. 잔류 포격은 늦게 합류해도 **구체**는 보이지만(NetworkList 초기 목록) 그 구체의 `Attached` 사건은 다시 내지 않는다.
- **공용 RPC 사건은 서버 판정 시각 + 전송 지연**에 클라에 도착한다. 전 피어 경로 사건은 그 경로의 기존 시각(스킬 시작·발사 RPC)과 같다.
- 전 피어 경로 사건의 위치는 **그 피어의 시전자 위치**다(원격 화면은 NetworkTransform 보간만큼 다를 수 있다 — 기존 스킬 연출과 같은 수준).
- 칼날의 영역(일반 E 강타)의 Pose 는 시작 순간 발밑이다 — 판정은 전진한 뒤 클립 Hit 순간 위치에서 한다.
- 광폭 레이저·이중 출력의 Hit 은 대상 오브젝트가 없다(기존 RPC 가 지점만 들고, 이중 출력 판정 함수가 맞은 유닛을 내지 않는다).
- 잔류 포격 `Exploded` 는 이 피어에서 구체를 한 번이라도 띄운(대상을 찾은) 폭탄만 난다 — 위치를 모르면 내지 않는다.

## 6. Play 확인 항목(사람)

DebugTraitLab(호스트) + MPPM 클라 1명으로, 임시 구독자(위 예시처럼 `Debug.Log`)를 씬에 두고:
1. 특성별로 위 표 사건이 **호스트·클라 각각 1번씩** 찍히는지(호스트 중복 없음).
2. 진격의 충격·끝없는 칼날·추격의 궤적·수호의 파동 `Activated` 의 `HasShape` 가 클라에서도 true 이고, 공격 범위 기즈모와 같은 자리·크기인지.
3. 잔류 포격: 대상마다 Attached → 지연 뒤 Exploded, 대상이 먼저 죽으면 빈 폭발 Exploded(Target null/사망).
4. 광폭 레이저 + 집속 연사: 발마다 Activated.
