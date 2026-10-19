# 핸드오프 — 적 공격 범위표시 C3 패키지 (경석 담당)

> 2026-10-19 · 작성: Claude(claude-alt).
> 디자인 쪽에서 만든 **적 공격 범위표시 C3 시험 구현**을 몬스터 담당(경석)에게 넘기는 요약이다.
> 처음엔 문서로만 전달했으나(`feature/SkillIndicatorC3`), **할 일 57 에서 `feature/EnemyIndicatorC3` 에 몬스터 스크립트까지 적용했다**
> — 적용 내역·되돌리기는 [§8 적용 완료](#8-적용-완료-할-일-57)를 볼 것.
> 패키지 원본은 사용자가 별도로 전달한다. 상세(경로별 주의·되돌리기·웹 편집기 사용법)는 패키지 README 가 원본이다.

## 1. 한 줄 요약

피해 범위·시간·서버 권한·RPC·쿨다운·애니 이벤트는 **그대로**, 바닥 예고의 **외형(색·이중 경계·쐐기 텍스처)만** 바꾸는 시험 구현.
정본 = 팔레트 3개(일반/간파/발동) + 개별 override 8개(01·02·04·05·08·09·10·11) + 연결된 표시 15개.

## 2. 대상 15종

| ID | 표시 | 쐐기(최종) | 적용 경로 |
|---|---|---|---|
| 01 | 23호 좌·우 훅 사각 예고 | 끔 | BossAttackConeTelegraph |
| 02 | 23호 어퍼 경로 + 끝 원 | 끔 (켜면 직선 구간만) | BossAttackConeTelegraph 합집합 |
| 03 | 23호 잡기 · 안쪽으로 채우는 부채꼴 | 없음 | BossAttackConeTelegraph 부채꼴 |
| 04 | 23호 일반 돌진 · 경로 띠 | 켬 · 크기100/간격75/시작69 | BossAttackConeTelegraph |
| 05 | 23호 레이지 돌진 · 즉시 채운 띠 | 켬 · 크기100/간격75/시작74 | BossAttackConeTelegraph |
| 06 | 23호 점프 · 고정 착지 경계 원 | 4개 · 앵커 81.4% | TwentyThreeBoss RPC → EnemyC3JumpView |
| 07 | 23호 점프 · 차오르는 착지 원 | 성장층(경계층 06 쐐기 유지) | 같은 점프 뷰 성장층 |
| 08 | 건틀렛 펀치 · 좌우 사각 예고 | 끔 | BossAttackConeTelegraph |
| 09 | 건틀렛 스매시 · VFX 원 | 4개 · 앵커 81% · 외곽선 불투명도 1 | GauntletBot → 재생 SpriteRenderer 의 sprite 교체 |
| 10 | 스피너 채찍 · 사각 예고 | 끔 | BossAttackConeTelegraph |
| 11 | 스피너 돌진 · 간파 경로 사각 | 켬 · 크기100/간격75/시작69 | BossAttackConeTelegraph |
| 12 | 박격포 · 고정 착탄 원 | 4개 · 앵커 81.4% | MonsterSkyStrike 바닥 판 |
| 14 | 웰즈 드론 · 잠긴 착탄 원 | 4개 · 앵커 81.4% | WellsDroneAttack |
| 15 | 23호 전기 장판 · 경고 칸 | 없음 | BossElectricFloor 경고/성장 판 |
| 16 | 23호 전기 장판 · 발동 칸 | 없음 | BossElectricFloor 발동 판 (기존 점멸 alpha 배율만 적용) |

ID 13 은 없다(패키지 원본 번호 그대로).

## 3. 팔레트 (`HEX / 불투명도`)

| 요소 | 일반 공격 | 간파 가능 공격 | 발동 영역 |
|---|---|---|---|
| 외곽선 | #ffffff / 0.95 | #ffffff / 0.95 | #ffc3b5 / 0.95 |
| 내부선 | #bc8178 / 1 | #b982c4 / 1 | #bb726a / 1 |
| 외곽 채움 | #ed8f82 / 0.25 | #d29ef5 / 0.25 | #e96b62 / 0.25 |
| 진행 채움 | #ed8f82 / 0.232 | #d29ef5 / 0.232 | #e96b62 / 0.232 |
| 쐐기 | #ed8f82 / 0.42 | #d29ef5 / 0.42 | #e96b62 / 0.42 |

- 개별 override 가 그룹보다 우선. 스매시 09 는 외곽선 불투명도만 1.
- **색과 간파 판정은 별개** — 붉게 바꿔도 간파 여부는 안 바뀐다.
- 공통 기하: 외곽선 3.5 · 안쪽선 2.5 · 선 간격 2 · 외곽 채움 시작 57% · 직선 쐐기 크기 100%.

## 4. 패키지가 건드리는 파일

| 구분 | 파일 | 비고 |
|---|---|---|
| 기존 수정 (patch) | `Monster/Boss/BossAttackConeTelegraph.cs` | 01~05·08·10·11 데칼 |
| | `Monster/Boss/BossElectricFloor.cs` | 15·16 |
| | `Monster/Boss/GauntletBot.cs` | 09 — **패키지 기준 이후 development 에서 변경됨** |
| | `Monster/Boss/TwentyThreeBoss.cs` | 06·07 점프 — **패키지 기준 이후 크게 변경됨(+471/−83, 2026-10-19 재확인)** |
| | `Monster/Boss/WellsDroneAttack.cs` | 14 |
| | `Monster/MonsterSkyStrike.cs` | 12 |
| 신규 | `Monster/EnemyC3Visuals.cs` | JSON 파싱·텍스처 생성·캐시·점프 뷰 |
| 신규 (Editor) | `Monster/Editor/EnemyC3Validation.cs` | `Tools > Enemy C3 > …` 검증·기존 테스트 실행 메뉴 |
| 신규 | `Assets/Resources/Telegraph/EnemyC3Selected.json` | 런타임용 15개 펼친 설정 |

- 경로는 `Assets/1.Scripts/` 기준. 신규 3개는 patch 에 없다 — meta 와 함께 따로 복사.
- 패키지 patch 기준 HEAD 는 `6ed043d8`. 그 뒤로 `GauntletBot.cs`·`TwentyThreeBoss.cs` 가 바뀌었다(2026-10-19 확인) → **`git apply` 대신 diff 를 읽고 수동 통합**했다(§8).
- 신규 3개 meta guid(`acdcdb11…`·`727428fd…`·`b711abdb…`)는 현재 프로젝트와 충돌 없음(2026-10-19 확인).
- prefab/scene 배선 변경 없음. 기존 Telegraph 재질·셰이더(`MA_TelegraphDecal`·`MA_TelegraphQuad`·`SG_TelegraphDecal`·`TelegraphQuad.shader`)에 그대로 의존.

## 5. 주의·미검증 (README 에서 추림)

| 항목 | 내용 |
|---|---|
| 수치 단위 | 간격75·시작69 등은 **웹 시안 좌표**. 화면 px·월드 m 아님. 기준(사각 폭200, 돌진 폭114, 어퍼 폭130, 원 반경165, 칸 한 변300)으로 실제 도형 크기에 맞춰 환산 |
| 플레이어 수치 복사 아님 | 플레이어 직선의 .96m/2m/2.6m 를 적에 그대로 쓴 것이 아니다 |
| preset 선택 방식 | 몬스터 종류·팔레트·도형으로 preset ID 를 **추론**하는 시험 방식. 공격 ID 를 명시 전달할지는 담당자 결정 |
| 스타일 소스 | C3 경로는 `TelegraphStyleSO` 의 색·마스크 대신 JSON 색을 쓴다. 기존 마스크 미적용 — 편집 소스 혼동 주의 |
| 점프 | 카탈로그 VFX(Drop_Charge_Boundary/Indicator) 대신 EnemyC3JumpView 생성. 경사면 투영 안 됨. 정식 VFX 체계와 통합할지 결정 필요 |
| 스매시 | SVN 프리팹은 안 고치고 재생된 SpriteRenderer 의 sprite 만 교체. 프리팹 구조가 바뀌면 대상 재확인 |
| 그랩 | 반경 8m/90°·바깥→안쪽 채움. 각도 밖 원형 잔선 수정본 — 거리 계산을 전체 원 반경 차이로 되돌리면 재발 |
| 프리셋 반영 | JSON 갱신 후 **Play 재시작** 필요(캐시). 실행 중 즉시 반영 없음 |
| 텍스처 캐시 | 첫 생성 프레임 지연·장시간 전투 메모리. 캐시 상한/풀링은 정식 반영 전 검토 |
| 검증 범위 | 컴파일 0 에러, 선택값 일치 검사, 기존 테스트 25개 통과(TelegraphStyle/MonsterSkyStrike/BossElectricFloorPattern). **최종 설정 15종 Play 캡처 없음, MPPM·빌드 미검증**. 동봉 PNG 는 생성 텍스처이지 Play 화면이 아니다 |
| 범위 밖 | 조준 레이저·몸 크로스헤어·부위 발광·방향 링·플레이어 스킬 표시·SVN VFX 원본 |

## 6. 이번 스킬 범위표시 교체와의 관계

| 질문 | 답 |
|---|---|
| 같은 셰이더·재질을 쓰나? | **아니오.** 스킬 쪽은 `SkillLineIndicator.shader`·`SkillLineRange.mat`·`SkillRangeIndicator.mat`(+신규 `SelectedTargetCircleC.mat`) 만 교체했다. 적 패키지는 Telegraph 재질·셰이더 + 런타임 생성 텍스처를 쓰고, 스킬 표시 파일을 참조하지 않는다 |
| 겹치는 파일이 있나? | 없음. 스킬 쪽은 `Player/Skill/Targeting/` 2개 + `Player.prefab`, 적 쪽은 `Monster/` 6개 + 신규 3개 |
| 디자인 언어 | 같은 C3 계열(흰 외곽선 + 내부선 + 쐐기). 스킬 = 민트(#c0f5de), 적 = 붉은/보라 계열 |
| 순서 의존 | 없음. 어느 쪽을 먼저 병합해도 된다 |

## 7. 통합 뒤 확인 (README 프로그래머 확인 항목)

- [ ] 호스트/클라 양쪽에서 위치·방향·최대 범위, 예고 종료/취소/사망 시 해제
- [ ] 돌진(일반/레이지/스피너) 핀 고정·최소/최대 경로, 어퍼 합집합 경계
- [ ] 그랩 90° 부채꼴만 표시·안쪽 채움
- [ ] 점프/스매시 바닥 높이·반경·페이드·그로기 종료, 박격포/드론 착탄 시간
- [ ] 전기 칸 경고→발동→점멸→해제, 타일 중첩
- [ ] 첫 텍스처 생성 지연, 장시간 캐시 메모리, 재실행/씬 전환 정리
- [ ] 철판 홈·지형 가림·다른 맵·다수 VFX 중첩·색각 차이에서 간파/일반 구분

## 8. 적용 완료 (할 일 57)

> 2026-10-19 · 브랜치 `feature/EnemyIndicatorC3` · 커밋 `637091b9` · 작성: Claude(claude-alt). **경석 통보용.**
> 피해 범위·시간·서버 권한·RPC·쿨다운·애니 이벤트는 그대로이고 **바닥 예고 외형만** 바뀌었다.

### 8.1 바뀐 파일

| 구분 | 파일 (`Assets/` 기준) | 내용 |
|---|---|---|
| 신규 | `1.Scripts/Monster/EnemyC3Visuals.cs` | 패키지 그대로 복사, meta guid `acdcdb11…` 유지 |
| 신규 | `1.Scripts/Monster/Editor/EnemyC3Validation.cs` | 패키지 그대로, guid `727428fd…` 유지 |
| 신규 | `Resources/Telegraph/EnemyC3Selected.json` | 패키지 그대로, guid `b711abdb…` 유지 |
| 수정 | `1.Scripts/Monster/Boss/BossAttackConeTelegraph.cs` | 01~05·08·10·11. 결과가 패키지 변경 후 전체본과 **동일** |
| 수정 | `1.Scripts/Monster/Boss/BossElectricFloor.cs` | 15·16. 패키지 전체본과 동일 |
| 수정 | `1.Scripts/Monster/Boss/WellsDroneAttack.cs` | 14. 패키지 전체본과 동일 |
| 수정 | `1.Scripts/Monster/MonsterSkyStrike.cs` | 12. 패키지 전체본과 동일 |
| 수정 | `1.Scripts/Monster/Boss/GauntletBot.cs` | 09. 아래 8.2 |
| 수정 | `1.Scripts/Monster/Boss/TwentyThreeBoss.cs` | 06·07. 아래 8.2 |

- 위 4개(전체본과 동일)는 패키지 기준 `6ed043d8` 이후 변경이 없어 패치 hunk 를 손으로 옮긴 결과가 전체본과 같다.
- 기존 6개 `.meta`·prefab·scene·`Assets/50.Art`·`GameData.xlsx`·`TelegraphStyleSO` 와 그 에셋 값은 **안 건드렸다**.

### 8.2 23호·건틀렛 — 현재 코드에 맞춰 옮긴 지점

경석의 `6ed043d8` 이후 변경(23호 콤보 체인·점프 후딜·훅/어퍼 토글·예고 시간 `TelegraphTime`, 건틀렛 `IInterruptSlowMotionTarget`)은 **C3 hunk 와 겹치는 줄이 없었다.** 그래서 패키지 hunk 와 같은 내용을 현재 위치에 얹었고, 패키지와 다르게 바꾼 로직은 없다.

| 파일 | 바뀐 지점 | 그대로 둔 것 |
|---|---|---|
| `GauntletBot.cs` | `LateUpdate` 의 스매시 예고 색칠 루프 — `TelegraphStyleSO.interrupt.fill` 파랑으로 SpriteRenderer 색을 바꾸던 것 → `EnemyC3Visuals.ApplySmash(go)` (sprite 를 C3 텍스처로 교체) | 카운터 창 판정·`_smashTintVersion` 재칠 조건·슬로우 모션 인터페이스(`_interruptSuccessCount`) |
| `TwentyThreeBoss.cs` | 필드 `_c3JumpTelegraph` 추가 · `ShowJumpTelegraphClientRpc` 본문 = 카탈로그 루프(`Drop_Charge_Boundary/Indicator`) 대신 `EnemyC3Visuals.CreateJump(point, radius, growTime)` · `ReleaseJumpTelegraphs()` 첫 줄에서 C3 점프 뷰 파괴 | RPC 시그니처·호출 시점(서버 `BeginJumpHover` 표시 / `ApplyJumpLandingDamage`·`AbortAttackChain` 해제), 콤보·훅·돌진 로직 전부. `_boundaryHandle`/`_indicatorHandle`·`WarnNo…EntryOnce` 는 남겨 두었다(이제 쓰이지 않음 — 되돌리기 쉽게) |

**알아 둘 점 (경석 변경과의 상호작용, 코드는 안 고침):**

- `BossAttackConeTelegraph` 는 preset 을 **도형·팔레트·`growTime`** 으로 추론한다(`C3Id`). 즉시 연결 후속 훅·어퍼(`ChainedLight`)는 `growTime 0` 으로 표시를 띄우므로 **01(훅) 대신 05(레이지 돌진 · 쐐기 켬)** 로 분류된다. 지금은 `No23`·`No23_Raid` 에셋의 `hookTelegraphMarkers = 0` 이라 훅·어퍼 표시가 아예 안 떠 화면 영향이 없다. **토글을 켜면 연결 훅에 쐐기가 보일 수 있다** — 공격 ID 를 명시 전달할지 담당자 결정(§5 "preset 선택 방식").
- 같은 이유로 예고 없는 즉발 공격(`telegraphDuration 0`, 기존 경로)도 05 로 분류된다(패키지 기준에서도 같았다).
- 인터럽트 파랑(커밋 `de2df265`·`637104a1`)·공격 예고 색 조절(`3e28ddd0`)은 C3 경로에서 **JSON 팔레트로 대체**된다(간파 = 보라 `#d29ef5`). `TelegraphStyleSO` 값은 그대로라 `TelegraphStyleTests` 는 통과하지만, **그 에셋을 고쳐도 이 6개 경로의 색은 안 바뀐다** — 색은 `Resources/Telegraph/EnemyC3Selected.json` 에서 고친다.

### 8.3 검증 (2026-10-19, 에디터 · Play 미진입)

| 항목 | 결과 |
|---|---|
| 컴파일 | 에러 0 |
| `Tools > Enemy C3 > Validate selected preset` | OK — 15 매핑·개별 override 3·원 경계/알파·어퍼 쐐기·돌진 쐐기 위치·스매시 쐐기·데칼 Show/Hide |
| `Tools > Enemy C3 > Run existing telegraph tests` | 25 통과 / 0 실패 / 0 건너뜀 (TelegraphStyle·MonsterSkyStrike·BossElectricFloorPattern) |
| `Tools > Tests > EditMode 전체 실행` | 2125 통과 / 0 실패 / 20 건너뜀 |

증거 사본(생성 텍스처 PNG·결과 txt): `C:\UnityProject\_lane-artifacts\MainProject-WorkTree\enemy-c3-apply-20261019\`. 생성 텍스처이지 Play 화면이 아니다.

### 8.4 되돌리기

- **이번 적용만 통째로**: `git revert 637091b9` (신규 3개 삭제 + 6개 연결 원복이 한 커밋에 들어 있다).
- **연결만 끄고 신규 파일은 남기기**: 6개 파일에서 `EnemyC3Visuals.` 호출 hunk 만 되돌린다(`git diff 637091b9~1 637091b9 -- <파일>` 의 반대). 패키지 `rollback/*.cs.txt` 는 `6ed043d8` 기준이라 **23호·건틀렛에는 덮어쓰면 안 된다**(경석 변경이 사라진다). 나머지 4개는 rollback 본과 원래 동일했다.
- **신규 3개 제거**: 연결 hunk 를 먼저 되돌린 뒤 `EnemyC3Visuals.cs`·`Editor/EnemyC3Validation.cs`·`Resources/Telegraph/EnemyC3Selected.json` 을 meta 와 함께 삭제(순서를 바꾸면 컴파일 에러).

### 8.5 남은 확인 — 사람이 Play 로 (§7 체크리스트와 같음)

- [ ] 23호: 훅/어퍼(토글 켤 때)·잡기 부채꼴·일반/레이지 돌진·**콤보 연결 중** 예고, 점프 착지 원 성장·해제(콤보 즉시 연결·그로기·사망 포함)
- [ ] 건틀렛 스매시 원 sprite 교체·페이드, 펀치 사각, 스피너 채찍/돌진
- [ ] 박격포·웰즈 드론 착탄 원, 전기 장판 경고→발동→점멸→해제
- [ ] MPPM 호스트/클라 양쪽 동일, 첫 텍스처 생성 지연·장시간 캐시 메모리
