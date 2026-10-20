# CONTEXT.md - Shared Project Language

> 🆕 **새 환경에서 처음 여는 사람은 [Docs/tech/environment-setup.md](Docs/tech/environment-setup.md) 부터.**
> 이 프로젝트는 git 만으로 안 선다 — 아트가 SVN 에 있고, 없어도 Unity 는 조용히 열린다.
> 열기 전에 `python Docs/tech/check-environment.py` 를 돌릴 것.

This file defines the shared vocabulary for the project. Keep it concise. It is not a full spec and should not contain implementation plans.

Update this file when a term becomes important enough that future agents or teammates must use it consistently.

## ▶▶ 작업 세션 (2026-10-20 · 경석(Claude) · **보스 앞뒤 구분 끄기 · 점프×3 착지 간파 창 · PLAN 정리 · 레이드 새 페이즈(간파 릴레이·연쇄 간파)**, 브랜치 `feature/Boss23`) — ① ② ✅ 구현(미커밋) · 컴파일 0 · 전투 EditMode 257/0 · 보스 패턴 10/0 · ⏳ 경석 Play · ③ ✅ 정리(미커밋) · ④ 계획 [PLAN-boss-raid-relay.md](PLAN-boss-raid-relay.md) ✅ 승인 · ✅ 구현(미커밋) · 전투 EditMode 279/0 · ⏳ 경석 Play — `No23_Raid` 20% = 간파 릴레이(자폭은 enum 그대로), 연쇄 간파는 phases 에 `RaidChainCounter` 행 추가로 사용 · 은희 요청서 [handoff-raid-relay-lock.md](Docs/tech/handoff-raid-relay-lock.md)
- Play 1차(10-20) 반영: 릴레이 방향 = 화면 아래 투영(✅ 경석 확인) · 1인 밀림 ×2 · 점프×3 착지 간파 = 피해 직후 0.8초 창(2차: 자세 정지 없앰 — 착지 클립 이어 재생 → 이동 자세 크로스페이드, 끊긴 클립 루프 이펙트 끔) ✅ 경석 확인. 릴레이 안전 구역 = 6칸 + 꼭대기 위 3칸(`relaySafeExtendApex`). 디버그 단축키 정리 — 보스 단타 예약 5종 삭제, **F3 = 점프·점프·점프 콤보 예약**([debug-hotkeys.md](Docs/tech/debug-hotkeys.md)). ⏳ 점프×3 Play.
- ③ 결과: 경석 PLAN 14개 → `Docs/history/PLANS/`(링크 자동 갱신 — CONTEXT·handoff·telegraph-style·raid-23-phases·`PLAN-gunner.md` 링크 1줄). `PLAN-mcp.md` 는 gitignore 개인 노트라 그대로. 남김: boss-raid·flatkit·cleanup-optimization.
- 확정(경석 10-20): ① `BossDataSO.useFacingRules` 하나(기본 꺼짐) — 간파 정면 판정·위치 백어택·앞뒤 바닥 표시를 함께 끈다(어쌔신 강제 백어택은 유지). ② 점프·점프·점프 3번째 타 = 착지 피해 직후 간파 창 0.5초(콤보 타 데이터) — 성공 = 기존 간파 보상, 실패 = 그대로 전투. ③ 경석 PLAN 완료분 → `Docs/history/PLANS/`, `PLAN-enemy-indicator-c3.md` 삭제(다른 팀원이 진행). ④ 레이드에 간파 릴레이·연쇄 간파 페이즈 2종을 만들어 두고 배치는 경석이 조합 — grill → PLAN 후 구현.
- SVN 아트 r478(은희 `P_HumanoidBot.prefab`) 받음 · 충돌 0 · ⏳ 핀 477 → 478 은 git 커밋 때.
- 🔒 수정: `Monster/Boss/TwentyThreeBoss.cs`(+ 신규 partial `TwentyThreeBoss.RaidRelay.cs`) · `BossRelayRoster.cs`(신규) · `BossDataSO.cs` · `BossComboEntry.cs` · `BossDirectionIndicator.cs` · `Monster/MonsterBase.cs` · `Monster/Editor/{MonsterInspectorCatalog,BossEntryDrawers,BossRaidVariantAuthoring,BossRelayRosterTests}.cs` · `Editor/CombatEditModeTestRunner.cs` · `No23.asset`·`No23_Raid.asset` · `TutorialGuideTexts.asset` · 루트 `PLAN-*.md`(경석분 이동)

## ▶▶ 작업 세션 (2026-10-20 · 경석(Claude) · **인게임 ESC 일시정지 메뉴 + 종료 확인**, 브랜치 `feature/Boss23`) — 계획 [PLAN-pause-menu.md](Docs/history/PLANS/PLAN-pause-menu.md) ✅ 승인 · ✅ 구현 · 데이터 테이블 239/0 · 로컬라이제이션 150/0 · ✅ 경석 Play · ✅ 커밋(git · SVN **r477**, 핀 477)
- 🔸 **은희**: 바탕화면/로비 나가기 처리는 비워 둠 → `PauseMenuView.ExitToDesktopRequested`·`ExitToLobbyRequested` 에 연결. 상세 [handoff-pause-menu.md](Docs/tech/handoff-pause-menu.md). 설정창 나가기 버튼 꺼짐 → 호스트 결과 화면 경로가 UI 에서 빠진 상태.
- ESC 패널(`OptionPanel`) = 화면 전체 반투명 검정 + 캔버스 정렬 1000(HUD 위). 타이틀 설정창 세로 위치 = `TitleFlowDirector._titleSettingsOffsetY`(+60). 타이틀 메뉴 3단 +170 올림(SETTING = 예전 START 자리, `TitleRigAuthoring` 도 같이).
- 🔴 **병합 Export 주의**: 하면 Map 시트 엘리베이터 16행에 r470 에서 지운 Elevator 열(D)이 되살아난다. 이번엔 Strings 행만 직접 넣었다.
- 🔒 수정 파일: `UI/Pause/**` · `Managers/MapSceneManager.cs` · `Localization/UiStrings.cs` · `Settings/Editor/SettingsFontAssetAuthoring.cs`·`SettingsHostSceneAuthoring.cs` · `2.Prefabs/UI/PauseMenu.prefab`·`Textures/PauseSelectRing.png` · `4.MapScene.unity` · `StringTable.asset` · `art-svn.json`

## ▶▶ 작업 세션 (2026-10-20 · 경석(Claude) · **설정창 UI 새 아트(SVN r466 UI_Panel) 교체**, 브랜치 `feature/Boss23`) — 계획 [PLAN-settings-art.md](Docs/history/PLANS/PLAN-settings-art.md) ✅ 승인 · ✅ 구현 · ✅ 경석 타이틀 Play · 설정 EditMode 26/0 · 플레이어 508/0 · ⏳ ESC(맵) Play · ✅ 커밋(SVN r476 · git)
- 🔸 **git 과 SVN 을 같이 받을 것** — SVN **r476**(핀 476) = UI_Panel 스프라이트(r466) + 서울알림체 `UI/Fonts/`(r476). 설정창 프리팹이 둘의 guid 를 참조한다.
- 다시 만들 때: `Tools/UI/Settings/설정 패널 항목 배치 (멱등)` → 새 아트까지 입힌다. 씬 옛 틀은 `설정창 옛 틀 끄기 — 타이틀·ESC (멱등)`. 폰트는 `설정창 글꼴 에셋 만들기 (서울알림체)`.
- 서울알림체 = 공공누리 제1유형(출처 표시) — 크레딧에 표기 필요.
- 🔒 수정 파일(동시 수정 주의): `Assets/2.Prefabs/UI/SettingsBody.prefab` · `Assets/1.Scripts/Settings/**`(UI·Editor·`SettingsUiStrings`) · `Managers/TitleOptionsPanel.cs` · `Managers/MapSceneManager.cs`(닫기 연결만) · `UI/Title/TitleFlowDirector.cs`(닫기 연결만) · `0.Scenes/MainFlow/1.TitleScene.unity`·`4.MapScene.unity`(옛 틀 끄기) · `Docs/tech/art-svn.json`(핀 457 → 476) · SVN `Assets/50.Art/UI/Fonts/`(서울알림체 Heavy — 신규)
- SVN r457 → r476 update·커밋 완료(충돌 0). r462 거너 이벤트도 핀 안에 들어가 `PlayerAnimationEventRequirementTests` 통과(플레이어 EditMode 508/0).

## ▶▶ 작업 세션 (2026-10-17 · 은희(Claude) · **어쌔신 비주얼 v18 반영 — Flat Kit 재질 + 키 ×1.25**, 브랜치 `feature/AssassinVisual`) — 계획 [PLAN-assassin.md](PLAN-assassin.md) §9 ✅ 승인(기본안) · ✅ V1~V6 — 은희 Play 확인 · SVN **r457**(핀 457) · development 병합·푸시 · EditMode 전체 1793 / 0(건너뜀 20)
- 🔸 **git 과 SVN 을 같이 받을 것** — SVN r457 = 어쌔신 `Assassin.fbx`(v18g)·`.meta`(임포트 Scale Factor 1.25)·`Textures/Assassin_BaseColor.png`(T3H). git 쪽 단검 배율·머리카락 길이 값·`Run_Loop` 0.8 은 이 아트를 전제한다. 한쪽만 받으면 키·단검·머리카락이 어긋난다.
- ✅ 동시 수정 잠금 해제(10-17 병합·푸시). 고친 곳: `2.Prefabs/Player/Assassin/Assassin_Armature.prefab` · 신규 `3.Materials/FlatKit/Player/Assassin/FK_Assassin_Body.mat` · `4.Animations/Player/Assassin/AssassinAnimatorController.controller` · `1.Scripts/Player/Editor/AssassinHairAuthoring.cs`·`AssassinShellAuthoring.cs`·`AssassinHairPrefabTests.cs`·신규 `AssassinVisualPrefabTests.cs`·`PlayerEditModeTestRunner.cs` · `1.Scripts/Rendering/Editor/FlatKitPlayerVariants.cs` · `Docs/tech/art-svn.json`·`player-prefabs.md`(§7 5-1·§13·§14) · `Docs/design/character/character_assassin.md` · `PLAN-assassin.md` §9.
- 10-17 에디터 메뉴 실행(MCP): `Tools/Player/Assassin/머리카락 본 연결`(재캡처, 은희 허락) · `Tools/Rendering/Flat Kit/플레이어 Variant 구성`(재실행 안전 확인 — 해시 19개 동일) · 플레이어·튜토리얼 EditMode 러너.
- ⏸ 후속(범위 밖): 텍스처 남은 항목·v18.2 눈·입 UV 밀도(10/22 Codex 한도 해제 뒤 — PLAN §9 끝) · `Assassin_BaseColor` 4096 무압축(약 85 MiB → 4096 BC7 권장) · 판정 거리를 커진 몸에 맞출지 기획 확인 · 변신 R·Q 일섬 VFX 배율(민경) · 단검 전용 텍스처 · 로비 초상화 · `Run_Start`/`Run_Stop` 속도.
- `Docs/tech/player-prefabs.md` 를 잠근 「거너 레이저건 정렬」 세션(`fix/GunnerGunAlign`)은 이미 병합됐다(`1d2e4b22`) — 그 잠금은 끝난 것으로 본다.

## ▶▶ 반영 (2026-10-19 · 경석(Claude)) — `feature/Boss23` → `development` 병합·푸시 `6a59ae53`(origin/development 30여 커밋 선병합 `eac7ea01`, 충돌 0)
- 배치 모드 EditMode 전체 1942 — 통과 1921 / 실패 1 / 건너뜀 20 · error CS 0.
- 🔴 실패 1 = `PlayerAnimationEventRequirementTests` — 거너 `gunner_skill_RMB_interrupt` 의 Hit·End 이벤트 누락. **이 작업과 무관.**
  원인: 은희가 SVN **r462** 에 `gunner.fbx.meta` 클립 이벤트를 올렸는데(git `feature/GunnerAnimEvents` 짝) **아트 핀 `art-svn.json` 은 457 그대로** → 핀대로 받은 사본은 이벤트가 없다. → ✅ 10-20 설정창 작업에서 핀 476 으로 올려 해결.
- SVN: 이 작업은 아트 변경 0. 원격 SVN 최신 r467 · `GameData.xlsx` 는 다른 작업 사본에서 잠김(기획 편집 중으로 보임).

## ▶▶ 작업 세션 (2026-10-19 · 경석(Claude) · **23호 콤보 즉시 연결 + 훅·어퍼 바닥 예고 토글**, 브랜치 `feature/Boss23`) — 계획 [PLAN-boss-combo-chain.md](Docs/history/PLANS/PLAN-boss-combo-chain.md) ✅ 승인 · ✅ 구현(미커밋) · 컴파일 0 · 전투 EditMode 257/0 · Verify 필드 없음 0 · ⏳ 경석 Play
- 수정: `TwentyThreeBoss.cs`(즉시 연결·후속 타 분기·토글) · `BossComboEntry.chainOnHit` · `BossDataSO` 새 칸 5(comboDashWindup 0.7 · comboGrabTelegraph 0.3 · comboJumpRecovery 0 · comboDashRecovery 0.3 · hookTelegraphMarkers) · `BossComboPolicy`(+테스트) · 인스펙터 사전 · 에셋 No23·No23_Raid(콤보 6종 즉시 연결 · 새 HookHookHook w15·cd8 · 바닥 예고 끔). 개전 콤보는 즉시 연결 제외.
- 같이 미커밋: 앞 단계 콤보 가중치(돌잡·점돌 w25·cd8 · UpperGrab) · PLAN-boss-balance.
- 2차(경석 Play 후): 점프→돌진 이펙트 잔존 수정(끊긴 클립 루프 끄기 `StopCutClipLoopsClientRpc`) · 단타 훅·어퍼 끔(`singleHookUpper`) · 훅·어퍼 히트 뒤 0.3초(`comboHookFollowThrough`) · 돌진 다음 잡기 예고 1초(`comboGrabTelegraphAfterDash`) · 새 콤보 JumpJumpJump w21·cd12(15 → 1.3배 빈도). 컴파일 0 · EditMode 257/0 · ✅ 커밋 `f976803b` + 가중치 조정 커밋.
- ⚠️ 이 세션 중 Unity MCP 브리지(node, `mcp-bridge-launcher.js`)가 CPU 루프로 멈춰 PID 종료로 복구 — 메모리 [unity-mcp-bridge-hang] 대처 그대로 통했다.

## ▶▶ 작업 세션 (2026-10-19 · 경석(Claude+Codex) · **23호 공격 빈도 밸런스**, 브랜치 `feature/Boss23`) — 계획 [PLAN-boss-balance.md](Docs/history/PLANS/PLAN-boss-balance.md) ✅ 승인
- ⚠️ P1(가중치 10·쿨 16/12) → 경석 Play 에서 두 콤보가 한 번도 안 나옴 → **P1' 적용(미커밋)**: `DashGrab`·`JumpDash` 가중치 25·쿨 8 + **신규 `UpperGrab`(어퍼→잡기 무조건) 가중치 10·쿨 10** (No23·No23_Raid). 🔴 콤보 빈도 손잡이는 쿨이 아니라 **가중치**(첫 타 단타 쿨이 실제 제한). 시뮬 1인 근 기존 6.5초 · 레이드 7.7초.
- ⏳ Play 체감(기존·레이드 1인 근 · 가능하면 원거리 2인) · P2(기존 전역 간격 0.75→1.0)·P3(잡기 예고 0.7→1.0) = 경석이 기획에 전달(xlsx).
- 핵심: 원거리만 있는 조합이 가장 빡빡(5~6초)한데 숫자로는 거의 안 움직인다(주기 구조) — Play 로 보고 필요하면 규칙 논의. 시뮬 스크립트는 로컬(scratchpad) — 공유 여부 미정.

## ▶▶ 다음 세션 (경석) — 미사용 8칸 삭제부터
- 순서: ① 경석이 GameData.xlsx(Boss·Monster 시트)에서 8칸 행 삭제 → `svn update` → 충돌 0 → SVN 커밋 → 아트 핀 갱신 ② Claude 가 그 리비전에서 행이 다 빠졌는지 확인 → 코드에서 8칸 삭제 → 컴파일 · 전투 EditMode · Verify "필드 없음" 0.
  8칸 = `maxShield`(Monster·Boss 둘 다) · `grabThrowDistance` · `bombThrowInterval` · `spreadAngle` · `raidSelfDestructCounterBonus` · `raidSelfDestructCounterInterval` · `raidSelfDestructCounterWindow` · `raidSelfDestructDamageReduction`.
  🔴 순서를 거꾸로 하면 테이블 Play·빌드가 "필드 없음"으로 막힌다(10-19 f5b48b27).
  같이 고칠 곳: `MonsterInspectorCatalog.cs` 의 Unused 줄 · `TwentyThreeBoss.cs`(grabThrowDistance 경고 · bombThrowInterval → `BossWells.ConfigureCycle`) · `BossDataSO` 의 미사용 주석.
- ⏳ 남은 확인: `attackWindup` 선딜 MPPM 2인 · 팀장·기획 인스펙터 눈 확인(★ 목록) · 다중 선택·Undo.
- 미커밋: `PLAN-monster-data-inspector.md`·`CONTEXT.md`(8칸 표기 정정·이 인수인계) — 다음 커밋에 같이. `Docs/design/boss/raid-23-phases.html` 은 이 작업과 무관(추적 안 함).

## ▶▶ 작업 세션 (2026-10-19 · 경석(Claude+Codex) · **몬스터 데이터 인스펙터 + 선딜 연결 + 죽은 칸 삭제**, 브랜치 `feature/Boss23`) — ✅ 커밋(미푸시) · 계획 [PLAN-monster-data-inspector.md](Docs/history/PLANS/PLAN-monster-data-inspector.md) · [PLAN-attack-windup.md](Docs/history/PLANS/PLAN-attack-windup.md) · 조사 [monster-data-survey-1019.md](Docs/tech/monster-data-survey-1019.md)(333칸)
- `b54596df` 인스펙터 1차 — `Monster/Editor/` `MonsterInspectorCatalog.cs`(칸 이름·묶음·★ 는 여기 한 곳) · `MonsterDataSOEditor.cs` · `BossEntryDrawers.cs` · `MonsterInspectorGUI.cs` · `DataTableInspectorBadge.TryGetWrite`. 표 값 ≠ SO 값은 맞추지 않는다(xlsx 가 정본 — 경석).
- `2d9efa34` `attackWindup` = Chomp·Humanoid 예비동작 길이(÷ attackSpeed). 히트는 애니 이벤트 그대로. Humanoid 0.73 → 0.55(경석 결정). 경석 Play 확인.
- `4f6bd2eb` 죽은 칸 6개 삭제(xlsx r461 행 0). 2차(장판·드론·예고 스타일·중간보스)는 안 한다.
- 검증: 컴파일 0 · 전투 EditMode 246/0 · Verify 필드 없음 0 · 차이 96.
- ⚠️ `[DataTableSheet]` SO 칸을 지우거나 이름 바꾸기 전에 **지금 xlsx** 를 다시 보고 Verify("필드 없음" 0).

## ▶▶ 작업 세션 (2026-10-18~19 · 경석(Claude) · 23호 콤보 · 레이드 ④ 실드 · HUD 레이캐스트, 브랜치 `feature/Boss23`) — ✅ 커밋·푸시 · SVN r457(핀 457)
- 콤보 = [PLAN-boss-combo.md](Docs/history/PLANS/PLAN-boss-combo.md) — 룰렛 가중치(훅·훅·돌진 20 · 훅·어퍼 15 · 돌진·잡기 15 임시, 팀장이 정함) · 0.3초 · 추격 유예 1초 · 어그로 고정 · 개전 오른훅→왼훅→돌진(기존·레이드, 1인 제외). Codex 교차검증 반영.
- 레이드 ④ 자폭 = 체력바 실드(간파 게이지·창·전기 구슬 제거) · ③ 구출 자막 제거. 간파 보너스·창 칸은 xlsx 행 때문에 **미사용으로 남김**(다음 병합 Export 때 정리).
- HUD: 플레이어 상태·키가이드 Raycast Target 끔(미니맵·보스 체력바·보스 타이머는 원래 끔, 스킬 슬롯은 툴팁용 유지). 23호 `hitVFXCollider` = HurtBox 연결(기존·레이드 — 비어 있어 피격 이펙트가 안 나오고 경고만 찍혔다).
- 📝 메모(손대지 않음): `4.MapScene` `UI/Canvas/Fade_Image` 가 Raycast Target 켜진 전체 화면 이미지다. 지금은 **꺼진 채 아무도 안 켜서** 무해하지만, 누가 켜면 그 캔버스 UI 클릭을 전부 막는다 — 쓰게 되면 Raycast Target 부터 끌 것.

## ▶▶ 작업 세션 (2026-10-16 · 경석(Claude) · 간파 슬로우·스피너·드론 재질, 브랜치 `feature/Boss23`) — ✅ 커밋·푸시 · SVN r454(핀 454)
- 간파 슬로우 모션 = 23호·중간보스 3종에 연결(`8fc8c0a1`, 은희 handoff-interrupt-slowmo). 튜토리얼 연습 스피너는 D18 대로 안 걸림.
- 스피너 회전 = 예고가 다 찬 뒤부터(준비 동작을 예고 시간에 맞춰 늘임 · `MonsterBase.AnimatorSpeedScale`).
- 웰즈 드론 회색 = DRONE.fbx 에 머티리얼 없음 → `WellsDrone.prefab`(래퍼) + `DRONE_Mat`(SVN r454, URP Lit 베이스+노멀). 저작 `Tools/Boss/웰즈 드론 — 재질·래퍼 프리팹 만들기`.
- 레이드 테스트 빌드 메뉴 `Build/Windows64 Player (MainFlow · 레이드 보스)`(BOSS_RAID_BUILD, 출력 `MainProjectBuilds/WindowsRaid`). ⚠️ 빌드 후 ProjectSettings preloadedAssets·link.xml 부산물 되돌릴 것.
- ⏸ 보류: Stage1 `Slot_8_Z9_M` 배치 어긋남(은희 보고) — 팀장 나중에 확인. 전투 시스템 개정(제압 칸·간파 성공 넉백+그로기→취약 8초·취약 중 CC 패턴 끊기)은 칸 UI 회의 후, 보스 넉백 ↔ UnitWallImpacts 연결은 은희와 상의.

## 작업 세션 (2026-10-14 · Codex · 로비 전선다발 통합)
- 완료(v022): 현재 Art/lobby 씬에 바닥·벽면 배선 2개 배치, `PF_Lobby_CableRouting_v022` 별도 저장. 신규 모델/부품 프리팹 11종, 기존 v006 베이스컬러 재사용. 기존 1,451개 오브젝트 배치·메쉬·재질 및 콜라이더 339개 보존. Blender/FBX 검수·Unity Scene View 확인 완료(Play 미수행).
- 사용자 승인: 제작한 전선다발을 Unity에 넣고 현재 로비 배치에 맞춰 연결. 신규 모델·프리팹·공유재질 추가, 현재 Art/lobby 씬의 배선 추가만 수행. 책상·플랫폼·전면 캐비닛의 사용자 배치를 유지. 기록: aiworkspace/90_조립검수/v022.

## 작업 세션 (2026-10-14 · Codex · 로비 연습장 시멘트 바닥)
- 후속 명도 조정(v021): 연습장 전용 시멘트 머티리얼 BaseColor/Color를 0.72로 낮춰 저장. 텍스처·UV·메쉬는 그대로 유지, 8개 바닥만 영향. 동일 각도 Scene View 확인 완료.
- 사용자 요청: 연습장 4×4m 바닥 8개에 시멘트 베이스컬러 적용. 홀 진입부와 락커룸은 제외.
- 수정 범위: 신규 바닥 FBX·공유 아틀라스 v006·전용 머티리얼 추가, `Art/lobby.unity` 및 로비 건축/Complete 프리팹의 8개 바닥 참조. 기존 바이너리·GUID·배치·콜라이더 보존.
- 현재 열린 씬의 저장 전 상태를 별도 백업한 뒤 에디터에서 반영. 검수 기록: `D:/p_MT/lobby/aiworkspace/90_조립검수/v020/validation`.
- 완료: 시멘트 아틀라스 v006·바닥 v004 추가, 연습장 8개 씬/프리팹 교체 및 저장. 사용자 작업 중 추가한 프랍도 보존. 배치/콜라이더/UV2 검증 통과, Unity Scene View 확인 완료(Play 검증은 미수행).
## ▶▶ 작업 세션 (2026-10-14 · 경석(Claude) · **23호 위협 어그로 + 일반 몬스터 추적 해제**, 브랜치 `feature/Boss23`) — 계획 [PLAN-boss-aggro.md](Docs/history/PLANS/PLAN-boss-aggro.md) ✅ 승인(10-14) · ✅ 구현·커밋 · 컴파일 error CS 0 · 전투 EditMode 176/0(신규 27) · 보스 패턴 10/0 · ✅ 팀장 Play 확인(10-14)
- 정정: Gauntlet·Spinner·WallBot 은 중간보스(`rank 1`)라 `loseTargetRadius` 0 — 적용 = 일반 7종.
- `MonsterDataSO.loseTargetRadius`·`BossDataSO.threat*`·`farPressure*` 는 xlsx 에 행이 없어 SO 값으로 돈다(테이블 반영은 병합 Export 때).
- `169da399`: 레이드 장판 상시화(①③ 교차 · ②④ A/B · 간격 4.2초) · ④ 자폭 HP 피해 90% 감소 · ② 송전탑 HP ×2. 이어서 ④ 자폭 30초 · 게이지 750(1인, 300 → 750 — 팀장 "너무 빨리 파훼").
- ✅ 동시 수정 잠금 해제(10-14 커밋·푸시). 고친 곳: `Monster/Boss/TwentyThreeBoss.cs` · `BossAggroPolicy.cs` · `BossDataSO.cs` · 신규 `BossThreatTable.cs` · `Monster/MonsterBase.cs` · `MonsterDataSO.cs` · `MonsterPerceptionPolicy.cs` · `2.Prefabs/Monster/Data/` 일반 몬스터 7종 `.asset` · 테스트.

## ▶▶ 다음 세션 (경석) — 팀장이 `feature/Boss23` Play 테스트 후 지시. 지금 상태 = development 에 병합·푸시(10-14), SVN **r438**(핀 438 — r437 은희 로컬라이징 Export · r438 레이드 자폭 30·750 셀).
- ✅ 10-14 `origin/development`(사운드 동기화 5커밋, PR #31·#32) 병합 — 충돌은 이 파일만. 사운드 데이터가 SVN 으로 이동해 **`Assets/51.Audio` 를 `^/4_Resources/Sound` r436 으로 체크아웃**(이 PC 는 처음, 이전엔 빈 폴더). 옮겨진 13개 .meta guid 동일 · 컴파일 error CS 0 · ⏳ BGM·UI_Click Play 청취 미확인.
- 테스트 대상: 레이드 ①~④(④ 실패 즉사기·자막 3종) · 점검 수정 17건 + P1(공격 슈퍼아머 해제)·P2(송전탑 상승 여유)·P3(레이지 예고)·P4(방전 유지) — 전부 **Play 미확인**.
- 기획 메모 = [raid-23-phases.md](Docs/design/boss/raid-23-phases.md) 「추가할 것」 칸.

## ▶▶ 작업 세션 (2026-10-14 · 경석(Claude+Codex) · **보스·몬스터 전체 점검** ✅ 완료(동시 수정 잠금 해제), 브랜치 `feature/Boss23`) — 범위 = 23호(기존·레이드)·웰즈·장판·송전탑·디렉터 · 중간보스 3 · 일반 몬스터 · MonsterBase·간파·예고·투사체 등 공용 부품. 항목 = 버그·예외 경로 / 네트워크·권한 / 예고·판정 일치 / 문서·데이터 불일치. 처리 = 확실한 것은 바로 수정, 판단 필요한 것은 목록(팀장 10-14).
- ✅ ③ 2인 잡기 구출 팀장 Play 확인(10-14) — 레이드 4구간 전부 검증. 기존/레이드는 툴바로 계속 비교.
- ✅ 점검 결과·수정 = [boss-monster-audit-1014.md](Docs/tech/boss-monster-audit-1014.md) · 수정 커밋 `b9280888`(확실한 버그 17건, 컴파일 0 · 전투 149/0 · 보스 패턴 10/0, ⏳ Play 미확인). 팀장 결정: P1(공격 슈퍼아머 공격 끝나면 해제)·P2(송전탑 상승 여유를 차징 대기에)·P3(레이지 띠 예고, 타이밍 유지)·P4(방전 중 제압 무시) 반영. WallBot·데이터 테이블·테스트 씬 = 안 함.

## ▶▶ 작업 세션 (2026-10-14~ · 경석(Claude) · **23호 레이드 변형(지스타)**, 브랜치 `feature/Boss23`) — 계획 [PLAN-boss-raid.md](PLAN-boss-raid.md) ✅ 승인(10-14) · W1 진행 · 회의 기록 [raid-rework-meeting-1014.md](Docs/design/boss/raid-rework-meeting-1014.md)
- ✅ W1 `b9be4190`: 변형 프리팹·데이터·툴바 "보스: 기존/레이드"(에디터 툴바 우클릭 → Dev/Boss Variant 체크해야 보일 수 있음)·구간 예약 큐·인원 HP·즉사 상한·웰즈 자막. 저작 = `Tools/Boss/레이드 변형 — 만들기`.
- ✅ 10-14 `origin/development`(44커밋: 튜토리얼 구간 스킵 Alt+1~0 · MapScene 그룹 6개·매니저 13개 프리팹화 · 거너 회전 잠금 · 에임 3D) 병합 + SVN **r434**(핀 434). MapScene 충돌 22곳은 **dev 씬 기준 + 우리 설정창 변경(옛 77블록 삭제·새 2블록·OptionPanel 자식)만 재적용**으로 해결(끊긴 참조 0). `raidBossPrefab` 은 매니저 프리팹 `Managers/BossEncounterDirector.prefab` 에 연결.
- ✅ 10-14 W2 `ec1985d8` · W3 `485b3f16` · 전투 기록 로그 `35bfffd5` · HP 상향 `f25f4c45` — **각본 4구간 조합 완료**(팀장 Play: ①~④·엔딩·방전 안전 칸 확인). 1인 2880 / 2인 7200 / 3인 13104.
  ⏳ 남은 것: 측정 플레이(`[23호/전투기록]` 로그 — 기존 vs 레이드 · 1/2/3인) → 수치 조정 · 빌드 기본 보스 결정(지금 빌드는 항상 기존). 2인 MPPM 의 ③ 잡기 구출은 아직 Play 미확인.
- 로아 발탄·쿠크식 구간 각본 4개(기동 시연 · 드론 유도 송전 · 구출 · 자폭 공동 게이지).
- 📄 **구간별 진행 설명(기획용, 팀장이 「추가할 것」 칸에 덧붙임)** = [raid-23-phases.md](Docs/design/boss/raid-23-phases.md). 10-14 추가 변경: ④ 실패 = 즉사기 · 자막은 규칙 안내 3종만. **기존 23호(`TwentyThree.prefab`·`No23.asset`)는 그대로**, 변형(`TwentyThree_Raid`·`No23_Raid`) 추가 후 비교.
- 🔴 **수정 예정 — 동시 수정 금지**: `Monster/Boss/TwentyThreeBoss.cs` · `BossDataSO.cs` · `WellsDroneAttack.cs` · `BossChargingPylon.cs` · `BossElectricFloor.cs` · `Map/BossEncounterDirector.cs` · `4.MapScene.unity`(BossEncounterDirector 칸만) · 신규 `TwentyThree_Raid.prefab`·`No23_Raid.asset`.

## ▶▶ 작업 세션 (2026-10-13 · 경석(Claude) · **공격 예고 색·마스크 + 일반 몹 예고(Chomp·Humanoid·Mortar)**, 브랜치 `feature/Boss23`) — 계획 [PLAN-telegraph-style.md](Docs/history/PLANS/PLAN-telegraph-style.md) · [PLAN-monster-tells.md](Docs/history/PLANS/PLAN-monster-tells.md) ✅ 완료·팀장 확인 · 커밋·푸시(동시 수정 잠금 해제) · SVN 커밋
- 아트 전달 = [Docs/tech/telegraph-style.md](Docs/tech/telegraph-style.md). 예고 색·마스크 정본 = `2.Prefabs/Monster/Data/TelegraphStyle_Default.asset`(몬스터 데이터 12개 연결).
- Mortar = 수직 낙하 포격(`rangedStyle` SkyStrike). 🔴 `skyStrike*` 칸은 xlsx 미반영([DataTableIgnore]) — 테이블로 옮기려면 병합 Export.
- 10-13 후속 ✅(커밋): 기존 테스트 실패 2건 해결 — "최원거리 타깃은 Jump 전용" 옛 규칙(테스트·런타임 계약·주석)이 09-17 G3(돌진 = 최원거리) 뒤 미갱신 → "최원거리 = 거리창 끈 행만(Jump·Dash)"으로 정정, 전투 EditMode 125/0. 설정 비네트: 방향 반전 수정(`VignetteOpenScale`) · 실드 비네트 연결 · 최소에서도 모서리 유지 · 표시 0~100(50 = 기본). "후처리 (블룸 등)" → "블룸·색수차". 팀장 확인 OK.
- ⏸ 보류: "비네트" 용어(플레이어가 모를 수 있음 — 후보 "화면 가장자리 어둡기/부드러움") — 팀장 추후 수정.
- ✅ **10-13 `origin/development`(`b21f6e52`, 71커밋 — 튜토리얼·공용 목숨·체력 재생·간파 쿨·대시 아이콘·로비) 병합**. SVN **r431**(핀 431, check-environment 전부 통과). 충돌 2: `PlayerEditModeTestRunner.cs`(양쪽 테스트 다 살림) · `4.MapScene.unity`(옛 옵션 패널 77블록은 설정창 쪽대로 삭제 유지, dev 튜토리얼 신규 10블록 — `TutorialGuideUI`·`TutorialTraitUI`·`TutorialDirector` — 만 살림. dev 가 옛 블록을 수정한 것 0 · 끊긴 fileID 0 · SceneRoots 39). Codex 교차검증 동의. 컴파일 error CS 0 · EditMode 1284 중 1264 통과 / 실패 0 / 건너뜀 20(EuniTween Ignore).
- ▶ **후속(미착수, 튜토리얼 담당과 공유 필요)**: 튜토리얼 안내 문구 `TutorialGuideTexts.asset`(46·99·120·210·237행)이 **Space·Q·E·F·R 고정 표기** — 설정창 키 리바인드 후 안내와 실제 키가 어긋난다(입력은 `PlayerInputReader`·`ZoneBridgeGateManager` 에서 리바인드를 따름). 수정 = 문구에 키 자리표시 + `KeyBindingConfig.DisplayName()` 연동. (Codex 머지 검토에서 발견)
- ⏳ 병합 후 Play·MPPM(튜토리얼 흐름 + 새 설정창 ESC 겹침·입력 차단) 미확인.
- ✅ 10-13 `12d4bf1a`(Boss23 원격만): 존1 허수아비 미표시 수정 — `TutorialZone1Flow` 가 로딩 중(활성 씬 = `2.LoadingScene`) `Instantiate` 해 로딩 씬 언로드 때 같이 파괴됐다 → 스폰 전 `MoveGameObjectToScene(StageTutorial 씬)`. 팀장 Play 확인. development 에도 같은 버그가 있다(이 커밋이 들어가야 고쳐짐).
- ▶ **다음 작업 후보(팀장 10-13)**:
  1. **영어 대응** — ✅ HUD 키가이드만 연결(10-13): 신규 `Settings/Localization/LocalizedSprite.cs`(`LocalizedText` 의 이미지판) 를 `CombatHUD.prefab` `KeyGuide` 에 붙여 English 면 SVN r429 `keyguide_en.png`(은희), 한국어면 `keyguide.png`. ⏳ **튜토리얼 문구(`TutorialGuideTexts.asset`) 영어 번역은 나중**(팀장 — 바꿔야 함, 위 키 표기 자리표시와 같이 설계).
  2. **송전탑 상승 높이** — ✅ `riseHeight` 1 → **0.5m**(`bossroom.prefab` 4개, 팀장 10-13 "일단 절반"). 숨김 기준 위치는 그대로. ⏳ Play 체감 확인.

## ▶▶ 작업 세션 (2026-10-14 · SimJangBounce(Claude) · **사운드 동기화 — xlsx 로 라이브러리·엔티티·클립·SoundID 연결**, 브랜치 `feature/SFX`) — 계획 [PLAN-sound-sync.md](PLAN-sound-sync.md) ✅ 승인(10-14) · ✅ S1~S5 구현(미커밋) · EditMode 93/93 · 실제 에셋 엔드투엔드 18/18 · ⏳ xlsx 병합 Export(사람) · Play 청취
- 🔴 **수정 예정 — 동시 수정 금지**: `1.Scripts/Sound/SoundCatalog.cs`(10-14 이동) · `51.Audio/Data/SoundCatalog.asset`·`51.Audio/Data/SoundLibrary/`(10-14 SVN 으로 이동) · `BroAudio/Editor/Resources/BroEditorSetting.asset`(출력 경로) · `1.Scripts/Sound/AudioManager.cs` · `1.Scripts/Sound/Editor/SoundSync*.cs`(신규) · `Managers/Title·Lobby·MapSceneManager.cs`(BGM 호출) · `DataTable/Editor/DataSourcePlayMode.cs`·`DataTableMenu.cs`(동기화 필요 경고 한 줄) · `Docs/tech/data-table.md`.
- 인수인계 = [handoff-sound-sync.md](Docs/tech/handoff-sound-sync.md). ✅ 10-14 **Play(테이블 모드)·빌드 자동 동기화(결정 C)** — 결과가 `51.Audio/Data` 에 남으니 SVN 커밋. ✅ xlsx `Sound` 시트 새 구성으로 교체(로컬 · Sound 시트만 이식, 다른 시트 바이트 동일) — 🔴 **SVN 커밋 필요**. BGM 호출은 `PlayBGM("BGM_Title"|"BGM_Lobby"|"BGM_InGame")`, `SoundCatalog` 고정 필드 삭제.

## ▶▶ 작업 세션 (2026-10-09 · 은희(Claude) · **거너 레이저건 정렬 + 기본 공격 손 이탈**, 브랜치 `fix/GunnerGunAlign`(development `778bbb87` 기준)) — 계획 [PLAN-gunner-gun-align.md](PLAN-gunner-gun-align.md) ✅ 승인 · ✅ S1~S5 구현(미커밋) · ✅ 실제 Animator 레이어 합성 실측(총구 yaw 0°, 손–손잡이 ≤ 5cm) · 보고 영상 `Recordings/gunner_gun_align_report.mp4`(gitignore) · ⏳ Play·MPPM · ⏸ PLAN §6 판정 시작점
- 원인(Unity 실측): 총은 이미 `rig/artillery` 본에 붙어 있는데 손으로 맞춘 오프셋(09-30)이 **총을 앞뒤로 뒤집어** 총구 yaw ≈174°. 애니는 `LaserGun_Mount` = `artillery` 프레임(오프셋 0)으로 저작됨 — 손 = `Grip_R`/`Grip_L`(Q 는 `Brace_R`) 좌표와 소수점 3자리 일치. + 기본 공격 상체 클립 `attack01` 은 `c_pos` 각도(180°)가 하체 `Q_charge_loop`(146.3°)와 달라, 마스크가 `c_pos` 를 빼므로 팔만 34° 돌아 손이 11~19cm 이탈.
- 🔴 **수정 예정 — 동시 수정 금지**: `Gunner/Gunner_Armature.prefab` · `GunnerAnimatorController.controller` · `Player/Gunner/GunnerBasicAttack.cs`·`GunnerBasicAttackData.cs` · `GunnerBeamView.cs` · `Player/Editor/GunnerShellAuthoring.cs` · `Docs/tech/player-prefabs.md`.

## ▶▶ 작업 세션 (2026-10-08 · 은희(Claude+claude-alt) · **거너 기본 공격 홀드 연사 → 클릭 단발 + 입력 창**, 브랜치 `fix/GunnerDefaultAttack`) — ✅ 구현 `24aebc9d` · 컴파일 OK · development 반영 · ⏳ Play·MPPM 손맛 확인
- 클릭 1회 = 준비 동작 → 1발. 직전 발사 + `comboWindowOpen~Close`(초, `GunnerBasicAttackData`) 안 재클릭 → 서버 예약 → `FireInterval` 후 준비 없이 다음 발. 창은 애니 이벤트가 아니라 데이터 시간 값(판정이 시간 기반·발사 클립 배속 재생). 상세 = [character_gunner.md](Docs/design/character/character_gunner.md) §4.
- 새 수치 2개는 xlsx 미반영 — 병합 Export 전까지 SO 기본값(0.05/0.2).

## ▶▶ 작업 세션 (2026-10-08 · 은희(Claude+Codex) · **사운드 데이터 테이블**, 브랜치 `feature/SoundAssetDataTable`(development `37f97719` 기준, WorkTree2)) — 계획 = [PLAN.md](PLAN.md) 맨 위 ✅ 승인 · ✅ 구현 `8637b802`(Codex) + 예시 키 `UI_Click`(Claude) · EditMode 9/9 · 필드 목록에 `entries[0].*` 11개 확인 · ✅ Play 청취(은희 10-08, BGM·UI_Click) · ✅ xlsx Sound 시트 **SVN r415**(병합 Export, r414 대비 기존 값 변경 0) · ✅ development 반영(10-08)
- 사용법 = [Docs/tech/data-table.md](Docs/tech/data-table.md) Sound 항목. `AudioManager.Instance.Play("키")` / `Play("키", pos)`. 볼륨·피치 = BroAudio 엔티티 값에 곱하는 배율.
- `UIClick`·`UI_Click` = BroAudio 엔티티 `SoundLibrary/LobbyLibrary/UI_Click_01`(UI 타입, 클립 `51.Audio/UI/temp_clickSound.mp3`) — `d8fb6fa5`. 클립 .meta 는 **SVN r412** 같이 받을 것(guid `c02d8e2d…`).
- `AudioManager` 를 `0.BootStrapScene` 에 복구(catalog=SoundCatalog). 8월 씬 정리 때 빠지고 10-06 정리에서 고아 프리팹으로 삭제돼 메인 플로우에 없었다.
- BGM 3종 엔티티 클립 재연결(Title=`Title_Scene_BGM_03 (2)`, Lobby=`Bureau Lobby`, InGame=`BGM_Factory_Stage_01`) + 그 .meta 를 **SVN r413** 에 등록. 🔴 `51.Audio` 의 .meta 대부분이 SVN 미등록이라 PC 마다 guid 가 달라 BroAudio 클립 참조가 끊긴다 — 새 사운드는 .meta 를 반드시 SVN 에 같이 커밋. 다른 PC 는 update 전 같은 이름 로컬 .meta 삭제. `Slash_01` 은 클립 파일이 없어 미연결.
- 새 키 추가 = SO 인스펙터(배열 크기), 수치 = xlsx `Sound` 시트.
## ▶▶ 작업 세션 (2026-10-07 · 은희(Claude+Codex+claude-alt) · **결과 화면 플레이어별 통계**, 브랜치 `feature/ResultStats`) — 계획 [PLAN-result-stats.md](PLAN-result-stats.md) ✅ 승인 · ✅ 1단계 코드(`0c60bb7c`, 컴파일 에러 0 · EditMode 21/21) · ✅ 2단계 ResultScene UI(저작 메뉴 `Tools/UI/Authoring/Result Stats Rows`, 멱등 · 행 프리팹 `2.Prefabs/UI/Result/ResultPlayerRow.prefab`) · ✅ MPPM 3인 ML-Agent QA(10-08, `MainProject-MLAgent` 의 `qa/ResultStats`): 중도 종료·전멸·클리어 모두 3피어 값 동일·화면 행·"나" 일치·서버 독립 집계 일치. 간파(카운터)만 미검증 · 미푸시
- 🔴 **수정 예정 — 동시 수정 금지**: `Managers/SessionStatsTracker.cs`·`SessionResult.cs`·`MapSceneManager.cs`·`UI/ResultStatsView.cs`·`Unit/Unit.cs`(ApplyHealthDamage 이벤트)·`Player/Life/PlayerLifeCycleController.cs`(서버 정적 통지)·`Boss/TwentyThreeBoss.cs`·`GauntletBot.cs`·`SpinnerBot.cs`·`WallBot.cs`(간파 성공 이벤트 1줄씩 — 경석 공유 = [handoff](Docs/tech/handoff-result-stats-monster-hooks.md))·`5.ResultScene.unity`(2단계). `MonsterBase.cs`·`PartyWipeWatcher.cs` 는 결국 안 바뀜(getter 는 `Unit`).
- 신규: `Unit/CombatStatsEvents.cs`·`Managers/SessionStatsAggregator.cs`·`SessionResultPayload.cs`·`UI/ResultPlayerRowView.cs`·테스트 `Managers/Editor/*` + 러너 `Tools/Tests/결과 통계 EditMode 테스트 실행`.
- 팀 규칙 변경(10-07 은희): **PR 없음** — 본인이 `development` 직접 병합, 타 영역 변경은 `Docs/tech/handoff-*.md` 로 공유([workflow.md](Docs/tech/workflow.md)).

## ▶▶ 작업 세션 (2026-10-08 · 은희(Claude) · **`feature/Assassin` → `development` 반영**) — ✅ 완료
- development(FloatingDamageShare 포함) 를 Assassin 에 머지(`9bed0270`, 충돌 = `art-svn.json` 핀만 → r407) 후 development 빨리 감기. SVN **r407** 같이 받을 것.
- 이번에 development 로 들어간 것: 어쌔신 로비 노출 · 옵션 "Hold Skill: Toggle" · 대시 재충전 중 AimIndicator 어둡게 · Dev Boot 캐릭터 드롭다운(`DevBootCharacterCatalog`) · 보스 제한 600초 · xlsx 밸런스(23호 HP 10000·팔라딘 220) · 데이터 테이블 빌드 원복 수정.
- SVN 정리: 은희 PC 의 로컬 guid 재생성 `.meta` 6개 SVN 값으로 되돌림 · `Interrupt.png` guid 를 가로채던 미버전 복사본 `Common_Skill_Interrupt_Icon.png` 제거 · r405 에서 빠진 `Common_DashIcon`/`Common_DashSlotIcon` `.meta` 를 **r410** 커밋.

## ▶▶ 작업 세션 (2026-10-07 · 은희(Claude) · **은희 PC Unity MCP → 공식 MCP 전환**(개인 환경, 팀 파일 무변경), 브랜치 `feature/Assassin`) — ✅ 완료
- 원인(Codex 분석): 경석 포크 `McpServer.OnAfterAssemblyReload()` 가 머신 전역 `EditorPrefs("MCP_Port")` 로 먼저 Start → 리로드마다 다른 워크트리 포트 선점·재시도 10회 소진 정지. 포크 수정은 경석 영역이라 미착수.
- 은희 PC 4 워크트리에 `com.unity.ai.assistant`(2.20.0-pre.2) 임베디드(git exclude, `packages-lock.json` skip-worktree) · 각 `.mcp.json` `unity` = `relay_win.exe --mcp --project-path <워크트리>` · codex-watcher `-c` 로 레인 project-path · claude-alt 프롬프트 갱신. 도구 = `Unity_RunCommand`(C# 실행)·`Unity_GetConsoleLogs`.
- 🔸 Codex/claude-alt: Refresh = `AssetDatabase.Refresh()`, 메뉴 = `EditorApplication.ExecuteMenuItem`, Play 진입 금지(도구 차단 없음). 리로드 중 "Unity not detected" 는 ~10초 뒤 재호출.
- 같이 커밋: `99d99119` preloadedAssets 죽은 guid 제거 · `ba24ac2e` link.xml 부산물 제거.
- ✅ 10-07 스모크 테스트(MainProject 레인, 읽기 전용): Codex(서버명 `unity_mcp`)·claude-alt(`unity`, 지연 로드 → ToolSearch) 둘 다 MainProject 에디터에 정확히 붙음 · Refresh·컴파일 상태·콘솔 OK. ⏳ 도메인 리로드 후 재연결은 미검증(변경 없어 리로드 안 일어남) — 첫 `.cs` 위임 때 확인.

## ▶▶ 작업 세션 (2026-10-07 · 은희(Claude) · **팔라딘 평타 전진 복구 + 공격속도 ×1.2 + 간파 선딜 0.25**, 브랜치 `fix/PaladinDAForward`) — 계획 [PLAN-paladin-da.md](PLAN-paladin-da.md) ✅ 승인 · ✅ C1~C4 구현(진행 = PLAN §7) · SVN **r404** 같이 받을 것 · ⏳ 은희 Play · 미푸시
- 🔴 **수정 예정 — 동시 수정 금지**: `Player/DefaultAttackController.cs`·`DefaultAttackData.cs`·`4.Animations/Player/PlayerAnimatorController.controller`·`Garen_Default_Attack_1~4.anim`·`9.ScriptableObject/Player/Garen/PlayerDefaultAttackData.asset`·`FirstMeleeInterruptSkillData.asset`·`GameData.xlsx`(SVN 잠금).

## ▶▶ 작업 세션 (2026-10-07 · 은희(Claude+Codex) · **데미지 플로팅 멀티 공유**, 브랜치 `feature/FloatingDamageShare`(development `3217a052` 기준, WorkTree2)) — ✅ 구현 `b2bdb73f`(Codex) · EditMode 23/23 · ✅ MPPM 2인 Play 확인(은희 10-07) · ✅ development 반영(10-08)
- `FloatingDamageSettings.asset` 표시 필터 = `AllWithOwnEmphasis`: 전원 숫자 표시, **내 것 = 1.0배·알파 255 / 팀원 것 = 0.75배·알파 200**(SO `teammateScaleMultiplier`·`teammateAlpha`, `[DataTableIgnore]`). 판정 = 순수 `FloatingDamageDisplayPolicy`(일반·허수아비 경로 공용). RPC·권한·누적 키 변경 없음. 상세 = [floating-damage-design.md](Docs/tech/floating-damage-design.md).
- Play 확인 항목(✅ 통과): 호스트·클라 각자 내 숫자 크고 진함 / 팀원 숫자 작고 반투명, 끝에 1.0배로 안 튐 / 공격자별 누적 안 섞임 / 내가 맞은 숫자 계속 미표시.

## ▶▶ 작업 세션 (2026-10-06 · 은희(Claude+Codex) · **3번째 캐릭터 어쌔신**, 브랜치 `feature/Assassin`) — 계획 [PLAN-assassin.md](PLAN-assassin.md) ✅ 승인 · ✅ A0~A10·A12(`0294cb47`, 진행 = PLAN §8) · ⏸ A11 백어택(경석 OK 대기) · ⏳ A13 xlsx · SVN **r403** 같이 받을 것(단검·GameData.xlsx Assassin 시트) · A7~ 은 claude-alt(다른 계정 Claude) 위임 · ⏳ 은희 Play 회귀(A1~A5) + 어쌔신 전 키트
- 기획 사본 = [character_assassin.md](Docs/design/character/character_assassin.md)(맨 아래 「구현 결정」 우선). 아트 = SVN `50.Art/Char/assassin/` v16 · 클립은 git `4.Animations/Player/Assassin/` 로 이전.
- 🔴 코어 변경 포함: 플레이어 **SuperArmor = 몬스터와 같은 의미**(CC·잡기 거부) · 스킬 쿨 장부 스킬 단위 + 슬롯 교체 · 간파 베이스 추출 · 캐릭터 HUD = `<Char>_Armature/HUD`(거너 과열 게이지 이동).
- 🔴 백어택은 **경석 공유 후**(PLAN §6 초안). 🔴 **수정 예정 — 동시 수정 금지**: `Player/Skill/*`(Controller·Base·Data·Targeting·Interrupt)·`Unit/StatusEffectController.cs`·`PlayerStateController.cs`·`UI/Combat/PassiveHUD.cs`·`SkillCooldownHUD.cs`·`Player/Gunner/*` HUD·`Player_Gunner`/`Gunner_Armature` 프리팹·`CharacterRoster.asset`.

## ▶▶ 작업 세션 (2026-10-06~07 · 은희(Claude+Codex+claude-alt) · **스킬 직선 인디케이터 + HUD 호버 미리보기 + 지점 지정 확장**, 브랜치 `feature/GunnerSkillIndicatorUpgrade`) — ✅ 구현·배선 · EditMode 33/33 · ⏳ Play·MPPM 확인 · 미푸시
- 계획·결정 = [PLAN.md](PLAN.md) 최상단(그릴 21문항, 레퍼런스 = 롤 제라스 Q). 대상 = 거너 Q(차지 중 직사각형 6→16) · 거너 RMB/팔라딘 RMB/거너 E(HUD 호버 미리보기). 거너 R·팔라딘 R(사거리 원)은 다음 작업.
- 커밋 `85b6d6df`~`724ac4ba`. 배선 = `Tools/Player/Authoring/Wire Skill Line Indicator`(base `Player.prefab`, 멱등) · 테스트 = `Tools/Tests/스킬 직선 인디케이터 EditMode 테스트 실행`.
- 계획 밖 변경: `PlayerMotor.GetGroundedPreviewDistance`(미리보기 화살표 클립용) · `PlayerMotionSweep.Resolve` 선택 인자 `logDiagnostics`(기본 true, 기존 호출 동일). 셰이더는 임시 HLSL `.shader`(아트 교체 예정).
- 후속(10-07): 호버 방향 = `PlayerMovement.CurrentFacing`(루트는 안 돎) · 대상 지정 스킬 호버 = 사거리 원(팔라딘 R·거너 R).
- 후속(10-07, claude-alt `c53d9fc6` + `2a7830a9`): **지점 지정(GroundPoint) 확장 = 기능만** — `PlayerSkillData` 옵션 `groundPointOutOfRange`(Clamp 기본/AutoApproach) · `groundMarkerRadius`(0=안 그림) · `hoverMarkerDistance`(3). 새 RPC `SubmitPointApproachRpc`. **거너 R 에셋은 기획서대로 SingleTarget 유지**(은희 결정) — 쓰려면 에셋 `targetingMode=GroundPoint`·`AutoApproach`. EditMode 33/33. 팔라딘 Q·E 미리보기는 안 함.
- ⚠️ 이 워크트리에서 `Assets/Refresh` 뒤 도메인 리로드마다 Unity MCP(3003)가 안 돌아와 `Window > MCP Server` 수동 Start 가 필요했다(10-06~07 4회).
- ⚠️ base `Player.prefab` 을 저작 메뉴로 고치면 Unity 가 Variant(`Player_Gunner`·`Player_Paladin`)를 재저장해 잡음 diff(오버라이드 재정렬·base 와 같은 값)를 남긴다 — 커밋 전에 확인하고 되돌릴 것.

## ▶▶ 작업 세션 (2026-10-06 · 은희(Claude) · **스킬 툴팁 인라인 아이콘 + r381 툴팁 행 복구**, 브랜치 `feature/SkillTooltipIcons`) — ✅ 구현 · EditMode 83/83 · Verify 0 · **SVN r384** 같이 받을 것 · ⏳ Play 확인
- 🔴 **SVN r381(밸런스)이 `tooltip.*` 30행 + `# ─ 툴팁` 10행을 날렸다** → r384 에서 Export 병합으로 복구(r381 수치 유지, 서식 초기화). Boss·Monster `attackCooldown` 행도 인스펙터 값으로 새로 생김. 아트 핀 381 → **384**. 경석 남은 일 3(xlsx Monster 속도)은 **r384 위에서** 고칠 것.
- 인라인 아이콘 = `SkillTooltipGenerated/tooltipIcon_*.png` → 아틀라스 `SkillTooltipAttackIcon.asset`(guid 유지) · TMP `SkillTooltipAttackSprite`: `atk`·`shield`·`cooldown`. 재생성 = `Tools/UI/스킬 툴팁 인라인 아이콘 갱신`(⚠️ 인스펙터에서 바꾼 BX·BY·AD 를 덮어씀).
- 표기 = 아이콘이 수치 **앞**, 색은 태그에 직접: `<sprite name="shield" color=#74C7EC>{shieldAmount}`. 🔴 속성이 붙으면 **이름에 따옴표 필수** — 없으면 EmojiOne 노란 `?` 로 깨진다(로그 없음). 칼 #F4B860(코드) · 쿨다운 #B8C4D6(코드, 머리줄 `⏱` 대체).

## ▶▶ 작업 세션 (2026-10-12 · 경석(Claude) · **설정창**, 브랜치 `feature/Settings`) — 계획 [PLAN-settings.md](Docs/history/PLANS/PLAN-settings.md) · 1~3단계 + CRT 가독성 + 그래픽 확장 구현 ✅ · ✅ **10-13 커밋 + `feature/Boss23` 에 병합**(빨리 감기) · 남은 일 = PLAN 「다음 세션」(마우스 감도 보류 등)
- 통합 패널 `Assets/2.Prefabs/UI/SettingsBody.prefab` 을 타이틀·맵 ESC 가 같이 씀. 항목 수정 = `Tools/UI/Settings/설정 패널 항목 배치 (멱등)`(프리팹 원본을 연다).
- 팀장 Play 확인: 드롭다운·오디오·프레임 제한·키 변경. 해상도·창 모드·그래픽 품질·비네트·Alt+Tab 음소거는 빌드에서 확인 예정.
- 은희 `feature/DashDirectionOption`(대시 방향 옵션) **흡수함** — 따로 머지하지 말 것. 기본값은 **캐릭터 정면**(팀장 10-12).
- 10-13(경석 Claude): 설정창 한국어(PLAN S15·S16) + FPS/핑 표시(S17) + 타이틀 설정창 가독성(1.4배·어두운 컨트롤·타이틀 동안 렌더 스케일 100%) + 타이틀 CRT 값 인스펙터 노출 ✅ · 빌드 성공(10:55) · 팀장 빌드 확인 후 Alt+Enter·커서 가두기·AA(FXAA/SMAA) 추가 · 커밋됨. 추가 수정 파일: 신규 `Settings/Localization/*`·`Settings/UI/PerformanceOverlay.cs`·`Settings/Editor/SettingsDarkTheme.cs`·`SettingsLocalizationLinker.cs`, `Settings/UI/*` 컨트롤러, `GameplayConfig.cs`·`GraphicsQualityConfig.cs`, `UI/Title/TitleMonitorDisplay.cs`·`TitleFlowDirector.cs`·`TitleCrtFx.cs`, `SettingsBody.prefab`.
- 🔴 타이틀 CRT 값의 정본이 `MA_TitleMonitorUI.mat` → **`TitleMonitorDisplay` 인스펙터**로 옮겨졌다(머티리얼 값을 고쳐도 화면이 안 바뀐다).
🔴 **수정 예정 — 동시 수정 금지:** 신규 `GameSettings`·설정 패널 프리팹 · `Managers/DisplayResolutionBoot.cs`·`FrameRateCap.cs`·`TitleOptionsPanel.cs`·`MapSceneManager.cs`(옵션 패널 부분) · `Sound/VolumeSlider.cs` · `Player/UserInputConfig.cs`·`PlayerDashController.cs`(방향) · `InputSystem_Actions.inputactions`(Interact E→F) · `Map/ZoneBridgeGateManager.cs`(입력) · `UI/Interact/*`(키 표기) · `UI/Combat/FloatingDamage/*`(표시 필터) · `Camera/Feedback/CameraFeedback.cs`(배율). 씬(`1.TitleScene`·`4.MapScene`)은 튜토리얼 브랜치 머지 뒤.

## ▶▶ 작업 세션 (2026-10-12 · 경석(Claude) · **송전기 노랑 아웃라인 + 간파 넉백 후 취약 유지**, 브랜치 `feature/Boss23`) — 계획 [PLAN-boss-1012.md](Docs/history/PLANS/PLAN-boss-1012.md) ✅ 완료 · 팀장 Play 확인 · 커밋·푸시(동시 수정 잠금 해제)
🔴 **수정 예정 — 동시 수정 금지:** `Monster/Boss/BossChargingPylon.cs` · `Monster/Boss/TwentyThreeBoss.cs`(`FinishVulnerableKnockback`) · `Rendering/Editor/FlatKitSmoothNormalBaker.cs` · 신규 `Rendering/Toon/Shaders/OutlineOnly.shader`·`3.Materials/MA_PylonOutline.mat` · `2.Prefabs/Environment/Layouts/Zones/bossroom.prefab`(송전기 4개 필드).

## ▶▶ 다음 세션 인수인계 (2026-10-08~12 · 경석(Claude)) — 최신. 여기부터 읽을 것

**상태**: `feature/Boss23` = `origin/development`(`0d4accf7`) 병합(`0168192f`, 충돌 0, **미푸시**). SVN **r407**(핀 407, check-environment 전부 통과). 컴파일 `error CS` 0.
- 안 받은 것(경석 판단, 보스 작업에 무관): SVN **r408~r410**(xlsx 팔라딘 공속 · `Common_Dash*Icon.png.meta`) · development 이후 41커밋(`b2acda63`, 결과 통계 등).
  ⚠️ r410 받을 때 `Common_DashIcon/DashSlotIcon.png.meta` 가 로컬 미버전 파일(10-08 Unity 생성, guid 다름)과 트리 충돌 — **서버 쪽으로**(git 참조 0건 확인).
- r407 업데이트 때 `.meta` 4개(어쌔신 폴더·unitypackage·zip) 트리 충돌 → 서버 guid 로 통일(git 참조 0건).

**🟡 최적화 = 중단(경석 10-12). 빌드·측정은 하지 않기로.** 찾은 것만 기록(아래 남은 일 1·2·3·5 대체):
1. **로딩 화면 GPU** — 원인 후보: 맵 씬이 Additive 로 올라오는 동안 로딩 씬 카메라 + 맵 카메라가 둘 다 그림(로딩 UI 는 불투명 Overlay). 미측정.
   **시작 존 GPU** — 원인 미확인(로딩 씬은 완료 후 언로드 확인 → 별건). 존 프리팹 중첩이라 정적 비교로 안 보임 → 아래 2 고친 뒤 패스별 측정으로.
2. **패스별 GPU 기록 0 = 원인 확인(미수정)**: `ZonePerfRecorder.ScanPassesIfNeeded` 가 `new ProfilerRecorder(handle, 1, Default | GpuRecorder)` 로 만들기만 하고 시작 안 함 — `Default` = `SumAllSamplesInFrame | WrapAround`(**`StartImmediately` 없음**, 엔진 XML 문서 확인). 수정 = 옵션에 `StartImmediately` 추가.
3. **붕괴 조각** — 사망당 5~16조각(GO + MeshFilter·MeshRenderer·BoxCollider·Rigidbody), 소멸 때 슬롯마다 `new Material`, Rpc `ClientsAndHost` 라 전 피어가 같은 비용. 미측정.
4. 🔴 **기본 해상도 1080p 가 빌드에서 안 뜬다** — 레지스트리는 1920×1080 으로 정상화됐지만 `DisplayResolutionBoot`(은희 `c44ced45`, 10-07)가 실행마다 **2560×1440 강제**. 1440p 실측 = 일반 존 GPU 2.7~3.3 · **시작 존 6.5 · 로딩 7.8ms**(예산 5.0). 커서 배율도 1440 기준 → **팀장·은희 결정 필요.**
5. GRD·바닥 그림자 효과 — 미측정(바닥 Cast Shadows Off 는 S4 에서 적용 완료). 병목이 GPU 라 GRD 기대 낮음.
- Codex 교차검증: v0.162 에서 샌드박스가 안 떠 실패 → **Codex 데스크톱 앱을 끄면 동작**(10-12 확인, 절차 = 경석 로컬 규칙).
- ⚠️ 에디터 `Unexpected error in Burst compilation … Library\BurstCache\JIT\*.dll, error code 4551` = **Windows Smart App Control 이 서명 없는 Burst JIT DLL 차단**(코드 무결성 로그 3077, 경석 PC 09-18~ 9개). 코드 문제 아님 — 해당 잡만 Burst 대신 일반 C# 으로 돈다(에디터 성능만 영향). 끄려면 Windows 보안에서 Smart App Control 해제(⚠️ 한 번 끄면 OS 재설치 전엔 다시 못 켬).

**10-12 추가**: 보스 수정 3건 완료·푸시(돌진 예고 파랑 `de2df265` · 송전기 노랑 아웃라인 + 벽 아니면 취약 유지 `4fabedb3`, [PLAN-boss-1012.md](Docs/history/PLANS/PLAN-boss-1012.md)). 이어서 `origin/development`(`25723503`, 123커밋) 병합 + SVN **r420** · 핀 415 → **420**(dev 코드가 r416 `F_KeyIcon`·r420 `SlowMotion` 시트를 요구 — dev 핀 415 는 부족했다). r410 대시 아이콘 `.meta` 2개 트리 충돌 → 서버 guid. Codex 머지 전 교차검증 "진행해도 됨". 컴파일 error CS 0 · 전투 EditMode 108/2(기존 실패 2).
- ⚠️ dev 데이터로 23호 **취약 4 → 8초**(HP 2,000 → 10,000 · 공격력 10 → 45). 「벽 아니면 취약 유지」와 합쳐져 취약이 최대 8초 — ✅ 팀장 Play 체감 확인(10-12).
- ⚠️ `TwentyThree_Solo.prefab` `GlobalObjectIdHash` 변경 — 머지 전 빌드와 섞어 멀티 테스트 금지.

## ▶▶ 이전 인수인계 (2026-10-06~08 · 경석(Claude))

**상태**: `feature/Boss23` = **`373ef5a6`**(원격 푸시 완료, 작업 트리 깨끗). 10-06 저녁 `development` 에 빨리 감기로 반영했고, 이후 development 는 다른 팀원 커밋으로 앞서 있음(`0d4accf7` — 다음 세션 시작 때 병합). SVN **r392**(핀 392, check-environment 통과). 전투 EditMode 108/2(기존 실패 2 = `BossCounterDataTests`).
MPPM 네트워크 지연 도구·`[LagDiag]` 진단은 **경석 로컬 전용**(`Assets/1.Scripts/Dev/NetLag/`·`Dev/Editor/DevNetworkLagToolbar.cs`, `.git/info/exclude`) — 메뉴 `Dev/네트워크 지연 (MPPM)`.

**확인한 것(체크 근거)**
- 4K GPU 병목: 개발 빌드 해상도 3종 실측(CSV `../MainProjectBuilds/WindowsDev/ZonePerf/`, 표 = PLAN §S6). 측정기 본문이 개발 빌드 DLL 에 있고 **배포 빌드 DLL 에 없음** 문자열로 확인.
- 타이틀 클릭: 빌드 Player.log `CollisionMeshData couldn't be created … monitor_screen` → r392 후 빌드에서 클릭 정상(팀장).
- 로비 HUD: 빌드 로그 로비 구간 플레이어 생성 0건, 맵에서 선택 캐릭터 정상 생성.
- 몬스터 본: MPPM 혼잡 핫스팟(400±200·5%)·모바일 2.5G(480±40·7%) 3판, 호스트·클라 2명 PeekABot 본 위치 이상 0 / 1,300+ 샘플(수정 전 매판 한 클라 발생, 머리 −7.86).
- 빌드 부산물(PC_RPAsset 프리필터·URP Global rid·preloadedAssets·ScriptableBuildPipeline.json·link.xml)은 빌드마다 생김 → 매번 되돌림, 커밋 안 함.

**🔴 클라에서 몬스터가 땅에 박혀 보이던 문제(핫스팟 3인 테스트 · PeekABot 받침대 고리만 보임·사격 안 보임) — 수정**
- 원인: 아트 팩(Robot Sentries) 몬스터 **본마다 kinematic Rigidbody + 외삽(Interpolate=2)**. 클라(NGO)는 프리팹 기본 위치에 만든 뒤 루트를 옮기고 `AutoSyncTransforms=0` 이라 물리가 옮기기 전 자세를 본에 다시 씀 → 본이 **루트 높이 × 거치는 Rigidbody 수** 만큼 아래로 굳음(실측 머리 −7.86 / 예측 −7.85). 호스트는 제자리 생성이라 안 생김. 타이밍 의존이라 지연 환경에서 잦음.
- 수정: `MonsterBase.DisableBoneRigidbodyInterpolation`(OnNetworkSpawn, 하위 본 Rigidbody 보간 끔, 아트 SVN 무수정). MPPM 혼잡 핫스팟·2.5G 3판 연속 본 이상 0(1,300+ 샘플).
- ⚠️ 철회한 가설: 로딩 씬과 함께 몬스터 삭제(실측: Loading→DDOL→Map 이동) · 접힌 바인드 포즈 · 컬링. "더 오래 기다리기"는 이 증상의 처방이 아니었음(굳은 자세가 25초간 유지).
- 별건(유효): 클라가 `AsyncOperation.progress>=0.9` 에서 100% 보고(`NetworkLoadingFlowController.cs:681`) + 서버가 평균 100% 만으로 완료 → 준비 확인 후 시작으로 바꾸는 계획은 **은희 합의 후** 별도.
- 재현: MPPM(호스트 + 클라)에서 클라에만 Multiplayer Tools `NetworkSimulator` 로 지연(예: 400ms ±200 · 손실 5%)을 걸고 `0.BootStrapScene` 부터 정식 흐름으로(Dev Boot 은 로딩 씬을 건너뜀). 각 인스턴스 로그 = `Library/VP/mppm*/Logs/Editor.log`.
배포용 빌드 = `../MainProjectBuilds/Windows/`(측정기·콘솔 없음 확인) · 측정용 = `../MainProjectBuilds/WindowsDev/`. 실측 표 = [PLAN-cleanup-optimization.md](PLAN-cleanup-optimization.md) §S6.

**이번 세션 결과**
- 🔴 **GPU 병목 = 4K 해상도.** 개발 빌드 일반 존 GPU: 1080p 2.2~2.5 · 1440p 2.7~3.3 · 4K 4.7~5.0ms(Main 0.4~1.2). → 기본 해상도 **1920×1080**(네이티브 끔, `6df20e8c`). 메시 병합·GRD(CPU 제출 절감)는 우선순위 뒤로.
- 타이틀 마우스 클릭(빌드만) = `monitor_screen.fbx` Read/Write 꺼짐 → MeshCollider·`textureCoord` 실패. **SVN r392** 로 켬, 빌드 클릭 확인.
- 로비 전투 HUD = `0.BootStrapScene` 의 `NetworkConfig.PlayerPrefab = Player_Gunner` 오버라이드(민경 `3121b04c`)가 NGO 접속 즉시 자동 생성 → 삭제(`65e3ae92`). 빌드 로비 생성 0건 확인.
- `FloatingDamageAnchor.worldOffset` 추가 — 23호 머리 위 숫자 위치는 프리팹 `TwentyThree`/`_Solo` 에서 조정(값 미조정).

**남은 일**
1. 🔴 **시작 존·로딩 화면 GPU 2~3배**(1080p 4.2/5.1ms, 4K 13/15ms) — 해상도와 별개, 다음 최적화 1순위. 로딩 화면 뒤에서 맵이 드로우 528 로 계속 그려짐.
2. **패스별 GPU 기록이 빈다**(`SystemInfo.supportsGpuRecorder` True 인데 값 0) — `ZonePerfRecorder.ScanPassesIfNeeded` 방식 재검토.
3. 붕괴 사망(`MonsterCollapseDebris` — 죽을 때마다 조각 GO·Rigidbody·BoxCollider 생성, 풀링 없음) 다수 처치 순간 부하 측정.
4. `defaultPlayerPrefab`(선택 없을 때 기본 캐릭터) 아직 **Player_Gunner** — 팀장 결정(팔라딘 복구 수정은 권한 거부로 미적용).
5. 해상도 1080p 가 실제로 뜨는지 확인 — 레지스트리 `HKCU\Software\DefaultCompany\MainProject` 에 이전 4K 저장값 있음(Default 키 비교로 리셋될 것으로 예상, 미확인).
6. 보류(팀장 10-06): Alt+Tab 처리. ⚠️ 현재 `runInBackground: 0` → 호스트가 Alt+Tab 하면 서버 정지, 30초 후 클라 끊김(`DisconnectTimeoutMS 30000`).
7. 전달 — **민경**: 핀 390 인데 거너 코드는 r391 필요했음(development 핀 392 로 해결됨 — 팀원은 SVN r392 받을 것) · `PlayerShieldVfx.OnDestroy` 가 `NetworkBehaviour.OnDestroy` 가림(CS0114) · 부트스트랩 오버라이드 건. **은희/사운드**: `VolumeSlider`·`TitleSceneManager:53` 이 8월에 흐름에서 빠진 `AudioManager` 를 찾아 NRE → 설정 화면 볼륨 슬라이더 미동작. **팀**: `Build/` 메뉴 빌드는 데이터 테이블 훅을 안 거침(인스펙터 값으로 빌드) · VisualSVN 서버 라이선스 만료 경고.
8. `.claude/commands/coop-agent*.md` 가 development 에서 git 추적 해제되어 병합으로 지워짐 — 복원: `git show 99ffc63a~3:.claude/commands/coop-agent.md` 등을 `~/.claude/commands/` 로.
9. **실제 핫스팟 빌드 3인으로 몬스터 본 수정 재확인**(MPPM 으로만 확인함). 다른 몬스터(Mortar·Chomp 등)가 높은 곳에서 땅에 박혀 보이는지도 함께.
10. **로딩 흐름 개선**(클라 0.9 조기 100% 보고·서버가 LoadEventCompleted 안 기다림·진행률 매 프레임 전송) — 은희 합의 → PLAN. 이번 증상과는 별건이지만 실제 결함.
11. 이전 남은 일(아래 블록) 중 2·3·4·5·6 그대로.

## ▶▶ 다음 세션 인수인계 (2026-10-06 · 경석(Claude))

**상태**: 전수조사 정리·최적화 S1~S5 끝 → `development` = `feature/Boss23` = **`661cf289`**(푸시). SVN **r381**(핀 381, check-environment 전부 통과).
상세·실측 = [PLAN-cleanup-optimization.md](PLAN-cleanup-optimization.md) §4 · 정리 노트 = 노션 「개인포트폴리오 > 전수조사 기반 성능 최적화…」(VeyTrace 하위로 옮길 것 — 도구 권한상 직접 못 만듦).

**남은 일 (다음 세션)**
1. **ForProfile 빌드 · L존 측정** — GPU Resident Drawer 켜기 여부(에디터에선 드로우 486→193 인데 프레임 6.85→7.77ms) · 바닥 그림자 Off 효과. 켤 때 `m_BrgStripping` = **2**(1 은 전부 제거).
2. **에디터 사전 메시 병합**(같은 메시 묶어 그리기) — Codex 권장안 PLAN §S4 아래. 큰 작업 → grill·PLAN.
3. **xlsx Monster 시트 속도 반영** — Chomp 2.4/3.36 · Humanoid 2.5/3.5 · Mortar 1.75/2.1. 🔴 **r381 에서 기획(김태현)이 xlsx 를 고쳤다** → 반드시 r381 위에서 해당 칸만 수정, SVN 커밋 전 팀장 확인.
4. **담당별 목록 전달** — 은희(CONTEXT 아래 📮 목록 + 레거시 Paladin 네트워크 등록·TempPlayer_Armature·Garen 컨트롤러·R1·ToonLit 레거시·벽 투명화 레거시) · 민경(INab Demo Assets — 이펙트 프리팹이 데모 메시 30곳 참조) · 아트(SVN `VFX/**/OldVersion`·`SurfaceV1` 원본·빈 아트 폴더 4).
5. **미검증 Play** — MPPM(원격 애니·블렌드 고정) · GauntletBot 예고 · 23호 전기 장판 2회차 VFX · 피격 전이 블렌드 고정.
6. 기존: `BossCounterDataTests` 실패 2(No23 Dash) 팀장 결정 · `TitleSceneManager.cs:53` BGM NRE · `SoulVisualPrefab` 미지정 경고(은희).

## ▶▶ 작업 세션 (2026-10-06 2차 · 경석(Claude) · **S6 존별 실시간 측정 도구**, 브랜치 `feature/Boss23`) — ✅ 완료·커밋(`c445a7bd`) · 빌드 측정 끝(위 인수인계). 실행 = `Dev_Boot.unity` Play(MapScene 직접 Play ✗). 진행·실측 = PLAN §S6 진행
계획 = [PLAN-cleanup-optimization.md](PLAN-cleanup-optimization.md) §S6(승인 10-06). 예산 = 이 PC 200fps(5.0ms) · 결정은 개발 빌드 숫자 · 이번 범위 = 측정 도구까지 · **존 진입/이탈 때 렌더 설정 전환 안 함**(근거 §S6).
🔴 **수정 예정 — 동시 수정 금지:** `Dev/Profiler/ProfilerHUD.cs` · 신규 `Dev/Profiler/ZonePerfRecorder.cs` · `Editor/BuildWindowsPlayer.cs`(측정용 Development 메뉴 추가) · `0.Scenes/MainFlow/4.MapScene.unity`(컴포넌트 부착).

## ▶▶ 작업 세션 (2026-10-06 · 경석(Claude) · **전수조사 정리·최적화 S1~S5**, 브랜치 `feature/Boss23`) — ✅ S1~S5 커밋 · development 반영(10-06)
계획 = [PLAN-cleanup-optimization.md](PLAN-cleanup-optimization.md)(팀장 결정 §4: 이번 라운드 S1+S2, 은희 영역은 목록만).
🔴 **수정 예정 — 동시 수정 금지:** `Monster/MonsterBase.cs` · `Monster/Boss/TwentyThreeBoss.cs`·`GrabController.cs`·`BossChargeClipLoop.cs`·`BossPatternTargets.cs`·`BossElectricFloor.cs` ·
`Rendering/RetroCRT/RetroCRTFeature.cs` · `Rendering/Silhouette/PlayerSilhouetteFeature.cs` · `Rendering/Fog/FogRendererFeature.cs`·`FogManager.cs` · `Map/Minimap/MinimapController.cs`·`MinimapNetworkSync.cs` · `Map/SteamVent.cs` · 프레임 상한(부트스트랩 1곳).

진행표·실측·미검증 = PLAN §4 "진행 (10-06)"·S3·S4. S5 파일 정리 = 커밋 `4a9041d2`·`4f32f26d`(목록만 남긴 담당별 후보는 PLAN §2 S5). SVN r381 확인(Paladin_SkillIcon .meta 트리 충돌 6건 → SVN guid 로 해결).
📮 **은희에게 넘길 목록(팀장 결정: 목록만)** — 근거·위치는 PLAN §2:
`Player.cs:386`·`PlayerCorpseController.cs:248` `RaycastAll`→`RaycastNonAlloc` · `ToonLit.shader:137` 안 쓰는 SSAO·혼합그림자·ShadowMask multi_compile(변형 8배) ·
HUD 매 프레임 문자열(`PlayerHealthHUD`·`StatusEffectHUD`·`SkillCooldownHUD`·`DashCooldownHUD`·`PassiveHUD`) · 참조 0 스크립트(`AttackElement`·`OverlapAttack`·`IKnockbackSettable`·`PlayerColorAssigner`) ·
네트워크: `_animSpeed` 양자화 · 몬스터 투사체 NGO 풀 · NetworkTransform 38개 회전 X/Z·스케일·HalfFloat · 보호막 `NetworkList` 전체 재직렬화(`Unit.cs:452`) · 먼 몬스터 관찰자 관리 · 플레이어 정적 레지스트리(미니맵·PartyWipeWatcher 가 쓰게).

**10-06 앞선 작업 — 걷는 몬스터 이동 애니(미커밋, 팀장 Play 확인)**
- `MonsterDataSO.locomotionPlaybackScale`(기본 1, `[DataTableIgnore]`) — 발맞춤 재생 배율에 곱함, Play 중 인스펙터 실시간 반영. Chomp 0.85 · Humanoid 0.9 · Mortar 0.43.
- 속도: Chomp 2.4/3.36(1.2배) · Humanoid 2.5/3.5(기본 몹, 걷기 클립뿐이라 절충) · Mortar 1.75/2.1(0.7배). 🔴 **xlsx Monster 시트는 아직 옛 값**(2/2.8 · 3/4.5 · 2.5/3) — 테이블 모드·빌드는 옛 속도. SVN 갱신 필요.
- `MonsterBase`: 이동 블렌드 = 감쇠 0.12 + **이동→액션 전이 동안 고정**(공통) + **원거리 이동형 공격 종료 시 걷기 값 선세팅**(Mortar "앉는 프레임"). 원인 = 공격 종료 `ResetToLocomotion` 코드 CrossFade 가 속도 0 Movement 로 감. Host 실측 대기우세 0. ⏳ MPPM · Gauntlet 예고 · 피격 전이 Play.
- `HasParameter` 매 프레임 `animator.parameters` 할당 → 컨트롤러별 해시 캐시.
- Mortar 가 최소 거리 안에서 쏘는 것 = 팀장 OK(그대로).

## ▶▶ 다음 세션 인수인계 (2026-10-05 · 경석(Claude)) — 여기부터 읽을 것

**이 세션에서 끝낸 것**: 몬스터 공격속도·쿨다운·이동 애니(development 반영 `4040189c`, SVN r372) · 타이틀 시작 연출 + 존 게이트 모니터 패널·외곽선(`feature/Boss23` `5e3e46fe`, development **미반영**) ·
존 프리팹 NetworkObject 정리(아래 1번, **Unity 검증 대기**).

**남은 일 (우선순위 순)**
1. 🔴 **존 NetworkObject 정리 검증** — Unity 를 꺼 둔 채 YAML 로 했다: 존 7종(ZoneL_typeA/B/C · ZoneM_typeA/B · ZoneS_typeA · Zone_typeQuest01) 루트 NetworkObject 제거 ·
   `DefaultNetworkPrefabs.asset` 항목 31 → 24 · 아트 씬 `all_mesh.unity` 의 고아 `GlobalObjectIdHash` 오버라이드 5건 제거 · 재감염 방지 테스트 `ZonePrefabNetworkRulesTests`(전투 EditMode 러너 등록).
   → Unity 열고 **컴파일 · `Tools/Tests/전투 EditMode 테스트 실행`(신규 2건 통과 확인) · 튜토리얼 Play(존 생성·다리 게이트 F) · MPPM** 후 커밋. 원본 백업 = 이 PC 스크래치(세션 종료 시 사라짐) — 문제 시 `git checkout` 으로 되돌릴 것.
   근거: 존은 `MapContentSpawner` 가 로컬 Instantiate(네트워크 Spawn 은 몬스터뿐), 7종에 NetworkBehaviour 없음 확인.
2. 🔴 **데이터 테이블 Play·빌드 차단(기존 문제)** — SVN r366(은희)이 xlsx 에 `tooltip.*` 행을 넣었는데 코드(`feature/SkillTooltip`)가 git·SVN·GitHub 어디에도 없다(10-04 전수 확인).
   테이블 모드 Play 가 오류 30개로 꺼진다(`DataSourcePlayMode.cs:103`). **은희에게 브랜치 푸시 요청**(팀장). 그동안 경석 PC 데이터 출처 = 인스펙터.
   대안(공유 후에만): xlsx 툴팁 행 필드명 앞에 `#` — 값 보존, 되돌리기 쉬움.
3. **development 반영** — `feature/Boss23` 의 타이틀·모니터 작업(`5e3e46fe` 이후). 팀장 결정 시.
4. **bossroom NetworkObject 4개 용도 확인** — 루트가 아니라 중첩 프리팹 오브젝트에 붙어 있다(GlobalObjectIdHash 4130406446 · 3863298080 · 3199288331 · 3620266727). 정상 네트워크 오브젝트인지 감염인지 모름 → 확인 후 가드 테스트 `Excluded` 에서 빼기.
5. ✅(10-06 완료 — 위 작업 세션) ~~**안 쓰는 스크립트·파일·폴더 정리(팀장 10-05 요청)**~~ — 큰 작업: grill → PLAN → 승인. 원칙 = 참조 0 을 guid grep 으로 증명 · git/SVN 소유 구분(아트·.meta 는 SVN) · **Unity 끄고**(디렉터리 이동·대량 삭제) · 지운 뒤 컴파일·테스트·Play.
   알고 있는 후보(전부 확인 필요):
   - `Assets/1.Scripts/Map/ZoneInteractRing.cs` — 10-04 부터 미사용. 단 Visual Scripting 생성 코드 `Assets/Unity.VisualScripting.Generated/.../AotStubs.cs` 가 참조 → **Node Library 재생성 후** 삭제.
   - `TitlePowerOff` 의 시작 경로는 이제 폴백뿐(EXIT 는 사용 중 — 클래스는 유지).
   - `Assets/2.Prefabs/Player/Legacy/`(구 Paladin·TempPlayer_Armature — 스폰 안 됨, player-prefabs.md) · `Assets/3.Materials/Environment/NotUsedInMap/` · 레거시 `MonsterTimeController`(Enemy 전용).
   - 기존 감사 문서 `Docs/04-report/deadcode-audit-2026-09-09.md` 를 출발점으로 쓸 것.
   - 이 세션의 1회성·진단 도구는 남겨 둠: `Tools/Monster/공격 클립 길이 보고`·`이동 클립 고유 속도 측정`, `Tools/Map/Authoring/Zone Monitor Screen/*` — 계속 쓸지 정리 때 판단.
6. **미검증 Play** — 몬스터 `attackSpeed` 0.5/2 · MPPM(클라 공격 애니 속도·Mortar 발사·WallBot 평타 2단) · 모니터 패널 MPPM(다른 피어가 켠 패널).
7. ~~기존 EditMode 실패 2(`BossCounterDataTests`)~~ — 10-13 해결: 데이터가 맞고(G3) 테스트·계약 검사가 낡았다.
8. `Assets/1.Scripts/Player/Fall/FallBoundarySettings.cs` 줄 끝(EOL)만 다른 변경이 계속 뜬다 — 내 것 아님, 커밋하지 않음.

## ▶▶ 작업 세션 (2026-10-03 · 경석(Claude) · **타이틀 시작 연출 · 존 게이트 패널 모니터 켜짐·외곽선**, 브랜치 `feature/Boss23`) — ✅ 팀장 Play OK(10-04) · `feature/Boss23` 푸시 `5e3e46fe`(development 미반영). 게이트 활성 표시 = 모니터 화면(바닥 링 삭제). ⚠️ ZoneL_typeB 를 프리팹 모드로 열어 둔 채 파일을 바꾸면 Auto Save 가 덮어쓴다 — 값은 `Tools/Map/Authoring/Zone Monitor Screen/5` 로
계획 = [PLAN-title-monitor.md](Docs/history/PLANS/PLAN-title-monitor.md)(승인 10-03, Codex 설계 회의 반영). 🔴 **수정 예정 — 동시 수정 금지:** `UI/Title/*`(TitleFlowDirector·TitleMonitorDisplay·셰이더) · `Map/ZoneBridgeGate.cs`·`ZoneBridgeGateManager.cs` · `ZoneL_typeB.prefab`(게이트 값) · 신규 외곽선 셰이더.

## ▶▶ 작업 세션 (2026-10-02 · 은희(Claude+Codex) · **캐릭터 선택 화면**, 브랜치 `feature/SelectCharactorUI`) — 계획 [PLAN-character-select.md](PLAN-character-select.md) 승인됨
- ✅ 1단계 Codex(코드) `09e9cc57` · ✅ 2단계 Claude(씬·에셋·`LobbySceneManager` 배선). 결과 요약 = [player-prefabs.md §8.3](Docs/tech/player-prefabs.md)
- 🔴 남은 것: **은희 MPPM Play 확인**(호스트+클라2: 초상화·Ready 동기화 / Start 활성 / 고른 Variant 스폰) → PR. 레이아웃은 첫 배치라 Play 보고 조정 가능
- 범위 밖: 전투 HUD 초상화 Paladin 고정(Gunner 도 Paladin 얼굴)
## ▶▶ 작업 세션 (2026-10-16 · 민경(Claude) · **어쌔신 일반 Q = 관통 일섬(PierceLine + PierceBurst)**, 브랜치 `feature/VFX`) — 계획 [PLAN.md](PLAN.md) 맨 위 · ✅ MyProject 컨펌 · ✅ 반입·배선(컴파일 에러 0) · ⏳ Play·MPPM · 🔴 SVN add · 🔴 은희 공유(handoff §3-3)
- MyProject `Export/Player3/BM Thrust Slash_PierceLine`(+Z 1m 메시 파티클 3겹 + 잔흔·속도선)·`_PierceBurst`(Thrust Slash 재채색) → `Players/Player3/Q_Dash/FX_Assassin_PierceLine`·`_PierceBurst`, 카탈로그 `Assassin_Q_PierceLine`·`Assassin_Q_PierceBurst`. 의존 11개 추가(머티리얼 3·FBX 1·텍스처 7 — `Gradient_Quad02.png` 는 이름 충돌로 `Textures/PilotoStudio/Shared/`).
- `EffectManager` 축별 배율 원샷 오버로드 + `EffectPool.Rent(Vector3)`. 일섬은 OnEnd 에서 시작점→실제 끝점, 임시 돌진 궤적 삭제.

## ▶▶ 작업 세션 (2026-10-15 · 민경(Claude) · **어쌔신 변신 Q = Storm Slash 루프(플레이어 루트·Rate over Distance)**, 브랜치 `feature/VFX`) — 구현(컴파일 에러 0) · ⏳ Play·MPPM · 🔴 SVN add · 🔴 은희 공유(handoff-assassin-r-vfx §3-2)
- MyProject `Export/Player3/BM Strom Slash.prefab`(운반체 Velocity 제거 → FuzzAlpha·데칼이 직접 Rate over Distance, World, Looping, Emitter Velocity = Transform) → `Players/Player3/Q_TransformedDash/FX_Assassin_StormSlash(.prefab|_Entry.asset)`, 카탈로그 `Assassin_Q_StormSlash`(outroDuration 1.1). 의존 3개 추가(Ring 머티리얼·Trail_Cone_Cut.fbx·텍스처 1 — Ring 렌더러는 꺼져 있음).
- `AssassinDashStrikeSkill` 변신 데이터일 때만 OnClientPlay → `BeginStormSlash`, OnEnd → `EndStormSlash`. 임시 돌진 궤적은 일반 Q 만(변신 Q 에서 제거).

## ▶▶ 작업 세션 (2026-10-15 · 민경(Claude) · **어쌔신 변신 E = Multi Slashes 루프(원 중심·배율 = 원 반경)**, 브랜치 `feature/VFX`) — 구현(컴파일 에러 0) · ⏳ Play·MPPM · 🔴 SVN add · 🔴 은희 공유(handoff-assassin-r-vfx §3-1)
- MyProject `Export/Player3/BM Multi Slashes.prefab` 원본 그대로 반입 → `50.Art/VFX/Common/Players/Player3/E_CircleStrike/FX_Assassin_MultiSlashes(.prefab|_Entry.asset)`, 카탈로그 `Assassin_E_MultiSlashes`. 의존 22개 추가(PBR FX.shadergraph·머티리얼 3·텍스처 17·FBX 1, `PilotoStudio/` 하위).
- 끊김 대책 = 엔트리 `outroDuration` 1.1(Release 는 방출만 멈춤 → 남은 입자 수명 최대 1.05초). 바닥 데칼 알파 곡선은 손대지 않음(민경 결정). 임시 `Burst_rings` 1회 유지.
- `AssassinCircleStrikeSkill` OnClientPlay → `BeginCircleStrike`, OnEnd → `EndCircleStrike`. 배율 = `AreaRadius × circleStrikeScaleFactor(1)`.

## ▶▶ 작업 세션 (2026-10-14 · 민경(Claude) · **어쌔신 R 변신 VFX = Blade Vortex(0.5초 생성·0.5초 사라짐)**, 브랜치 `feature/VFX`) — 계획 [PLAN.md](PLAN.md) 맨 위 · ✅ 1단계 MyProject 컨펌(민경) · ✅ 2단계 반입·배선(컴파일 에러 0) · ⏳ Play·MPPM · 🔴 SVN add · 🔴 은희 공유 [handoff-assassin-r-vfx.md](Docs/tech/handoff-assassin-r-vfx.md)
- 원본: MyProject `Export/Player3/BM Blade Vortex.prefab` → 사본 `_Fast`(t=0 버스트 + 알파·크기 키 앞당김, 민경이 위치·크기 조정) · `_End`(원샷, 바깥으로 흩어짐). 원본은 그대로.
- MainProject: `50.Art/VFX/Common/Players/Player3/R_Transform/FX_Assassin_BladeVortex(.prefab|_End.prefab|_Entry.asset)` · 카탈로그 `Assassin_R_BladeVortex`. Piloto 의존 23개(UberFXSG+서브그래프 4·머티리얼 7·텍스처 9·FBX 2)는 `50.Art/VFX/*/PilotoStudio/`, guid 유지.
- `EffectEntry.outroPlayRate`(기본 1) 신설 — Release 시 남은 루프 입자를 배속(Main ▸ Simulation Speed). 이 엔트리 8 · outroDuration 0.6.
- `AssassinSkillView` 변신 루프 = `PlayLooping`/`Release`(임시 `CharacterCirclePurple`·`Poof_electric` 제거). EffectStagePlayer 는 단계 교체가 즉시 회수라 파티클에서 끊겨 안 씀(grill Q13).
- 🔴 SVN add: 위 R_Transform 폴더 + PilotoStudio 폴더 4곳(폴더 .meta 포함).

## ▶▶ 작업 세션 (2026-10-12 · 민경(Claude) · **카운터 성공 섬광 = 몸 표면 CameraDir 재생**, 브랜치 `feature/VFX`) — ✅ Play 확인(민경 10-12, 4종 모두) · ⏳ **MPPM 2인 남음** · 🔴 경석 공유
- `EffectSocketPlayer.PlayOnceOnSurface(Collider)` 추가 — `EffectHitPoint.Resolve(CameraDir)` 로 위치·회전, 배율은 소켓 플레이어 `scale`. 표면 못 쓰면 `PlayOnce`(소켓) 폴백.
- `MonsterBase.EffectSurfaceCollider` = `hitVFXCollider ?? bodyCollider` (23호는 hitVFXCollider 비어 있음 → 몸 캡슐).
- Gauntlet·Spinner·Wall·23호 `PlayInterruptFlashRpc` → `interruptFlash.PlayOnceOnSurface(EffectSurfaceCollider)`. 전역 `EffectManager.HitPointMode` 와 무관하게 항상 CameraDir.
- 프리팹: `TwentyThree` 는 소켓을 비우고 배율 1.2 → 1.6(표면이 위치를 정하므로), `TwentyThree_Solo` 는 비어 있던 `interruptFlash` 를 새로 배선. 봇 3종은 소켓을 폴백으로 남겨 뒀다.
- 🔴 **Play 에서 건틀릿·월봇만 섬광이 낮게 떴다** — Hurtbox 캡슐이 높이 2 로 몸보다 짧아 표면 점이 아래에 찍혔다. 몸 높이에 맞춰 키움(`107f8912`). **피격 판정도 같이 커지므로 경석 확인 요망.** 스피너·23호는 원래 맞았다.
- ⏳ 남은 것: **MPPM 2인 확인**. CameraDir 는 각 피어가 자기 카메라로 계산하므로 1인 Play 로는 검증되지 않는다. `Camera.main` 이 null 이면 **에러 없이** 소켓 폴백으로 떨어진다.

## ▶▶ 작업 세션 (2026-10-08 · 민경(Claude) · **웰즈 드론 폭발 = FX_Drone_Explosion_Entry 일회 재생**, 브랜치 `feature/VFX`) — 구현 완료 · ⏳ Play 확인 · 🔴 경석 공유
- `WellsDroneDataSO`: `explosionVfxPrefab`·`explosionColor`·`explosionGrowScale` 삭제(엔트리 칸 없음 — 카탈로그가 단일 출처). 예고 원 색(`circleOuterColor`·`circleFillColor`)은 유지(결정).
- `WellsDroneAttack.ClientImpact`: 임시 폭발 원·Instantiate/Destroy 제거 → `EffectManager.Play(entry, 바닥, identity, scale = 폭발 반경 m)`. 프리팹이 반경 1m 저작 전제. 핸들 없음 → 취소돼도 터진 폭발은 끝까지 재생. `explosionDuration` 은 서버 쿨다운 대기만(연출 길이 무관).
- 엔트리 = `EffectCatalog.Wells_Drone_Explosion`(헤더 '보스 — 웰즈 자폭 드론', `EffectCatalog.asset` 배선). 비면 경고 후 연출 생략.

## ▶▶ 작업 세션 (2026-10-07 · 민경(Claude) · **거너 VFX 폴더 스킬별 정리 + 미사용 연출 3종(UltTick·FocusRing·ShieldGrant) 제거**, 브랜치 `feature/VFX`) — 구현 완료 · ⏳ 민경 `Tools/VFX/거너 이펙트 폴더 정리` 실행
- `Player2/` → `LeftClick_BasicAttack`·`RightClick_Interrupt`·`Q_ChargeLaser`·`R_TrackingLaser`·`E_FrostBackstep`. 이동은 도구(`GunnerVfxFolderSort.cs`, AssetDatabase.MoveAsset)로 — guid 유지. `README.txt`·`FX_OverHitting0`(미참조)은 루트에 남김.
- 셋업 도구 경로 갱신: `GunnerBeamSetup`·`GunnerBackstepSetup`·`GunnerUltSetup`(TickFx 생성 → 남은 TickFx 제거로 바뀜).
- 🔴 은희 공유: `GunnerTrackingLaser.cs` 에서 `tickPlayer`·`tickVfxInterval`·`UpdateTickVfx` 삭제(피해 로직 무관). `GunnerTrackingLaser.prefab` 의 `TickFx` 오브젝트 제거. `Player_Gunner.prefab` 에 남아 있던 `QFocusRing` 소켓(FocusRing 엔트리 참조) 제거. `GunnerBeamView.cs` 주석 1줄.
- 🔴 SVN: 이동분은 delete+add 로 커밋(또는 TortoiseSVN Repair move). UltTick·FocusRing·ShieldGrant 프리팹+엔트리 6개는 도구가 OS 휴지통으로 보냄 → SVN delete.
- ✅ 폴더 정리 실행·검증(guid 참조 누락 0). 이후 쓸모없는 도구 삭제: `GunnerVfxFolderSort.cs`·`CrackOverlaySetup.cs`(+.meta, SVN delete 필요), `EffectSystemSetup.cs` 의 `v1 기본 에셋 생성`·`스모크 테스트` 메뉴 제거(git, 은희 공유). 거너 배선 도구 8개는 플레이어 프리팹 재구성 대비로 유지.

## ▶▶ 작업 세션 (2026-10-06 · 민경(Claude) · **23호 사망 디졸브 = FlatKit 디졸브 셰이더**, 브랜치 `feature/VFX`) — ✅ Play 확인(민경 10-06, 실루엣 자국 없음)
- 23호(`TwentyThree.prefab`)는 부위 붕괴 대상이 아니라(팀장 지시 — 쓰러진 자세 2초) `DissolveDeath` 를 유지하되, 녹을 때 셰이더만 잡몹과 같은 **`VFX/Stylized Surface Dissolve`** 로 바꿨다. 기존 템플릿(DissolveFx, URP Lit 계열)은 갈아끼우는 순간 FlatKit 셀 음영·림·아웃라인이 빠져 질감이 바뀌었다.
- `DissolveDeath.cs`: `stylizedDissolveShader` 필드 추가. 원본이 `FlatKit/Stylized Surface` 면 `new Material(원본)` + 셰이더 교체(룩 유지), 아니면 템플릿 경로. 노이즈는 템플릿의 `_NoiseTexture` 를 빌림. 진행 방향이 반대(`_DissolveAmount` 0→1 / `_Cutoff` 1→0)라 따로 보간. FlatKit 디졸브 슬롯이 있는 렌더러는 그림자 끔(ShadowCaster 가 깎기를 안 탐). 셰이더 필드가 비면 예전 동작 그대로.
- `DissolveDeath` 사용처는 23호 하나뿐(본체 `FK_No23_Toon` + 탑승 웰즈 `FK_SK_welz_*` 전부 FlatKit). 인터럽트 오버레이 슬롯 건너뛰기는 그대로.
- 🔴 **경석 공유**: `DissolveDeath.cs`, `TwentyThree.prefab`(필드 3줄 추가).
- 🔴 **디졸브 후 실루엣 음영이 남던 문제**: `StylizedSurfaceDissolve.shader` 의 `DepthOnly`·`DepthNormals` 가 URP 기본 패스라 디졸브를 안 깎았다 → 색은 사라져도 깊이 버퍼에 원래 모양이 남고, 깊이를 읽는 `FogRendererFeature`(깊이 안개)·`DecalRendererFeature` 가 그 자리를 다르게 칠했다. URP 패스 사본 2개(`StylizedDissolveDepthOnlyPass.hlsl`·`StylizedDissolveDepthNormalsPass.hlsl`)에 `FlatKitDissolveClip` 을 넣어 교체. 잡몹 부위 붕괴 조각도 같은 셰이더라 같이 고쳐진다. 🔴 SVN add 2개 + `.meta`.

## ▶▶ 작업 세션 (2026-10-06 · 민경(Claude) · **아군 보호막 연출 = 받는 쪽이 주인**, 브랜치 `feature/VFX`) — 구현 완료 · 배선·Play 검증 대기
계획·그릴 결과 = [PLAN.md](PLAN.md) 맨 위 항목.
- 거너 Q 가 아군에게 보호막을 줄 때 띄우던 `FX_GunnerShieldGrant` 를 걷고, **아군 자신의 `PlayerShieldVfx` 배리어**로 바꿨다.
- 🔴 **지속시간을 넘기지 않는다.** 보호막은 시간보다 먼저 깨지고(`Depleted`), `GunnerCharge` 는 `Stack` 이라 인스턴스가 여럿이며, 시전자(Q 스킬)가 보호막(5초)보다 먼저 끝난다. 셋 다 타이머로는 못 센다.
- 🔴 **보호막은 이미 전 피어에 복제돼 있다** — `Unit._replicatedShields` + public `ShieldInstanceCount`/`GetShieldInstance`/`ServerShieldEnded`. `PlayerShieldVfx` 가 그걸 직접 읽어 켜고 끈다. **`Unit.cs`·`Player.cs`·`Health.cs` 무수정.**
- 색은 출처로 가른다: `HolyShield` = 금색(기존) / 그 외 = **흰색**(신규 `50.Art/VFX/Common/Players/Shield/`). 겹치면 **금색 우선**, 배리어는 언제나 하나.
- `PlayLocal()`·`ServerEnd()` 는 **오프라인 폴백**으로 바뀌었다(`IsNetworkActive` 면 no-op) — 덕분에 `FirstMeleeSubSkill` 무수정.
- 🔴 **은희 공유 2건**: ① `ChargeLaserFiredRpc` 시그니처에서 `Vector3[] shieldPoints` 제거(네트워크 계약 변경) ② `Player_Gunner.prefab` 에 `PlayerShieldVfx` 추가(NetworkBehaviour 목록 변경).
- 🔴 **같이 고친 기존 버그 2건**: 팔라딘 `HolyShield` 소켓 `safetyTimeout` 8 < 보호막 10초(8초에 강제 회수) → 15 / `PlayChargeLaser` 의 조기 `return` 이 Q 발사 애니를 건너뛸 수 있었다.
- **배선**: `Tools/VFX/보호막 연출 배선` (두 프리팹 + EffectCatalog 프리워밍 등록까지 한 번에).
- 🔴 **SVN add**: `Shield/` 폴더의 프리팹 3 + 엔트리 2, `_Materials/FX_AllyShield_*.mat` 4장, `PlayerShieldSetup.cs`, 각 `.meta`.
- 🔴 **대쉬 잔상이 MPPM 에서 안 보이던 버그 수정**: 이동 권한이 2026-09-29 이후 **오너**로 뒤집혔는데(`Player.ServerAuthoritativeMovement == false`) `PlayerSkillVfx` 의 잔상 폴링이 여전히 "서버 권한" 전제로 `SendTo.ClientsAndHost`(서버 전용) RPC 를 쐈다 — **클라가 대쉬하면 아무에게도 안 간다**(호스트가 대쉬할 때만 보임). 최후의 심판과 같은 **오너 → 서버 → NotOwner 2홉**으로 교체. 🔴 **은희 공유**(`PlayerSkillVfx.cs`, RPC 목록 변경).
- 🔴 **대쉬 잔상 2차(2026-10-06)**: `DashAfterimage` 의 잔상 루트를 Awake 시점 액티브 씬에 만들어서, 로비 흐름(MPPM)에서 그 씬(로딩·로비)이 언로드되면 루트째 파괴되고 다시 안 만들어졌다 → `EnsureGhostRoot()` + `DontDestroyOnLoad`, 파괴된 조각 정리. ✅ MPPM 확인 완료(민경 10-06), 진단 로그 삭제.
- 🔴 **대쉬 잔상 실종의 확정 원인(Editor.log 스택으로 확인)**: `DashSpeedLines` 가 궤적을 Awake 시점 액티브 씬에 만들어 로비 흐름에서 씬 언로드와 함께 파괴됨 → `DashAfterimage.Play()` 가 **속도선부터** 켜다 `MissingReferenceException` 으로 빠져나가 잔상 0장 → 예외가 `PlayerSkillVfx.Update` 까지 올라가 RPC 전송도 건너뜀. 수정: 궤적 루트 `DontDestroyOnLoad` + 파괴 시 재생성, `Play()` 순서를 잔상 먼저·속도선 나중으로. 속도선 없는 E 백스텝 잔상은 처음부터 멀쩡했다.
- MPPM 은 Dev Boot 가 아니라 **로비 흐름**이고, `0.BootStrapScene.unity` 에 커밋 안 된 로컬 오버라이드로 `defaultPlayerPrefab`·`NetworkConfig.PlayerPrefab` = **`Paladin_VFX`(구 통짜)** 가 들어가 있다.
- **`FX_GunnerUltLaser` (B) 정규화**: `Shoot`·`UltLaser` 만 `scalingMode: Local` 이라 부모 1/9 와 **런타임 반경 배율(×2r)을 무시**하고 있었다 → Hierarchy 로 바꾸고 크기 재계산(UltLaser X/Z 9 · Shoot 5.5 · Y 6.255 유지). 런타임(r=2) 지름 4m / 2.44m, 길이 5.56m. 🔴 `UltLaser` 는 나머지 8개 시스템의 **부모**라 트랜스폼 스케일(0.1111)은 건드리면 안 된다. 메시는 내장 Cylinder(중심 피벗) — 길이의 절반이 바닥 아래.
- ⚠️ **가붕이는 대쉬 잔상이 아예 없다**: `DashAfterimage` 가 `Paladin_VFX.prefab`(스폰 안 되는 보관용)에만 있고 `Player_Paladin.prefab` 에는 없다. 별건 — 배선 도구 필요.
- **Q 집중 발밑 링(`FX_GunnerFocusRing`) 제거**(2026-10-06): 감속(×0.5)·조준고정은 조작감으로 읽히고, 그 연출의 화살표는 위로 솟아 "모인다"(충전) 기호라 묶여 있다는 신호와 반대였다. 에셋은 남기고 배선만 뺐다 — `QFocusRing` 소켓은 `Tools/VFX/거너 Q 스킬 배선` 이 지운다.
- 🔴 **같이 고친 기존 버그 3번째**: `GunnerBeamView.EndCharge` 의 `if (chargeLoopPlayer == null) return;` 이 **과충전 루프 정지보다 앞**에 있었다 — 충전 소켓이 비면 과충전이 영영 안 꺼진다.

## ▶▶ 작업 세션 (2026-10-06 · 민경(Claude) · **거너 VFX 미싱 머티리얼 복구**, 브랜치 `feature/VFX`) — 🔴 SVN add 필요
- 증상 = Player2 이펙트 5종(`CoolVent`·`FocusRing`·`FrostBlast`·`FrostTrail`·`UltLaser`)의 파티클 머티리얼이 Missing.
  원인 = 샌드박스(MyProject) 의 **Hovl Studio 팩이 MainProject 에 없고**, 포팅 때 이 5개가 빠졌다.
- 해결 = 샌드박스에서 **`.meta` 를 통째로 복사해 guid 를 보존**(이 폴더의 기존 관례와 동일) →
  `50.Art/VFX/_Materials/` 에 `Snowflake_Hovl`·`Arrow1`·`Flare`·`Mask1`·`TechCircle2`, `Textures/` 에 같은 이름 png 5장.
  🔴 `Snowflake` 는 **이름 충돌**(기존 것은 Little Enchant 팩, 참조 0) 이라 `_Hovl` 접미사를 붙였다.
- 🔴 같이 빠져 있던 것 = `Models/Cylinder.fbx`(Hovl). `FX_GunnerUltLaser` 의 Renderer **메시**가 이걸 가리킨다 — 없으면 레이저가 안 보인다.
- 🔴 **SVN add**: 위 11개 파일 + 각 `.meta` (`Assets/50.Art` 는 git 무시).
- `FX_GunnerOvercharge` 강화(Q 최대 충전 0.8초 창 연출이 1초에 1발·0.4유닛이라 안 보였다) → 주기 1.0→0.3s · 버스트 1→3 · 상시 10/s · 크기 ×1.5 · 스케일 0.1→0.2 · 색 HDR 청백. 샌드박스 사본도 같은 값.

## ▶▶ 작업 세션 (2026-10-06 · 민경(Claude) · **잡몹 사망 = 디졸브 → 부위별 붕괴**, 브랜치 `feature/VFX`) — 구현 완료 · Bake·Play 검증 대기
계획·조사 = [PLAN.md](PLAN.md) 맨 위 항목.
- **부위 조각은 블렌더 없이 본으로 가른다.** 이 팩 로봇은 강체 리깅(ChompBot 1661/1661 단일 본)이고 본 경계를 가로지르는 폴리곤이 **0개** — 메시가 이미 닫힌 껍데기들의 합집합이라 가르면 단면이 안 생긴다.
- 조각 정점은 `bindposes[본]` 을 먹여 **본 로컬 공간**에 담는다 → 런타임은 조각을 그 본의 월드 트랜스폼에 놓기만 하면 **사망 포즈가 근사 없이** 재현된다. 미리 잘라 둔 스태틱 파츠(바인드 포즈)로는 안 되는 지점.
- 새 파일 = `1.Scripts/Monster/Collapse/` 의 `MonsterPartSet` · `MonsterCollapseDebris` · `CollapseDeath` (+ Editor 2). 도구 = `Tools/Monster/부위 붕괴 — ChompBot 설정` / `— 선택한 프리팹에 적용`.
- 🔴 **경석 공유**: `ChompBot.prefab` 의 `DissolveDeath` → `CollapseDeath` 교체. `MonsterBase` 는 안 건드렸다(`IDeathEffect` 구멍 그대로). NetworkBehaviour 개수는 같지만 **목록 순서가 바뀐다**.
- `DissolveDeath` 는 **남긴다** — 보스가 계속 쓰고, 붕괴 쪽도 조각 **퇴장**에 같은 템플릿을 재사용한다.
- 파편 규칙은 상자(`FragmentBurstEffect`)와 동일: Effect(17) 레이어, 월드 지오메트리만 충돌, `ContinuousSpeculative`. 조각 루트는 **몬스터의 자식이 아니다**(디스폰과 함께 증발하는 구조 회피).
- 범위: ChompBot → 잡몹 8종. **보스·중간보스 제외**(23호 `delayAfterClipEnd 2초` = 팀장 지시 2026-09-30).

## ▶▶ 작업 세션 (2026-10-02~03 · 경석(Claude) · **몬스터 공격속도 = 애니 재생 배율 · 쿨다운 종료 기준 · 이동 애니 맞춤**, 브랜치 `feature/Boss23`) — ✅ development 반영(10-03) · **SVN r372 같이 받을 것**(GameData.xlsx Monster attackSpeed→1 · Chomp 2/2.8, 핀 372) · 팀장 Play 1차(Chomp·Mortar) · ⏳ MPPM·배율 0.5/2 Play 미검증
계획·결정·교차검증 이력 = [PLAN-monster-anim-speed.md](Docs/history/PLANS/PLAN-monster-anim-speed.md) §3 · S4-b.
- `attackSpeed` = 공격 애니 재생 배율(SO 직접 — `MonsterBase.AttackAnimSpeed`, Unit.AttackSpeed 는 클라에서 0) · 공격 중 코드 타이머 ÷ · 돌진 속도 ×r · 가속 ×r².
- `attackCooldown` = 공격 **끝난 뒤** 쉬는 시간(23호만 시작 기준). `animator.speed` 는 `MonsterBase` 한 곳(23호 제외).
- 이동 맞춤 = `Tools/Monster/이동 클립 고유 속도 측정 → SO 기록`. Chomp = 블렌드 100% + 임시 속도(배회 2.0 · 추격 2.8, 입 애니 때문 — 아트 클립 요청 여지).
- 기존 버그 수정 동반: Mortar 발사·WallBot 평타 2단 트리거 클라 전파 · Spinner 예고 띠 = 실제 돌진 거리.
- 🔴 **데이터 테이블**: xlsx Monster 시트 `attackSpeed` 를 r372 에서 1 로 갱신(r371 이하를 받으면 테이블 모드·빌드에서 공격 애니가 0.5~0.8배). `attackCooldown`·이동 맞춤 값은 테이블에 행이 없어 SO 값(측정값 4개는 `[DataTableIgnore]`).
- (은희 공유) r366 `tooltip.*` 행의 코드(`feature/SkillTooltip`)는 10-06 development 반영 ✅. 빈 칸 `Paladin!C32`·`Gunner!C18` 은 `tooltip.*` 칸이면 빈 문자열로 통과, 아니면 여전히 테이블 Play·빌드 차단 — 미확인.
- ⚠️ EditMode 기존 실패 2(`BossCounterDataTests` — No23 Dash 가 FarthestPlayer) 는 이 작업과 무관, 미결.

## ▶▶ 현재 인수인계 (2026-10-02 · Claude · **존 원본 0.98 축소** — 방이 줄어든 만큼 존 내용물도 줄임, 브랜치 `feature/ZoneAssetScale`)

결정·범위·검증·팀원 업데이트 순서 = [PLAN-zone-asset-scale.md](Docs/history/PLANS/PLAN-zone-asset-scale.md). 사용자 단계별 검수·Play 확인 완료(10-02).
- 존이 쓰는 FBX 82개 Scale Factor 0.98(SVN `.meta`) + 원본 프리팹·존 11개 배치 0.98 + 존 밖(보스룸·복도·Stage1 복도벽)은 보정 스케일 1/0.98 로 **겉모습 그대로**.
  튜토리얼 문 8곳은 복도 바닥을 늘려 메움. 존 바닥은 벽 밑단 밑으로 20m 변 31cm · 40m 변 11cm.
- 🔴 **SVN r371(FBX meta 82 · 볼록 충돌 16 · `floor_MV.prefab`, 핀 371)과 git 을 같이 받아야 한다** — 한쪽만 받으면 존 바닥 4m 마다 8cm 틈/겹침. r366 GameData.xlsx(SkillTooltip)도 같이 딸려 온다.
- 🔴 존 바닥이 벽까지 닿는지는 벽 바운드(0.634)가 아니라 **벽 밑단(중심선 ≈0.52)** 으로 잴 것(0.97 시도에서 이걸로 틀림). 소품·통로 한계는 그대로 콜라이더 안쪽 면(0.634).
- Stage1 은 범위 밖: 문마다 0.2/0.4m 바닥 틈(목록만). 롤백·패치 스크립트는 이 PC `_backup/zone-asset-scale-20261002/`(git 제외).

## ▶▶ 작업 세션 (2026-10-02 · 은희(Claude) · **플레이어블 스킬 툴팁 + 캐릭터별 아이콘**) — ✅ 구현(Codex `a9fe968c` + Claude `3dbf7f13`) · EditMode 82/82 · Verify 0 · xlsx SVN r366(툴팁 30행 TODO 문구) · 🔴 Play·MPPM 남음 · 브랜치 `feature/SkillTooltip`

계획·결정·남은 질문 = [PLAN-skill-tooltip.md](PLAN-skill-tooltip.md).
- 확정: 문구 = 스킬 SO·패시브 컴포넌트의 `displayName`·`description` → 기존 `Paladin`/`Gunner` 시트 행 · 거너 패시브 = 과열(`GunnerHeat`) → `Slot_P` · 자리표시자 `{필드}`·`{필드:%}`·`{dmg}` · 피해 = **계수 + 실제 수치** · 아이콘 = 스폰된 캐릭터 Variant 데이터에서(캐릭터마다 다름).
- 다음: 은희 Play·MPPM → 기획이 TODO 문구 교체(SVN 잠금). 🔴 xlsx r366 과 이 브랜치 코드는 같이 나가야 한다. 툴팁 칸(`tooltip.*`)만 빈 칸 = 빈 문자열(그 외 빈 칸 = 오류, 판정은 Applier 로 이동). 저작 메뉴 = `Tools/UI/스킬 툴팁 구성`(재실행 가능). 스킬 아이콘 = 각 출처 `tooltip.icon`(지금 전부 비어 있음 — 아트 대기) → 슬롯 프레임(`slot_skill`) 안 자식 `Slot_*/Icon`(은희 제작) 에만 들어간다. 프레임은 덮지 않는다. 저작 메뉴는 `Icon` 을 찾아 연결만 한다(만들거나 배치를 바꾸지 않음). 칸 클릭 = 스킬 키(D18). 피해 = 계수+실제, 차지·과열은 최소~최대 범위 · Shift = 계산식 · 키워드 = TMP `<style>` 태그. 🔴 **수정 예정 — 동시 수정 금지(구현 시작 시)**: `Assets/1.Scripts/UI/Combat/*`·`CombatHUD.prefab`·`PlayerSkillData` 계열·`FirstMeleePassive`·`GunnerHeat`·`Assets/1.Scripts/DataTable/Editor/DataTableTemplate.cs`·`GameData.xlsx`(SVN 잠금).

## ▶▶ 현재 인수인계 (2026-10-02 · 은희(Claude) · **데이터 테이블 xlsx → 게임 수치 + Dev Boot 툴바** → development 반영)

사용법 = [Docs/tech/data-table.md](Docs/tech/data-table.md) · 설계·결정 = [PLAN-data-table.md](Docs/history/PLANS/PLAN-data-table.md). 브랜치 `feature/DataTable`(+ `feature/DevBootToolbar` 병합).
- **원본 = `Assets/50.Art/DataTable~/GameData.xlsx`(SVN, 잠금)**. 툴바 `데이터: 테이블/인스펙터` = Play 출처(테이블은 메모리에서만 적용·종료 시 복구). **빌드는 항상 xlsx 값**(Build 버튼 훅, 빌드 후 파일째 원복).
- 대상 = SO + **프리팹 컴포넌트**(시트 = 타입 이름, Id = 에셋/프리팹 파일 이름). 프리팹 값을 SO 로 옮기지 않았다. 노출 = `[DataTableSheet]`, 기술 값 = `[DataTableIgnore]`.
- 🔴 **게임 코드 변경(은희 지시)**: `Player.moveSpeed` 삭제(Unit 이동 스탯 = `PlayerMovement.maxSpeed`) · `fallDamageRatio` 씬 → `PlayerGameRuleData` · `PlayerMovement.Start()` rotate_Speed 덮어쓰기 제거 · MapScene `Temp_MultiGameRule` → `GameRule.prefab`.
- 🔴 **경석 영역(은희 지시, 어트리뷰트만 — 동작 변경 0)**: `MonsterDataSO`·`BossDataSO`·`MonsterMeleeAttack`·`MonsterCounterWindow`·`TurretHeadAim`·`Gauntlet/Spinner/WallBot`·`MonsterBase`·`LinearKnockback`. 보스 수치 239개의 기술 값 판단·23호 M6(폭탄·장판·송전기, 장판 작업 후)·레거시 `Enemy/*` 삭제 후보 — **경석 공유 필요**.
- 🆕 **10-02 D7 시트 개편**(브랜치 `feature/DataTableLayout` → development 반영, xlsx SVN r362): **세로 표**(행 = 필드, 열 = 대상, 설명 = B열) · 카테고리 6장 `Player`/`Paladin`/`Gunner`/`Monster`/`MidBoss`/`Boss`(`[DataTableSheet("Monster", Order = n)]`) · `Paladin_VFX` 제외. 병합 Export = 배치 새로 짜고 값은 (대상, 필드) 로 이전 → 필드 1020 · Verify 0 · 테스트 74/0. 🔴 새 xlsx 와 새 코드는 **같이** 나가야 한다(옛 코드는 세로 표를 못 읽음).
- Dev Boot 툴바: `▶ Dev Boot` 버튼 + 씬 드롭다운 분리, 파스텔 배경(🔴 에디터 확인 대기).
- 검증: EditMode 68건(`Tools/Tests/데이터 테이블 EditMode 테스트 실행`) · Verify 0(1038 필드) · ✅ **은희 10-02 전부 확인**: 테이블/인스펙터 Play(체력·공속·목숨 수 — Dev Boot·씬 직접) · MPPM · 실제 빌드 · 레거시 정리 후 23호 잡기·보스방·장판 VFX.
- **`GameData.xlsx` = SVN r362**(`art-svn.json` 핀 362) — 10-02 D7 세로 표·카테고리 6장. `DataTable~` 는 SVN 기본 무시 규칙(`*~`)에 걸려 `svn add --no-ignore` 로 추가했다 — 새 xlsx 를 더 만들면 같은 방법으로.
- ⚠️ 이 브랜치의 `857c8454`~`84cd98a8` 은 CONTEXT.md 를 21줄로 잘랐다(정규식 실수) — `origin/development` 병합 때 원래 내용으로 복구됨.

## ▶▶ 작업 세션 (2026-10-01~02 · 경석(Claude) · **몬스터 리디자인 Flat Kit · 23호 전기 장판 · 웰즈 자폭 드론 · 터렛 조준선 · 차징 루프**, 브랜치 `feature/Boss23`, 푸시 `6d50defb`)

**상태: ✅ development 반영(10-02, 경석 직접 병합 `68730770`).** 팀장 Play·비주얼 확인 완료. 남은 것 = MPPM 2인 검증 · 장판 패턴 SO 편집(보류).
- **플레이어 Flat Kit Variant**(10-02): 부모 = `FlatKit/Player/FK_Paladin_Toon`(가붕이 몸) · Variant = 검·방패 · `Gunner/FK_Gunner_Body`(4K) · `Gunner/FK_LaserGun`.
  셰이더·외곽선·셀 값은 **부모에서만** 고친다. 재구성 = `Tools/Rendering/Flat Kit/플레이어 Variant 구성 (가붕이 부모)`. 은희 ToonLit Variant(`3.Materials/Toon/`)는 남겨 둠(미사용).
- 전원 유령(부활 대기)이면 전기 장판 정지 — 드론은 대상 선정에서 원래 멈춤.
- **전기 장판·자폭 드론** — 계획·진행·피드백 이력 = [PLAN-boss-electric-drone.md](Docs/history/PLANS/PLAN-boss-electric-drone.md) §6 · 기획 사본 [Docs/design/boss/](Docs/design/boss/).
  컴포넌트 `BossElectricFloor` · `WellsDroneAttack`(NetworkBehaviour 아님 — 23호가 스폰 때 AddComponent, ClientRpc 중계). 23호 상태는 폴링
  (`IsChargeJumpActive`·`IsChargeGimmickActive`·`ActivePauseConditions`·`IsFightActive`). 정지 조건 = `BossPauseCondition` Flags(SO `pauseOn`).
  수치·연출 전부 SO: `2.Prefabs/Monster/Data/BossElectricFloorData` · `WellsDroneData`(No23·No23_Solo 연결, `Tools/Boss/전기 장판·자폭 드론 데이터 만들기·연결`).
  교차검증(Codex + Claude) 반영 `c9fcfd95`. EditMode 10/10(`Tools/Tests/보스 패턴 EditMode 테스트 실행`). **MPPM 미검증.**
  팀장 확정 튜닝: 드론 범위 1.25칸 · 드론 2배 · 23호 피해 120 · 송전기 시작 = 점프 출발 · 화면 좌/우하단 대각선 비행(DashStart→DashLoop, 느리게→급가속) ·
  크로스헤어 초록→주황→빨강(점멸 없음). ⏸ 보류: 장판 패턴 모양 SO 편집(가능 확인만, 7×7 고정 — PLAN §6-2).
- **터렛 조준선**(PeekABot·TeslaBot `TurretHeadAim`): 추적 앞 절반 초록 → 주황 → 고정 빨강 → 발사 순간 꺼짐. 색·전환점 프리팹 인스펙터.
- **23호 차징 클립** `BossChargeClipLoop`: 처음 1회 f0~ → 이후 **f62~f125 반복**(자세·속도 이음매 최적). `BossDataSO.chargeLoopStart/EndFrame`. 차징 제한시간 **30초**.
- 🔴 미커밋(내 것 아님): `BossPatternVisuals.cs` 가 `Resources/BossPatterns/WellsDroneCrosshair.png` 를 먼저 쓰도록 바뀜 + 그 텍스처 — 작성자 확인 후 커밋.
- 로컬 전용(커밋 금지, `.git/info/exclude`): `Monster/Editor/BossClipMotionProbe.cs`(차징 클립 움직임·이음매 분석).
- development(`8c5117a9`, 이지원 최종 메쉬·SurfaceV1 머티리얼) + SVN r356(드론 `Char/Drone/`) 반영. 몬스터 프리팹 8종은 **아트판 그대로** 채택 — SurfaceV1 = URP Lit(BaseMap·Normal·MetallicGloss).
  미사용 예전 FK 4종 삭제(`7fa7d382`). 드론 모델 DRONE.fbx 는 아직 Flat Kit 아님.
- ✅ 결정(10-01): Flat Kit Stylized Surface 를 **그대로** 쓴다. 이 셰이더는 GI 를 metallic 0·smoothness 0 으로 고정 계산해
  금속성 맵·반사가 빠지지만 **아트팀이 문제없다고 확인**(팀장 전달). 셰이더 복제 확장 안 함. 전환 = `Tools/Rendering/Flat Kit/Convert Characters`.
- 물 작업(이전 세션)은 커밋·푸시 완료 — 남은 것: WaterPart 모서리 다듬기 · Play/MPPM. 상세 [PLAN-flatkit.md](PLAN-flatkit.md) 9-b.

## ▶▶ 작업 세션 (2026-10-02 · 은희(Claude) · **플레이어블 공통 툰 머티리얼**, 브랜치 `feature/PlayableToonMaterial`) — ✅ 구현 · ✅ Play 확인(은희). 남은 것 = PR. 검·방패는 텍스처 없어 흰색(결정). `Paladin_VFX`·Legacy 는 부모 직참조라 흰 몸체(보관용, 수용)

계획 = [PLAN-playable-toon-material.md](Docs/history/PLANS/PLAN-playable-toon-material.md). `Paladin_Toon` → `PlayableCharacter_Toon` 부모 + 캐릭터·무기별 Material Variant(Base Map만 다름).
🔴 **수정 예정 — 동시 수정 금지:** `Assets/3.Materials/Toon/Paladin*_Toon.mat`·신규 Variant, `Player/Paladin/Paladin_Armature.prefab`·`Player_Paladin.prefab`, `Player/Gunner/Gunner_Armature.prefab`.

## ▶▶ 현재 인수인계 (2026-10-02 · 은희(Claude) · **결과 화면 클라 표시 수정** → development 반영 `515b247a`, PR 없이 직접 병합)

- 증상: ResultScene 에서 처치 수·생존 시간이 호스트에만 나오고 원격 클라는 `-`/`--:--`. 원인은 집계·`SessionResult.Capture` 가 서버 전용이고 static 이라 클라에 값이 안 감.
- 수정(`MapSceneManager`): 결과 전환 named message `MapScene.GoToResult` 의 더미 바이트 대신 `HasValue/Cleared/SurvivalSeconds/Kills` 를 실어 보내고, 클라 수신 핸들러가 `SessionResult.Capture` 후 전환한다. 전환 신호와 같은 메시지라 씬 언로드와의 경쟁 없음.
  - 🔴 `Capture` 는 브로드캐스트보다 먼저 불려야 한다(현재 `PartyWipeWatcher`·`BossEncounterDirector` 모두 충족). ExitButton 경로는 `HasValue=false` → 호스트·클라 모두 대시.
  - ⚠️ 메시지 포맷 변경 — 구버전 빌드와 섞어 접속하면 읽기 실패.
- 검증: 은희 MPPM 테스트 완료. ⏳ 경석에게 PR 없이 병합한 것 공유.
## ▶▶ 현재 인수인계 (2026-10-02 · Claude · **벽 모듈 메시 정렬** — ✅ 반영 완료(git `2ddf2838` → development `578569f6` · SVN r363), 🔴 Play 미확인)
계획·결정·실측 = [PLAN-wall-modules.md](Docs/history/PLANS/PLAN-wall-modules.md). 벽 메시 5종(피벗 중앙·4.000m, 이음새 실금 막으려 끝면을 살짝 겹치게 — 지상 ±2.001m · 지하 ±2.0005m)과 코너 조립(COM 피벗 = 벽 중심선 교차점)을 4m 그리드에 맞췄다.
- 🔴 **SVN 8개(FBX 6 — 복도벽 포함 + `walll_brick_cornerCOM_*` 2)와 git 5개(`bossroom.prefab` · `Level_wall_hallway_tutorial.prefab` · `StageTutorial.prefab` · `TutorialStageAuthoring.cs` · `all_mesh.unity`)는 함께 반영해야 한다.** 한쪽만 들어가면 벽·콜라이더가 최대 1.9m 어긋난다. SVN 업데이트는 Unity 끄고(바이너리 FBX).
  👉 **팀원 업데이트 순서**: Unity 끄기 → SVN 을 r363 이상으로 → git `development` 를 `578569f6` 이상으로 → Unity 열기.
  둘 중 하나만 받으면 코너·복도가 벌어져 보인다(2026-10-02 실제로 발생 — 로컬 git 이 병합 전 커밋에 머물러 있었음).
- bossroom: 방 안쪽 27.37 → **26.73m 정사각**, `InvisibleBoundaries` 안쪽 면 ±13.366, `NavMeshMargin` 재생성(±12.866). 🔴 Play 에서 `[23호] NavMesh 여유` ≈1.5 확인 필요(경석 영역).
- 튜토리얼 hallway: 방 5개 변당 0.32m 축소(중심 ≤6cm 이동), 복도벽은 메시 몸통만 늘린 FBX(끝 맞물림 블록 유지, 스케일 1, 상단 빔 끝을 문 H빔까지 연장, 끝 블록은 방 벽 앞면에 맞춰 7.4mm 뒤로), 콜라이더는 `Tutorial/1. Add Wall Colliders` 로 재생성.
  **존 5개를 "존 바닥 끝 = 벽 중심선"으로 재정렬**(`StageTutorial` 슬롯·`TutorialStageAuthoring.Slots`·`all_mesh` 존 — 세 곳 일치) + 복도 바닥 ×1.005 → 문 바닥 구멍 0, 벽 밖 소품 28→1.
  🔴 부서지는 상자 3개가 벽에 크게 묻힘(레벨 담당 판단) · 방끼리 NavMesh 연결돼 몬스터가 문을 넘어올 수 있음(Play 확인) · 개발 빌드에서 문턱 콜라이더 확인. 상세 PLAN §존 소품.

## ▶▶ 현재 인수인계 (2026-10-01 · 은희(Claude) · **데미지 숫자 Re:C v0.2 + 자체 이징 `EuniTween`** → development 반영 — ✅ **완료(은희 10-02 확인)**)

계획·결정 원본 = [PLAN-damage-popup.md](Docs/history/PLANS/PLAN-damage-popup.md) (기획 원본은 레포 밖 `Re_C_데미지_숫자_표기.md`). 1·2단계 ✅ 구현, EditMode 122건 통과. Play 확인 완료.
- **`EuniTween`**(별도 asmdef `Assets/1.Scripts/EuniTween/`): `Ease` 31종 + `Easing.Evaluate`·`Punch`. 트윈 라이브러리 도입 안 함. 러너 없음(필요해질 때).
- **데이터**: `AttackType` 끝에 `SkillQ/E/R` · `AttackHitPattern{Single,Multi}`(Multi = 가붕이 Q·거너 R) · RPC 에 2바이트 · 누적 키 = 공격자+타입+대상.
  `MonsterRank`(`MonsterDataSO.rank`, `Unit.Rank` virtual — 몬스터·더미가 덮어씀). 🔴 **경석에게 rank 필드 추가 알림 필요.**
- **연출**: 오버레이 Canvas(1920×1080) · 단발 = 맞은 자리 고정 / 누적 = 대상 추적 · 이미지 글꼴(`FloatingDamageDigitSet`, `50.Art/UI/damage`).
  🔴 **기획과 다른 은희 결정**: 누적 추적 · 강도별 채움 색(노랑/주황/빨강) · 누적 숫자의 크기·색 = 누적 합계 기준 · 크기 50/100/140% · "굵게" 없음(이미지). 기획자 공유 필요.
- **더미**: `TrainingDummy`(일반, HP 100) · `_Elite`(MidBoss 300) · `_Boss`(Boss 2000) Variant, TrainingDummy 씬 배치. 안 맞으면 스폰 자리 복귀.
- ⏳ 남은 것: 외곽선 PNG 가 회색(175)이라 흰 외곽선은 아트 교체 필요 · `50.Art/UI/damage/*.png.meta` 슬라이스 **SVN 커밋 확인** ·
  `DefaultNetworkPrefabs` 에 SVN 검수 프리팹 8종 등록됨(`08f29f47`, SVN 없으면 빈 참조) · PR.

## ▶▶ 현재 인수인계 (2026-10-01 · 은희(Claude) · **원거리 캐릭터 "거너" G0~G9 구현 완료**, 브랜치 `feature/SecondCharacter`, push 안 함)

기획 원본 [character_gunner.md](Docs/design/character/character_gunner.md)(§0 확정 변경 D1~D15) · 계획·진행 [PLAN-gunner.md](PLAN-gunner.md) §7 · 프리팹 구조 [player-prefabs.md](Docs/tech/player-prefabs.md) §0.
**징크스(스택 폭발) 기획은 폐기** — 기존 원거리 슬롯·모델을 거너가 쓴다(아트 `Assets/50.Art/Char/gunner/`, SVN 핀 **346**).
- 🔴 **전 캐릭터 공통 코어 변경**(가붕이 Play 확인함): 보호막 = 종류·출처별 인스턴스(`ShieldType`, 사망 시 전부 제거, HUD = 남은 합/부여 합) ·
  기본 공격 = `IPlayerBasicAttack`(base 에서 빠지고 Variant 가 얹음) · `PlayerActionState` 끝에 **`AttackReady`·`Focus`** ·
  스킬 시스템 확장(수동 쿨 커밋·실행 중 좌클릭·`OnFixedTick`·`EntryActionState`·`CanBeCanceledByDash`) · `PlayerMotor` 일시 아군 차단/적 통과 ·
  **대시 우선은 행동이 허락할 때만**(가붕이는 전부 아니오 = 기존과 같음).
- 거너 조립 = 메뉴 `Tools/Player/Gunner/*`(재실행 안전). 스폰 = `Dev/Dev Boot/캐릭터/거너`(개인 EditorPrefs). 정식 캐릭터 선택은 미구현.
- ⏳ **후속**: 임시 연출 교체(발사선·폭발선·추적 레이저 원기둥 → **민경 VFX**, 훅 = `GunnerBeamView`·`GunnerHeat.StageChanged/OverheatChanged`) ·
  임시 과열 게이지(OnGUI) → CombatHUD 정식 UI · **R 대상 사망 후 재탐색 미검증** · 밸런스 수치(전부 SO) · 캐릭터 선택 경로(player-prefabs.md §8.3) · R 기본 잠금(빌드 시스템 몫, D15).

## ▶▶ 현재 인수인계 (2026-09-30 · 은희(Claude) · **플레이어 base+Variant + 패시브 버프 모델** → development 반영)

`fix/Player` 를 development 에 **직접 머지·푸시**(은희 결정 — PR 리뷰 생략). 구조 원본 = [player-prefabs.md](Docs/tech/player-prefabs.md) §0·§7.
- **스폰 = `Player_Paladin`**(`Player.prefab` 의 Variant). 역할 동작은 base, 가붕이 고유는 Variant, 몸체는 `Paladin/Paladin_Armature.prefab`.
  구 `Paladin`·`TempPlayer_Armature` → `Player/Legacy/`, `Paladin_VFX` 는 `Player/Paladin/` 보관(스폰 안 됨). 🔴 **민경: VFX 작업은 `Player_Paladin.prefab` 에서.**
- **패시브 = `PassiveCharge` 버프 모델** + 적중 전 훅(`IPlayerOnHitBonus`, 막타 합산) + 범용 적중 이벤트(`Player.ServerAttackLanded`, 현재 구독자 0 — 스택·빌드용). [PLAN-passive-onhit.md](Docs/history/PLANS/PLAN-passive-onhit.md) §12·§13.
- **죽은 대상은 피격·피해를 거절**(`Unit.ReceiveAttack`·`ApplyHealthDamage`) — 전 유닛 공통 동작 변경. **FloatingDamage = 방어 후·클램프 전 최종 피해(초과분 포함)**, 모든 피해 경로.
- 경석 `ClearDebuffsServer()`(09-29) 와 합류 확인: `PassiveCharge` 는 분류상 Buff → 보스 연출(`ClearDebuffsServer`)에 **남는다**(PLAN R-1 해소, Play 재확인 필요).
  아래 경석 09-29 항목의 "패시브는 상태효과를 안 쓴다" 는 이 머지로 **낡았다**.
- ✅ 버프/디버프 판정 일원화(2026-09-30): 경석 `IsDebuff` 삭제 → `ClearDebuffsServer` 가 `StatusEffectCategories.Of` 사용. 현 타입 결과 동일, 표에 없는 새 타입만 "경고 + Debuff" 로 바뀜.
- ⏳ **후속**:
  캐릭터 선택 경로(§8.3) · 원거리 투사체 네트워크 스폰 · `Player.ReceiveAttack` 의 `shieldVfx` 결합 · `PlayerEncounterLockAuthoring` 의 `Paladin_VFX` 순회(경석 판단).

## ▶▶ 작업 세션 (2026-09-29 · 은희(Claude) · **Group Painter "벽 그룹" 표시**, 브랜치 `feature/WallGroupVisualize`)

Group Painter 툴바에 **`벽 그룹`** 토글 추가 — 씬(또는 열린 프리팹)의 `WallTransparencyGroup` 마다
`targetRenderers` 를 팔레트 색 하나로 씬 뷰에 칠한다. 기존 ObjectId 오버레이를 그대로 쓰고 색 소스만 하나 늘렸다.
WallTransparencyGroup 오브젝트를 선택하면 그 그룹만 또렷해진다. 둘 다 켜면 Painter 그룹 색이 위.
수정: `WallTransparencyGroup.cs`(읽기 전용 `TargetRenderers` 만), `Editor/TransparentGroupSession.cs`·`Visualizer.cs`·`Window.cs`.
상태: dotnet build 통과, **에디터 확인 대기**.

## ▶▶ 현재 인수인계 (2026-09-29 · 경석 · `feature/Boss23` → development 반영)

- **다음 세션 = 전기장판 · 자폭드론** — 기획 문서 수령 대기(받으면 바로 착수). 자폭드론 자리: `TwentyThreeBoss.OnWellsAttackCycle`(Wells 공격 주기 — 현재 빈 자리 경고).
- 09-29 반영: 취약 넉백(방 회전) · 넉백 종료 시 그로기·취약 종료 · 제압 그로기 루프 · 취약 중 돌진 · 어퍼 예고 0.5 · 차징 점프 착지 범위 공격 · 점프 체인 안 끊김 · 잡기 낚아채는 프레임 부착 · 점프 착지 경계/Warp 복구 · 보스방 NavMesh 여유 띠(실측 1.5m) · Start → 튜토리얼 스테이지([PLAN-tutorial-stage.md](Docs/history/PLANS/PLAN-tutorial-stage.md)) · 차징 오라 데칼 제거 · 진입 연출 디버프만 해제 · Dev 공격 예약 단축키(F3/F4/F6/F9/F11, 대기열 8, Shift+F3 비우기).
  상세: [PLAN-boss-counter-vulnerable.md](Docs/history/PLANS/PLAN-boss-counter-vulnerable.md) §7-2.
- 🔴 **10-01 — Flat Kit 전환(캐릭터·물) · `feature/Boss23` 에만 푸시(development 미반영)**. 계획·기록 [PLAN-flatkit.md](PLAN-flatkit.md).
  - 캐릭터: 몬스터 8종·23호·Wells·플레이어 → `FlatKit/Stylized Surface`(플레이어는 부드러운 법선 복제 셰이더). 룩 값 한 곳 `Assets/1.Scripts/Rendering/Editor/FlatKitCharacterLook.cs`(외곽선 0.4 = 1080p 1.08px). 메뉴 `Tools/Rendering/Flat Kit/`.
  - 물: 보스방 `Water_BossRoom`(벤트 밑 −0.12) + 존 프리팹 `Water`(**`ZoneWater` = 존별 수면 높이 한 칸**, 기본 −4.3414(10-02 존 0.98 축소, 원래 −4.43), 재실행해도 유지). 존 물은 **뚫린 곳(구덩이·벤트)에만** 깔린다(구덩이 벽 안쪽 면까지). 머티리얼 `FK_Water_Pool09` 은 월드 UV 복제 셰이더 — 인스펙터로 조절(도구가 안 덮어씀). 맵 330m 큰 쿼드는 팀장이 삭제.
  - PC_Renderer: Flat Kit 외곽선 피처 추가 · MaskBlur 끔 · SSAO 값 조정 · 그림자 캐스케이드 2/35m · 데칼 50m · LOD Cross Fade·Terrain Holes·데이터 기반 렌즈 플레어 끔.
  - 🔴 Flat Kit 데모 씬을 열면 URP 에셋이 데모용으로 바뀐다 → `Restore Project Pipeline`. `Assets/FlatKit/Demos` 는 git 제외.
- 🔴 **09-30 — 23호 사망 타이밍**: 사망 클립 0.7배속 → 끝난 뒤 2초 → 디졸브 2초 → 결과 화면(대기 0). `DissolveDeath.delayAfterClipEnd` 신설(기본 0). 상세: [PLAN-boss-death-telegraph.md](Docs/history/PLANS/PLAN-boss-death-telegraph.md) 끝. ✅ 팀장 Play 확인.
- 🔴 **09-29 저녁 — 튜토리얼 스테이지 180° 회전 + 은희 투명화 존 머지**(`fix/stage_tutorial260929`). **SVN r340 필수**(구석 `walll_brick_cornerCOM_*`). 상세: [PLAN-tutorial-stage.md](Docs/history/PLANS/PLAN-tutorial-stage.md) 끝. ✅ 팀장 Play 확인(문제없음). `d9519cbb` development 반영.
- 🔴 **SVN r338 필수** — development 의 `WallTransparencyDither.hlsl`(은희)과 r338 `Generic_Standard.shadergraph` 가 짝. r336 이하면 화면 전체 분홍(`undeclared identifier WallTransparencyDither_float`). 핀 `art-svn.json` = 338.
- ⏳ 남은 것: 잡기 부착이 매번 `안전망` 으로 붙는다(클립 이벤트와 구간 타이머가 같은 순간 0.786s — 타이머가 Update 에서 먼저 닿음) → 짧은 유예로 이벤트 경로 우선 · 임시 진단 로그 3종 삭제(`[23호/점프진단]`·`[모터/끼임진단]`·`[23호] NavMesh 여유`) · 모터 EditMode 테스트 · 벤트 오브젝트(제작 중) 연동 확인 · 투명화는 **아트 쪽 작업**(튜토리얼·보스방 미적용).
- ⚠️ 기존 버그(범위 밖): 결과 씬 `ResultSceneManager.cs:24` `AudioManager.Instance.StopBGM()` 널 참조(이 흐름에 AudioManager 없음) — 사운드 담당.

- 사망 연출·중간보스 예고: [PLAN-boss-death-telegraph.md](Docs/history/PLANS/PLAN-boss-death-telegraph.md) (팀장 확인 완료).
- NavMesh(몬스터 쪽): 추격 목적지 투영 + 도달 불가 시 대기(`MonsterBase.ChaseTarget`) · 재부착 수평 거리 기준 + 리쉬 기준점 갱신(`MapNavMeshBaker`) ·
  23호 에이전트 반경 유지 · 넉백 종료 Warp 1m + 같은 섬. **아트 쪽(FBX Read/Write · 누락 콜라이더 · 보행 불가 지정)은 미착수** —
  Read/Write 꺼진 MeshCollider 는 베이크 때 AABB 박스가 돼 계단·코너벽·기둥이 틀어진다(`UnreadableMeshColliderBakeScope`), `wall_basic_square` 콜라이더 없음, 보스방 FBX 272개 콜라이더 없음.
  ⚠️ SVN r333 새 벽 프리팹 5종(`Environment/Prefabs/Layouts/wall/`)도 **콜라이더 0 · Read/Write 꺼짐** — 보스방은 투명벽이 막아 무관, 다른 존에 쓰면 통과된다(지원 공유 필요).

## ▶▶ 🔴 은희에게 — 플레이어 겹침 해소 요청 (2026-09-28 경석, `feature/Boss23`)

- 보스·중간보스가 **플레이어를 막게** 했다: `PlayerGameRuleData.asset` obstacleMask 에 Enemy 추가(2185 → 2441),
  GauntletBot·SpinnerBot 몸 캡슐 `m_Enabled: 1`. 상세·근거는 [PLAN-player-motor.md](PLAN-player-motor.md) Enemy 항목의 ⚠️ 정정.
- **요청**: 이미 겹친 상태를 풀어 주는 처리. 서버가 움직이는 보스가 플레이어를 파고들면(추격·돌진·잡기 해제)
  `PlayerMotionSweep` 의 CapsuleCast 가 시작 겹침을 거리 0 · 법선 = −이동방향으로 돌려줘 **전 방향이 막힌다**(끼임).
  제안: 스윕 전에 `OverlapCapsule(obstacleMask 중 Enemy 비트)` → `Physics.ComputePenetration` 로 **수평만** 밀어내고,
  그 변위도 정적 마스크 스윕을 거쳐 벽을 뚫지 않게. 서버·오너가 같은 모터를 돌리므로 한 곳에 넣으면 된다.
  (모터 코드는 은희 담당이라 경석이 손대지 않았다.)
- ⚠️ **09-28 추가 — 팀장 Play: 23호 돌진이 끝난 뒤 플레이어가 확실히 낀다.** 보스 쪽에서는 완전히 못 막는다:
  ① 이 브랜치는 `ServerAuthoritativeMovement = false` — 돌진 캐리 중 오너가 **자기 화면의 보스 복제 위치**를 따라 움직이는데,
     보스는 NetworkTransform 보간으로 뒤처져 있다가 돌진이 멈추면 **따라잡으며 콜라이더가 플레이어 안으로 들어온다**(정상 종료엔 분리 없음, 넉백은 벽 충돌 때만).
  ② ~~23호 NavMeshAgent 반경이 런타임에 데이터 값 0.3 으로 덮인다~~ → 09-28 `KeepPrefabAgentRadius` 로 막음(0.85 유지).
  ③ 서버가 미는 수단(넉백)도 같은 스윕을 타서 **겹친 뒤에는 빼내지 못한다.** → 겹침 해소는 모터에서만 풀린다(팀장 09-28: 은희에게 그대로 넘김).
  경석 쪽 완화: 훅·어퍼 전진은 앞 플레이어에 닿으면 멈춤(`TwentyThreeBoss.PlayerBlocksLunge`).
- 🔴 **09-29 경석 — `PlayerMotionSweep.cs` 동작 변경(팀장 승인: 진단으로 원인 확정 시 진행).** `TryCast` 가 **Enemy 레이어의 시작 겹침 히트**(distance 0 · point 0)를, 이동 방향이 `ComputePenetration` 분리 방향과 같은 쪽(내적 ≥ 0)일 때 무시한다(`IsEscapingEnemyOverlap`). 근거 실측: `[모터/끼임진단]` 거리 0 · 법선 −dir · 시작겹침 True · 관통 0.48m, 이동 방향 = 분리 방향인데도 막힘. 강제 밀어내기 없음(Codex 지적: 벽 사이·입력 경합 회피) — 더 파고드는 방향은 그대로 막힌다. 넉백(보스 바깥 방향)도 같은 이유로 막혔던 것으로 보고 함께 풀린다. ⚠️ 모터 EditMode 테스트 미실행.
- ⚠️ 같은 파일에 **임시 진단 로그**(동작 무변경).
- 🔴 **09-29 경석 — 코어 변경 알림(팀장 요청): 보스방 진입 연출이 디버프만 지운다.** `StatusEffectController.ClearDebuffsServer()` + `IsDebuff()` 추가, `PlayerEncounterLock.BeginCinematicServer` 가 `ClearAllServer` 대신 호출. 판정 = 차단류 6종(Airborne·Stunned·Slowed·Rooted·Silenced·Debilitated) 디버프 · SuperArmor 버프 · modifier 는 배율 <1 디버프 / >1 버프 / =1 중립. `ClearAllServer` 는 그대로(TrainingDummy 사용). ⚠️ 현 코드에서 플레이어 버프형 상태는 SuperArmor(메인·궁극기)뿐 — 패시브는 상태효과를 안 쓴다. 버프형 상태를 새로 걸 땐 배율 >1 규약을 지킬 것.
- 🔴 **09-29 경석 → 은희 요청 2건 (팀장 09-29: 플레이어 쪽은 은희에게 넘긴다)**
  1. **잡기 해제 위치**: Carry 구속 중 플레이어는 `GrabController.GrabSocket`(보스 손)을 그대로 따라간다(`PlayerStateController` 구속 상태 `TryGetTargetPose`). 보스가 방 가장자리에서 잡으면 손이 투명벽 밖이라 해제 위치가 **경계 밖·공중**이 된다 — 실측 (487.31, **1.42**, 14.23), 방 로컬 경계 ±14 초과, 이후 `Fence_MetalSheet`·`Boundary_XMax` 와 **정적 시작 겹침**으로 이동·대시 0m 고착(`[모터/끼임진단]` 시작겹침 True). 제안: 구속 종료 시 바닥 투영 + 정적 시작 겹침이면 안전 위치로(PhysX CCT 의 overlap recovery 와 같은 역할 — 정적 지오메트리 한정). 보스방 경계는 `InvisibleBoundaries/Boundary_*` 안쪽 면(방 로컬 축).
  2. **모터 Enemy 시작 겹침 탈출 검토**: 위 `IsEscapingEnemyOverlap`(경석 09-29) 유지 여부·구석(벽 2개 + 보스)에서 탈출 각도(현재 내적 ≥ 0, −0.75 로 완화 검토) — 은희 판단. `Resolve` 를 `ResolveCore` + `LogIfBlockedByEnemy` 로 감쌌다 — 수평 이동이 요청의 10% 미만으로 막히고 원인이 Enemy 레이어일 때만 0.5초에 한 번 `[모터/끼임진단]` 을 찍는다(히트 콜라이더·거리·법선·시작 겹침 여부·ComputePenetration). 원인 확정 후 경석이 지운다. (밀어내기 안은 Codex "보완 후 가능"으로 기각 — 위 좁은 수정으로 대체.) 경석 쪽 추가: 취약 넉백도 앞 플레이어에서 멈춤.

## 📌 아직 유효한 규약·함정 (색인 — 원문은 아카이브)

> 07-21 ~ 09-28 인수인계 원문 = [Docs/history/CONTEXT-archive-2026-07-21_09-28.md](Docs/history/CONTEXT-archive-2026-07-21_09-28.md).
> 끝난 계획서(`PLAN-*.md` 22개 + 09-22 이전 `PLAN.md`) = [Docs/history/PLANS/](Docs/history/PLANS/). 파일 이름은 그대로라 코드 주석의 `PLAN-xxx.md` 로 찾으면 된다.
> 아래는 **지금도 지켜야 하는 것만** 한 줄씩. 자세한 근거는 괄호 속 절 제목으로 아카이브에서 grep 한다.
> 새 인수인계는 위에 쌓고, 끝난 것은 아카이브로 옮긴다. 이 파일은 300줄 안팎을 유지한다.

- **이펙트 파사드·Skill ID 테이블은 지스타(2026-11 중순) 이후.** 그 전엔 이펙트 발동 하드코딩. 다시 제안하지 말 것. (「이펙트 구조 개선은」)
- **개발 브랜치 = 오너 권위 이동**(지스타까지). 서버 권위는 `feature/player-motor-server-auth` 에 보존. 예측 재생은 "그 틱의 모든 의도" 재현, 입력 RPC 는 raw 두 필드뿐. (「개발 브랜치 = 오너 권위」·「이번에 확정된 불변식」) 원본 [PLAN-player-motor.md](PLAN-player-motor.md)
- **공격속도 = 스탯 추가 안 함, 클립 길이 일반화.** 평타 타이밍은 애니 이벤트가 결정, `MotionDuration` 배율은 효과 없음. (「공격속도」)
- **존 프리팹에 `NetworkBehaviour` 금지** — `ZoneMonsterSpawnSet`(MonoBehaviour) + 씬 상주 매니저·`SlotID`. (「존 프리팹에 `NetworkBehaviour`」)
- **`PlayerSkillBase` 는 MonoBehaviour** — 서버 전용 연출 on/off 는 별도 NetworkBehaviour(`PlayerShieldVfx` 선례). (「`PlayerSkillBase` 는 MonoBehaviour」)
- **에셋 팩 프리팹은 풀링 전제 아님** — 스크립트 걷고 드라이버로. (「풀링을 전제하지 않는다」)
- **조용한 실패 금지** — 특히 "호스트만 됨"은 MPPM 2인에서만 드러난다. (「조용한 실패를 만들지 말 것」)
- **에디터 전용 스크립트는 `Editor/` 안에** — 밖이면 플레이어 빌드만 깨지고 Addressables 에러로 위장. 빌드 실패 시 `Editor.log` 먼저. (「`Editor/` 밖에 두면」)
- **에디터 프로파일러 프레임 총합 불신** — `EditorLoop` 90%. 성능은 Development 빌드로 잰다. (「프로파일러의 프레임 총합」)
- **Unity MCP 끊김 = 도메인 리로드·에디터 종료 구간.** Unity 서버를 재시작해 고치지 말 것, 몇 초 뒤 재호출. 수정은 몰아서 컴파일. (「Unity MCP 가 끊겼을 때」)
- **SVN 커밋 전 `svn status` 의 내가 안 고친 `M` 확인** — Unity 가 낡은 메모리 사본을 디스크에 되쓴 경우가 있었다. 경로 지정 커밋. (「SVN — **커밋하기 전에」)
- **`.csproj` 가 낡으면 `dotnet build` 노이즈** — csproj 건드리지 말고 Unity 리프레시. (「`.csproj` 가 낡아」)
- **`ProjectSettings/NetcodeForGameObjects.asset` 미추적 유지**(팀장 지시) · `.claude/worktrees`·`TempToybox` 손대지 말 것. (「손대면 안 되는 것」)
- ⚠️ 미해결 별건: `AudioManager` 인스턴스가 어느 씬에도 없음(BGM 무음·NRE, `16ec8ef0` 이후) — 사운드 담당. (「브랜치 주의 (2026-09-23)」)

## Project Summary

A top-down cooperative action game inspired by Ravenswatch-style structure.

Current near-term target:
- Start game
- Boss intro sequence
- Boss combat
- Listen-server network vertical slice

Later scope:
- Map expansion
- Growth systems
- General mobs
- Additional content

## Core Terms

- Player: A human-controlled networked unit.
- Host: The player running the listen server.
- Client: A connected player that is not the host.
- Server authority: Logic owned and decided by the server/host, then replicated.
- Owner authority: Logic controlled by the owning client, usually player input and movement.
- Unit: A gameplay actor with common state and snapshot behavior.
- UnitBase: The common base for shared unit state and snapshot only. Movement, abilities, status effects, and networking behavior should be composed with components where possible.
- Boss: A server-authoritative enemy with encounter flow, patterns, state, and network-visible presentation.
- Boss intro: The sequence before combat begins, including presentation and state transition into battle.
- State abnormality: Status effect or condition applied to a unit.
- Build: A player growth or ability configuration concept.
- Skill: A player or boss action/pattern defined by data and executed by runtime logic.
- ScriptableObject data: Authoring-time gameplay data for skills, builds, bosses, patterns, and tuning values.
- Vertical slice: A thin but complete path through gameplay, networking, UI/presentation, and verification.

## Networking Language

- Player input: Usually owner-authoritative.
- Player movement: Usually owner-authoritative unless a specific anti-cheat or server correction rule is chosen.
- Boss state: Server-authoritative.
- Enemy state: Server-authoritative.
- Damage: Server-authoritative.
- Drops/rewards: Server-authoritative.
- Scene progression: Server-authoritative.
- Snapshot: A compact representation of state needed for synchronization, save, debug, or replay-like inspection.

## Design Preferences

- Prefer composition over deep inheritance.
- Prefer data-driven tuning for gameplay content.
- Prefer small vertical slices over broad unfinished systems.
- Prefer clear module interfaces that hide meaningful implementation.
- Prefer names from this file and `Docs/` over ad hoc synonyms.

## Open Vocabulary To Resolve

Add definitions when these become concrete:
- Exact boss encounter phase names
- Player class names
- Ability categories
- Build/growth terminology
- State abnormality taxonomy
- Scene/session flow terms
- Network room/lobby terms

## Resolved Terms (2026-07-21)

- Boss enter pad: BossRoom 역할 존 중앙의 진입 패드(트리거+테두리 표시). 생존 플레이어 점유 시 카운트다운(3·2·1), 전원 이탈 시 취소. 완주 시 생존자 전원 보스룸으로 텔레포트. 튜닝은 BossTeleportManager 인스펙터.
- RangedTurret: 고정 포탑 몬스터 아키타입(PeekABot·TeslaBot). 넉백 면역, 경직만 적용.
- Knockback direction: 공격이 AttackInfo.knockbackDirection으로 명시(방향성 공격). zero면 수신측이 방사형(대상-공격자)으로 폴백(장판/폭발형).
