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
