# PLAN-crt-fx.md — CRT / 글리치 연출 툴킷

> 상태: **승인 대기** · 2026-09-21
> 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 소비처: [PLAN-title-flow.md](PLAN-title-flow.md) (타이틀 연출이 이 툴킷에 의존한다)
> 레퍼런스: VolFx 트레일러 — **에셋을 구매하지 않는다.** 보이는 결과만 우리 식으로 재구현한다
> (AIRULE "Reference-Based Implementation").

## 1. 목표

CRT 연출 파라미터를 **런타임에 커브로 흔들 수 있게** 만들고, 그 위에 타이틀에서 실제로 쓰는 효과를 얹는다.

**완료 조건에 못을 하나 박는다** — 🔴 **모든 효과는 타이틀 흐름 어딘가에서 최소 1회 실사용한다.**
안 쓰는 효과는 프로덕션 경로를 안 지나가서 "만들어 뒀는데 실제로는 안 도는" 상태가 된다
(lessons 반복패턴 1번). 쓸 자리가 없으면 만들지 않는다.

## 2. 현재 이해 (레포에서 확인한 사실)

### 이미 있는 것

`Rendering/RetroCRT/Shaders/CyaniluxRetroCRT.shadergraph` 에 노출된 프로퍼티:

```
Bezel Size · CRT Screen Warp · CRTWarpStrength · Distortion Strength
RGB Stripe Resolution · RGB Stripe Strength      ← 어퍼처 그릴
Scanline Height · Scanline Strength
Scrolling Static · Static Strength                ← 노이즈
Image Distortion · Image Brightness
```

별도로 `PixelScanline.shader` 가 픽셀레이트 + 색 띠 스캔라인을 담당한다.
URP Volume 쪽은 `99.Settings/DefaultVolumeProfile.asset` 에 Bloom · **ChromaticAberration** ·
ColorAdjustments · FilmGrain · LensDistortion · Vignette 등이 이미 들어 있다.

### 🔴 없는 것 — 이게 이 계획서의 1번 작업이다

`RetroCRTFeature` 가 머티리얼에 실제로 넣어 주는 값은 **`_BezelSize` 하나뿐**이다
(`RetroCRTFeature.cs:79,120`). 나머지 프로퍼티는 전부 **머티리얼 에셋에 박힌 고정값**이고,
`RetroCRTController` 의 공개 API 는 `EffectEnabled` 와 `ToggleEffect()` **둘뿐** — 켜고 끄기만 된다.

> **즉 룩은 거의 다 있는데 흔들 손잡이가 없다.**

## 3. 레퍼런스 분석 결과

영상을 0.3초 간격으로 추출해 확인했다(0:00~0:25, 0:44~0:48, 1:22~1:24 / 총 104프레임).
0:44 구간에 에셋 자체 데모 UI 가 효과 이름을 띄운다:
`Bloom · Scanlines(+Scanline Drift) · Chromatic · Outline · Dither · Ascii · Flow · Vhs · Crt ·
Jpeg Compression · Noise · Crush · Selective · Lights · Adjustments`

| 시각 | 관찰된 것 |
|---|---|
| 0:00~0:04.5 | 상시 크로마틱 + 배럴 워프 + 스캔라인 + 비네트. 구체가 지나가며 **프레임 피드백 트레일**(흰 코어 + 마젠타/시안 프린지), 약 2.5초 감쇠 = `Flow` |
| 0:05.7 | 🔴 **한 프레임 만에 룩이 통째로 교체** — 외곽선 검출 + 강한 블룸. 페이드가 아니라 컷 |
| 0:06.6~0:08.4 | **텍스트 스크램블 디코드** — 랜덤 글자가 약 0.9~1.2초에 걸쳐 실제 문장으로 수렴 |
| 0:09.6~0:24 | 텍스트 줄이 하나씩 추가. **각 줄이 글리치로 들어온다**(스크램블 + RGB 스플릿 + 가로 변위). 2~3초 간격 **불규칙**하게 가로 찢김 밴드가 화면을 훑는다 |
| **0:24.0** | 🔴 **화면 전체가 둥근 모서리 + 배럴 워프된 작은 CRT 로 수축**하며 밀려난다. 스캔라인 밴드가 보이는 채로 |
| 0:44~0:48 | Jpeg = 8×8 블록 아티팩트 / Vhs = 밝은 가로 광대역 스윕(헤드 스위칭) + 적녹 엣지 분리 + 그레인 / **45.5초 이미지가 블록 단위로 산산조각**(데이터모시) / Crt = 가로 행 변위(양끝 흰 여백) + **세로 롤** + 스캔라인 |
| 1:22~1:24 | Clean / Noise / Crush 비교. Noise = 가로 스트릭 데이터모시, Crush = 색 계조 뭉갬 + **블록 드롭아웃**(검은 사각형이 튀었다 사라짐) |

## 4. 접근

### 4.1 D0 — 파라미터 런타임 전달 경로 (**선행. 이게 없으면 나머지가 전부 막힌다**)

- `RetroCRTController` 에 프로퍼티별 런타임 값 + 공개 setter 를 추가
- `RetroCRTPass` 가 `MaterialPropertyBlock` 에 `_BezelSize` 외 나머지도 밀어 넣도록 확장
- 🔴 **프로퍼티 참조 이름을 고정 문자열로 박지 않는다.** Shader Graph 자동 생성 이름은 재저장에
  깨진다(lessons #5). 그래프에서 Reference 를 `_CRTWarpStrength` 처럼 **직접 지정한 것만** 사용하고,
  `Shader.PropertyToID` 캐시 + **시작 시 `HasProperty` 검사 후 없으면 경고**를 남긴다
- 흔드는 쪽은 `CrtFxDriver` (신규) — `AnimationCurve` + 지속시간으로 한 파라미터를 0→피크→0 으로 굴린다.
  원샷(버스트)과 지속(앰비언트) 두 모드

### 4.2 효과 — 타이틀 기여도 순

| # | 효과 | 구현 | 난이도 | **타이틀에서 쓰는 자리** |
|---|---|---|---|---|
| D1 | **Chromatic Aberration** | 타이틀 `GlobalVolumeProfile.asset` 에 항목 추가 (URP 기본) | 낮음 | 상시 톤 |
| D2 | **가로 변위 밴드** (VHS 트래킹/tear) | 신규 — 행 단위 UV 오프셋 + 노이즈. 불규칙 주기 | 낮음 | 키 입력 순간 · 메뉴 대기 중 간헐 |
| D3 | **텍스트 스크램블 디코드** | 신규 — TMP 문자열 치환. 셰이더 아님 | 낮음 | PRESS ANY KEY · 메뉴 버튼 등장 |
| D4 | **화면 수축 CRT 전환** | 신규 — 화면을 CRT 면 텍스처로 취급해 축소 | 중 | Start 누른 뒤 로비 전환 |
| D5 | **세로 롤** | 신규 — UV Y 오프셋 + 랩어라운드 | 낮음 | 글리치 버스트에 동반 |
| D6 | **블록 글리치 / 데이터모시** | 신규 — 블록별 UV 오프셋 + 색 셔플 | 중 | 키 입력 순간 (D2 와 합성) |
| D7 | 전원 온 (수축 → 확장) | D0 위에서 `Image Distortion` + `Brightness` 커브 | 낮음 | 씬 진입 |
| D8 | 다가갈수록 왜곡 증가 | D0 위에서 `CRTWarpStrength` 커브 | 낮음 | Approaching 구간 |

**D1~D3 을 1차로 끊는다.** 여기까지면 타이틀 흐름이 연출로서 성립한다.
D4~D6 은 2차, D7~D8 은 D0 이 끝나면 사실상 설정값이라 같이 붙인다.

### 4.3 만들지 않는 것

`Outline` · `Flow`(피드백 트레일) · `Dither` · `Ascii` · `Jpeg Compression` · `Crush` · `Selective`(레이어 마스크).
타이틀에 쓸 자리가 없다. §1의 못을 여기에 적용한다.
**Selective 는 특히 필요 없다** — 결정상 CRT 를 화면 전체에 켜기로 했으므로 레이어 선택 적용이 무의미하다.

### 4.4 데모 씬

`0.Scenes/Debug/CrtFxScene.unity` (신규) — 전 효과를 한 화면에서 토글·슬라이드로 확인.
🔴 단 **데모 씬 통과는 완료 조건이 아니다.** 타이틀 실사용이 완료 조건이다.

## 5. 수정 예정 파일

| 파일 | 내용 |
|---|---|
| `1.Scripts/Rendering/RetroCRT/RetroCRTController.cs` | 런타임 파라미터 + setter |
| `1.Scripts/Rendering/RetroCRT/RetroCRTFeature.cs` | MPB 에 나머지 프로퍼티 전달 + `HasProperty` 검사 |
| `1.Scripts/Rendering/RetroCRT/Shaders/CyaniluxRetroCRT.shadergraph` | Reference 이름 고정 · 신규 노드(D2/D5/D6) |
| `1.Scripts/Rendering/RetroCRT/CrtFxDriver.cs` (신규) | 커브 구동 |
| `1.Scripts/Rendering/RetroCRT/ScreenShrinkPass.cs` (신규) | D4 |
| `1.Scripts/UI/Title/TextScramble.cs` (신규) | D3 |
| `0.Scenes/Art/title/GlobalVolumeProfile.asset` | ChromaticAberration 추가 |
| `0.Scenes/Debug/CrtFxScene.unity` (신규) | 데모 |

🔴 `99.Settings/PC_Renderer.asset` 과 `CyaniluxRetroCRT.mat` 은 **맵 씬과 공유한다.**
기본값을 바꾸면 보스전 룩이 같이 바뀐다 — 기본값은 건드리지 않고 **런타임 오버라이드로만** 흔든다.

## 6. 리스크

| # | 항목 | 대응 |
|---|---|---|
| R1 | 🔴 **공유 머티리얼·Renderer 를 맵 씬과 함께 쓴다** | 기본값 불변. 런타임 오버라이드만. 맵 씬 룩 회귀를 검증에 포함 |
| R2 | Shader Graph 자동 생성 프로퍼티 이름이 재저장에 깨진다 | Reference 직접 지정 + `HasProperty` 경고 |
| R3 | 셰이더 변종 컴파일 지연으로 첫 발동이 튄다 | 워밍업 또는 Variant 수집 (lessons #3 #13) |
| R4 | D4(화면 수축)가 Overlay UI 를 못 먹는다 | CRT 패스는 `AfterRenderingPostProcessing` — **Overlay HUD 는 그 뒤에 그려진다.** 페이드/안내를 어디에 둘지 D4 설계 시 확정 |
| R5 | 글리치가 설정창 글자 가독성을 깬다 | [PLAN-title-flow.md](PLAN-title-flow.md) §8-10 과 같은 기준으로 판정 |
| R6 | 영상은 **소리를 못 들었다.** 연출의 절반이 사운드다 | 사운드는 이 계획서 범위 밖. 별도 판단 필요 |

## 7. 범위 밖

- VolFx 에셋 구매·도입 · 셰이더 코드 이식
- §4.3 의 미채택 효과
- 맵/보스전 연출에의 적용 (타이틀이 먼저)
- 사운드

## 8. 검증

1. **D0**: 런타임에 `CRTWarpStrength` 를 0 → 0.15 → 0 으로 굴렸을 때 화면이 실제로 따라 휜다
2. **D0**: 프로퍼티 이름이 안 맞으면 **조용히 무시되지 않고 경고가 뜬다** (그래프 재저장 후 재확인)
3. **D1~D8**: 각 효과가 **타이틀 흐름의 지정된 자리에서 실제로 발동**한다 (데모 씬이 아니라)
4. 🔴 **맵 씬 회귀** — 보스전에 들어가 CRT 룩이 이전과 같은지. 공유 머티리얼 기본값이 안 바뀌었는지
   `git diff` 로 `CyaniluxRetroCRT.mat` · `PC_Renderer.asset` 확인
5. 🔴 로비 진입 후 `RetroCRTController.ActiveController == null` + 패스 미등록
6. 셰이더 변종 첫 컴파일로 인한 프레임 튐이 **연출 시작 시점에 보이지 않는다**
