# PLAN — 옵션창(모달) 열림 시 Player 입력 차단 매니저

- **브랜치**: `feature/ui-input-gate` (base: `feature/dash-soul`)
- **작업 폴더/레인**: `C:\UnityProject\MainProject-WorkTree`
- **위임 대상**: Codex (CoopAgent)
- **목표**: UI 모달(옵션창 등)이 열려 있는 동안, 로컬 Player가 마우스/키보드 입력(이동·공격·스킬·대시·마우스조준)을 받지 않도록 하는 상태 관리 매니저 신설.

## 확정된 요구사항 (grill 결과)
1. **차단 범위**: 입력 **전체 차단** (`SetInputEnabled(false)` 수준 — 이동 포함 전부).
2. **모달 일반성**: **여러 모달 지원** — reason 토큰 카운터. 여러 모달이 겹쳐 열려도 마지막 하나가 닫힐 때만 입력 복원.
3. **시간정지 없음**: 입력만 차단. `Time.timeScale`은 건드리지 않음(넷코드 co-op — 서버/타 플레이어는 계속 진행).
4. **로컬 오너 한정**: 각 클라이언트의 모달은 그 클라의 로컬 Player 입력만 차단(`Player.IsMovementAuthority` 가드).
5. **비클로버**: 기존 `PlayerLifeInputPolicy`(생명주기 게이트)와 충돌 금지. 죽은/소울 상태에서 옵션창 닫아도 입력이 되살아나면 안 됨.

## 현황 파악 (읽은 코드)
- 입력 진입점 `PlayerInputReader`(신 Input System, `PlayerInput` 컴포넌트). 이미 `SetInputEnabled(bool)`(이동+전투+대시 전부 차단), `SetCombatInputEnabled(bool)` 보유.
- 선례 `PlayerLifeInputPolicy`: per-Player 컴포넌트가 lifecycle 이벤트를 듣고 `inputReader`를 게이팅. `Player.IsMovementAuthority`로 로컬 오너 가드.
- 인게임 옵션창은 이미 존재: `MapSceneManager`의 `optionPanel`, `OpenOptionPanel()`(Esc/버튼). 단 `SetOptionPanel(bool)`이 private → **닫기 경로를 훅으로 잡기 어려움** → 아래 GameObject 기반 방식 채택.
- Player.prefab: `Assets/2.Prefabs/Player/Player.prefab`.

## 설계 (기존 패턴 답습)
### 신규 파일
1. **`Assets/1.Scripts/Managers/UiInputGateManager.cs`** — 정적(static) 매니저.
   - `HashSet<object>` 로 활성 block reason 토큰 보관.
   - `Acquire(object token)` / `Release(object token)` — 멱등(set 추가/제거).
   - `bool IsInputBlocked => count > 0`.
   - `event Action<bool> BlockedChanged` — 0↔1 전이 시에만 발화.
   - GameObject/Inspector 필드 불필요 → 씬 배치 없음.

2. **`Assets/1.Scripts/UI/UiModalBlocker.cs`** — 모달 패널 GameObject에 붙는 컴포넌트.
   - `OnEnable` → `UiInputGateManager.Acquire(this)`
   - `OnDisable` → `UiInputGateManager.Release(this)`
   - **누가 어떻게 SetActive(true/false) 하든** 자동 감지 → `SetOptionPanel`이 private여도, 닫기 버튼이 패널을 직접 끄더라도 안전. reason 토큰 = `this`.

3. **`Assets/1.Scripts/Player/PlayerUiInputPolicy.cs`** — Player.prefab에 붙는 per-Player 컴포넌트 (`PlayerLifeInputPolicy` 미러링).
   - `UiInputGateManager.BlockedChanged` 구독(OnEnable)/해제(OnDisable), `Start`/구독 시 현재 상태 즉시 적용.
   - `Player.IsMovementAuthority` 일 때만 `inputReader.SetUiInputSuppressed(blocked)` 호출.
   - 참조는 `GetComponent`로 self-resolve → 프리팹엔 컴포넌트만 추가.

### 수정 파일
4. **`Assets/1.Scripts/Player/PlayerInputReader.cs`** — UI 억제 레이어 추가 (비클로버 핵심).
   - 필드 `private bool uiInputSuppressed;` 추가, `public void SetUiInputSuppressed(bool suppressed)` 추가.
   - `private bool EffectiveInputEnabled => inputEnabled && !uiInputSuppressed;`
   - 공통 헬퍼 `ApplyInputState()`: `playerInput.enabled = EffectiveInputEnabled;` + 비활성 시 `Direction = Vector2.zero;` — `SetInputEnabled`/`SetUiInputSuppressed` 둘 다 이 헬퍼 경유 → 서로 덮어쓰지 않음.
   - 게이트 참조 교체: `CanReadCombatInput`, `DashPressed`, `Update()`의 `inputEnabled` → `EffectiveInputEnabled`.
   - 기존 `inputEnabled`(생명주기/오너), `combatInputEnabled` 의미/호출부는 보존. UI 억제는 독립 3번째 축.

### Unity 배선 (Codex가 프리팹/씬 편집)
- `PlayerUiInputPolicy` → `Assets/2.Prefabs/Player/Player.prefab` 에 컴포넌트 추가.
- `UiModalBlocker` → MapScene의 **OptionPanel** GameObject에 추가. (여유되면 **WarningMessage_Panel** 에도 동일 추가 — 카운터 일반성/일관성)

## 완료 조건
- [ ] 컴파일 에러 0.
- [ ] 옵션창 열림 → 로컬 Player 이동/공격/스킬/대시/마우스조준 전부 무입력. 닫힘 → 정상 복원.
- [ ] 죽은/소울 상태에서 옵션창 열고 닫아도 입력이 되살아나지 않음(생명주기 게이트 우선).
- [ ] 여러 모달 겹침 시 마지막 닫힘에서만 복원(카운터 동작).
- [ ] 원격 Player/서버 영향 없음(로컬 오너 한정), `Time.timeScale` 미변경.

## 미결/가정
- MapScene 외 씬 옵션창은 이번 범위 밖. 동일 패턴으로 `UiModalBlocker`만 붙이면 확장 가능.
- 넷코드 MPPM 2인 검증은 컴파일 통과 후 사용자/후속 단계.
