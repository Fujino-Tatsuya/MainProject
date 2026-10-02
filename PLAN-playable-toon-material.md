# PLAN — 플레이어블 캐릭터 공통 툰 머티리얼

> 2026-10-02 · 은희(Claude) · 브랜치 `feature/PlayableToonMaterial` · 상태: **✅ 구현 완료 — Play 눈확인 대기**
> 답변: Q1 부모 Base Map 비움 · Q2 거너 = `LaserGun_BaseColor_Toon.png` 맞음(총도 같은 텍스처) · Q3 검·방패 틴트 안 덮음 → **흰색 수용**

## 1. 목표
플레이어블 캐릭터(몸체·무기)는 **공통 부모 머티리얼 하나**를 공유한다. 캐릭터·파츠마다 **Base Map 텍스처만** 다르다.
셰이더 수치는 부모에서 한 번 고치면 전원에게 반영된다.

## 2. 확정 결정 (grill 2026-10-02)
| 항목 | 결정 |
|---|---|
| 교체 대상 | **Base Map 텍스처**(`_BaseMap`). `_BaseColor` 틴트는 흰색 유지 |
| 방식 | **Unity Material Variant**(`m_Parent`) — 코드 없음, MPB 안 씀 |
| 공통 부모 | `Paladin_Toon.mat` → **`PlayableCharacter_Toon.mat`** 로 이름 변경(guid `49b3ffd5…` 유지) |
| 가붕이 몸체 | 새 Variant `Paladin_Toon.mat`(Base Map = `paladin_basecolor.jpg`) |
| 무기 | Sword/Shield/LaserGun 도 **같은 부모의 Variant**. 무기 전용 값(FaceLift off·외곽선·밝기·채도·틴트)은 **버리고 부모 값으로 통일** |
| 거너 몸체 | `Gunner_Toon.mat` Variant 를 만들어 `Gunner_Armature` 에 연결 |

## 3. 작업 단계
1. **부모 이름 변경** — 에디터 안에서(MCP `move_asset`) `Paladin_Toon.mat` → `PlayableCharacter_Toon.mat`. guid 유지 확인.
   부모의 Base Map 은 비우거나(흰) 가붕이 텍스처를 남긴다 → §5 Q1.
2. **가붕이 몸체 Variant** `Paladin_Toon.mat` 생성, `Paladin_Armature` 의 슬롯 0 을 Variant 로 교체.
3. **무기 Variant** — `Paladin_Sword_Toon`·`Paladin_Shield_Toon` 을 Variant 로 전환(guid 유지, `m_Parent` 설정, 무기 전용 값 제거).
   LaserGun 은 `LaserGun_Toon.mat` Variant 신규.
4. **거너 Variant** `Gunner_Toon.mat` — Base Map = `50.Art/Char/gunner/LaserGun_BaseColor_Toon.png`(→ §5 Q2), `Gunner_Armature` 렌더러 슬롯 연결.
5. 문서: `Docs/tech/player-prefabs.md` 에 "캐릭터 머티리얼 = `PlayableCharacter_Toon` + 캐릭터별 Variant(Base Map만 덮음)" 한 줄.

## 4. 리스크 / 엣지케이스
- 🔴 **검·방패는 지금 텍스처 없이 틴트로 색을 낸다.** Base Map 만 덮으면 흰색 + 흰 틴트 = **새하얗게 나온다.**
  → 무기 텍스처가 없으면 임시로 `_BaseColor` 만 추가로 덮는다(§5 Q3).
- 무기 FaceLift 가 켜져서 무기 셰이딩이 얼굴처럼 평평해질 수 있음 — 사용자 결정(값 통일)으로 수용, Play 눈확인.
- `Paladin_VFX`·Legacy `Paladin` 도 부모 guid 를 참조 → 이름만 바뀌고 계속 부모를 쓴다(보관용이라 수용).
- 이름 변경은 **에디터 안에서**만(밖에서 하면 CLAUDE.md §6 — Unity 종료 필요). 변경 후 `.meta` guid 확인.
- `Gunner_Armature` 소스가 `laser_gun.fbx`(guid `f2b31415…`) — 몸체·총이 한 메시/서브메시인지 확인 후 슬롯 배정.
- 셰이더 키워드(`_FACELIFT_ON` 등)는 Variant 가 상속 — 덮은 속성 외 키워드가 갈라지지 않는지 확인.

## 5. 남은 질문 (승인 시 답 필요)
- **Q1** 부모 Base Map: 비움(흰 텍스처) / 가붕이 텍스처 유지?  (제안: **비움** — 부모가 특정 캐릭터에 묶이지 않게)
- **Q2** 거너 Base Map = `LaserGun_BaseColor_Toon.png` 맞나? (현재 어디에도 안 쓰이는 유일한 거너 텍스처)
- **Q3** 검·방패 텍스처가 없으니 임시로 `_BaseColor` 틴트를 덮어 지금 색감을 유지할까?

## 6. 검증
- 컴파일 / 콘솔 에러 0, `Assets/Refresh` 후 `.mat`·`.meta` guid 대조.
- 가붕이·거너 프리팹 미리보기에서 텍스처·외곽선 정상.
- 부모의 `_ShadeThreshold` 를 임시로 바꿔 모든 Variant 에 반영되는지 확인 후 되돌림.
- 사용자 Play 눈확인(가붕이·거너, MPPM 불필요 — 렌더링만).
