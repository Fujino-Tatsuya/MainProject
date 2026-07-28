# PLAN: MovingPlatform (이동플랫폼)

> 브랜치: `MovingPlatform` (feature/PlayerSkill에서 분기, 2026-07-21 승인)
> 그릴 완료 후 승인된 플랜.

## 목표

MapScene 비의존 **자기완결 프리팹**으로 이동 플랫폼 제작 → 맵 제작자가 드래그 배치해 재사용.
MPPM 멀티에서 검증. 테스트씬은 별도 담당(내가 만들지 않음).

## 확정 결정 (그릴 결과)

| 항목 | 결정 |
|---|---|
| 배치 | 씬 오소링. 부모(컨트롤러) + 자식(큐브 플랫폼·웨이포인트) 프리팹 |
| 형태 | 플레이스홀더 큐브(아트 후순위). 나르는 대상 = 플레이어만 |
| 경로 웨이포인트 | 자식 Transform 배열 |
| 상태 모드 | 순환(닫힌 루프 A→B→C→D→A) / 반복(핑퐁 A→B→A→B) / 경유(끝점 후 역순 왕복 A→B→C→B→A). enum + 오소링 고정 |
| 가동 | 상시 |
| 이동 프로파일 | 사다리꼴: `cruiseSpeed`(m/s) + `acceleration`(m/s²), 감속거리 자동. 평행이동만(회전 없음) |
| 정지시간 | 웨이포인트별 override + 전역 기본값 |
| 캐리 방식 | (b) 소유자측 델타 주입 → `PlayerMovement.MoveRoot()`. 락스텝(계수=1) |
| 캐리 코드 위치 | Player.cs 안에 직접 |
| 탑승 판정 | 소유자측 중심 발밑 1점 검사. 라이더 판정 콜라이더 > 가시 큐브 |
| 동기 모델 | **결정론**: 위치 = f(공유시계). NetworkTransform 없음. MovingPlatform은 NetworkObject 아님 |
| 공유 시계 | `NetworkClock` (NetworkManager.prefab 부착), pause-aware |
| 게임 상태 | GameManager에 enum{Title, Lobby, Loading, MainGame, Result} 추가. MainGame 진입 시 MainGameStart 스탬프 |
| 일시정지 | 멀티 = 없음 / 솔로(host)만 → NetworkClock.Pause |

## 아키텍처 (책임 분리)

### 1. `NetworkClock.cs` (신규, NetworkManager.prefab 부착)

- 공유 서버시간의 단일 소스. 기존 소유 클래스 없음(greenfield).
- 노출: `ServerNow`(raw ServerTime passthrough), `GameNow`(pause-aware), `MainGameElapsed`(MainGame 시작 이후 pause-aware 경과).
- 상세 타임스탬프 수집: `SessionFormedAt`, `MainGameStartedAt` 등 (서버 권위, 전원에 배포).
- `Pause()/Resume()` — 솔로 host만 사용.
- 서버가 타임스탬프를 정하고 CustomMessaging으로 전원 배포(NetworkLoadingFlowController와 동일 패턴). late joiner 없음(F4)이라 1회 배포로 충분.
- 기존 `StatusEffectController`는 raw `ServerTime` 직접 사용 중 → 의미변화 우려로 **이전하지 않음**. NetworkClock이 raw도 노출하므로 추후 교체 여지만 남김.

### 2. `GameManager`(TempGameManager.cs) 확장

- `enum GameState { Title, Lobby, Loading, MainGame, Result }` + `CurrentState`.
- 전환 지점에서 상태 세팅.
- MainGame 진입 훅: 로딩 플로우가 MapScene 활성화 완료 시 → `CurrentState=MainGame` + `NetworkClock.MarkMainGameStart()`.

### 3. `MovingPlatform.cs` (프리팹 컨트롤러)

- 평범한 MonoBehaviour, NetworkObject 아님.
- 위치 = `f(NetworkClock.MainGameElapsed)` 순수함수 → 모든 클라 동일 계산으로 일치. NetworkTransform/서버스폰 불필요.
- Awake에 웨이포인트 + 프로파일로 **타임라인 테이블 프리컴퓨트**(구간별 가감속·정지 누적시간). 매 프레임 `t_mod → 위치` 조회. **적분(dt 누적) 금지 → FP 드리프트 방지.**
- 노출: `Vector3 CurrentDelta`(이번 프레임 이동량) + 라이더 판정 콜라이더.

### 4. Player.cs 캐리

- 발밑 1점 검사 → 올라탄 MovingPlatform의 `CurrentDelta`를 `MoveRoot()`에 가산.
- 생사 무관(시체도 실림). 하차 = 라이더 콜라이더 이탈 시 캐리 중단.

## 구현 순서

1. `NetworkClock` + GameManager GameState + MainGame 진입 훅
2. `MovingPlatform` 컨트롤러 + 프리팹(큐브 + 웨이포인트 + 라이더 콜라이더). 3모드·프로파일·정지
3. Player.cs 캐리 배선
4. 솔로 일시정지 → NetworkClock.Pause 연결
5. `dotnet build` 0오류 → (사용자 테스트씬에서) MPPM 2인 동시탑승 + 3모드 검증

## 리스크 / v1 제외

- FP 결정론: 반드시 순수함수 f(t), dt 누적 금지.
- 하차 관성 → 대시 이후.
- 압사·플랫폼간/몬스터 충돌·late joiner/재접속 → v1 없음.
- 수직 플랫폼 → v1 수평 전용.

## 완료 조건

`dotnet build` 0오류 + MPPM 2인 동시 탑승 + 3모드 시연.

## 딜리버러블

프리팹 + `MovingPlatform.cs` + `NetworkClock.cs` + GameManager/Player.cs 편집. 테스트씬은 사용자.
