# PLAN — 플레이어블 스킬 툴팁 + 캐릭터별 스킬 아이콘

> 상태: **✅ 은희 승인(2026-10-02) · 구현 = Codex 위임(브랜치 `feature/SkillTooltip`)**. 작성 2026-10-02 은희(Claude).
> 담당 영역 = 은희(Player·UI). 데이터 테이블 확장 포함 — 사용법 원본은 [Docs/tech/data-table.md](Docs/tech/data-table.md).

## 1. 목표 · 완료 조건

가붕이(`Player_Paladin`)·거너(`Player_Gunner`)의 HUD 5칸(P·Q·E·RMB·R)에 마우스를 올리면 롤 스타일 툴팁이 뜬다.
문구는 `GameData.xlsx` 에서 오고, 자리표시자가 실제 수치를 채운다. Shift 를 누르면 계산식이 보인다. HUD 아이콘은 캐릭터마다 다르다.

**완료 조건**: 두 캐릭터 × 5칸 = 10칸 전부 아이콘·툴팁이 맞게 나오고 · 테이블 모드에서 xlsx 문구가 반영되고 ·
잘못된 자리표시자는 Play·빌드 전에 오류 위치로 막히고 · EditMode 테스트 통과 · 은희 Play/MPPM 확인.

## 2. 확정된 결정

| # | 결정 | 비고 |
|---|---|---|
| D1 | **문구 = 각 출처의 문자열 필드** → 기존 `Paladin`/`Gunner` 시트의 그 대상 구역에 행으로 | 대상·행 규칙은 기존 데이터 테이블 그대로 |
| D2 | **거너 패시브 = 과열** | `IPlayerPassive`(쿨타임 fill 용)에 맞추지 않는다 — 툴팁 출처는 별도 인터페이스 |
| D3 | **거너 과열 툴팁 = `Slot_P`** | 과열 아이콘 표시, 쿨타임 fill 없음 |
| D4 | **자리표시자 사용** | 문법 §3 |
| D5 | **피해 = 계수 + 실제 수치** | 실제 수치 = 호버 순간의 플레이어 최종 공격력 기준 |
| D6 | **아이콘 = 캐릭터마다 다름** — 스폰된 Variant 의 데이터에서 읽음 | 캐릭터 선택 UI(미구현)가 생겨도 그대로 동작 |
| D7 | **호버 = 최종 피해만** / **호버 + Shift = 최종 피해 + 계산식** | Shift 는 게임 입력에 안 묶여 있음(Dev `Shift+F3` 만). 열린 툴팁이 Shift 에 즉시 반응 |
| D8 | **좌클릭 기본 공격 툴팁 없음** | HUD 칸 신설 안 함 |
| D9 | **아이콘 = 스킬 데이터의 `Sprite icon` 필드** | Variant 오버라이드 안 씀. 이름·설명·아이콘 출처 하나 |
| D10 | **구성 = 롤 툴팁 레퍼런스**(은희 제공 5장) | §4 |
| D11 | **키워드 색 = TMP Style Sheet 태그** `<style=stun>기절</style>` | 색은 스타일 에셋 하나에서 관리 |
| D12 | **실제 피해 = 그 스킬의 실제 판정 공식.** 거너 차지 레이저·과열 단계는 **최소 ~ 최대 범위**(최소 차징·과열 0단계 ~ 최대 차징·최고 단계) | 범위라 과열 현재값에 따라 변하지 않음 — 공격력 버프만 실시간 |
| D13 | **위치 = 칸 위 고정**(칸 중앙 정렬, 화면 밖이면 안으로) · 호버 **0.15초** 후 표시 · 벗어나면 즉시 숨김 | |
| D14 | **로컬 플레이어 자기 HUD 만** | 버프 배율이 오너 클라에 정확한지 구현 때 확인 + MPPM |
| D15 | **EventSystem 없는 씬 단독 Play 는 대응 안 함** | 정식 흐름·Dev Boot 는 BootStrap 경유 |
| D16 | **임시 문구는 Claude 가 10칸 채워 Export 병합**(문구 앞 `TODO`) → 기획이 SVN 잠금 후 수정 | |
| D17 | 공격력 아이콘은 **임시 스프라이트** → 아트 교체 | |
| D18 | **칸 클릭 = 그 스킬 키 입력**(Q·E·RMB·R 칸) — `PlayerInputReader` 스킬 입력 경로에 가상 입력으로 넣는다(네트워크·권한 흐름 그대로) | 세부 C1~C7 아래 |
| D18-C3 | **방향형 스킬 방향 = 칸 위 커서 위치 그대로**(화면 아래쪽으로 나가는 것 = 의도) | 은희 확정 |
| D18-C1 | 칸 위 좌클릭은 **그 스킬 입력으로만** — 기본 공격(`Attack`)·조준 확정(`PlayerSkillTargeting.WasConfirmPressed`)은 막는다 | |
| D18-C2 | Hold 스킬 = 칸 위 마우스 누르는 동안 Held, 떼면 해제(칸 밖에서 떼도 해제) | |
| D18-C4 | 조준형(ClickToConfirm) = 칸 클릭은 **조준 진입만**, 같은 클릭으로 확정 안 함. 확정 = 이후 월드 클릭 · 조준 중 다른 칸 = 지금처럼 전환 | |
| D18-C5 | 칸 우클릭 = 동작 없음(조준 중이면 지금처럼 취소) | |
| D18-C6 | `Slot_P`·`Slot_Dash` 클릭 = 동작 없음 | |
| D18-C7 | 툴팁 표시 전(0.15초 안)에 클릭해도 동작 | |

## 3. 자리표시자

같은 대상의 필드 경로 = xlsx A열 표기 그대로. 다른 에셋 참조 없음.

| 쓴 것 | 값 | 표시 |
|---|---|---|
| `{cooldownTime}` | 8 | `8` |
| `{shieldDuration:0.0}` | 2.5 | `2.5` |
| `{healPercent:%}` | 0.15 | `15%` |
| `{dmg}` | 공격력 130 · 계수 1.8 · 고정 10 | `244⚔` · Shift: `244 = (10 + 180%⚔)` |
| `{dmg}` (차지 레이저) | 최소/최대 차징·과열 단계 | `120~360⚔` · Shift: `120~360 = (0 + 60%~270%⚔)` |

- `{dmg}` = 피해 색 + 공격력 아이콘(`<sprite name=atk>`) 자동. Shift 안내 줄은 `{dmg}` 가 있을 때만.
- 피해 계산은 **툴팁 쪽에서 따로** 한다(같은 SO 값 + `FinalAttackDamage`). **판정 코드는 바꾸지 않고**, 아래 세 곳에 "툴팁 계산과 같이 바꿀 것" 주석만 단다:
  가붕이·기본 `RoundToInt(FinalAttackDamage × 계수) + 고정` (`PlayerSkillController.cs:325`) ·
  차지 레이저 `× DamageMultiplierAt(0~1) × StageDamageMultiplier(0~최고)` (`GunnerChargeLaserSkill.cs:117`) ·
  추적 레이저 `× StageDamageMultiplier(0~최고)` (`GunnerTrackingLaserSkill.cs:49`).
- 없는 필드·형식 오류·닫히지 않은 `{`/`<style>` = **테이블 적용 단계 오류**(`GameData.xlsx › Paladin!C40`) — 테이블 모드 Play·빌드가 시작 전에 멈춤. Verify 에도 표시. 인스펙터 모드에선 툴팁에 `[오류: …]` 로 표시 + 경고 로그.

## 4. 툴팁 레이아웃 (D10)

```
┌──────────────────────────────────────────┐
│ [아이콘]  [Q] 정의의 일격           ⏱ 8초 │  키 배지 자동(P 는 없음) · 쿨타임 자동(P 숨김)
│           궁극기                          │  subtitle — 빈칸이면 줄 없음
├──────────────────────────────────────────┤
│ description (자리표시자·<style> 치환)     │  셀 안 Alt+Enter = 문단
├──────────────────────────────────────────┤
│        자세한 정보를 보려면 [Shift] 키를… │  {dmg} 있을 때만
└──────────────────────────────────────────┘
```
- 소모값 줄 없음(스킬 자원 소모 없음). 쿨감 스탯 없음 → 쿨타임 = `cooldownTime`(쿨감이 생기면 따라가도록 한 곳에서 읽음).

## 5. 구조

```
SkillTooltipText (직렬화 구조체)       displayName · subtitle · description(문자열, [DataTableText]) · icon(Sprite)
   ↑ 필드 `tooltip` 로 보유
PlayerSkillData(4+4 SO) · FirstMeleePassive(컴포넌트) · GunnerHeatData(SO)
   ↓
ISkillTooltipSource                    Tooltip · 값 출처(Object) · TooltipDamage(Player) → 최소/최대 + 계산식 항
   ↓
SkillTooltipFormatter (순수 C#)        자리표시자 파싱·치환·검증 — EditMode 테스트 대상
   ↓
SkillSlotHover (칸마다, IPointerEnter/Exit) → SkillTooltipView (CombatHUD 안 한 개, Shift 감시·위치 보정)
SkillCooldownHUD / PassiveHUD.Bind     → 출처에서 아이콘 갱신 + 칸 Hover 에 출처 연결
```
- xlsx 행: `tooltip.displayName` · `tooltip.subtitle` · `tooltip.description` (구역 안 `#  ─ 툴팁` 묶음).
- 과열 툴팁 출처 = `GunnerHeatData`(SO, 이미 `Gunner` 시트 대상) — `GunnerHeat` 컴포넌트가 아니라 데이터 SO 에 둔다.
- 데이터 테이블: Export 는 **`[DataTableText]` 가 붙은 문자열만** 템플릿에 내보낸다(`animatorStateName` 같은 다른 문자열은 계속 제외). `DataTableTemplate.cs:102` 확장.

## 6. 작업 순서

1. **데이터 테이블 문자열 내보내기** — `[DataTableText]` · Export·병합·Verify 가 문자열을 다룸 · 줄바꿈 셀 왕복 테스트.
2. **`SkillTooltipText` + 출처 10곳에 필드** · `ISkillTooltipSource` · 툴팁 피해 계산 + 판정 3곳 주석(동작 변경 0).
3. **`SkillTooltipFormatter`** + 테이블 적용 단계 검증 연결 + EditMode 테스트.
4. **UI**: `SkillTooltipView` 프리팹(`CombatHUD` 안) · 칸 Hover · TMP Style Sheet(키워드 스타일) · 임시 공격력 스프라이트 · 칸 아이콘을 출처에서 갱신 · 거너 `Slot_P` 표시.
5. **아이콘 연결**: 10칸 출처에 스프라이트 지정(가붕이 = 지금 칸 스프라이트 이전, 거너 = 있으면 지정 / 없으면 임시).
6. **xlsx**: Export 병합(임시 `TODO` 문구) → SVN 커밋 r363+ · `art-svn.json` 핀 갱신.
7. 문서: `data-table.md`(문자열·자리표시자·스타일 태그 쓰는 법) · CONTEXT 인수인계.

🔴 6 은 SVN 잠금 필요 · Unity 켜둔 채 가능(텍스트 에셋·xlsx 는 임포트 대상 아님). 프리팹 수정은 `CombatHUD.prefab`·SO 몇 개 — 켜둔 채 가능 범위.

## 7. 리스크

- Export 는 xlsx 서식을 지운다(기존과 같음).
- `[DataTableSheet]` 는 `Inherited = false` — 확인함: 10곳 모두 자기 클래스에 붙어 있음(`FirstMeleePassive` = `Player_Paladin` 대상, 과열 = `GunnerHeatData`).
- 새 xlsx(문구 행)와 새 코드는 **같이** 나가야 한다(옛 코드는 없는 필드로 오류).
- 판정 공식을 바꾸고 툴팁 계산을 안 바꾸면 숫자가 어긋난다 → 판정 3곳 주석으로 안내(공용화는 하지 않음 — 은희 결정).
- 거너 아이콘 아트 없을 수 있음 → 임시.

## 8. 검증

- EditMode(Claude 직접): 자리표시자 파서(형식·범위·오류 위치) · 툴팁 피해 계산(계수·고정·차징·과열 범위) · 문자열 Export/Import 왕복(줄바꿈 포함) · 기존 데이터 테이블 테스트 전부.
- Verify 0 · 컴파일 0 경고 신규.
- Play(은희 수동): 가붕이·거너 각 5칸 호버 · Shift 전환 · 아이콘 · 공격력 버프 중 수치 변화 · 테이블/인스펙터 모드 · MPPM 클라.
