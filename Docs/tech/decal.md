# URP Decal — Use Rendering Layers 활성화

> Unity 6000.3 / URP 기준. Decal Projector에 `Rendering Layers` 항목을 노출시키려면
> Universal Renderer Data의 **Decal Renderer Feature**에서 `Use Rendering Layers`를 켜야 한다.

---

## 1. 현재 사용 중인 URP Asset 확인

먼저 어떤 URP Asset이 실제로 적용되는지 확인한다.

```
Edit → Project Settings → Quality
```

활성화된 품질 단계의 **Render Pipeline Asset**을 본다.

- 여기에 에셋이 지정돼 있으면 **이 설정이 우선**이다.
- `None`이면 아래 기본값을 사용한다.

```
Edit → Project Settings → Graphics → Default Render Pipeline
```

> ⚠️ Quality의 파이프라인 설정이 Graphics의 기본값을 **덮어쓴다.**

---

## 2. Universal Renderer Data 열기

확인한 URP Asset을 Project 창에서 선택한다.

Inspector에서 **`Renderer List`** 항목을 찾고, 사용 중인 Renderer 항목을 클릭하거나
오른쪽 **`⋮`** 버튼을 눌러 Renderer Data 에셋을 선택한다.

> Unity 6000.3 공식 문서도 URP Asset의 `Renderer List`에서 Renderer 항목 또는 `⋮`를 클릭해
> Universal Renderer 에셋을 찾도록 안내한다.

보통 파일 이름은 다음 형태다.

```
UniversalRenderer
PC_Renderer
URP_Renderer
MainRenderer
```

---

## 3. Decal Renderer Feature에서 옵션 켜기

Universal Renderer Data 에셋을 선택한 뒤 Inspector 맨 아래쪽으로 내려간다.

```
Renderer Features
└─ Decal
   ├─ Technique
   ├─ Max Draw Distance
   └─ Use Rendering Layers   ← 이 옵션 체크
```

`Use Rendering Layers`를 체크한다.

Decal 항목이 없다면 아래로 추가한다.

```
Add Renderer Feature → Decal
```

---

## 정리

- 정확한 설정 경로: **Decal Renderer Feature → Use Rendering Layers**
- 이 옵션은 `Max Draw Distance` 아래쪽에 있는 별도 체크박스다.
- 활성화하면:
  - Decal Projector에 **`Rendering Layers`** 항목이 나타난다.
  - **DepthNormal prepass**가 추가된다. (성능 비용 발생)

---

## 데칼이 바닥과 안 섞이던 문제 (할 일 202, 2026-11-04)

### 원인(코드 확인)
- 맵 바닥(존·보스방 바닥 재질 `Generic_01_A`·`Generic_01_A_Wet`)은 SVN `Assets/50.Art/MapGen/MapObj/material/Generic_Standard.shadergraph` = **URP Lit** 이다. DBuffer 데칼을 조명 **전에** 받으므로 조명·그림자·포그(화면 후처리)는 원래 데칼에도 걸린다.
- 섞이지 않게 보인 주원인은 **데칼이 바닥 노멀을 덮어쓴 것**이다.
  - 플레이어 `SkillRangeIndicator`·`AimIndicator` 그래프: NormalAlpha = 상수 1, NormalTS = 평평 → **투영 상자 전체**(투명한 부분까지)에서 바닥 노멀맵·하이라이트가 사라져 네모난 평평한 자국이 생겼다.
  - 예고 4종(`SG_EnemyC3Decal`·`SG_TelegraphDecal`·`SG_DecalFloorOnly`·`SG_ColoredDecal`): 기본 bump(평평) 노멀 × 알파 × `Normal_Blend` 0.5 → 데칼이 찍힌 곳만 바닥 요철이 절반쯤 평평해져 스티커처럼 떠 보였다.
- 색은 DBuffer 규칙상 **바닥 알베도와 lerp(덮기)** 만 된다 — 데칼 셰이더는 바닥 알베도를 읽을 수 없어서 곱·오버레이는 데칼 쪽에서 못 한다.
- 참고: Flat Kit(`Assets/FlatKit/.../Lighting_DR.hlsl`)은 데칼을 **최종 셰이딩 색**에 lerp 하고 메인 라이트만 곱한다(텍스처·밴드·추가 광원이 빠짐). 맵 바닥은 Flat Kit 이 아니고 캐릭터는 수신자 비트가 없어 지금은 영향 없다.

### 고친 것(데칼 쪽만 — 바닥·Flat Kit·SVN 셰이더 무수정)
- 데칼 6종 그래프 모두 `affectsNormal` 끔(Albedo 만). 바닥 노멀맵·하이라이트가 데칼 아래에도 그대로 남는다. 재질의 `Normal_Blend` 칸은 남아 있지만 이제 쓰이지 않는다.
- 플레이어 2종 그래프는 **Angle Fade** 를 켰다. 프로젝터 기본 180/180 이면 변화 없음 — 바닥 전용으로 쓰려면 `DecalReceivers.ConfigureFloorProjector` 와 같은 값을 넣는다.
- 가장자리: C3 는 셰이더 AA·띠 그라데이션, 플레이어는 텍스처 알파 그대로 + 거리 감쇠(telegraph-style.md §8).

### 남은 선택지(바닥 셰이더 수정 필요 — 하지 않음)
- 데칼 색을 바닥 색과 **곱/오버레이**로 섞으려면 바닥 셰이더(`Generic_Standard.shadergraph`, SVN)가 DBuffer 를 직접 읽어 곱해야 한다(URP Lit 기본 적용 대신 커스텀). 필요하면 별도 할 일로.
