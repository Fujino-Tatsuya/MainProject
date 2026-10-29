# handoff — 트렌치커버 벤트 연기 VFX (2026-10-29 · 민경)

> 계획: [PLAN.md](../../PLAN.md) 「트렌치커버 벤트 연기」 · 브랜치 `feature/VFX`. 순수 배경 연출 — 판정·충돌·네트워크 무관.

## 무엇이 바뀌었나
- `2.Prefabs/Environment/Architecture/Floors/floor_metal_trenchcover.prefab`
  - 커밋본의 자식 `FX_Smoke_2`(바닥 연기)는 **뺐다**(민경 결정 — 새 연기로 대체).
  - 구멍 소켓 `SmokeHole_1~9`(메쉬 실측 중심: x −0.98/0/0.98 · z 0.987/0.007/−0.973 · y −0.15) + 각 소켓에 `FX_Vent_Smoke_Cel`.
  - 루트에 `VentSmokeEmitter` — `holes` 9칸 연결.
- 배치된 곳: `ZoneM_typeA·B·C`(각 2) · `Zone_typeQuest02`(4) — 존 프리팹은 수정하지 않았다(중첩 프리팹으로 자동 반영).
- `2.Prefabs/Environment/Layouts/Zones/bossroom.prefab`(12): 원래 **FBX 원본 모델을 직접** 놓아 연기가 안 나왔다 → 12개를 벤트 프리팹으로 교체(`PrefabUtility.ReplacePrefabAssetOfPrefabInstance`, 위치·회전·재질·그림자·레이어·스태틱 오버라이드 보존). bossroom 을 쓰는 씬 4개에 이 인스턴스를 겨눈 오버라이드 없음 확인.
- 새로 트렌치커버를 놓을 땐 FBX 말고 **이 프리팹**을 끌어다 놓을 것.

## 동작 (`1.Scripts/Effects/VentSmokeEmitter.cs`)
- 구멍마다 독립: 분출 1.25~2.05초(예고 0.45 포함) → 휴지 2~6초 반복. 벤트당 동시 분출 최대 3. 켜질 때 시작 시점을 무작위로 잡아 벤트끼리 엇박.
- 분출 = 루트 `Play` → 먼저 예고 `Leak`(구멍에서 0.45초 가늘게 새어 나옴) → 0.45초 뒤 본 분출(압력 퍼프·머리 원샷 + 기둥·뭉게 반복 — 입자 startDelay). `burstDuration`(1.25~2.05)은 예고 포함 길이, 정지 = `Stop(StopEmitting)` — 남은 연기는 위로 흘러 사라진다.
- 벤트 렌더러가 어느 카메라에도 안 보이면 분출을 멈추고 시간도 세지 않는다(다시 보이면 새로 엇박).
- 클라마다 로컬 랜덤(동기화 없음). `BurstStarted(int hole, Vector3 pos)` 이벤트(예고 시작 시점 — 본 분출은 0.45초 뒤) — 나중에 사운드를 붙일 자리(지금은 구독자 없음).
- 수치(시간 범위·동시 수)는 프리팹 인스펙터에서 조정.

## 연기 모양 (`50.Art/VFX/Common/Env/FX_Vent_Smoke_Cel.prefab`)
- Leak(분출 전 예고) · Puff(분출 순간 낮고 빠르게 옆으로 터지는 압력 고리) · Blast(분출 순간 한 번, 기둥보다 굵고 빠르게 먼저 치솟는 머리 덩어리 10개) · Jet(굵고 진한 기둥, 높이 약 4.2m — 처음 판의 2배). 기둥·머리·퍼프·예고의 밑동은 살짝 어둡고 붉은 뜨거운 증기색(빌더 `SteamHot`)이었다가 오르며 밝은 증기 → 탁한 회색 · Billow(기둥 따라 오르며 커지는 뭉게 — 꼭대기에서 뭉텅이로 남았다가 작아지며 사라짐).
- 재질 `_Materials/VentSmoke/M_VentSmoke_Cel`(Hovl `SmokeFree1.mat` 복제 — URP Particles/Unlit · 알파 블렌드 · 소프트 파티클) + 텍스처 `Textures/VentSmoke/T_VentSmoke_Cel`(덩이 1~4개 만화풍 연기 8종, 4×2 — 입자마다 칸 무작위).
- 원본: MyProject `Tools/VFX/Vent Smoke (트렌치커버 벤트 연기) 빌드` · 텍스처 생성기 `My project/DevTools/gen_vent_smoke_cel.py`.

## 부담
- 활성 구멍 하나 ≈ 입자 최대 ~170(머리 12 + 기둥 120 + 뭉게 30 + 퍼프 8 상한). 벤트당 동시 3 · 화면 밖 정지.

## 확인할 것 (Play)
- 존·보스방 진입 → 벤트 9구멍이 서로 다른 박자로 분출·정지, 콘솔 오류 없음.
- 카메라를 벤트에서 멀리 → 분출 멈춤, 돌아오면 다시 시작.
- 연기가 전투 가독성(몬스터·장판)을 가리지 않는지.

## SVN
- add: `50.Art/VFX/Common/Env/FX_Vent_Smoke_Cel.prefab(.meta)` · `50.Art/VFX/_Materials/VentSmoke/`(+ 폴더 .meta) · `50.Art/VFX/Textures/VentSmoke/`(+ 폴더 .meta)
- git: `floor_metal_trenchcover.prefab` · `bossroom.prefab` · `1.Scripts/Effects/VentSmokeEmitter.cs(.meta)`
