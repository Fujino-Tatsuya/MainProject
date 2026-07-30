# 사운드 시스템 (FMOD Sound System)

> 출처: 팀 논의 정리. FMOD Studio 미들웨어 기반. 이벤트/파라미터 중앙 관리 + 씬별 뱅크 로딩 + 소유권 기반 제어를 원칙으로 한다.
> 관련 스크립트: `Assets/1.Scripts/SceneFlow/` (FmodEvents, FmodParams, SoundManager, SceneAudioLoader)

## 설계 원칙

1. **데이터 정의는 통합, 라이프사이클 관리는 분리한다.**
   - 이벤트/파라미터 "정의"는 한 곳에 모아 매직 스트링·중복을 없앤다.
   - 뱅크 로드/언로드, 인스턴스 캐싱은 각자의 라이프사이클에 맞는 곳에서 한다.
2. **소유권(ownership)을 따라 제어 책임을 배치한다.**
   - 전역에 속한 것(글로벌 파라미터·볼륨)은 중앙(SoundManager)이 설정한다.
   - 특정 인스턴스에 속한 것(로컬 파라미터)은 그 인스턴스를 소유한 오브젝트가 설정한다.
3. **컴포넌트와 코드를 용도에 맞게 조합한다.**
   - 위치에 묶인 3D 사운드는 컴포넌트의 자동 추적/도플러를 활용.
   - 게임 로직이 결정하는 재생·전역 제어는 코드로.

---

## 구성 요소

| 파일 | 형태 | 책임 |
|---|---|---|
| `FmodEvents.cs` | 싱글톤 MonoBehaviour | EventReference(이벤트 참조) 데이터 보관. `[Header]`로 씬 구분 |
| `FmodParams.cs` | **static class** | 파라미터 이름 상수 (Local / Global 중첩 구분) |
| `SoundManager.cs` | 싱글톤 | 재생·BGM·볼륨·파라미터 창구 (순수 오디오 제어) |
| `SceneAudioLoader.cs` | 싱글톤 | 씬↔뱅크 로드/언로드 (뱅크 생명주기 전담) |

### 왜 이렇게 나눴나

- **FmodEvents가 싱글톤인 이유**: EventReference는 인스펙터에서 드래그로 연결하는 데이터라 MonoBehaviour여야 한다.
- **FmodParams가 static class인 이유**: 파라미터 이름은 컴파일 타임 상수라 인스펙터 연결·런타임 변경이 없다. 싱글톤은 불필요한 오버헤드.
- **SceneAudioLoader를 SoundManager에서 분리한 이유**: 단일 책임(SRP). SoundManager는 "재생/믹싱", SceneAudioLoader는 "뱅크 생명주기"만 담당해 강결합을 막는다.

---

## 뱅크(Bank) 로딩 전략

- FMOD Settings의 `Load Banks`는 **항상 필요한 뱅크(Master/Strings 등)만** 자동 로드하도록 `Specified`로 둔다.
- **씬 전용 뱅크는 `SceneAudioLoader`가 씬 전환에 맞춰 로드/언로드**한다. (메모리 최적화)
- 로더는 `SceneManager.sceneLoaded` / `sceneUnloaded` 훅 + **`씬 이름 → 뱅크 목록` 인스펙터 매핑**으로 동작한다.
- 자신이 로드한 뱅크만 추적(`HashSet`)해 씬 이탈 시 언로드하며, 자동 로드 뱅크(Master 등)는 건드리지 않는다.

> **주의**: `Load Banks = All`이면 모든 뱅크가 시작 시 로드되어 씬별 로딩이 무의미해진다. 씬 분리 전략을 쓰려면 `Specified`로 바꿔야 한다.

### 향후 확장
지금은 단순 매핑으로 충분하다. 크로스페이드 / 로딩중 / 로드 실패 복구 등 **전이 규칙이 복잡해지면 SceneAudioLoader 안에 StateMachine을 도입**한다. (YAGNI — 필요해질 때 추가)

---

## 오디오 샘플 데이터 (메모리)

- **Load Bank Sample Data 끔** → 샘플은 뱅크 로드 시점이 아니라 **이벤트 첫 재생 시** 로드 → 첫 재생에 짧은 지연 가능하나 메모리 효율적.
- **언로드 방식** (3가지):
  1. **자동(참조 카운팅)**: 이벤트 인스턴스 릴리즈로 카운트 0이 되면 자동 언로드.
  2. **뱅크 단위 수동**: `Bank.loadSampleData()` / `unloadSampleData()` — 뱅크는 유지하고 샘플만 제어.
  3. **이벤트 단위 수동**: `EventDescription.loadSampleData()` / `unloadSampleData()` — 개별 이벤트 샘플만.
- **Event Emitter의 Preload Sample Data**: 컴포넌트 활성화(`Awake`) 시 샘플을 미리 로드해 첫 재생 지연 제거. 내부적으로 `EventDescription.loadSampleData()`를 대신 호출하는 편의 기능.
- 수동 `loadSampleData()`는 호출 횟수만큼 `unloadSampleData()`로 짝을 맞춰야 실제 해제된다.

---

## 파라미터 관리

### 이름은 상수로 (매직 스트링 제거)
파라미터는 문자열 이름으로 접근하므로 `FmodParams`에 상수로 모아 오타·리팩터링 리스크를 없앤다.
- `FmodParams.Local.*` — 특정 EventInstance에만 적용되는 로컬 파라미터
- `FmodParams.Global.*` — 시스템 전체에 영향을 주는 글로벌 파라미터

### ID 캐싱 (고빈도 갱신 최적화)
매 프레임 갱신하는 파라미터(엔진 RPM 등)는 이름 대신 `PARAMETER_ID`로 설정해 문자열 룩업을 회피한다.
- **ID는 뱅크가 로드된 뒤에만 조회 가능** → 캐싱 시점은 씬/뱅크 라이프사이클과 묶인다.
- **ID 캐싱 위치는 사용처 오브젝트** — 그 인스턴스를 소유한 컴포넌트가 자기 필드에 ID를 보관하고 라이프사이클에 맞춰 관리한다.

### 소유권에 따른 제어 책임

| | 소속 | 설정 주체 | SoundManager 역할 |
|---|---|---|---|
| **글로벌 파라미터** | StudioSystem (전역) | **SoundManager** | 설정 + ID 조회 둘 다 |
| **로컬 파라미터** | 특정 EventInstance | **인스턴스 소유 오브젝트** | ID 조회 헬퍼만 제공 |

- 글로벌은 주인이 될 오브젝트가 없어 전역 창구(SoundManager)가 설정까지 담당.
- 로컬은 인스턴스를 소유한 오브젝트만 설정 가능. SoundManager는 인스턴스를 들고 있지 않으므로 **ID 조회만** 거든다.

---

## SoundManager API

### 재생
- `PlayOneShot(EventReference, Vector3)` — fire-and-forget 일회성 재생. 재생 후 자동 릴리즈, 제어 불가, 위치 고정.
- `CreateInstance(EventReference)` — 제어가 필요한 EventInstance 반환. **호출자가 stop() 후 release() 책임**.

### BGM (인스턴스 하나만 유지)
- `PlayBGM(EventReference)` — 기존 BGM 페이드아웃 후 교체.
- `StopBGM(bool fadeout)` — 정지 및 해제.
- `SetBGMParameter(string, float)` — BGM 로컬 파라미터 변경 (전투 강도 전환 등).

### 파라미터
- `TryGetParameterId(EventReference, string, out PARAMETER_ID)` — 로컬 파라미터 ID 조회(뱅크 로드 후에만 성공). 사용처 캐싱용.
- `SetGlobalParameter(string, float)` — 글로벌 by-name. **저빈도 변경용(가독성 우선)**.
- `SetGlobalParameter(PARAMETER_ID, float)` — 글로벌 by-id. **고빈도 갱신용(성능)**.
- `TryGetGlobalParameterId(string, out PARAMETER_ID)` — 글로벌 파라미터 ID 조회.

### 볼륨 / 전역 제어
- `SetVolume(string vcaPath, float)` — VCA(볼륨 그룹) 볼륨. 예: `"vca:/BGM"`, `"vca:/SFX"`.
- `SetBusPaused(string busPath, bool)` — Bus 일시정지/해제. 예: `"bus:/"`(마스터).

---

## 컴포넌트 vs 코드 — 사용 기준

| 상황 | 방식 |
|---|---|
| 씬 고정, 위치 기반, 단순 트리거 (화톳불·앰비언스 존) | **Event Emitter 컴포넌트**로 끝 |
| 게임 로직이 재생을 결정, 전역 관리 필요 (BGM·UI·볼륨) | **SoundManager + FmodEvents** |
| 3D로 오브젝트에 묶였지만 재생/파라미터는 로직이 제어 (차량 엔진·NPC 음성) | **컴포넌트 부착 + GetComponent 코드 제어 + FmodEvents** |

- **판단 기준**: 공간 속성(위치·도플러)이 오브젝트에 묶이면 컴포넌트가 이득. "무엇을/어떻게"가 게임 상태에 따라 동적이면 코드가 담당. 둘이 겹치면 하이브리드.
- `PlayOneShot`은 위치가 호출 시점에 고정되므로 **움직이는 3D 음원엔 부적합**

---

## 리소스 해제 규칙

> **"내가 Create / Load 한 것만 내가 release / unload 한다."**

| 대상 | 해제 필요 | 비고 |
|---|---|---|
| `EventInstance` (CreateInstance) | ✅ `release()` | 내가 만든 것 |
| `PARAMETER_ID` / `PARAMETER_DESCRIPTION` | ❌ | 값(struct), 핸들 아님 |
| `EventDescription` (get 계열) | ❌ | 뱅크가 소유, 조회일 뿐 |
| 뱅크 / 샘플 데이터 (Load) | ✅ `unloadBank` / `unloadSampleData` | 로드 횟수만큼 언로드 |

- `EventInstance.release()`는 "즉시 파괴"가 아니라 **"재생 종료 후 정리해도 됨" 표시**. 일회성은 start() 직후 release 가능, 루프 사운드는 필드로 들고 있다 stop() 후 release.

---

## 사용 예시

```csharp
// UI 클릭음 (위치 무관 일회성)
SoundManager.Instance.PlayOneShot(FmodEvents.Instance.UIClick);

// BGM 교체
SoundManager.Instance.PlayBGM(FmodEvents.Instance.InGame);

// 사운드 오브젝트에서 로컬 파라미터 ID 캐싱 후 매 프레임 갱신
if (SoundManager.Instance.TryGetParameterId(engineEvent, FmodParams.Local.RPM, out var rpmId))
    _instance.setParameterByID(rpmId, currentRpm);

// 글로벌 파라미터 — 저빈도(by-name) / 고빈도(by-id)
SoundManager.Instance.SetGlobalParameter(FmodParams.Global.Environment, 1f);
if (SoundManager.Instance.TryGetGlobalParameterId(FmodParams.Global.Tension, out var tId))
    SoundManager.Instance.SetGlobalParameter(tId, tension);   // 매 프레임
```

---

## TODO / 확인 필요

- [ ] FMOD Settings `Load Banks` 를 `Specified`(Master/Strings)로 변경 (현재 `All`)
- [ ] `FmodEvents` / `FmodParams` 의 주석 처리 항목을 실제 이벤트·파라미터 이름으로 채우기
- [ ] `SceneAudioLoader` 씬↔뱅크 매핑 테이블 인스펙터 세팅
- [ ] `FmodEvents` / `SoundManager` / `SceneAudioLoader` 를 부트스트랩 씬 GameObject에 부착
- [ ] VCA/Bus 경로(`vca:/BGM` 등)가 FMOD Studio 프로젝트에 실제 존재하는지 확인
