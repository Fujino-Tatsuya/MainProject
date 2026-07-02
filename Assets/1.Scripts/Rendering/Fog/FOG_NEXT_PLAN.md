# Fog 다음 작업 계획 — 이터널리턴 FoW 마감 + 어비스 물안개 (다음 세션 이어받기)

> 대상 경로: `Assets/1.Scripts/Rendering/Fog/`
> 관련 커밋: `cac5e7e`(LoS 차폐 복구 + 경계 각도블러/지터)

---

## 0. 현재 상태 (이미 구현됨)
- 전역 포그: 거리/높이/밀도모드/볼륨/노이즈/페인트마스크 (`FogCore.hlsl` `Fog_Evaluate`)
- 층 디밍 + 시야범위 디밍(FOW `viewRange`) + **LoS 차폐**(`_LosBrightness`/`_LosSaturation` — 시야 밖 은은하게 어둡게+탈채도)
- **LoS 경계 자연스럽게**: 라디얼맵 각도 블러(`losAngleBlur`) + 셰이더 노이즈 각도 지터(`losAngleJitter`)
- 파이프라인: `FogManager`(글로벌 push + `_LosTex` 라디얼맵 CPU 생성) → `FullScreenFog.shader`(풀스크린 합성) → `FogRendererFeature`(URP 패스)

---

## 1. Task 1 — 이터널리턴 FoW 마감 (대부분 구현됨, 튜닝 위주)
현재 LoS/디밍 시스템으로 거의 커버됨. "선명한 2D 탑다운 시야"로 다듬기:

**인스펙터 세팅 (FogManager + FogProfile)**
- `losEnabled` ON, `dimEnabled` ON
- `viewRange` 15~25 (현재 0=끔 → 켜야 플레이어 반경 시야), `viewFade` 3~5
- `losEdgeFade` 5~8 (거리방향 소프트섀도우)
- `losAngleBlur` 6~10 / `losAngleJitter` 0.02~0.04 (부채꼴 경계 완화)
- 이터널리턴은 시야 밖이 거의 어둠 → `losBrightness` 0.1~0.2, `losSaturation` 0.15~0.25 (더 어둡게, 취향)

**개선 여지 (선택)**
- 시야 경계에 약한 비네팅/컬러 그레이딩
- LoS 라디얼맵 갱신 빈도(현재 매 LateUpdate) — 성능 이슈 시 N프레임마다

---

## 2. Task 2 — 어비스 물: 불투명 어두운 물 Plane [신규 구현, 방식 확정]

> **결정(2026-07, 팀장 확정):** 포그 기반 어비스 물안개 ❌ → **불투명 물 Plane 1장** 방식.
> - 물은 **불투명·어두움** → 물 바닥이 안 보이고 카메라가 물 안쪽을 못 봄(목표 자동 달성)
> - **흐르는 것만** 보이면 됨 (스크롤 노이즈 물결)
> - **실시간 반사 없음** (Planar Reflection/실시간 Probe 금지 — 프레임 예산)
> - 어두운 물이라 비침(스크린 왜곡/굴절)도 없음
> - 바닥 메시를 따로 그리지 않음 (불투명이라 불필요)

### 구현 — `WaterDark.shader` (신규, Unlit 계열 1패스)
경로 제안: `Assets/1.Scripts/Rendering/Water/WaterDark.shader` (+ 머티리얼 `Assets/3.Materials/Water/`)
- **Opaque, ZWrite On, 라이팅 없음(Unlit)** — 쿼드 1장, 사실상 0 비용
- 컬러 = 어두운 심연색 `_DeepColor`(예 0.02,0.04,0.08) 기반
- **물결**: 절차 노이즈 2겹(서로 다른 scale·스크롤 속도, `FogCore.hlsl`의 `Fog_ValueNoise` 패턴 재사용) → 밝은 물결색 `_FlowColor`를 얇게 lerp
- **fake 깊이감**: 가장자리(벽 접점) 살짝 밝고 중앙으로 갈수록 어둡게 — UV 또는 월드거리 그라데이션. 바닥 지오메트리 없이 깊어 보이게
- (선택) depth 기반 엣지 폼(벽 접점 하얀 거품) — Fog 패스가 이미 depth 사용 중이라 추가 비용 낮음. 1차엔 생략 가능
- 프로퍼티: `_DeepColor` `_FlowColor` `_FlowSpeed1/2` `_FlowScale1/2` `_FlowStrength` `_EdgeBrighten`

### 씬 배치
- 구멍 지형 아래 **Y<바닥면**에 물 Plane(쿼드) 배치, 맵 구멍 전체를 덮게
- 물 Plane엔 **콜라이더 없음** (캐릭터는 통과해 떨어짐)
- 낙하 캐릭터는 불투명 Plane이 시각적으로 자연 차폐 (별도 렌더러 토글 불필요)

### 낙하/복귀 연동 (기존 시스템 연결만)
- 물 Plane 아래에 **리스폰 트리거 볼륨**(BoxCollider isTrigger) 1개 → 기존 "근처 복귀" 시스템 호출
- 카메라는 어차피 따라가지 않음(기존 동작 유지) → 물 안쪽 노출 없음

### 성능 체크리스트
- ✅ 쿼드 1장 + Unlit 1패스 (드로우콜 +1)
- ✅ 텍스처 0장(절차 노이즈) 또는 노이즈 텍스처 1장
- ❌ 금지: 실시간 반사, 투명 블렌딩(오버드로우), 스크린 굴절(grab pass), 테셀레이션/버텍스 웨이브

---

## 3. 작업 순서 (다음 세션 — 방식 확정됨, grill 불필요)
1. `WaterDark.shader` + 머티리얼 작성 (불투명 어두운 물, 노이즈 2겹 스크롤, fake 깊이 그라데이션)
2. 테스트 씬 구멍 아래 물 Plane 배치 → 탑다운/근접 육안 검증 (흐름 보임 + 물 안쪽 안 보임)
3. 리스폰 트리거 볼륨 연결(기존 복귀 시스템)
4. losEnabled/viewRange 켜고 이터널리턴 FoW 튜닝 (§1 권장값)
5. 검증(Unity 스샷, 프레임 확인) → feature/map 커밋

## 4. 참고 — 파일 역할
| 파일 | 역할 |
|---|---|
| `FogManager.cs` | 글로벌 프로퍼티 push, `_LosTex` 라디얼맵 CPU 생성(각도 블러 포함) |
| `FogProfile.cs` | ScriptableObject 설정값 |
| `FogCore.hlsl` | 포그/디밍/LoS 수학 |
| `FullScreenFog.shader` | 풀스크린 합성 패스 |
| `FogRendererFeature.cs` | URP 렌더 패스 등록 |
| (예정) `../Water/WaterDark.shader` | 불투명 어두운 물 Plane (Task 2, 신규) |
