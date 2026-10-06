# CONTEXT.md - Shared Project Language

> 🆕 **새 환경에서 처음 여는 사람은 [Docs/tech/environment-setup.md](Docs/tech/environment-setup.md) 부터.**
> 이 프로젝트는 git 만으로 안 선다 — 아트가 SVN 에 있고, 없어도 Unity 는 조용히 열린다.
> 열기 전에 `python Docs/tech/check-environment.py` 를 돌릴 것.

This file defines the shared vocabulary for the project. Keep it concise. It is not a full spec and should not contain implementation plans.

Update this file when a term becomes important enough that future agents or teammates must use it consistently.

## ▶▶ 다음 세션 인수인계 (2026-10-06 · 경석(Claude)) — 여기부터 읽을 것

**상태**: 전수조사 정리·최적화 S1~S5 끝 → `development` = `feature/Boss23` = **`661cf289`**(푸시). SVN **r381**(핀 381, check-environment 전부 통과).
상세·실측 = [PLAN-cleanup-optimization.md](PLAN-cleanup-optimization.md) §4 · 정리 노트 = 노션 「개인포트폴리오 > 전수조사 기반 성능 최적화…」(VeyTrace 하위로 옮길 것 — 도구 권한상 직접 못 만듦).

**남은 일 (다음 세션)**
1. **ForProfile 빌드 · L존 측정** — GPU Resident Drawer 켜기 여부(에디터에선 드로우 486→193 인데 프레임 6.85→7.77ms) · 바닥 그림자 Off 효과. 켤 때 `m_BrgStripping` = **2**(1 은 전부 제거).
2. **에디터 사전 메시 병합**(같은 메시 묶어 그리기) — Codex 권장안 PLAN §S4 아래. 큰 작업 → grill·PLAN.
3. **xlsx Monster 시트 속도 반영** — Chomp 2.4/3.36 · Humanoid 2.5/3.5 · Mortar 1.75/2.1. 🔴 **r381 에서 기획(김태현)이 xlsx 를 고쳤다** → 반드시 r381 위에서 해당 칸만 수정, SVN 커밋 전 팀장 확인.
4. **담당별 목록 전달** — 은희(CONTEXT 아래 📮 목록 + 레거시 Paladin 네트워크 등록·TempPlayer_Armature·Garen 컨트롤러·R1·ToonLit 레거시·벽 투명화 레거시) · 민경(INab Demo Assets — 이펙트 프리팹이 데모 메시 30곳 참조) · 아트(SVN `VFX/**/OldVersion`·`SurfaceV1` 원본·빈 아트 폴더 4).
5. **미검증 Play** — MPPM(원격 애니·블렌드 고정) · GauntletBot 예고 · 23호 전기 장판 2회차 VFX · 피격 전이 블렌드 고정.
6. 기존: `BossCounterDataTests` 실패 2(No23 Dash) 팀장 결정 · `TitleSceneManager.cs:53` BGM NRE · `SoulVisualPrefab` 미지정 경고(은희).

## ▶▶ 작업 세션 (2026-10-06 2차 · 경석(Claude) · **S6 존별 실시간 측정 도구**, 브랜치 `feature/Boss23`) — ✅ 도구·에디터 검증·개발 빌드(미커밋) · ⏳ 팀장 빌드 측정 → 숫자 보고 다음 결정. 실행 = `Dev_Boot.unity` Play(MapScene 직접 Play ✗). 진행·실측 = PLAN §S6 진행
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
7. 기존 EditMode 실패 2(`BossCounterDataTests` — No23 Dash 가 FarthestPlayer): 데이터가 맞으면 테스트 수정, 실수면 데이터 원복 — 팀장 결정.
8. `Assets/1.Scripts/Player/Fall/FallBoundarySettings.cs` 줄 끝(EOL)만 다른 변경이 계속 뜬다 — 내 것 아님, 커밋하지 않음.

## ▶▶ 작업 세션 (2026-10-03 · 경석(Claude) · **타이틀 시작 연출 · 존 게이트 패널 모니터 켜짐·외곽선**, 브랜치 `feature/Boss23`) — ✅ 팀장 Play OK(10-04) · `feature/Boss23` 푸시 `5e3e46fe`(development 미반영). 게이트 활성 표시 = 모니터 화면(바닥 링 삭제). ⚠️ ZoneL_typeB 를 프리팹 모드로 열어 둔 채 파일을 바꾸면 Auto Save 가 덮어쓴다 — 값은 `Tools/Map/Authoring/Zone Monitor Screen/5` 로
계획 = [PLAN-title-monitor.md](Docs/history/PLANS/PLAN-title-monitor.md)(승인 10-03, Codex 설계 회의 반영). 🔴 **수정 예정 — 동시 수정 금지:** `UI/Title/*`(TitleFlowDirector·TitleMonitorDisplay·셰이더) · `Map/ZoneBridgeGate.cs`·`ZoneBridgeGateManager.cs` · `ZoneL_typeB.prefab`(게이트 값) · 신규 외곽선 셰이더.
## ▶▶ 작업 세션 (2026-10-02 · 은희(Claude+Codex) · **캐릭터 선택 화면**, 브랜치 `feature/SelectCharactorUI`) — 계획 [PLAN-character-select.md](PLAN-character-select.md) 승인됨
- ✅ 1단계 Codex(코드) `09e9cc57` · ✅ 2단계 Claude(씬·에셋·`LobbySceneManager` 배선). 결과 요약 = [player-prefabs.md §8.3](Docs/tech/player-prefabs.md)
- 🔴 남은 것: **은희 MPPM Play 확인**(호스트+클라2: 초상화·Ready 동기화 / Start 활성 / 고른 Variant 스폰) → PR. 레이아웃은 첫 배치라 Play 보고 조정 가능
- 범위 밖: 전투 HUD 초상화 Paladin 고정(Gunner 도 Paladin 얼굴)

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
- **전기 장판·자폭 드론** — 계획·진행·피드백 이력 = [PLAN-boss-electric-drone.md](PLAN-boss-electric-drone.md) §6 · 기획 사본 [Docs/design/boss/](Docs/design/boss/).
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
  상세: [PLAN-boss-counter-vulnerable.md](PLAN-boss-counter-vulnerable.md) §7-2.
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
