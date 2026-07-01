# Toon Shader 아트 인수인계 가이드

> 블루아카이브풍 SD 캐릭터 셀셰이더 (URP17 / Unity 6). 캐릭터를 **부위별 머티리얼**로 나눠 적용한다.
> 프로그래머가 기본 셋업 후 아트가 인스펙터에서 미세 튜닝하는 워크플로.

---

## 1. 캐릭터 파트 ↔ 머티리얼 매핑 (Welz 기준)

| 현재 메시 이름 | 역할 | 머티리얼 | 권장 이름(fbx) |
|---|---|---|---|
| `body_female.007` | 얼굴/두상 | **Welz_Skin** | `Face` |
| `body_female.005` | 귀 + 목 | **Welz_Skin** | `Ear_Neck` |
| `body_female.006` | 다리/스타킹/신발 | **Welz_Cloth** | `Legs` |
| `Plane.003` | 앞머리 | **Welz_Hair** | `Hair_Front` |
| `Plane.013` | 옆/뒷머리 | **Welz_Hair** | `Hair_Back` |
| `Plane.005` | 상의/로브 | **Welz_Cloth** | `Cloth_Top` |
| `Plane.004` | 안경 렌즈 | **Welz_Glass** | `Glasses` |

> ⚠️ 메시 이름(`body_female.005` 등)은 **fbx(블렌더)에서 바꿔야 영구 반영**된다.
> Unity 씬 인스턴스에서 rename하면 재임포트 시 원래 이름으로 돌아온다. 위 "권장 이름"으로 fbx에서 정리 요망.

---

## 2. 머티리얼 종류

| 머티리얼 | 대상 | 특징 | 셰이더 |
|---|---|---|---|
| **Welz_Skin** | 피부/얼굴/귀·목 | 밝고 **저대비** (Ambient Fill 높음) | Project/ToonLit |
| **Welz_Hair** | 머리카락 | **고대비** + 이방성 하이라이트(엔젤링) | Project/ToonLit |
| **Welz_Cloth** | 옷/로브/다리 | **고대비** | Project/ToonLit |
| **Welz_Glass** | 안경 렌즈 | 반투명 유리 + 프레넬 | Project/ToonGlass |

---

## 3. ToonLit 프로퍼티 — 뭘 조절하면 뭐가 바뀌나

### Cel Shading (셀 음영)
| 프로퍼티 | 효과 |
|---|---|
| **Fixed Light** (토글) | 켜면 실제 씬 라이트를 무시하고 **아래 고정 방향**으로 음영. 캐릭터가 돌거나 라이트가 움직여도 음영 일관(블아식) |
| **Fixed Light Dir** | 고정 음영 방향(오브젝트 공간). Y↑ = 위에서 오는 세로 음영. 현재 `(0.15, 1, 0.35)` |
| **Shade Tint** | 그림자부 색. **어둡게** 하면 대비↑ |
| **Shade Threshold** | 밝은면↔그림자 경계 위치(half-lambert 기준 0.5) |
| **Shade Smoothness** | 경계 부드러움. **낮을수록 딱 끊김(대비↑)** |
| **2nd Shade Threshold / Smoothness** | 더 어두운 2단 그림자 |
| **Shade Strength** | 음영 전체 세기 |
| **Ambient Fill** | 그림자부 밝기. **높이면 그림자가 밝아짐**(대비↓). 얼굴은 높게(0.9) = 평평·밝게 |

### Rim Light (역광 테두리)
| Rim Color / Power / Intensity / Align | 외곽 프레넬 역광. Align은 광원쪽만 비출 비율 |

### Hair Anisotropic (머리 전용, Hair Mode 토글)
| 프로퍼티 | 효과 |
|---|---|
| **Hair Mode** (토글) | 켜면 엔젤링(천사링) 하이라이트 |
| **Hair Spec Color** | 하이라이트 색. **어둡게 = 은은**, 밝게 = 강렬 |
| **Hair Spec Threshold** | 밴드 위치. **높이면 밴드가 얇아짐** |
| **Hair Spec Smoothness** | 밴드 부드러움. **낮을수록 얇고 또렷** |
| **Hair Highlight Shift** | 밴드를 위/아래로 이동 |
> ⚠️ 이방성은 메시 **탄젠트 방향**에 의존. fbx 탄젠트가 머리카락 흐름과 어긋나면 밴드가 넓거나 엉뚱하게 뜬다. 그 경우 fbx UV/탄젠트 정리 필요.

### Metal Toggle (메카 23호 등, Metal Mode 토글)
| Metal Mode / Spec Color / Threshold / Smoothness | 금속용 하드 스페큘러. 캐릭터(피부/천)는 끔 |

### Tone (톤)
| Brightness | 전체 밝기 |
| Saturation | 채도 |

### Outline (외곽선)
| **Outline Color** | 외곽선 색. **부위색의 어두운 톤**으로 지정(검정 통일 X). 머리=남색/옷=회보라/피부=살구 |
| **Outline Width** | 외곽선 두께(오브젝트 공간). 현재 0.005 |

---

## 4. ToonGlass 프로퍼티 (안경)
| Tint (rgb/알파) | 유리 색 + 투명도(a 낮을수록 투명) |
| Fresnel Power / Edge Brightness | 가장자리 빛나는 유리 느낌 |

---

## 5. 아트 작업 가이드 (자주 만지는 것)
- **엔젤링 다듬기**: `Hair Spec Threshold`↑ + `Hair Spec Smoothness`↓ → 얇은 링. `Hair Highlight Shift`로 위치.
- **부위 아웃라인 색**: 각 머티리얼 `Outline Color`를 그 부위 대표색의 어두운 톤으로.
- **얼굴 밝게 유지**: Skin의 `Ambient Fill`↑(0.9), `Shade Strength`↓.
- **몸/옷 대비 강하게**: Cloth/Hair의 `Ambient Fill`↓ + `Shade Smoothness`↓.
- **음영 방향**: `Fixed Light Dir`을 더 수직(Y↑)으로 = 위→아래 세로 음영.

---

## 6. 현재 세팅 요약 (프로그래머 기본값)
- 세로 고정 음영 ON(Fixed Light) — Dir `(0.15, 1, 0.35)`
- 아웃라인 width 0.005, 부위별 색
- 머리 이방성 ON — Spec Color `(0.5,0.5,0.6)`, Smoothness 0.004
- 얼굴 SDF 그림자는 **범위 제외**(밝게+외곽선으로 대체)
