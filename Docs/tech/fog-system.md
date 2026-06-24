# 포그 시스템 (커스텀 URP 스크린스페이스 Fog)

> 위치: `Assets/1.Scripts/Rendering/Fog/`. URP RenderGraph 풀스크린 1패스(해석적). 탑다운/Forward+ 경량.
> **순수 비주얼** — 네트워크/게임플레이 로직 없음(각 클라 로컬 렌더, 동일 씬·설정이면 동일 결과).
> 기획 검토용 요약 문서. "쓴다/안 쓴다", 파라미터 튜닝 판단에 사용.

## 0. 켜는 법 (3단계)
1. **렌더러 피처**: `Assets/99.Settings/PC_Renderer.asset`(+`Mobile_Renderer.asset`) Inspector →
   `Add Renderer Feature → Fog Renderer Feature`. (Shader 칸 비우면 자동 탐색)
2. **매니저**: 빈 GameObject → `Add Component → Fog Manager` → `Fog Profile` 에셋 할당
   (`Assets/Create → Rendering → Fog Profile`). → 즉시 화면에 포그(에디터에서도 실시간).
3. (선택) 로컬 볼륨(`Fog Volume`) / 페인트 마스크(`Window → Rendering → Fog Painter`).

끄기: FogManager 컴포넌트의 `Fog Enabled` 체크 해제 → 전 화면 포그 즉시 off (피처는 패스스루).

---

## 1. 포그의 3개 레이어 (개념)
| 레이어 | 무엇 | 어디서 조절 |
|--------|------|-------------|
| **전역(Global)** | 씬 전체 높이·거리 기반 포그 | **FogProfile** |
| **로컬 볼륨(Volume)** | 박스/스피어 영역에 포그 **추가** | **FogVolume** 컴포넌트 |
| **페인트 마스크(Mask)** | 손으로 칠해 포그 **추가/제거/색칠** | **Fog Painter** + FogManager |

최종 포그 = `전역` + `Σ 볼륨` 을 누적 → `마스크`로 곱(가/감) → 색·태양 적용. 거리·높이로 자연 감쇠.

---

## 2. FogProfile 파라미터 (전역 — 기획 튜닝 대상)

### 색 / 밀도
| 항목 | 의미 | 비고 |
|------|------|------|
| **Color** | 포그 색(HDR) | 분위기 핵심. 살짝 채도 있는 회청색 추천 |
| **Distance Mode** | 거리 포그 곡선: `Linear`/`Exponential`/`ExponentialSquared` | Exp 계열이 자연스러움 |
| **Density** | 거리 포그 농도(Exp/Exp² 스케일) | 클수록 가까이서도 짙어짐 |
| **Distance Start / End** | Linear 모드의 시작/끝 거리(m) | Linear일 때만 의미 |
| **Max Opacity** | **최종 불투명도 상한(0~1)** | 포그가 너무 짙으면 이걸 낮춤. 현재 0.22 = 옅음 |

### 높이 기반
| 항목 | 의미 |
|------|------|
| **Height Start** | 이 고도(Y) **이하 = 풀 포그** |
| **Height End** | 이 고도 **이상 = 포그 0** (사이는 부드럽게 감쇠) |
| **Height Strength** | 저고도 "바닥 안개" 세기(거리 무관). 0이면 거리포그만 |

> 높이 포그라서 **높은 바닥/단상 위는 옅고, 낮은 바닥은 짙음**. 벽 위/아래 경계에서 자연 감쇠.

### 스카이박스
| 항목 | 의미 |
|------|------|
| **Skybox Influence** | 하늘(원거리) 픽셀에 포그 적용 비율(0~1). 탑다운이면 0~0.5 정도 |

### 태양 인스캐터 (햇빛 산란 글로우)
| 항목 | 의미 |
|------|------|
| **Use Main Light Direction** | 켜면 씬의 메인 디렉셔널 라이트(Sun) 방향 사용 |
| **Sun Direction** | 위 끄면 수동 방향(빛이 진행하는 방향) |
| **Sun Color / Intensity / Power** | 태양 쪽을 바라볼 때 포그 글로우 색/세기/집중도 |

### 노이즈 (구름결·흐름)
| 항목 | 의미 |
|------|------|
| **Noise Enabled** | 노이즈 on/off. off면 균일한 포그 |
| **Noise Texture** | 흐름 패턴 텍스처. **비우면 절차적(procedural) 노이즈가 기본 사용됨**(아래 3절) |
| **Noise Scale** | 노이즈 크기(작을수록 큰 덩어리) |
| **Noise Strength** | 농도 변동 폭(0=균일, 1=강하게 출렁) |
| **Noise Scroll** | 월드 X,Z 방향 흐름 속도 → **"포그가 흐르는" 효과** |

---

## 3. 노이즈 텍스처 — 없을 때 기본 동작
- **Noise Texture를 비워두면**: 셰이더 내장 **절차적 value-noise**(해시 기반)가 자동 사용됨.
  별도 에셋 없이 바로 흐르는 포그가 나온다. → 기본값으로 충분히 자연스러움.
- **텍스처를 넣으면**: 그 텍스처의 R 채널을 노이즈로 사용(직접 디자인한 구름결 등 적용 가능).
- 둘 다 `Noise Scale`(크기)·`Noise Strength`(세기)·`Noise Scroll`(흐름 속도)로 동일하게 제어.
- 즉 **기획이 텍스처를 안 줘도 동작**하고, 원하면 텍스처로 교체해 룩을 바꿀 수 있음.

---

## 4. FogVolume (로컬 영역 포그 — 추가)
오브젝트(또는 존 프리팹 루트)에 `Fog Volume` 추가. **그 영역에 포그를 더한다**(빼는 게 아님 — 빼기는 마스크).
| 항목 | 의미 |
|------|------|
| **Shape** | `Box` / `Sphere` |
| **Box Size / Sphere Radius** | 영역 크기(Transform 스케일과 곱해짐) |
| **Density** | 그 영역에 더할 포그 농도 |
| **Soft Border** | **경계 페이드 폭(미터)**. 클수록 가장자리가 부드럽게 사라짐 → "딱 네모" 방지. 볼륨 크기에 맞춰 3~10+ |
| **Override Color / Color** | 그 영역만 다른 색(예: 독구름=초록) |

> Transform로 배치하므로, 맵 존 프리팹에 붙이면 **존이 셔플돼도 포그가 따라감**(테마별 포그).

---

## 5. Fog Painter (마스크 — 손으로 추가/제거/색칠)
`Window → Rendering → Fog Painter` 창.

### 절차
1. 창에서 **Fog Manager** 자동 탐색(또는 지정).
2. **Create New Mask** → 해상도 선택 → 저장 위치 지정. (자동으로 FogManager에 할당+활성)
3. **마스크 월드 영역**(Center / Size X,Z)을 맵 범위에 맞춤. (씬에 노란 사각 기즈모로 표시)
4. **페인팅 시작** 토글 ON → 씬뷰에서 마우스로 바닥(XZ 평면)에 칠한다.
   - **Brush**: `Add Fog`(추가) / `Erase Fog`(제거=포그 없는 공간) / `Paint Color`(색칠)
   - **Brush Size**(월드 m) / **Strength** / (색칠 시)**Color**
   - 씬뷰에 원형 브러시 미리보기가 보이고, 드래그로 칠함.
5. **Save Mask** 로 저장(.asset). 씬 재로드해도 유지.
6. 채우기 버튼: `Fill Neutral`(변화 없음=중립) / `Clear (no fog)`(전체 제거) / `Fill Full Fog`(전체 추가).

### 마스크가 하는 일 (셰이더 해석)
- 마스크 알파 A: **중립 0.5**(변화 없음) 기준. **0=포그 제거, 1쪽=포그 추가**(곱연산).
- 마스크 RGB: 칠한 색으로 그 영역 포그를 **틴트**(FogManager의 `Mask Tint Strength`로 세기).
- 마스크 영역(Center/Size) **밖은 영향 없음**(전역 포그 그대로).

> "특정 구역만 포그 없애기"는 **Erase 브러시**, "특정 구역 다른 색 포그"는 **Paint Color**.
> 브러시는 부드러운 원형 falloff라 자연스럽게 번진다(Strength/Size로 조절).

---

## 6. 성능 / 제약
- 풀스크린 1패스·해석적(레이마칭 없음). 볼륨은 거리순 **최대 16개**(FogManager `Max Volumes`).
- Linear 컬러스페이스 + HDR. PC/Mobile 렌더러 양쪽에 피처 등록 필요(모바일은 볼륨 수 줄이기).
- 비주얼 전용 → 멀티플레이 동기화 불필요(같은 씬·마스크면 전 클라 동일).

## 7. "쓴다/안 쓴다" 판단 포인트 (기획)
- **분위기 가치 충분** + **저비용** → 권장. Profile 하나로 룩 프리셋화·재사용.
- 안 쓸 거면: FogManager 미배치 or `Fog Enabled` off (렌더 비용 0).
- 더 화려한 진짜 볼류메트릭(라이트 샤프트/레이마칭)은 현재 범위 밖(필요 시 향후 확장 경로 있음).
