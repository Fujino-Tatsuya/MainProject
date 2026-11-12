# PLAN — 보스 B 승리 연출 (BOSS DOWN)

> 🔶 **11-09 교체됨** — 처치 연출은 ZZZ 글리치([PLAN-boss-victory-glitch.md](PLAN-boss-victory-glitch.md))로 바뀌고, 이 문서의 카메라 무빙·주황·회색 원·프리셋·슬라이더 코드는 각 `Legacy/` 로 옮겨 연결을 끊었다. 아래는 기록.

> 상태: ✅ 경석 승인(2026-10-27 — Q1~Q3 권장안, R1~R6 문서대로) → ✅ 구현(10-27, 미커밋) · 컴파일 0 · 전투 EditMode 402/0(승리 규칙 38 추가) · ⏳ 경석 Play · ⏳ MPPM. 브랜치 `feature/SettingsDdol`(development `6135a330` 위).
> **11-06 할 일 200 1차 정리 → [§10](#10-할-일-200--엔딩-연출-1차-정리-11-06-사용자-결정--브랜치-featurebossvictoryrework1)** — 추가 연출 구간·단색 배경·이펙트 단색·색 원복/글리치/타이포/디졸브 삭제·234 벽 제한 삭제 → 벽 숨김. 앞 절과 다르면 §10 이 맞다.
> 기획 원본: `C:\Users\user\Downloads\보스_승리연출_v02_프로그래머_전달사양.md`(2026-10-26, 콘티 v02) — 이 문서의 시간·색·배치 수치를 시작값으로 쓴다.
> 레퍼런스: Zenless Zone Zero WIPEOUT 모음(https://www.youtube.com/watch?v=KfjGNVzCIdA) — 막타 → 흰 번쩍 → 낮은 카메라 정지 · 주황 배경 · 탈색 인물 · 인물 **뒤** 큰 글자.

## 1. 목표
보스 B(23호 레이드 변형, `TwentyThree_Raid → No23_B`)가 쓰러지는 순간부터 3.2초 동안, 전원이 **같은 화면**으로
막타 → 히트스톱 → 카메라 하강 → 주황 배경·무채색 인물 → `BOSS DOWN` 타이포 → 붕괴 재개를 본다.
그 뒤 32° 구도에 머문 채 보스 사망 클립·디졸브가 끝나면 지금처럼 결과 화면으로 간다.

## 2. 확정 사항 (경석 10-27 grill)
| # | 항목 | 결정 |
|---|---|---|
| D1 | 적용 보스 | **보스 B 만**(`BossVariantSelection.UseRaid` 로 스폰된 23호). A·중간보스·훈련 스파링·튜토리얼은 기존 사망 그대로 |
| D2 | 구도 기준 | **전원 같은 화면**. 막타 캐릭터 = 좌측 전경, 타격점·보스 = 중앙~우측 중경, 나머지 팀원 = 좌우 후경(문서 §2 표). ⚠️ "각자 자기 캐릭터 중심"으로 답했다가 뒤집음(경석 10-27) |
| D3 | 조작 | **처치 순간부터 잠금**(전원 입력·이동·스킬, 피해도 안 받음). 결과 화면은 로비에서 뜨고, 그걸 넘기면 기존 흐름대로 조작이 풀려 있어야 한다 |
| D4 | 3.2초 뒤 | 카메라는 **32° 종료 구도 유지**, HUD 계속 숨김 → 사망 클립·디졸브가 끝나면 기존대로 결과 화면(사라짐 + 1초). 플레이 카메라 복귀 없음 |
| D5 | 타이포 | **코드로 생성** — 프로젝트의 가장 굵고 좁은 TMP 폰트, 기울임 + 약한 회전, 청록→보라→분홍→노랑 가로 4색, 얇은 밝은 테두리 + 우하단 짙은 그림자. 문구 `BOSS DOWN` |
| D6 | 무채색 | **음영 살린 회색조** — 원래 조명의 명암을 회색으로 바꿔 어두운 쪽 `#12171C`, 밝은 쪽은 대상별 목표색(막타 `#E9E9DF` · 팀원 `#C8D1D1` · 보스 `#4D5660`) |
| D7 | 사망 팀원 | 있는 그대로(쓰러진 모습도 회색 처리). 구도 계산의 "팀원 둘"에서는 빼고 살아 있는 사람만 담는다. 관전 중인 클라도 같은 연출을 본다 |

## 3. 내가 정한 기본값 (승인 때 같이 확인)
| # | 항목 | 기본값 |
|---|---|---|
| K1 | 막타 판정 | 치명 피해를 준 플레이어. 플레이어가 아닌 피해(벤트·지속 피해 등)로 죽으면 **마지막으로 때린 플레이어**, 그것도 없으면 위협 1위 |
| K2 | 타격점 | 치명타의 타격 위치(근접 = 판정 접점, 원거리 = 투사체 명중점). 없으면 보스 몸통 높이의 막타 쪽 표면 |
| K3 | 스킵 | 없음(3.2초) |
| K4 | 사운드 | 이번엔 새 소리 없음. 히트스톱·타이포 진입·붕괴 재개 3곳에 사운드 키 칸만 두고 비워 둔다(비우면 무음 — 기존 규칙) |
| K5 | 잔여물 | 처치 확정 순간 보스 투사체·드론·전기 장판·송전탑 VFX·남은 몬스터를 정리(숨김/종료). 플레이어 투사체는 숨김 |
| K6 | 수치 위치 | 시간·색·배치는 연출 SO 에셋(인스펙터)에 모은다. xlsx 시트는 이번엔 안 만든다(콘티 조정이 끝나면 옮길지 결정) |
| K7 | 1인 | 팀원이 없으면 막타 + 보스만으로 구도. 2인이면 팀원 하나 |

## 3.5 레퍼런스 실측 (`C:\Temp\videoref.mp4`, 2560×1440 60fps, ffmpeg 프레임 추출 — 10-27)
한 건(영상 4.25s~)을 1/60초 단위로 본 흐름:
| 영상 시각(막타 기준) | 화면 |
|---|---|
| +0 | HUD 전투 화면, 막타 |
| +1프레임 | **화면 전체 즉시 회색조** + HUD 함께 소거 + **가로 띠 글리치**(스캔라인 단위로 좌우 밀림·잔상) |
| +0.07~0.2s | 카메라 **컷**(낮은 각도, 막타 캐릭터 뒤) · 회색 · **속 빈 외곽선 글자**(얇은 밝은 선)만 먼저 보임 · 원형 충격파 링 |
| +0.23s | 주황 틴트 들어옴 + 글자 속 **가운데 글자부터 바깥으로** 색이 차오름(약 0.1~0.15s) |
| +0.35s~ | 완성: 글자 좌→우 보라·파랑·하늘·분홍·빨강, 인물·팀원·적이 글자 **앞** |
| 이후 | 글자 색이 배경과 같은 연노랑으로 바래며 사라짐(알파보다 색이 먼저 빠짐) |

관찰:
- **배경은 평면 주황이 아니다** — 환경이 보이는 채로 명암을 낮추고 황토색(ochre)으로 물들임. 바닥 가까운 아래쪽은 더 회색. 옅은 가로 스캔라인.
- 인물 = 음영 살린 회색조(밝은 회색 몸체·어두운 옷). 적(대형 기계) = 중간 회색. 일부 원색 포인트(빨간 신발 등)가 약하게 남는 경우 있음.
- 글자 = 굵은 이탤릭, 화면 폭보다 넓어 양 끝 잘림, 세로 중앙 약 38~62% 띠. 이 컷에서는 테두리·그림자가 거의 안 보임.
- 레퍼런스는 **월드가 멈추지 않는다**(인물이 계속 움직이고 카메라도 조금 흐름) · 전체 약 1.2초.

### 문서 v02 와 레퍼런스가 다른 점 — 기본은 **문서대로**, 바꿀지 결정 필요
| # | 항목 | 문서 v02 | 레퍼런스 |
|---|---|---|---|
| R1 | 0.12~0.72 색 | 기존 색 유지 | 막타 직후 바로 회색조 + 가로 띠 글리치 |
| R2 | 카메라 | 0.22~0.72 부드러운 하강 | 낮은 구도로 컷 |
| R3 | 타이포 진입 | 우측에서 밀려 들어오며 두 띠 어긋남 → 정렬, 불투명도 0→1 | 제자리 외곽선 먼저 → 가운데부터 색 채움 |
| R4 | 배경 | 주황 면 단순화(상부 `#ED9B25`·바닥 `#D67A16`) | 환경이 보이는 황토 틴트, 바닥 회색 |
| R5 | 정지 | 0.12~2.00 인물·보스 정지 | 계속 움직임 |
| R6 | 타이포 소거 | 2.00~3.20 불투명도 1→0 | 색이 배경색으로 바랜 뒤 사라짐 |
→ 구현은 R4·R6 처럼 **문서가 열어 둔 부분**(배경 "디테일 약화", 소거 방식)만 레퍼런스 쪽으로 기울이고, 나머지는 문서 수치. 셰이더 강도·모드를 인스펙터에서 바꿀 수 있게 둔다.

## 4. 현재 이해 (코드 실측)
- 보스 사망: `MonsterBase.EnterDead()` → `SetState(Dead)`(사망 트리거) → `PlayDeathEffectAfterClip`(클립 2.58초 → 디졸브 2초) → 디스폰 + `ServerDeathSequenceCompleted`.
- 결과 화면: `BossEncounterDirector.HandleBossDefeated`(`Died`, 클리어 기록·안전망 12초) → `HandleBossVanished`(+1초) → `MapSceneManager.GoToResult()`.
- `Unit.Died` 에는 공격자가 없다 → 23호 `ReceiveAttack` 에서 치명타의 `GetAttackerClientId(hitContext)`·타격 위치를 따로 기록해야 한다.
- 연출 부품: `CinematicOverlay`(HUD 숨김 · Cinemachine 연출 카메라 Enter/Place/Exit · 후처리 볼륨) — 보스 등장·4구역 연출이 쓰는 것을 그대로 쓴다.
- 조작 잠금: `PlayerEncounterLock.SetCinematicInputLockServer`(보스 텔레포트가 쓰는 복제 잠금).
- 인물 마스크: `PlayerSilhouetteFeature` 가 렌더링 레이어로 플레이어만 마스크에 그리는 패턴이 있다 — 같은 방식으로 인물·보스·핵심 이펙트 마스크를 만든다.

## 5. 설계
### 5.1 진행(서버 권한 · 시간 = 서버 시각)
1. 23호 B 가 치명타를 받는 순간(서버): 막타 clientId·타격점·막타 캐릭터 위치/방향, 살아 있는 팀원 목록을 확정 → **시작 서버 시각**과 함께 `ClientRpc` 한 번(+ 늦은 합류용 NetworkVariable 스냅샷).
2. 같은 순간: 전원 `SetCinematicInputLockServer(true)` + 피해 무시, 잔여물 정리(K5), 결과 화면 안전망을 연출 길이만큼 미룬다.
3. 사망 연출 보류: 사망 클립은 0.12초에 멈췄다가 **2.00초에 재개**, 디졸브·디스폰은 그 뒤 기존대로. 즉 `PlayDeathEffectAfterClip` 앞에 "승리 연출 보류 시간"을 끼운다(보스 B 만).
4. 결과 화면: 기존 `HandleBossVanished` 그대로(+1초). 결과 → 로비로 가면 기존 흐름대로 조작이 풀린다(잠금은 씬 전환 때 정리 — 확인 항목).

### 5.2 화면(모든 피어 로컬 · 연출 시계 = 서버 시각 기준 경과, 월드 timeScale 안 건드림)
| 구간 | 처리 |
|---|---|
| 0.00 | HUD 숨김(`CinematicOverlay.HideHud`), 연출 카메라를 **지금 플레이 카메라 위치·회전·FOV 34 그대로** 켠다(튐 없음) |
| 0.12–2.00 | 정지: 플레이어 3명·보스 Animator `speed = 0`, 핵심 타격 VFX·파편 ParticleSystem `Pause`(로컬 표현만 — 네트워크 상태·판정과 분리) |
| 0.22–0.72 | 카메라: 시작 → 최종 구도로 smoothstep(`u²(3−2u)`) 보간. 주시점 = 타격점(고정) |
| 0.72–2.00 | 카메라 완전 고정(댐핑·흔들림 끔) |
| 0.72–0.80 / 2.00–2.24 | 색 전환 강도 0→1 / 1→0 (smoothstep) |
| 0.92–1.08 / 2.00–3.20 | 타이포 진입·완성 / 소거 |
| 2.00–3.20 | 정지 해제(사망 클립 재개), 카메라 위로 올라가며 후퇴 → 32° |
| 3.20~ | 32° 유지, 연출용 상태 정리(타이포·색·정지), HUD 숨김 유지 → 결과 화면 |

**최종 구도 계산(0.72초 전 확정, 전원 같은 입력이라 같은 결과):** 피치 24°, FOV 34 고정. 방위각은 "막타 캐릭터 → 타격점" 방향을 기준으로 막타가 화면 왼쪽 앞에 오도록 잡고, 팀원이 화면 밖이면 거리 → 방위각(±) 순으로 넓힌다. 보스가 막타를 완전히 가리면 방위각을 돌린다.

### 5.3 합성(URP 렌더러 기능 `BossVictoryFeature` — 강도 0 이면 아무것도 안 함)
레이어 순서 = 배경 → 타이포 → 인물·보스 → 핵심 타격 이펙트.
1. 마스크 패스: 인물(막타/팀원)·보스·핵심 이펙트를 렌더링 레이어로 골라 깊이 비교해 그린다(채널별 ID).
2. 합성 패스(전체 화면): 마스크 밖 = 주황 그라데이션(상부 `#ED9B25` → 바닥 `#D67A16`) + 그 위에 타이포 RT, 마스크 안 = 원래 색의 명도를 대상별 회색 램프로(D6), 핵심 이펙트 = 원색 유지(가장 밝게 `#FFF9CD`). 강도로 원래 화면과 섞는다.
3. 타이포는 화면 공간 캔버스를 별도 RT 로 그려 합성 패스에서만 쓴다 → 인물 뒤에 깔리고, 컬러가 탈색되지 않는다.
4. 렌더링 레이어: 플레이어는 기존 실루엣 레이어 재사용, 보스·핵심 이펙트용 비트 추가(연출 시작 때 런타임으로 켜고 끝나면 끔 — 프리팹 무수정 목표).

### 5.4 파일(예정)
- 새: `Map/BossVictory/BossVictoryDirector.cs`(서버 진행·RPC) · `BossVictoryPresentation.cs`(로컬 카메라·정지·타이포) · `BossVictoryRules.cs`(순수 — 타임라인·곡선·구도 계산, EditMode 테스트) · `BossVictoryConfig.cs`(SO) · `Rendering/BossVictory/BossVictoryFeature.cs` + 셰이더 · 테스트 `BossVictoryRulesTests.cs`
- 수정: `TwentyThreeBoss.cs`(치명타 기록 · B 판별 · 사망 보류 훅) · `MonsterBase.cs`(사망 연출 보류 시간 훅 — 기본 0, 동작 무변경) · `BossEncounterDirector.cs`(연출 시작·안전망 연장) · URP 렌더러 에셋(기능 추가 — 텍스트 에셋 1개)

## 5.5 Codex 회의 반영 (10-27, `codex exec -s read-only` · 결과 원문 `C:\Temp\codexreview\out.txt`)
코드로 다시 확인한 지적만 반영했다(✔ = Claude 가 코드에서 재확인).

| # | 지적 | 계획 변경 |
|---|---|---|
| C1 ✔ | **보스 B 는 0줄 자폭**(`No23_B.selfDestructAtZero = 1`). 첫 치명타는 사망이 아니라 자폭 예약이고, 실드를 부수면 `KillAfterZeroSelfDestruct()` 가 공격자 정보 없이 `TakeDamage(999999)` 로 처치한다 | 승리 시작 = **실제 사망(`Died`) 한 번**. 막타 = **HP 또는 자폭 실드에 마지막으로 피해를 준 플레이어**(피해 때마다 갱신, 사망 때 확정). K1 대체 |
| C2 ✔ | `AttackHitContext` 에 명중점이 없다(`sourcePosition`·공격자·맞은 콜라이더뿐) | 타격점 = 서버에서 보스 콜라이더 `ClosestPoint(sourcePosition)` 근사(1차). K2 대체 |
| C3 | `Died` 는 `base.ReceiveAttack` **안에서** 먼저 터진다 → base 뒤에 기록하면 늦다 | 공격 문맥은 base 호출 **전에** 임시 보관 |
| C4 ✔ | `SetCinematicInputLockServer(true)` 는 진행 중 스킬·공격을 끊는다 → 0.12초에 **공격 자세가 아니라 Idle 이 얼 수 있다** | 결정 필요 **Q1** |
| C5 | `Animator.speed = 0` 한 번 쓰기는 `PlayerAttackSpeedAnimator`(실행 순서 10000)·보스 속도 코드와 싸운다 | 기존 속도 결정 지점에 "승리 홀드" 우선순위를 합성. 원격 플레이어는 NetworkTransform 보간도 홀드 |
| C6 | 사망 보류를 코루틴 **맨 앞 고정 대기**로 넣으면 사망 상태 검출을 놓친다 | 사망 상태 검출은 즉시, **클립 대기 계산에서 0.12~2.00(1.88초)을 빼는** 방식. 결과 전환 하한 = 시작 + 3.20. 안전망 12초 → 여유 있게 연장. 총 결과 전환 ≈ 10초 |
| C7 | 새 디렉터를 런타임 `AddComponent<NetworkBehaviour>` 로 붙이면 안 된다(4구역 연출도 같은 제약) | **복제 스냅샷은 `BossEncounterDirector` 의 NetworkVariable 하나**(RPC 없음, 늦은 합류도 같은 경로). 화면은 일반 MonoBehaviour |
| C8 | 클라마다 Transform·물리가 달라 구도 계산이 갈린다 | **구도는 서버가 한 번 계산**(후보 방위각 고정 순서 + 점수 — 필수: 막타·보스·타격점 화면 안 / 높음: 막타 좌측·가림 없음 / 중간: 팀원) → 24°·32° 자세를 스냅샷에 담아 보냄. 원거리 막타는 상체 위주 구도 프리셋 |
| C9 | 합성 위치 | `BeforeRenderingTransparents` 의 **안개(450) 다음**, RenderGraph. 마스크 = 불투명만(막타/팀원/보스 전용 렌더링 레이어 비트 — 실루엣 Local/Remote 비트는 클라마다 뜻이 달라 재사용 불가). 핵심 타격 VFX 는 합성 **뒤** 투명 패스에서 원래 재질로 → 원색 유지. 비핵심 투명물은 숨김. Flat Kit 외곽선은 마스크를 조금 넓혀 근사 |
| C10 | 타이포 | `CinematicOverlay` 캔버스는 ScreenSpaceOverlay 라 RT 로 못 뺀다 → 전용 ScreenSpaceCamera 캔버스 + RT. `HideHud` 제외 대상에 등록 |
| C11 | 승리 중 꺼야 할 것 | `GlobalTimeScale`(전역 슬로모션) 강제 리셋·재발동 금지 · `CinemachineExternalImpulseListener`(화면 흔들림)·HP 비네트 · 실루엣·호버 외곽선 · 월드 공간 UI(머리 위 바·피해 숫자) · `HitFlash` |
| C12 | `PartyWipeWatcher` 가 승리 중에도 결과 전환을 앞당길 수 있다 | 승리 진행 중 전멸 전환 보류 |
| C13 | B 판별을 전역 `UseRaid` 로 하면 B 프리팹이 비었을 때 A 로 폴백된 판에서도 켜진다 | **실제 스폰된 인스턴스**가 레이드(B)인지로 판별 |
| C14 | 3.20초에 `CinematicOverlay.Dispose()` 를 부르면 HUD·카메라가 돌아온다 | 3.20초엔 색·타이포·정지만 끝내고 Dispose 는 씬 종료 때 |
| C15 | 장판·드론은 이미 `IsDead` 에서 정리된다 | K5 는 투사체·남은 몬스터·풀링 VFX 빈틈만. "숨김"과 "판정 종료"를 구분 |

**Codex 와 다르게 가는 것**
- Codex 는 "공통 시작 구도"를 권했지만, 문서 §2 "시작 시 카메라가 튀면 안 된다"가 우선 → **각자 자기 플레이 카메라에서 출발, 0.72초부터 전원 같은 구도**를 수용 기준으로 명시.

**결정 필요**
| # | 항목 | 선택지(권장 먼저) |
|---|---|---|
| Q1 | 0.12초에 멈출 플레이어 자세 | **(권장)** t=0 엔 입력만 막고 진행 중 공격 애니는 그대로 둔 채 0.12초에 얼림 → 2.00초 정지 해제 때 스킬 취소·무적 잠금 적용 / 또는 t=0 에 바로 잠금(공격이 끊겨 Idle 자세로 멈출 수 있음) |
| Q2 | 정지시킬 타격 VFX | **(권장)** 1차 = 승리 전용 타격 VFX 하나(타격점에 새로 띄워 정지·원색 유지) / 또는 이미 떠 있는 기존 타격 VFX 전부를 찾아 정지(분류·풀 회수 문제로 범위 큼) |
| Q3 | 팀원 배치 | **(권장)** 실제 위치 유지 + 구도 점수로 최선 / 또는 연출용 복사본을 좌우 후경 자리에 세움(보장되지만 작업 큼) |

## 6. 리스크
- **합성이 가장 크다**: 반투명·이펙트·외곽선(Flat Kit)·안개(`FogRendererFeature`)·실루엣 기능과 순서 충돌. 강도 0 일 때 비용 0 이어야 한다.
- 막타가 원거리일 때 막타 캐릭터와 타격점이 멀다 → 거리 보정으로 해결 못 하면 구도 규칙 조정 필요(경석 Play 확인).
- 정지 중 플레이어 오너 쪽 이동·물리가 남아 있으면 자세가 흔들린다 → 입력 잠금 + 로컬 Animator 정지로 막는다.
- 사망 보류가 `ServerDeathSequenceCompleted`·결과 화면 안전망(12초)과 엇갈리지 않게.
- 늦은 합류·연출 중 끊김: 스냅샷으로 남은 구간만 재생, 3.2초 지났으면 종료 구도만.

## 6.5 구현 순서 (Codex 권장안 기준)
1. 실제 사망·막타 스냅샷 — 일반 처치 · 0줄 자폭 예약 · 실드 파괴 처치 · 환경 피해를 로그로 확인(예약에서 시작 안 함, 사망에서 1회).
2. 복제 스냅샷 + 서버 구도 계산 + 연출 카메라 — 1·2·3인, 원거리 막타, 관전자에서 호스트·클라 자세 비교.
3. 조작 잠금 · 사망 보류(1.88초) · 결과 전환 하한 · 슬로모션 리셋 · 전멸 전환 보류 — A·중간보스 회귀.
4. 표현 정지·자세 보존(Q1) — 이동·대시·넉백·공격 중 막타.
5. 마스크·회색조 합성 — 디버그 ID 화면, Frame Debugger 로 패스 순서와 강도 0 비용 0 확인.
6. 타이포 RT · 승리 타격 VFX(Q2).
7. 중단·늦은 합류·결과 화면까지 MPPM 통합.

## 7. 검증
- EditMode: 타임라인 경계(0.12/0.22/0.72/0.80/0.92/1.08/2.00/2.24/3.20)·smoothstep·구도 계산(1·2·3인, 원거리 막타, 사망 팀원 제외) — `CombatEditModeTestRunner` 에 픽스처 추가.
- Play 1인: 문서 §6 완료 조건 5개 + 결과 화면 → 로비 조작 풀림.
- MPPM 3인: 호스트·클라 같은 구도·같은 타이밍, 클라 막타, 관전자 시점.
- A 보스·중간보스 처치 = 기존 그대로(회귀).

## 8. 구현 결과 (10-27)
| 파일 | 내용 |
|---|---|
| `Map/BossVictory/BossVictoryRules.cs` | 타임라인 경계·smoothstep·사망 표현 시계·서버 구도 계산(후보 방위 × 거리 점수) |
| `Map/BossVictory/BossVictoryConfig.cs` + `Resources/BossVictory/BossVictoryConfig.asset` | 수치·색·타이포·셰이더·글꼴(SeoulAlrim Heavy)·타격 VFX(FX_Hit_Spark) — 메뉴 `Tools/BossVictory/설정 에셋 만들기·배선 (멱등)` |
| `Map/BossEncounterDirector.Victory.cs` (partial) | 복제 스냅샷 `VictoryState` · 막타 확정 · 구도 계산 · 무적(`InvulnerabilityCause.BossVictory`) · 슬로모션 리셋 · 2.00초 정식 잠금 · 결과 전환 하한 |
| `Map/BossVictory/BossVictoryPresentation.cs` | 모든 피어: 카메라·정지·색 강도·타이포 5조각(두 띠)·타격 VFX·HUD/월드 UI/흔들림 끄기·입력 홀드 |
| `Rendering/BossVictory/BossVictoryRender.cs` + 셰이더 2 | 연출 동안만 게임 카메라에 패스 주입(렌더러 에셋 무수정) — 마스크(렌더링 레이어 4·5·6, 스텐실 128) → 합성 |
| 수정 | `TwentyThreeBoss`(막타 기록·사망 보류) · `MonsterBase`(`IsDeathPresentationPaused`·`CanPauseDeathPresentation`, 기본 무변경) · `BossEncounterDirector`(partial·훅 4곳) · `PartyWipeWatcher`(보스 격파 뒤 전멸 전환 안 함) · `PlayerLifeInputPolicy`(승리 입력 홀드 — 취소 없음) · `PlayerInvulnerability`(원인 추가) |

**Codex 안과 다르게 간 것(구현 중 결정)**: 타이포를 별도 RT 로 합성하지 않고 **마스크 패스가 인물 픽셀에 스텐실 128 을 찍고, 타이포 TMP 재질이 NotEqual 로 읽는다**. RT·전용 카메라가 없고, 타이포(큐 2990)가 합성 뒤·VFX(3000) 앞에 그려져 레이어 순서가 그대로 맞는다.

### Play 체크리스트
> 테스트 키 **F11**(호스트, `Dev/DevBossFinalBlow.cs`): 어디서든 한 번 — 튜토리얼 보스 진입 스킵 → 엘리베이터 없이 보스방 → 등장 연출 스킵 → 23호 페이즈·0줄 자폭 완료 + HP 1. 다음 한 대가 실제 처치.
1. 보스 B 처치(일반·0줄 자폭 실드 파괴 둘 다) → `[BossVictory] 시작 — 막타 client …(기록 있음)` 로그 1회.
2. 0.12초에 막타 공격 자세로 멈춤(Idle 로 안 돌아감), 0.22–0.72 카메라가 내 화면에서 튐 없이 내려옴.
3. 0.72 주황 배경·회색 인물, 0.92 `BOSS DOWN` 오른쪽에서 들어와 두 띠 정렬, **글자가 인물·보스 뒤**.
4. 2.00 붕괴(사망 클립) 재개 · 색 2.24 원복 · 3.20 글자 사라짐 · 32° 유지 → 결과 화면(약 10초).
5. A 보스·중간보스 처치 = 기존 그대로. 결과 → 로비에서 조작 정상.
6. MPPM: 클라 막타 · 0.72 부터 호스트·클라 같은 구도 · 관전자.

## 9. 경석 Play 1차 피드백 반영 (10-27)
| 항목 | 변경 |
|---|---|
| 배경 | 단색 주황 → **틴트 34%**(원래 화면이 비친다, `backgroundTint`) |
| 인물·보스 | 회색 단색 유지 — **보스 디졸브가 시작될 때까지**(기획 2.00–2.24 원복 대신). 디졸브 시작(`DissolveDeath.HasStarted`)에 0.24초로 원래 색 |
| 정지 | 완전 정지 → **슬로모션**(`slowMotionScale` 0.1). 서버 사망 대기도 같은 배율(`MonsterBase.DeathPresentationTimeScale`) |
| 플레이어 자세 | 처치 순간 클립이 끝나거나 Idle 로 넘어가려 하면 **마지막 프레임에 멈춤**(씬 끝까지) |
| 글리치 | 레퍼런스 1:30~1:33·캡처 3장 — 회색으로 밝게 날아감 + 가로 띠 밀림 + 가로 잔상 + 스캔라인. 구간 = 막타~카메라 하강(0.12–0.72) · 카메라 멈춤(0.72–0.90) · 다시 움직임(2.00–2.14), 24fps 로 깜빡임(`glitchBursts`·`glitchShift`·`glitchStreak`·`glitchWash`·`glitchFlicker`) |
| 타이포 | 외곽선 0.08 → 0.22 · 진입 느리게(페이드 0.4초·안착 0.45초 — `typoFadeInSeconds`·`typoSettleSeconds`) |
| 외곽선 | 연출 동안 **플레이어 실루엣·적 호버 외곽선 끔**(보스에 가려진 윤곽선이 컷을 깬다) |
| F11 카메라 | ⚠️ 뒤집음(경석 10-27): 엘리베이터 없는 즉시 이동·강제 카메라 복귀를 버리고 **정상 엘리베이터 경로에 시작 시각만 당기는 스킵**(`DevStartElevatorSkippedServer`) — 카메라 복귀가 정상 경로로 일어난다. F11 완료 직후·2초 뒤 화면 vcam 진단 로그. 리그 없는 도착 컷(`CutToCurrentTarget`)은 일반 수정으로 유지 |
| 보스 이펙트 | 처치 순간(모든 피어) 보스 공격·타격·바닥 예고를 전부 걷는다 — 훅 도중 처치되면 이펙트가 컷에 남았다(`TwentyThreeBoss.StopPresentationEffectsLocal`: 소켓 이펙트 Stop · 보스 추종 EffectManager 즉시 반납(`ReleaseAllFollowingImmediate`) · 착지 예고 원 · 부채꼴·점프 바닥 예고 · 차징 오라 · 몸 파티클) |
| 보스 위 주황 점선 | 마스크를 하드웨어 깊이(LEqual) 대신 깊이 텍스처 선형 비교(여유 8cm)로 — 이음새에서 픽셀이 빠져 배경(주황)으로 칠해지던 것 |
| 몬스터 외곽선 | 연출 동안 보스 Flat Kit 외곽선 두께 ×0.35·색 #12171C(`bossOutlineScale`·`bossOutlineColor`), 플레이어는 그대로(`characterOutlineScale` 1). HitFlash 가 블록을 비우므로 매 프레임 덮고, 디졸브 시작에 원래 값 |

## 10. 할 일 200 — 엔딩 연출 1차 정리 (11-06, 사용자 결정 · 브랜치 `feature/BossVictoryRework1`)
> 이 절이 앞 절(§5.2 표·§8·§9)과 다르면 **이 절이 맞다**. A(서버 판정·무적·안전망·입력 잠금)·B(카메라)·H(HUD·월드 UI 숨김)·I(넘어가기)·J(소리 키) 는 그대로.

### 10.1 바뀐 것
| 요소 | 변경 |
|---|---|
| C 추가 연출 구간 | 타임라인 맨 앞에 **추가 연출 구간**(설정 `preludeSeconds`, 기본 0초 — 아직 내용 없음). 서버가 처치 순간 값을 복제 상태 `VictoryState.PreludeSeconds` 에 담는다(새 RPC 없음, 모든 피어 동일). **단계 시각 t = 경과 − 구간**(`BossVictoryRules.StageTime`) — 슬로모션·카메라 하강·색·서버 사망 대기·2.00 정식 잠금·결과 하한(구간 + 3.20)·안전망(+구간 + 1.88)이 전부 같은 만큼 밀린다. 연출을 꽂는 곳 = `BossVictoryPresentation.PreludeStarted / PreludeEnded`(static event, 모든 피어 로컬) |
| D1 배경 | 위/아래 그라데이션 두 색 → **단색 `#EAAD70`**(`backgroundColor`). 틴트 강도 0.34 그대로 |
| D2 단색 | 인물·무기(몸 렌더러 마스크)뿐 아니라 **반투명으로 그려진 이펙트·궤적까지 단색**. 합성을 반투명 **뒤**(AfterRenderingTransparents)로 옮기고, 반투명 전 색 복사본과 차이가 나는 픽셀을 이펙트로 보고 단색 램프(`effectColor`, `effectDetectGain`). 노출 1.6 → **1.75**(`characterExposure`) |
| D3 색 원복 | 삭제 — 단색·주황 상태 **씬 끝까지 유지** |
| D4 글리치 | 전 구간 삭제(설정 칸·셰이더 코드·규칙·테스트). TV 글리치를 넣을 자리 = 합성 셰이더 Frag 끝 |
| D5 외곽선 | 연출 동안 플레이어·보스 Flat Kit 외곽선 **폭 0**(매 프레임, 씬 끝까지). 옛 `bossOutlineScale`·`bossOutlineColor`·`characterOutlineScale` 칸 삭제 |
| D6 | 실루엣·적 호버 외곽선 끔 유지 |
| E 타이포 | `BOSS DOWN` 생성 자체를 뺐다(타이포 칸·글꼴 배선·규칙 삭제). `UiStrings.BossVictoryTypo` 키·시트 행은 미사용으로 남음. 사운드 키 `sfxTypo` 는 옛 진입 시각(단계 0.92)에 그대로 |
| F 타격 VFX | 승리 전용 타격 VFX 칸(`hitVfxPrefab`) **비움**(코드는 남김 — 나중에 추가). 저작 메뉴도 더는 자동으로 채우지 않는다. 보스 이펙트 정리는 그대로 |
| G 보스 사망 | 사망 애니는 추가 연출 구간 **뒤**에(서버 사망 대기 배율 0 · 클라 보스 몸 Animator 0 — 웰즈 Fear 는 그대로). **디졸브 삭제(보스 B 승리 경로)**: `MonsterBase.KeepsCorpseOnDeath`(23호 = 승리 연출 대상일 때) → 사망 클립 끝 + `DelayAfterClipEnd` 에 `ServerDeathSequenceCompleted` 만 올리고 디스폰하지 않는다 → 기존 `HandleBossVanished`(+1초) → 결과 화면. 보스는 사망 자세로 남는다 |

### 10.2 카메라·벽 (사용자 추가 지시 11-06)
- **할 일 234(카메라 벽 밖 방지) 삭제** — `d38ed4c3` 되돌림(`keepCameraInsideRoom`·`wallMargin`·`Arena`·`TwentyThreeBoss.TryGetArenaRect`·테스트 5·wall-concept §7).
- 승리 연출 동안 **벽 가림 처리 끔**: `WallOcclusionDriver.SuppressedByCinematic`(플레이어 주변 디더 전역 끔) · `WallTransparencyGroup.SuppressedByCinematic`(구역 상단 그라데이션 → 밖 값). 평소 플레이는 그대로(연출이 켜고 씬 종료 때 끈다).
- 대신 **앞면이 확실히 카메라를 향한 벽만 남기고 나머지 벽 렌더러를 끈다**(모든 피어 로컬, 한 번 끄면 씬 끝까지): 후보 = 타격점 반경 `wallSearchRadius`(40m) 안의 Wall 레이어·Wall 태그·이름 `wall*` 조상 아래 렌더러. 법선 = 바운즈 얇은 축(방 안쪽), 숨김 = 카메라가 벽 바깥 **또는** `dot(법선, −수평 시선) ≤ wallFacingThreshold`(0.3 — 뒷면·옆면). 규칙 `BossVictoryRules.WallInwardNormal / ShouldHideWall`.

### 10.3 새 타임라인 (경과 e = 처치 확정부터, P = 추가 연출 구간 기본 0)
| 경과 | 단계 t | 화면(모든 피어) | 서버 |
|---|---|---|---|
| 0 | −P | HUD·월드 UI 숨김 · 연출 카메라 = 지금 플레이 카메라 · 입력 홀드 · 외곽선 0 · 벽 가림 끔 · 보스 이펙트 정리 · `PreludeStarted` | 무적 · 전역 슬로우 리셋 · 결과 하한 = P + 3.20 · 안전망 + P + 1.88 |
| 0 ~ P | −P ~ 0 | 추가 연출 구간(내용 없음) · 보스 몸 정지(사망 애니 대기) · 플레이어 공격 동작은 흐름 | 사망 대기 배율 0 |
| P | 0 | `PreludeEnded` · 보스 사망 애니 시작 | 사망 대기 배율 1 |
| P + 0.12 | 0.12 | 슬로모션(×0.1) 시작 · 사운드 `sfxHitStop` | 배율 0.1 |
| P + 0.22 ~ 0.72 | 0.22 ~ 0.72 | 카메라 하강 → 24° 구도(모든 피어 같은 화면) | |
| P + 0.72 ~ 0.80 | 0.72 ~ 0.80 | 배경 단색 주황 틴트 · 인물·무기·이펙트 단색 들어옴 → **씬 끝까지 유지** | |
| P + 0.92 | 0.92 | 사운드 `sfxTypo`(글자는 없음) | |
| P + 2.00 | 2.00 | 슬로모션 해제 · 카메라 32° 로 상승 시작 · `sfxResume` | 배율 1 · 전원 연출 잠금(스킬 취소) |
| P + 3.20 | 3.20 | 32° 구도 유지 | 결과 하한 |
| 사망 클립 끝 + `DelayAfterClipEnd` | — | 보스 사망 자세로 남음(디졸브 없음) | `ServerDeathSequenceCompleted` → +1초 결과 화면 |

### 10.4 반복 디버그 씬 — 할 일 244 (11-06, 브랜치 `feature/BossVictoryDebugScene`)
처치 연출을 처음부터 끝까지 반복해서 보며 수치를 맞추는 랩. 200 결과(승리 연출 중 구역 벽 투명화 끔 포함)는 그대로다.

**씬** `Assets/0.Scenes/Debug/DebugBossVictory.unity` — 빌드 목록(EditorBuildSettings)에 넣지 않는다. 씬에 든 것 = 방향광·Global Volume(맵씬 복사) · `CameraSwitcher` · `EffectManager` · `FloatingDamageSpawner` · `BossVictoryLab`(`Dev/BossVictoryLab/DebugBossVictoryLab`). **네트워크 오브젝트는 씬에 두지 않고** 랩이 런타임에 만든다:
- 보스방 지형 = `bossroom.prefab` 복제(벽 포함 — 벽 숨김 판정을 실제 벽으로 본다, 안의 송전탑 NetworkObject 는 꺼 둠).
- 보스 디렉터 = `BossEncounterDirector.prefab` 을 호스트가 뜨면 스폰 → **23호 B(`TwentyThree_Raid`, 웰즈 포함)를 실게임 스폰 경로(`SpawnBossServer`)로** 착지점에 세운다(등장 연출 없이, 서버 FSM 은 멈춘 채 — 서 있기만).
- 막타 = Dev Boot 가 띄운 내 플레이어(툴바 캐릭터). 팀원 = 몸·무기만 남긴 표시용 복제본(`CharacterSelectPreviewPool.CreatePreviewObject`, 네트워크·입력·물리 없음) 0~2명.

**실행** 툴바 Dev Boot 드롭다운에서 `DebugBossVictory` → ▶(솔로 호스트). 툴바 보스 = **B**(기본값)여야 연출이 뜬다(A 면 패널이 알려 준다).

**한 바퀴(실게임 경로)** 원상복구 → 보스·막타·팀원 배치 → 0.8초(카메라 복귀) → 막타 공격 애니(`killerAttackState`, 없으면 생략) → 막타 준비(`DevPrepareFinalBlowServer` — F11 과 같음) → **내 플레이어가 준 피해 한 대**(`ReceiveAttack`) → Died → `HandleBossDefeated` → `TryStartVictoryServer` → 사망 표현 → 결과 대기. 결과 화면으로 넘어갈 순간에 랩이 전환을 막고 1.5초 뒤 처음부터(자동 반복 끄면 거기서 멈춤).

**조작**(패널 = 화면 왼쪽 위)
| 항목 | 내용 |
|---|---|
| F7 / 처음부터 | 원상복구 후 다시 |
| F8 / 일시정지 | 멈춤·재개 |
| 자동 반복 | 끝나면 원상복구 → 처음부터 |
| 배속 ×0.1·0.25·0.5·1 | 느리게 보기 |
| 점프(슬라이더·0.72·2.00·3.20) | 처음부터 다시 돌려 그 **단계 시각**까지 빨리 감은 뒤(최대 ×8) 멈춘다 — 애니·사망 대기·벽 숨김까지 그 시각의 실제 상태 |
| 막타 각도·거리·방향 보정 | 보스 기준(0° = 화면 아래, + = 시계 방향). 구도 후보가 바뀐다. 다음 재생에 반영 |
| 팀원 0~2명·캐릭터 ▶ | 플레이어 1~3명. 팀원 위치 = 막타 기준 로컬(`teammateOffsets`) |
| 표시 | 단계 시각·경과·추가 연출 구간·단계 이름·배속 |

**설정 값** `Resources/BossVictory/BossVictoryConfig.asset` 인스펙터(패널 "설정 SO 선택")에서 바꾸면 **다음 재생에 반영**(서버 구도·추가 연출 구간·색 켜기는 처치 순간에 읽고, 슬로 배율·벽 기준 등 몇 칸은 바로). 🔴 **SO 라 Play 를 끝내도 값이 남는다** — 원하지 않으면 git 으로 되돌린다.

**배속·점프 원리** 표시(카메라·색·단계)는 서버 시각 기준 경과, 애니·서버 사망 대기는 게임 시간이다. 랩은 Time.timeScale 을 바꾸면서 매 프레임 복제 상태의 처치 시각(`StartServerTime`)을 (실제 Δ − 게임 Δ)만큼 밀어 둘을 같은 시계로 묶는다(`BossVictoryLabRules.ClockShift`).

**실게임 코드 변경(동작 불변)**
- `BossEncounterDirector.Victory.cs` partial 메서드 2개(`DevAppendVictoryTeammates` · `DevBlockResultTransition`) 선언 + 호출 1곳씩(`TryStartVictoryServer` 팀원 목록 끝 · `TickResultTransition` 하한 검사 뒤). 구현은 `BossEncounterDirector.Dev.cs`(`UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS`)에만 — 릴리스 빌드엔 C# 이 호출째 지운다.
- `Dev.cs`: 표시용 팀원 목록·결과 차단 플래그·`DevSpawnVictoryBossServer`·`DevResetVictoryServer`·`DevShiftVictoryClockServer`.
- `BossVictoryPresentation`: 복제 상태가 **꺼지면 정리**(실게임은 한 번 켜지면 씬 끝까지라 영향 없음) · 개발 빌드에서만 표시용 팀원도 팀원 단색 마스크.

**알려진 한계**
- 씬에 `BossTeleportManager`·`MapSceneManager` 가 없어 디렉터 스폰 때 `BossTeleportManager를 찾지 못했습니다` 오류 1줄이 뜬다(정상).
- 조명은 맵씬 방향광·Global Volume 만 복사했다(라이트맵·물·안개 없음). 보스방 안 조명(LGT_*)은 프리팹에 있어 그대로다.
- 팀원 복제본은 Idle 자세 그대로(공격 클립 홀드 없음). 막타 공격 상태 이름은 캐릭터마다 다르다(팔라딘 `Default_Attack1` — 없는 이름이면 그냥 서서 처치).
- 보스방 NavMesh 가 없어 보스 에이전트는 꺼 둔 채다(서 있기만 하므로 무관).
- 호스트 1인 전용(클라 창은 패널만 보인다).

**구도 프리셋 — 244 후속 A (11-06 사용자 결정)** Config 의 카메라 구도 규칙 값 묶음(부감 24°/32°·FOV·기준 화면비·거리 범위·간격·종료 거리 배수·몸 높이·방위각 후보)을 이름 붙여 저장·불러오기·삭제한다.
- 목록 = git 추적 SO `Assets/1.Scripts/Dev/BossVictoryLab/BossVictoryCameraPresets.asset`(클래스 `BossVictoryCameraPresets` — 팀 공유). 기본 항목 `기본(200)` = 지금 값. 랩 `cameraPresets` 칸에 연결돼 있다.
- 패널 아래 "구도 프리셋": 이름 칸 + [현재 값 저장](같은 이름 = 덮어씀) · 항목마다 [불러오기]·[삭제]. 불러오면 **Config 에 덮어써 다음 재생부터**(구도는 처치 순간 서버가 고른다). 에디터에선 저장·불러오기·삭제 때 두 SO 를 바로 디스크에 쓴다.
- 런타임 게임 코드는 이 SO 를 읽지 않는다(Config 만).

**카메라 구도 슬라이더 — 244 추가 (11-06 사용자)** 패널 "카메라 구도"(프리셋 바로 위, 같은 묶음): 정지/끝 부감·FOV·기준 화면비·최소/최대 거리·거리 간격·끝 거리 배수·몸 높이 슬라이더(값 표시 + [기본 n] = 코드 기본값) · 방위각 후보 항목별 슬라이더(+ 추가 / − 삭제 / 기본값) · [구도 전부 기본값으로]. Config 를 직접 고친다(SetDirty — 프로젝트 저장 때 남는다). 구도는 처치 순간 서버가 고르므로 **다음 재생부터** — 옆에 [바로 다시 재생 (F7)].
- **라이브** 토글: 켜면 값을 바꿀 때마다 진행 중 연출 구도도 다시 계산(`BossEncounterDirector.DevRecomposeVictoryServer` — 처치 순간 기억한 막타·타격점·보스·팀원 위치 + 지금 Config 로 `Compose` → 복제 상태의 정지·끝 구도·FOV 덮어씀). 방위·거리 후보도 다시 고르므로 다음 재생과 같은 구도다. 기억은 partial `DevRememberVictorySubjects`(Dev.cs 에만 구현 — 릴리스엔 호출째 없음).

### 10.5 발밑 회색 원 — 할 일 246 (11-06, 브랜치 `feature/BossVictoryDebugScene`)
주황 틴트 동안 막타 발밑 둘레는 주황 대신 흑백.
- **중심** = 표시가 시작되는 순간(처치 확정) 막타 플레이어(`KillerClientId`) 위치. 그 피어에서 못 찾으면 타격점. 이후 고정.
- **범위** = 중심에서 **수평(XZ) 거리**만 본다 — 위에서 봤을 때 원 안에 드는 바닥·벽·소품 전부, 높이 한도 없음(원기둥). 픽셀 월드 위치는 합성 패스가 `_CameraDepthTexture` 로 되살린다(합성 패스 `ConfigureInput(Depth)`).
- **모습** = 배경 픽셀만: 원래 명도(조명 결과, 무늬 유지) × 밝기 배율의 흑백. 인물·보스·이펙트 단색은 그대로. 가장자리는 반경 안쪽 "부드러움"(m)에 걸쳐 흑백 → 주황. 원래 화면과의 섞임(`_Weight`, 0.72–0.80 들어옴)은 그 위에 그대로.
- **커짐** = 주황이 들어오기 시작하는 단계 0.72초(`DescendEnd`)부터 반경 0 → 목표, smoothstep(`BossVictoryRules.GroundCircleRadius`). 커지는 시간 0 = 바로 목표.
- **Config 칸**(`BossVictoryConfig` "발밑 회색 원") `groundCircleRadius` 6m(0 = 끔) · `groundCircleGrowSeconds` 0.6초 · `groundCircleSoftness` 1m · `groundCircleBrightness` ×1. 표시가 매 프레임 읽어 재생 중에도 바로 반영.
- **랩** 패널 아래 "발밑 회색 원" 슬라이더 4개(반경 0–20·커지는 시간 0–3·부드러움 0–5·밝기 0–2)가 Config 를 직접 고친다(SetDirty — 프로젝트 저장 때 디스크에 남는다). 커지는 모습 = '0.72' 점프 → 재개(F8), 느리게는 배속 ×0.25.
- 파일: `BossVictoryConfig`·`BossVictoryRules.GroundCircleRadius`(+EditMode 테스트 2)·`BossVictoryRender.SetGroundCircle`·`BossVictoryComposite.shader`·`BossVictoryPresentation`(중심 저장·매 프레임 설정).
- 한계: 하늘·깊이 없는 픽셀은 원 밖으로 본다. 반투명(이펙트)은 깊이에 안 써서 그 뒤 바닥 기준으로 판정되지만 이펙트는 어차피 단색 쪽이 이긴다.
