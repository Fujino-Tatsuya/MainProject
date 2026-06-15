# 코딩 / 프로젝트 컨벤션

## 네이밍 (C#)
- 클래스/메서드/프로퍼티/enum: **PascalCase**
- 지역변수/파라미터: **camelCase**
- private 필드: **`_camelCase`**
- 상수: **PascalCase** (또는 `const`/`static readonly`)
- 인터페이스: **`I`** 접두 (`IDamageable`)

## 폴더 컨벤션 
- 현재 `Assets/Scenes`, `Scripts` 처럼 사용.

## Addressables
- 주소/그룹명에 **점·공백 금지**. "Simplify Addressable Names" 또는 커스텀 주소로 경로 의존 줄이기.
- 네트워크 스폰 프리팹은 NGO 등록과 주소 관리 정합성 유지.

## 데이터 주도 (ScriptableObject)
- 스킬(`AbilityConfig`), 빌드(`BuildModifier`), 보스 패턴(`BossPhasePattern`)은 SO로.
- 작은 SO 에셋 다수 = 머지 충돌 최소 + 디자이너/프로그래머 튜닝 편의.

## 입력
- **Unity Input System(액션 에셋)** 사용. Q/E/R/우클릭/좌클릭/이동/대쉬 액션 정의.

## 어셈블리 정의 (asmdef) — 권장
- 모듈별(Core/Network/Player/Boss/UI) `asmdef` 분리 → 컴파일 시간↓, 의존성 명확.
- 네임스페이스는 모듈명 기준(`Project.Core`, `Project.Player` 등). 폴더 번호 제거 후 정렬.
