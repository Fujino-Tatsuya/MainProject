# 핸드오프 — 적 공격 범위표시 C3 패키지 (경석 담당)

> 2026-10-19 · 작성: Claude(claude-alt).
> 디자인 쪽에서 만든 **적 공격 범위표시 C3 시험 구현**을 몬스터 담당(경석)에게 넘기는 요약이다.
> 처음엔 문서로만 전달했으나(`feature/SkillIndicatorC3`), **할 일 57 에서 `feature/EnemyIndicatorC3` 에 몬스터 스크립트까지 적용했다**
> — 적용 내역·되돌리기는 [§8 적용 완료](#8-적용-완료-할-일-57)를 볼 것.
> 🔴 **2026-10-20 셰이더 재개발(할 일 57 S1~S5)**: 패키지의 런타임 CPU 텍스처 굽기를 셰이더로 바꿨다 — 지금 구조·성능·되돌리기는
> [§9 셰이더 재개발](#9-셰이더-재개발-할-일-57-s1s5)이 원본이다. §4~§5 의 "텍스처 생성·캐시"는 패키지 원본 설명이다.
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
| 신규 | `Monster/EnemyC3Visuals.cs` | JSON 파싱·텍스처 생성·캐시·점프 뷰 (**할 일 57 이후 텍스처 생성·캐시 없음** — 셰이더 값 넣기, §9) |
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
| 텍스처 캐시 | 첫 생성 프레임 지연·장시간 전투 메모리. 캐시 상한/풀링은 정식 반영 전 검토 → **할 일 57 셰이더 재개발로 해소**(런타임 텍스처 생성 0, §9) |
| 검증 범위 | 컴파일 0 에러, 선택값 일치 검사, 기존 테스트 25개 통과(TelegraphStyle/MonsterSkyStrike/BossElectricFloorPattern). **최종 설정 15종 Play 캡처 없음, MPPM·빌드 미검증**. 동봉 PNG 는 생성 텍스처이지 Play 화면이 아니다 |
| 범위 밖 | 조준 레이저·몸 크로스헤어·부위 발광·방향 링·플레이어 스킬 표시·SVN VFX 원본 |

## 6. 이번 스킬 범위표시 교체와의 관계

| 질문 | 답 |
|---|---|
| 같은 셰이더·재질을 쓰나? | **아니오.** 스킬 쪽은 `SkillLineIndicator.shader`·`SkillLineRange.mat`·`SkillRangeIndicator.mat`(+신규 `SelectedTargetCircleC.mat`) 만 교체했다. 적 쪽은 C3 전용 셰이더(`SG_EnemyC3Decal`·`Monster/EnemyC3Plate`, 할 일 57 — §9)와 Telegraph 재질·셰이더를 쓰고, 스킬 표시 파일을 참조하지 않는다 |
| 겹치는 파일이 있나? | 없음. 스킬 쪽은 `Player/Skill/Targeting/` 2개 + `Player.prefab`, 적 쪽은 `Monster/` 6개 + 신규 3개 |
| 디자인 언어 | 같은 C3 계열(흰 외곽선 + 내부선 + 쐐기). 스킬 = 민트(#c0f5de), 적 = 붉은/보라 계열 |
| 순서 의존 | 없음. 어느 쪽을 먼저 병합해도 된다 |

## 7. 통합 뒤 확인 (README 프로그래머 확인 항목)

- [ ] 호스트/클라 양쪽에서 위치·방향·최대 범위, 예고 종료/취소/사망 시 해제
- [ ] 돌진(일반/레이지/스피너) 핀 고정·최소/최대 경로, 어퍼 합집합 경계
- [ ] 그랩 90° 부채꼴만 표시·안쪽 채움
- [ ] 점프/스매시 바닥 높이·반경·페이드·그로기 종료, 박격포/드론 착탄 시간
- [ ] 전기 칸 경고→발동→점멸→해제, 타일 중첩
- [ ] ~~첫 텍스처 생성 지연, 장시간 캐시 메모리~~(할 일 57 셰이더화로 해당 없음 — 대신 프로파일러 스파이크가 사라졌는지, §9.11), 재실행/씬 전환 정리
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
| `GauntletBot.cs` | `LateUpdate` 의 스매시 예고 색칠 루프 — `TelegraphStyleSO.interrupt.fill` 파랑으로 SpriteRenderer 색을 바꾸던 것 → `EnemyC3Visuals.ApplySmash(go)` (sprite 를 C3 그림으로 교체 — 할 일 57 S3 이후 에디터에서 미리 구운 PNG `Resources/Telegraph/EnemyC3_Smash09`) | 카운터 창 판정·`_smashTintVersion` 재칠 조건·슬로우 모션 인터페이스(`_interruptSuccessCount`) |
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
(이 표는 `637091b9` 시점이다. 셰이더 재개발 뒤 검증은 §9.)

### 8.4 되돌리기

- 🔴 **먼저 §9.10 의 셰이더 재개발 커밋(R → S5 → S1)을 역순으로 되돌린 뒤**에 아래를 한다 — S1~S5 가 `637091b9` 위에 쌓여 있어 바로 revert 하면 충돌한다.
- **이번 적용만 통째로**: `git revert 637091b9` (신규 3개 삭제 + 6개 연결 원복이 한 커밋에 들어 있다).
- **연결만 끄고 신규 파일은 남기기**: 6개 파일에서 `EnemyC3Visuals.` 호출 hunk 만 되돌린다(`git diff 637091b9~1 637091b9 -- <파일>` 의 반대). 패키지 `rollback/*.cs.txt` 는 `6ed043d8` 기준이라 **23호·건틀렛에는 덮어쓰면 안 된다**(경석 변경이 사라진다). 나머지 4개는 rollback 본과 원래 동일했다.
- **신규 3개 제거**: 연결 hunk 를 먼저 되돌린 뒤 `EnemyC3Visuals.cs`·`Editor/EnemyC3Validation.cs`·`Resources/Telegraph/EnemyC3Selected.json` 을 meta 와 함께 삭제(순서를 바꾸면 컴파일 에러).

### 8.5 남은 확인 — 사람이 Play 로 (§7 체크리스트와 같음)

- [ ] 23호: 훅/어퍼(토글 켤 때)·잡기 부채꼴·일반/레이지 돌진·**콤보 연결 중** 예고, 점프 착지 원 성장·해제(콤보 즉시 연결·그로기·사망 포함)
- [ ] 건틀렛 스매시 원 sprite 교체·페이드, 펀치 사각, 스피너 채찍/돌진
- [ ] 박격포·웰즈 드론 착탄 원, 전기 장판 경고→발동→점멸→해제
- [ ] MPPM 호스트/클라 양쪽 동일, ~~첫 텍스처 생성 지연·장시간 캐시 메모리~~ → 셰이더 재개발 뒤 확인 항목은 §9.11

## 9. 셰이더 재개발 (할 일 57 S1~S5)

> 2026-10-20 · 브랜치 `feature/EnemyIndicatorC3` · 작성: Claude(claude-alt). 계획 원본 = `_lane-artifacts/MainProject-WorkTree/57-shader/PLAN-57-enemy-c3-shader.md`(레포 밖).
> 겉모습·피해 범위·시간·서버 권한·RPC·애니 이벤트·간파 판정은 그대로이고, **그리는 방법만** CPU 텍스처 → GPU 셰이더로 바뀌었다.

### 9.1 왜

- 패키지의 `EnemyC3Visuals.Bake` 는 384×384 텍스처를 **CPU 로 픽셀마다** 계산했고, 예고가 처음 뜨는 RPC 경로에서 돌았다.
  사용자 프로파일(Work1): `WellsDroneAttack.ClientLock → PaintLockCircle` 한 프레임 **4,237ms**(Deep Profile), 일반 프로파일 `MonsterBase.Update(TwentyThree)` **181ms** 스파이크.
- `Decal()` 캐시 키에 크기(F4)가 들어가 돌진처럼 길이가 바뀌면 매번 다시 굽고, 해제 없이 576KB 씩 쌓였다.
- 수용 기준(PLAN-57 §6): **런타임 텍스처 생성 0**(정적 검사로 못박음), 23호 돌진·드론 고정·점프 **첫 호출** 각 1ms 미만(Stopwatch, 에디터), 예고 1회당 GC 0(가능한 범위). 충족 여부는 §9.4.

### 9.2 지금 구조

| 무엇 | 파일 (`Assets/` 기준) | 하는 일 |
|---|---|---|
| 공용 모양 식 | `1.Scripts/Rendering/Telegraph/EnemyC3Sdf.hlsl` | 예전 `Bake` 픽셀 함수(Box·Segment·Polygon·Fins·CircleFins·부채꼴·합집합 SDF·Over·테두리/채움 합성)를 HLSL 로 옮긴 것. AA = 화면→p 야코비안의 최대 특잇값(`EnemyC3Aa`, 회전 불변 — 할 일 57 R) |
| 바닥 판 셰이더 | `1.Scripts/Rendering/Telegraph/EnemyC3Plate.shader`(`Monster/EnemyC3Plate`) + `Resources/Telegraph/MA_EnemyC3Plate.mat` | 원·사각 판 12·14·15·16, 점프 06·07. 재질 한 장 공유 + `(id, 사각, 채움)` 별 `MaterialPropertyBlock` 캐시(`EnemyC3Visuals.PaintPlate/PaintLayer/PaintActive`). renderQueue 3100·블렌드는 `TelegraphQuad` 와 같다 |
| 데칼 셰이더 | `1.Scripts/Rendering/Telegraph/SG_EnemyC3Decal.shadergraph` + `EnemyC3Decal.hlsl` + `Resources/Telegraph/MA_EnemyC3Decal.mat` | `SG_TelegraphDecal` 복제 그래프에서 Sample Texture 대신 Custom Function(`EnemyC3DecalShade`). 색·마스크 합성은 기존 `TelegraphMask.hlsl`(`TelegraphShade`) 그대로. `SG_TelegraphDecal`·`TelegraphMask.hlsl` 은 **무수정** |
| 판·조준 코드 모양 | `1.Scripts/Rendering/Telegraph/TelegraphQuad.shader` 의 `_CodeShape` | `BossPatternVisuals` 가 예전에 런타임으로 굽던 64²/128² 흰 텍스처(사각·원판·조준)를 셰이더에서 재현. `_CodeShape.x` 0 = 기존 `_MainTex`(기본값 — 재질 에셋 무수정), 1 사각 · 2 원판 · 3 조준, `y` = 테두리 비율(0.005 단위) |
| 스매시 09 | `Resources/Telegraph/EnemyC3_Smash09.png`(+meta) | SpriteRenderer 라 sprite 가 필요 → 에디터에서 **미리 구운 PNG**. 메뉴 `Tools/Enemy C3/Bake smash 09 sprite`(`Monster/Editor/EnemyC3SmashSpriteBaker.cs`), PPU 18.3605 = 384/(원본 sprite 폭 × `OuterScale("09")`), 중심 피벗·FullRect·무압축 sRGB. `ApplySmash` 는 할당만 |
| 에디터 비교 기준 | `1.Scripts/Monster/Editor/EnemyC3BakeReference.cs` | 예전 C3 CPU `Circle/Square/Decal`(+캐시) — 픽셀 비교 기준·스매시 PNG 굽기·`EnemyC3Validation` 전용. **런타임에서 부를 수 없다** |
| 에디터 비교 기준 | `1.Scripts/Monster/Editor/LegacyDecalTextureReference.cs` | 57 이전 비-C3 굽기 원본(`BuildArcTexture`·`BuildDiscTexture`·`BuildSquare`·`BuildDisc`·`BuildCrosshair`) — 픽셀 비교 기준 전용 |
| 비교용 셰이더 | `1.Scripts/Monster/Editor/EnemyC3DecalCompare.shader`(`Hidden/EnemyC3DecalCompare`) | pass 0 = 예전 텍스처, pass 1 = 그래프가 부르는 바로 그 `EnemyC3DecalShade` |

`SG_EnemyC3Decal` 모드(`_C3Shape.x`) — 값은 전부 `EnemyC3Visuals` 가 재질 벡터로 넣는다(색은 감마 `SetVector`):

| x | 모양 | 넣는 함수 | 쓰는 곳 |
|---|---|---|---|
| 0 | 부채꼴 외곽선(반각 ≥ 179.9 = 원) | `SetDecalProperties` | `BossAttackConeTelegraph` 03(잡기)·끝점 부채꼴 |
| 1 | 띠 외곽선 + 쐐기 | `SetDecalProperties` | 01·04·05·08·10·11 |
| 2 | 합집합(띠 + 끝 원) 외곽선 + 쐐기 | `SetDecalProperties` | 02(어퍼·돌진 합집합) |
| 3 | 부채꼴 채움 / 잡기 고리(`w` = 안쪽 반경), `y` = 후방 호 | `SetArcFill` | 공격 부채꼴 채움·잡기 · `BossDirectionIndicator` 앞/뒤 호 |
| 4 | 띠 채움(1.5 텍셀 페더) | `SetRectFill` | 경로 띠 채움(길이는 프로젝터 크기가 자란다) |
| 5 | 합집합 채움(`w` = 전선 m, 25단계) | `SetUnionFill` | 합집합 채움 |
| 6 | 원판 채움(채움 알파 + 테두리) | `SetDiscFill` | `AoeTelegraph` 코드 원판(`shapeTexture` 없을 때) |

- 모드 3~6 은 예전 256²/128² 텍스처를 **텍셀 중심 식 → 8비트 → 이중선형(Clamp)** 으로 재현한다(겉모습 동일 목표).
- `BossAttackConeTelegraph`·`BossDirectionIndicator` 는 프로젝터마다 재질 인스턴스 **1개**(예전과 같은 수)를 만들고, 크기·각도·진행이 바뀌면 **값만** 다시 쓴다.
  공격 예고(할 일 57 R 부터)·방향 표식·`AoeTelegraph` 의 인스턴스는 `EnemyC3Visuals.NewDecalInstance(source, color)` 가 원래 재질의 renderQueue·`_DrawOrder`·데칼 설정(Normal_Blend·각도 페이드·메시 바이어스)을 복사한다. 공격 예고는 그 위에 조각별 순서(외곽선 renderQueue +1·`_DrawOrder` 1)만 덮는다.

### 9.3 사용자 결정 10-20 (사실 기록)

- PLAN 원래 범위는 C3 경로(§2 의 15종)였다. 정적 검사(§9.5)를 "예외 0" 으로 만들려면 **57 이전부터 있던 비-C3 런타임 굽기**도 없애야 했고, 사용자가 2026-10-20 에 그 셰이더화를 승인했다.
- 그래서 몬스터 담당(경석) 영역인 다음 경로도 셰이더로 바뀌었다: `BossAttackConeTelegraph` 의 **채움**(부채꼴·띠·합집합·잡기 고리 — S4a), `AoeTelegraph` 코드 원판·`BossDirectionIndicator` 앞/뒤 호(S4c1), `BossPatternVisuals` 판·조준 기본 텍스처(S4c2).
- 보스 FSM·판정·RPC·애니 이벤트·간파·재질 에셋·프리팹·SO 는 바뀌지 않았다. 아트 텍스처 경로(`AoeTelegraph.shapeTexture`, `WellsDroneAttack` 의 `crosshairTexture`)는 그대로다 — 비어 있을 때만 코드 모양.

### 9.4 성능 (에디터 측정, 2026-10-20)

측정 도구 = 메뉴 `Tools/Enemy C3/Measure shader telegraph cost`(`Monster/Editor/EnemyC3TelegraphPerfProbe.cs`, EditMode — Play 진입 없음).
결과 원본 = `C:\UnityProject\_lane-artifacts\MainProject-WorkTree\57-shader\S5\perf.txt`·`perf.tsv`(i9-13900KF · RTX 4060 Ti · Unity 6000.3.16f1).

- **콜드** = 정적 캐시 비움(JSON·MPB·재질 참조) + 새 오브젝트에서 첫 호출. 표의 첫 행 콜드는 **도메인 리로드 직후 첫 호출**(JIT 포함).
- **예열 후** = 같은 오브젝트에 2회 부른 뒤. GC = 예열 후 200회 연속 호출의 관리 힙 증가 ÷ 200.
  (`GC.GetAllocatedBytesForCurrentThread` 는 Unity Mono 에서 항상 0 이라 쓰지 않았다. 힙 증가는 4KB 블록 단위라 1회 값은 거칠고, 연속 평균이 기준이다.)
- 🔴 **GPU 첫 드로우(셰이더 변형 첫 사용·GPU 컴파일)는 측정 밖**이다 — Stopwatch 밖(렌더 스레드)이라 이 표에 없고, 따로 재지 않았다.

| 경로 | 콜드 1회 | 콜드 10회 평균 | 예열 후 1회 | 예열 후 10회 평균 | 예열 후 GC B/회 |
|---|---:|---:|---:|---:|---:|
| 23호 돌진 `Show`(합집합 02, 같은 크기) | 42.29 ms¹ | 6.37 ms | 0.175 ms | 0.163 ms | 2,662² |
| 23호 돌진 `Show`(합집합 02, **길이 변경** — 예전 캐시 미스 경로) | 2.63 ms³ | 2.38 ms | 0.181 ms | 0.185 ms | 2,662² |
| 띠 `Show`(01, 길이 변경) | 2.71 ms³ | 2.36 ms | 0.079 ms | 0.078 ms | 369⁴ |
| `RebuildUnionOutline` / `RebuildBandOutline` / `RebuildConeOutline` | 0.28 / 0.27 / 0.53 ms | 0.22 / 0.22 / 0.25 ms | 0.005 / 0.011 / 0.005 ms | 0.005 / 0.011 / 0.005 ms | 0 |
| `RebuildUnionFill` / `RebuildInwardConeFill` | 0.11 / 0.06 ms | 0.013 / 0.007 ms | 0.0009 / 0.0005 ms | 0.0009 / 0.0004 ms | 0 |
| 합집합 진행 단계 변경 1회(`ApplyFill`) | 0.10 ms | 0.012 ms | 0.0011 ms | 0.0010 ms | 0 |
| 웰즈 드론 `ClientLock`(→`PaintLockCircle`→`PaintPlate("14")`) | 7.87 ms⁵ | 1.19 ms | 0.020 ms | 0.017 ms | 0 |
| `PaintLockCircle` / `PaintPlate("14")` | 0.30 / 0.30 ms | 0.27 / 0.25 ms | 0.004 / 0.004 ms | 0.004 / 0.004 ms | 0 |
| 점프 `CreateJump`(→`EnemyC3JumpView.Begin`) | 2.95 ms⁵ | 0.70 ms | 0.117 ms | 0.099 ms | 143⁶ |
| `EnemyC3JumpView.Begin`(오브젝트 준비 후) | 0.51 ms | 0.42 ms | 0.074 ms | 0.079 ms | 61⁶ |
| 보스 방향 표식 `ApplyDecal`(크기·각도 변경) | 0.21 ms | 0.031 ms | 0.010 ms | 0.010 ms | 0 |
| `BossPatternVisuals.Paint(CodeShape)` | 0.57 ms | 0.059 ms | 0.0012 ms | 0.0009 ms | 0 |
| [참고] 예전 CPU 굽기 `EnemyC3BakeReference.Circle("14")` | 1,196.7 ms | 1,184.2 ms | 1,178.7 ms | 1,187.2 ms | 643,072 |
| [참고] 예전 CPU 굽기 `EnemyC3BakeReference.Decal`(합집합 02) | 397.1 ms | 393.0 ms | 393.8 ms | 386.8 ms | 604,843 |

1. 도메인 리로드 직후 첫 호출 — JIT·정적 생성자·프로젝터 6개와 재질 인스턴스 6개 생성(`EnsureBuilt`) 포함. 그 뒤 콜드는 2.4ms 안팎(대부분 `EnsureBuilt` — 보스 컴포넌트 하나에 한 번).
2. **C3 값 넣기에서 나오는 할당이 아니다.** 합집합 `Show` 는 부채꼴·띠 프로젝터 4개를 켰다가 같은 호출에서 끈다(57 이전부터의 흐름) — 그 4개를 켜기→끄기만 해도 2,703 B/회(측정 행 `Show 내부: 프로젝터 4개 켜기→끄기`). URP `DecalProjector` 의 OnEnable/OnDisable 비용이며, 빌드에서도 같은지는 미확인. 고치려면 `Show` 흐름 변경이라 이번 범위(동작 변경 금지) 밖 — 후속 후보.
3. 정적 캐시 비움 + 새 컴포넌트 — `EnsureBuilt` 포함. 예전엔 같은 경로가 384² 굽기(위 참고 행, 수백 ms)였다.
4. `Place` 의 바닥 탐색(`GroundProbe`) — 약 370~390 B/회. 57 과 무관한 기존 코드.
5. 도메인 리로드 직후 + `EnsureView`(판 3장 생성). 콜드 2회째부터 1.2ms(웰즈)·0.7ms(점프).
6. 점프 뷰는 매번 새 GameObject 를 만든다(57 이전 VFX 카탈로그 경로도 생성했다) — 그 래퍼 할당. 텍스처·재질은 만들지 않는다(§9.5 결정적 테스트).

**수용 기준(PLAN-57 §6 "첫 호출 각 1ms 미만")**:
- **콜드 첫 호출 — 미충족.** 23호 돌진 `Show` **42.29 ms** · 웰즈 드론 `ClientLock` **7.87 ms** · 점프 `CreateJump` **2.95 ms**(에디터 도메인 리로드 직후 첫 호출, JIT·정적 생성자·`EnsureBuilt`/`EnsureView` 오브젝트 생성 포함). 정적 캐시만 비운 콜드도 2.4~2.7ms(`Show`)로 1ms 를 넘는다.
- **예열 후 첫 호출 — 충족**(최대 0.181ms, 합집합 길이 변경 `Show`). 단, 이것은 PLAN 기준(첫 호출)이 아니다.
- **GPU 첫 드로우는 측정 밖**(위) — 빌드에서 셰이더 변형 첫 사용 비용이 더해질 수 있다.
- 남은 일 = 스폰/씬 준비 때 `EnsureBuilt`·`EnsureView`·재질·셰이더 워밍을 미리 해 첫 호출을 예열 후 수준으로 내리는 **예열 방안 — 사용자 결정 대기**(아직 코드 없음).

예고 1회당 GC(예열 후) — C3 값 넣기 경로(Rebuild*·`ApplyFill`·`PaintPlate`·`PaintLockCircle`·`ClientLock`·`ApplyDecal`·`Paint(CodeShape)`)는 **0 B**, 0 이 아닌 곳은 위 주 2·4·6(C3 와 무관한 기존 흐름·오브젝트 생성). 예전 CPU 굽기 대비 예열 후 `PaintPlate("14")` 는 약 30만 배, 합집합 외곽선은 약 7만 배 싸다(에디터 기준). 사용자 프로파일 수치(4,237ms Deep / 181ms)는 빌드·Play 환경이라 직접 비교 대상이 아니다 — Play 프로파일 재확인은 §9.11.

### 9.5 재발 감시·결정적 테스트

| 테스트 (`Monster/Editor/`) | 무엇을 막나 |
|---|---|
| `EnemyC3RuntimeTextureGuardTests`(6) | `Monster/**`·`Rendering/Telegraph/**` 의 Editor 밖 `.cs` 에서 Texture2D 생성·`SetPixel(s/s32/Data)`·`Sprite.Create` 금지(위반 파일:줄·메서드 출력). **예외 목록 0**(S4c2 확정) — 다시 넣으려면 사용자 결정, `NoRuntimeExceptions` 가 막는다. 할 일 57 R: 작은 렉서로 주석·문자열(일반·verbatim·보간 — 구멍은 코드)·문자 리터럴을 지운 뒤 **파일 전체**를 검사 → `UnityEngine.`/`global::` 정규화 이름·`using` 별칭·Texture2D 로 선언된 변수·필드·프로퍼티·반환의 target-typed `new()`·줄바꿈 생성식·닫힌 블록 주석 뒤 코드·`using static` Sprite 도 잡고, 문자열 속 금지어는 무시(`ScannerCatchesBypasses` 자기검사). 알려진 한계: 의미 분석이 없어 인자 위치의 target-typed `new()`(`Foo(new(1,1))`)는 못 본다(Roslyn 은 에디터 참조 불확실로 미사용) |
| `EnemyC3TelegraphInstanceTests`(6) | 예열 후 `Show`(합집합·띠·부채꼴·잡기 ×20 + 진행 단계)·`PaintPlate/PaintActive/Paint(CodeShape)` ×20·`CreateJump` ×10(+파괴)·`ApplySmash` ×20 을 되풀이해도 재질·Texture2D·Sprite 인스턴스 수가 늘지 않는다. 타이밍 단언은 넣지 않는다(머신마다 흔들림 — 시간은 §9.4 메뉴로). 할 일 57 R: 공격 예고 6조각이 예고 재질(`MA_TelegraphDecal`, 그리고 `MaterialOverride` 경로)의 renderQueue(+조각 순서)·`_DrawOrder`·Normal_Blend·각도 페이드·메시 바이어스를 물려받는지 |

### 9.6 픽셀 비교 테스트 (예전 CPU 그림 vs 셰이더, 384² RT, 기준 = 채널 오차 ≤ 2/255 픽셀 99% 이상)

| 테스트 (`Monster/Editor/`) | 단계 | 건수 | 결과 |
|---|---|---|---|
| `EnemyC3PlateShaderTests` | S1 | 6 id × 테두리/채움 × 크기 3 = 36 | 원 테두리 99.257%(최대 5/255) · 원 채움 99.984% · 사각 100% |
| `EnemyC3DecalShaderTests` | S2 | id 8종 × 크기 3 = 24 + 그래프 함수 연결 1 | 전부 ≥ 99.998% |
| `EnemyC3SmashSpriteTests` | S3 | PNG vs CPU `Circle("09")` + 화면 크기·피벗 | 100%(최대 0) |
| `EnemyC3FillShaderTests` | S4a | 부채꼴 3 · 잡기 고리 10 · 띠 1 · 합집합 18 = 32 + 런타임 동작 3 | 100%(최대 1/255) |
| `EnemyC3MarkerDiscShaderTests` | S4c1 | 방향 표식 12 + 원판 3 + 재질 전제 1 + 런타임 2 | ≥ 99.67%(표식 최대 4/255, 원판 최대 8/255) |
| `BossPatternPlateShaderTests` | S4c2 | 원판 5 · 사각 4 · 조준 2 · 마스크 2 = 13 + 기본 경로 3 | 100%(최대 1/255) |

| `EnemyC3DecalRenderTests` | R | **실제 `SG_EnemyC3Decal` 데칼 렌더**: 외곽선 3(띠·부채꼴·합집합) + 채움 5(부채꼴·고리·띠·합집합·원판) × DBuffer = 8, ScreenSpace 2, 기법 확인 1 | 전부 100%(최대 2/255) |

나란히 PNG(예전|셰이더|차이×16)·`results.tsv` = `_lane-artifacts/MainProject-WorkTree/57-shader/<단계>/`(테스트가 그 폴더가 있을 때만 남긴다). 렌더 비교는 `57-shader/R/render/`(맨바닥|텍스처 데칼|C3 데칼|차이×16).

- 위 S1~S4c2 비교는 비교용 셰이더(`Hidden/EnemyC3DecalCompare`)로 **같은 hlsl 함수**를 부를 뿐이라, 그래프 연결(Custom Function → BaseColor·Alpha·NormalAlpha)·Decal SubTarget·패스 설정은 보지 못한다. 그건 `EnemyC3DecalRenderTests` 가 본다: EditMode 프리뷰 씬에 Lit 바닥 + `DecalProjector` + 정사영 카메라를 두고 프로젝트 렌더러(`PC_Renderer`, Decal 기능)로 RT 에 찍어, 같은 바닥·같은 프로젝터에 **기존 텍스처 데칼**(`MA_TelegraphDecal` + 예전 CPU 텍스처)을 찍은 것과 비교한다. 데칼이 실제로 보이는지(맨바닥과 다른 픽셀 ≥ 1%)도 단언한다.
- 기법: 프로젝트 설정은 Automatic → Windows 데스크톱 = **DBuffer**(`ProjectUsesDBufferDecals` 가 못박음). ScreenSpace 는 `DecalRendererFeature` 설정을 **메모리에서만** 잠깐 바꿔 찍고 되돌린다(애셋 파일 무변경). GBuffer(디퍼드)는 프로젝트가 Forward+ 라 안 본다.

### 9.7 알려진 차이 (화면상 무차이로 추정 — Play 확인 필요)

- **원 테두리 99.26%**: CPU 굽기(고정 384 해상도, aa = extent/384)와 화면 미분 AA 의 차이가 동심원 줄무늬처럼 테두리 가장자리에 몰린다(최대 5/255).
- **데칼 알파 1/255 반올림**: 셰이더가 Bake 텍스처처럼 알파를 1/255 단위로 맞춘다 — 반올림 경계에서 1/255 차이.
- **원판 최대 8/255**(`AoeTelegraph` 코드 원판): 알파 양자화 순서 차이가 투명 sRGB RT 의 저알파 가장자리에서 확대된 값.
- 실제 화면은 화면 해상도로 그려 CPU 384² 보다 가장자리가 더 매끈할 수 있다(PLAN R2, 허용).
- **AA 폭(할 일 57 R)**: `EnemyC3Aa` = 화면→p 야코비안의 최대 특잇값. 원근·경사로 비등방이면 거친 축 기준이라 경계가 조금 더 부드럽고(계단 없음 쪽), 모양을 돌려도 폭이 같다. 축 정렬이면 예전 식과 같은 값이라 위 픽셀 비교 결과는 R 전후 동일(`57-shader/R/tsv-compare.txt`). 데칼 쪽 `max(extent)/64` 상한(깊이 불연속 대응)은 그대로.

### 9.8 프리셋을 바꿀 때

- `Resources/Telegraph/EnemyC3Selected.json` 이 정본이다. 바꾸면 **다음 Play 부터** 반영된다(`EnemyC3Visuals.ResetForPlay`) — 셰이더 경로는 다시 굽을 것이 없다.
- 🔴 **스매시 09 값(색·기하·`smashShowMarkers`)을 바꾸면 PNG 를 다시 굽는다**: 메뉴 `Tools/Enemy C3/Bake smash 09 sprite` → `Assets/Resources/Telegraph/EnemyC3_Smash09.png` 커밋(meta guid 는 그대로여야 한다). 안 구우면 `EnemyC3SmashSpriteTests` 가 실패해 알려 준다.
- 검사: `Tools/Enemy C3/Validate selected preset` → `Tools/Enemy C3/Run existing telegraph tests`.

### 9.9 커밋

| 단계 | 커밋 | 내용 |
|---|---|---|
| S1 | `dff34d8b` | `EnemyC3Sdf.hlsl` + `EnemyC3Plate` — 바닥 판 12·14·15·16·06·07 |
| S2 | `f01990a7` | `SG_EnemyC3Decal` + `EnemyC3Decal.hlsl` — 공격 데칼 외곽선 01~05·08·10·11 |
| S3 | `e7f92c18` | 스매시 09 미리 구운 PNG, 런타임 `Sprite.Create` 제거 |
| S4a | `11c0fa1e` | 공격 예고 채움(부채꼴·띠·합집합·잡기 고리) 셰이더화 |
| S4b | `298f8991` | CPU Bake 를 에디터 전용 `EnemyC3BakeReference` 로 이동, 정적 검사 추가 |
| S4c1 | `bad1edea` | 방향 표식 데칼·`AoeTelegraph` 코드 원판 셰이더화 |
| S4c2 | `d83f9817` | `BossPatternVisuals` 판·조준 → `TelegraphQuad` `_CodeShape`, 정적 검사 예외 0 |
| S5 | `2663e6a0` | 성능 측정 메뉴·결정적 테스트·이 절 |
| R(A·B) | `c037f68a` | 공격 예고 데칼 인스턴스 `NewDecalInstance` 로(데칼 설정 보존), `EnemyC3Aa` 회전 불변(최대 특잇값) |
| R(C·D) | `1844dd71` | 런타임 텍스처 정적 검사 렉서 기반 강화, 실제 데칼 렌더 비교 `EnemyC3DecalRenderTests` |
| R(E) | 메시지 `할 일 57 R — 문서` | 이 절의 커밋·되돌리기·수용 기준 정정 |

### 9.10 되돌리기

- 🔴 **Unity 를 닫고** 한다(여러 에셋·셰이더 그래프·`.meta` 를 한 번에 다시 쓴다 — `CLAUDE.md` 6번). 끝나면 `.meta` guid 가 그대로인지 확인.
- **커밋 단위로, 최신부터 역순**: R 문서(`할 일 57 R — 문서`) → `1844dd71` → `c037f68a` → S5 `2663e6a0` → `d83f9817` → `bad1edea` → `298f8991` → `11c0fa1e` → `e7f92c18` → `f01990a7` → `dff34d8b`. 각 단계 뒤 컴파일 0 에러가 정상이다.
  - 가운데 하나만 revert 하면 충돌하거나 컴파일이 깨진다(예: S4b 가 Bake 를 Editor 로 옮겼고 S4a·S4c 테스트가 그 위에 있다).
  - S4c2 까지만 되돌리면 `BossPatternVisuals` 런타임 굽기가 돌아오고 정적 검사 예외 목록도 함께 복원된다(같은 커밋). S4b 를 되돌리면 정적 검사 테스트 자체가 사라진다.
  - S1 까지 다 되돌리면 `637091b9` + `72cbefba` 상태(패키지 CPU 굽기, §9.1 스파이크 재발)다. C3 자체를 빼려면 이어서 §8.4.
- 부분 되돌리기(아트 텍스처로만 바꾸기 등)는 하지 않는 것을 권한다 — 셰이더와 호출부가 같은 커밋에 묶여 있다.

### 9.11 사람이 Play 로 확인할 것 (S1~S5 전체)

- [ ] S1 박격포 12·웰즈 드론 14 착탄 원(테두리·쐐기 4개·채움 성장), 전기 장판 15 경고 칸 → 16 발동 점멸 — 예전과 같은 색·두께
- [ ] S1 23호 점프 06 경계 원 + 07 성장 원 — 크기·성장·해제(콤보·그로기·사망)
- [ ] S2 23호 훅(토글 켤 때)·어퍼 합집합 02·잡기 03·일반 돌진 04·레이지 돌진 05 외곽선·쐐기, 건틀렛 펀치 08, 스피너 채찍 10·돌진 11
- [ ] S2 경사·단차·프롭 위 데칼 투영과 캐릭터·벽 제외(리시버) — 예전과 같은지
- [ ] S3 건틀렛 스매시 09 원 크기·피벗·페이드(FadeInHoldEffect)·외곽선 불투명도 1
- [ ] S4a 공격 채움 — 부채꼴 성장, 띠 길이 성장, 합집합 25단계 전선, 잡기 바깥→안 고리
- [ ] S4c1 보스 방향 표식 앞/뒤 호(색·알파 0.85·점프 중 숨김), `AoeTelegraph` 원판(23호 차징 오라 — `shapeTexture` 가 비어 있으면 코드 원판)
- [ ] S4c2 웰즈 조준(코드 조준·아트 조준 둘 다), 전기 장판·판 테두리 두께, 마스크 켠 스타일
- [ ] S5 프로파일러: 23호 돌진·웰즈 드론 고정·점프 첫 예고에서 스파이크(예전 181ms·Deep 4,237ms)가 사라졌는지, GC Alloc 열
- [ ] MPPM 호스트/클라 양쪽 동일, Play 재시작·씬 전환 뒤에도 같은 그림(정적 캐시 초기화)
- [ ] §9.7 알려진 차이(원 테두리 줄무늬·데칼 알파·원판 가장자리)가 화면에서 보이지 않는지
- [ ] R 경사·원근 시점에서 판·데칼 외곽선 가장자리 폭이 모양 방향(돌진 방향)에 따라 달라지지 않는지(`EnemyC3Aa` 최대 특잇값)
- [ ] R 첫 예고 프레임 히치(콜드 첫 호출 §9.4 미충족 + GPU 첫 드로우) — 예열 방안 결정의 근거로
