# 몬스터 리모델링 — VFX 호환 보강 계획

작성: 2026-09-26 · Unity 6000.3.16f1 · 상태: **8종 연결 구조 검사·Mortar Editor 프리뷰 확인 / 실전 VFX 검증 대기**

실행 결과: [제작 결과와 검증 범위](C:/MainProject-git/Generated/monster-remodel-20260926/README.md). 실제 피격·공격 이벤트, RPC, 사망 코루틴·풀 회수 검증은 남아 있다. 아래 시험 전체를 통과한 것으로 해석하지 않는다.

## 1. 결론과 적용 범위

**기존 본과 애니메이션을 유지하는 것에 더해, VFX의 연결·발생 위치·시점·종료·재질 반응을 함께 보존한다.** 새 외형은 복원된 초기 디젤펑크 8종 시안을 기준으로 한다. [전체 제작 계획](C:/MainProject-git/PLAN-monster-remodel.md)에 이 문서의 조건을 함께 적용한다.

현재 구조는 메쉬 교체 방식으로 VFX를 유지할 수 있다. 다만 외장이 두꺼워지면 소켓이 장갑 안에 묻히거나, 피격 섬광이 새 표면과 어긋날 수 있다. 새 메쉬·재질에서의 실제 호환성은 아래 시험을 통과해야 확정된다.

이번 목표는 현재 연결된 효과의 호환이다. Mortar·Tesla의 새 포구 섬광, Wall의 새 돌진 궤적, Tesla의 전기 공격처럼 현재 연결되지 않은 연출을 새로 추가하는 작업은 별도다.

## 2. 확인한 현재 구조

| 경로 | 실제 동작 | 리모델링에서 보존할 것 |
|---|---|---|
| 애니메이션 효과 | 클립의 `PlayEffect / StartEffect / StopEffect` → Animator의 `EffectAnimEventRelay` → 루트의 `EffectAnimEvents` → ID가 같은 재생기 | 클립 이벤트와 시간, Animator 위치, ID, 재생기와 소켓 참조 |
| 소켓 효과 | `EffectSocketPlayer`가 `EffectEntry`를 재생. 원샷은 시작 위치에 남고, 루프는 지정 Transform을 추종 | 원샷/루프 구분, 소켓 위치·회전, offset·scale, 정상 종료와 중단 경로 |
| 투사체 효과 | `MonsterProjectileEffects`가 네트워크 스폰 때 궤적 시작, 디스폰 때 궤적 해제 후 폭발 | 투사체 프리팹·Entry 참조·발사구·발사 방향·궤적 종료 |
| 조준·공격 예고 | 포탑의 `TrackingLaser`, Gauntlet의 Smash 예고, 중간보스 인터럽트 오버레이 | 예고선 원점·방향, 실제 판정과 맞는 예고 크기, 표시 기간 |
| 피격 | 서버가 공격자 위치를 알리고 각 피어가 자기 콜라이더에서 효과 위치를 계산. HitFlash는 렌더러 색을 변경 | 피격 콜라이더, 재질 색 프로퍼티, 기존 렌더러와 색 복원 |
| 사망 | `DissolveDeath`가 재질을 교체하고 첫 스킨드 렌더러를 파티클 방출 표면으로 연결 | 사망 대상 렌더러, 새 메쉬 표면, 재질의 텍스처·색 전달, 디스폰까지의 수명 |

`EffectEntry`는 파트 프리팹·지연·사운드·수명·종료 파트를 갖는다. `EffectManager`가 이를 재생하고 파트별 풀에 반환한다. 효과를 몸체의 자식으로 새로 붙여 직접 재생하는 방식으로 바꾸지 않는다. [이벤트 등록](C:/MainProject-git/Assets/1.Scripts/Effects/EffectAnimEvents.cs:43), [소켓 재생](C:/MainProject-git/Assets/1.Scripts/Effects/EffectSocketPlayer.cs:73), [효과 수명](C:/MainProject-git/Assets/1.Scripts/Effects/EffectEntry.cs:25), [효과 풀](C:/MainProject-git/Assets/1.Scripts/Effects/EffectPool.cs:36).

## 3. 8종의 실제 VFX 연결과 제작 조건

아래는 현재 프리팹·클립 메타·코드를 읽어 확인한 연결이다. 소켓 이름은 식별용이며 제작 기준 기록에서는 전체 경로와 fileID까지 저장한다. 소켓 필드가 비어 있어 **자기 Transform을 사용하는 정상 설정**도 있으므로, 빈 필드를 일괄 보정하지 않는다.

| 몬스터 | 현재 연결 | 새 외형에서 맞출 부분 / 필수 시험 |
|---|---|---|
| **Chomp** | `PlayEffect("Bite")` → `Bite_VFX_Socket` → `Fx_Bite_Entry`, 원샷 | 위·아래턱을 벌리고 닫을 때 물기 효과가 이빨·금속 덮개에 묻히지 않아야 함. 발동 순간의 위치에 남는 현재 표현 유지 |
| **Humanoid** | AttackStrike의 `PlayEffect("Slash")` → `SlashVFX_Socket` → `FX_Mob_Slash_Entry`, 원샷 | 손잡이·무기 끝·베기 효과의 방향과 시점 일치. 새 무기 길이 때문에 효과와 타격점이 벌어지지 않게 제작 |
| **Mortar** | 자체 소켓형 VFX/클립 VFX 이벤트 없음. 기존 발사구에서 공유 투사체를 발사하며 투사체에 궤적·폭발 연결 | 포신 입구를 기존 포구에 맞춤. 발사·비행·착탄·수명 만료를 검사. 별도 포구 섬광이 없는 상태를 누락 버그로 판정하지 않음 |
| **Peek** | Shoot의 `PlayEffect("Muzzle")` → 자기 `Muzzle_Socket` → `FX_Mob_Muzzle_Entry`, 원샷. `TrackingLaser`와 공유 투사체 | 포구 섬광·조준선·탄 출발점이 일치. 머리 좌우/후방 회전에도 전면 덮개가 빛과 탄을 가리지 않아야 함 |
| **Tesla** | 소켓형 VFX/클립 VFX 이벤트 없음. `TrackingLaser`와 공유 투사체 | 넓어진 머리 외장과 기존 포구·조준선의 간섭 확인. Peek과 동일한 포구 섬광이 있다고 전제하지 않음 |
| **Gauntlet** | `Wind_L/R` → `Punch_L/R_Socket` → `FX_Punch_Wind_Entry` 루프. 명중 RPC의 `Spark_L/R` → `Spark_L/R_Socket` → `FX_Punch_Hit_Spark_Entry` 원샷. `Smash` 예고 루프·`Dust` 원샷·카운터 성공 섬광 | 좌우 6종 펀치 모두 바람·스파크가 건틀릿 끝과 맞아야 함. 헛스윙에는 명중 스파크가 없어야 함. Smash 예고·먼지의 중심/바닥 높이와 카운터 중단 확인 |
| **Spinner** | `Whip_L/R` → `WhipL/R_Socket` → `FX_Mob_Slash1_Entry` 원샷. `Spin` → `Spin_Socket` → `FX_Mob_Spin_Entry` 루프/종료 파트. 별도 메쉬 잔상과 카운터 성공 섬광 | 좌우 끝날과 휩 섬광, 회전 반경과 루프 효과 정합. 정상 돌진 종료·카운터·사망에서 루프 회수. Body/Blades와 파티클 효과를 각각 검수 |
| **Wall** | 소켓형 효과는 코드에서 호출하는 카운터 성공 섬광. 인터럽트 오버레이·공통 피격·사망 효과도 있음 | 전면 장갑과 방패가 섬광을 가리지 않아야 함. 평타·돌진·충격파는 기존 표시와 판정을 기준으로 비교하며 새로운 돌진 VFX를 가정하지 않음 |

세 중간보스의 카운터 성공 섬광은 `InterruptFlashVFX`에서 공통 `FX_Interrupt_Flash_Entry`를 원샷으로 재생한다. 현재 ID가 비어 있어도 코드가 직접 참조하므로 정상이다. 애니메이션 ID를 임의로 새로 부여할 필요가 없다.

소켓의 현재 부모도 보존한다. 외형상 자연스러워 보이는 본으로 다시 붙이면 기존 VFX 궤적이 바뀐다.

| 기준점 | 확인한 부모·특징 |
|---|---|
| Chomp Bite | `Head/Bite_VFX_Socket` |
| Humanoid Slash | `R_SlendertBot/Root/SlashVFX_Socket`. 무기 자식이 아님 |
| Mortar Muzzle | 게임 루트 직속, 로컬 위치 `(0, 1.7, 0.35)`. Cannon 본 자식이 아님 |
| Peek Muzzle | `HeadRotator/Head/Muzzle_Socket` |
| Tesla 발사구 | ranged muzzle이 `Head` 본을 직접 참조 |
| Gauntlet Wind/Spark | `FingerB01.L/R` 아래의 Punch/Spark 소켓. Wind는 scale 2와 offset `(0, 0, 0.5)` 사용 |
| Gauntlet 예고/성공 Flash | SmashTelegraph는 게임 루트 아래 y=0.02, 성공 Flash 소켓은 `FaceSwitch` 아래 |
| Spinner Whip/Spin | Whip은 `Knife_L/R` 끝, Spin은 `Root` 아래 y=1. 성공 Flash도 Spin 소켓 사용 |
| Wall 성공 Flash | 별도 소켓 대신 `Head` 본 직접 참조 |

Mortar·Peek·Tesla는 **같은 `P_MonsterProjectile.prefab`**을 참조한다. 여기에 `FX_Mob_Bomb_Trail_Entry`와 `FX_Mob_Bomb_Explode_Entry`가 연결되어 있다. 한 종의 외형을 맞추려고 공용 투사체·효과를 수정하면 다른 두 종도 영향을 받는다. [투사체의 실제 연결](C:/MainProject-git/Assets/2.Prefabs/Monster/P_MonsterProjectile.prefab:240), [투사체 재생 수명](C:/MainProject-git/Assets/1.Scripts/Effects/MonsterProjectileEffects.cs:39).

현재 연결된 공격 효과의 파트들은 자체 VFX 메쉬·파티클을 사용하며 몬스터의 스킨드 메쉬를 Shape로 참조하지 않는다. 투사체 궤적에는 별도 TrailRenderer 2개가 있다. Gauntlet Smash 예고는 SpriteRenderer와 FadeInHoldEffect를 쓴다. **새 몬스터 메쉬 자체에 직접 영향을 받는 예외는 사망 파티클 표면과 Spinner의 스킨드 잔상판**이다. 공격 효과 전체를 새 몸체에 다시 스키닝할 필요는 없다.

## 4. 새 메쉬 제작을 제한하는 핵심 조건

### A. 피격 이펙트용 콜라이더가 실제 피격 판정과 같다

8종의 `hitVFXCollider`는 모두 Hurtbox에 붙은 CapsuleCollider를 참조한다. 별도의 장식용 표면이 아니다. 따라서 피격 섬광을 새 장갑 표면으로 옮기려고 이 콜라이더를 확대·이동하면 실제 피격 판정도 달라진다.

**기본 대응:** 콜라이더를 고정하고 새 몸체의 주요 피격 면을 그 범위에 맞춘다. 앞·뒤·옆에서 맞을 때 섬광이 허공이나 장갑 깊숙한 곳에 보이지 않는지 확인한다. 형상 조정으로 해결할 수 없어 별도 VFX 표면 계산이 필요하면 후속 설계 항목으로 기록한다. [피격 위치 계산](C:/MainProject-git/Assets/1.Scripts/Effects/HitVFXPlayback.cs:38), [표면 계산](C:/MainProject-git/Assets/1.Scripts/Effects/EffectHitPoint.cs:83).

### B. 포구·소켓을 고정하고 외장의 입구를 맞춘다

총구는 탄 출발점과 조준선에도 쓰인다. 장갑을 키운 뒤 총구 본이나 소켓을 앞으로 옮기는 방식으로 보정하지 않는다. 포신 입구·주먹 끝·날 끝을 기존 기준점에 맞춰 제작한다.

소켓 추종 효과는 부모의 스케일을 상속하지 않는다. Chomp의 100 배율이나 Gauntlet의 모델 배율을 바꿔 효과 크기를 맞추는 방식도 사용하지 않는다.

현재 구현에서 루프의 socket offset은 **회전을 따르는 월드 단위 길이**이고, 원샷의 socket offset은 `position + offset`으로 적용된다. 같은 숫자라도 회전 시 결과가 다를 수 있다. 기본값을 유지하고, 보정이 필요하면 회전별 실제 화면으로 검증한다. Entry 내부 파트 offset에는 효과 scale이 적용된다. [원샷 위치](C:/MainProject-git/Assets/1.Scripts/Effects/EffectSocketPlayer.cs:99), [루프 위치 계산](C:/MainProject-git/Assets/1.Scripts/Effects/EffectManager.cs:556).

### C. 재질·렌더러 구조가 VFX 연결의 일부다

- **HitFlash:** 최초 피격 또는 기본색 사용 때 비활성 자식까지 렌더러와 첫 재질의 기본색을 캐시한다. 새 외형·재질은 스폰 전에 완성한다. `_BaseColor`/`_Color` 지원과 피격 후 색 복원을 확인한다. 별도 MaterialPropertyBlock로만 넣은 외형 값이 피격 종료 때 사라지지 않는지도 검사한다.
- **InterruptOverlay:** Gauntlet·Spinner·Wall은 지정된 몸체 렌더러의 재질 슬롯 1을 사용한다. 몸체를 여러 렌더러/서브메쉬로 나누거나 일반 재질을 추가하지 않는다. 기존 1서브메쉬 몸체와 `[몸체, Interrupt]` 순서를 유지한다.
- **DissolveDeath:** 사망 시 재질을 바꾸면서 조건부로 `_BaseMap`→`_MainTexture`, `_BaseColor`→`_BaseColor`를 복사한다. 금속·노멀·발광·툰 조명·텍스처 tiling/offset은 복사하지 않는다. 실제 ShaderGraph의 색 이름은 `_Main_Color`여서 기본색도 자동 전달된다고 확정할 수 없다. 컴파일된 셰이더의 지원 프로퍼티와 생존→피격→인터럽트→사망 외형을 파일럿에서 확인한다.
- **사망 파티클:** 첫 스킨드 렌더러를 방출 표면으로 직접 연결한다. 파티클 프리팹은 서브메쉬 0 선택과 메쉬 정점 색 사용이 켜져 있으므로 이 둘도 검사한다. 기존 렌더러의 메쉬를 교체하고, 숨긴 원본 몸체를 추가로 남기거나 자식 순서를 임의로 바꾸지 않는다. Gauntlet의 정적 주먹까지 전신에서 자동 방출된다고 가정하지 않는다.
- **Spinner 예외:** 사망 디졸브 목록은 Body 렌더러 하나를 명시한다. Blades 잔상은 이 목록 밖이므로 생존·회전·사망 시 표시를 별도 비교한다.

근거: [피격 색 캐시](C:/MainProject-git/Assets/1.Scripts/Unit/HitFlash.cs:94), [인터럽트 슬롯](C:/MainProject-git/Assets/50.Art/VFX/Scripts/InterruptOverlay.cs:61), [사망 재질](C:/MainProject-git/Assets/1.Scripts/Monster/DissolveDeath.cs:159), [사망 파티클 표면](C:/MainProject-git/Assets/1.Scripts/Monster/DissolveDeath.cs:206).

인터럽트 무늬는 현재 메쉬의 로컬 좌표를 사용하고 법선 방향으로 표면을 부풀린다. 같은 월드 크기로 보여도 로컬 좌표·배율·법선이 바뀌면 오버레이 무늬 크기와 두께가 달라질 수 있다. 기존 메쉬 좌표계를 유지하고 새 법선을 검수한다. [오버레이 셰이더](C:/MainProject-git/Assets/50.Art/VFX/Shaders/InterruptOverlay.shader:113), [사망 색 프로퍼티](C:/MainProject-git/Assets/50.Art/VFX/ShaderGraph/DissolveFx.shadergraph:1057).

### D. 시작과 종료를 함께 보존한다

- `EffectAnimEvents`는 Awake에서 ID를 수집한다. 런타임에 새 재생기나 동일 ID를 덧붙이지 않는다. Animator의 릴레이와 상위 등록소 연결을 유지한다.
- 원샷은 발동 시 위치·회전에 남는다. 루프는 소켓을 따라가며 Stop/Release에서 종료 파트와 잔여 시간을 거친 뒤 반환된다. 임의로 원샷을 루프로 바꾸지 않는다.
- Gauntlet의 Smash 예고는 공격 상태를 벗어날 때 코드로 종료한다. Spinner의 Spin도 정상 돌진 종료가 Dizzy 클립을 지나지 않으므로 상태 이탈 시 코드 종료가 필요하다. 이 경로를 유지한다.
- Gauntlet의 Wind_L/R은 펀치 클립의 Stop 이벤트로 종료하지만, 상태 이탈 코드에서 즉시 끄는 대상은 Smash뿐이다. 펀치가 중간에 잘리면 OnDisable·5초 안전 타임아웃에 의존할 가능성이 있으므로 원본에서도 재현되는지 선행 검사한다.
- 재진입·카운터·피격 전이·사망·디스폰·씬 종료를 검사한다. 안전 타임아웃에 도달해서 꺼지는 것을 정상 종료로 합격 처리하지 않는다.

근거: [Gauntlet 상태 이탈](C:/MainProject-git/Assets/1.Scripts/Monster/Boss/GauntletBot.cs:300), [Spinner 상태 이탈](C:/MainProject-git/Assets/1.Scripts/Monster/Boss/SpinnerBot.cs:280), [Release와 종료 파트](C:/MainProject-git/Assets/1.Scripts/Effects/EffectManager.cs:338).

### E. 네트워크 재생 경로를 유지한다

애니메이션 이벤트 효과는 각 피어에서 로컬로 재생한다. Gauntlet 명중 스파크·중간보스 카운터 성공·피격 효과는 기존 RPC 경로를 유지한다. 투사체 궤적·폭발은 각 피어의 네트워크 스폰/디스폰에서 재생한다. 어느 경로에도 추가 RPC나 서버 전용 조건을 붙이지 않는다.

특히 이동 중 피격은 클라이언트가 보간된 자기 몬스터의 콜라이더에서 위치를 계산한다. 호스트 화면만 보고 합격시키지 않는다. [피격 RPC](C:/MainProject-git/Assets/1.Scripts/Monster/MonsterBase.cs:1097), [명중 스파크](C:/MainProject-git/Assets/1.Scripts/Monster/Boss/GauntletBot.cs:268).

## 5. VFX 보정이 필요할 때의 작업 범위

1. **먼저 메쉬를 조정한다.** 포구 덮개·턱·건틀릿·방패의 두께와 열린 공간을 조정해 기존 효과를 살린다.
2. 그래도 필요한 경우 **게임 판정과 무관한 해당 몬스터의 VFX 전용 offset·scale 또는 별도 Entry/파트 사본**으로 한정해 보정안을 만든다. 원샷/루프의 좌표 해석 차이까지 검사하고 종별 변경값을 기록한다.
3. 공용 Entry·파트 프리팹·셰이더를 직접 바꾸는 대신 영향 범위를 확인한다. 공유 자원을 바꿔야 하면 관련 몬스터 전체를 재검수한다. 이 범위는 VFX 담당 민경과 몬스터 담당 경석이 함께 검토한다.
4. 본·총구·공격 소켓·피격 콜라이더·애니메이션 이벤트 시간·게임 판정·네트워크 경로는 외형 보정 수단으로 사용하지 않는다.

Gauntlet Smash 예고는 실제 `smashRadius`를 코드에서 효과 scale에 넣는다. **예고 프리팹의 기준 반경 1과 판정 반경의 대응을 유지**한다. 장갑이 커졌다는 이유로 예고만 넓히지 않는다. [크기 연결](C:/MainProject-git/Assets/1.Scripts/Monster/Boss/GauntletBot.cs:407).

## 6. 기존 제작 계획에 추가할 단계

### V0 — 원본 VFX 기준 기록

제작 전에 원본 8종을 실제 게임에서 재생해 다음을 기록한다.

- 효과별 시작 주체(클립/코드/RPC), 클립 GUID·이벤트명·문자열·시간, 원샷/루프/중단 경로.
- Animator·릴레이·등록소·재생기의 오브젝트 참조, ID, 소켓 전체 경로·fileID·로컬 Transform, offset·scale·타임아웃.
- Entry·파트·재질 GUID, part delay·수명·종료 파트·사운드, 공용 사용처.
- 포구·예고선·피격 콜라이더·사망 표면 렌더러, 인터럽트 슬롯과 숨김/표시 상태.
- 실제 카메라에서 원본 공격·피격·카운터·사망 영상. 현재 없는 연출과 기존 문제도 기록해 새 메쉬의 결함과 구분한다.

**통과 기준:** 종별로 무엇이 어디서 켜지고 언제 꺼져야 하는지 설명할 수 있고, 비교 영상과 원본 참조를 확보한다.

### V1 — 단순 외형에서 위치와 크기 확인

Mortar 파일럿에서 포구→투사체 궤적→폭발·피격·사망을 먼저 확인한다. Humanoid에서는 무기와 베기 효과를 확인한다. 나머지 종도 상세 장갑을 만들기 전에 해당 소켓과 효과가 보이는 공간을 확보한다.

**통과 기준:** 포구·주먹·턱·칼날과 효과가 맞고, 장갑에 가려지지 않는다. 회전해도 예고선이 탄 출발점과 맞으며, 피격 섬광이 실제 몸체 근처에 보인다.

### V2 — 재질을 전체에 적용하기 전 대표 효과 시험

Mortar의 새 재질로 피격·사망을 검증하고, Gauntlet의 기존 몸체 또는 단순 외장 사본에 같은 재질 방식을 적용해 인터럽트까지 검증한다. 중간보스 재질을 대량 제작하기 전에 이 시험을 마친다. Spinner는 Body/Blades를 분리한 상태에서 추가 검수한다.

**통과 기준:** 도색이 피격 후 복원되고 카운터 표시가 지정 몸체 전체에 나온다. 사망 전환의 색·텍스처가 의도대로 보이며, 파티클이 교체된 표면에서 나온다.

### V3 — 완성 모델에서 중단·반복·네트워크 검수

아래 표를 각 종에 해당하는 효과별로 실행한다. 기본 리모델링 계획의 애니메이션 검수와 같은 전투에서 함께 촬영한다.

| 시험 | 합격 조건 |
|---|---|
| 정상 공격·연속 공격 | 원본과 같은 이벤트 횟수·시점. 두 번째 이후에도 효과 크기·위치·색이 같음 |
| 헛스윙/명중·좌/우 | Gauntlet 스파크가 맞은 경우에만 올바른 손에서 재생. 물기·베기는 원래 이벤트 규칙 유지 |
| 예고·회전·발사 | 포탑의 전후좌우 조준, 방향 고정, 발사, 추적 재개에서 섬광·선·탄 방향 일치 |
| 카운터 성공/실패·준비 취소 | 오버레이·성공 섬광·준비 루프의 시작과 종료가 원본 규칙과 같음 |
| 돌진·회전 후 복귀 | Spinner Spin이 카운터 실패 후 정상 종료에서도 회수. Wall 표시가 다음 공격까지 남지 않음 |
| 동작 중 피격·사망·디스폰 | 필요 없는 루프가 남지 않고 정상 종료 파트 후 반환. 사망 재질/파티클과 잔상 표시 정상 |
| 투사체 직격·착탄·수명 만료 | 기존 경로대로 궤적 해제·폭발 1회 재생. 몸체 외형 때문에 탄이 장갑 안에서 출발하지 않음 |
| VFX 풀 반복 사용 | 서로 다른 위치에서 반복해도 이전 입자·궤적·배율이 남지 않음. 종료 파트가 끝난 후 활성 효과 수가 기준으로 복귀 |
| MPPM 호스트+클라이언트 | 양쪽에서 효과가 보이고 중복 재생이 없음. 이동 중 피격도 몸에서 떨어지지 않음. 포탑 예고 중 늦은 접속도 기존 동작과 비교 |
| 실제 레벨 조명·카메라·동시 전투 | 장갑에 효과가 과도하게 가려지지 않고, 투명 잔상·예고선이 읽힘. 같은 상황의 성능 측정에서 정한 예산 충족 |

일반·중간보스는 현재 `MonsterSpawner`의 Instantiate/Spawn과 사망 후 Despawn 경로다. **몬스터 재스폰 검수와 VFX 풀 재사용 검수를 구분한다.** 몬스터 풀링 도입은 포함하지 않는다. [몬스터 생성](C:/MainProject-git/Assets/1.Scripts/Monster/MonsterSpawner.cs:96), [사망 후 디스폰](C:/MainProject-git/Assets/1.Scripts/Monster/MonsterBase.cs:1320).

사망 파티클은 `DissolveDeath`가 몬스터 자식으로 직접 생성하며 EffectPool을 사용하지 않는다. 디스폰까지의 유예 시간이 입자 꼬리를 충분히 남기는지 별도로 검사한다. 풀 회수 검수는 EffectManager에서 재생한 효과에 적용한다.

## 7. 산출물과 적용 판단

- 종별 VFX 연결표와 변경 전 참조 기록. 소켓·이벤트·Entry·재질·공용 의존성을 포함한다.
- 원본/새 외형을 같은 조건에서 찍은 공격·피격·카운터·사망 비교 영상.
- 새 외형에 필요한 VFX 보정 목록: 대상 몬스터, 이유, 변경값, 공유 영향, 검수 결과.
- 호스트·클라이언트 결과와 VFX 풀 반복 결과, 남은 결함 및 원본 복구 목록.

**본·애니메이션 검사와 VFX 검사를 모두 통과한 종만 게임 프리팹에 적용한다.** 적용 전 원본 참조를 보존하고, 결함이 남으면 해당 종의 기존 메쉬·재질을 유지한다.

## 8. 이번 조사에서 확인한 범위와 남은 점

이번에는 코드·프리팹·클립 메타·VFX 에셋의 연결 구조를 읽었다. 새 메쉬의 실제 재생, 호스트/클라이언트 화면, 효과 누수·가림·성능은 아직 시험하지 않았다.

선행 확인 대상은 **사망 재질 색 전달**과 **Gauntlet Wind 중단 시 종료**다. 원본에서 재현되면 기존 결함으로 기록하고, 새 형상 때문에 생긴 회귀와 구분한다. 호환 재질·외형 조정으로 해결되지 않으면 필요한 VFX 자산/종료 처리 보강을 별도 변경 목록으로 정리한다. 이 문제가 남은 상태를 VFX 전체 검증 완료로 표시하지 않는다.

직접 배선 근거: [Chomp](C:/MainProject-git/Assets/2.Prefabs/Monster/ChompBot.prefab:455), [Humanoid](C:/MainProject-git/Assets/2.Prefabs/Monster/HumanoidBot.prefab:390), [Mortar](C:/MainProject-git/Assets/2.Prefabs/Monster/MortarBot.prefab:443), [Peek](C:/MainProject-git/Assets/2.Prefabs/Monster/PeekABot.prefab:116), [Tesla](C:/MainProject-git/Assets/2.Prefabs/Monster/TeslaBot.prefab:261), [Gauntlet](C:/MainProject-git/Assets/2.Prefabs/Monster/GauntletBot.prefab:447), [Spinner](C:/MainProject-git/Assets/2.Prefabs/Monster/SpinnerBot.prefab:738), [Wall](C:/MainProject-git/Assets/2.Prefabs/Monster/WallBot.prefab:200).

`EffectManager.SetPlayRateForTarget` API가 있어도 현재 검색된 호출은 효과 시험 도구뿐이다. 따라서 히트스톱 때 모든 몬스터 VFX도 함께 정지한다고 전제하지 않는다. 해당 정지/감속 상황은 원본과 비교하고, 기존 동기화 문제라면 모델 교체와 구분해 기록한다. [효과 속도 API](C:/MainProject-git/Assets/1.Scripts/Effects/EffectManager.cs:394).

과거 CONTEXT의 미연결 표보다 현재 프리팹을 기준으로 삼았다. 예를 들어 Wall·Spinner의 카운터 성공 섬광은 현재 연결되어 있다. 본 계획은 게임/VFX 코드와 에셋을 변경하지 않은 상태의 제작 지침이다.
