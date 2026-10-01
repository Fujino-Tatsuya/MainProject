# PLAN — 데미지 숫자 표기 Re:C v0.2 반영

> 상태: **승인 대기** (2026-10-01 · 은희(Claude)) · 브랜치 `feature/DamagePopupTweening`
> 기획 원본: `GA7thFinal_VeyTrace/2_Documents/20_Design/ReC/Re_C_데미지_숫자_표기.md` (v0.2, 레포 밖)
> 기존 구조: [Docs/tech/floating-damage-design.md](Docs/tech/floating-damage-design.md) · 이징: `Assets/1.Scripts/EuniTween/`

## 1. 확정 결정

| # | 결정 | 근거 |
|---|---|---|
| D1 | 트윈 라이브러리 도입 안 함. 연출은 자체 `EuniTween`(`Easing.Evaluate`·`Punch`) | 2026-10-01 은희 |
| D2 | 누적 단위 = **공격자 + AttackType(슬롯) + 대상**. 공격 사용 ID는 발급하지 않음 | ID 방식의 이점은 "숫자 표시 중 같은 슬롯 재사용 구분" 하나뿐인데 비용은 모든 공격 경로 수정 |
| D3 | `AttackType` 끝에 `SkillQ`·`SkillE`·`SkillR` 추가. `Skill`(=2)은 "슬롯 미상"으로 유지 | 직렬화 값 0/1만 에셋에 존재. 소비자 2곳(`MonsterHitReactionPolicy`·`BossBomb`)은 `Default` 비교라 영향 없음 |
| D4 | 공격에 **단발/누적** 구분 추가 (`AttackHitPattern { Single, Multi }`, 기본 `Single`) | 단발 = 타격마다 새 숫자, 누적 = 기획 §5 흐름 |
| D5 | 좌표 = 단발은 **첫 타격 시 월드 위치에 고정**, 누적(Multi)은 **대상을 따라감**(대상이 꺼지면 마지막 위치) → 매 프레임 화면 투영 + 1080p 기준 픽셀 오프셋 + 화면 가장자리 보정 | 탑다운 카메라가 플레이어를 따라가므로 화면 고정은 숫자가 미끄러짐. 🔴 누적 추적은 **기획 §4 "따라가지 않음"과 다름** — 은희 결정(2026-10-01 Play 후), 기획자에게 공유 |
| D6 | 대상 등급 = **`MonsterDataSO.rank`** (`MonsterRank { Normal, MidBoss, Boss }`, 기본 `Normal`). `MonsterBase` 가 아닌 Unit(더미·파일런 등) = Normal | 등급 구분 코드가 레포에 없다(`MonsterArchetype` 은 AI 분기용, 중간보스 3종도 `Melee`). 클래스 하드코딩 대신 데이터로. 🔴 **경석 영역 — 필드 추가 합의 필요** |
| D7 | 기준 최대 체력 = 대상의 기본 `MaxHp`. 인원수 체력 보정은 현재 없음 | 기획 §3 추가 제안. 보정이 생기면 그 전 값을 쓴다 |
| D8 | 자기 피해만 표시 = 기존 `DisplayFilter.OwnDealtOnly` 로 Settings 값 변경 | 기획 §2 추가 제안 |

**남는 빈틈 (수용):** 누적 숫자가 떠 있는 동안(마지막 타격 + 0.75초) 같은 슬롯을 다시 쓰면 앞 숫자에 합쳐진다. 현재 스킬 쿨타임은 이보다 길다.

## 2. 1단계 — 데이터 (네트워크)

1. `AttackType` 에 `SkillQ/E/R` 추가, 주석 갱신(슬롯을 읽는 코드가 생겼다).
2. `AttackInfo.hitPattern` 추가. 플레이어 스킬·평타가 슬롯 타입과 패턴을 채운다.
   - 슬롯: 스킬이 장착된 슬롯에서 결정(구현 시 스킬 시스템 확인). 평타 = `Default`.
   - 패턴: 스킬 데이터(SO)에 필드. 다단·지속(가붕이 Q 틱, 거너 R 레이저 등) = `Multi`.
3. `Unit` 이 `_damageAttackerClientId` 와 같은 방식으로 타입·패턴을 보관 → `ClientDamageDealtClientRpc` 에 `byte` 2개 추가.
4. `ClientDamagedAttributed` 인자를 구조체 `DamageDealtInfo`(amount·channel·attacker·attackType·hitPattern)로 교체. 구독자 3곳 갱신
   (`FloatingDamagePresenter`·`UnitCameraFeedbackReporter`·`TrainingDummyDamagePresenter`).
5. 스포너 누적 키를 D2 로 교체. `Single` 은 누적하지 않음. **연출은 아직 기존 그대로.**
6. `MonsterRank` enum + `MonsterDataSO.rank` 추가(끝에만 값 추가 규칙 주석). 값 설정 5개:
   `GauntletBotData`·`SpinnerBotData`·`WallBotData` = MidBoss · `No23`·`No23_Solo` = Boss. 나머지는 기본값 Normal 이라 수정 없음.

검증: EditMode(키 규칙) + Play 1회(평타 연타 = 숫자 여러 개, Q 다단 = 숫자 하나, Q 와 E 분리).

## 3. 2단계 — 연출 (기획 §3·§4·§5)

- **강도 판정**: `개별 타격 ÷ MaxHp × 100` → 낮음/중간/높음. 구간표는 등급별로 Settings(SO).
- **크기·등장**: 유지 90/100/120% · 등장 확대 없음 / 110→100%(0.08s) / 140→120%(0.12s) · 높음은 한 단계 굵게 · 높음 흔들림 ±3px 0.08s 1회.
- **단발 흐름**: 0.20s 동안 24px 상승 → 최고점에서 12px 하강하며 끝 0.25s 동안 투명화 → 제거. 전체 0.65/0.75/0.85s.
- **누적 흐름**: 첫 타격 0.20s 동안 24px 상승 후 유지 → 타격마다 값 즉시 갱신 + 105% 펀치(0.06s, 최소 간격 0.10s, 간격 내 적중은 펀치만 생략)
  → 마지막 타격 후 0.50s 유지 → 0.25s 동안 12px 하강하며 투명화.
  - **"숫자가 유지되는 동안 누적"(기획 106행) = 사라짐 0.25s 까지 포함**(2026-10-01 은희). 사라지는 중 적중하면 불투명도 복구 + 유지로 복귀.
    근거: 거너 R 틱 간격 0.50s = 유지 0.50s 라 유지만으로는 경계에서 붙었다 떨어졌다 한다(1단계 Play 에서 R 만 누적 안 됨 — 옛 유지 0.3s).
  - ~~크기는 지금까지 받은 개별 타격 중 가장 높은 구간 유지~~ → 아래 은희 결정으로 누적 합계 기준.
  - **채움 색은 강도별(낮음 노랑 · 중간 주황 · 높음 빨강, HP 피해만)이고, 누적 숫자는 크기·색 모두 누적 합계로 판정** —
    합산될수록 구간이 오르고, 오를 때 그 구간의 등장 확대를 다시 보여 준다(높음은 강조 간격 규칙 적용).
    🔴 기획 §2 "색은 유형만"·§5 "누적액으로 크기를 올리지 않음"과 다름 — 은희 결정(2026-10-01).
  - 높은 구간 첫 진입 시 큰 확대 + 흔들림. 같은 대상 기준 강조 간격 0.30s, 간격 내면 크기·값만 반영.
- **위치**: 생성점 = 몸 중심에서 화면 위로 20px, 분산 ±12 / ±6px. 가장자리 보정.
- **표시 계층**: 스크린 스페이스 오버레이 Canvas + `CanvasScaler`(1920×1080 기준) → 기획 픽셀 값을 그대로 단위로 쓴다.
  스포너가 Awake 에서 자식으로 Canvas 를 만든다 — 팝업 프리팹은 이미 TMP UGUI 라 수정 없음, 씬 수정 없음.
- **생성점**: `FloatingDamageAnchor` 가 있으면 그 위치, 없으면 대상 루트 Collider 중심(없으면 위치 + 1m).
- 이징: 상승 `OutCubic` · 등장 확대 `OutBack` · 펀치 `Punch(OutQuad)` · 하강 `InQuad` · 투명화 `Linear` — Settings 에서 `Ease` enum 으로 교체 가능.
- 이번 제외: 치명타·속성 색, 최종 글꼴·색 값(기획 §2).

검증: 강도 판정·누적 규칙은 순수 클래스로 분리해 EditMode. Play(TrainingDummy 씬)로 눈 확인, MPPM으로 자기 피해만 보이는지 확인.

## 4. 수정 예정 파일

- 1단계: `Unit/Weapon/BaseAttack.cs`(AttackType·AttackInfo) · `Unit/Unit.cs` · `Monster/MonsterDataSO.cs` + 신규 `Monster/MonsterRank.cs` · `2.Prefabs/Monster/Data/` 에셋 5개 · 플레이어 스킬·평타(`Player/Skill/*`, `Player/Gunner/*`, `PlayerDefaultAttack`, `DefaultAttackProjectile`) · 구독자 3곳 · `FloatingDamageSpawner`.
- 2단계: `UI/Combat/FloatingDamage/*` · `2.Prefabs/UI/FloatingDamagePopup.prefab` · 스포너가 놓인 씬 3개(`MainFlow/4.MapScene`·`Debug/TrainingDummy`·`Debug/4.MapScene-TV3`) · Settings 에셋.

## 5. 리스크

- RPC 시그니처 변경 → 호스트·클라 빌드가 섞이면 안 됨(MPPM 은 같은 빌드라 무관).
- 씬 3개·프리팹 수정 — 텍스트 에셋 소량이라 Unity 켠 채 가능(CLAUDE.md §6). 수정 후 `cmp`·guid 확인.
- 🔴 **기획 구간이 현재 수치와 안 맞는다** — "낮음"이 거의 안 나오고 평타도 대부분 "높음"(가붕이 공격력 33 기준):
  ChompBot(100) 평타 33% · GauntletBot(300) 평타 11%·Q틱 3.3% · 23호(2000) 평타 1.65% → 전부 높음.
  구간은 Settings(SO) 값이라 코드 영향은 없음. → ✅ **2026-10-01 재조정 수령·반영**:
  일반 15% / 50% · 중간 보스 5% / 20% · 최종 보스 0.75% / 3% (중간 이상 / 높음 이상).
- 스크린 스페이스 전환 시 월드 오클루전·깊이 정렬이 사라진다(숫자가 항상 맨 위). 기획 의도와 맞음.
