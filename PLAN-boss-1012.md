# PLAN-boss-1012.md — 송전기 아웃라인 · 취약 유지 (23호)

> 상태: **✅ 완료 — 팀장 Play 확인(10-12)** · 2026-10-12 · 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 요청: 팀 피드백 2건(10-12). 선택지는 채팅에서 팀장이 골랐다(아래 각 절 "결정").

## A. 송전기(차징 기둥) — "부숴야 하는 기믹"으로 보이게 노랑 아웃라인

### 결정 (팀장 10-12)
- 방식 = **FlatKit 아웃라인 덧붙이기**. 색 = **노랑**. 표시 = **공격 가능(Live)할 때만**.
- 버린 것: 화면 공간 실루엣(렌더러 피처 수정 범위가 큼) · 재질 교체(음영이 환경과 달라짐).

### 왜 "FlatKit 재질로 바꾸면 끝"이 아닌가
- 송전기(`Env_Mv_bosscharger_upper`, `bossroom.prefab` 안 4개)는 맵 공용 아틀라스 `Generic_01_A.mat`(자체 셰이더 그래프 `Generic_Standard`, **아웃라인 패스 없음**)을 쓴다. 이 재질은 git 프리팹 **107개**가 공유 → 재질에 켜면 맵 전체가 테두리를 갖는다.
- FlatKit 오브젝트 아웃라인은 렌더러 피처(`PC_Renderer` 「Flat Kit Per Object Outline」, LightMode `Outline` 패스만 그림)로 **이미 켜져 있다** → 아웃라인 패스만 가진 재질을 송전기에만 얹으면 된다.

### 구현
1. **셰이더** `Assets/1.Scripts/Rendering/Toon/Shaders/OutlineOnly.shader` — `UsePass "Project/FlatKit Stylized Surface (Skinned Outline)/OUTLINE"` 하나만. 속성 = `_OutlineColor`·`_OutlineWidth`·`_OutlineScale`·`_CameraDistanceImpact`·`_OutlineDepthOffset` + 키워드 `DR_OUTLINE_ON`·`DR_OUTLINE_SMOOTH_NORMALS`·화면/오브젝트 공간.
   다른 패스가 없으므로 일반 렌더·그림자·깊이에는 안 나온다(피처가 그릴 때만 보인다).
2. **재질** `Assets/3.Materials/MA_PylonOutline.mat` — 노랑, 화면 공간, 부드러운 법선 켬. 두께는 Play 에서 팀장 확인 후 조정.
3. **부드러운 법선** — 각진 기계 메시라 모서리에서 아웃라인이 끊긴다 → `FlatKitSmoothNormalBaker.Targets` 에 `Env_Mv_bosscharger_upper.fbx` 추가 + `GetVersion` 2→3(SVN FBX·.meta 는 안 고친다, 임포트 산출물만).
4. **`BossChargingPylon`**
   - `NetworkVariable<bool> _outlined`(서버 쓰기) — Live 진입 시 true, Lowering·Hidden·파괴 시 false. 지금 상태는 서버 전용이라 클라가 모른다.
   - 모든 피어: 스폰 때 송전기의 `MeshRenderer` 마다 **자식 렌더러**(같은 메시 + 아웃라인 재질 1장)를 만들고 `enabled` 만 토글. 자식에 `NoHitFlash` — 피격 플래시가 아웃라인 색을 덮지 않게.
     원래 렌더러의 재질 배열은 **건드리지 않는다**(HitFlash 가 재질을 캐시하므로 배열을 바꾸면 꼬인다).
   - `[SerializeField] Material outlineMaterial` — `bossroom.prefab` 의 4개 인스턴스에 지정(텍스트 에셋 1개, Unity 켠 채 가능). 비어 있으면 1회 경고 후 아웃라인 없이 동작.

### 리스크
- `bossroom.prefab` 은 아트 팀(존 레이아웃)도 고치는 파일 — 필드 4줄 추가라 충돌 작음. development 41커밋 안 받은 상태라 머지 때 확인.
- 송전기 메시에 탄젠트가 없으면 부드러운 법선 복원이 안 된다 → 그때는 원래 법선으로 돌아간다(셰이더가 처리). Play 에서 모서리 확인.

## B. 간파 넉백 후 취약 유지 — 벽 충돌일 때만 풀린다

### 결정 (팀장 10-12)
- 취약 중 간파 스킬 넉백이 **벽에 안 부딪히고 끝나면**(거리 소진·플레이어에 막힘) **취약 유지** — 남은 시간대로 흐른다.
- **그로기도 유지**(넉백 종료로 해제하지 않는다 — 원래 1.5초 타이머대로).
- 벽 충돌은 지금과 같다: 게이지 −20 → 취약 종료(0 이면 제압) → 반동 후 그로기 해제.
- ⚠️ **뒤집음**: 09-29 팀장 결정 "넉백이 끝나면 그로기·취약을 함께 끝낸다"(`FinishVulnerableKnockback`)를 벽 충돌 경로로만 좁힌다.

### 구현
- `TwentyThreeBoss.FinishVulnerableKnockback` — 반동(=벽 충돌) 종료일 때만 지금 동작(취약 종료 + 그로기 해제). 그 외에는 넉백 플래그만 내리고 끝.
- 같은 취약 안에서 다시 밀려 벽에 닿으면 벽 판정 그대로(`_environmentConsumed` 는 벽에서만 세워진다 — 변경 없음).

## 검증
- 컴파일 `error CS` 0 · 전투 EditMode(기존 실패 2 외 0).
- Play(팀장): ① 차징 때 송전기가 다 올라온 뒤에만 노랑 테두리, 부서지거나 내려가면 꺼짐, 클라에서도 동일(MPPM) · 모서리 끊김 여부 · 맵 다른 오브젝트에 테두리 없음.
  ② 취약 중 간파 넉백 → 벽 아님: 파란 틴트 유지·다시 밀 수 있음·그로기 1.5초 그대로 / 벽: 게이지 −20·취약 종료.

## 진행 (10-12) — 구현 ✅ · 컴파일 error CS 0 · 전투 EditMode 108/2(기존 실패 2) · ✅ 팀장 Play 확인
- A: `OutlineOnly.shader`(UsePass, 셰이더 오류 0) · `MA_PylonOutline.mat`(노랑, 화면 공간, 두께 **2.0** = 캐릭터 0.4 의 5배, 거리 보정 0 = 캐릭터와 동일) ·
  법선 굽기 `Env_Mv_bosscharger_upper.fbx` 완료(정점 4322 · 위치 그룹 1250) · `BossChargingPylon` NetworkVariable + 자식 렌더러 · `bossroom.prefab` 4개 지정(+4줄, Unity 재직렬화 없음 cmp 확인).
- B: `FinishVulnerableKnockback` — 반동(벽) 종료일 때만 취약 종료 + 그로기 해제, 아니면 로그만.
