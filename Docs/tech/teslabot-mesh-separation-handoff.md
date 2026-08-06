# TeslaBot — 메쉬 분리된 채 전투 / 작업지시서

> 작성 2026-07-31 (경석 세션, 조사만 수행 · 수정 안 함). 담당자 배정용.
> 증상: **일반 몬스터 TeslaBot 이 모델 메쉬가 분리된 상태로 전투한다.**

## 1. 결론 요약

**정적 조사로는 근본 원인을 확정하지 못했다.** 프리팹·리그 임포트·컨트롤러 배선은 모두 정상이었고,
남은 유력 후보는 **애니메이션 클립이 실제로 재생되지 않는 것**이다. 확정하려면 에디터에서
런타임 관찰이 필요하다(§4 절차).

가장 먼저 볼 것 → **§3-A. 애니 fbx 4개가 동일 파일이다.**

## 2. 배제된 것 (다시 조사하지 말 것)

| 항목 | 확인 내용 | 판정 |
|---|---|---|
| 프리팹 본 트랜스폼 오버라이드 | `2.Prefabs/Monster/TeslaBot.prefab` 의 중첩 프리팹 수정은 **루트 1개뿐**(`m_Name`+위치/회전). 자식 본을 건드린 오버라이드 0건 | 정상 |
| 리그 임포트 불일치 | 4개 클립 전부 `animationType: 2`(Generic) · `avatarSetup: 2` · `rootMotionBoneName: Root` · `optimizeGameObjects: 0` 로 **동일** | 정상 |
| 다른 봇 클립 오염 | 컨트롤러가 참조하는 클립 GUID 3개 전부 TeslaBot 폴더의 fbx 로 해석됨 (교차 참조 없음) | 정상 |
| Animator 위치 | 게임 프리팹이 아니라 아트 프리팹 `P_TeslaBot.prefab` 에 있음 — MortarBot 과 동일 구조 | 정상 |

참고: 프리팹 구조가 MortarBot 과 같은 패턴이므로 "TeslaBot 프리팹만 이상하게 조립됐다"는 방향은 아니다.

## 3. 남은 가설 (우선순위 순)

### A. 애니 fbx 4개가 **바이트 단위로 같은 파일** ← 최우선

```
Animations/TeslaBot/  (전부 376,844 bytes, md5 99facd062ce38f05aba277eab036dbee)
  A_Alert.fbx   A_Idle.fbx   A_Reveal.fbx   A_Shoot.fbx
```

4개가 완전히 동일한 파일인데 `.meta` 는 서로 다른 take 를 지정한다:

| 파일 | 선언된 takeName |
|---|---|
| A_Idle.fbx | `Armature\|Idle` |
| A_Alert.fbx | `Armature\|Alert` |
| A_Reveal.fbx | `Armature\|Reveal` |
| A_Shoot.fbx | `Armature\|Shoot` **(클립 2개 선언)** |

두 가지 해석이 가능하고 **어느 쪽인지 확인이 필요하다**:
- (정상) 원본 fbx 하나에 take 4개가 다 들어있고, 파일을 복제해 각각 다른 take 를 고른 것 → 문제없음
- (버그) 파일에 take 가 하나뿐인데 나머지 3개가 없는 take 를 가리킴 → 해당 클립이 비어서
  본이 안 움직이고 **일부만 애니메이션되어 메쉬가 분리돼 보인다**

증상이 "분리된 채"인 점, 그리고 전투 상태(Charge/Shoot)에서 난다는 점이 후자와 맞아떨어진다.
대조군으로 MortarBot 은 파일이 서로 다르다(단, `A_MortarBot_AttackLoop` 와 `A_MortarBot_AttackShoot`
는 md5 가 같다 — 같은 문제의 축소판일 수 있으니 함께 볼 것).

### B. 컨트롤러 상태-클립 배선이 뒤바뀜

`Controller_TeslaBot` 상태는 **Idle / Charge / Shoot** 3개뿐인데:

| 상태 | 실제로 물고 있는 클립 소스 |
|---|---|
| Idle | `A_Idle.fbx` ✔ |
| Charge | **`A_Shoot.fbx`** |
| Shoot | **`A_Alert.fbx`** |

Charge↔Shoot 이 뒤바뀐 것처럼 보인다. 의도된 재매핑일 수도 있으나 확인 필요.
(이것만으로는 메쉬 분리를 설명하지 못한다 — 잘못된 동작이 나올 뿐이다.)

### C. 미사용/누락 클립

- `A_Reveal.fbx` 는 **어디서도 참조되지 않는다**(컨트롤러·프리팹·코드 전부 0건). 센트리 봇류는
  보통 접힌 상태에서 Reveal 로 전개된다 — 전개 전 포즈가 "분리된 것처럼" 보일 가능성이 있다.
- 컨트롤러에 `RunBlend` 파라미터가 있으나 **Run 상태도, Run 클립도 없다**(MortarBot 은 `A_MortarBot_Run.fbx` 보유).
  이동 중 포즈가 정의되지 않는다.

## 4. 확인 절차 (담당자용)

1. Unity 에서 `A_Idle / A_Alert / A_Reveal / A_Shoot` fbx 를 각각 선택 → Inspector **Animation 탭** →
   take 목록에 선언된 takeName 이 실제로 존재하는지, 클립 프리뷰에서 본이 움직이는지 확인.
   **하나라도 프리뷰가 정지 상태면 가설 A 확정.**
2. `P_TeslaBot` 을 씬에 놓고 `Controller_TeslaBot` 상태를 하나씩 강제 재생 → 어느 상태에서 메쉬가
   분리되는지 특정.
3. 분리되는 클립을 찾으면 → 원본 fbx 를 take 별로 다시 익스포트하거나, take 가 다 들어있는 단일
   fbx 에서 클립을 나눠 임포트하도록 정리.
4. 겸사겸사 B(Charge/Shoot 배선)와 C(Reveal 미사용, Run 없음)도 판단해서 정리.

## 5. 주의

- `50.Art` 는 **SVN** 이다. fbx/meta 수정 시 git 이 아니라 SVN 으로 커밋할 것.
- `A_Alert.fbx.meta` / `A_Shoot.fbx.meta` 는 2026-07-20 에 수정된 흔적이 있다(다른 둘은 2025-06-27 원본).
  그때 무엇을 바꿨는지 SVN 로그를 먼저 볼 것 — 회귀라면 그 시점이 유력하다.
