# PLAN — 컨베이어 벨트 (타일 기반, 플레이어/시체 캐리)

- **브랜치**: `feature/ConveyorBelt` (base: `feature/PlayerSkillAnimation`)
- **레인/폴더**: `C:\UnityProject\MainProject-WorkTree` (gate 워처)
- **위임 대상**: Codex (CoopAgent)
- **목표**: 밟은 대상을 일정 방향·속도로 밀어내는 타일 기반 컨베이어 벨트. 기존 이동 플랫폼 캐리 seam 재사용.

## 확정 요구사항 (grill 결과)
1. **동작 모델**: 가산 — 벨트 속도가 입력 이동에 더해짐(가만히=표류, 반대=감속, 순방향=가속).
2. **방향**: 타일별 `transform.forward` × 속도.
3. **영향 대상 (v1)**: 플레이어 + 시체(Corpse). **적은 v2로 분리**(서버 BT/NavMesh 얽힘, 별도 phase).
4. **통합**: 공통 인터페이스 `ISurfaceCarrier`로 캐리 seam 일반화(MovingPlatform도 구현).
5. **속도/형태**: 등속. **타일 기반**, 부모 그룹이 공용 속도 보유. 직선 + **직각 코너 타일**.
6. **코너 타일**: 타일을 대각선으로 분할, 라이더가 입구 삼각형이면 inDir, 출구 삼각형이면 outDir. `inDir`/`outDir` 직렬화.
7. **네트워크**: 벨트 무NetworkObject. 플레이어=오너측 캐리, 시체=서버측. 비오너=루트 NetworkTransform 동기. `Time.timeScale` 무변경.
8. **접지/상태**: 밟고 있을 때만(공중=없음), 스턴/대시/사망 무관 적용(플랫폼과 동일).
9. **비주얼**: 유저가 셰이더 그래프 제작. **Codex는 게임플레이 로직+컴포넌트+프리팹+방향 기즈모만**(머티리얼/셰이더 제외).
10. **테스트**: 자동 테스트 없음. Codex는 코드+샘플 프리팹. 검증은 사용자가 수동(체크리스트는 본 PLAN 하단).

## 현황 파악 (base=feature/PlayerSkillAnimation 기준)
- 캐리 seam: `Player.cs.ApplyPlatformCarry()`(오너측 Update, RaycastAll `platformRiderMask`/`platformGroundCheckDistance`) → `MovingPlatform` 발견 시 `movement.AddCarryDelta(platform.CurrentDelta)`.
- `PlayerMovement`: 입력이동 + `_carryDelta`를 단일 MovePosition으로 합산(상태 무관).
- `PlayerCorpseController`: 서버측에서 `corpseRigidbody.position += platform.CurrentDelta`.
- `PlayerGroundingSensor.IsMovingPlatform`: 접지 콜라이더 부모의 MovingPlatform 유무.

## 설계

### 신규 파일
1. **`Assets/1.Scripts/Map/ISurfaceCarrier.cs`** — 인터페이스.
   ```csharp
   public interface ISurfaceCarrier { Vector3 GetCarryDelta(Vector3 riderWorldPos, float dt); }
   ```
   - MovingPlatform: `CurrentDelta` 반환(riderPos/dt 무시).
   - ConveyorTile: `방향(riderPos) * beltSpeed * dt` 반환.

2. **`Assets/1.Scripts/Map/ConveyorGroup.cs`** — 부모 컨트롤러. 공용 `beltSpeed`(m/s, 기본 3) 보유. 자식 타일이 참조.

3. **`Assets/1.Scripts/Map/ConveyorTile.cs`** — `MonoBehaviour, ISurfaceCarrier`. 타일 콜라이더와 같은 오브젝트(또는 자식). 부모 `ConveyorGroup`에서 속도 조회(`GetComponentInParent`), 미발견 시 자체 기본값 폴백.
   - `enum TileKind { Straight, Corner }`.
   - Straight: `GetCarryDelta = transform.forward(수평화) * speed * dt`.
   - Corner: 직렬화 `inDir`/`outDir`(로컬 축 or 4방위 enum). 라이더 월드좌표를 타일 로컬로 변환 → 대각선 분할 판정으로 in/out 삼각형 선택 → 해당 방향 * speed * dt.
   - `OnDrawGizmos`로 방향 화살표 표시.

### 수정 파일
4. **`Assets/1.Scripts/Map/MovingPlatform.cs`** — `ISurfaceCarrier` 구현 추가. `GetCarryDelta(_, _) => CurrentDelta;` (기존 로직/CurrentDelta 보존).
5. **`Assets/1.Scripts/Player/Player.cs`** — `ApplyPlatformCarry`의 프로브를 `GetComponentInParent<ISurfaceCarrier>()`로 교체, `movement.AddCarryDelta(carrier.GetCarryDelta(transform.position, Time.deltaTime))`. (오너측 게이트 유지)
6. **`Assets/1.Scripts/Player/Corpse/PlayerCorpseController.cs`** — 플랫폼 캐리부를 `ISurfaceCarrier`로 교체(서버측), `corpseRigidbody.position += carrier.GetCarryDelta(corpseRigidbody.position, Time.deltaTime)`.

### Unity 배선 (Codex가 프리팹 제작)
- 자기완결 프리팹: `ConveyorGroup` 부모 + **직선 타일**·**코너 타일** 프리팹.
- 타일 콜라이더 = MovingPlatform 라이더와 동일 레이어(`platformRiderMask` ∩ 접지 마스크 포함) 배치.
- 비주얼/머티리얼/셰이더는 **유저 담당**(방향 기즈모만 Codex).

## 완료조건 (컴파일)
- [ ] 컴파일 에러 0.
- [ ] 벨트 NetworkObject 없음, `Time.timeScale` 무변경.
- [ ] 기존 MovingPlatform 캐리 동작 회귀 없음(인터페이스 전환 후에도 동일).

## 수동 검증 체크리스트 (사용자 Unity/MPPM)
- [ ] 직선 벨트 위 **가만히** → 벨트 방향으로 등속 표류.
- [ ] 벨트 **반대로** 걷기 → 느려짐 / **순방향** → 빨라짐 (가산).
- [ ] 벨트에서 **벗어나면** 즉시 표류 정지.
- [ ] **코너 타일**: 입구 방향으로 진입 → 대각 경계 넘으면 출구 방향으로 90° 라우팅(좌/우회전 각각).
- [ ] **공중**(벨트 위 점프/낙하 중)에는 밀림 없음.
- [ ] **스턴/대시/사망** 중에도 밟고 있으면 밀림(플랫폼과 동일).
- [ ] **시체**가 벨트 위에서 표류(서버 시뮬).
- [ ] **MPPM 2인**: 오너/비오너 모두 위치 일치(오너 캐리 + 루트 NT 동기, 이중적용/어긋남 없음).
- [ ] 벨트 + 이동플랫폼 인접 배치 시 밟은 표면 기준 정상 동작(첫 히트 우선).

## 미결/가정
- 적(Enemy) 캐리 = **v2 별도 phase**.
- 곡선/가변속/경사 벨트 = 범위 밖.
- 코너 대각 분할의 정확한 부등식은 구현 시 확정(의도: 입구 삼각형=inDir, 출구 삼각형=outDir).
