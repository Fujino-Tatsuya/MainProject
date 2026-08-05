# PLAN — 플로팅 데미지 (feature/FloatingDamage)

> 상태: **구현 완료 · Unity/MPPM 검증 대기**. Codex(`fd` 레인) 구현 및 정적 컴파일 통과.
> 설계 근거: [Docs/tech/floating-damage-design.md](../tech/floating-damage-design.md)
> 워크트리 `C:\UnityProject\MainProject-WorkTree` / 브랜치 `feature/FloatingDamage` (base `Convayor-V2` c256a5d21)
> 레포 루트의 `PLAN.md`는 별건(개발 진입점 단일화, 승인 대기)이라 건드리지 않는다.

## 범위

플레이어·몹·보스가 **받은** 피해를 월드스페이스 숫자로 표시한다. 판정 코드는 건드리지 않는다(읽기 전용 소비만 추가).
힐·실드·상태이상 팝업은 **계약만** 뚫고 미구현. 크리티컬은 도입하지 않는다.

## 작업 단계

### 1단계 — Unit 시임 확장 (판정 무수정)
- `Unit`에 델타 포함 이벤트 추가: `event Action<int amount, DamageChannel channel> ClientDamagedAmount`
  - [Unit.cs:474-482](../../Assets/1.Scripts/Unit/Unit.cs:474) `OnHpReplicated` / `OnShieldReplicated`에서 `previous - next`를 실어 발생
  - 기존 `ClientDamaged`는 **시그니처·동작 유지** ([HitFlash.cs](../../Assets/1.Scripts/Unit/HitFlash.cs) 무수정)
- [Unit.cs:456](../../Assets/1.Scripts/Unit/Unit.cs:456) `OnNetworkSpawn`에서 `FloatingDamagePresenter` 자동 부착 (HitFlash와 동일 조건 — 없을 때만)
- 완료조건: 컴파일 통과, HitFlash 동작 변화 없음

### 2단계 — 데이터 계약 + SO
- `PopupKind { Damage, Heal, ShieldDamage, Status, Text }`
- `struct FloatingPopupRequest { Unit target; PopupKind kind; int amount; bool fromLocalPlayer; }` — 단일 진입점
- `FloatingDamageSettings` (SO)
  - 표시 필터 enum: `AllDamage`(기본) / `OwnDealtOnly` / `AllWithOwnEmphasis`
  - `stayTimeout` 0.3 / `animateDuration` 0.5 / `fadeDuration` 0.3
  - 산포 각도 ±35°, 초기 속도, 중력
  - 동시표시 상한 32
  - `PopupKind`별 색·폰트 크기 테이블 (코드 분기 없이 확장)
- `.asset` 기본 인스턴스 생성

### 3단계 — 팝업 본체 (상태머신)
`FloatingDamagePopup` — Active / Animating / FadingOut
- **Active**: 대상 오버헤드 체력바 옆 월드 오프셋에 **위치 고정**, 들어오는 피해 **합산**, 스케일 펀치. 무피격 `stayTimeout` 경과 시 이탈
- **Animating**: SO 산포 각도 내 랜덤 방향 이동(중력 적용), **색이 점점 어두워짐**. `animateDuration` 후 이탈
- **FadingOut**: 이동 감쇠 + 알파 0 → 풀 반납
- Animating/FadingOut 중 새 피해 → **새 Active 팝업 생성**(기존은 자기 수명대로 종료)
- 빌보드: `LateUpdate`에서 `Camera.main` 회전 추종 ([UnitOverheadHealthBar.cs:31](../../Assets/1.Scripts/UI/Combat/UnitOverheadHealthBar.cs:31)과 동일 방식)
- 프리팹 `Assets/2.Prefabs/UI/FloatingDamagePopup.prefab` (World-space Canvas + TMP_Text)

### 4단계 — 스포너 + 프레젠터
- `FloatingDamageSpawner` — 씬 상주 1개, `UnityEngine.Pool.ObjectPool<T>` 풀, 상한 초과 시 최고령 반납, **대상+채널별 Active 팝업 레지스트리**
- `FloatingDamagePresenter` — `Unit.ClientDamagedAmount` 구독 → 스포너 요청, 필터 판정
  - **고정 규칙: 대상이 로컬 플레이어면 표시하지 않는다**
- 완료조건: 호스트 단독에서 몹을 때리면 숫자가 체력바 옆에 누적되고, 손을 떼면 튀어나가 어두워지며 사라진다

### 5단계 — 공격자 식별 RPC 경로
- 필터가 `OwnDealtOnly` / `AllWithOwnEmphasis`일 때만 서버가 히트별 `ClientRpc`로 (대상, 피해량, 공격자 ClientId) 전송
- `AllDamage`(기본)에서는 이 RPC가 **전혀 발생하지 않을 것**
- 완료조건: 필터 3값 각각 의도대로 동작, 기본값에서 추가 트래픽 0

### 6단계 — 씬 배선 + 검증
- `4.MapScene`에 `FloatingDamageSpawner` 배치
- MPPM 2인(호스트+클라): 상대가 몹을 때린 숫자가 내 화면에도 뜨는지 / 내가 받은 피해 숫자는 뜨지 않는지
- 논리 단위로 커밋

## 완료조건 (전체)
1. 몹·보스·원격 플레이어가 받은 피해가 월드 숫자로 표시된다
2. 연타 시 Active 팝업 하나에 합산되고, 무피격 0.3초 후 튀어나가며 어두워지고 페이드로 사라진다
3. 로컬 플레이어 자신이 받은 피해는 숫자로 뜨지 않는다
4. 기본 필터에서 신규 RPC 트래픽이 0이다
5. `HitFlash`·기존 HUD 동작 변화 없음, 판정 코드(`ApplyHealthDamage` 계열) 무수정
6. 힐·상태이상은 `PopupKind` 값과 요청 진입점만 존재(미구현)하고, 추가 시 코드 분기 증식이 필요 없다

## 하지 않을 것
- 크리티컬 판정 도입 / 데미지 계산식 변경
- 로컬 추정 경로(설계 문서 §3 C안)
- Screen-space 투영 방식, VFX Graph 방식
- 판정·네트워크 권위 구조 변경
- 레포 루트 `PLAN.md` 수정

## 구현 결과 (2026-08-05)

- 1~6단계 코드, 기본 Settings 에셋, 월드스페이스 TMP 프리팹, `4.MapScene` 배선을 완료했다.
- `AllDamage`는 기존 NetworkVariable 델타만 소비하며, 공격자 식별 RPC는 다른 두 필터에서만
  호출되도록 서버 가드했다.
- 원거리 기본 공격 투사체도 공격자를 잃지 않도록 `AttackHitContext.sourceUnit` 선택 메타데이터를
  추가했다. 이는 판정에 사용하지 않고 표시 필터에서만 읽는다.
- Unity 6000.3.16f1 컴파일 응답 파일과 현재 패키지 참조로 전체 런타임 C#을 컴파일해
  **0 errors**를 확인했다.
- 이 환경의 Unity Licensing Client IPC 실패로 에디터 임포트 및 MPPM 검증은 수행하지 못했다.
  Unity 에디터에서 프리팹/씬 직렬화 확인 후 완료조건의 MPPM 2인 시나리오를 실행해야 한다.
