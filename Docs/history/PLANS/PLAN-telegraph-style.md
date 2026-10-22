# PLAN-telegraph-style.md — 공격 예고 색·마스크를 아트가 직접 조절 (v2 · Codex 회의 반영)

> 상태: **✅ 완료(팀장 확인 10-13)** · 2026-10-13 · 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 요청(팀장 10-13): 예고 데칼이 너무 쨍하다 → 색을 인스펙터에서 바로, 마스크 이미지로 패턴·이미지 커스터마이징. 인터럽트 포함.
> **규격 문서**를 아트에 전달 → 아트가 그 기준으로 바로 수정.
> v1(CPU 굽기)은 Codex 교차검증(10-13)으로 폐기 — 아래 「v1 에서 바뀐 것」.

## 0. 현재 상태 (10-13 조사 + Codex 정정)
| 예고 | 사용처(실제) | 그리는 방식 | 색 |
|---|---|---|---|
| 공격 예고 `BossAttackConeTelegraph` — 부채꼴·경로 띠·합집합(윤곽선 + 차오르는 채움) | 23호 · 건틀렛 펀치 · 스피너 채찍/돌진 (**일반 몹은 안 씀** — 재질 칸 비어 있음) | **데칼**. 모양 텍스처를 CPU 로 생성(색까지 구워 넣음) | 코드 상수 — 공격 빨강 / 인터럽트 파랑 |
| 웰즈 드론 착탄 원 | `WellsDroneAttack` | **바닥 판**(`BossPatternVisuals`, Sprites/Default) | `WellsDroneDataSO.circleOuter/FillColor` |
| 23호 전기 장판 사각 | `BossElectricFloor` | **바닥 판**(같음) | `BossElectricFloorDataSO.warnOuter/FillColor` |
| 건틀렛 스매시 바닥 원 | `GauntletBot` | 아트 VFX 스프라이트 | `InterruptRgb` 덮어씀(재생당 1회) |
| 원형 장판 `AoeTelegraph` | **지금 쓰는 곳 없음** — 점프 예고는 이펙트로 이전, 차징 오라 프리팹 칸 비어 있음(No23·No23_Solo) | 데칼 | 프리팹 인스펙터(현재 셰이더에 색 입력이 없어 저장된 빨강은 실제로 안 먹음) |

- 공격 예고 재질 `MA_BossMarkerDecal` = 저장소의 **`SG_DecalFloorOnly`** 셰이더 그래프(입력 `Base_Map`·`Normal_Map`, Angle Fade). ⚠️ v1 의 "URP 기본 셰이더"는 틀렸다. `_BaseColor` 가 있는 `SG_ColoredDecal` 도 이미 있다.
- 방향 표식(`BossDirectionIndicator`)도 같은 재질을 쓴다 — **표식은 이번 범위 밖, 그대로 둔다.**

## 1. 결정 (팀장 10-13)
| # | 결정 |
|---|---|
| T1 | 범위 = 공격 예고 + 인터럽트 · 드론 원 · 전기 장판 · 스매시 원(색만) · 원형 장판(지원만 — 쓰는 곳 없음) |
| T2 | 저장 = **몬스터별 SO** `TelegraphStyleSO`. 비우면 공용 기본 에셋 |
| T3 | 마스크 입히기 = 층마다 **늘이기 / 반복(m)** 선택 |
| T4 | 윤곽선·채움 **따로**(각각 색·마스크, 윤곽선은 두께도) |
| T5 | 처리 = **GPU**(Codex 권장). 모양은 지금처럼 코드가 그리되 **흰색**으로, 색·마스크는 셰이더 |
| T6 | Play 중 SO 수정값은 **에셋에 그대로 남긴다**(아트가 보며 맞춘 값을 그대로 씀) |
| T7 | 반복 패턴은 **예고를 따라간다**(보스가 돌면 패턴도 돈다 — 줄무늬가 공격 방향을 가리킴) |

## 2. 구현
### 2-1. 셰이더 — 계산은 HLSL 하나에 두 경로가 공유
- `Assets/1.Scripts/Rendering/Telegraph/TelegraphMask.hlsl` — 함수 하나: `모양(텍스처 알파) × 색 × 마스크`.
  마스크 좌표 = **예고 기준점의 로컬 좌표**(월드 위치 − 원점을 예고의 좌우/전방 축에 투영)
  → 늘이기 = 전체 크기로 나눠 0~1, 반복 = 반복 크기(m)로 나눔. 세기 0~1 로 마스크를 섞는다.
  🔴 마스크 좌표를 **데칼 UV 로 잡지 않는다** — 채움은 프로젝터 크기를 키우며 자라서 UV 기준이면 패턴이 같이 커지며 미끄러진다.
  예고 기준점(보스에 붙어 따라감)의 로컬 좌표면 프로젝터가 커져도 패턴은 제자리다(T7).
- **데칼**: `SG_TelegraphDecal`(새 그래프) = `SG_DecalFloorOnly` 복제 + 속성(`_BaseColor`·`_MaskTex`·마스크 파라미터) + 커스텀 함수 노드 1개(위 HLSL) + 월드 위치 노드. **Angle Fade 등 기존 노드 보존.**
  새 재질 `MA_TelegraphDecal` → 공격 예고만 이 재질을 쓴다. 표식은 기존 재질 그대로(영향 없음).
- **바닥 판**: `Monster/TelegraphQuad` HLSL 셰이더 — `Sprites/Default` 와 같은 블렌드(프리멀티 알파·ZWrite Off·Cull Off)·같은 속성명(`_MainTex`·`_Color`) + 마스크 속성. `BossPatternVisuals` 재질만 교체하면 기존 호출(`Paint`)이 그대로 동작(크로스헤어 포함, 마스크 기본 = 없음).

### 2-2. 스타일 SO
- `TelegraphStyleSO` — 스타일 4벌: `attack`·`interrupt`·`circle`(드론 원·원형 장판)·`floorTile`(전기 장판).
  각 벌 = `outline`{색, 두께 m, 마스크} + `fill`{색, 마스크}. 마스크 = 텍스처·방식(늘이기/반복)·반복 크기 m·세기·회전(도).
- `MonsterDataSO.telegraphStyle` 칸. 기본 에셋 `Assets/2.Prefabs/Monster/Data/TelegraphStyle_Default.asset`(몬스터 데이터 옆 — 10-13 경로 정정).
- 🔴 **빈 칸 해석은 빌드에 포함돼야 한다**(경로만으로는 로드 안 됨): 기본 에셋을 각 몬스터 데이터에 **실제로 연결**하는 저작 메뉴를 돌리고, 그래도 비면 **코드 기본값**(= 지금 상수)으로 그린다(null 안전).
- 드론·전기 장판의 기존 색 칸(`WellsDroneDataSO`·`BossElectricFloorDataSO`)은 기본 에셋으로 값을 옮긴 뒤 **[Obsolete] 표시만**(데이터 보존, 읽지 않음) — 두 곳에서 색을 고치는 상태를 만들지 않는다.

### 2-3. 연결 (🔴 모든 피어)
- 예고는 RPC 로 각 피어가 그린다 — 스타일은 **각 피어의 로컬 데이터 SO** 에서 읽는다(지금 모양 값과 같은 규약). RPC 에 스타일을 싣지 않는다.
- 주입 지점은 **서버 전용 초기화가 아니라** 모든 피어가 지나는 곳(그리는 순간 `data.telegraphStyle` 조회). 23호는 `_boss` 설정 직후 서버 분기 앞.
- 공격 예고: 소유 몬스터가 `Style` + 팔레트(공격/인터럽트)를 넘김. 건틀렛 스매시 원 = 자기 스타일의 `interrupt.fill` 색(정적 `InterruptRgb` 제거).
- ⚠️ 한 피어에서만 한 Play 튜닝은 그 피어 화면에만 보인다(로컬 에셋 수정이므로) — MPPM 확인 때 유의. 빌드는 같은 에셋이라 일치.

### 2-4. 실시간 반영 (T6)
- SO `OnValidate` 는 **버전 번호만 올린다**(텍스처·재질 작업 금지 — 로딩 스레드에서도 불릴 수 있음).
- 켜진 예고가 `LateUpdate` 에서 버전 변화를 보고 **재질 값만** 다시 넣는다. `Show` 를 다시 부르지 않는다 — 부르면 채움 시간이 0 으로 돌아간다.
- 스매시 원 "재생당 1회 칠하기"·드론/전기 장판 `Paint` 도 버전 변화 시 다시 칠한다.
- 빌드에는 `OnValidate` 가 없다 — 초기 적용은 `Show`/생성 경로에서 독립적으로.

### 2-5. 화면 동일성 (기본 에셋 = 지금 화면)
- **알파는 한 번만**: 모양 텍스처는 흰색 + 모양 알파만, 색 알파는 셰이더에서 한 번 곱한다(`fadeFactor` 1 유지). 지금은 색 알파가 텍스처에 구워져 있다.
- **윤곽선 두께**: 공격 예고는 지금도 월드 0.25m 를 모양별로 환산(부채꼴 ÷반경, 띠 ÷폭/÷길이, 합집합 m) — 환산식은 그대로 두고 0.25 를 SO 값으로.
  드론·전기 장판은 판 텍스처의 테두리 비율(`BuildDisc 0.08`·`BuildSquare 0.1`)이 지금 기준 — SO 두께를 m 로 받으면 반경별로 화면이 달라지므로 **이 둘은 "테두리 비율(0~0.5)"로 받는다**(규격 문서에 단위 명시).
- **색 공간**: 지금 텍스처는 sRGB 로 굽힌다 → 셰이더 `_BaseColor`(sRGB 입력 → 선형)와 같은 결과. 8비트 양자화 차이(≤1/255)만 허용.
- 원형 장판은 쓰는 곳이 없어 화면 동일성 검증 대상이 아니다. 지원은 하되 기존 프리팹 값(저장된 빨강)이 새 셰이더에서 처음 먹게 되는 점을 규격 문서에 적는다.

### 2-6. 캐시·자원 (Codex 지적 — 기존 누수 포함)
- 모양 캐시 키에 **실제 치수·두께**를 넣는다(지금은 부채꼴 = 각도만, 띠 = 비율만 → 크기가 바뀌어도 이전 두께가 남는 기존 버그). 스타일은 셰이더 값이라 캐시와 무관해진다.
- 합집합·잡기의 단계 굽기: 매 단계 `new Texture2D`·`new Color32[]` → **버퍼·텍스처 재사용**(같은 크기면 덮어쓰기).
- `OnDestroy` 에 합집합 텍스처·재질 정리 추가(지금 누락). `AoeTelegraph` 복제 재질 정리·반복 복제 방지.
- 단계는 0~24, **25 상태**(v1 의 "24번" 정정).

### 2-7. 규격 문서 `Docs/tech/telegraph-style.md` (아트 전달용)
층 구조 그림(윤곽선/채움 · 늘이기/반복), 각 칸 의미·단위(m vs 비율), 마스크 이미지 규격(정사각·2의 거듭제곱 256/512, **흰 = 보임 / 검 = 투명**, 읽는 채널, sRGB 끔, 반복 모드는 Wrap Repeat, 압축), 공격/인터럽트 색 규약(빨강 = 맞는다 / 파랑 = 간파 가능 — 바꿀 때 의미 유지), Play 중 확인 방법(SO 선택 → 바로 반영 · 값이 에셋에 남음), 몬스터별 SO 만드는 법.

## 3. 리스크
- **셰이더 그래프 파일 직접 편집** — 노드는 커스텀 함수 1개 + 위치 1개 + 속성만 추가해 범위를 줄인다. 저장소의 커스텀 함수 노드(`CyaniluxRetroCRT.shadergraph`) 형식을 따른다. 임포트 오류·Angle Fade 유지를 Unity 에서 확인.
- 데칼 셰이더에서 월드 위치 → 예고 로컬 좌표 변환이 데칼 패스(DBuffer/스크린스페이스)에서 같은 값을 주는지 — Play 확인.
- 동시 예고 수 부하(합집합 단계 굽기는 남는다) — 버퍼 재사용으로 할당은 없애고, 굽기 자체 비용은 측정.
- 드론·전기 장판 판 셰이더 교체가 크로스헤어·전기 표시 깜빡임(같은 `BossPatternVisuals`)에 영향 — 마스크 기본 없음이면 Sprites/Default 와 같아야 한다.

## 4. 검증
- 컴파일 0 · 셰이더 임포트 오류 0 · EditMode(코드 기본값 = 기존 상수 / 마스크 좌표 늘이기·반복 / 버전 갱신).
- Play(에디터): 기본 에셋에서 **변화 없음**(23호 공격·인터럽트·합집합·잡기, 건틀렛 펀치·스매시 원, 스피너 채찍·돌진, 드론 원, 전기 장판, 크로스헤어) — 수정 전 스크린샷과 비교.
  SO 색·마스크(늘이기/반복)·세기를 Play 중 바꾸면 채움 진행이 끊기지 않고 즉시 반영.
- MPPM 2인: 클라에서도 같은 스타일로 그려짐.

## v1 에서 바뀐 것 (Codex 10-13)
CPU 굽기 → GPU · 사용처 정정(일반 몹 아님, 드론·전기 장판 추가, 원형 장판 미사용) · 재질 사실 정정(SG_DecalFloorOnly) · 마스크 좌표를 UV 가 아니라 예고 로컬 좌표로 · 모든 피어 주입 · 빈 칸 해석(연결 + 코드 기본값) · OnValidate 는 버전만 · 진행 유지 갱신 · 알파 1회 · 두께 단위 · 캐시 키·누수.

## 진행
### 10-13 구현 ✅ · 컴파일 0 · 셰이더 오류 0(데칼 패스 7·판 패스 1) · 전투 EditMode 117/2(기존 실패 2 = BossCounterData Jump/Dash — 무관) · TelegraphStyleTests 전부 통과 · ⏳ 화면 확인
- 셰이더: `Rendering/Telegraph/TelegraphMask.hlsl`(공용 계산) · `TelegraphQuad.shader`(판) · `3.Materials/SG_TelegraphDecal.shadergraph`(SG_DecalFloorOnly 복제 + 속성 6 + 커스텀 함수 1 + 월드 위치 1, 연결선 3개 교체 — 스크립트로 생성).
- 재질: `Resources/Telegraph/MA_TelegraphDecal.mat`(MA_BossMarkerDecal 값 복사) · `MA_TelegraphQuad.mat` — 코드가 Resources 로 불러 빌드에 항상 포함. 없으면 옛 경로(색 굽기)로 대체.
- `TelegraphStyleSO`(Monster/) + `MonsterDataSO.telegraphStyle`([DataTableIgnore]) + 기본 에셋, 몬스터 데이터 12개 연결(저작 메뉴 `Tools/Monster/공격 예고 스타일 준비 (멱등)`). ⚠️ 저장하며 다른 팀원이 추가한 미저장 기본값 칸(`locomotion*`)이 같이 기록되고, 코드에서 없어진 `isMidBoss` 가 빠졌다 — 값 변화 없음.
- `BossAttackConeTelegraph` 재작성: 모양 흰색 굽기 + 재질 색·마스크, 스타일은 붙은 몬스터 데이터에서(`TelegraphStyleSO.For`, 모든 피어), 버전 갱신(진행 유지), 캐시 키에 반경·두께, 텍스처·버퍼 재사용(`BossDirectionIndicator.PixelBuffer`), 합집합 정리 누락 수정. `InterruptRgb` 제거.
- 드론(`WellsDroneAttack.PaintLockCircle`)·전기 장판(`BossElectricFloor.PaintWarnTile`) = `BossPatternVisuals.PaintStyled`. 옛 색 칸 4개 `[Obsolete]`(값 보존). `Paint` 는 마스크를 끈다(발동 표시 재사용).
- 건틀렛 스매시 = 스타일 interrupt.fill RGB, 버전 바뀌면 다시 칠함. `AoeTelegraph.ApplyStyle`(호출 쪽이 넘길 때만) + 복제 재질 정리. 23호 차징 오라만 호출(프리팹 칸 비어 있어 실제 표시 없음).
- 규격 문서 [Docs/tech/telegraph-style.md](../../tech/telegraph-style.md).
- Play(에디터, 부분): 보스방 전기 장판 경고 칸이 새 판 셰이더로 정상 렌더링 확인. 공격 예고 데칼 = 팀장 확인 OK(10-13 "데칼 제대로 보임"). 임시 확인 메뉴 삭제.
