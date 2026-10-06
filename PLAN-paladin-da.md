# PLAN-paladin-da — 팔라딘 평타 전진 복구 · 공격속도 ×1.2 · 간파 선딜 0.25

> 상태: **✅ 승인** (D3 0.2 → 0.25 수정 후) (2026-10-07 · 은희(Claude))
> 브랜치: `fix/PaladinDAForward` (`development` = `feature/Assassin` = `3217a052` 에서 분기 — 파일 변화 0 이라 Unity 켠 채 전환 가능)

## 1. 결정 (2026-10-07 채팅 확정)

| # | 항목 | 결정 |
|---|---|---|
| D1 | 평타 전진 방식 | **루트모션 재저작**. 지금 클립의 **디딘 발 기준 자연 보폭**으로 타마다 새로 계산(옛 1.34~1.77m 복원 아님). 아트 쪽도 재저작으로 판단 |
| D2 | 공격속도 | **×1.2**. 데이터 테이블 수치로 노출 — 값 하나가 평타 4타 재생 속도를 런타임에 올린다(클립 파일은 안 건드림) |
| D3 | 간파(단죄의 방패) 선딜 | `hitDelay` **0.15 → 0.25** |

## 2. 현재 상태 (근거)

- 클립 4개 = `Assets/4.Animations/Player/Garen/Garen_Default_Attack_1~4.anim`(Humanoid, `RootT`·`LeftFootT`·`RightFootT` 커브).
- `98c522b8`(09-21) 이 1초로 리타임하면서 **전진 커브를 지우고** 디딘 발을 다시 만들었다. `KeepOriginalPositionXZ`·`LoopBlendPositionXZ` 0 → 1.
- `cbf4bfdd` 가 이동 방식만 `AnimationRootMotionProjected` 로 되돌림 → 릴레이(`PlayerRootMotionRelay` → `DefaultAttackController.HandleAnimatorMove`)는 살아 있지만 받을 델타가 0.
- 평타 상태 = `Assets/4.Animations/Player/PlayerAnimatorController.controller`(`Paladin_Armature`·`Paladin_VFX`·Legacy 가 사용) `Default_Attack0~3`, 속도 1, 속도 파라미터 없음. 코드에도 공격속도 개념 없음.
- 간파 상태 `Interrupt` = `Garen_Parry.anim` — **Hit 이벤트 없음** → 판정은 `PlayerInterruptSkillBase` 의 `HitDelay` 타이머만으로 정해진다(값만 바꾸면 반영).
- 수치 원본 = `GameData.xlsx`(SVN) `Paladin` 시트. `DefaultAttackData`(Order 0)·`FirstMeleeInterruptSkillData`(Order 4). 빌드는 항상 xlsx 값.

## 3. 접근

### C1 공격속도 (코드 + 컨트롤러)
- `DefaultAttackData` 에 `attackSpeed`(기본 1, Min 0.1) 추가. 클래스가 이미 `[DataTableSheet("Paladin")]` 라 자동 노출. 팔라딘 SO 값 = 1.2.
- 컨트롤러에 float 파라미터 `AttackSpeed` 추가, `Default_Attack0~3` 의 Speed Multiplier 를 그 파라미터로.
- `DefaultAttackController` 가 공격 시작 때(서버·오너·프록시 전 피어, 같은 SO 값) `AttackSpeed` 를 넣는다.
- 클립 이벤트(Hit·콤보 창·End)는 재생 속도를 따라 자동으로 1.2배 빨라진다. 루트모션 거리는 그대로(빨리 같은 거리).
- 시간 기반 값 보정: `attackEndFallbackTime`(End 누락 안전망) = `MotionDuration / attackSpeed`. `ScriptedForwardDistance` 속도도 같은 기준(팔라딘은 안 쓰지만 일관성).
- `loopBackEntryTime` 은 `CrossFadeInFixedTime` 오프셋(초)으로 들어간다 — 상태 속도가 오프셋에 곱해지는지 Unity 에서 확인하고, 아니면 `/ attackSpeed` 보정.

### C2 루트모션 재저작 (에디터 메뉴 + 클립 4개)
메뉴 `Tools/Player/Paladin/평타 루트모션` 두 개:
1. **분석(읽기 전용)** — Paladin 아바타에 클립을 60fps 로 샘플(루트모션 끔). 프레임마다 **디딘 발**(높이 임계값 안 + 수평 속도 최소) 을 고르고, 그 발이 월드에서 멈춰 있도록 하는 몸의 전진량을 누적 → 타별 총 거리·구간 그래프를 콘솔에. 🔴 **이 숫자를 은희가 보고 OK 한 뒤 2 진행**(게이트).
2. **쓰기** — 누적 전진량을 `RootT.z` 에, 같은 양을 `LeftFootT.z`·`RightFootT.z` 에도 더해(몸 기준 자세 불변) 저장. `KeepOriginalPositionXZ`·`LoopBlendPositionXZ` = 0(XZ 루트모션을 델타로 내보냄). Y·회전 베이크와 이벤트는 그대로.
- `movementType` 은 이미 `AnimationRootMotionProjected` — SO 수정 없음. 투영 방향 = `attackDirection`, 벽은 기존 `PlayerMotor` 스윕.

### C3 간파 선딜
- `FirstMeleeInterruptSkillData.asset` `hitDelay` 0.15 → 0.25. `skillDuration` 0.6 은 그대로(판정 < 종료).

### C4 xlsx (SVN)
- SVN update(`--non-interactive --accept postpone`) → Get Lock → 데이터 테이블 **병합 Export**(새 `attackSpeed` 행) → `Paladin` 시트 `attackSpeed`=1.2 · `hitDelay`=0.25 → Verify 0 → SVN 커밋 → `Docs/tech/art-svn.json` 핀 갱신.
- ⚠️ Export 는 xlsx 서식을 날린다(data-table.md). 커밋 전 diff 확인.

## 4. 범위 밖
- 공격속도를 스탯/모디파이어(버프로 오르는 공격속도)로 만드는 것 — 지금은 캐릭터 데이터 값 하나.
- 거너·어쌔신 평타(`DefaultAttackController` 아님), 간파 클립 속도, `skillDuration`.

## 5. 리스크
- **크로스페이드 0.05s 동안 루트모션 버림**(`UpdateRootMotionTransitionGuard`) → 시작 직후 전진이 조금 덜 됨. 분석 단계에서 그 구간 전진량이 크면 보고.
- 루프백 진입(`loopBackEntryTime` 이후부터 재생)은 앞 구간 전진을 건너뛴다 — 설계상 의도, 거리 조금 짧음.
- `.anim` 이 각 40만 줄 — Unity API 로만 쓴다(텍스트 편집 금지). 쓰기 뒤 `.meta` guid 불변 확인.
- 컨트롤러는 Legacy 프리팹도 공유 — 파라미터 기본값 1 이라 영향 없음.

## 6. 검증
- Refresh → 컴파일 0 → 플레이어 EditMode 전체(현재 109) 통과.
- 분석 메뉴 출력(타별 거리) 을 이 문서 §7 에 기록.
- 은희 Play(테이블 모드): ① 타마다 전진, 발이 안 미끄러짐 ② 콤보 템포 1.2배, 히트·콤보 창 어긋남 없음 ③ 루프 연타·벽 앞 ④ 간파 판정 타이밍이 0.25s ⑤ MPPM 클라 창에서 같은 거리·템포, 위치 튐 없음.

## 7. 진행 기록
- (비어 있음)
