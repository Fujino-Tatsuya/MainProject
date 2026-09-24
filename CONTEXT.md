# CONTEXT.md - Shared Project Language

> 🆕 **새 환경에서 처음 여는 사람은 [Docs/tech/environment-setup.md](Docs/tech/environment-setup.md) 부터.**
> 이 프로젝트는 git 만으로 안 선다 — 아트가 SVN 에 있고, 없어도 Unity 는 조용히 열린다.
> 열기 전에 `python Docs/tech/check-environment.py` 를 돌릴 것.

This file defines the shared vocabulary for the project. Keep it concise. It is not a full spec and should not contain implementation plans.

Update this file when a term becomes important enough that future agents or teammates must use it consistently.

## ▶▶ 작업 세션 (2026-09-22 · **Dev 부팅 자동화 — 툴바 "Dev Boot"**, 브랜치 `tool/DevBootAutomation`)

작업자: **은희(Claude 설계 → Codex 구현 위임)**, 레인 `MainProject` (= `C:\UnityProject\MainProject`).
계획·근거·확정 결정은 [PLAN.md](PLAN.md) 최상단.
**상태: 코드·검증 완료** (커밋 `17d92726` · `d2e3bdbb` · `7d17fbfc`). Play 검증 8항목 전부 통과.
알려진 한계 2건(직접 Play 경로 · `playModeStartScene` 초기화 범위)은 **고치지 않기로 결정** — 근거는 [PLAN.md](PLAN.md) 7절.

**무엇을 푸는가** — Dev 부팅의 두 가지 마찰:
1. 부팅할 씬을 바꾸려면 공유 씬 `Dev_Boot.unity` 안의 `DevSceneBooter.scene` 필드를 고쳐야 한다
   (= git 추적 씬이 dirty, 팀원 기본값이 통째로 바뀐다. 2026-09-17 Codex 리뷰 consider #4 가 이미 지적).
2. 타겟 씬이 빌드 씬 목록에 없으면 **Play 가 이미 시작된 뒤** 로그 하나 찍고 멈춘다.
   → 자동 등록은 **Play 진입 전 에디터 훅**에서 해야 한다.

**용어** — 여기서 "Dev Boot" 는 *툴바 드롭다운에서 씬을 고르면 빌드 목록을 임시 보정하고
`Dev_Boot` 씬으로 Play 에 진입해 그 씬을 부팅하는 것* 이다. 내장 Play 버튼은 **교체하지 않는다**
(6000.3 의 `OverridableToolbar` 는 Scene 뷰 툴바만 지원. `[MainToolbarElement]` 로 옆에 붙인다).


**🔴 부팅 씬 위치 — `Assets/0.Scenes/Debug/Dev_Boot.unity`** (2026-09-22 이동, `0094e75d`).
예전 위치는 `Assets/0.Scenes/Dev_Boot.unity` 였다. meta guid `180a2dd6e0939fed247ab6908eb0ec7d`
는 그대로라 참조는 안 깨졌다. **코드는 경로가 아니라 이 GUID 로 씬을 찾는다**
(`DevBootLauncher.DevBootScenePath`) — 경로 상수를 다시 박지 말 것. 박아두면 다음 이동 때
직접 Play 판정·강제 정리 메뉴·드롭다운의 자기 제외가 조용히 안 걸린다(`7359e839` 에서 겪은 일).

**🔴 MPPM 과의 관계 — 시작 방법에 따라 갈린다 (2026-09-22 실측 확정)**

활성 MPPM 시나리오가 있을 때, **어느 쪽이 이기는지는 Play 를 어떻게 시작했느냐로 정해진다.**
`MPPM2` 를 활성화한 상태로 둘 다 확인했다.

| 시작 방법 | 메인 에디터가 시작하는 씬 | 왜 |
|---|---|---|
| 툴바 `Dev Boot ▾` 에서 씬 선택 | **`Dev_Boot`** (= Dev Boot 승) | `DevBootLauncher.Launch` 가 `playModeStartScene` 을 **설정한다.** 이건 MPPM 이 씬을 연 뒤, Play 진입 시점에 치환되므로 덮어쓴다 |
| `Dev_Boot` 씬을 열어둔 채 내장 Play | **시나리오의 InitialScene** (= 프로필 승) | `PrepareDirectDevBootIfNeeded` 는 **빌드 목록만 보정하고 `playModeStartScene` 은 건드리지 않는다.** 덮을 게 없으니 MPPM 이 연 씬이 그대로 간다 |

**이 갈림은 의도된 것이다(2026-09-22 은희).** 시작 방법을 바꾸는 것만으로 "Dev Boot 단독 부팅" 과
"MPPM 시나리오대로" 를 골라 쓸 수 있다. 경고·거부 가드는 **의도적으로 넣지 않았다** — 가드를 넣으면
이 선택지가 막힌다.

왜 두 기구가 이렇게 노는가 — MPPM 구현은 패키지가 아니라 에디터 내장
`UnityEditor.MultiplayerModule.dll` 에 있고(`com.unity.multiplayer.playmode@2.0.2` 는 문서만 든
껍데기다), 그 DLL 은 `playModeStartScene` 을 **전혀 참조하지 않는다.**
`SetupAndLoadInitialScene` / `CleanupInitialScene` 이 `EditorSceneManager.OpenScene` 과
`GetSceneManagerSetup`·`RestoreSceneManagerSetup` 으로 *에디터에 열린 씬* 을 바꿨다 되돌릴 뿐이다.
`playModeStartScene` 은 그보다 뒤, *Play 가 실제로 시작하는 씬* 을 덮는다. 그래서 설정돼 있으면 이기고,
없으면 MPPM 이 이긴다.

참고: 활성 시나리오는 `UserSettings/PlayModeUserSettings.asset` 의 `m_LastActiveConfiguration`
에 들어간다(git 미추적). `Assets/Settings/PlayMode/DevBoot.asset`(InitialScene=Dev_Boot 인 옛
시나리오)은 2026-09-22 은희가 삭제했다 — 툴바가 그 역할을 대신한다.

**🔴 동시 수정 주의 — 이번 세션이 건드리는 파일**

| 파일 | 상태 |
|---|---|
| `Assets/1.Scripts/Dev/DevBootTarget.cs` | 신규(런타임) — EditorPrefs 키의 유일한 원본 |
| `Assets/1.Scripts/Dev/Editor/DevBootLauncher.cs` | 신규 — 목록 보정·원복·Play 진입 |
| `Assets/1.Scripts/Dev/Editor/DevBootSceneCatalog.cs` | 신규 — 씬 스캔 + 최근 목록 |
| `Assets/1.Scripts/Dev/Editor/DevBootToolbar.cs` | 신규 — `[MainToolbarElement]` 드롭다운 |
| `Assets/1.Scripts/Dev/Editor/DevBootLauncherTests.cs` | 신규 — EditMode |
| `Assets/1.Scripts/Dev/DevSceneBooter.cs` | 수정 — `scene` 필드 제거(부팅 시퀀스는 무수정) |
| `Assets/1.Scripts/Dev/Editor/DevBuildSceneList.cs` | 수정 — 썩은 `DevScenes` 배열·활성/비활성 메뉴 삭제 |
| `ProjectSettings/EditorBuildSettings.asset` | 수정 — Dev_Boot 등록 제거 |

**팀 공지** — `Dev/빌드 씬 목록/테스트 씬 활성화·비활성화` 메뉴는 **없어진다.** 툴바 `Dev Boot ▾`
가 대신하고, 빌드 목록은 Play 종료 시 자동 원복된다. 커밋 전
`git diff ProjectSettings/EditorBuildSettings.asset` 이 비어 있는지 확인할 것
(에디터 크래시로 원복이 안 돌면 `Dev/Dev Boot/빌드 목록 강제 정리`).

## ▶▶ 현재 인수인계 (2026-09-22 · 구역 진입 기반 벽 투명화 1단계 **검증 완료**, 브랜치 `feature/TransparentV2-keepgoing`)

작업자: **은희(Claude + Codex 위임)**. 계획·근거는 [PLAN.md](PLAN.md) 최상단.
배선 절차는 [Docs/tech/wall-transparency-shadergraph-setup.md](Docs/tech/wall-transparency-shadergraph-setup.md).

**상태: 코드·셰이더 완료, 사용자 Play 검증 완료.** 커밋 16개, **push 안 함.**

| 파일 | VCS |
|---|---|
| `Assets/1.Scripts/Rendering/WallTransparencyZone.cs` (감지, 신규) | git |
| `Assets/1.Scripts/Rendering/Occlusion/WallTransparencyGroup.cs` (표현, 신규) | git |
| `Assets/3.Materials/Level1_Materials/Occlusion/WallTransparencyDither.hlsl` (신규) | git |
| `Assets/Tests/EditMode/Occlusion/WallTransparencyGroupTests.cs` (신규, 6개) | git |
| `Assets/50.Art/MapGen/MapObj/material/Generic_Standard.shadergraph` | 🔴 **SVN — 별도 커밋·공지 필요** |

**용어** — 여기서 "구역 투명화" 는 *구역에 플레이어가 있으면 그 구역이 지정한 벽 그룹이
높이 그라데이션으로 사라지는 것* 이다. **시선 차단 판정이 아니다.** 기존 `WallOcclusionDriver`
의 카메라-플레이어 선분 기반 픽셀 투명화(= "A 시스템", `4.MapScene` 에서 `m_Enabled: 0`)와 별개다.

**설계 요약**
- 감지: `Player(6)` **레이어만** 본다. `Player`·`Unit`·`NetworkObject` 를 참조하지 않는다 —
  테스트 씬에서 레이어만 바꾼 캡슐로 검증된다. 점유는 루트 Transform 단위.
- 표현: 그룹이 원본 머티리얼 종류마다 **런타임 인스턴스 1개**를 만들어 공유한다.
  MaterialPropertyBlock 은 쓰지 않는다 — SRP Batcher 가 깨진다.
- 벽/바닥 구분: **Material Variant 를 만들지 않는다.** 그룹이 인스턴스에만
  `EnableKeyword("WALL_OCCLUSION_DITHER")` 를 한다. 그래서 벽 프리팹의 머티리얼을
  교체할 일이 없다. 🔴 그래프의 키워드는 **Multi Compile** 이어야 한다(Shader Feature 면
  빌드에서 변종이 잘려 에디터에서만 동작한다).
- 높이 그라데이션: **아래가 사라지고 위가 남는다.** `baseY` 에서 알파 0 → `fadeHeight`
  만큼 위에서 1. 벽 한 층 = 2.5 이므로 기본 `fadeHeight = 5`(2층).

**2026-09-21 결정(은희)** — `PLAN.md` 의 2026-09-14 「투명화 끄고 실루엣으로」(경석)에 대해,
**실루엣은 그대로 두고 벽 투명화를 함께 간다.** 기존 투명화 시스템은 끄지도 지우지도 않는다.

**남은 것**
- 존 프리팹 오서링(구역 볼륨 + 그룹 리스트) — 파일럿부터
- `Generic_Basic.shadergraph`(펜스) 동일 배선
- MPPM 2인 확인 / 바닥·SSAO before-after 비교


## ▶▶ 이전 인수인계 (2026-09-21 #3 · 은희 · **이펙트 파사드는 지스타 이후로 연기** + 이벤트 이중 발화 수정)

작업자: **은희(Claude)**. 전체 리빌드 에러 0. **Play 검증까지 완료 — 이 건은 닫혔다.**

### ✅ development 머지 + origin 푸시 완료 (2026-09-21) — `10804346` → `1b85173b`

fast-forward(충돌 0). 작업 브랜치는 `fix/unit-clientdamaged-double-fire` 였다.

```
1b85173b  chore(addressables): link.xml 제거
52025c1d  docs: 이펙트 파사드는 지스타 이후로 연기 + 민경에게 넘길 제약 기록
320e85fe  fix(unit): ClientDamaged 가 HP 감소마다 2회 발화하던 것 수정
```

체크아웃 없이 `git push . HEAD:development` 로 올렸다 — 워킹트리가 뒤로 갔다 앞으로 오지 않아
**Unity 리임포트가 돌지 않았다.** (`Packages/manifest.json` 변경 없음을 먼저 확인했다.)

🔴 **`1b85173b` 주의 — `Assets/AddressableAssetsData/link.xml` 이 development 에서 빠졌다.**
Unity 가 에디터 리프레시 중에 지운 것을 그대로 확정했다. `8b1a1a70` 에서 **IL2CPP 링커 보존용으로
의도적으로 추가**했던 파일이고, Addressables/ResourceManager 의 프로바이더 4종
(`AssetBundleProvider` · `BundledAssetProvider` · `InstanceProvider` · `SceneProvider`)과
`UnityEngine.ComputeShader` 를 `preserve="all"` 로 묶고 있었다.
→ **IL2CPP 빌드 후 에셋 로딩이나 씬 전환이 실패하면 여기부터 의심할 것.**
`git show 8b1a1a70` 으로 원본을 복구할 수 있다.

### ✅ Play 검증 완료 (2026-09-21, 은희 MPPM 실측)

피격 시 플래시(`HitFlash`)가 **한 번만** 도는 것을 확인했다. 이중 발화 수정은 실기 검증까지 끝났다.

### 🔴 확정 — 이펙트 구조 개선은 **지스타(2026-11 중순) 이후**다

민경·은희 합의(2026-09-21): 출품 전까지 **이펙트 발동은 전부 하드코딩**으로 간다.
**이펙트 전용 Facade + Skill ID 테이블** 관리는 출품 이후 은희가 진행한다.

→ **11월 중순 전에는 이펙트 이벤트 표면 설계를 다시 꺼내지 말 것.** 이 결정을 모르면
다음 세션의 Claude/Codex 가 또 파사드를 제안한다(실제로 이번에 `feature/PacadeForEffect`
브랜치까지 팠다가 접었다 — 그 브랜치는 삭제됐다).

### 이번에 고친 것 — `Unit.ClientDamaged` 가 HP 감소마다 **2회** 발화하고 있었다

`Unit.OnHpReplicated` 가 `ClientHpChanged` 직후와 아래 블록, **두 곳에서** `ClientDamaged` 를
불렀다. 바로 위 주석이 `//충돌난거 임시 해결함 추후 수정 해야됨.` — 머지 충돌 봉합 자국이다.

지금까지 증상이 없던 이유: 유일한 구독자 `HitFlash` 는 플래시를 **재시작**할 뿐이라 두 번 불려도
똑같아 보였다. **민경이 여기에 이펙트를 물리는 순간 피격마다 두 번 터진다** → 그래서 11월을
기다리지 않고 지금 고쳤다. `OnShieldReplicated` 는 원래 1회라 손대지 않았다.

### 🔴 민경에게 넘긴 제약 — 하드코딩 전에 반드시 읽을 것

**① 회복 이펙트를 `Unit.HealHp` 자리에 하드코딩하면 호스트에서만 보인다.**
`HealHp` 는 `if (!IsServer) return;` 가드가 걸려 있다(`Unit.cs`). 이 레포가 반복해서 밟은 버그라
`EffectSocketPlayer`·`EffectStagePlayer`·`EffectPathPlayer` docstring 에 전부 경고가 박혀 있다.
→ 회복 연출은 **`ClientHpChanged` 를 구독해 `next > previous` 로 판별**해야 한다.

**② 힐/쉴드 RPC 3개는 죽은 코드다** — `HealHpRpc`·`IncreaseShieldRpc`·`SetShieldRpc` 는
`SendTo.Server`(클라→서버)이고 **호출부가 0개**다. 여기 훅을 걸면 아무 일도 안 일어난다.

**③ 쓸 수 있는 훅은 이게 전부다:**

| 이벤트 | 용도 | 비고 |
|---|---|---|
| `ClientDamaged` | 피격 연출 | 이번에 이중 발화 수정됨 |
| `ClientDamagedAmount` | 피해량별 연출 | HP/쉴드 채널 구분 |
| `ClientDamagedAttributed` | 내가 때린 것만 | 구독자 있을 때만 RPC 발송 |
| `ClientHpChanged` | **회복 포함** 전체 변화 | 회복은 이것으로만 가능 |
| `Died` | 사망 연출 | ⚠️ **서버 전용** — 그대로 쓰면 호스트만 보인다 |

**쉴드 획득·파괴는 이벤트가 아예 없다**(`FirstMeleeSubSkill` 의 보호막). 민경이 필요하다고 하면
11월 전에 하나 뚫어야 할 수도 있다.

### 11월 설계 때 이미 확정된 제약 — 다시 조사하지 말 것

**회복 원인(Skill ID)을 클라에 보내려면 새 ClientRpc 를 파는 수밖에 없다.**
`Unit` 의 RPC 9개 중 서버→클라는 `ClientDamagedAttributedClientRpc` **하나뿐**이고, 나머지 8개는
전부 `SendTo.Server` 다. NetworkVariable 복제는 **값만** 넘겨 원인이 경계에서 소실된다.
피격 쪽이 공격자 ID 를 넘기려고 별도 ClientRpc 를 판 것이 같은 이유이고,
구독자가 있을 때만 보내는 게이팅(`RequiresAttributedDamageRpc`) 선례도 거기 있다.

### ⚠️ 이번에 드러난 별건 — `.csproj` 가 낡아 `dotnet build` 가 그냥은 안 돈다

Auto Refresh 가 꺼져 있어 Unity 가 `.csproj` 를 재생성하지 않았다. **양방향으로 틀린다** —
사라진 `Wells&No.23/*.cs` 2개를 계속 참조해 `CS2001`, 새로 생긴
`Monster/Boss/IBossEntranceAnimation.cs` 가 빠져 `CS0246`. 내 변경과 무관한 노이즈다.
→ csproj 를 건드리지 말고 **임시 사본**을 만들어 빌드하고 지우는 식으로 우회했다.
Unity 창을 한 번 클릭하면 정리된다.

---

## ▶▶ 이전 인수인계 (2026-09-21 #2 · 입장 연출·차징 점프·돌진 사거리 — **전부 Play 검증 대기**)

작업자: **경석(Claude)**. 브랜치 `feature/Boss23`. 컴파일 통과(에러 0).

## 🔴 브랜치 주의 (2026-09-23) — development 의 `a8ebd7f2` 를 feature/Boss23 에 머지하지 말 것

- `455c2a0b`(Boss23 ← development 머지)를 development 로 FF 푸시한 뒤, **development 에만** `a8ebd7f2` 를 올려
  `1.TitleScene` 을 이전 평면 UI(`3810ed29` 판)로 되돌렸다 — 3D 오피스 타이틀은 메뉴가 모니터 뒤에 가려 **Start 불가**라서.
- Boss23 에 development 를 다시 머지하면 이 복원이 딸려 와 **타이틀 작업이 통째로 되돌아간다.** 받아야 할 게 생기면
  `a8ebd7f2` 를 제외하고 cherry-pick 하거나, 머지 후 `1.TitleScene` 을 Boss23 판으로 되돌릴 것.
- 타이틀 완성 후 Boss23 → development 머지 때 `1.TitleScene` 충돌 → **Boss23 판 채택** + `1.TitleScene/` 라이팅 폴더 복구.
- 타이틀 진행 상태(09-24): 계획서 `PLAN-title-flow.md` §0.8~0.9. **팀장 Play 확인 완료** — PRESS ANY KEY CRT 룩 · 모니터 지직거림 ·
  START/SETTING/EXIT(줌 없는 설정) · Start/Exit 전체 화면 꺼짐 · 벽 모니터 화면 반복 · 메인 모니터 로고 CRT.
  🟡 **미결: Start 후 로비가 너무 바로 뜬다 → 로비 진입 "켜짐" 연출을 넣을지 다음 세션에 결정**(§0.9). 남은 단계: 설정창 가독성.
- ⚠️ 09-23 밤 에디터를 켜 둔 채 Unity 내부 오디오 Assert(`Access version should be odd when acquiring lock`)가 무한 반복 →
  로그 24.6GB → OOM 크래시(09-24 11:26). 우리 코드 무관. 다음 실행 때 "Recovering Scene Backups" 는 **No**(백업은 Play 진입 시 BootStrap 자동 백업).

## ▶▶ 현재 인수인계 (2026-09-23 #2 · 맵 룩 복구·미니맵 315°·**데칼 벽 타기 수정** — Play 검증 완료)

작업자: **경석(Claude + Codex 교차검증)**. 브랜치 `feature/Boss23`. 컴파일 에러 0.
아래 셋은 팀장이 Play 로 확인했다(데칼·F9·디밍). 미니맵 315° 는 방향 대조가 남았다.

| 건 | 내용 |
|---|---|
| **맵 외곽 어둡게(디밍·LoS)** | `4.MapScene` FogManager `dimEnabled/losEnabled` 0→1. 🔴 09-16 머지 `c4dbd4b9` 가 development 의 1/1 을 **0/0 으로 되돌렸던 것**이 원인 — "은희 PC 와 비주얼이 다르다"의 정체 |
| **F9(LookToggle) 무반응** | 붙어 있던 `MaskBlurController` 오브젝트가 `c44d235c`(09-18) 에서 **비활성화**돼 한 번도 안 돌았다. 컴포넌트를 FogManager 오브젝트로 옮기고 `startLook: 1`(B). ApplyDim 이 `fogEnabled=false` 를 강제하던 줄 제거 |
| **미니맵 각도** | 315°(= 카메라 요각 −45°). 코드 기본값·씬 값 둘 다. 135° 는 180° 뒤집혀 있었다 |
| **F6 장판 소환** | `DevTelegraphProbe.cs` 삭제 |
| **데칼이 벽을 타고 올라감** | 원인 3겹 — 아래 |

**데칼 원인 3겹** (다음 사람이 한 겹만 고치고 "안 된다"고 하지 않게)
1. URP 기본 `Decal.shadergraph` 는 **`angleFade: false`** — 프로젝터 각도값을 통째로 무시한다.
   → `Assets/3.Materials/SG_DecalFloorOnly.shadergraph`(복사본, angleFade on) 로 `MA_AoeDecal_Red`·`MA_BossMarkerDecal` 교체. `SG_ColoredDecal` 도 on.
2. **Angle Fade 값은 "도"가 아니다.** 설정값 = `180·((1−cosθ)/2)²`, 수직벽 = **45**. end ≥ 45 면 벽이 항상 50% 남는다.
   → `DecalReceivers.FloorAngleFadeStart/End = 1.47/8.18`(실제 35°→55°). 프리팹 2개(Aoe·FireFloor) 동일.
3. 벽 메시의 **위를 향한 면**(루버 판자·기둥 밑동)은 각도로 못 거른다.
   → `DecalReceivers.Tag` 가 벽 형태 렌더러(높이 > 1.2m 이고 높이 > 수평 짧은 변)를 **수신자에서 뺀다**. `Unit`(송전탑) 은 예외로 남김.

**남은 것**
- 🔴 **HUD 스킬 슬롯이 청록 판으로 보임** — SVN **r326(은희)** 이 HUD 텍스처 `.meta` 5개의 **guid 를 새로 만들었다**
  (`slot_cooldown*`, `gauge_HP*`, `portrail_gunner`). `CombatHUD.prefab` 은 옛 guid 를 가리켜 스프라이트가 끊겼다. 은희에게 전달 예정 —
  **guid 를 옛 값으로 되돌리는 쪽 권장**(Unity 닫고).
- 플레이어 `AimIndicator`·`SkillRangeIndicator` 데칼도 같은 증상 가능(셰이더 angleFade off, 180/180). 이번엔 보스만.
- `fix/art_zone260923`(원격, 존 깊이 수정) 이 development 에 미머지.
- 다음 작업: **아트 씬 오브젝트를 `1.TitleScene` 으로 이식**.

## ▶▶ 이전 인수인계 (2026-09-23 · prep 클립·미니맵·타이틀 — **다음 세션에 4건 처리**)

작업자: **경석(Claude)**. 브랜치 `feature/Boss23`(development 대비 **5 ahead / 0 behind**).
컴파일 에러 0. **Play 검증은 전부 남았다.**

### 🔴 다음 세션은 여기부터 — 팀장 지시 4건

1. **미니맵 각도 재조정** — 135° 도 틀렸다(팀장: "내가 각도 잘못 알려줘서 다시 맞춰야 함").
   `MinimapController.MapRotationDegrees` 는 **public 필드라 인스펙터에서 바로 돌려볼 수 있다.**
   🔴 값을 확정하면 **코드 기본값과 `4.MapScene.unity:2682` 둘 다** 고쳐야 한다 —
   씬에 직렬화된 값이 이기므로 코드만 고치면 아무 일도 안 일어난다(이번에 그 함정을 밟을 뻔했다).
2. **development 비주얼 변경분 반영** — ⚠️ **확인 필요.** 팀장은 "development 최신 커밋이
   내 비주얼과 매우 달라졌다"고 했는데, 2026-09-23 03:20 기준 **development 에 새 커밋이 0개**다
   (내가 5 ahead / 0 behind). 09-23 에 머지한 32커밋(벽 투명화·팔라딘 애니)이 이미 그 변경분일
   가능성이 높다. 아니라면 **누군가 푸시를 안 했다는 뜻**이므로 그것부터 확인할 것.
3. **3D 오피스 아트를 `1.TitleScene` 으로 이식** — 지금 씬에는 **아트가 하나도 없다**
   (루트 9개가 전부 UI·vcam·FX). 화면의 어두운 사각형은 3D 모니터가 아니라 `Option_Panel` UI 다.
   아트가 들어오면 **`CRT_Anchor` 를 모니터 화면에 맞추면** 캔버스 2개가 따라간다.
4. **prep 클립 Play 검증** — 아래 완료기준 참조. 🔴 **데미지 타이밍 불변이 1순위다.**

### 이번에 넣은 것 (2026-09-23)

| 커밋 | 내용 |
|---|---|
| `c1a3a4b2` | **예고 구간을 prep 클립으로** — 훅L·훅R·어퍼·돌진. 잡기는 현행(얼리기) 유지 |
| `a40aeeb0` | 미니맵 45° → 135° (⚠️ **각도는 다시 잡아야 함**) |
| `be6e69ee` | 타이틀 CRT 캔버스가 뒤를 보던 것 — `CRT_Anchor` Y 180° |
| `e11776ac` | 타이틀 연출 + CRT FX 툴킷 **진행 중 보존**(계획서 2종 승인 대기) |
| `868f300c` | 보스 취약 상태 계획서(그릴 16문항 확정, **승인 대기**) |

### 🔴 prep 클립 — Play 에서 볼 것

1. **데미지가 그대로 들어가는가**(최우선). 재개 지점을 `telegraphPoseNormalized` 로 유지했지만 실측 필요.
2. 훅L·훅R·어퍼 예고에 **준비동작이 움직여 보이고**, 0.5초 뒤 멈췄다가 0.7초에 공격이 나가는가.
3. **돌진이 정상 발동하는가** — prep 을 틀면 `TickHitEventFallback` 의 `IsName(DashAttack)` 검사가
   막혀 **돌진이 아예 안 나갈** 수 있었다. 준비 신호를 즉시 세워 막았는데 실측이 필요하다.
4. 잡기는 기존과 동일한가(얼린 자세 — grab_prep 클립이 아직 없다).

### ⚠️ 알아 둘 것

- **`unity_set_transform` 은 위험하다.** rotation 만 넘겼는데 **position 을 (0,0,0) 으로 덮어썼다**
  (CRT_Anchor 가 원점으로 날아갔다 — 확인 안 했으면 그대로 저장될 뻔했다).
  → **position·rotation·scale 세 개를 항상 같이 넘길 것.**
- **타이틀 씬은 팀에서 아무도 안 만진다.** 원격 12개 브랜치 전부 최신 커밋이 `c0d4457d`(8/7)이고
  그 위는 내 작업뿐이다. **development 껄 가져오면 CRT/카메라 1271줄이 날아간다.**
- `No23.asset`/`No23_Solo.asset` 의 **BOM 을 제거**했다. `403d65c3` 에서 내가 붙인 것이고
  다른 몬스터 SO 는 전부 BOM 이 없다. Unity 는 읽지만 도구가 `%YAML` 헤더를 못 찾는다.
- **SVN r326** 최신화 완료. 보스 FBX 교체에도 **애니 이벤트 6개 전부 생존**(교훈 #109 재발 없음).

### 미착수 (계획만 있음)

- [PLAN-boss-vulnerable.md](PLAN-boss-vulnerable.md) — 취약 상태. **승인 대기**
- [PLAN-title-flow.md](PLAN-title-flow.md) · [PLAN-crt-fx.md](PLAN-crt-fx.md) — **승인 대기**
- **`.md` 전수조사·정리 계획** — 팀장 요청, 아직 안 씀. 밑작업 숫자만:
  레포에 `.md` **335개**(절반 가까이가 `.claude/worktrees/` 의 죽은 복사본) ·
  `CONTEXT.md` **4718줄** · `PLAN.md` 3817줄. CLAUDE.md 는 CONTEXT.md 를 *"concise, not a full spec"*
  으로 규정하는데 실제로는 세션 인수인계가 전부 쌓여 있다.

---

### ✅ development 머지 완료 (2026-09-21) + 🔴 **SVN 쪽에 따로 들어간 정리 1건**

`feature/Boss23` → `development` **fast-forward**(충돌 0). 커밋 13개 / 44파일 / `+3018 −366`.
로컬 체크아웃 없이 `git push origin feature/Boss23:development` 로 올렸다 —
체크아웃하면 워킹트리가 뒤로 갔다 앞으로 오며 **Unity 리임포트가 두 번** 돈다.
(`Packages/manifest.json` 변경이 없는 것을 먼저 확인했다. 있으면 에디터를 닫아야 한다.)

🔴 **git 에 없는 변경이 하나 있다 — SVN r319.**
`MapPrefabCatalog.asset`(유령 키 20개) · `MapGenConfig.asset`(3개)에서 **대응 필드가 사라진
저작값 23개를 제거**했다. 이 둘은 `Assets/50.Art/` 밑이라 **`.gitignore:84` 로 git 제외 · SVN 소유**다
→ **development 머지에는 안 실려 있다. SVN 최신화(r319+)를 받아야 반영된다.**

- 왜 지웠나 — 필드를 주석 처리(`5e858c5b`)했는데 에셋에는 값이 남아 있었다. Unity 는 대응 필드가
  없는 키를 **로드 때 무시하고 다음 저장 때 조용히 버린다** → 누가 인스펙터를 건드리는 순간
  예고 없이 사라지는 상태였다. 의도적으로 지금 지웠다.
- 기능 영향 없음 — 소비처(`MapCatalogPopulator` / `MapGeometryBuilder`)는 development 에도 없다.
  원격 브랜치 12개 중 남은 곳은 `feature/Level1` 뿐인데 development 보다 **957 커밋 뒤처진** 브랜치다.
- 남긴 값 — `BossIcon`/`SpawnIcon`/`QuestIcon` · `MonsterGroups` 8건. Unity 로 되읽어 `unknownKeys` 0 확인.

⚠️ **함정(한 번 밟았다)** — 에셋을 `utf-8-sig` 로 쓰면 **BOM 이 붙어 `%YAML` 헤더가 깨진다.**
Unity 가 "not text-serialized YAML" 로 거부해서 발견했다. `.asset`·`.prefab`·`.unity` 는
**BOM 없이** 쓸 것. 쓰기 전후로 첫 5바이트가 `%YAML` 인지 확인하면 걸린다.

✅ **welz 머티리얼 12건**(`Char/Boss/SK/welz_*.mat`)이 SVN 미추가(`?`)로 보이지만
**로컬 잔재라 무시한다**(팀장 확정 2026-09-21). 보스 모델 수정분은 이미 development 에 들어가 있다.
→ 다음 세션에서 다시 꺼내지 말 것.

### 🟡 은희에게 넘김 — **결과 화면이 호스트에서만 채워진다** (경석 진단 완료 · 팀장 확정 "지금은 둔다")

**증상**(2026-09-21 MPPM 실측) — 클리어 후 ResultScene 에서 호스트만
`CLEAR / 생존 시간 04:35 / 처치 11` 이 나오고, **원격 클라는 `-` / `--:--` / `-`** 다.

🔴 **고장이 아니라 미구현이다.** `SessionResult.cs` 의 docstring 이 이미 적어 두고 있다 —
*"정적 보관으로 둔다(리슨 서버 로컬 표시 기준). 원격 클라이언트에도 같은 값을 보여야 하면
**서버 브로드캐스트를 얹어야 한다 — 지금은 미구현**."* 클라가 보는 값은 `ResultStatsView` 의
`!SessionResult.HasValue` 분기 그대로다(값이 **도달한 적이 없다**).

```
SessionStatsTracker (MonoBehaviour · 서버만 집계)
  → SessionResult.Capture()      ← static. 호스트 프로세스 안에만 존재한다
  → ResultStatsView 가 그 static 을 읽는다
```

- 집계 진입점 2곳: `BossEncounterDirector:688`(클리어) · `PartyWipeWatcher:66`(전멸)
- **`SessionStatsTracker` 도 `PartyWipeWatcher` 도 `MonoBehaviour` 다** — 복제 수단이 없다.

🔴 **손댈 때 걸릴 함정 — 브로드캐스트와 씬 전환이 경쟁한다.**
`Capture` 직후 ResultScene 으로 넘어가므로, 단순히 ClientRpc 를 쏘면 **씬 언로드가 더 빨라
클라가 여전히 빈 값을 본다.** 값을 씬 전환 뒤까지 살아남는 쪽에 실어야 한다 —
`DontDestroyOnLoad` 네트워크 싱글톤의 `NetworkVariable`, 또는 **ResultScene 자체를 네트워크 씬**으로.
어느 쪽이든 SceneManagement 설계를 건드리므로 **은희 영역**이다(AGENTS.md §5).

### 🔴 다음은 MPPM 2~3인 **한 판으로 몰아서** — 검증 목록이 아래 하나로 합쳐졌다

계획서 [PLAN-boss-entrance-charge.md](PLAN-boss-entrance-charge.md) 의 완료 기준을 그대로 따라간다.

0. ✅ **잡기 = 1명만 Carry / 나머지 넉백 — 2026-09-21 실측 통과**(B0 검증 2번).
   같은 판에서 **인터럽트가 안 되는 별개 버그**가 드러나 고쳤다(`29c4389b`, 아래) → **재검증 필요**:
   ① `Hold`·`Throw` 에서 인터럽트가 통하는가 ② **3타째에는 안 통하는가** ③ 성공 시 잡힌 사람이 풀려나는가
1. **잡기 나머지 2건** — [PLAN-boss-backlog.md](PLAN-boss-backlog.md) **B0**
2. **입장 연출** — 하강 중 체공 포즈 / 착지 클립 / **데미지 0** /
   🔴 **MPPM 클라(호스트 아님) 화면에서도 보이는가**(스폰과 같은 프레임 RPC라 실측 안 됨) /
   🔴 **전투 시작 후 착지 포즈가 안 남는가**(보스를 Idle 에 머물게 해서 확인)
3. **차징 점프** — 올라갔다 사라지고 `BossLandingPoint` 에 떨어지는가 / 착지 직후 차징 정상 시작 /
   체공 중 무적·착지 후 피격 / 차징 착지 데미지 0 / 끊었을 때 투명·무적 잔존 없음 / 배속 잔존 없음
4. **돌진** — 실사거리 13.65m → **29.25m** 로 2.1배. 🔴 **체감 과하면 `dashDuration` 부터 내린다**

### 이번에 넣은 것 (2026-09-21 #2)

| | |
|---|---|
| **입장 연출 애니** | 하강 `JumpHover` / 착지 `JumpLanding` / 전투 직전 로코모션 복귀. 새 RPC 없이 점프어택 경로 재사용. seam = `IBossEntranceAnimation`(신규) |
| **차징 진입 = 점프** | 걸어가던 `ChargeMove` 구간 **제거**. 기존 `JumpTakeoff`/`Leap`/`Land` 재사용 + `_chargeJump` 플래그로 종료 분기만 가름 |
| **돌진 사거리** | `dashDuration` 0.91→**1.5** · `dashSpeedMultiplier` 6→**7.8** · `dashMaxDistance` 16→**30** |
| **개명** | `jumpSearchRadius` → **`playerScanRadius`** (점프 전용이 아니었다 — 차징 송전탑 인원 계산도 같은 값을 쓴다) |
| **SO 정리** | `chargeMoveArriveDistance`/`chargeMoveSpeedMultiplier`/`chargeMoveTimeout` 3종 제거(읽는 코드가 사라짐) |
| **잡기 인터럽트** | 🔴 **실측으로 잡은 버그**(`29c4389b`) — `PerformAttackHit` 이 공격 종류를 안 가리고 카운터 창을 닫아, **잡기 클립 자신의 `OnAttackHit`(정규화 0.354)** 이 창을 0.25초 만에 꺼 버렸다. `StartAttack` 에는 같은 Grab 예외가 이미 있었는데 여기만 빠져 있었다 — G6 에서 창을 옮길 때 **여닫는 지점 한쪽만** 고친 것 |

### 🔴 이번에 드러난 것 — **PLAN §5 의 거리값 2건은 효과가 0 이었다**

`No23.asset` 값을 올려도 **아무 일도 안 일어나는** 상태였다.

- 돌진 `maxDistance` — DashAttack 은 `ignoreDistanceWindow: 1` 이라 거리창을 **아예 안 읽는다**.
- `dashMaxDistance` — 실사거리가 `min(값, 지속시간×속도)` 인데 **지속시간이 먼저 물렸다**(13.65m).

**코드 주석이 이미 그렇게 말하고 있었는데 PLAN 이 그걸 모르고 쓰였다.**
→ 교훈: **SO 값을 올리기 전에 그 값을 읽는 코드에 클램프가 있는지 먼저 본다.**

### ⚠️ 확인했지만 안 고친 것

1. **`playerScanRadius` 가 두 용도 공용이다** — 점프 타겟 탐색 + **차징 송전탑 인원 계산**.
   30 → 45 로 올렸을 때 송전탑 개수 판정 반경도 같이 올라갔다. **의도였는지 기록이 없다.**
   지금은 Tooltip·주석으로 명시만 했다. 송전탑 개수가 인원과 안 맞으면 여기를 의심할 것.
2. **`chargeZonePrefab` 이 비어 있다**(`{fileID: 0}`) → 차징 **전기 장판은 지금 데이터로 안 나온다.**
   검증할 때 없는 걸 찾지 말 것.
3. ~~돌진(1.5초)이 `dashStunDuration`(1초)보다 길어져 끌려가는 도중 스턴이 풀린다~~
   → ⚠️ **정정(2026-09-21): 그런 일은 없다. 내가 틀렸다.**
   `dashStunDuration` 은 캐리를 붙잡는 값이 **아니다** — `ReleaseDashCarry` 에서 구속을 푼
   **다음 줄**에, 그것도 **벽에 처박았을 때만** 거는 사후 기절이다(Tooltip 도 "벽 충돌 시"라고 적혀 있다).
   캐리는 `PlayerActionState.Restrained` 이고 **타이머가 없다** — 보스가 `EndRestrained()` 를
   부를 때까지 유지된다. 즉 캐리(돌진 내내) → 해제 → 스턴 시작 순서라 **둘이 겹치지 않는다.**
   `dashDuration` 을 늘려도 스턴에는 영향이 없다.
4. `attacks[].damage` 8개는 전부 0 이지만 **정상이다** — `attackDamage: 10` 폴백. 건드리지 말 것.

---

## ▶▶ 이전 인수인계 (2026-09-21 #1 · 미니맵·보스타이머 구현 완료, 다음은 **잡기 검증**)

작업자: **경석(Claude)**. 브랜치 `feature/Boss23`.

### 🔴 다음 세션은 여기부터 — MPPM 2~3인으로 **검증 3건**

[PLAN-boss-backlog.md](PLAN-boss-backlog.md) 의 **B0** 를 열면 그대로 따라 할 수 있다.
코드는 다 들어갔고 컴파일도 통과했다. **Play 확인만 남았다.**

1. 잡기 — 예고가 끝난 뒤 다가간다 → **안 잡혀야** 정상
2. 잡기 — 예고 안에 3명 → **1명 Carry / 2명 넉백**
3. 잡기 — 끌려간 뒤 붙잡히기 전에 죽는다 → **유령이 안 잡혀야** 정상

이어서 미니맵·타이머 쪽도 아직 실측이 남았다(아래 "이번에 넣은 것" 참조).

### 이번에 넣은 것 (2026-09-19 ~ 21)

| | |
|---|---|
| **보스 제한시간** | `BossTimerManager`(신규, 서버 권한 5분) + 만료 시 강제 개시. HUD 게이지 동작 확인됨 |
| **미니맵 룩** | 플랫 회색 + 외곽선 + 45° 회전 + 둥근 코너. 미탐사=어두운 채움(팀장 실측으로 1회 뒤집음) |
| **HUD 슬롯** | `CombatHUD.prefab` 에 저작 스크립트로 생성(멱등). 순수 추가 329줄, `.meta` guid 불변 |
| **호스트 ACK 버그** | 기존 버그 — MPPM 2인 실측으로 확정 후 수정(T16) |
| **SO 전수조사** | 유령 필드 54 → **현역 0**. 레거시 트리 `_Legacy` 로 격리 |
| **잡기 예고/판정** | 재탐색 제거 + 유령 방지 — **위 검증 3건 대기** |

🔴 **커밋 전 확인** — `Assets/AddressableAssetsData/link.xml` 이 또 삭제돼 있다(`git status` 의 `D`).
Addressables 재빌드가 지우는 것이고 **은희 영역**이다. 내 작업과 무관하니 복구하고 커밋할 것.

---

## ▶▶ 이전 인수인계 (2026-09-19 · 동기화·빌드 정상화 완료, 다음은 **미니맵**)

작업자: **경석(Claude)**. 브랜치 `feature/Boss23` — 원격과 동기(`e610df18`).
보스 작업은 **이미 development 에도 들어가 있다**(`8635dd40` 가 `feature/Boss23` 를 머지).

### 다음 세션은 여기부터 — **미니맵**

[PLAN-minimap.md](PLAN-minimap.md) 를 열면 바로 착수할 수 있다. 보류 사유였던
"팀원의 `CombatHUD.prefab` 미푸시"는 **해소됐다**(`c44d235c`, `c603e699` → development → 내 브랜치).

🔴 **단, 들어온 건 키가이드뿐이다** (2026-09-19 실측):

| 항목 | 프리팹 내 참조 |
|---|---|
| `keyguide.png` | **1건** — 오브젝트 이름이 그냥 **`Image`** (이름 안 바꿈) |
| `minimap` 문자열 | **0건** — 슬롯도 스크립트도 없다 |
| 타이머 아트 3종 | **0건** |

→ 계획의 **D5·D6(프리팹을 건드리지 않고 슬롯을 이름으로 찾는다)은 폐기**했다.
이제는 `CombatHUD.prefab` 에 슬롯을 **직접 만드는 게 맞다** — 충돌 위험이 사라졌기 때문이다.
나머지 결정(D1~D4·D7·D8)과 슬라이스 S1~S5 는 그대로 유효하다.

핵심 요약 — **새 렌더러를 만들 필요가 없다.** 맵 모양(`_SilTex`)은 `BuildSilhouette()` 가
이미 정확히 생성하고 있고, 탐사 3단계도 `MinimapUI.shader` 에 구현돼 있다.
룹이 다른 이유는 채움이 **지형 사진(`_MainTex`)** 이고 외곽선이 없고 코너가 직각이기 때문이다.

### 이번에 마무리한 것

| | |
|---|---|
| SVN | **r316** — 23호 클립 저작 복구 + 최신화. 충돌 5건은 서버(은희 `r315`)본으로 |
| CombatHUD 흰색 | **해결** — `.meta` 가 없어 스프라이트 참조 7건이 끊겨 있었다 |
| git | `feature/Boss23` == 원격. development 흡수 완료 |
| 돌진 캐리 | `dashCarryFrontOffset 1.8 → 2.2` (보스 캐슐 안으로 0.11m 파고들던 겹침 제거) |

### 🔴 닫히지 않은 것

1. **맵 가장자에 끼면 못 움직임** — **미해결. 플레이어 측(은희)으로 넘김**(팀장이 전달 완료).
   지형은 정상으로 확인됐다 — `bossroom.prefab` 의 바닥(30×1×30, 윗면 y=0.5)이
   네 방향 벽 밑으로 **0.51m 더 들어가 있고** 벽 아랫면도 y=0.5 로 딱 맞물린다. 툁·틈 없음.
   의심 지점은 `Paladin.prefab` 의 **`MainSkill` 콜라이더** — `enabled=1` · `isTrigger=0` ·
   `2×1×2` · 몸 앞 0.8m. 몸통 캡슐이 `r=0.38` 인데 **폭이 5배**다.
   끄는 코드는 없고, `Player(6)` 레이어는 `Ground·Wall·Env` 와 충돌한다.
2. **점프 연속 사용 시 애니 배속 잔존** — 미검증(G4 완료기준 4번).
   깨는 가장 빠른 길은 **이륙 중에 그로기·카운터로 끊는 것**(`AbortAttackChain` 경로).
3. 🔴 **`VisualSVN Server license expired`** — r316 커밋은 통과했지만 곷 막힐 수 있다. 관리자 통보 필요.
   → **2026-09-21 재확인: 여전히 만료 상태이고, 여전히 커밋은 된다**(r319 통과).
   경고가 **커밋 *뒤*에** 뜨는 형태라 실패로 오인하기 쉽다 — `Committed revision N` 이 찍혔으면 들어간 것이다.
   상태는 그대로이므로 **관리자 통보는 아직 유효한 할 일**이다.
4. `gauge_HP_noncolor` 만 `textureType: 0` — 마스크 원본이면 의도. 은희에게 확인.
5. `Assets/Resources/PerformanceTestRun*.json` 4개가 untracked — `Resources/` 라 **빌드에 들어간다.** `.gitignore` 검토.
6. `Assets/AddressableAssetsData/link.xml` 이 한 번 삭제된 적 있다(복구함).
   다시 뜨면 Addressables 재빌드가 지우는 것 — 은희 영역.

## ▶▶ 작업 세션 (2026-09-21 · **타이틀 연출 + CRT FX 툴킷** — 계획 승인 대기)

작업자: **경석(Claude)**. 브랜치 `feature/Boss23`. Codex 는 아래 파일을 건드리지 말 것.

**계획서 2종 — 승인 후 구현 시작**: [PLAN-title-flow.md](PLAN-title-flow.md) ·
[PLAN-crt-fx.md](PLAN-crt-fx.md)(신규). 후자가 전자의 선행 의존이다.

목표 = 타이틀을 평면 UI 에서 **3D 오피스 씬 + 중앙 CRT 안의 메뉴**로 전환.
`PRESS ANY KEY` → 카메라 인(Cinemachine vcam 3대) → 모니터 안 Start/Setting/Exit.

**수정 예정 파일**

| 파일 | 내용 |
|---|---|
| `0.Scenes/MainFlow/1.TitleScene.unity` | 아트 이식 · vcam 3대 · Canvas 3층 · 영구 콜백 재지정 |
| `1.Scripts/UI/Title/TitleFlowDirector.cs` (신규) | 상태 머신 · 입력 · 선택 복구 · 패드 Cancel |
| `1.Scripts/UI/Title/BlinkingText.cs` · `TextScramble.cs` (신규) | 깜빡임 · 스크램블 |
| `1.Scripts/Managers/TitleSceneManager.cs` | ESC 처리 제거/위임 + 상태 가드 |
| `1.Scripts/Rendering/RetroCRT/*` | 🔴 **파라미터 런타임 전달 경로** + `CrtFxDriver.cs`(신규) |
| `0.Scenes/Art/title/GlobalVolumeProfile.asset` | ChromaticAberration 추가 |
| `0.Scenes/Debug/CrtFxScene.unity` (신규) | FX 데모 |
| `0.Scenes/Art/title.unity` | 🔴 **백업 보존. 손대지 않는다** |

🔴 **공유 자산 주의** — `99.Settings/PC_Renderer.asset` 과 `CyaniluxRetroCRT.mat` 은 **맵 씬과 공유**한다.
기본값은 건드리지 않고 런타임 오버라이드로만 흔든다. 보스전 룩 회귀를 검증에 포함했다.

🔴 **Codex 교차검증으로 1판에서 5건이 뒤집혔다** — 목록은 [PLAN-title-flow.md](PLAN-title-flow.md) §9.
그중 미해결 최대 리스크는 **CRT warp(0.035)와 UI 클릭 좌표 불일치**다(실측 대기).

## ▶▶ 작업 세션 (2026-09-19 · 미니맵 룩 + **보스 제한시간 타이머** — 계획 승인 대기)

작업자: **경석(Claude)**. 브랜치 `feature/Boss23`. Codex 는 아래 파일을 건드리지 말 것.

**계획서 2종 — 승인 후 구현 시작**: [PLAN-minimap.md](PLAN-minimap.md)(D9~D12 추가) ·
[PLAN-boss-timer.md](PLAN-boss-timer.md)(신규). 🔴 PLAN-minimap §7 의 "보스 타이머 = 범위 밖" 은 폐기.

**수정 예정 파일**

| 파일 | 내용 |
|---|---|
| `Rendering/Minimap/MinimapUI.shader` | 플랫 채움 + 외곽선, 배경 알파 0 |
| `Map/Minimap/MinimapController.cs` | 둥근 코너 · 베이크 건너뛰기 · 45° 회전 · 슬롯 부착 |
| `Map/BossTimerManager.cs` (신규) | 서버 권한 제한시간(기본 300초) |
| `Map/BossTeleportManager.cs` | `ForceStartEncounter()` + 이동 대상 판정 변경 + **호스트 ACK 순서 수정** |
| `Map/BossEncounterDirector.cs` | 참가자 판정 `Alive` → `!= PermanentDead` (한 줄) |
| `Player/Fall/PlayerFallRecovery.cs` | 강제 이동 시 지연 복귀 취소 |
| `UI/Combat/BossTimerHUD.cs` (신규) | 게이지 표시 전용 |
| `UI/Editor/CombatHudSlotAuthoring.cs` (신규) | 프리팹 슬롯 저작 메뉴 |
| `2.Prefabs/UI/CombatHUD.prefab` | 슬롯 2개 추가 **(이것 외 변경 금지)** |

🔴 **그릴에서 확정된 것 중 기존 동작을 바꾸는 것 1건** — 보스룸 이동 대상이
"생존자만"에서 **"`PermanentDead` 가 아닌 전원(Soul 포함)"** 으로 바뀐다. 타이머 강제 이동뿐 아니라
**평소 패드 진입 경로도 같이 바뀐다**(같은 코드를 쓴다). 팀장 지시: 목숨 남은 사망자를 두고 가면 이동이 꼬인다.

### ✅ SO 전수조사 + 정리 (2026-09-21) — "뭘 만져야 바뀌는지" 가 안 보이던 원인

**증상** — 팀장: "SO 가 너무 많아서 어떤 걸 조절해야 수정이 되는지 명확하게 안 보인다."
**원인** — 값은 저장돼 있는데 **코드가 읽지 않는 필드**가 섞여 있었다. 만져도 아무 일이 안 난다.

직렬화 필드 **392개**를 훑어 분류했다(스크립트는 세션 스크래치패드의 `so_audit3.py`):

| 판정 | 정리 전 | 정리 후 |
|---|---|---|
| live (밖에서 직접 읽힘) | 299 | 299 |
| via (같은 파일 프로퍼티 경유) | 15 | 15 |
| ⚠️ check (파일 안에서만 쓰임 — 사람 확인) | 24 | 24 |
| 🔴 **ghost (아무도 안 읽음)** | **54** | **25 (전부 `_Legacy` 안)** |

→ **현역 코드의 유령 0개.**

**한 것**
1. `Assets/9.ScriptableObject/Enemy/Boss/Wells&No.23/` → **`Assets/_Legacy/Wells&No.23/`**
   (민경이 작업하던 구 보스 데이터. 경석이 인수해 보스를 재작성하면서 남은 잔재 — 팀장 확인.
   `hookDamage`·`jumpDamage`·`grabCoolTime` 등 **보스 튜닝처럼 생긴 29필드가 전부 참조 0** 이었다.)
   🔴 Unity `AssetDatabase.MoveAsset` 으로 옮겼다 — 에디터를 켠 채 파일시스템으로 옮기면
   `CLAUDE.md §6` 의 EPERM 사고가 난다. **에디터가 직접 옮기게 하면 GUID·`.meta` 가 보존된다.**
2. 현역 SO 의 유령 **29개 주석 처리** — 주석마다 **마지막 저작값과 "실제로 만질 곳"** 을 남겼다
   (주석 처리하면 Unity 가 다음 직렬화에서 에셋 값을 버리므로).

**정본이 어디인지 — 헷갈리던 것들**

| 무엇 | 만져도 안 되던 곳 | 실제로 만질 곳 |
|---|---|---|
| 보스 데미지·쿨타임·넉백 | `_Legacy/Wells&No.23/*.asset` | **`2.Prefabs/Monster/Data/No23.asset`** (`BossDataSO`) |
| 폭탄 투척·착지 | `BossDataSO.bomb*` | **`BossBomb` 프리팹** (`[SerializeField]` 12개) |
| 폭발 장판 | `BossDataSO.fireZone*` (전부 0, "0=프리팹값" 오버라이드가 미구현) | **FireFloor 프리팹** |
| 점프 예고 진하기 | `BossDataSO.jumpTelegraph*Alpha` | **`FX_Drop_Charge_*` 파티클 프리팹** |
| 차징 밀어내기 | — | `BossDataSO.chargeAuraRadius`(3.5) · `chargeAuraKnockbackStrength`(5) ✅ **정상 동작** |

**중간보스 판정** — `MonsterDataSO.isMidBoss` 는 에셋 12개에 값이 있고 3개가 true 였지만
(Gauntlet·Spinner·WallBot) **코드가 한 번도 읽지 않는다.** 실제 구분은 **전용 클래스 +
`MonsterCounterWindow` 컴포넌트**가 한다. 기능 결손은 아니고 플래그만 죽어 있었다.

🔴 **아직 안 닫힌 것** — `MapPrefabCatalogSO` 는 `GetPool`/`PickVariantIndex`/`GetPrefab`
**세 공개 메서드도 밖에서 참조 0** 이다. 즉 이 SO 에서 살아 있는 건 미니맵/오버뷰 아이콘
`Texture2D` 3개뿐이다. 필드가 아니라 **API 를 들어내는 일**이라 이번엔 건드리지 않고 주석으로 표시만 했다.
맵 생성 재개 계획이 없으면 블록 전체가 삭제 후보다.

🔴 **내가 한 번 틀렸다** — `jumpTelegraphPrefab` 을 "지금은 아무도 읽지 않는다" 는 **코드 주석을
믿고** 잘랐다가 컴파일이 깨졌다. 실제로는 `Monster/Editor/BossDataWiring.cs:30` 이 쓴다(복구함).
**낡은 주석보다 실측이 우선이다** — 내 감사 데이터는 그 필드를 처음부터 `live` 로 잡고 있었다.

### ✅ Codex 3차 교차검증 (2026-09-21) — 구현 코드. 버그 7건 잡아 전부 수정

계약 8개 중 **6개 [지킴]**(T16·T8·T14·T15·D15·D9), 컴파일 오류 0. 잡힌 것과 수정:

| # | 문제 | 수정 |
|---|---|---|
| 1 | 🔴 **제때 도착해도 강제 이동이 또 걸린다** — 도착 콜백이 `_expired` 를 안 지워서 이미 도착한 플레이어를 다시 끌고 감 | 도착 확정 시 `_expired` 해제 |
| 2 | 🔴 **ACK 실패 후 제한시간 집행이 끝난다** — `ForceStartEncounter` 성공은 "경고 시작" 일 뿐인데 거기서 재시도 플래그를 소모 | `_expired` 는 **도착 확정에서만** 해제. 0.5초 간격 재시도 |
| 3 | 🔴 **원격 오너가 낙하 카메라·입력 잠금에 갇힌 채 끌려간다** — 서버의 코루틴 필드로 원격 연출 진행 여부를 판정하고 있었음(원격은 서버 사본에 항상 null) | 조기 반환 가드 제거, 취소 RPC 항상 전송(멱등) |
| 4 | 🔴 **타이머 HUD 가 스스로를 꺼서 영원히 안 돌아온다** — `root` 가 자기 GameObject 라 `SetActive(false)` 하면 `Update` 가 멈춤 | `CanvasGroup.alpha` 로 교체. 컴포넌트는 계속 살아 있음 |
| 5 | **대시 중 텔레포트하면 도착 지점부터 남은 대시를 이어 달린다** | 오너 쪽에서 `PlayerStateController.EndDash()` 호출 |
| 6 | **`[` `]` 크기 단축키가 슬롯 크기를 무시**(400 → 외접 566, 화면 밖) / 슬롯 해제 시 외접 크기 미복원 | 슬롯 부착 중 단축키 차단, 해제 시 `ApplyPanelSize` 재적용 |
| 7 | 반전 모드에서 `Stopped` 가 게이지를 가득 채움 / 대기 중 로그 폭주 / 미니맵 자원 미해제 / T1 시계 샘플 불일치 | `Stopped` 는 방향 무관 0, 로그 제거, `OnDestroy` 정리, **서버 시계 하나만 사용** |

🔴 **2번은 계획서(리스크 7)에 이미 적혀 있던 요구였는데 구현에서 빠졌다.** 계획에 적는 것과
구현이 지키는 것은 별개다 — 교차검증이 그 간극을 잡았다.

### ✅ MPPM 2인 실측 판정 (2026-09-20 17:48) — 호스트 ACK 버그 **확정**

계측을 넣고 2인으로 돌린 결과, 가설이 그대로 재현됐다(계측은 판정 후 제거함):

```
TeleportAlivePlayers 시작 — 대상 2명 [0,1]
CompleteArrival 호출 #1 — arrived=[0] awaiting=0      ← 루프 첫 바퀴 중에 이미 완료
HandleAlivePlayersArrived — 도착=[0] → 잠금명단=[0] (접속자 2명)
연출 잠금 적용 → clientId=0                            ← clientId=1 없음
CompleteArrival 호출 #2 — arrived=[0,1]               ← 올바른 명단이 뒤늦게 완성되지만
[BossEncounter] 이미 진행 중(Descending)이라 도착 신호를 무시합니다.   ← 버려짐
```

→ **원격 플레이어는 보스 등장 연출 중 잠기지 않았다.** T16 으로 수정함.

🔴 **왜 빌드 테스트로는 안 잡혔나** — `[BossTeleport]`·`[BossEncounter]` 로그는 전부 `Edit.Log`
(`[Conditional("UNITY_EDITOR")]`)라 **빌드에는 존재하지 않는다.** 9/18 3인 세션은 빌드였으므로
"정상으로 보였다"는 관찰에 **로그 근거가 애초에 없었다.** 앞으로 흐름 검증은 MPPM(에디터)로 한다.

**덤으로 확인된 것** — 같은 로그에 유니티가
`Setting linear velocity of a kinematic body is not supported.` 를 찍고 있었다.
Motor 구동 중 리지드바디는 kinematic 이라 텔레포트의 `rb.linearVelocity = 0` 이 **아무것도 지우지
않았다.** 그래서 보스 텔레포트를 `PlayerMotor.TeleportAuthoritative` 경유로 바꿨다(수직 속도·예약 이동 정리).

### 보스 잡기 인터럽트 — **버그 아님. 사양대로다** (2026-09-20 판정)

"지짐이·내려찍기 3회 동안 인터럽트가 안 된다"를 조사한 결과, 로그의 내장 진단이 답을 갖고 있었다:

```
[23호] 인터럽트가 카운터로 성립하지 않았다 — 서버=True · 창열림=False · 정면=True · 페이즈=Throw
```

창은 **붙잡기 성공 시점에 열리고 3번째 내려치기 시작에 닫힌다**(`AdvanceGrabSlam` 의
`_grabSlamsLeft <= 1`). 주석에 **"팀장 확정 C10 — 3번째 직전까지가 인터럽트 가능"** 으로 박혀 있다.
즉 지짐이 + 내려치기 1·2타(약 2.5초)는 가능하고 3타만 불가다. **팀장 재확인: 스펙 유지.**

🔴 **함정** — `No23.asset` 의 Grab 행에 `counterWindowDuration: 1.3` 이 저작돼 있지만
**Grab 에서는 이 값이 창 길이로 쓰이지 않는다**(`opensNow` 가 Grab 을 명시적으로 제외한다).
Dash 전용이다. 이 값을 키워도 잡기 인터럽트 구간은 1초도 안 늘어난다.

### Codex 교차검증 (2026-09-20) — 계획에 구멍 5개, 전부 계획에 흡수됨

`codex exec -s read-only` 로 두 계획서 + 관련 코드 12개를 검증시켰다. **주요 주장 3개는 코드로 재확인했다.**

1. 🔴 **원안 T7 이 제한시간을 우회시켰다** — 만료 직전 패드 진입 → 만료 시 타이머 종료 → 패드 이탈 →
   **보스 이동도 제한시간도 소멸.** → T7 개정 + T13(Pad/Forced 분리).
2. 🔴 **전원 Soul 도착 시 보스가 안 나온다** — `BossEncounterDirector.IsAliveParticipant` 가 `State == Alive`
   만 통과시켜 `_eligibleClientIds` 0 → Idle 복귀. **부활해도 Director 재진입 경로가 없어 교착.**
   → 팀장 확정: **만료 시 Soul 을 목숨 1개씩 써서 강제 부활시킨 뒤 이동**(T14) + 참가자 판정 확장(T15).
3. 🔴 **기존 버그 — 호스트 ACK 조기 확정.** `TeleportAlivePlayers` 가 `_awaitingArrival.Add` 직후 루프 안에서
   RPC 를 보내는데 NGO 는 **호스트 대상 ClientRpc 를 동기 실행**한다(`clientRpcMessage.Handle` 확인).
   호스트가 slot0 이면 **원격 등록 전에 `CompleteArrival()`** → `_eligibleClientIds` 가 호스트 1명.
   9/18 3인 세션이 "정상"으로 보인 이유는 **보스는 뜨고 전투도 굴러가기 때문**이다(잠금만 누락).
   → 팀장 확정: **이번에 같이 고친다**(T16).
4. `PlayerFallRecovery` 의 **지연 복귀 코루틴**이 보스룸 도착 후 옛 안전지점으로 되돌릴 수 있다 → T17.
5. 미니맵 — `_BgAlpha` 는 `.mat` 에 0.35 로 저장돼 **셰이더 기본값만 바꾸면 안 먹고**,
   D4 의 "미탐사=외곽선만" 은 **현재 동작이 아니라 새로 만들어야 하는 것**이다 → D11·D13~D15.

**아직 안 닫힌 것** — Soul 을 텔레포트한 뒤 Motor 내부 속도 때문에 **옛 좌표로 되돌아가는지**는
정적 분석으로 판정 불가. **MPPM 실측 항목**이다(패드 경로의 Soul 동반 이동에만 남는 리스크).

🔴 **알려진 대가** — `CombatHUD` 가 `Player.prefab` 자식이고 `PlayerCombatUiLifecyclePolicy` 가
완전 사망 시 캔버스를 끄므로 **죽으면 미니맵·보스 타이머가 같이 사라진다.** 브리핑 후 그대로 가기로 함.
완화하려면 은희 영역(플레이어 UI 수명) 합의 필요.

## ▶▶ 진행 중 (2026-09-18 · **PC 간** Relay 접속 실패 — 진단 계측 투입, 브랜치 `development`)

작업자: **Claude**. 수정 파일: `Assets/1.Scripts/Network/NetworkDiagnosticsLog.cs`(신규) ·
`NetworkSessionLauncher.cs` · `Session/RelayConnectionProvider.cs` · `UnityServicesBootstrap.cs` ·
`Assets/1.Scripts/Managers/LobbySceneManager.cs`.

**증상** (팀장 확인, 2026-09-18) — **같은 PC 에서 빌드 두 개를 띄우면 붙는다.**
**다른 PC 와 붙이려 하면 안 된다.** 에디터도 된다.
→ 처음엔 "빌드에서만"으로 잡았는데 **범위가 틀렸다. PC 간에만 나는 문제다.**

같은 PC 에서 되는 것이 제일 큰 단서다 — 같은 실행 파일이라 `configHash` 가 자동으로 일치한다.
다른 PC 는 그 보장이 없다. **양쪽 빌드가 서로 다른 커밋인 경우가 1순위 용의자.**
새 빌드 없이 판정하는 법: **본인 빌드 폴더를 통째로 복사해 상대 PC 에서 실행**해 보면 된다.

**Player.log 에서 읽어낸 것** — 릴레이는 뚫렸다. `RelayServiceException` 이 없고
`StartClient success` 까지 간 뒤 `reason='Client-1 disconnected by server.'` 로 끊긴다.
이 문구는 NGO 의 `NetworkManager.DisconnectClient(clientId)` 한 곳에서만 나오고
우리 코드에는 호출부가 없다. 호스트가 거절한 것이고, 패키지 내부 호출부는 둘뿐이다.

1. `ConnectionRequestMessage.Deserialize` → `NetworkConfig.CompareConfig` **해시 불일치**. 즉시 끊긴다.
2. `NetworkConnectionManager` 의 **pending 타임아웃**(`ClientConnectionBufferTimeout` 10초) —
   transport 는 붙었는데 connection request 메시지가 안 온 경우. **약 10초 뒤** 끊긴다.

둘 다 경고가 **호스트 쪽에만**, 2번은 심지어 `LogLevel.Developer` 에서만 찍혀서
기본 설정으로는 어느 쪽인지 알 수 없었다. → 그래서 계측을 넣었다.

**넣은 것**
- `NetworkDiagnosticsLog` — 실행 파일 옆(에디터는 프로젝트 루트)에 `network.log`.
  접속 관련 `Debug` 로그와 **모든 경고·에러**를 모으고, `StartHost/Client` 직후
  **NetworkConfig 해시와 프리팹 목록 전체**를 블록으로 남긴다. `AutoFlush` 라 크래시해도 남는다.
- `NetworkSessionLauncher.verboseNetcodeLogging`(기본 켜짐) — NGO 로그를 Developer 로 올린다.
  위 2번 경고를 보이게 하는 유일한 방법이다.
- **로비 화면에 `cfg=<해시>` 표시** — 호스트/클라가 다른 PC 에 있으면 한쪽 network.log 만으로는
  아무것도 못 가린다. 파일을 주고받는 대신 두 사람이 화면의 숫자만 맞춰 보면 된다.
  조인코드 옆·Host 시작·접속 시도 중·거절 메시지에 붙는다.
- 호스트가 명시적으로 끊은 경우(`disconnected by server`)의 안내 문구를 분리했다.
  기존의 "IP/Port 를 확인하세요" 는 릴레이가 이미 뚫린 상황이라 사람을 엉뚱한 데로 보냈다.

### ✅ 판정 완료 (2026-09-18 19:47, 3인 실측 — 은희·지원·태형)

**같은 빌드를 셋이 나눠 쓰니 붙었다.** 3인 Relay 세션이 끝까지 돌았다 —
은희 호스트(`DPH867`) → 지원 `clientId=1` → 태형 `clientId=2`,
`4.MapScene` 3인 동시 로드(`tracked=3`) → 보스전 → Result → Lobby 복귀.
마지막 끊김은 호스트의 `OnApplicationQuit` 이다(버그 아님).

**`configHash` 3대 전부 `2385332456939975880`, 프리팹 26개** — 에디터 값과도 같다.
→ **앞으로 PC 간 테스트는 빌드 폴더를 복사해 쓰고, 화면의 `cfg=` 숫자를 먼저 맞춰 볼 것.**

### ✅ 이전 실패의 원인 확정 — 릴레이가 아니라 빌드 버전 차이

실패한 PC(`D:/p_MT/26.09.18-Build/`)의 `Player-prev.log` 가 결정적이었다. **28회 시도했다.**

| 방식 | 횟수 | 결과 |
|---|---|---|
| Relay 조인코드 | 7 | `Client-1`~`Client-7 disconnected by server.` |
| **Direct IPv4 `172.33.1.3:7777`** | 21 | `Client-1`~`Client-21 disconnected by server.` |

**전송 계층을 LAN 직결로 바꿔도 똑같이 거부당했다** → 릴레이·DTLS·방화벽 전부 배제.
`Client-N` 의 N 이 연속 증가하므로 호스트가 살아서 id 를 발급하고 끊은 것이고,
`ConnectionApproval=0` 이라 남는 거부 사유는 **`NetworkConfig` 해시 불일치** 하나뿐이다.
→ 그 PC 의 빌드와 호스트 빌드가 서로 다른 커밋이었다.

### 🔴 같은 로그에서 나온 진짜 버그 — 로딩 0% 고정 (고침, 2026-09-18)

28회 실패 후 그 PC 가 직접 호스트를 켜고 게임 시작을 눌렀는데
`StartGameLoading` 다음에 **`HandleSceneEvent` 가 한 줄도 안 나왔다.**
정상 실행에는 Load → LoadComplete → LoadEventCompleted 가 찍히고 그래야
`StartTargetLoadAfterSceneEvent` 가 타깃 씬 로드를 시작한다 → **MapScene 로드가 시작조차 안 됨.**

원인은 [NetworkLoadingFlowController](Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs) 의
`_callbacksRegistered` 래치다. 이 래치는 `OnDestroy` 에서만 풀리는데,
**NGO 는 `Start*` 마다 `SceneManager`·`CustomMessagingManager` 를 새로 만들고 Shutdown 때 null 로 만든다**
(`NetworkManager.cs` 의 Initialize/ShutdownInternal). 그래서 두 번째 세션부터는
죽은 객체에 붙은 구독만 남고 새 객체에는 영영 안 붙었다.

- **재현 조건**: 같은 실행 안에서 접속을 한 번이라도 시도한 뒤 호스트를 켜면 발생.
  프로그램을 새로 켜고 바로 호스트하면 정상(그래서 여태 안 잡혔다).
- **수정**: 래치 대신 **구독 대상 인스턴스를 기억**해 세대가 바뀌면 떼고 다시 붙인다
  (`IsRegistrationCurrent`). `UnregisterNetworkCallbacks` 도 현재 프로퍼티가 아니라
  기억해 둔 객체에서 뗀다.
- `CustomMessagingManager` 핸들러도 같이 죽으므로 클라의 진행률 메시지도 함께 복구된다.

### 🔴 같은 로그에서 나온 별건 — 오디오가 통째로 죽어 있다

3대 + 에디터 **전부** 동일하게 씬마다 NRE 가 난다. 세 줄 다 `AudioManager.Instance.___` 다.

- `TitleSceneManager.cs:33` · `LobbySceneManager.cs:77` · `ResultSceneManager.cs:24`
- `AudioManager` 컴포넌트를 가진 것은 **`BossScene.unity` 와 `AudioManager.prefab` 뿐**이고
  MainFlow 씬(BootStrap/Title/Loading/Lobby/Map/Result) 어디에도 없다.
  게다가 `BossScene` 은 `c603e699` 에서 빌드 설정에서 빠졌다.
- → **BGM 이 한 번도 안 나오고 있다.** `AudioManager.prefab` 을 `0.BootStrapScene` 에 넣으면 된다
  (`DontDestroyOnLoad` 라 한 번만).

### 🔴 보스 데이터 (호스트 로그에만 — 서버 권한)

`TwentyThree(Clone): Dash 행의 attackTargeting 이 FarthestPlayer 다` — `No23.asset` 의 Dash 행.
검증 코드가 스스로 잡았다. 그 밖에 전투 중 경고:
`송전기 — 4초 안에 못 갔다(2.1m) → 워프` · `인터럽트가 카운터로 성립하지 않았다` ·
`Failed to create agent because it is not close enough to the NavMesh`.


---

## 🔴 공격속도 — **속성 추가 안 함, 클립 길이 일반화로 간다** (2026-09-21 확정)

브랜치 `feature/PlayerAttackSpeedAttribute` 는 **커밋 0개로 폐기**했다. 이름과 달리 스탯을 새로
만드는 작업이 아니었다.

**결정**: 공격속도는 스탯/모디파이어로 풀지 않는다. **모든 playable 캐릭터의 공격 애니메이션
클립 길이를 일반화(정규화)** 하는 방향으로 간다.

**왜 — 조사에서 나온 것 (다시 조사하지 말 것)**

- **평타 타이밍은 100% 애니메이션 이벤트가 결정한다.** `Hit`/`ComboWindowOpen`/
  `ComboWindowClose`/`End` 전부 클립에 박힌 AnimationEvent →
  `PlayerAnimationEventRelay.cs:21` → `DefaultAttackController.cs:346` (`IsServer` 게이트).
- **`DefaultAttackStep.MotionDuration` 은 함정이다.** 에셋의 `motionDuration` 은 4스텝 전부 `0`
  이라 실효값은 `clip.length` 인데, 이 값은 **End 이벤트 유실 대비 fallback** 과 (현재 미사용인)
  스크립트 이동에만 쓰인다. 여기에 배율을 곱해도 **화면상 공격은 안 빨라지고 fallback 만 일찍
  터진다.**
- **플레이어엔 평타 쿨다운도 입력 버퍼도 없다.** 게이트는 `PlayerStateController.CanAttack` 뿐.
  연타 상한은 순전히 "End 이벤트가 언제 오는가" 다.
- **`animator.speed` 를 만지는 플레이어 코드 0건**, `PlayerAnimatorController` 에 speed 파라미터
  없음(공격 state 4개 모두 `m_Speed: 1`, `m_SpeedParameterActive: 0`).
- 클립 실측: `Garen_Default_Attack_1~4` 길이 2.0 / 1.5 / 1.833 / 1.6초인데 **End 이벤트는
  0.733 / 0.567 / 0.733 / 0.533초.** 클립 뒷부분이 통째로 잘린다. ← 일반화 작업의 출발점.

**죽어 있는 것 — 살릴지 지울지 아직 미정**

- `Unit.FinalAttackSpeed` (`Unit.cs:367`) **게임플레이 소비자 0건.**
  `Docs/tech/game-structure-uml.md:401` 이 이미 이 사실을 적어 뒀다.
- `Unit.ChangeAttackSpeedValue` / `ChangeAttackSpeedValueRpc` 호출처 0건.
- `StatusEffectType.AttackSpeedModifier` (`1 << 9`) 를 `Apply` 하는 코드·에셋 0건.
  (대조: `MoveSpeedModifier` 는 `PlayerMovement.cs:68,191` 에서 실사용 중)
- 🔴 **`Player.prefab:929` 의 `attackSpeed` 값이 `0`이다** (Paladin·Paladin_VFX 도 전부 0).
  아무도 안 읽어서 안 터졌을 뿐, 배선하는 순간 배율 0이 된다.
- `CharacterDefinition` 은 중복 스탯 소스가 **아니라 사문(死文)** 이다. 이 SO 의 에셋 인스턴스가
  프로젝트에 **0개**고, 읽는 쪽 `PlayableCharacterVisual` 은 어떤 프리팹·씬에도 안 붙어 있다.
  실제 스탯 소스는 `Player.prefab` 의 SerializeField 하나뿐.

**몬스터 쪽은 의미가 다르다** — `MonsterBase.cs:1346` 은 `간격 = 1 / AttackSpeed`(초당 횟수)로
살아 있다. 단 행별 명시 쿨다운이 있으면 무시되므로 **보스에선 죽어 있다**
(`BossDataSO.cs:503` 주석). 플레이어와 같은 `Unit._attackSpeed` 필드를 쓰지만 의미가 다르다.

---

## ▶▶ 이전 인수인계 (2026-09-18 · 공격 범위/회전 재작업 + G4 **완료**, 브랜치 `feature/Boss23`)

작업자: **경석(Claude)**. 계획: [PLAN-boss-attack-shapes.md](PLAN-boss-attack-shapes.md) (승인됨 2026-09-18).

**수정 예정 파일 — Codex 는 이 파일들을 동시에 건드리지 말 것:**
`Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab` ·
`Assets/2.Prefabs/Monster/Data/No23.asset` ·
`Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs` ·
`Assets/1.Scripts/Monster/Boss/BossAttackConeTelegraph.cs` ·
`Assets/1.Scripts/Monster/Boss/BossDataSO.cs` ·
`Assets/4.Animations/Wells&No.23/No.23/Controller/No23Controller.controller`

### ✅ 팀장 Play 검증 완료 (2026-09-18)

| 항목 | 결과 |
|---|---|
| 돌진 폭·길이 1.3배 + 예고 폭 버그(`Max(x,z)`→`x`) | ✅ |
| 어퍼컷 띄+원 **합집합 한 덩어리**(SDF min) | ✅ |
| 훅 네모(4.1×6, 치우침 ∓1.07, 몸통 커버 `boxBackOffset 2`) | ✅ |
| 예고 중 회전 잠금(스냅 1회 후 고정) | ✅ |
| 잡기 부채꼴 밖에서 잡히던 버그(`InAttackCone` 공용화) | ✅ |
| 돌진 클립 `Boss_23_dash.001` | ✅ |
| G4 점프 이륙 + 이펙트 타이밍 | ✅ |

🟡 **미검증 1건** — 완료기준 4번(점프 연속 사용 시 애니 배속 잔존). 복원은 두 곳에 있다 —
`BeginJumpHover()` 의 `RestoreAnimatorSpeedClientRpc()` 와 `AbortAttackChain` 의 기존 복원(조기 반환 앞).
깨는 가장 빠른 길은 **이륙 중에 그로기·카운터로 끊는 것**(그 경로가 `AbortAttackChain` 을 탄다).

### 🔴 돌진 클립 교체의 숨은 비용 — 애니 이벤트가 같이 사라진다

`Boss_23_dash` 에만 `OnAttackHit`(정규화 0.15)가 있고 `.001` 은 **이벤트 0개**다.
선딜 게이트가 `IsAnimationReady && IsTimerElapsed` **논리곱**이라 교체 직후 돌진이
**애니만 나오고 전진 0m** 가 됐다(에러·로그 없음).

→ `.meta` 에 이벤트를 심지 않고(SVN) **`BossAttackEntry.hitEventFallbackNormalized`** 칸을 신설해
정규화 시간으로 준비 신호를 대신 낸다. 돌진 = **0.57** (클립 2.633초 × 0.57 = 창 1.5초).
컨트롤러 `DashAttack` 속도는 **1** 이다(2.894 는 클립을 창보다 먼저 끝내버렸다).
⚠️ **다른 클립을 교체할 때도 `.fbx.meta` 의 `functionName` 개수를 먼저 비교할 것.**

### 이번에 확정된 것 (팀장, 2026-09-18)

- **돌진 폭·길이 둘 다 1.3배.** 폭은 `DashBody` 콜라이더, 길이는 `dashDuration`.
- **훅·어퍼·잡기는 예고 중 회전하지 않는다** — 2026-08-18 확정의 **뒤집기**.
  "더 자주 빗나가는 게 맞다"(팀장). 예고 시작에 스냅 조준하고 잠근다.
- 훅 = **네모**(예고·판정 동시), 어퍼 = **띠 + 끝점 원형**(`coneAngle 180 → 360`).
- 돌진 클립 → `Boss_23_dash.001`, 재생속도 **1.2배** 검토.

### 🔴 다시 재지 말 것 — 이번에 실측한 것

- **`dashMaxDistance` 는 구속하지 않는다.** `0.7 × 2.5 × 6 = 10.5m < 16m` 라 **시간이 먼저 끝난다.**
  이 칸만 올리면 아무 변화가 없다. 길이는 `dashDuration` 또는 `dashSpeedMultiplier` 로 바꾼다.
- **돌진 예고 폭은 SO 에 없다.** `TwentyThree.prefab` 의 `DashBody` `m_Size` 에서 읽는다
  (`TryGetDashFootprint`, `halfExtents` 는 `lossyScale` 반영 월드값).
- **훅·어퍼·잡기의 판정은 앵커 콜라이더가 아니라 부채꼴이다** — `coneRadius > 0` 이면
  `HitCone(coneRadius, coneAngle)` 을 탄다(`TwentyThreeBoss.cs:1003`). `Hand_L/R` 의
  BoxCollider(2.6³)는 이 경로에서 안 쓰인다. **예고와 판정은 이미 같은 칸에서 나온다.**
- **돌진은 이미 원샷이다.** `BeginDash` 가 `SetDestination` 1회, `TickDash` 는
  **시간 만료 또는 도착** 중 먼저 오는 쪽에서 끝난다. "반복"으로 보이는 것은
  `Boss_23_dash` 의 **`loopTime: 1`**(애니 루프) 또는 `rageDashCount: 3`(과충전 3연속)이다.
- 돌진 클립 비교(60fps): `Boss_23_dash` 17→102 = **1.42초 루프** /
  `Boss_23_dash.001` 0→158 = **2.63초 원샷**. internalID 는 각각
  `4043419722265811029` / `-3181347391771587835`, FBX guid `cbdaae8bc76ee814d8a32d754976bdde`.

### 🔴 어그로가 "가운데로 튀는" 건 — Codex 교차검증 결과 (2026-09-18)

원인 후보가 **셋**이고 로그로 구분된다. 다시 조사하지 말 것.

| 보이는 것 | 원인 |
|---|---|
| `State=Return` + HP 가 **2000 으로 즉시 회복** | 리쉬 |
| `[23호] 송전기 — … 이동 시작` / `**워프**로 맞춘다` | 차징 기믹(의도됨) |
| 위 둘 없는데 Idle/Chase 에서 중앙을 봄 | 타깃 오식별 |

- 🔴 **차징은 배제됨** — 팀장 관찰상 **만피에서도 발생**하는데, 차징은 `phases` 의 66%/33% 임계에서만 예약된다.
- **리쉬는 실재한다.** `EnterReturn()` → `Unit.Revive()` → `Health.Revive()` → `_currentHp = _maxHp`.
  **복귀 완료가 아니라 진입 즉시**다. 그리고 페이즈는 안 되돌려서 "HP 만따인데 페이즈는 진행된" 상태가 가능.
- **리쉬(15m)를 넘기는 경로는 전부 클램프가 없다**: 점프 착지 ~43.8m · 레이지 3연속 합계 31.5m ·
  차징 워프 상한 없음 · 일반 돌진 13.65m · 어퍼 3.75m · 훅 2.5m · 일반 추격 상한 없음.
  `TwentyThreeBoss.cs` 에 `leash` 문자열은 **0건**이다.
- `_spawnPosition` 은 **Awake 가 아니라 `ServerInitialize()`(OnNetworkSpawn)** 에서 잡힌다(`MonsterBase.cs:198`).
  씬에 직접 배치된 보스는 **그 순간의 자기 위치**가 기준이다(`BossLandingPoint` 를 자동으로 안 따른다).
- **고친 것(2026-09-18)**: `MonsterTargeting.IsAttackable` 이 이제 **`Player` 컴포넌트를 요구**한다.
  예전엔 생명주기 컴포넌트가 없으면 `true` 라 `playerMask` 에 걸린 지형·구조물도 타깃이 됐다.
  함께 `FindNearestTarget` 의 타깃 기준을 `transform.root` → **`Player.transform`** 으로 통일했다
  (AdoptAggro·최원거리 탐색과 기준이 갈라져 exclude 비교가 어긋나던 문제).
  ⚠️ **정적 검색상 Player 레이어(6)에는 플레이어 프리팹뿐이다** — 가드만으로 안 잡힐 수 있다.
  그러면 발생 시점의 `_target` 이름을 로그로 찍어 좁혀야 한다.
- 🟢 **원인 확정 (2026-09-18, Editor.log 실측)** — 리쉬가 맞다. 다시 조사하지 말 것.
  로그: `[Monster] TwentyThree(Clone) 리쉬 기준점 이동 — (500.00, 18.73, 0.00) → (500.00, 0.75, 0.00) (leash 15m)`
  보스룸은 `4.MapScene` 에 **x=500** 으로 놓인 `bossroom.prefab`(Floor **30×30**, Area 28.5×28.5,
  모든 스케일 1, `1bd65563` 이후 미변경). **방이 커진 것이 아니다.**
  보스룸 구간 표본 501개를 기준점 대비로 재보면:
  x `-11.5~13.6` · z `-13.6~6.8` (둘 다 ±15 안) 이지만 **대각선 최대 19.2m**.
  → **15m 초과 표본이 124개(25%)**, 22m 초과는 0개.
  즉 **정사각형 방(30×30) 안에 원형 리쉬(반경 15)를 넣어 네 모서리(21.2m)가 튀어나온 구조**였다.
  조치: `leashRadius: 15 → 22`. 돌진 증가(10.5→13.65m)는 주원인이 아니다 —
  플레이어 자기 이동만으로 이미 25%가 리쉬 밖이었다.
  배제된 것: `송전기` 로그 **0건**(차징 아님) · `23호/어그로` **0건**(AdoptAggro 아님) ·
  `워프` 7건은 전부 `[Dev] F5 워프` · CombatHUD 캔버스는 **콜라이더가 없어** 타깃이 될 수 없다.
- 남은 결함(고치지 않음): 보스 이동 목적지에 리쉬 클램프 없음 · `EnterReturn` 이 페이즈를 안 되돌림.

### 보류된 작업

- **미니맵** — [PLAN-minimap.md](PLAN-minimap.md). 팀원이 `CombatHUD.prefab` 에 키가이드·미니맵 UI 를
  **미푸시 로컬**로 갖고 있어, 같은 프리팹을 건드리면 머지 충돌이 난다. 푸시 후 재개.
- 🔴 **SVN `.meta` 누락 5건** (서버에도 없음 — 팀원마다 guid 가 갈린다):
  `gauge_HP.png` · `gauge_HP_noncolor.png` · `portrail_gunner.png` ·
  `slot_cooldown1,5.png` · `slot_cooldown2,3,4.png`. **은희에게 `.meta` 커밋 요청 필요.**
  배선하면 전원 참조가 깨지므로 그때까지 쓰지 말 것.
- `Dev_Boot` 부트 씬이 `TrainingDummy` → **`4.MapScene`** 으로 바뀌었다(`c446e980`). 빌드 시 주의.

---

## 이전 인수인계 (2026-09-17 · 보호막 VFX 를 이펙트 정책으로 이식, 브랜치 `feature/VFX`)

작업 세션: **민경(Claude)**. 계획·근거·검증은 [PLAN.md](PLAN.md) 최상단.

**수정함 (동시 편집 주의)**: 🔴 `Player/Skill/FirstMeleeSubSkill.cs` · `Player/PlayerShieldVfx.cs`(신규) ·
`Effects/HolyShieldEffect.cs`(신규) · `Effects/HolyShieldEffectSystem.cs`(신규) · `Effects/EffectManager.cs` ·
`2.Prefabs/Player/Paladin/Paladin_VFX.prefab` ·
`50.Art/VFX/Common/Player1/Skill02/FX_HolyShield_{Barrier,Motes}.prefab`(신규) ·
`…/FX_HolyShield_{,Break_}Entry.asset`(신규)
🔴 = 은희 담당 파일. 공유 필요(AGENTS.md §3).

### 새 용어 — **파트 드라이버는 다섯 종이 됐다**

`HolyShieldEffectSystem` 이 Shuriken · FloorArea · FadeInHold · FragmentBurst 에 이어 등록됐다.
붙이는 법은 그대로 — `IEffectSystem` 구현 + `EffectManager.Awake` 한 줄.

### 🔴 에셋 팩 프리팹은 **풀링을 전제하지 않는다**

`Destroy(gameObject)` · `Awake`/`Start` 1회 셋업 · `enabled = false` 로 끝내기 — 셋 다 풀에서는
조용히 깨진다(2회차 대출부터 안 보이거나, 풀 인스턴스가 증발한다). `EffectPrefabRules` 는
`ParticleSystem.stopAction` 과 `TrailRenderer.autodestruct` 만 잡으므로 **MonoBehaviour 의 Destroy 는
그냥 통과한다.** 팩에서 가져온 프리팹은 스크립트를 먼저 걷어내고 드라이버로 다시 쓸 것.

### 🔴 `PlayerSkillBase` 는 MonoBehaviour 다 — 스킬에 RPC 를 못 단다

서버 전용 경로(만료 코루틴 등)에서 연출을 켜고 꺼야 하면 별도 `NetworkBehaviour` 가 필요하다.
`PlayerShieldVfx` 가 그 선례다 — 시작은 `OnClientPlay`(전 피어)라 RPC 없이, 종료만 Reliable RPC.

### `EffectEntry.outroDuration` 은 **모든 파트 중 가장 늦게 끝나는 것**에 맞춘다

`IEffectSystem.Stop` 에 시간 인자가 없어 드라이버가 자기 outro 길이를 매니저에 알릴 통로가 없다.
엔트리 값이 짧으면 코드 구동 파트가 **걷히다 말고 반납된다**.

### 🔴 알려진 문제 — `Effect_48_Impact.mat` 은 텍스처가 빠져 이상하게 보인다 (2026-09-18)

`Assets/50.Art/VFX/_Materials/Effect_48_Impact.mat` 의 `_MainTex`(`3884f641…`)와
`_MaskTex`(`36e6fefa…`)가 **프로젝트 어디에도 없다** — `Assets` · `Packages` · `PackageCache` ·
SVN pristine 까지 확인했다. `Effect_48` 계열 중 이 머티리얼만 살아남았다.

유니티는 빈 텍스처 슬롯을 셰이더 선언의 기본값으로 채우는데 둘 다 `= "white"` 다. 그래서
`Shader_IntegratedEffect` 의 `tex *= tex2D(_MainTex, …)` 루프가 전부 ×1 이 되어 `tex` 가 1.0 인 채로
나오고, `res = tex × _TintColor(0.93, 2.26, 7.33)` 이 **쿼드 전체에 균일하게** 적용된다 —
무늬로 어두워지는 곳이 없어 블룸이 통째로 물린다. 모양은 `_FixedMaskTex`(`mask_4.png`)가 알파만 깎아
겨우 남아 있다. **"갑자기 밝아졌다"의 원인은 톤매핑이 아니라 이것이다.**

영향: `Effect_48_Impact.mat` → `FX_Punch_Wind.prefab` → `FX_Punch_Wind_Entry.asset` 하나뿐
(TrashMobScene 에서 보인다). **원본 팩에서 텍스처를 다시 import 해 물리면 끝난다** — 민경이 나중에 처리.

---

## 이전 인수인계 (2026-09-17 · 23호 공격 재작업 G1·G2·G3·G5·G6·G7 완료, 브랜치 `feature/Boss23`)

작업자: **경석(Claude)**. 계획·근거는 [PLAN.md](PLAN.md) 최상단(1~8차 확정) — 기획 문서
`Re:C | 웰즈 & 23호 보스 전투 아이디어`(팀장 제공, 레포 밖) 기준으로 공격 5종을 재작업했다.

🔴 **Play 검증은 아직 안 했다.** 컴파일·엔진 되읽기까지만 확인했다. 특히 MPPM 2~3인이 필요한 것:
잡기의 전원 끌려옴 · 1명만 붙잡힘 · 붙잡히지 않은 사람의 구출.

### 무엇이 바뀌었나

| 슬라이스 | 내용 |
|---|---|
| **G1/G2** 훅·어퍼 | **예고(0.7초 차오름) → 전진 2.5m → 공격.** 끝점은 부채꼴(훅 120°·팔 쪽 30° 치우침 / 어퍼 180° 반원), 경로는 반경 1.2m 구가 훑는 띠. 경로·끝점이 히트 윈도우를 공유해 **1인 1회** |
| **G3** 돌진 | 거리 게이트 제거(`ignoreDistanceWindow`) + **최원거리 플레이어** 조준 + 어그로 승계. 카운터 창 1.5초 동안 **채워지는 직선 띠** 예고 |
| **G5** 잡기 | **예고 → 끌어당김 → 붙잡기 → 지짐이 → 내려치기 ×3 → 놓아주기.** 전체 속도 배수 `grabCycleSpeed` 한 칸으로 조절 |
| **G6** 인터럽트 | 잡기 창이 **붙잡은 뒤 ~ 3타째 직전**으로 이동. 리액션 잡기=R 고정 / 돌진=L·R 난수(서버가 뽑아 복제) |
| **G7** Wells | **폭탄 투척 제거** — 장식이 됐다. 주기·억제는 드론 자리로 보존(`OnWellsAttackCycle`) |

### 🟢 다음 세션은 여기부터 — **G4 점프 이륙**

**[PLAN.md](PLAN.md) §3-A 를 열면 바로 착수할 수 있다.** 실측값·구현 순서·함정을 다 박아 둑다 —
다시 재지 말 것. 한 줄 요약: `Leap`(`Boss_23_jump`, 1.90초)을 **3배속 0.633초**로 재생하는
이륙 단계를 앞에 넣고, **모델 숨김·무적을 그 뒤로 미룬다.**

🔴 가장 틀리기 쉬운 곳 세 개(상세는 §3-A):
① 재생속도를 상수 3 으로 박지 말고 **클립 길이에서 역산**할 것 ·
② `_stateTimer` 예산에 이륙 몴을 더할 것 ·
③ 예고 장판 `growTime` 을 `이륙 + 체공` 으로 늘릴 것(안 그러면 장판이 공중에서 다 차버린다).

⚠️ `animator.speed` 는 **자세 홀드·잡기 배수와 공유하는 값**이다. 복원을 빼먹으면
이후 모든 애니가 3배속으로 남는다.

### 현재 브랜치 상태 (2026-09-17)

`feature/Boss23` = `origin/feature/Boss23` = **`origin/development`** 이 전부 `6a93a2b3` 로 같다.
development 에 **fast-forward 로 올린 상태**라 팀원이 받으면 이 작업이 함께 들어간다.

⚠️ 보스 씬 로드 시 **직렬화 예외 2670건**(`Unable to find type: 'BossStateChanged'` 등)이 뜼는데
**이번 작업과 무관한 기존 문제**다 — 우리 브랜치가 `f6175811` 에서 지운 레거시 BT 타입들이고,
해당 DB는 git 추적 대상도 아닌 생성 캐시다. 컴파일·플레이를 막지는 않는다.
정리하려면 Visual Scripting 패키지를 실제 쓰는지부터 확인할 것(원래 목록의 ④번).

**남은 것: G4(점프 이륙)** — 클립 전체를 3배속(1.90 → 0.63초)으로 재생하는 방식으로 확정. 미착수.

### 🔴 다음 사람이 알아야 할 것

- **애니 상태 3종은 도구가 만든다** — `Tools/Boss/No23/Add Grab Cycle States`
  (`MagneticGrab` · `GrabEnd` · `getowned_R`). 멱등이라 아트가 fbx 를 다시 올리면 **다시 누르면** 된다.
- **예고와 판정은 같은 SO 칸에서 나온다**(`coneRadius`/`coneAngle`/`coneOffsetAngle`).
  한쪽만 고치면 "장판 밖인데 맞는" 버그가 된다.
- **잡기 속도 배수는 애니와 코드 타이머에 동시에** 걸린다(`ScaledGrab`). 한쪽만 걸면 조용한 데미지 0.
- `Assets/50.Art/` (SVN)는 **건드리지 않았다.** G4 를 `.meta` 절단에서 3배속으로 바꾼 것이 그 이유다.

### 확정 사항 요약 (상세는 `PLAN.md` §3)

- 훅·어퍼는 **2.5m 전진하며** 공격하고 **경로·끝점 전부 데미지**(1인 1회). 훅은 **팔 방향으로 치우친 부채꼴**.
- 돌진은 `minDistance 0` + **가장 먼 플레이어** 조준 + 어그로 승계. 예고는 **테두리 → 채움 → 발동**.
- 점프는 **이륙을 0.5~0.8초 보여주고**(클립 앞부분 절단) 그 뒤 숨김·무적. 착지점은 **즉시 확정 유지**.
- 잡기는 **부채꼴 예고 → 전원 끌어당김 → 최근접 1명 붙잡기 + 나머지 넉백 → 지짐이 → 내려치기 ×3 → 놓아주기**.
  인터럽트는 **붙잡은 뒤 ~ 3타째 직전**. 사이클 전체에 **속도 배수 하나**(애니 + 코드 타이머 동시 적용).
- **Wells 폭탄 투척 제거** — 장식이 된다. `Throw` 주기·상태는 **드론 자리로 보존**.
- 범위 밖: 보스 고유 게이지 · 고출력 상태 · 드론 공격 본체 · **과충전 전기 지대**(이펙트 미제작) ·
  데미지 밸런스 · 보스방/모델 크기(동결).

### 🔴 이번에 확정된 실측 — 다음 사람이 다시 재지 말 것

- **FBX 는 60fps** (`TimeMode: 3` · `CustomFrameRate: 60.0`).
  🔴 **`.fbx.meta` 의 클립 이벤트 `time` 은 정규화(0~1)이지 초가 아니다** — 초로 읽으면 전부 틀린다.
- 보스방 **30×30m**, `BossArea` 28.5×28.5, 대각선 ≈42.4m.
- 잡기 계열 클립 5종의 용도가 확정됐다(팀장 확인):
  `magneticgrab`(1.65s) 끌어당김 · `grab`(3.12s) 붙잡기 · `grabshock`(1.13s) 지짐이 ·
  `grabdump`(0.65s) 내려치기 ×3 · `grabend`(1.37s) 놓아주기.
  이 중 **`magneticgrab` · `grabend` 는 컨트롤러에 미배선**이었다.
- `Boss_23_getowned_L` / `_R`(0.48s) = **인터럽트 성공 리액션**. 컨트롤러에는 `getowned`(=L) 하나뿐이었다.

---

## ▶▶ 이전 인수인계 (2026-09-16 · `origin/development` 머지 완료, 브랜치 `feature/Boss23`)

development 32커밋을 흡수했다(`c4dbd4b9`). 컴파일 에러 0, `4.MapScene` 로드 시 콘솔 0건.
**아래 「머지 보존」 두 절은 머지 이전 각 브랜치의 옛 인수인계다 — 이 절이 최신이다.**

### 머지에서 내린 결정 4가지

| 대상 | 결정 | 이유 |
|---|---|---|
| 투명화(벽 머티리얼 교체) | **계속 OFF** | 실루엣 윤곽선(`c94ffe4e`)으로 대체한 결정 유지 |
| 화면효과 | **development 의 RetroCRT(Cyanilux)로 통일** | 팀 전체가 쓰는 최신 방향. Boss23 의 PixelScanline 계통은 제거 |
| 23호 프리팹 | **development 판(구 `SK_23.fbx`) 채택** | dev 의 VFX 배선 463줄이 전부 구 FBX fileID 기준이라 신 FBX 와 못 섞는다 |
| FogManager 디밍/LoS | **0/0 으로 되돌림** | dev 가 `72392d6d` 에서 켰으나 투명화를 걷어낸 것과 같은 맥락 |

현재 값(라이브 에디터 확인): `WallOcclusionDriver.m_Enabled 0` · `RenderCostAB.startWithWallOcclusion False` ·
`startWithSilhouette True` · `toggleKey F7` · `fog/dim/los 0/0/0` · `PC_Renderer` 피처 6개
(MaskBlur · Decal · RetroCRT · Fog · SSAO · PlayerSilhouette).

### 🔴 지금 깨져 있는 것 — 몬스터 4종 (development 에서 넘어온 문제)

`TwentyThree` · `GauntletBot` · `SpinnerBot` · `WallBot` 프리팹이 **존재하지 않는 에셋 2개**를 참조한다.

```
머티리얼  guid 98b1c99dee6c7b5488abe440aed99c45   m_Materials.Array.data[1] (오버레이 슬롯)
스크립트  guid c7a41f60d2b84e6a9c15d380be720063   InterruptOverlay
```

- 디스크에 실물 없음. **git 이력에도 `.meta` 0건 — 한 번도 커밋된 적이 없다.**
- 프리팹이 `m_Materials.Array.size: 2` 로 슬롯을 늘리고 슬롯 1 을 비워 두므로 **서브메시 하나가 렌더되지 않는다**(Play 화면에서 보스가 파편처럼 보이는 원인).
- 유입 지점: `5092995b 인터럽트 섬광 배선 4종` / `cab1bf66 인터럽트 성공 섬광을 RPC로 — 보스 4종 규약 통일`.
  base 와 머지 전 `feature/Boss23` 에는 **4개 전부 0건**이었고 머지로 들어왔다.

🔴 **development 를 받는 사람은 누구나 같은 증상을 본다.** 담당(민경)이 두 에셋을 올려야 풀린다.
**23호 모델 재스왑으로는 23호 하나만 고쳐지고 나머지 3종은 그대로다.**

### 실종 스크립트 전수 (`unity_find_missing_scripts`, 커버리지 full)

8종 / 영향 에셋 128개. **8개 전부 머지 이전부터 실종**이었음을 guid 대조로 확인했다.

| 타입 | 영향 | 위치 |
|---|---:|---|
| `VeyTrace.Rendering.Occlusion.OcclusionSection` | 108 | `Assets/legacy/.../LevelDeliveryV3/` |
| `VeyTrace.Rendering.Occlusion.ElevationLevel` | 12 | 〃 |
| `VeyTrace.Rendering.Occlusion.ElevationStack` | 12 | 〃 |
| `InterruptOverlay` | 4 | 몬스터 프리팹 4종 (위 항목) |
| Portal VFX 2종 · INab 2종 | 19 | 외부 에셋 |

`VeyTrace.Rendering.Occlusion` 어셈블리에 실제로 있는 건 `WallOcclusionGlobals` ·
`WallOcclusionMaterialBinder` · `WallOcclusionSettings` 뿐이다.

### 남은 작업

1. **23호 모델 재스왑** — 계획은 아래 `PLAN.md` 에 그대로 있다.
2. **몬스터 4종 오버레이 에셋 복구** — 민경 담당. 이게 선행되지 않으면 재스왑해도 3종은 깨진 채다.
3. **블랙보드 실종 타입 3종** — `BaseAttackChoice` · `BombLauncher` · `BossStateChanged` 가 코드에 없다.
   이번 세션에 경고가 안 뜬 건 고쳐져서가 아니라 **해당 블랙보드를 든 씬을 안 열었기 때문**이다.
4. **Visual Scripting 노드 DB** — `1334 node options failed to load`. 이 프로젝트가 VS 를 실제로 쓰는지부터 확인할 것.
5. **Play 검증 미완** — 컴파일·씬 로드까지만 확인했다.

### 이번 머지에서 걸러낸 함정 (다음 머지 때 반복될 것)

- git rename 탐지가 `.meta`(150~240바이트 보일러플레이트)를 **서로 다른 계통끼리 짝지었다**
  (`PixelScanline.meta → 0.Scenes/Art/title.meta`). GUID 자체는 안 깨졌지만 **파일이 조용히 삭제된다.**
  1차 머지에서 18건이 삭제됐고 그중 3건(물 에셋)은 지우면 안 되는 것이었다.
  → **머지 후 `git status | grep '^D '` 를 전수 감사**하고, 각 파일이 base/HEAD/dev 중 어디에 있었는지로 판정할 것.
- `ProfilerHUD` 가 양쪽에서 각자 생성돼 **중복될 뻔했다**(`&882340002` vs `&1493541738`).
  그대로 두면 `RenderCostAB` 가 둘이 되어 실루엣·투명화 토글이 서로 싸운다.
- 씬 충돌은 **변경 규모부터 재라.** `4.MapScene` 은 base→HEAD 23+/51-, base→dev 1679+/2784- 였다.
  작은 쪽을 손으로 재적용하는 게 훨씬 안전하다(실제로 5개 중 4개만 얹으면 됐다).

---

> 🔀 **2026-09-16 머지 보존 — feature/Boss23 (물·실루엣·MCP) 쪽 절.** 아래 development 쪽 절과 함께 남겨 둔다.
> 이 아래는 **머지 이전** 인수인계다. 최신 상태는 위 절을 볼 것.

## ▶▶ 현재 상태 (2026-09-15 · 물 — **물가 마스크로 재설계, 움직임 미검증 · 커밋 안 됨**)

**이번 주 순서**: ① 조준선 클라 복제 ✅ → ② 벽 가림 실루엣 ✅ → ③ **물** ← 지금 여기.

🔴 **작업 대상은 `4.MapScene-trensparent` 하나다(2026-09-15 팀장 확정).**
`4.MapScene` 의 물 2개는 **건드리지 않는다** — 수면 높이 −19 도 그대로 둔다.
(그 씬에는 측정 도구 컴포넌트 제거만 들어갔다.)

### 🔴 Unity MCP 가 끊겼을 때 — 서버를 다시 켜면 더 나빠진다

이전 세션에서 세 번 반복한 함정이다. **원인은 패키지가 아니다.**

- 브릿지(node) 프로세스는 **Claude 세션이 열릴 때 한 번** 뜨고 그때의 토큰을 들고 있다.
- Unity 의 `Stop Server → Start Server` 는 `~/.unity-mcp/auth-token-3000.json` 을 **새로 발급**한다.
- 그래서 재시작할수록 브릿지의 토큰만 더 어긋난다 → 콘솔에 `Rejected unauthorized request to /sse: Invalid session token`.

증상: 커넥터 목록에 `unity` 가 **connected 인데 `tool_count: 0`**. 붙은 것처럼 보여서 오진하기 쉽다.
확인법: 브릿지 프로세스 시작시각과 토큰 파일 mtime 을 비교한다(토큰이 더 최근이면 확정).
**조치: Unity 서버는 켜둔 채 Claude 쪽 세션/커넥터만 다시 연다.**

✅ 2026-09-15 에 이 순서(서버 재시작 → **그 뒤에** 새 Claude 세션)로 복구되는 것을 확인했다.
`tool_count: 87`. 순서가 반대면 또 어긋난다.

> ⚠️ **정정 (2026-09-15 오후 실측)** — 위 설명은 **끊김의 주된 원인이 아니다.**
> - 토큰 불일치가 아니었다: 토큰 `auth-token-3000.json` mtime **10:12** < 브릿지 프로세스 시작 **13:53**.
>   브릿지가 뜰 때 현재 토큰을 읽으므로 맞는다.
> - **에디터를 완전히 껐다 켜도 토큰 파일이 새로 써지지 않았다**(10:12 그대로).
>   "Unity 재시작 = 토큰 재발급" 은 적어도 **에디터 전체 재시작에서는 거짓**이다.
>   (`Stop Server → Start Server` 버튼은 따로 확인하지 않았다.)
>
> **실제 끊김 원인 두 가지:**
> 1. **도메인 리로드.** 스크립트를 재컴파일할 때마다 MCP 서버의 C# 쪽이 통째로 재생성되고 수 초간 불통이다.
>    이 세션에서 재컴파일을 **17번**(`compilationGeneration` 17) 했다 — 그게 그대로 끊김 횟수다.
>    값 하나 고치고 컴파일하기를 반복하지 말고 **수정을 몰아서** 하면 줄어든다.
> 2. **에디터 종료 구간.** 콘솔 `14:07:13 OnApplicationQuit` → `14:08:53 [MCP] Tool registry initialized`.
>    그 1분 40초는 전면 불통이다. 고장이 아니다.
>
> 🔴 어느 쪽이든 **Unity 서버를 다시 켜서 고치려 하지 말 것.** 없던 토큰 불일치를 만든다. 몇 초 기다렸다 재호출.

### 물 — 재작업한 것 (`WaterDark.shader`, `WaterDark.mat`)

증상은 "물이 아니라 그냥 흐르는 텍스처". 원인은 **셰이더 안에 파형이 두 개**였던 것 —
흐르는 얼룩(노이즈)과 벽을 치는 파도(사인)가 서로를 모르니 흐름과 벽 사이에 인과가 없었다.

- **수면 높이 `h` 하나로 통일.** 열린 수면 색·물가 띠·거품이 전부 여기서 파생된다. 물가는 자기 파형을 갖지 않는다.
- **windward 게이팅.** 화면 미분으로 월드 수심 기울기를 풀어 "얕아지는 방향"을 구하고, 흐름이 그쪽을 향하는 벽만 친다. 사방이 균일하면 파도가 아니라 수면 전체의 숨쉬기로 보인다.
- **비대칭 런업 + 천해 증폭.** 대칭 사인은 철썩임이 아니다.
- **수면 하이라이트 추가.** 반사 금지는 유지 — 씬을 안 읽고 고정 광원 + 절차 노멀(프래그먼트 유한차분)로 만드는 툰 스펙큘러다. 먼 수면 지직거림은 픽셀이 덮는 월드 거리로 페이드해 막았다.
- **마루 3개 중첩**(방향 34°/−57°, 파장 ×0.58/×1.9). 파장을 정수비로 두면 마디가 고정돼 "더 복잡한 빨래판"이 되므로 일부러 어긋난 비율이다.
- 머티리얼: `_WaveLength 18→9`, `_DepthWarpScale 0.09→0.16` (뷰가 벽 사이 10~25m 웅덩이라 무늬가 너무 컸다).

### 물가를 "얕음"에서 "물가까지의 거리"로 재설계 (2026-09-15 오후)

증상 두 개가 **한 줄에서** 나왔다 — 물가색을 "물가"가 아니라 "얕음"에 칠하고 있었다.

- 벽 옆 띠 폭이 카메라 각도를 탐 → `waterDepth` 는 수면 아래 수심이 아니라 **시선이 만난 표면과의 Y 차**
- 열린 수면 한가운데가 흰 면적 → 깊이 노이즈가 `shadedDepth` 를 0 으로 눌러 물가색이 칠해짐

**해결: 물가까지의 거리를 에디터에서 구워 텍스처로 준다.**

- `WaterShoreMaskBaker.cs` (신규) — 메뉴 `Tools/Rendering/Look/Bake Water Shore Mask (open scene)`
  - 수면보다 위에 있는 **렌더러 바운즈를 CPU 로 래스터화** → Felzenszwalb–Huttenlocher 정확 EDT
    → signed distance → `WaterShoreMask.png` (8비트, ±20m, 텍셀 0.38m)
  - 🔴 **카메라 렌더로 점유를 찍지 않는다.** 두 번 밟았다: 알파로 빈 곳을 가렸더니 URP 가 알파를
    1 로 채워 전 픽셀이 육지, 마젠타 배경으로 바꿨더니 다음 베이크에서 통째로 뒤집혔다.
  - 🔴 **베이크는 셰이더가 프로퍼티를 선언한 뒤에** 돌려야 한다. 먼저 돌리면 Unity 가
    `SetTexture` 를 **조용히 버린다**(로그는 "구웠다"인데 그림은 안 바뀐다).
  - 🔴 R16 `Texture2D` 를 `.asset` 으로 저장하면 셰이더에서 **검게 샘플링**된다. PNG 로 간다.
- 셰이더: 색·물가 띠·거품·파도 방향이 전부 이 거리에서 나온다. 처오름(swash)은 **물가 기준**으로
  주기를 돈다(전역 스크롤이 아니다 — 그게 "텍스처가 흘러간다"의 정체였다).
  - 🔴 위상은 `ωt + k·d` 라야 파면이 **물가 쪽으로** 온다. `ωt − k·d` 는 반대다.
  - 🔴 띠는 **좁은 가우시안**이어야 한다. `1 - saturate(q/w)` 로 두면 `q<0` 전체가 1 이라 흰 면적이 재발한다.
- 🔴 **거리장의 등고선은 직사각형이다** — 사각형 방 안에서 동심 사각형 무늬("고정 네모")로 보인다.
  기하학적으로 맞는 값이라 버그가 아니다. `Shore Warp` 로 **색·거품용 사본만** 흔들어 흐트러뜨린다.
  물가 판정용 거리를 흔들면 접촉선과 파도 방향까지 흔들린다.
- 인스펙터 정리: 프로퍼티 85개 중 **21개만 노출**(나머지 `[HideInInspector]`).
  🔴 `[Header()]` 는 **ASCII 만** 받는다. 한글을 넣으면 셰이더가 통째로 안 컴파일된다.

### 보스룸 수면 −19 → −3.1 (2026-09-15 팀장 지시)

맵 물과 같은 비주얼로 맞추기 위해. 같이 나온 것:

- 🔴 **물 오브젝트가 전부 `Default`(0) 였다.** `MinimapController.BakeTerrain()` 이 `Water` 레이어를
  컬링으로 빼는데 **한 장도 못 거르고 있었다.** 남은 방어가 `BakeMinWorldY = -5` 뿐이라,
  수면을 −3.1 로 올린 시점부터 330m 쿼드가 미니맵을 덮었을 가능성이 크다
  (`MinimapController.cs:34` 주석이 정확히 이 실패를 경고하고 있었다).
  → 물·바닥 4개를 `Water` 레이어로 옮겼고, **`WaterBedAuthoring` 이 이 불변식을 매번 강제**한다.
  콜라이더가 없어서 물리·LoS 에는 영향 없다. 낙사는 `fallThresholdY = -30` 이라 무관.
- ⚠️ `unity_set_transform` 에 position 만 주면 **회전·스케일이 초기화된다.** 쿼드가 세로로 섰다.

### 🔴 포그 실측 (2026-09-15) — 문서가 틀렸던 부분

**안개는 실제로 꺼져 있다** (`FogManager.fogEnabled = False`). 그런데 `FogRendererFeature` 하나가
**안개 / 디밍+LoS / 어비스 물안개** 를 함께 그리고, 게이트가 `fogEnabled || dimEnabled || abyssEnabled` 다.
씬 값은 `dimEnabled True` · `losEnabled True` · `FogProfile.abyssEnabled 1`.

→ **"불투명 큐 유지" 제약은 그대로 유효하되, 이유는 포그가 아니라 디밍/LoS 다.**
투명 큐로 가면 시야 밖에서도 물만 환하게 남는다.

어비스 물안개 실측: `a = saturate((0 - y)/50) * 0.356 * wobble(0.625~1.375)`
→ 수면 −3.1 에서 **1.4~3.0%**(안 보임). 수면 −19 였던 보스룸은 **8.5~18.6%** 였다 —
수면을 올리면서 이 차이도 사라졌다.

### 🔴 보스(웰즈) 텍스처링 꼬임 — **고치지 말고 둘 것** (2026-09-15)

인게임에서 보스 텍스처가 꼬여 보인다. 원인은 우리 쪽이 아니다.

```
서버:  Assets/50.Art/Char/Boss/SK/SK_welz.fbx  (+ .meta)      ← 모델만 올라옴
로컬:  welz_backH / welz_face / welz_frontH /
       welz_head / welz_protectionglass / welz_top  (.mat)    ← 전부 미추적(?)
```

아트가 **모델·애니메이션 작업 중에 FBX 를 갈아 올렸고 머티리얼은 서버에 없다.** Unity 가
로컬에서 머티리얼을 새로 뽑아 쓰는 중이라 매핑이 어긋난다(교훈 #104 와 같은 뿌리).

🔴 **지금 손대지 않는다.** 예정된 변경이 남아 있다 — **보스 크기 확대**, 그리고 공격이 밋밋해서
**훅·어퍼컷을 전진하면서 치는 애니메이션으로 교체**. 그게 끝나고 **머티리얼까지 서버에 올라온 뒤**
받아서 갈아 끼우면 끝난다. 그 전에 로컬 머티리얼을 커밋하면 팀 참조가 깨진다.

⚠️ **정정 (2026-09-15, 팀장 확인 + 실측)**

1. **`SK_welz` 가 인게임에서 `wells` 로 나오는 것은 정상이다.** 파일명(`welz`)과 표기(`wells`)가
   다른 것은 오타가 아니라 의도다 — 이 절의 원래 진단이 이 점을 증상으로 오해했다.
2. **「보스 크기 확대」는 끝났다** — 아트가 SVN **r299** 로 `23_action_01_RiderSlot_x1_7.fbx`(1.7배)를
   올렸고, 이번 세션에 교체를 마쳤다. 아래 「23호 1.7배 모델 교체」 참조.
3. 위의 **`welz_*.mat` 6개가 SVN 미추적**이라는 부분은 **여전히 유효하다.** 커밋하지 말 것.
   (23호 쪽 머티리얼 `Boss_23_base.mat` 은 SVN 에 정상 등록돼 있어 프리팹에서 참조해도 안전하다.)
4. 남은 예정 변경은 **훅·어퍼컷 전진 애니메이션 교체** 하나다.

### 23호 **1.7배 모델 교체** (2026-09-15 완료 · 커밋 대기)

아트 SVN **r299**(7A_LeeJiWon)로 온 `Assets/50.Art/Char/Boss/23_action_01_RiderSlot_x1_7.fbx` 로 갈았다.
**"모델만 바뀐 것"이 아니었다** — 리그 스케일 100→**1.70**, 메시 노드 `tripo_part_0`→**`Boss_23`**,
신규 본 **`slot_rider.x`**, 테이크 `getowned01/02`→**`getowned_L/_R`** + 신규 `magneticgrab`·`dash.001`.

🔴 **fileID 는 승계된다.** `fileIdsGeneration: 2` 는 이름 경로 기반이라 루트·본·동명 클립의 fileID 가
재익스포트를 넘어 그대로다(실측: 루트 `-8679921383154817045`·`919132149155446097` 일치, 동명 클립 15/15 일치).
그래서 **프리팹·컨트롤러는 guid 만 바꿔 끼웠다.** 새 fileID 가 필요한 건 **분할 클립 4개와 개명 클립**뿐이다.

🔴 **데미지가 0 이 되는 경로가 세 개 있었다. 전부 조용하다 — 컴파일도 테스트도 안 깨진다.**

| # | 원인 | 조치 |
|---|---|---|
| 1 | 신규 `.meta` 가 기본값이라 **애니 이벤트 10개**(`OnAttackHit`×6·`OnAttackEnd`×4)가 통째로 없음. 히트는 타이머 폴백이 없다 | `No23ClipEventAuthoring` 이 이제 **클립 목록 전체를 소유**(분할·트림 포함, 23클립). 「검증만」→「적용」 |
| 2 | `Hand_L`/`Hand_R`/`GrabSocket` 의 `LocalScale = 0.01` 은 구 리그 월드스케일 **100** 상쇄용. 신규는 1.70 이라 주먹 히트박스가 2.6→**0.044**(59분의 1) | `LocalScale` → **1** (월드 박스 4.42 = 1.7배) |
| 3 | 컨트롤러 `getowned` 가 없어진 `getowned01` 을 가리킴 | `getowned_L` 로 재지정 |

**판정값 1.7배 스케일** (팀장 확정): 캡슐 r 1/0.9→1.7/1.53 · h 3.4→5.78 · center 1.7→2.89,
NavMeshAgent r 0.5→0.85 · h 2→3.4, `DashBody` size·pos, `FD_Anchor` y 3.5→5.95,
`BossDirectionIndicator` 링 1.6/2.6→2.72/4.42, 데이터 `attackRange` 2→3.4 와 근접 4종 `maxDistance`.
**탐지·리쉬·돌진·도약 거리와 속도는 안 건드렸다** — 몸 크기가 아니라 아레나 설계값이다.

🔴 **`TwentyThree.prefab` 안에는 웰즈가 타고 있다.** 웰즈도 `LocalScale 0.01` 상쇄를 쓰고 있어
같이 고쳤다 — 위치 ×100, 스케일 **1/1.7 = 0.588235**. `SK_welz.fbx` 는 리그 100 그대로라
**아트는 23호만 키웠다** → 웰즈는 제 크기(월드 1.0)를 유지하는 게 맞다고 판단했다.
웰즈 오프셋(1.46, −0.12, −14.62°)이 신규 본 **`slot_rider.x` 와 정확히 일치**한다 —
아트가 탑승 슬롯으로 만든 본이 맞다. **`slot_rider.x` 아래로 재부모화하면 매직넘버가 사라진다(후속).**

⚠️ **내가 한 번 망가뜨렸다가 되돌린 것** — 머티리얼 재연결 도구가 `GetComponentsInChildren` 로
자식 렌더러를 전부 훑어 **웰즈 머티리얼 6개까지 `Boss_23_base` 로 덮었다**(교훈 #74 #75).
오버라이드 6건 제거로 복구했고, 도구는 **메시의 출처 fbx** 로 대상을 거르도록 고쳤다(이름으로 거르면
다음 재익스포트에 또 깨진다 — 노드 이름은 이미 한 번 바뀌었다).

⚠️ `PrefabUtility.SaveAsPrefabAsset` 이 두 프리팹을 **재직렬화**했다 — 스크립트에 있고 파일에 없던
필드(`attackReceiverSource`·`hitVFXCollider`·`hitVFXType`·`frontMaterial`·`backMaterial`)가 **기본값으로**
채워지고 순서가 정규화됐다. 값 변화는 없다. `m_Script` 집합·오브젝트 블록 수 대조 완료(교훈 #35 #41).

**미확정 — 아트 확인 필요**: `magneticgrab`·`dash.001` 의 용도(대응 FSM 상태가 없어 이번엔 미사용),
`getowned_R`(컨트롤러가 _L 만 쓴다 — 좌/우 피격 분기는 구조 변경 후보).

**남은 검증**: Play 로 훅·어퍼·대시·착지·잡기가 **실제로 데미지를 내는지**. 정지 스샷으로는 못 본다.

### 23호 **툰 셰이딩 적용** (2026-09-15 팀장 지시)

팔에 PBR 빛 연산이 보인다는 지적에서 출발했다. 실측 결과 **툰은 한 번도 안 붙어 있었다** —
이번 세션 이전부터 그랬다(구 fbx 의 `externalObjects` 리맵도 URP/Lit 머티리얼을 가리켰다).

| 머티리얼 | 셰이더 | `_BaseMap` | 상태 |
|---|---|---|---|
| `50.Art/.../texture/Boss_23_base.mat` | **URP/Lit** | `Boss23_BaseColor_4K.png` | 구판 — 이게 붙어 있었다 |
| `3.Materials/Toon/No23_Toon.mat` | **ToonLit** | ~~`lagacy/Boss_23_basecolor.png`~~ → **4K 로 교체** | **지금 이걸 쓴다** |

🔴 **툰 머티리얼은 git(`3.Materials/Toon/`)에 두고 SVN 텍스처를 참조한다. 반대로 하지 말 것** —
`50.Art` 는 SVN 이라 아트가 fbx·mat 을 덮으면 셰이딩 설정이 통째로 날아가고, 우리가 고치면 SVN 커밋이 된다.

🔴 **함정**: `No23_Toon.mat` 의 `_BaseMap` 이 **구판 텍스처**(`texture/lagacy/`, 1.4MB)를 가리키고 있었다.
그대로 갈아끼웠으면 셰이딩은 얻고 **텍스처는 퇴행**했을 것이다. 현재판 `Boss23_BaseColor_4K.png`(5.7MB)로 돌렸다.

⚠️ **톤 값은 구판 텍스처 기준으로 잡힌 것이다** — `_Brightness 1.08` · `_CharAmbient 0.32` ·
`_Saturation 1.02` 등. 4K 로 바꿨으니 **실제 보스 조명 아래에서 다시 봐야 한다**(빈 씬 씬뷰로는 판단 불가).
프로퍼티 세트 자체는 팔라딘(유일한 실사용 툰)과 동일하고 보스용으로 따로 튜닝돼 있다
(`_OutlinePixels 5.4` vs 팔라딘 1.3, `_FaceLiftOn 0`).

⚠️ **웰즈는 아직 URP/Lit 이다.** 웰즈가 23호 등에 타고 있어 한 화면에 같이 나온다 — 셰이딩이 섞인다.
`Wells_*_Toon.mat` 3개와 `Welz_*.mat` 4개가 `3.Materials/Toon/` 에 있으나 **전부 참조 0곳**이고,
웰즈 렌더러는 슬롯이 **6개**(head/frontH/top/backH/protectionglass/face)라 대응이 자명하지 않다. **미정.**

> 전수조사: `ToonLit` 머티리얼 11개 중 실제로 붙어 있는 것은 **팔라딘(플레이어) 3개뿐**이었다.

### 판정값 추가 조정 (2026-09-15 팀장 지시)

- `jumpAoeRadius` **3.5 → 5.25**(×1.5). 착지 AoE 와 예고 장판이 같은 값을 쓴다 — 장판도 같이 커진다.
- `grabRadius` **2.2 → 3.74**(×1.7). 🔴 **이건 내가 깬 짝을 복구한 것이다** — 앞서 `Grab` 행의
  `maxDistance` 를 2.2→3.74 로 올렸는데 `grabRadius`(`OnAttackHit` 시점의 실제 판정)는 2.2 로 남아
  **2.2~3.74m 에서 허공을 잡게 돼 있었다.** 원래 두 값은 같은 숫자로 짝지어져 있었다.
  **둘은 항상 같이 움직여야 한다.**
- 안 건드린 것: `chargeAuraRadius 3.5` · `detectionRadius 8` · `leashRadius 15` · 돌진/도약 거리 · 속도.


### 🔴 다음 세션 **첫 순서** = 브랜치 정리 (2026-09-15 팀장 확정)

전수조사보다 이게 먼저다.

1. `development` 를 받아 현재 브랜치(`feature/Boss23`)와 **머지**, 충돌을 이상 없이 처리
2. `development` 에 **푸시**
3. `development` 기준으로 **새 브랜치**를 파서 이후 작업
4. 기존 `feature/Boss23` **삭제**

⚠️ 머지 전에 확인할 것: 이 브랜치의 물 작업은 `WaterDark.shader` · `WaterDark.mat` ·
`4.MapScene-trensparent.unity` · 신규 `WaterShoreMaskBaker.cs` · `WaterShoreMask.png` 에 걸쳐 있다.
**씬과 머티리얼은 머지 충돌이 나면 수동으로 봐야 한다** — 프리팹·씬 머지는 컴포넌트를 조용히
떨어뜨린 전례가 있다(교훈 #35 #41). 머지 후 `grep -o "Assembly-CSharp::[A-Za-z_0-9]*" | sort -u`
로 컴포넌트 집합을 대조할 것.

### 🔴 그다음 = **전수조사 + 정리** (2026-09-15 팀장 지시)

지금까지 물만 보고 달렸다. 주변이 성한지 확인하고 빚을 갚는 세션.

1. **움직임 검증** — 정지 스샷으로는 못 본다. 플레이 모드에서 **직선 벽 한 구간**을 고정 카메라로
   보며 `접근 → 접촉 → 후퇴 → 남은 거품`이 구별되는지. **이게 안 되면 노브를 아무리 만져도 소용없다.**
2. **미니맵 실제 확인** — 레이어를 옮겼으니 물이 안 덮는지 Play 로 눈으로.
3. **`4.MapScene`** — 아직 구판 그대로다. 같은 처리를 할지 팀장 판단.
4. **빚 갚기** — 숨긴 프로퍼티 63개와 구판 수심 경로(`_UseShoreMask` off) 코드 삭제.
   지금 지우면 되돌릴 대조군이 없어서 남겨 뒀다.
5. **고정 맵 저작 후 마스크 재베이크** — 지금 마스크는 방 안쪽이 물로 찍혀 있다
   (`Level_wall_hallway` 는 벽만 있고 바닥이 런타임 생성).
6. `enableFrameTimingStats` 원복 여부 — 끄면 `ProfilerHUD` 가 GPU ms 를 잃는다.
7. `4.MapScene` 의 `WallOcclusionDriver` 비활성 — 켤지 말지 미정.
8. `_LapAmount/_LapFreq/_LapScale/_LapSpeed` 는 `.mat` 에 있는데 **셰이더가 안 쓴다**(죽은 값).

⚠️ **비용은 아직 추정치다.** 픽셀당 텍스처 샘플 1~2개로 링 샘플링(깊이 탭 8~16)보다 싸고
베이크는 에디터 1회라 런타임 0 이다. 그래도 `ProfilerHUD` 로 실측할 것.

## ▶▶ 이전 상태 (2026-09-14 · 고정 터렛 조준 개편 **완료**)

**이번 주 순서(2026-09-14 팀장 확정)**: ① 조준선 클라 복제 ✅ → ② **플레이어가 벽에 가렸을 때
비주얼 처리** → ③ **물 추가**. ②③ 은 레퍼런스가 있어서 그것에 맞추면 끝나는 작업이라 보스보다 앞에 둔다.
**보스 몬스터 패턴은 그 다음** — 기획이 목·금(9/17~9/18)에 나온다. 나오면 grill → PLAN →
승인 순서로 [PLAN.md](PLAN.md) 맨 위에 새 `CURRENT PLAN` 을 만든다.
**퀘스트 영역은 착수 전 취소**됐다(PLAN.md 의 ❌ 절에 잠긴 결정 14건을 남겨 뒀다).

### 고정 터렛(PeekABot·TeslaBot) 조준 개편 — A~D 전부 Play 확인 완료

커밋 13건: `d14643c5`(A 마스크) `09607915`(B 머리 조준) `41eab95e` `eac0d402` `b1eda4ed`(구조+C 예고선)
`7fe960d9` `0b6c62e9` `cca99d4e` `17c9d428`(D 스폰) `ef614349`(발사 이벤트) `5c83eda5`(고정 유지)
`a05a8e26`(발사 방향) `ea6884f5`(도구 삭제).

동작: 사거리 진입 → 조준선 ON, **0.7초 추적** → **0.5초 고정 유지** → 발사(선 꺼짐) → 0.25초 뜸 → 재조준.
두 시간 모두 `TurretHeadAim` 인스펙터에 노출돼 있다.

구조 요점(다시 건드릴 때 필요한 것만):
- 컨트롤러는 **2레이어**다. `Base`=Idle 전신 / `Shoot`=머리 마스크 Override weight 1.
  레이어 1 의 평상시 상태 `HeadIdle` 에 **Idle 클립이 물려 있어야 한다** — 비우면 머리가 폭주한다(교훈 #97).
- 몸통 회전은 `MonsterBase.BodyRotationLocked` 가 막고, 조준은 `TurretHeadAim` 이 `LateUpdate` 에서
  머리 본에 **델타를 얹는다**(덮어쓰지 않는다 — 본 로컬 축을 모른다).
- 발사는 `ITurretAimGate` 가 연다(`MonsterBase.SeekTurret` 한 곳에서만 물린다). 탄은
  `MonsterRangedAttack.FireDirection` 으로 **조준선과 같은 방향**으로 나간다.
- 예고선은 `TrackingLaser`(LineRenderer). PeekABot 은 아트에 있던 것, **TeslaBot 은 우리가 만든 것**.
  머티리얼은 `MA_TurretAimLaser`(URP Unlit) 를 **런타임 주입**한다 — 아트 기본값이 내장 RP 라 URP 에서 안 보인다.

### SVN 쪽 2건 — **커밋 완료(r297)**. 재발 경로만 기억할 것

| 파일 | 무엇 | 재발 |
|---|---|---|
| `Assets/50.Art/.../TeslaBot/A_Shoot.fbx.meta` | `Shoot` 클립에 `OnAttackHit`·`OnAttackEnd` 저작 | **아트가 팩을 갱신하면 덮인다** → TeslaBot 이 다시 발사를 멈춘다 |
| `Assets/50.Art/MapGen/.../MapGenConfig.asset` | `GroupID 3` 을 TeslaBot 으로(자리표시자 정리) | — |

재발 시 복구 도구: `git checkout a05a8e26 -- Assets/1.Scripts/Monster/Editor/TeslaShootClipEventAuthoring.cs`
(1회용이라 `ea6884f5` 에서 지웠다. 같은 커밋에 `TurretAnimatorAuthoring`·`TeslaTurretGroupRestore` 도 있다.)

### 조준 복제 (2026-09-14 추가 · **MPPM 2인 검증 대기**)

`TurretHeadAim` 이 `MonoBehaviour` → **`NetworkBehaviour`** 가 됐다. 두 프리팹 모두 이미
루트에 `NetworkObject` 가 있어 그대로 붙는다(위 "존에는 `NetworkBehaviour` 금지" 는
**존 프리팹 한정** — 거긴 `NetworkObject` 가 없어서 에디터가 강제로 붙이는 게 문제였다).

- 왜 필요했나: 주석은 "각 피어가 자기 타깃으로 같은 계산을 한다"고 적혀 있었지만 **틀렸다.**
  타깃(`MonsterBase._target`)이 `IsServer` 게이트 안이라 **클라는 `CurrentTarget` 이 항상 null** →
  클라에서는 머리도 안 돌고 예고선도 안 켜졌다. 피하라고 보여 주는 선이 호스트에만 보였다.
- 복제하는 것은 **결과값 2개**뿐: `_netYaw`(float) · `_netTelegraphing`(bool). 서버 쓰기/전원 읽기.
  타깃 참조도 `NetworkTransform` 도 안 태운다. 벽 차단 레이캐스트는 각 피어가 각자 한다.
- **바뀔 때만** 보낸다(`yawSendThreshold` 0.5°). 고정 유지 구간·타깃 없는 동안은 0바이트.
  단 **고정으로 넘어가는 첫 프레임은 임계값을 무시하고 보낸다** — 그 각이 곧 탄 방향이라
  0.5° 어긋난 채 굳으면 클라 예고선이 거짓말이 된다.
- 클라는 받은 각을 `replicationSmoothing`(0.08초) 안에 따라잡는다. 🔴 여기에
  `turnDegreesPerSecond` 를 쓰면 **지연이 영원히 안 줄어든다**(같은 속도로 쫓으면 못 따라잡는다).
- 늦게 들어온 클라는 `OnNetworkSpawn` 에서 현재 각을 **보간 없이** 깐다(안 깔면 휙 돈다).

### 남은 것

- 스폰 분포: `ZoneM_typeA`=PeekABot / **`ZoneM_typeB`=TeslaBot** / `ZoneL_*`=MortarBot /
  `ZoneS_typeA`·`Quest01/02`=ChompBot. 바꾸려면 **존 프리팹의 `ZoneMonsterSpawnSet.defaultMonsterPrefab`**
  을 고친다 — `MapGenConfig.MonsterGroups` 는 `LevelDeliveryV3` 계통 전용이라 현재 맵에 영향이 없다(교훈 #99).

### 이전 세션(2026-09-11)에 한 것

고정 터렛 파트 분리 해결(아래 ✅ 절) · NavMesh 보류 확정(🟡 절) · 레포 루트 정리.
커밋 `9c0b496c` `1bc3a7c1` `91e25d37`.

레포 루트 정리 결과 — `output/`(MCP 감사 산출물 167MB · 1403파일)을 **팀 볼트**
`04-report/mcp-audit-output/` 으로 이관하고 보고서 링크 12곳을 새 경로로 고쳤다(전부 해석 확인).
포폴 자료 16개는 레포 밖으로 뺐고, **같은 폴더의 MCP 감사 도구 12개는 추적 중이라 레포에 남겼다.**
`/output/` · `/.vscode/` · `/.claude/settings.local.json` 을 gitignore 에 추가했다.

### 레포 밖으로 옮긴 것 — 어디에 있는지 (2026-09-14 · Codex 교차검증 후 `c614c366`)

| 대상 | 간 곳 |
|---|---|
| MCP 감사 증거 전체(09-08 ~ 09-11) | `TeamVault/MainProejectVault/04-report/mcp-audit-output/` |
| 포폴 웹앱·초안·도구 | `C:/Users/user/Projects/PortfolioWork/` |
| `tmp` · `Docs_Old` · `Generated` | `C:/Users/user/Projects/MainProjectArchive/` |

`PortfolioPrintPrototype/node_modules`(38,467파일)만 삭제했다 — `npm ci` 로 재생성된다.
나머지는 **삭제하지 않았다**: `Docs_Old` 344파일 중 172개, `Generated` 128파일 중 62개가
Git 이력·외부 사본 어디에도 복구 근거가 없다(Codex 실측).

⚠️ `scripts/` 의 감사 도구 11개가 기본 출력을 **레포 내 `output/`** 으로 잡고, 그중 4개는
**기존 산출물을 읽는다**. 재실행하려면 `AUDIT_OUTPUT` 을 위 볼트 경로로 지정해야 한다.

### 🔴 손대면 안 되는 것

- **`.claude/worktrees` 3.5GB** — 15개 중 **10개가 `rc=128` 로 `git status` 자체가 실패**한다
  (`.git` 파일이 옛 경로 `C:/Users/user/MainProject` 를 가리킨다). 읽히는 5개 중 4개에
  미커밋 변경이 있다. **빈 출력을 "클린"으로 읽으면 안 된다**(교훈 #96).
- **`TempToybox` 497MB** — `HEAD` 가 `refs/heads/.invalid` 인 중단된 clone 잔재로 보이나,
  진행 중인 clone 프로세스 여부가 미확인이라 보류.
- **`Assets/` 내 바이트 중복 파일** — GUID 참조 0건이어도 동적 로딩을 배제하지 못했다.

미처리 1건:
- `ProjectSettings/NetcodeForGameObjects.asset` — **미추적 유지**(팀장 지시). `ProjectSettings`
  30개 중 이것만 빠져 있고 `.gitignore` 가 막는 것도 아니다(커밋 이력 0건 = 한 번도 add 안 됨).
  이 설정이 가리키는 `Assets/DefaultNetworkPrefabs.asset`(프리팹 34개)은 추적 중이라,
  **목록은 공유되는데 목록을 자동으로 채우는 스위치는 각자 로컬**인 상태다.
  **네트워크 = 은희 영역** → 공유·판단 대기.

## ▶▶ 이전 인수인계 (2026-09-09 · 죽은 코드 정리 + 존 NetworkBehaviour 제거, 브랜치 `feature/Boss23`)

작업 세션: **경석(Claude)**. 조사·근거는 [Docs/04-report/deadcode-audit-2026-09-09.md](Docs/04-report/deadcode-audit-2026-09-09.md).
Play 검증 통과(팀장 확인). 커밋 8건 — `7b257f19` `df5249af` `91493afc` `f6175811` `51d6eb51`
`df3caf44` `2f0a0b4f` `53f3413f`.

### 🔴 이번에 확립된 규약 — 존 프리팹에 `NetworkBehaviour` 를 붙이지 않는다

`MonsterSpawner`(NetworkBehaviour)가 존 프리팹 루트에 있던 것이 **「Remove Component 해도
`NetworkObject` 가 되붙는다」의 원인**이었다. NGO 의 `NetworkBehaviourEditor.cs:321→413` 이
인스펙터를 그릴 때마다 다이얼로그를 띄우고 **기본 버튼이 "Yes"** 이며, 제거하면
`NetworkObjectEditor.cs:188` 이 되붙인다. 게이트인 `Check for NetworkObject Component` 는
**`EditorPrefs`(머신 단위)** 라 팀원은 각자 다시 밟는다.

→ 존 쪽 저작 데이터는 **`ZoneMonsterSpawnSet`(순수 `MonoBehaviour`)** 을 쓴다.
   이관 도구: `Tools/Map/Authoring/존 몬스터 스포너 배선 (적용)` (멱등 · 구 컴포넌트와
   `NetworkObject` 를 함께 걷는다). 존 프리팹 8종 이관 완료.
   ⚠️ **존에 새 컴포넌트를 붙일 때 `NetworkBehaviour` 를 상속하지 말 것.** 상태 복제가 필요하면
   `ZoneBridgeGateManager` 처럼 **씬 상주 매니저 + `SlotID` 키** 로 한다.

`MonsterSpawner` 자체는 유지된다 — `MonsterScene`·`TrashMobScene` 에서 진짜 네트워크
스포너로 쓰인다. 존에서만 뗐다.

### 🔴 다른 담당 영역 — 공유 필요

| 대상 | 무엇 | 담당 |
|---|---|---|
| `Player/PlayerColorAssigner.cs` | 무참조로 삭제했다가 **팀장 지시로 원복**(`2f0a0b4f`). guid 동일. 아직 어디에도 부착 0. 재질 변경 본문이 `:26` 부터 주석 처리된 상태 — 배선 계획이 있으면 은희가 진행 | **은희** |
| `Unit/Weapon/{AttackElement,AttackTriggerRelay,OverlapAttack}.cs` | 참조 0으로 측정됐지만 **손대지 않았다**(팀장 지시). 삭제/유지는 은희 판단 | **은희** |
| `Effects/Editor/EffectSystemSetup.cs` | 파일 자신이 *"이 폴더는 더 이상 없다 … 되살릴지 폐기할지는 VFX 담당(민경) 판단이 필요하다"* 고 적고 있다. 이번에 판단하지 않고 남겼다 | **민경** |
| `Effects/EffectTestMover.cs` | 참조 0이지만 **팀장 지시로 보존** | 민경 참고 |
| `TextMesh Pro/Resources/TMP Settings.asset` | 한글 폴백에 `NotoSansKR SDF` 등록(`53f3413f`). 전 UI 에 영향 — 앞으로 한글 텍스트는 폰트를 직접 안 갈아도 폴백으로 렌더된다 | UI 전체 |

### ✅ 해결 — 고정형 몬스터 파트 분리 (PeekABot · TeslaBot)

**원인은 팀장이 짚었다**: 아트 팩 컨트롤러에 `Hide`/`Raise`(PeekABot)·`Charge`(TeslaBot) 상태가
남아 있고, 거기서 나오는 전이가 **우리가 쓰지 않는 트리거**를 요구해 몹이 그 상태에 갇혔다.
3단 신축 컬럼이 중간에 걸려 몸체가 분리돼 보였던 것이다.
(내가 배제/추정한 것들 — 애니메이션 FBX 소스, `hitTrigger` 부재 — 은 **원인이 아니었다**.
`hitTrigger` 없음은 [PLAN.md](PLAN.md) 에 이미 *의도된 정상*으로 기록돼 있었다.)

수정: `Idle` + `Shoot` 두 상태만 있는 컨트롤러를 만들어 **데이터로 교체**한다 —
`MonsterDataSO.animatorControllerOverride` → `MonsterBase.OnNetworkSpawn` 에서 적용(전 피어).
⚠️ 프리팹 오버라이드로는 안 된다 — Animator 가 2단 중첩 프리팹 안에 있어 외부에서
`m_Controller` 를 덮으면 **저장은 되고 YAML 에도 남는데 로드하면 null** 이다(2026-09-10 실측).
커밋 `9a662add`(감사 확장) `8c7af3d2`(컨트롤러) `d4069344`(주석 정정) `c148bdf8`(에이전트 제거).

고정 터렛이므로 **`NavMeshAgent` 도 제거**했다(PeekABot·TeslaBot 만 · 나머지 6종 유지).
`RequireComponent` 0건 · `MonsterBase` 의 모든 접근이 null 가드 · 복귀 판정에 거리 폴백이
있어 에이전트 없이도 상태가 안 멈춘다. 도구 재실행 시 md5 동일(멱등).

### 🟡 보류 — 스폰 지점이 NavMesh 밖일 수 있다 (팀장 결정 2026-09-11)

`MapContentSpawner.TryResolveSpawnPoint` 는 **바닥만** 보고 NavMesh 를 보지 않는다. "바닥 위지만
NavMesh 밖"인 지점이 통과하고, 이동형 몹이 거기 서면 **에러 없이 조용히 안 움직인다.**
→ Play 에서 실제 문제로 드러나지 않으면 **그대로 둔다.**

덧붙일 때의 게이트는 **프리팹에 `NavMeshAgent` 가 있을 때만 샘플링**이다(근거는 해당 함수 주석).
NavMesh 를 읽는 소비자가 에이전트이므로, 고정 터렛 2종은 자동으로 빠진다 —
`archetype == RangedTurret` 로 걸면 지금은 같은 결과지만 대리 지표라 나중에 어긋난다.
> 🔀 **2026-09-16 머지 보존 — origin/development 쪽 절.** 위 절과 내용이 겹치면 최신 인수인계를 새로 쓸 것.
---

> 🔀 **2026-09-17 머지 보존 — development 쪽 절.** 위는 `feature/Boss23`, 아래는 development 이다.
> development 가 94커밋과 함께 **살아 있는 인수인계 3건**을 가져왔다 —
> 허수아비(`feature/training-dummy`) · 팔라딘 Q 토글(`fix/PaladinQCastToggle`) ·
> 플레이어 이동 Motor(`feature/player-motor`). 셋 다 보스 파일과 겹치지 않아 버린 것은 없다.

## ▶▶ 진행 중 — 허수아비 (2026-09-16 · 브랜치 `feature/training-dummy`)

**작업 세션.** Claude 단독. 수정 파일 = `Assets/1.Scripts/Map/TrainingDummy/*`,
`Assets/2.Prefabs/TrainingDummy.prefab`, `Assets/DefaultNetworkPrefabs.asset`. **이 파일들 동시 수정 금지.**

**상태.** 코드·프리팹·검증 씬 완료, 컴파일 오류 0. **Play 검증 대기**(호스트 단독 + MPPM 2인).
설계·확정 사양·알려진 한계는 [PLAN-training-dummy.md](PLAN-training-dummy.md) — 여기 중복 기술하지 않는다.

> ⚠️ 2026-09-22 갱신: 아래 절차는 낡았다. `DevSceneBooter.scene` 필드는 제거됐고 씬도
> `Assets/0.Scenes/Debug/Dev_Boot.unity` 로 옮겼다. 지금은 툴바 `Dev Boot ▾` 에서 고른다
> (이 문서 최상단 「Dev 부팅 자동화」 작업 세션 참조). 씬 로드·스폰 흐름 설명은 그대로 유효하다.

**검증 경로 = `Dev_Boot` 씬.** `DevSceneBooter.scene` 에 띄울 씬 이름을 적고 Play 하면
호스트 기동 → **`NetworkSceneManager` 로 씬 로드**(씬에 배치된 NetworkObject 가 자동 스폰된다)
→ 플레이어 스폰까지 정식 흐름 그대로 돈다. 대상 씬은 **빌드 목록에 enabled 로 등록**돼 있어야 한다.
허수아비 검증 씬은 `Assets/0.Scenes/Debug/TrainingDummy.unity`.
`MonsterTestBootstrap` 은 쓰지 않는다 — 좌클릭 디버그 공격이 플레이어 기본 공격과 입력이 겹친다.

⚠️ **씬마다 `FloatingDamageSpawner` 를 직접 넣어야 데미지 숫자가 뜬다.** 씬 싱글턴이라
`4.MapScene` 것이 따라오지 않는다.

**용어.** *허수아비(Training Dummy)* = 연습장에 놓는 표적. **몬스터가 아니라 맵 오브젝트다** —
`MonsterBase` 계열을 일절 쓰지 않고 `Unit` 만 상속한다. 경석(팀장)의 몬스터 담당 범위 밖.

### 🔴 이번에 확인된 사실 — 전부 코드로 검증됨

1. **`UnitBase` / `IDamageable` 은 이 레포에 없다.** 실제 이름은 `Unit` / `IAttackReceiver` 다.
2. **`CombatTarget`(18) 레이어는 정의만 있고 C# 어디에서도 참조되지 않는다.**
   여기에 무언가를 두면 플레이어 스킬이 하나도 맞지 않는다. 피격 대상은 `Enemy`(8) + `EnemyHurtBox`(14) 다.
3. **체력 0 은 조준을 끊는다.** `PlayerSkillTargeting` 이 `CurrentHealth <= 0` 을 InvalidTarget 으로 처리한다
   (`:201` / `:293` / `:387`). 죽지 않는 대상은 하한을 **1** 로 둬야 궁극기 조준이 유지된다.
4. **데미지 숫자는 "실제 HP 델타"다** (`Unit.cs:533`). 체력이 하한에 붙으면 델타가 0 이라 숫자가 멈춘다.
   명목 피해를 띄우려면 전용 RPC 경로가 필요하다.
5. **`UnitOverheadHealthBar` 는 플레이어 전용이다** — `GetComponentInParent<Player>()` + `!IsOwner` 가 박혀 있다.
   다른 유닛에 재사용 불가.

### 다음 작업
- 연습장 씬과 진입 흐름 (은희 · 네트워크/SceneManagement) — `NetworkLoadingFlowController` 가
  `targetSceneName = "MapScene"` 를 하드코딩 중이라 그쪽을 손대야 한다
- DPS 미터 / 상태이상 아이콘 UI — 별도 작업으로 분리됨

## ▶▶ 현재 인수인계 (2026-09-16 · Hold 스킬 토글 조작 옵션, 브랜치 `fix/PaladinQCastToggle`)

**작업 세션.** Claude. 수정 파일 = `Player/UserInputConfig.cs`(신규),
`Player/Skill/PlayerSkillController.cs`. **이 두 파일 동시 수정 금지.**

**상태.** 코드 완료, **Play 검증 대기**(Unity 에디터가 이 워크트리에 붙어 있지 않아 컴파일도 미확인).

### 용어 — "조작 방식"은 "스킬 설계값"이 아니다

`PlayerSkillInputType`(Press/Hold)은 **스킬의 수명주기 타입**이다 — 어떤 `PlayerSkillBase` 파생을
쓰는지가 여기서 갈린다. 여기에 `Toggle` 을 세 번째 값으로 넣지 않는다.
**같은 Hold 스킬을 꾹 눌러 쓰느냐 토글로 쓰느냐는 유저 조작 취향**이고, 레이어가 다르다.

- `UserInputConfig.HoldSkillAsToggle` — 로컬 유저 설정(PlayerPrefs). 기본 false.
  true 면 Hold 스킬은 **눌러서 진입 → 다시 누르거나 지속시간 만료 시 종료**.
- **스킬 SO 값(지속시간·쿨타임·피해·전진속도)은 두 방식이 완전히 동일하다.** 같은 스킬이기 때문.
- **네트워크에 실리지 않는다.** 서버는 이 설정을 모른다 — 종료 신호는 기존
  `NotifySkillReleasedRpc` → `OnReleased()` 경로로 똑같이 도착한다. 서버 코드 변경 0.
- 지속시간 만료 종료는 두 방식 모두 기존 서버 안전망(`MaxDurationReached`)이 처리한다.
  토글은 "끄는 입력"이 한 번 더 와야 하므로 **안전망 의존도가 홀드보다 높다** — 없애지 말 것.
- 조작 방식은 **시전 시점에 확정**한다(`activeHoldUsesToggle`). 시전 중 옵션이 바뀌어도 그 시전은 안 흔들린다.
- 옵션 UI 는 아직 없다. `TitleOptionsPanel` 의 Controls 탭이 비어 있고, 붙일 때
  `UserInputConfig.HoldSkillAsToggle` 만 읽고 쓰면 된다.

**함정.** 토글은 시전한 그 press 가 곧바로 종료 입력으로 읽힌다 — 켜자마자 꺼진다.
`isToggleEndArmed` 가 "시전 입력이 한 번 떨어졌는지"를 보고 그 전의 재입력을 무시한다. 지우지 말 것.

## ▶▶ 현재 인수인계 (2026-09-16 · 플레이어 이동 Motor 4b3, 브랜치 `feature/player-motor`)

**상태.** 4단계까지 완료. **오너 권위 브랜치 전 항목 Play 검증 통과(2026-09-16).**
4b3 은 **코드 완료 + EditMode 112/112 통과, Play 검증 대기.**
입력 지연은 해소 확인됐고, 루트모션 스냅 수정(b3-3) 후 재검증이 남았다.
상세는 [PLAN-player-motor.md](PLAN-player-motor.md) — 여기 중복 기술하지 않는다.

### 🔴 개발 브랜치 = 오너 권위 (지스타까지)

오너 권위 이동은 `development` 에 머지됐다(2026-09-16). 새 작업은 `development` 에서 분기한다.
서버 권위 구현은
`feature/player-motor-server-auth` 에 완성된 채로 보존돼 있고, 스위치 하나
(`Player.ServerAuthoritativeMovement`) + 프리팹 `AuthorityMode` 로 되살린다.
근거와 복귀 시 체크리스트는 [PLAN-player-motor.md](PLAN-player-motor.md)
"결정 — 지스타(1차 커트라인)까지는 오너 권위" 절.

**아래 불변식 중 1번은 서버 권위 브랜치에만 해당한다.** 나머지 셋은 양쪽 공통이며,
특히 2·4번은 서버 권위로 돌아갈 때 다시 문제가 되므로 지금도 지켜 두는 편이 싸다.

### 🔴 이번에 확정된 불변식

1. **플레이어 위치의 주인은 서버 하나다.** 루트/Armature `NetworkTransform` 은 `AuthorityMode 0`(Server).
   오너 인스턴스만 NT 를 끄고 로컬 예측 + 서버 보정으로 돈다.
2. **예측 재생은 "그 틱의 모든 의도"를 재현해야 한다.** Motor 에 의도를 넣는 채널은 넷이다 —
   velocity / grounded displacement / displacement / pose. raw 입력만 되돌리면 루트모션·스킬 전진·
   플랫폼 캐리·자동접근이 보정 때마다 사라진다.
3. **서버로 가는 입력 RPC 는 raw 두 필드뿐이다.** 클라가 보고한 변위를 서버가 신뢰하면 권위가 무너진다.
   오너의 전체 의도 기록은 **로컬 재생 전용**이다.
4. **오너 전용 게이트(`!IsOwner return`)는 owner 권위 시절의 잔재다.** 서버 권위에서는 서버도
   같은 의도를 만들어야 한다. 남아 있는 곳을 발견하면 `IsSimulating` 계열로 교정한다.

### 🔴 다음 사람이 밟을 함정 — 프리팹 YAML

**컴포넌트 블록은 반드시 `m_GameObject` fileID 로 대상을 확정한 뒤 읽는다.**
줄 위치로 읽으면 자식(`Corpse`, `Armature`)의 컴포넌트를 루트로 오인한다.
이번 세션에 Rigidbody 에서 한 번, NetworkTransform 에서 또 한 번 같은 실수가 났고,
두 번째는 그 오답 위에 진단·핸드오프까지 쌓였다.
**그리고 씬의 프리팹 인스턴스 오버라이드(`m_Modifications`)도 같이 확인한다** — 에셋 값만 보면 틀린다.

### 진단 로그 (검증 끝나면 제거 대상)
`[MoveDiag]` · `[Recon]` · `DevMoveSpeedProbe` · Motor 의 외부 이동 감지 경고.
제거 시점은 4단계 Play 검증 완료 후.

## 이전 인수인계 (2026-09-15 · 플레이어 이동 Motor 4b2-α, 브랜치 `feature/player-motor`)

**작업 세션.** Claude = PLAN·설계·리뷰 / Codex = 4b2-α 구현.
Codex 가 수정 중인 파일: `Player/Player.cs`, `Player/PlayerStateController.cs`,
`Player/PlayerDashController.cs`, `Player/PlayerEncounterLock.cs`, `Player/Life/PlayerLifeInputPolicy.cs`,
`Player/PlayerUiInputPolicy.cs`, `Player/Skill/FirstMeleeMainSkill.cs`,
`Player/Skill/Targeting/PlayerSkillTargeting.cs`, `Player/Motor/PlayerMotor.cs`.
**이 파일들 동시 수정 금지.**

**상태.** 1~3단계 · stepOffset · 4a · 4b1 전부 은희 Play 검증 통과. 지금은 4b2.
상세 설계는 [PLAN-player-motor.md](PLAN-player-motor.md) — 여기 중복 기술하지 않는다.

### 🔴 이번에 드러난 사실 — 서버 권위 NetworkTransform 이 클라 이동을 지운다

`Player.prefab` 루트 `NetworkTransform` 은 `AuthorityMode: 0` = **Server** 다
(`1ccf0d3`, 2026-07-27 이후 계속). PLAN 과 `Player.cs` 주석이 Owner 라고 적어둔 것은 **오류였다.**

- `NetworkTransform.cs:3743` — 서버 권위면 `CanCommitToTransform = IsServer` → **클라는 전부 비권위**
- `NetworkTransform.cs:4461 OnUpdate()` — 비권위 인스턴스는 매 프레임 `ApplyAuthoritativeState()` 로
  transform 을 **무조건 덮어쓴다**
- `Player.cs` — `motor.enabled = IsOwner` → **서버는 원격 플레이어를 영원히 안 움직인다**

⇒ 오너가 로컬로 움직여도 NT 가 서버의 정지 위치로 되돌린다.
**"클라가 스폰 직후 이동 불가" 증상의 1순위 용의자.** 4b2-α 가 이 모순을 제거한다.

**교훈으로 남긴다 — 프리팹의 네트워크 설정을 코드 주석으로 믿지 마라. YAML 을 직접 읽어라.**

### 🆕 플레이어 프리팹 사실 원본 (2026-09-16, Claude)
`Player.prefab` 과 `Paladin.prefab` 을 계속 헷갈리는 문제 → [Docs/tech/player-prefabs.md](Docs/tech/player-prefabs.md).

- **설계 의도**: `Player` = 캐릭터에 무관한 **역할** 프리팹. 그 밑 **`Armature` 자식을 교체해서 플레이 캐릭터를 바꾼다.**
- **현재 데이터**: 정식 흐름이 스폰하는 것은 **`Paladin.prefab`**(`NetworkLoadingFlowController.defaultPlayerPrefab`) —
  역할+캐릭터가 한 덩어리로 평탄화된 통짜 복제본이라 의도에서 벗어나 있다.
- 🔴 교체 메커니즘은 **코드에 이미 있다** — `PlayableCharacterVisual` + `CharacterDefinition`.
  그런데 **어느 프리팹에도 안 붙어 있고 `CharacterDefinition` 에셋이 0개**다.
- 🔴 `transform.Find("Armature")` 폴백이 3곳(`PlayerMovement`·`PlayerSoulController`·`PlayableCharacterVisual`)인데
  Paladin 의 자식 이름은 `Paladin_Armature` 라 전부 불발이다. 정리 시 **이름을 `Armature` 로 통일**할 것.
- ✅ **확정(2026-09-16)**: 캐릭터는 **스폰 전에** 유저 선택값으로 결정된다. 스폰 후 인게임 교체는 설계 범위 밖.
- ✅ **방식 확정**: `Player.prefab` 을 base 로 하는 **캐릭터별 Prefab Variant**. 각 Variant 를 NetworkPrefab 으로
  등록하고 스폰 시 고른다. 런타임 Armature 교체는 **미채택**(Armature 안 `NetworkTransform`·`NetworkAnimator`
  때문에 NGO 상 위험 — 클라는 등록된 프리팹을 스스로 인스턴스화하고 `NetworkSpawnManager.cs:873`,
  `NetworkBehaviourId` 는 계층 순서로 매겨진다 `NetworkObject.cs:2751`).
- 🔴 착수 전 선행 조건 2개 — ① **루트의 `FirstMelee*` 스킬 5종이 전부 가붕이 전용**이라 base 에서 걷어내야 한다
  (Variant 는 컴포넌트 제거가 취약). ② **로비에 캐릭터 선택 UI 가 없다** — 선택값 경로를 새로 만들어야 한다.
- ✅ **경계 확정**: **스킬 5종은 Variant 로 내린다**(base 는 `PlayerSkillController` 슬롯 컨테이너까지).
  걷어내도 코드는 안 깨진다 — `InitializeSkill`·`Player.passive?.`·`PassiveHUD.Bind` 전부 null 안전 확인.
- ✅ **1차 범위 확정**: `Player_Paladin` Variant 까지. 로비 선택 UI·징크스는 범위 밖.
- 📋 계획서 **[PLAN-player-variants.md](PLAN-player-variants.md)** — P1 착수.

**작업 세션 (2026-09-16, Claude · 브랜치 `feature/player-variants`).**
🔴 그 브랜치는 고유 커밋 없이 `development` 에 들어가 있어 정리 때 삭제됐다(2026-09-16).
P1 착수할 때 `development` 에서 다시 딴다. 수정 예정 파일:
`Assets/2.Prefabs/Player/**`, `Assets/2.Prefabs/UI/CombatHUD.prefab`,
`Assets/DefaultNetworkPrefabs.asset`, 그리고 P4 에서 씬 6개. **새 스크립트는 없다.**
🔴 **모터 작업과 같은 프리팹이다 — 이 브랜치 밖에서 플레이어 프리팹을 동시 수정하지 말 것.**
모터 쪽 프리팹 변경이 들어오면 즉시 리베이스해 격차를 작게 유지한다.

### 후순위 미해결
이동 플랫폼·컨베이어 위 상하 떨림(2026-09-15 은희 발견). 원인 미조사, b2/b3 와 독립.
[PLAN-player-motor.md](PLAN-player-motor.md) "미해결 (후순위)" 절 참조.

## ▶▶ 현재 인수인계 (2026-09-11 · 플레이어 이동 Motor 재정립 3단계, 브랜치 `feature/player-motor`)

작업 세션: **은희(Claude → Codex 위임)**. 승인 계획은 [PLAN-player-motor.md](PLAN-player-motor.md).

**✅ 1~3단계 승인 완료 (2026-09-11)** — `b9bd537` + `5f8e014`(막힘 판정 수정).
남은 것: **stepOffset(계단)** → **4단계**(결정론/예측·재조정, 8월 이후).

### 🔴 확정된 불변식 — 새 코드에서 반드시 지킬 것

- **플레이어 위치는 `PlayerMotor`만 바꾼다.** `MovePosition`/`transform.position`을 Player 하위에
  새로 추가하지 말 것(`PlayerFallRecovery` 등 텔레포트 계열만 예외). 이동은 Motor에 **의도**를 제출한다.
- **채널이 넷이다.** `AddVelocity`(m/s, 경사 투영) · `AddGroundedDisplacement`(m, 경사 투영,
  🔴 **수평 전용**) · `AddDisplacement`(m, 투영 없음 — 중력·플랫폼 캐리) · `SetPoseTarget`(절대 포즈, last-wins).
  수직 성분을 `AddGroundedDisplacement`에 넣으면 **경사에서 위로 떠오른다**(제출 시점 경고 로그가 잡는다).
- **`WasBlockedThisTick`은 제출된 수평 의도 기준**이다(Motor 자체 중력·스냅 제외). 전체 벡터로
  비교하면 접지 중 매 틱 true가 되어 넉백이 첫 틱에 취소된다.
- **상태는 `Tick()`(Update)에서 판단, `FixedTick()`(물리 틱)에서 이동 제출**한다.

- 플레이어 루트 Rigidbody는 두 프리팹 모두 `IsKinematic on` / `UseGravity off` / `Interpolate`다.
  본체의 `isKinematic`·`useGravity` 쓰기는 `PlayerMotor`만 소유한다(별도 물리 오브젝트인 Corpse 제외).
- Motor가 수직 속도 적분·최대 낙하속도·접지 스냅을 담당한다. 센서가 캡슐 표면과 지면의 간격을
  복원하므로 살짝 뜬 경우는 아래로, 얕게 파묻힌 경우는 위로 보정한다.
- `ApplyFlatGroundYLock`과 대시 자체 중력은 삭제했다. 걷기·대시·낙하는 같은 Motor 중력을 쓴다.
- 플레이어 넉백은 `AddForce` 대신 초기 속도 + 6m/s² 선형 감쇠를 Motor에 제출한다. 벽 차단 시
  속도만 0으로 만들고 계획된 경직 종료시각은 유지한다.
- `PlayerGameRuleData`가 장애물/Alive 지면/Soul 지면/플레이어 상호 차단/낙하/넉백 값을 소유한다.
  기본 마스크는 장애물=`Default|Ground|Wall|Env`, 지면=`Default|Ground|Env`다.
- 기본값은 플레이어끼리 통과(`blockOtherPlayers=false`). Soul은 이 값이 켜져도 Player를 통과하며,
  생명 상태 전환은 Motor 중력 채널만 끄고 켠다.
- `PlayerMotor.SetMode(Kinematic|Dynamic)`은 수직 속도 양방향 인계 계약만 마련했고 gameplay 사용처는 0개다.
- 비권한 피어는 Rigidbody 플래그를 바꾸지 않고 `PlayerMotor.enabled=false`로 NetworkTransform과의 경쟁을 막는다.

정적 검증: Dash 어셈블리 오류/경고 0, `Assembly-CSharp` 오류 0(기존 경고 18), 구형 기호·프리팹
플래그·`SetMode` 사용처 검사 통과. 사용자 지시로 Play/MPPM은 실행하지 않았다. 특히 접지 스냅,
내리막 대시 후 이동, 넉백 벽 충돌/경직, 플레이어·Soul 통과, 낙사 복귀는 수동 검증이 필요하다.

## ▶▶ 현재 인수인계 (2026-09-11 · 플레이어 이동 Motor 재정립 2단계-b, 브랜치 `feature/player-motor`)

작업 세션: **은희(Claude → Codex 위임)**. 브랜치 `feature/player-motor` (base `origin/development` `72392d6`).
계획·근거·완료조건은 [PLAN-player-motor.md](PLAN-player-motor.md) — 여기에 중복 기술하지 않는다.

**2단계-b 코드 완료, Play 검증 대기** — 커밋 `96c8350`.

- `Player/**`의 실제 `MovePosition(...)` 호출은 `PlayerMotor` 내부 1곳뿐이다(추락 복귀 예외 제외).
- FSM 판단·엣지 입력 소비는 `Update`에 유지하고, 대시·인터럽트·스크립트 평타·구속 추종의 이동 제출만
  `FixedTick`으로 분리했다. 프레임 히치 때 물리 틱 수만큼 동일 변위를 중복 제출하지 않는다.
- 애니메이터 루트모션은 `OnAnimatorMove`의 프레임 델타를 `AddDisplacement(+=)`로 래치해 다음 Motor 틱에 합산한다.
- 구속 추종은 델타 누적이 아니라 절대 포즈의 **마지막 값 우선** 채널이다. 일반 이동 의도보다 우선하며
  충돌 비활성화 상태의 기존 소켓/Push 추종 의미를 보존한다.
- 대시의 요청/적용/차단 진단은 `PlayerMotor.MovementResolved` 결과를 집계한다. 중복 스윕과
  `ClampByStaticGeometry`·`ResolvePlanarSlopeDirection`은 삭제했다.
- `PlayerStateContext.Rigidbody`는 제거했다. 3단계에서 교체될 넉백·구속 물리 플래그만 각 상태가
  자기 `Rigidbody`를 생성 시 1회 캐시한다.
- `DashPressed`는 Input System 콜백에서 래치하고 상태 틱 종료 후 소비한다.

수정 파일: `PlayerMotor.cs` · `Player.cs` · `PlayerMovement.cs` · `PlayerStateController.cs` ·
`PlayerInputReader.cs` · `DefaultAttackController.cs` · `FirstMeleeMainSkill.cs` · `PlayerSkillTargeting.cs`.

검증: `dotnet build Assembly-CSharp.csproj --no-restore` **오류 0**. 기존 경고 18건만 존재.
사용자 지시로 Play/MPPM은 실행하지 않았다. 대시 거리·평타 루트모션·구속 추종은 수동 검증 필요.

**1단계 수정함 (동시 편집 금지)**: `Assets/1.Scripts/Player/PlayerMovement.cs` ·
`Assets/1.Scripts/Player/Player.cs` · 🔴 `Assets/1.Scripts/Map/MovingPlatform.cs`(회귀 수정)

커밋: `1e6113b`(Codex, 루프 정정) → `76824f2`(회귀 수정).

**실측 검증 (2026-09-11, 단일 에디터)**: 지속 이동속도 **30fps 5.072 / 60fps 5.009 / 144fps 4.992 m/s
— 편차 1.58%** (완료조건 5% 이내 통과, `maxSpeed=5` 설정값과 일치). 컴파일 0에러.
예외는 전부 서드파티 `INab WeaponTrailEffect`(기존 문제, 무관).
계측기 = `Assets/1.Scripts/Dev/DevMoveSpeedProbe.cs`(**검증 종료 후 삭제할 것**).

**미검증**: 이동 플랫폼 탑승(= `76824f2`가 고친 대상) · MPPM 2인 · 경사/벽 슬라이드.

이번에 확정된 계약만 적는다:

- 🔴 **플레이어 위치는 최종적으로 `PlayerMotor` 하나만 쓴다.** 현재 `rb.MovePosition` 호출부가
  7곳(+중력)으로 흩어져 있고, 이게 벽 관통·경사·모서리 Y누수·대시 제자리종료의 공통 원인이다.
  2단계부터 `MoveRoot`/`MoveTowardsPoint`가 사라지고 `PlayerStateContext.Rigidbody`도 제거된다.
  **Player 하위에서 `MovePosition`/`transform.position`을 새로 추가하지 말 것.**
- **1단계 범위는 루프 정정뿐이다** — `Move()`/`ApplyPlatformCarry()`를 `FixedUpdate`로,
  `Time.deltaTime` → `Time.fixedDeltaTime`. 물리 플래그·넉백·마스크는 **건드리지 않는다**.
- `PlayerMovement`에는 이미 `FixedUpdate`가 있다(`ApplyFlatGroundYLock`, development에서 추가됨).
  새로 만들지 말고 **기존 것에 합류**시킬 것. 이 Y잠금은 3단계에서 삭제된다(kinematic 전환 후 불필요).
- 🔴 **`AddCarryDelta`는 변위(m), 입력 이동은 속도×dt다.** 1단계에서 둘 다 `FixedUpdate`로 가야
  일관된다 — 한쪽만 옮기면 플랫폼 탑승 중 이동량이 프레임레이트에 따라 갈린다.
- 🔴 **`ISurfaceCarrier` 구현체 둘의 계약이 다르다.** `ConveyorTile`은 `speed × dt`(속도형,
  루프 무관)지만 `MovingPlatform`은 `dt`를 **무시하고** 직전 샘플과의 차분을 돌려준다(변위형).
  **변위형은 생산 주기와 소비 주기가 반드시 같아야 한다** — 소비자만 `FixedUpdate`로 옮겼다가
  고프레임에서 이동량의 ~35%만 전달되는 회귀가 났다(`76824f2`에서 수정).
  새 `ISurfaceCarrier`를 만들 때 어느 쪽 계약인지 먼저 정할 것.

## 이전 인수인계 (2026-09-14 · 인터럽트 연출 4종 통일 + SpinnerBot 메시 분리, 브랜치 `feature/VFX`)

작업 세션: **민경(Claude)**.

**수정함 (동시 편집 주의)**: 🔴 `Monster/Boss/GauntletBot.cs` · 🔴 `Monster/Boss/SpinnerBot.cs` ·
🔴 `Monster/Boss/WallBot.cs` · `Monster/Boss/TwentyThreeBoss.cs` · `50.Art/VFX/Scripts/InterruptOverlay.cs` ·
`Monster/Editor/SpinnerBotMeshSplit.cs`(신규) · `2.Prefabs/Monster/SpinnerBot.prefab` ·
`50.Art/.../Models/R_Spinnerbot_01_{Body,Blades}.asset`(신규)
🔴 = 경석 담당 파일. 공유 필요(AGENTS.md §3).

### ✅ "인터럽트 가능" 오버레이는 이제 인터페이스로 붙는다

`InterruptOverlay` 가 `IBossTelegraph` 를 구현한다 — `SetCounterWindow(bool) => Interruptible = open`.
몬스터 코드는 이 컴포넌트를 **모른다.** 서버 `MonsterBase.ServerSetCounterWindow` → RPC → 각 피어의
`ApplyCounterWindowVisual` 이 `GetComponentInChildren<IBossTelegraph>` 로 찾아 부른다.
카운터 창을 여는 몹이면 **프리팹에 붙이기만 하면** 따라온다.

⚠️ `BossCounterTelegraph`(전신 노란 틴트)는 **어느 프리팹에도 안 붙어 있다**(guid 전수 검색 0건).
   즉 중간보스 3종은 그동안 카운터 창에 아무 표시가 없었다 — 뺏어온 게 아니라 빈자리를 채운 것이다.

### ✅ 인터럽트 성공 섬광 — 보스 4종 규약 통일

`FX_Interrupt_Flash_Entry`(`50.Art/VFX/Common/`, 몹 전용 아님) 원샷. 넷 다 모양이 같다:
`[Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]` + null 가드 + 경고 1회.

| 보스 | 호출 지점 | 프리팹 배선 |
|---|---|---|
| GauntletBot | `CounterSucceeded()` | ✅ 연결됨 |
| SpinnerBot | `CounterSucceeded()` | ❌ EffectSocketPlayer 새로 붙여야 함 |
| WallBot | `CounterSucceeded()` | ❌ EffectSocketPlayer 하나도 없음 |
| TwentyThreeBoss | `ReceiveAttack()` 카운터 성사 지점 | ⚠️ 플레이어는 있음(Id 비어 방치), 슬롯만 연결 |

🔴 **23호는 `EnterCounterGroggy` 안에 넣지 않았다.** 그 메서드는 송전기 전멸(S7) 경로도 함께 쓰는데
   그건 플레이어가 끊어낸 게 아니다. 섞으면 연출이 무엇을 칭찬하는지가 흐려진다.

🔴 가붕이의 `interruptFlash.PlayOnce()` 는 원래 `CounterSucceeded()` 안에서 **직접** 불렸다 —
   `TakeDamage` 의 `IsServer` 게이트 뒤라 **호스트에서만 보였다.** 이 레포 단골 사고. RPC 로 바꿨다.

### ✅ 가붕이 펀치 명중 스파크 — 좌/우

`PerformAttackHit()` 에서 `meleeAttack.Hit() > 0` 일 때만 `PlaySparkRpc(_currentAttack)`.
L/R 은 `GauntletAttackId` enum 에 이미 들어 있다. 좌/우를 RPC 인자로 싣는 이유: `_currentAttack` 은
**복제되지 않는 서버 전용 값**이라(선택 결과는 `PlayAttackAnimClientRpc` 로만 나간다) 클라가 모른다.

### 🔴 오버레이 머티리얼 슬롯 트릭의 한계 — SpinnerBot 메시 분리

**머티리얼 슬롯은 서브메시와 1:1 이다.** 개수를 넘는 슬롯만 "한 번 더 그리기"가 되고,
그 덤은 **언제나 마지막 서브메시**에 붙는다. 그래서:

- Gauntlet · Wall : 서브메시 1개 → 초과 슬롯이 몸 전체 ✅ (운이 좋았던 것)
- Spinner : 서브메시 2개(몸통 2299면 / 날개 **1면** 반투명 카드) → 초과 슬롯이 **날개**에만 ❌

몸통은 URP/**Lit 불투명**, 날개는 URP/**Unlit 투명**(`_Surface: 1`)이라 **서브메시 합치기는 불가**다
(머티리얼이 하나만 남아 둘 중 하나를 잃는다). 그래서 **몸통/날개를 렌더러 2개로 쪼갰다.**

🔴 **Blender 왕복은 막힌 길이다 (2026-09-14 실측).** 재export 하면 `Armature` 노드가 끼어든다 —
   원본 FBX(3ds Max)에 그 문자열 **0회**, Blender 재export 본에 **4회**. 이 리그는 Generic
   (`animationType: 2`)이고 클립 7종이 전부 `R_Spinnerbot_01` 아바타를 **Copy From Other Avatar**
   로 문다. Generic 은 본 **경로**로 바인딩하므로 노드가 하나 끼면 클립이 **조용히** 안 붙는다.
   → FBX · 아바타 · 본 GameObject 는 건드리지 말고 **메시만 프로젝트 안에서** 쪼갤 것.

도구: `Tools/Boss/SpinnerBot — 메시 분리 (몸통/날개) (멱등)` + `— 검증 (읽기 전용)`.
본 29개를 두 렌더러가 **공유**한다(복제하면 애니가 따로 논다). 쓰지 않는 정점은 버려 압축한다 —
통째로 복사하면 면 한 장짜리 날개가 몸통 4807 정점을 매 프레임 스키닝한다.
가중치는 구 API(`Mesh.boneWeights`, 영향 4개로 **잘림**) 대신 `GetAllBoneWeights` 로 정확히 옮긴다.

⚠️ Blender 와 Unity 는 세는 단위가 다르다. 몸통 2291정점/2299폴리곤(Blender) =
   4807정점/4023삼각형(Unity) — Unity 는 UV 이음새·노멀이 갈리는 자리마다 정점을 쪼갠다.
   **숫자가 안 맞는다고 버그로 의심하지 말 것.**

⚠️ 프리팹에 죽은 오버라이드 `m_Materials.Array.data[2]`(= Interrupt)가 남아 있다. 배열 크기가 2라
   지금은 무시되지만, **누가 슬롯을 하나 늘리는 순간 되살아나** 오버레이가 두 번 그려진다.

### 미결

- **컴파일·Play 검증 전무.** 이번 세션 변경분 전체가 한 번도 안 돌았다.
- 스피너·월봇 프리팹에 인터럽트 섬광 `EffectSocketPlayer` 붙이기 / 23호는 슬롯만 연결
- MPPM 2인으로 클라이언트에서 섬광·스파크가 보이는지 (가붕이 섬광은 이전엔 호스트 전용이었다)
- **커밋이 둘로 갈린다** — `Models/R_Spinnerbot_01_*.asset` 은 **SVN**,
  나머지 코드·프리팹은 **git**. 한쪽만 올리면 다른 사람 화면에서 스피너 메시 참조가 깨진다.

---

## 이전 인수인계 (2026-09-09 · 23호 점프어택 VFX 카탈로그 이관 + 애니 이벤트 이펙트 일반화, 브랜치 `feature/VFX`)

작업 세션: **민경(Claude)**.

**수정함 (동시 편집 주의)**: `Monster/Boss/TwentyThreeBoss.cs` · `Monster/Boss/BossDataSO.cs` ·
🔴 `Monster/MonsterMeleeAttack.cs` · `Effects/EffectAnimEvents.cs` · `Effects/EffectSocketPlayer.cs` ·
`Effects/IAnimEventEffect.cs`(신규) · `Effects/EffectPathPlayer.cs`(신규) ·
`Effects/EffectStagePlayer.cs`(신규).
**프리팹·씬·`EffectCatalog.asset`은 건드리지 않았다** — Unity 저작은 아래 「미결」 참조.

### 🔴 다른 담당 영역을 건드렸다 — 공유 필요

| 파일 | 변경 | 담당 |
|---|---|---|
| `Monster/MonsterMeleeAttack.cs` | `Hit()` 반환형 `void` → `int` (실제로 피해가 들어간 대상 수) | 몬스터 전체(경석) |
| `Monster/Boss/BossBomb.cs` | 상태 대입을 `SetState()` 로 일원화 + 이동 트레일·폭발 연출 RPC | 몬스터 전체(경석) |
| `Monster/AreaZone.cs` | `visualEffect`(EffectEntry) 필드 + 반경 연동 루프 재생·회수 | 몬스터 전체(경석) |

**셋 다 하위 호환이다.**

- `MonsterMeleeAttack.Hit()` 는 이미 내부에서 `TryResolveHit` 의 `bool` 을 받아 놓고 넉백 판단에만
  쓰고 버리고 있었다 — 그 값을 세어 돌려줄 뿐 판정 로직은 한 줄도 바뀌지 않았다.
  호출처 **10곳이 전부 문장 형태**(`meleeAttack?.Hit();`)라 GauntletBot·SpinnerBot·WallBot·
  MonsterBase 어느 것도 손대지 않았고 그대로 컴파일된다.
- `BossBomb` 은 `_state = ...` 대입 **8곳**을 `SetState()` 로 돌렸을 뿐 상태 전이 조건은 그대로다.
  🔴 앞으로 `_state` 를 직접 대입하면 트레일 연출이 어긋난다.
- `AreaZone` 은 **필드 추가뿐**이다. 기존 장판 프리팹은 `visualEffect` 가 비어 있어 동작이 안 바뀐다.

⚠️ **`Hit()` 의 반환값은 서버에서만 유효하다.** 클라는 판정 자체를 하지 않아 **항상 0** 이다 —
이 값으로 연출을 켜려면 반드시 RPC 로 내보낼 것(직접 재생하면 호스트에서만 보인다).

### 🔴 "때렸다"와 "맞았다"를 가르는 자리가 생겼다

근접 타격 연출은 **명중했을 때만** 나와야 하는데, 지금까지 그걸 알 방법이 없었다.
🔴 **애니 이벤트로는 못 가른다 — 클립은 맞았는지 모른다.** 허공을 때려도 터진다.
그래서 이 연출만은 **코드가 판정 결과를 보고** 낸다.

- 흐름: `PerformAttackHit` 에서 `meleeAttack.Hit() > 0` → `PlayAttackHitEffectRpc(attackId)`
  → 각 피어가 손별 `EffectSocketPlayer.PlayOnce()`.
- 배선은 **보스 인스펙터의 직접 참조**다 — `leftHookHit` / `rightHookHit` / `upperHit`
  (`grabPulse` · `chargeBall` 과 같은 방식). 손마다 소켓이 달라 공격별로 나눈다.
- Unreliable: 한 대 분이 빠져도 상태가 발산하지 않는다.

🔴 **`BossDataSO` 에 문자열 Id 로 두려다 되돌렸다(2026-09-10).** SO 는 에셋이라 프리팹의
컴포넌트를 참조할 수 없어서 매핑을 데이터에 두려면 문자열밖에 없는데, 그 대가가 오타 실패 모드다.
얻는 것은 "공격이 늘어도 코드가 안 는다" 하나뿐이고 **보스는 하나 · 근접은 3종**이라 값이 안 된다.
같은 이유로 `grabPulse` 도 id 조회에서 직접 참조로 바꿨다 — **이 보스의 연출 배선은 전부 직접 참조로 통일한다.**

⚠️ **두 계통을 섞지 말 것.** 명중과 무관한 연출(공격 궤적 등)은 애니 이벤트가 맞고,
명중이 조건인 연출만 이 경로다.

### 🔴 애니 이벤트 이펙트가 인터페이스로 열렸다

`EffectAnimEvents` 가 `EffectSocketPlayer` 구체 타입을 색인하던 것을
**`IAnimEventEffect`**(`Id` / `PlayOnce` / `Play` / `Stop`) 색인으로 바꿨다.
**새 연출 방식은 이 인터페이스만 구현하면 클립에서 바로 부를 수 있다** — 릴레이의 이벤트 함수
셋(`PlayEffect` / `StartEffect` / `StopEffect`)은 앞으로도 늘지 않는다.

| 구현체 | 무엇 |
|---|---|
| `EffectSocketPlayer` | 트랜스폼 **하나**에 붙어 따라다니는 이펙트 (기존) |
| `EffectPathPlayer` | 트랜스폼 **배열**을 훑고 지나가는 반복 펄스 (신규) |

- `EffectAnimEvents.effects` 필드가 `EffectSocketPlayer[]` → `MonoBehaviour[]` 로 바뀌었다
  (유니티가 인터페이스를 직렬화 못 한다 — `Hurtbox.attackReceiverSource` 와 같은 관용구,
  `OnValidate` 가 타입을 검사한다). **기존 프리팹 5종 모두 이 배열이 비어 있어 유실된 값은 없다.**
- `EffectAnimEvents.Has(id)` 추가 — **경고 없이** 존재만 본다. "없으면 그만"인 안전망 경로용이고,
  클립이 부르는 경로는 계속 `Find`(오타를 경고로 잡는다)를 쓴다.

### 🔴 `EffectPathPlayer` — 팔 전기 펄스의 일반화

구 `GrabPulseDriver`(archive 브랜치)를 이식하면서 **보스·팔에 대한 의존을 전부 걷어냈다.**
이 컴포넌트가 아는 것은 "월드 좌표를 가진 트랜스폼의 순서"뿐이라 팔·다리·무기·레일 어디에나 붙는다.

- `shoulder`/`forearm`/`hand` 3개 고정 필드 → **`Transform[] path`(2개 이상)**. 구간 사이는
  **길이 비례** 보간이다 — 균등하게 나누면 구간 길이 차만큼 관절에서 속도가 튄다.
- **애니메이터 상태 폴링을 걷어냈다.** 구 버전은 `GrabPulseProfile` SO 에 상태 이름을 적어 두고
  매 프레임 `GetCurrentAnimatorStateInfo` 로 재생 여부를 판정했다(SSM 전이가 콜백을 안 태워서 쓴 우회).
  이제 시작·종료가 **애니 이벤트**라 그 판정이 필요 없다 → **`GrabPulseProfile`(SO + 스크립트 2종)은
  이식하지 않았다.** 타이밍(`travelTime`/`interval`)은 컴포넌트 필드로 통일했다
  (팀장 결정: 일단 통일, 상태별 강약이 필요해지면 그때 id 를 분기).
- 앵커는 풀로 돈다 — `PlayLooping` 이 트랜스폼 하나를 추종하는데 펄스 겹침을 허용하므로
  앵커를 공유하면 **살아 있는 펄스 전부가 같은 지점으로 끌려온다.**
- 핸들 회수 3지점: 펄스 도착 · `Stop()` · `OnDisable()`(즉시).

### 🔴 팔 전기의 켜고 끄기는 **클립이 아니라 `TwentyThreeBoss` 가 한다**

애니 이벤트로 갈 수도 있었지만(경로는 열려 있다) **코드로 잡았다** — 그랩이 끝나는 길이 넷이라
끄는 책임을 클립에 맡기면 하나만 빠져도 팔에 전기가 영영 남는다(SpinnerBot 함정).
그래서 시작·종료를 그랩 상태 전이와 **같은 자리**에 둔다. 보스는 `EffectPathPlayer` 를
**직접 참조**한다(`grabPulse` 필드) — id 문자열 조회를 안 거치므로 오타 실패 모드가 없다.

| 시점 | 위치 |
|---|---|
| **시작** — 그랩 애니가 나갈 때 | `StartAttack` 의 `case BossAttackId.Grab` |
| 종료 — 던지기(손에서 떠나는 순간) | `ReleaseGrabThrow` |
| 종료 — 헛잡기 | `AcquireGrab` 실패 분기 |
| 종료 — 잡힌 대상 소멸 | `TickGrabHold` |
| 종료 — 체인 중단(카운터·그로기·사망) | `AbortAttackChain` ← **마지막 그물** |

- **판정(`AcquireGrab`)이 아니라 애니 시작에서 켠다.** 판정은 히트 프레임이라 거기서 켜면
  팔을 뻗는 동안 아무 예고가 없다. 대신 헛잡기 경로에서 반드시 꺼야 한다.
- **Throw 는 `BeginGrabThrow` 가 아니라 `ReleaseGrabThrow` 에서 끈다** — 던지기 준비 동작 내내
  전기가 붙어 있어야 "감전시켜 던진다"로 읽힌다(구 `GrabPulseProfile` 도 Throw 상태에서 계속 냈다).
- 두 RPC 모두 **Reliable(기본)**. Stop 이 유실되면 전기가 영영 남는다 —
  착지 충돌(`PlayJumpImpactRpc`)이 Unreliable 인 것과 정반대 이유다.
- 🔴 `EffectPathPlayer.OnDisable` 안전망만으로는 부족하다 — 그로기는 컴포넌트를 끄지 않는다.

### 🔴 `EffectStagePlayer` — 단계형 이펙트 (차징 구슬 · 앞으로 인터럽트 표시)

`인트로(차오름) → 지속 → 종료(정상/끊김)` 4단 엔트리를 시간에 맞춰 **갈아 끼우기만** 한다.
🔴 **아무것도 애니메이션하지 않는다** — "차오름"은 프리팹이 스스로 하는 일이라
(셰이더 float 0→1 이든 스케일이든) 연출 방식이 바뀌어도 이 코드는 안 바뀐다.

이펙트 재생기 가족이 셋이 됐다. 저 둘이 **어디서**를 달리한다면 이쪽은 **수명**을 나눈다:

| | 무엇 |
|---|---|
| `EffectSocketPlayer` | 트랜스폼 **하나**에 붙어 따라다닌다 |
| `EffectPathPlayer` | 트랜스폼 **배열**을 훑는 반복 펄스 |
| `EffectStagePlayer` | **단계**로 이어지는 루프 (intro / sustain / outro / abortOutro) |

- 🔴 **왜 보스 안이 아니라 별도 컴포넌트인가**: `MonsterBase.Update` 가 `if (!IsServer) return` 이라
  (`MonsterBase.cs:208`) **보스에는 피어 로컬 타이머를 둘 수 없다.** 인트로→지속 전환이 그래서 밖으로 나왔다.
- 지속을 **먼저 켜고** 인트로를 지운다 — 뒤집으면 한 프레임 연출이 통째로 사라진다(레거시가 같은 주석을 달았던 자리).
- 차징 구슬 배선: 시작 `StartChargingInPlace` / 깨짐·사그라짐 `TickCharge` / 체인 중단 `AbortAttackChain`.
  좌표·반경은 **서버가 싣는다** — 장판이 보스 자식이 아니라 별도 NetworkObject 라 클라 스폰 타이밍이 갈린다.
- `ChargeController`(레거시)는 **참조 0건인 죽은 코드다**(전 프리팹·씬 확인). 4개 이펙트 재생 코드가
  통째로 잠들어 있었다. 지울지 참고용으로 남길지 미정.

### 🔴 화면 중앙을 벗어난 대형 VFX 는 **흐려진다** (MaskBlur)

`PC_Renderer` 에 이 브랜치에서 추가된 **`MaskBlurFeature`** 가 화면 중앙의 둥근 사각형 **바깥을 블러**한다
(`Assets/99.Settings/MaskBlurSettings.asset` — center (0.5,0.5) · size (0.45,0.28) · roundness 8.59 ·
darken 0.063). 주입 시점이 `BeforeRenderingPostProcessing(550)` 이라 **투명 이후 = VFX 가 배경과 함께 흐려진다**
(피처 헤더가 그렇게 적고 있다 — 의도된 동작이다).

- 증상: 이펙트가 **화면 중앙 사각형 경계에서 잘린 것처럼** 보인다. 밝고 얇은 선은 살아남고
  넓고 연한 글로우만 뭉개져 사라지므로 "일부만 잘렸다"로 읽힌다.
- 씬 뷰·프리팹 프리뷰에는 렌더러 피처가 안 걸려 **멀쩡하게 보인다** — 이 차이 때문에 오진하기 쉽다.
- 게이트는 `MaskBlurController` 이고 **`4.MapScene` 계열에만 있다.** `BossScene`·`PlayerBossTest`
  에서는 안 돈다 → **같은 이펙트를 두 씬에서 비교하면 즉시 가려진다.**
- ⚠️ 차징 구슬만의 문제가 아니다. 점프 착지 예고·장판·폭발 등 **화면 중앙 밖 대형 VFX 전부**가 걸린다.
  보스 구간에서 컨트롤러를 끄거나(패스 통째로 빠짐, 비용 0) 보스 전용 `MaskBlurSettings` 를
  `SetSettings` 로 물리는 쪽이 후보다. **팀장 판단 대기.**

### 이번에 확립된 계약 (점프어택)

- 🔴 **23호 점프 착지 예고 2개는 이제 `AoeTelegraph` 프리팹이 아니라 카탈로그 루프 이펙트다.**

  | 예고 | 엔트리 | 뜻 |
  |---|---|---|
  | 경계 원(고정 크기) | `EffectCatalog.Drop_Charge_Boundary` | **어디에** 떨어지는가 |
  | 차오르는 원(0.1 → AoE 점증) | `EffectCatalog.Drop_Charge_Indicator` | **언제** 떨어지는가 |

  `TwentyThreeBoss.ShowJumpTelegraphClientRpc` 가 둘 다 `PlayLooping` 으로 빌리고,
  `HideJumpTelegraphClientRpc` 가 반납한다. 구 `JumpController`(레거시 보스)가
  `Drop_Charge_Indicator` 를 쓰던 것과 **같은 관용구**다 — 둘을 따로 만들지 말 것.
- **착지 충돌은 `EffectCatalog.Drop_Collision` 원샷이다** (`PlayJumpImpactRpc`, Unreliable).
  예고 2개와 달리 수명이 사건이 아니라 시간(엔트리 3.5초)이라 **핸들도 회수 책임도 없다.**
  🔴 **RPC 로 나가야 한다** — 호출 지점 `ApplyJumpLandingDamage` 가 `NotifyAttackHit`(`IsServer`
  게이트) 아래라 직접 재생하면 **호스트에서만 보인다**(이 레포의 단골 버그).
  데미지 0 조기 반환보다 **위**에 둔다 — 데미지가 0 이어도 착지는 일어났다.
  전달(Delivery)이 예고와 다른 것도 의도다: 원샷은 유실돼도 이펙트 하나가 빠질 뿐이지만,
  예고 해제가 유실되면 **장판이 바닥에 영구히 남는다** → 그쪽은 Reliable.
- **관용구: 크기는 `scale`, 시간은 `partDuration`, 끝은 `Release`.**
  `scale` 에 **판정 반경을 그대로** 넘긴다(예고가 판정에 대해 거짓말하지 않게).
  `partDuration` 은 **성장하는 쪽에만** 넘긴다 — 경계는 자라지 않아 드라이버에 줄 시간축이 없다.
  수명이 시간이 아니라 "착지"라는 **사건**이라 원샷이 아니라 루프다.
- 🔴 **루프 핸들은 세 곳에서 회수한다** — 재생 직전(재진입 방어) · `Hide` RPC · `OnDestroy`.
  체공 중 보스가 파괴되면 Hide RPC 가 오지 않아 `OnDestroy` 가 마지막 그물이다.
  빠뜨리면 보스가 죽을 때마다 풀에서 두 칸씩 새고 결국 예고가 아예 안 뜬다.
- **회전은 `Quaternion.identity` 다 — 경사면 정렬은 아직 없다.** 하려면 서버가 착지점 노멀을
  RPC 에 실어야 한다(`GroundProbe` 는 서버에서만 돈다). 지금 아레나가 평지라 미뤘다.
- **가드 순서가 바뀌었다.** 예전에는 `jumpTelegraphPrefab` 이 비면 예고 전체가 early return 이었다.
  지금은 두 엔트리가 서로 독립이고, 각각 1회 경고(`WarnNo{Boundary,Indicator}EntryOnce`)를 낸다.

### ⚠️ 이 변경으로 죽은 설정값 4건 (지우지 않고 명시만 했다)

정본 §6 "조용히 무시되는 설정값"을 또 만들지 않으려고 툴팁에 **⚠️ 미사용**을 박았다.
값을 잃으므로 실제 삭제는 팀장 확인 후.

| 대상 | 왜 죽었나 |
|---|---|
| `BossDataSO.jumpTelegraphPrefab` | 예고 2개 모두 카탈로그로 이관 |
| `BossDataSO.jumpTelegraphOuterAlpha` | 진하기가 `FX_Drop_Charge_Boundary` 파티클 저작값으로 |
| `BossDataSO.jumpTelegraphFillAlpha` | 진하기가 `FX_Drop_Charge_Indicator` 파티클 저작값으로 |
| `AoeTelegraph.ShowGrowing` | **호출자 0** (유일한 사용처가 점프 예고였다) |

`AoeTelegraph` 클래스 자체는 **살아 있다** — 송전기 차징 오라(`_chargeAuraTelegraph`)가 아직 쓴다.

### 미결 — Unity 저작 (에디터가 필요해 못 했다)

팔 전기 펄스는 **코드만 있고 아직 화면에 안 나온다.** 프리팹 배선 하나면 된다
(클립 이벤트는 필요 없다 — 코드가 켜고 끈다):

1. `2.Prefabs/Monster/Boss/TwentyThree.prefab` 루트에 **`EffectPathPlayer` 부착** —
   `effect = FX_Grab_ArmElectric_Entry` · `path = [어깨, 팔꿈치, 손]`(예전 배선과 같은 팔 3본, 순서대로).
   `Id` 는 비워도 된다(코드가 직접 참조한다 — 클립에서도 부르고 싶을 때만 채운다).
2. 같은 루트의 **`TwentyThreeBoss.grabPulse`** 필드에 그 컴포넌트를 연결.
   비어 있으면 첫 그랩에서 1회 경고가 뜬다.
3. 상태별 강약(Grab / Holding / Throw)이 필요해지면 그때 `EffectPathPlayer` 를 나눈다.
   지금은 한 벌(travelTime 0.4 / interval 0.6)로 통일했다.
4. **차징 구슬** — 보스 루트에 `EffectStagePlayer` 부착, intro/sustain/outro/abortOutro =
   `ChargeBall_Grow/Loop/FadeOut/Break`, `introDuration` 을 Grow 프리팹 저작 길이와 맞춘 뒤
   `TwentyThreeBoss.chargeBall` 에 연결. 카탈로그 4슬롯은 이미 배선돼 있다.
5. **근접 타격 연출** — 손별 `EffectSocketPlayer` 를 보스 인스펙터의
   `leftHookHit` / `rightHookHit` / `upperHit` 에 물린다. 어퍼가 어느 손이면 그 손 것을 그대로 물리면 된다.
   (`Id` 는 비워도 된다 — 코드가 직접 참조한다. 클립에서도 부르고 싶을 때만 채운다.)
6. ✅ **폭탄 장판 데칼 전환 — 완료**(2026-09-11, 아래 절 참조).

### ✅ 폭탄 장판을 데칼 + 파티클 2층으로 나눴다 (2026-09-11)

바닥에 눌러붙는 원판은 **데칼**, 위로 솟는 것은 **파티클**. 하나였던 `FX_Bomb_Exploded` 를 갈랐다.

| 층 | 어디에 |
|---|---|
| 원판(맥동) | `FireFloor.prefab` → `Visual/Decal` (`DecalProjector` + `ScalePulse`) |
| 불꽃·연기 | `AreaZone.visualEffect` = `FX_Bomb_Exploded_Entry` (`particles` + `fire` 만 남김) |

- 🔴 **`DecalProjector.ScaleMode = InheritFromHierarchy` 가 핵심이다.** 이래야
  `AreaZone.ApplyVisualRadius` 가 이미 하고 있는 `visual.localScale = (지름, 지름, 1)` 이 그대로 먹는다
  (URP `DecalUpdateCachedSystem` 확인: Inherit 면 `localToWorldMatrix`, 아니면 `TRS(pos, rot, 1)` 로
  **스케일을 버린다**). 덕분에 **장판이 자랄 때 데칼은 부드럽게 커진다** — 파티클 층은 배율이
  대출 시점에 확정이라 한 번 끊기는 것과 대조된다.
- 🔴 **`m_RenderingLayerMask = 2`**(`DecalReceivers.Mask`). 1 로 두면 **캐릭터 몸에도 칠해진다.**
- `m_Size = (1, 1, 4)` — XY 는 1(지름이 곱해진다) · Z 는 투영 **깊이**(반경과 무관한 축).
- 맥동은 `ScalePulse`(신규) — `AnimationCurve` 를 그대로 돌린다. Animator·컨트롤러를 안 쓴다:
  루프 상태 하나에 자산 둘과 Animator 평가 비용이 붙는데, 장판은 동시에 여러 개 깔린다.
  커브는 구 `circle` 의 `Size over Lifetime` 키프레임을 그대로 옮겼다(period 0.5 = 그 파티클 수명).
- ⚠️ **커밋이 둘로 갈린다.** `FX_Bomb_Exploded.prefab` 의 `circle` 제거만 `Assets/50.Art` =
  **SVN** 이고, 나머지(`FireFloor.prefab` · `MA_BombFloorDecal.mat` · `SG_ColoredDecal.shadergraph` ·
  스크립트)는 git 이다. 한쪽만 올리면 다른 사람 화면에서 원판이 둘로 보이거나(구 파티클 + 데칼) 아예 없다.

### 🔴 데칼에 색을 넣으려면 셰이더부터 갈아야 한다

**URP 내장 `Decal.shadergraph` 에는 색 프로퍼티가 아예 없다** — 노출 프로퍼티가
`Base_Map` · `Normal_Map` · `Normal_Blend` **셋뿐**이고 `ColorShaderProperty` 는 0개다(패키지 소스 실측).
머티리얼에서 색을 아무리 찾아도 없는 게 정상이고, 텍스처 색이 곧 화면 색이다.

- → `Assets/3.Materials/SG_ColoredDecal.shadergraph` 를 만들었다(패키지 그래프 복사 + `_BaseColor` 추가).
  참조 이름을 **`_BaseColor`** 로 맞춘 것은 의도다 — `AimIndicator` · `SkillRangeIndicator` 가 쓰는 이름이고
  `AoeTelegraph` 도 `Shader.PropertyToID("_BaseColor")` 로 그걸 찾는다.
- 🔴 **`AoeTelegraph.decalColor` 의 RGB 틴트는 지금까지 한 번도 적용된 적이 없다.**
  `MA_AoeDecal_Red` 가 내장 Decal 셰이더를 쓰는데 거기 `_BaseColor` 가 없어서 `HasProperty` 가드에
  걸려 조용히 무시된다(주석은 "decalColor 의 RGB만 틴트로 얹는다"고 적고 있다).
  그 머티리얼을 `SG_ColoredDecal` 로 옮기면 비로소 동작한다 — **보스 예고 장판 색이 바뀌므로 팀장 판단 대기.**

⚠️ **함정: 데칼 그래프에는 `Sample Texture 2D` 노드가 둘이다**(Base Map / Normal Map). 겉모습이 같다.
알파를 **Normal Map 쪽**에서 뽑으면 그 프로퍼티가 비어 있어 샘플이 기본값(알파 1)을 돌려주고,
**데칼이 텍스처 모양 없이 사각형으로 꽉 찬다.** 색이 제대로 나오는데 모양만 사각형이면 여기부터 볼 것.
어느 쪽이 Base Map 샘플인지는 노드의 `Texture` 입력을 따라가 확인한다.

### 미결 — 점프어택

- **Play 육안 확인** — `scale = 반경` 규약이 두 프리팹 저작 크기와 맞는지. 어긋나면 코드가 아니라
  `FX_Drop_Charge_{Boundary,Indicator}.prefab` 쪽에서 잡는다.
- 예고 높이가 아직 +1cm / +2cm 다. 아레나 중앙 바닥판(보행면 +6cm)에는 못 미쳐 묻힌다 —
  데칼·스텐실 작업에서 함께 0 으로 갈 것.

---


## 이전 인수인계 (2026-09-07 · 파괴 가능한 상자 + 파편 버스트, 브랜치 `feature/VFX`)

> 🔴 **이 절과 아래 두 절은 `feature/VFX`에서 옮겨 온 기록이다** (2026-09-08).
> VFX 작업은 `transparentV3` 위로 옮겨 갔다 — 이 브랜치가 그 결과다.
> 아래 본문의 "브랜치 `feature/VFX`"는 당시 기준이고, 코드는 이 브랜치에 있다.
> 옮기면서 확인된 것들:
> - 상자 ID 부여(`MapContentSpawner.AssignCrateIds`)는 손으로 다시 이식했다 — 원본 커밋이
>   충돌 후보로 남아 빠져 있었고, MPPM 2인에서 "호스트만 부서짐"으로 드러났다.
> - 디졸브는 플레이스홀더 끄기와 프리팹 템플릿 배선을 **함께** 넣어야 한다. 한쪽만 넣으면
>   사망 연출이 통째로 없어진다.
> - **보스 프리팹 배선 7건은 이식하지 못했다.** 대상인 `Wells&No.23/TwentyThree.prefab`이
>   `transparentV3`에서 삭제됐다(보스가 `MonsterBase` 계통으로 재작성 중). 그 배선 명세는
>   `feature/VFX`의 커밋 `0f91b658`·`5596d8ef`·`ae111ce7`·`cf360e35`·`58fc9211`·`8f06c17b`·
>   `ed4b7fd5`에만 남아 있으므로 **그 브랜치를 지우지 말 것.**

작업 세션: **민경(Claude)**. 계획·근거는 [PLAN.md](PLAN.md) 최상단 —
**코드 구현 완료, Unity 저작과 MPPM 검증 대기**.

**수정함 (동시 편집 주의)**: `Assets/1.Scripts/Map/Breakable/`(신규 4종) ·
`Assets/1.Scripts/Effects/FragmentBurstEffect{,System}.cs`(신규) · `Effects/EffectManager.cs` ·
`Assets/1.Scripts/fragments/MeshFragmentSet.cs` · `Assets/1.Scripts/fragments/Editor/MeshFragmentSetEditor.cs` ·
`Map/MapContentSpawner.cs` · 🔴 `Unit/Hurtbox.cs` · `Player/PlayerDefaultAttack.cs` ·
`Player/Skill/FirstMelee{Main,Interrupt}Skill.cs`

### 🔴 다른 담당 영역을 건드렸다 — 공유 필요

| 파일 | 변경 | 담당 |
|---|---|---|
| `Unit/Hurtbox.cs` | `attackReceiverSource`(MonoBehaviour) 필드 추가 | 코어(은희) |
| `Player/PlayerDefaultAttack.cs` | 진단 로그 거짓 양성 수정 | Player(은희) |
| `Player/Skill/FirstMelee*Skill.cs` | 중복 방지 셋 `HashSet<Unit>` → `HashSet<Object>` | Player(은희) |

전부 **하위 호환**이다 — `Hurtbox`는 `ownerUnit`이 최우선이라 기존 유닛 경로가 그대로고,
스킬은 게이트만 넓혔다. 그래도 AGENTS.md §4(코어 인터페이스 변경 사전 합의) 대상이다.

### 이번에 확립된 계약

- 🔴 **상자는 `Unit`이 아니다.** `MonoBehaviour, IAttackReceiver`다. 상자 100개면
  NetworkObject 100개 + NetworkVariable 400개인데 **동기화할 상태가 없다** — 사건 하나뿐이다.
  기존 데미지 파이프라인은 Unit 없이 돈다: `BaseAttack`이 Hurtbox를 찾으면 Unit을 안 거치고,
  `Hurtbox`가 `IAttackReceiver`로 폴백한다.
- 🔴 **파괴 단위는 프롭 프리팹(더미) 전체다.** 더미 안 상자들은 Rigidbody 없는 정적
  지오메트리라 아래만 끄면 위가 허공에 뜬다. 게다가 3단 더미는 반듯한 탑이 아니라
  바닥 2 + **걸쳐진** 1이라 지지 관계가 단일하지 않다.
- **판정 콜라이더와 차단 콜라이더를 분리한다.** 루트에 레이어 14 + 트리거 BoxCollider(판정),
  자식 MeshCollider는 원래 레이어 그대로(물리 차단). 자식 레이어를 옮기면 플레이어가 상자를 통과한다.
- **상자 ID = 스폰 순번** `((slotID + 1) << 16) | index`. 맵이 시드 기반 결정적 생성이라
  (`MapNetworkSync` → `MapGenerator`의 격리된 `System.Random`) 모든 피어가 같은 값을 낸다.
  좌표를 RPC에 실을 필요가 없다.
  - ⚠️ **`slotID + 1`이 필수다.** `ZoneSlot.SlotID`는 0부터라 +1이 없으면
    (슬롯 0, 인덱스 0)이 ID 0을 내는데 0은 "미할당" 표식이다.
  - 씬에 **손으로 배치한 상자**는 스포너를 안 거치므로 `authoredId`(음수)를 굽는다 —
    `Tools > Crates > 씬의 상자에 ID 부여`. 저작=음수 / 생성=양수로 공간이 분리된다.
- **파괴 전파는 `CrateBreakBroadcaster` 하나로 모은다.** `MapNetworkSync`와 같은 오브젝트에
  둔다 — 맵이 없으면 상자도 없으니 수명이 맞고, 빠뜨리면 맵 자체가 안 생겨 즉시 드러난다.
  RPC는 **Reliable(기본)**: `DissolveDeath`가 Unreliable인 것과 달리, 유실되면 때린 플레이어
  눈앞에서 상자가 이펙트 없이 증발한다.
- **파괴 이벤트는 둘이다.** `onBrokenLocal`(전 피어, 연출) / `onBrokenServer`(서버 1회, 드롭·보상).
  하나로 두면 연출이 호스트에만 보이거나 드롭이 인원수만큼 중복된다.
- **파편에 콜라이더를 달지 않는다.** 피어마다 다른 난수로 흩어지므로, 충돌시키면 플레이어가
  밀리는 결과가 클라마다 갈려 **연출이 아니라 디싱크**가 된다.

### 🔴 조용한 실패를 만들지 말 것 — 이번에 세 번 겪었다

"때려도 안 부서지는데 로그가 한 줄도 없다"로 세 번 시간을 썼다. 세 지점에 진단을 넣었으니
비슷한 구조를 만들 때 참고할 것.

| 지점 | 증상이었던 것 | 지금 |
|---|---|---|
| ID 0 상자 | 무반응 | Error + 두 원인 모두 안내 |
| 레지스트리 조회 실패 | 무반응 | Warning + 등록 개수 |
| 브로드캐스터 누락 | **호스트만 부서짐** | 네트워크 중이면 Error |

마지막이 특히 위험하다 — **호스트 혼자 테스트하면 정상처럼 보인다.** MPPM 2인을 띄워야 드러난다.

### 미결

- Unity 저작 6건(버스트 프리팹 굽기 · EffectEntry · 프롭 6종 · 브로드캐스터 부착 등) —
  체크리스트는 `PLAN.md`의 "Unity 저작" 절.
- 파편이 바닥을 통과하는 정도가 어색한지 **육안 확인** → 어색하면 `duration`을 0.4~0.6초로.
- ~~`Assets/fragments/`가 아직 git 미추적~~ → **해결.** `Assets/1.Scripts/fragments/`로 옮겨
  git에 추적되기 시작했다(`fe913115`). 이전 경로를 가리키는 문서·주석이 있으면 함께 고칠 것.

---


## 이전 인수인계 (2026-09-07 · 프로파일링 기준 + 파편 폭발 계측, 브랜치 `feature/VFX`)

작업 세션: **민경(Claude)**.

**수정함**: `Assets/1.Scripts/fragments/FragmentExploder.cs` · `Assets/1.Scripts/fragments/Editor/MeshFragmentSetEditor.cs`(이동) ·
`Assets/1.Scripts/Dev/Profiler/Prof.cs` · `Assembly-CSharp*.csproj`.
(당시 `Assets/fragments/`는 git 미추적이었다. 지금은 `Assets/1.Scripts/fragments/`로 옮겨 추적된다.)

### 🔴 에디터 프로파일러의 프레임 총합을 믿지 말 것

플레이 모드 프로파일링에서 **`EditorLoop`이 프레임의 90%를 차지한다**. 실측 2회 모두 동일했다:

| | 에디터 | 빌드본(Development) |
|---|---|---|
| CPU Active | 21.4 ~ 22.9ms | **2.20ms** |
| EditorLoop | 19.4 ~ 20.4ms | 없음 |
| Scripts | 0.9 ~ 1.1ms | — |

`EditorLoop`은 Scene 뷰·인스펙터·**프로파일러 창 자신**의 리페인트다(`Profiler.ParseThreadData`가
top marker에 뜬다). 게임을 느리게 만드는 게 아니라 프레임에 남의 일을 얹는 것이므로,
**"프레임이 튄다"의 원인 지목에 쓰면 반드시 오진한다.**

- **믿을 수 없는 것**: CPU Active Time · 프레임 타임 · FPS · 그래프 스파이크
- **대체로 믿을 수 있는 것**: Scripts / Physics / Rendering 버킷 · GC Alloc · 마커별 ms
- **예산 판정(16.67ms)은 반드시 Development Build에서 한다.** Deep Profiling Support는 끌 것.
- 에디터에서 봐야 하면 최소한 **Maximize On Play** + F2 HUD(`HitVFXDebugHUD`) 끄기 +
  Profiler Frame Count를 300으로. 버퍼에 9만 프레임이 쌓이면 창 자체가 스파이크 원인이 된다.

빌드 산출물: `D:\김민경\유니티\MainProject\Build\` (프로젝트 밖, D 드라이브).

### 🔴 에디터 전용 스크립트를 `Editor/` 밖에 두면 플레이어 빌드가 깨진다

증상이 **`BuildFailedException: Failed to build Addressables content ... "SBP ErrorError"`** 로 뜬다.
Addressables는 무죄고, 진짜 원인은 Editor.log의 그 위에 있는 `CS0246`이다 —
스크립트 컴파일이 먼저 실패했고 Addressables 전처리기가 뒤이어 예외를 던진 것뿐이다.

`MeshFragmentSetEditor.cs`가 `Assets/1.Scripts/fragments/`(= `Assembly-CSharp`)에 있어서 이랬다.
에디터에서는 `Assembly-CSharp`이 `UnityEditor.dll`을 참조하므로 멀쩡히 컴파일되고,
**플레이어 빌드에서만** 터진다. `Assets/1.Scripts/fragments/Editor/`로 옮겨 해결.
**빌드 실패 시 다이얼로그 메시지보다 `%LOCALAPPDATA%\Unity\Editor\Editor.log`를 먼저 볼 것.**

### FragmentExploder 실측 — 재조사 불필요

크레이트 **5개 동시 폭발**(파편 75개) 한 프레임, Development Build 기준:

```
Fragment.Explode      Calls  5   0.32ms   ← 60fps 예산의 1.9%
├ Fragment.Activate   Calls 75   0.19ms   (SetActive의 렌더러 등록 + PhysX actor 생성. Self는 0.01ms)
├ Fragment.OnExploded Calls  5   0.07ms   (이펙트 재생. 할당 160B는 전부 여기 UnityEvent)
└ Fragment.Forces     Calls 75   0.04ms
```

**프레임 드랍의 원인이 아니다.** 상시 비용도 0(`Update` 5콜 0.00ms / 0B).
Rigidbody 제거 같은 최적화는 **하지 말 것** — 0.19ms를 위해 검증된 코드를 다시 쓰는 건 손해다.

- 마커는 [`Prof.cs`](Assets/1.Scripts/Dev/Profiler/Prof.cs)에 있다(`Fragment.*`). 프로파일러가
  안 붙어 있으면 비용이 없으므로 릴리스에도 그대로 둔다. 검색어는 `Fragment.`(점 포함) —
  점이 없으면 `FragmentExploder.Update()`가 같이 걸려 노이즈가 된다.
- `fragmentCollision` 기본값 **false**(파편은 연출이지 게임플레이가 아니다). 끄면 파편이
  바닥을 통과하므로 `debrisLifetime`이 짧아야 한다 — **Play 육안 검증 대기**.
- 콜라이더를 다시 켜면 충돌 비용은 `Explode()` 프레임이 아니라 **이후 FixedUpdate의 `Physics`**에
  잡힌다. `Fragment.*` 마커에는 안 나타난다.

---


## 이전 인수인계 (2026-08-30 · 사망 디졸브 연출, 브랜치 `feature/VFX`)

작업 세션: **민경(Claude)**. 계획·근거는 [PLAN.md](PLAN.md) 최상단 항목(**승인 대기**).

**수정 예정 (동시 편집 금지)**: `Assets/1.Scripts/Monster/DissolveDeath.cs`(재작성) ·
`Assets/1.Scripts/Enemy/Boss/BossDeathEffectBinder.cs`(신규) ·
`Assets/2.Prefabs/Monster/*.prefab`(8종 배선) · `Assets/2.Prefabs/Wells&No.23/TwentyThree.prefab` ·
`Assets/TheVayuputra/DissolveShader/`(머티리얼 템플릿·파티클 사본)

이번에 확인된 계약만 적는다:

- 🔴 **몬스터와 보스는 사망 경로가 다르다.** 몬스터 = `MonsterBase.EnterDead()` → `IDeathEffect.Play`
  → `DespawnNow`. 보스(TwentyThree) = `Enemy : Unit`이라 그 훅이 **없고**, `Unit.Died`(서버 전용)만
  있으며 **디스폰되지 않는다** — `BossEncounterDirector`가 `defeatResultDelaySeconds`(=3) 뒤
  결과 씬으로 넘긴다. 보스 사망 연출은 3초 안에 끝나야 한다.
- 🔴 **위 보스 경로는 한시적이다.** 기존 보스(`Enemy : Unit` 계열)는 **폐기되고, 일반 몬스터와
  똑같은 방식으로 새로 만들어진다** — 즉 `MonsterBase`/`BossBase` 계통으로 옮겨가
  `EnterDead()` → `IDeathEffect.Play` 경로를 그대로 탄다. 그러면 지금의 두 갈래가 하나로 합쳐진다.
  **따라서 보스 전용 우회로를 늘리지 말 것.** `BossDeathEffectBinder`는 그때까지만 필요한
  어댑터이고, 보스가 새로 만들어지는 시점에 통째로 지운다(`DissolveDeath` 자체는 그대로 쓴다).
- 🔴 **`IDeathEffect.Play`는 서버에서만 불린다.** 여기서 바로 연출을 재생하면 호스트에서만 보인다
  (이 레포의 반복 버그. `PLAN.md` 08-11 §A). 연출은 RPC로 전 피어에 퍼뜨리고 각 피어가
  자기 로컬 렌더러로 그린다 — 좌표는 싣지 않는다.
- **`DissolveFx` 셰이더의 `_Cutoff`는 1 = 보임, 0 = 사라짐**이다. 기존 `DissolveDeath`
  플레이스홀더의 `_DissolveAmount` 0→1과 방향이 반대다.
- **몬스터 albedo는 사실상 하나다**(`T_RobotTexture.png`, 예외는 `M_SpinBotBlades`).
  그래서 캐릭터별 디졸브 머티리얼을 만들지 않고 **템플릿 1개 + 런타임 `_BaseMap` 복사**로 간다.
- 🔴 **`sharedMaterial`을 쓰지 말 것.** 플레이 모드 변경이 `.mat` 에셋에 눌러앉아 그대로 커밋되면
  팀 전체 캐릭터 머티리얼이 바뀐다. `renderer.material`(인스턴스) 또는 MPB만 쓴다.
- **몬스터 렌더러는 프리팹 루트에 없다** — 중첩 모델 프리팹/FBX 안에 있어 인스펙터 배선이 어렵다.
  런타임에 `GetComponentsInChildren<Renderer>(true)`로 수집하고, 파티클 Shape(메쉬 방출)도
  코드로 지정한다.

---


## ▶▶ 다음 세션 시작점 — 어그로 후속 3건 Play 재검증 (2026-09-03 종료)

**브랜치 `feature/Boss23`** · 컴파일 0에러 · EditMode **112/112**(전체 · 신규 `BossContactReachPolicyTests` 14건 포함) · 원격 미푸시

### 한 줄 상태

어그로 재선정을 단독 Play 로 보니 결함 3건이 나왔다(착지 후 8초 미적용 · 허공 훅 · 점프 후 어그로 복귀).
셋 다 원인을 코드에서 확정해 고쳤다. 남은 것은 **Play 재검증**이다 — 계획은 [PLAN.md](PLAN.md) 상단.

### 🔴 다음 할 일 — 단독 Play 4건 + MPPM

| 확인 | 기대 동작 |
|---|---|
| 착지 후 어그로 | **8초가 지나서야** 첫 재선정. 내려오자마자 튀지 않는다 |
| 훅 개시 거리 | **2.0m 안까지 걸어 들어간 다음** 훅. 가만히 선 대상에게 허공 훅이 없다 |
| 점프 후 어그로 | 후열을 때린 뒤 **그 대상에게 압박이 남는다**(원래 대상으로 안 돌아간다) |
| 돌진 캐리 | **밀고 간 플레이어**가 어그로를 가져간다 |
| MPPM 2인 | 카운터 자세 홀드 호스트/클라 일치(계획서 Task 6) + 어그로 노브 A/B |

🔴 MPPM 이 아니면 안 드러나는 것 — **카운터 자세 홀드는 각 피어의 로컬 애니메이터 상태다.**
단독 Play 로는 호스트/클라 불일치가 보이지 않는다(계획서 Task 6 의 핵심).

⚡ **테스트 단축키 — `F5` = 보스방 진입 패드로 순간이동**(개발용, 2026-09-03 추가).
스폰 지점에서 걸어가는 시간만 없애고 이후 흐름(카운트다운 → 산개 → 등장 연출)은 그대로 돈다.
씬 배치 없이 런타임에 스스로 붙는다(`Assets/1.Scripts/Dev/DevBossEntranceWarp.cs`).
⚠️ 빌드에서 쓰려면 **Development Build 를 켜야** 한다(`DEVELOPMENT_BUILD` 없으면 클래스 자체가 없다).
쓰는 키 현황 — F1·F2 이펙트 HUD · **F5 보스방 워프** · F8 프로파일러 · F9 룩 A/B · F10 디버그 부활 · M 맵 · `[` `]` 카메라.

승계는 로그로 확인된다 — `[23호/어그로] 점프 조준 → ... 로 승계` · `돌진 캐리 → ...`.
⚠️ Play 로그는 MCP 콘솔이 아니라 `Editor.log` 로 본다(콘솔 버퍼 100개 — `droppedCount` 를 항상 볼 것).

### 이번에 고친 것 — 증상 → 원인

| 증상 | 원인 | 수정 |
|---|---|---|
| 내려오자마자 어그로가 튄다 | `_lastRetargetTime` 초기값 **0** → 착지 시점엔 `Time.time - 0` 이 이미 8초를 넘어 있다 | `OnServerLogicResumed`(= 착지·NavMesh 스냅 완료 지점)에서 시계 리셋 |
| 훅 거리도 안 됐는데 때린다(허공) | 훅 거리창 3.2 > `attackRange` 2.0. `SeekBoss` 는 슬롯이 잡히는 즉시 멈춰 때린다 | `BossContactReachPolicy` — 접촉 공격은 `attackRange` 안에서만 **개시**, 붙은 뒤에는 저작값까지 유지 |
| 점프로 후열을 때리고 원래 대상으로 돌아온다 | `ResolveAttackTarget` 이 **조준만** 정했다. 게다가 착지 시점에 주기 시계가 만료 상태였다 | `MonsterBase.AdoptTarget` 진입점 + 점프·돌진에서 승계 + 시계 리셋 |

🔴 1번과 3번은 **같은 뿌리**였다 — 주기 시계가 "전투 시작"도 "어그로가 바뀐 순간"도 모르고 있었다.
어그로 기능을 얹을 때 **시계를 어떤 사건에 묶을지**를 함께 정하지 않으면 이 두 결함이 같이 나온다.

### 튜닝 노브 — `No23.asset` · `No23_Solo.asset`

| 노브 | 현재 | 비고 |
|---|---|---|
| `aggroRetargetInterval` | 8초 | 0 이면 기능 끔. 착지·승계 시점부터 센다 |
| `aggroAvoidsRepeatTarget` | No23 **켬** / Solo 끔 | MPPM 으로 확정할 값 |
| 훅 `maxDistance` | 3.2 → **2.6** | **잠정값.** 개시는 게이트(2.0)가 정하고 이 값은 "붙은 뒤 유지 상한"만 정한다 |
| 어퍼 `maxDistance` | 2.8 → **2.4** | 같음 |
| `chargeClearGroggyDuration` | 1.5초 | 송전기 전멸 보상 |
| Grab / Dash `counterWindowDuration` | 1.3 / 1.5 | Grab 준비 도달이 1.11초라 그보다 커야 홀드가 생긴다 |

🔴 `BossCounterDataTests` 는 **관계만** 고정한다(접촉 행 `maxDistance >= attackRange`). 거리창 값과
`aggroAvoidsRepeatTarget` 은 A/B 중이라 기대값으로 박지 않았다 — 박으면 튜닝마다 거짓 빨간불이 난다.

### 어그로 계약 (변동 없음)

- ✅ **어그로는 피해 분배를 바꾸지 않는다.** `_target` 은 "어디로 가고 누굴 보는지"만 정하고,
  누가 맞는지는 공간 판정이 따로 고른다(훅은 히트박스에 겹친 전원, Grab 은 포획 순간 반경 내
  최근접, Dash 는 경로에 먼저 걸린 사람).
- ✅ 주기 재선정은 `Idle`/`Chase` 에서만 성립한다(공격 중 타깃이 바뀌면 조준·체인이 흔들린다).
  **승계는 예외다** — 그 공격이 대상을 고른 순간이 승계 시점이라 공격 도중에 일어난다.
- ✅ **체인 예산은 데드락 안전망이지 정밀 종료 기준이 아니다**(`ChainBudgetSlack` 0.5초).

### 별건 (이번 작업과 무관, 미해결)

- ✅ **바닥 표식·장판을 URP 데칼로 전환 — 1·2단계 완료** (2026-09-04, 팀장+팀원 육안 판정 통과)
  - 확정 스펙: **캐릭터 아래 · 바닥과 장애물 위 · 오프셋 0**(높이로 띄우면 탑다운에서 밀려 보인다 =
    "예고가 판정에 대해 거짓말한다"). 전환 대상은 **전/후방 표식 · 차징 오라 · 점프 예고**.
  - 전환된 것: 표식(`BossDirectionIndicator` 데칼 경로) · 오라·점프 예고(`AoeTelegraph` 데칼 경로 +
    `AoeDecalTelegraph.prefab`). 아크·원은 **코드 생성 텍스처**라 아트 작업이 0이다.
  - 🔴 **되돌리기가 한 칸이다** — 표식은 `decalMaterial` 을 비우면 메시, 장판은 SO 프리팹 필드를
    옛 것으로. 두 경로가 코드에 공존한다.
    - ⚠️ **점프 예고는 2026-09-09 에 이 구도에서 빠졌다** — `EffectCatalog.Drop_Charge_*` 루프
      이펙트로 이관돼 `jumpTelegraphPrefab` 을 되돌려도 아무 일도 안 난다. 오라·표식은 그대로.
  - 🔴 실측으로 확정된 것 3개(다시 파지 말 것):
    ① 활성 렌더러는 `PC_Renderer.asset` 이고 **데칼 피처가 이미 켜져 있다**(`PP_Renderer`·`PP.asset` 은 죽은 애셋).
    ② `m_SupportsLightLayers: 1` + 모든 라이트가 bit 0 → **캐릭터를 다른 비트로 옮기면 어두워진다.**
    그래서 수신자(바닥·프롭)에 비트를 **OR 로 추가**한다(`DecalReceivers` · 지우기 금지).
    ③ 아레나는 스폰 존이 아니라 **맵 밖 x≈500 씬 고정 배치**다 — 수신자 표시는
    `BossArenaDecalReceiverInstaller`(런타임 자체 설치 · 전 피어)가 랜드마크 이름으로 찾아서 한다.
  - ⚠️ 내장 데칼 셰이더는 **알베도**를 칠해 조명을 탄다(메시는 Unlit 이었다). 알파 0.85 로 우회했고,
    어두운 구역에서 문제가 재발하면 **Emission 커스텀 Decal Shader Graph** 로 가야 한다.
  - 🔴 **VFX 로드맵 경계**(팀장 2026-09-04): 불장판은 **이펙트로 대체 예정이라 데칼로 안 옮긴다.**
    차징 오라는 이펙트로 바뀔 수 있고, 점프 예고는 현 상태 유지 + **착지 시 이펙트**.
    인수인계 지점은 `ApplyJumpLandingDamage` 의 `HideJumpTelegraphClientRpc()`(= 예고 종료 =
    착지 이펙트 시작)와 `Show/HideChargeAuraClientRpc` 다. 자세한 표는 [PLAN.md](PLAN.md).
- 🔴 **보스방에 있던 클라가 "게임 시작 스폰 위치"로 복귀했다** (팀장 빌드 관찰 2026-09-04 · 원인 미확정)
  - 2인 입장 후 잠깐 안 보는 사이에 **한 클라만** 보스방이 아니라 진입 시 스폰 위치로 돌아갔다.
  - 팀장 회상: **낙사 복귀**에서도 "저장했던 위치가 아니라 스폰 위치로 돌아가는" 경우가 있었다 →
    같은 뿌리(복귀 지점 선택)일 가능성이 있다.
  - 🔴 **진단 데이터가 없다.** Development Build 의 `Player.log`(1.6MB)에 `BossTeleport`·스폰·복귀
    키워드가 **0건**이다 — 그 계통이 전부 `Edit.Log` 로 찍고, `Edit.*` 는 `[Conditional("UNITY_EDITOR")]`
    라 **빌드에서 호출이 사라진다**(교훈 #86). 다음에 재현하려면 **먼저 로거를 고쳐야 한다** —
    `Edit` 에 `[Conditional("DEVELOPMENT_BUILD")]` 를 함께 붙이면 호출부 수정 0으로 개발 빌드에서 살아난다.
  - 그 로그에 남아 있던 예외 2건은 별건이다: `ResultSceneManager.Start:24` · `LobbySceneManager.Start:77`
    NullReference (로비·결과 씬 UI — 은희님 영역).
- **스피너봇 평타 조기 판정** — 애니 시작 즉시 데미지가 나가 칼이 안 맞았는데 피격된다.
- ✅ **23호 프리팹 중복 정리 완료 (2026-09-04).** 낡은 사본 `Assets/2.Prefabs/Wells&No.23/TwentyThree.prefab`
  (guid `1100cccaacdc1fe4ca3e1eb9680f8c75` · 앵커가 옛 규약 `LeftHookAttack`/`UpperAttack` 등)을 삭제하고
  `DefaultNetworkPrefabs.asset` 등록 목록에서도 뺐다(참조는 자기 `.meta` 와 그 목록 2곳뿐이었고 씬 배치 0건).
  **23호 프리팹은 이제 `Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab` 하나뿐**이다
  (앵커 `Hand_L`/`Hand_R`/`DashBody` = `No23.asset` 의 `hitboxAnchorName`).
  `TwentyThree_Solo.prefab` 은 에디터 오소링이 쓰는 별개의 정상 변형이니 건드리지 말 것.
  🔴 **검증 미완 — NGO 네트워크 프리팹 목록이 바뀌었다. MPPM 2인 접속 + 보스 스폰까지 한 번 확인해야 한다.**
- ⚠️ **Unity Hub 를 오래 켜 두면 그 안의 Unity 가 낡은 PATH 를 물려받는다**(교훈 #81).
- ⚠️ **배치모드 테스트는 `-quit` 를 주면 안 된다** — 테스트 전에 종료돼 XML 이 안 나오고
  로그만 "Exiting batchmode successfully" 로 끝난다(거짓 초록의 전형). 에디터가 열려 있으면
  프로젝트 잠금 때문에 아예 안 돈다(return code 1).

## 이전 시작점 — 넉백 세기 튜닝 (2026-08-18 밤 종료)

**브랜치 `feature/Boss23`** · **컴파일 0에러** · **원격 동기화 완료(ahead 0 / behind 0)**
**백업 = 로컬 `backup/boss23-20260815`**

**한 줄 상태: 존 몬스터 스폰이 맵 경로에서 처음으로 돈다(Play 확인). 빌드도 뽑았다.
남은 것은 넉백 세기 튜닝이다.**

### 🔴 다음 할 일 — 넉백 세기 튜닝 (이월)

팀장 판단: **넉백이 조금 과하다.** 전부 `No23.asset`(단독 변형은 `No23_Solo.asset`) 노브다.

| 노브 | 현재 | 절반 기준 출발점 |
|---|---|---|
| `dashKnockbackStrength` | 12 | 6 |
| `jumpKnockbackStrength` | 9 | 4.5 |
| `chargeAuraKnockbackStrength` | 8 | 4 |

⚠️ **강도 노브만 있다.** 진입점 `Unit.Knockback(방향, 강도)` 이 강도만 받으므로 지속·경직 노브는
**일부러 두지 않았다** — 노출해도 아무 일이 없는 "고장난 노브"가 되기 때문이다.

### ✅ 2026-08-18 밤에 닫은 것 — 존 몬스터 스폰 (Play 검증 완료)

**아트가 마커를 저작하면 실제 맵에 몬스터가 나온다.** 이번에 처음 연결됐다.

| 커밋 | 내용 |
|---|---|
| `20d657a`·`07ffe1f` | 아트(이지원) — 존 9종에 `MonsterSpawnPoint` 50개 + 씬/NetworkPrefabs |
| `6106636` | 맵 생성 경로가 존의 `MonsterSpawner` 저작을 실행하게 |
| `8d2c3d1` | 존 8종에 `MonsterSpawner` 배선 + 저작 도구 |

**저작 조합 = `MonsterSpawner`(존 루트) + `MonsterSpawnPoint`(자식 마커).**
지점 `Override` 가 있으면 그것, 없으면 스포너의 기본 몬스터.

| 존 | 기본 | 마커 | |
|---|---|---|---|
| `ZoneL_typeA/B/C` | MortarBot | 7/7/7 | 각 1지점 Gauntlet·Wall·Humanoid |
| `ZoneM_typeA/B` | PeekABot | 5/6 | |
| `ZoneS_typeA`·`Quest01/02` | ChompBot | 5/5/4 | |
| `ZoneS_typeStart` | — | 4 | 🔴 **일부러 제외**(시작 지점) |
| `ZoneS_typeBossEnter`·`ZoneM_typeC` | — | 0 | 마커 없음 |

🔴 **`spawner.SpawnWave()` 를 부를 수 없다 — 이번 세션의 핵심 교훈.**
`NetworkBehaviour.IsServer` 는 계산 프로퍼티가 아니라 **네트워크 스폰 때 세팅되는 자동
프로퍼티**다(`NetworkBehaviour.cs:476`). 존은 「비네트워크 규약」이라 `Spawn()` 되지 않으므로
존에 붙은 스포너의 `IsServer` 는 **영원히 false** 이고 `SpawnWave()`·`SpawnAt()`·`SpawnOne()`
이 전부 첫 줄에서 return 한다. **존에 `NetworkObject` 를 붙여도 아무도 Spawn 해 주지 않아
결과가 같다.** → `MapContentSpawner.SpawnFromZoneSpawner` 가 마커만 읽어 서버에서 직접 스폰한다.

⚠️ **자동 수집은 `MonsterSpawner.ResolveSpawnPoints()` 로 뺐다.** 원래 `OnNetworkSpawn` 안에만
있어서 네트워크 스폰되지 않는 오브젝트에서는 수집조차 돌지 않았다. `Spawn Points` 목록은
**비워 두는 것이 규약**이다 — 채우면 아트가 마커를 추가해도 반영되지 않는다.

**저작 도구** = `Tools/Map/Authoring/존 몬스터 스포너 배선` (적용 + 읽기전용 검증). 멱등하다.
기본 몬스터를 바꾸거나 존이 늘면 **손으로 만지지 말고 이걸 돌린다.**

### ✅ 함께 닫은 것

| 커밋 | 내용 |
|---|---|
| `1048386` | 보스 회전 감속 + 선딜 조준 (Play 검증) |
| `370e7df` | 착지 직후 첫 돌진 미이동 — 연출 중 FSM 정지 (Play 검증) |
| `f96fbd4` | 빌드 게이트가 정본 전투 맵(`-trensparent`)을 요구하게 |
| `57c58dc` | 로비 Relay 조인코드가 **흰 배경에 흰 글씨**라 안 보이던 것 |

### 🔴 이번 세션에 확정된 함정 (다음 사람이 밟을 지점)

- **정본 존 프리팹은 `Assets/2.Prefabs/Map/Zoneprefab/` 이다.** 판단 기준은 "어느 프리팹에
  컴포넌트가 붙어 있나"가 아니라 **`ZoneLayoutCatalog` 가 무엇을 가리키나**다.
  `LevelDeliveryV3/Zones/PF_Zone_*_V3` 는 `ZoneLayout` 저작이 들어 있지만 **카탈로그에 없어
  맵에 안 나온다.** 내가 이걸 거꾸로 읽어 문서를 두 번 다시 썼다.
- **`ZoneLayout`·`NodeMarker`·`MonsterSpawnEntries` 경로는 지금 안 쓰인다.** 정본 존 11개에
  `ZoneLayout` 이 아예 없어 `MapContentSpawner.SpawnMonstersFor` 가 항상 0 을 반환한다.
  `MonsterSpawnEntries` 는 **스크립트가 아니라 `ZoneLayout.cs:43` 의 필드**다(Add Component 에 안 뜬다).
- **맵 생성은 `MapGenerator.Start()` 가 아니라 `MapNetworkSync.OnNetworkSpawn()` 이 한다.**
  씬의 `AutoGenerateOnStart: 0` 이고 `Start()` 는 `NetworkManager.IsListening` 이면 early return 한다.
  서버가 시드를 뽑아 복제하고 클라가 같은 시드로 각자 생성한다.
- **빌드 산출물의 `MainProject.exe`·`UnityPlayer.dll` 은 내용이 안 바뀌면 갱신되지 않는다.**
  실제 콘텐츠는 `MainProject_Data/`(`level0`~ · `sharedassets*`)다. exe 타임스탬프로 빌드 여부를
  판단하면 안 된다.
- **인스턴스 2개를 띄우면 `Player.log` 는 먼저 잡은 쪽만 쓴다.** 나중에 뜬 쪽 로그는 남지 않는다.
  로그로 "인스턴스가 몇 개였나"를 추론할 수 없다.

### 🔴 남아 있는 팀장 액션 3건

1. **은희 님께 플레이어 수정분 push 요청** + `AudioManager` NRE 전달
   (`LobbySceneManager.Start()` **마지막 줄**이라 BGM 만 안 나오고 흐름은 정상)
2. **로비 UX** — 호스트가 조인코드를 정할 수 없는데(`StartRelayHost` 가 입력칸을 안 읽는다)
   입력칸이 열려 있어 "입력하면 방 코드가 되겠지"로 읽힌다. 호스트일 때 비활성화 + 발급 코드
   자동 채움이 맞다. 은희 담당
3. **`layprefab.prefab` 의 `LaserBlockWall` 걷어내기** — ⚠️ Quest 통로 차단 대체 구현 없음

### 아트 전달용 문서

정본 = [zone-monster-spawn-authoring.md](Docs/design/zone-monster-spawn-authoring.md)
넘길 사본(md+html) = `C:\Unity\_Handoff\zone-monster-spawn\` (레포 밖, 스냅샷)

---

## ✅ 2026-08-23 — 인프라 세션 (MCP 패키지 · SVN · git 위생). 게임플레이 변경 없음

**게임 코드·프리팹·씬은 건드리지 않았다.** 위 넉백 세기 튜닝이 여전히 다음 할 일이다.
바뀐 것은 MCP 패키지, 아트 워킹카피 위치, git 줄바꿈 처리다.

### 🔴 이번 세션에 확정된 함정 (다음 사람이 밟을 지점)

- **`Assets/50.Art` 는 정션이고, 도구마다 보이지 않는다.** `find` · PowerShell
  `Get-ChildItem -Recurse` · Node `readdirSync` 는 이 폴더를 **통째로 건너뛴다**
  (Dirent 가 `isDirectory()=false` / `isSymbolicLink()=true` 라 두 분기 모두 빠진다).
  Python `os.walk` 만 따라간다. MCP 프로젝트 인덱스가 이 이유로 프로젝트 `.meta` 의
  **35%(1,105개)** 를 못 보고 있었다. 아트 자산을 세거나 훑는 계측·스크립트는 이걸 먼저
  확인할 것. **`filesFailed: 0` 같은 통계는 근거가 못 된다** — 건너뛴 것은 실패로 세지 않는다.
- **아트 변경은 git 에 안 보인다.** `Assets/50.Art` 는 `.gitignore` 대상이고 실제 관리는
  **SVN** (`https://svna.gameinjae.kr/svn/GA7thFinal_VeyTrace`) 이다. "아트가 뭘 바꿨나"는
  `git status` 가 아니라 `svn status -u` 로 본다. svn CLI 를 이 PC 에 설치했다
  (`C:\Program Files\SlikSvn\bin\svn.exe`, PATH 등록).
- **아트 워킹카피를 옮겼다.** `OneDrive\바탕 화면\GA7thFinal_VeyTrace` →
  **`C:\svn\GA7thFinal_VeyTrace`**. 개인 OneDrive 가 `.svn/wc.db`(36 GB)를 동기화하면
  워킹카피가 깨진다. 정션도 새 경로로 다시 걸었다. 예전 경로를 박아둔 스크립트가 있으면 고칠 것.
- **`git status` 의 `M` 이 내용 변경을 뜻하지 않는다.** `ProjectSettings/*.asset` 등 5건이
  수정됨으로 떠 있었는데 `git diff --stat` 은 **변경 0줄**이었다. Unity 는 LF 로 쓰고
  `core.autocrlf=true` 는 CRLF 로 체크아웃하기 때문이다. **버리기 전에 `git diff --stat` 을
  먼저 보라** — 그 안에 진짜 변경이 섞여 있을 수 있다(실제로 `UnityConnectSettings.m_Enabled`
  0→1 한 건이 섞여 있었다). 원인은 `.gitattributes` 에 `eol=lf` 를 걸어 없앴다.
- **MCP 패키지 핀은 안정 브랜치에서 도달 가능한 커밋이어야 한다.** feature 브랜치만 가리키는
  커밋을 핀한 뒤 그 브랜치를 지우면 팀원 Unity 가 패키지를 해석하지 못한다. 그래서 포크의
  작업 브랜치를 `optimized` 로 합치고 핀을 그 계보로 옮겼다.

### ✅ 닫은 것

- MCP 코드 2건을 게임 레포에서 패키지로 회수(`.mcp-bridge-launcher.js`,
  `UnityMcpBehaviorGraphTools.cs`). **툴 이름 6개는 그대로** — 호출부는 안 깨진다
- 패키지 핀 `2ea969e` → `633c7f5` (dev-0.0.1). 실측 기록은 패키지 `README_ko.md` 참조
- 패키지 버그 2건 수정: 정션 스캔 누락(깨진 스크립트 탐지 9→13건), 런처 경로 탐색
- `.gitattributes` 에 Unity YAML 자산 `eol=lf` (바이너리 `.asset` 오버라이드는 그대로 유지)
- `ProjectSettings/ScriptableBuildPipeline.json` 팀 공유 시작 (Addressables 빌드 설정)
- SVN r283 → r286 동기화. 받을 것·커밋할 것 없었다 (아트는 r283 이후 변경 없음)

### 🔴 팀원에게 알려야 할 것

MCP 를 쓰는 사람은 **설정을 한 번 고쳐야 한다.** 런처가 레포 루트에서 사라져 패키지 안으로
갔다. 절차는 패키지의 `LOCAL_DEV_SETUP.md` 에 있다.

## 이전 시작점 — 회전 감속·첫 돌진 마감 (2026-08-18 낮)

**브랜치 `feature/Boss23`** · **컴파일 0에러** · **`origin/feature/Boss23` 과 동기화 완료(ahead 0 / behind 0)**
**백업 = 로컬 `backup/boss23-20260815`**

**한 줄 상태: 팀장 지시 2건(회전 감속·첫 돌진)을 닫고 Play 로 확인받았다. 원격에도 올라갔다.
다음은 넉백 세기 튜닝이다.**

### 🔴 다음 할 일 — 넉백 세기 튜닝

팀장 판단: **넉백이 조금 과하다.** 전부 `No23.asset`(단독 변형은 `No23_Solo.asset`)의 노브다.

| 노브 | 현재 | 절반 기준 출발점 |
|---|---|---|
| `dashKnockbackStrength` | 12 | 6 |
| `jumpKnockbackStrength` | 9 | 4.5 |
| `chargeAuraKnockbackStrength` | 8 | 4 |

⚠️ **강도 노브만 있다.** 진입점인 `Unit.Knockback(방향, 강도)` 이 강도만 받으므로 지속·경직 노브는
**일부러 두지 않았다** — 노출해도 조절이 아무 일도 안 하는 "고장난 노브"가 되기 때문이다.
지속·경직까지 만지려면 `Unit` 쪽 계약을 바꿔야 하고 그건 은희 담당 경계다.

### ✅ 2026-08-18 낮에 닫은 것 (커밋 3개 · Play 검증 완료 · 푸시 완료)

| 커밋 | 내용 |
|---|---|
| `1048386` | 보스 회전 감속 + 선딜 조준 |
| `370e7df` | 착지 직후 첫 돌진 미이동 — 연출 중에는 FSM 을 세운다 |
| `f10551b` | 문서 갱신(팀장 문답 2건 기록) |

**1. 보스 회전 감속** — `MonsterDataSO.turnSpeed` 신규. **0 = 즉시 회전**이라 몹 8종·중간보스 3종은
무영향이고, `No23`·`No23_Solo` 만 **10**(플레이어 `rotate_Speed` 와 동일값)이다.
회전 단일 지점 = `MonsterBase.RotateToward()` — `FaceTarget`·`FaceVelocity` 가 전부 통과한다.

🔴 **팀장 확정: 조준도 감속 + 선딜 동안만 회전 허용.** 감속 회전은 매 틱 불러야 도달하는데
23호의 조준은 `StartAttack` 직전 **1회**뿐이라 그대로 두면 엉뚱한 데를 때린다. 그래서
`FaceTargetDuringWindup` 훅(기본 false)을 만들어 **히트 이벤트 전까지**를 조준 구간으로 썼다.
"공격 중 회전 없음"(`FaceTargetWhileAttacking => false`) 규칙 자체는 그대로다.
⚠️ 잡기 되먹임은 여전히 안전하다 — 되먹임은 플레이어가 손 소켓에 붙은 **뒤**(= 히트 시점) 생긴다.

🔴 **`FaceTargetImmediate()` 예외가 하나 필요했다** — `BeginRageDash` 는 `FaceTarget()` **바로 다음 줄**에서
`transform.forward` 를 돌진 방향으로 굳힌다. 감속을 쓰면 그 프레임의 어중간한 각도가 박힌다.
**"돌아본 뒤 그 방향을 즉시 소비하는" 자리**는 앞으로도 이 함수를 써야 한다.

**2. 착지 직후 첫 돌진 미이동 — 돌진 코드의 결함이 아니었다**

🔴 **원인 = 연출 구간에 FSM 이 돌아서 다리 없이 공격을 시작한 것.**

```
Spawn()                → FSM 살아남(_initialized = true)
agent.enabled = false  → 다리 없음 (하강 연출 보호, Director:344)
  … 하강 1.2초 … 착지 … impactHold 0.9초 …   ← 이 2초 넘는 구간에서 FSM 이 공격을 고른다
BeginCombatServer → SnapBossToNavMesh → agent.enabled = true
```

No23 `detectionRadius` 8m 라 하강 막바지에 플레이어가 인지 반경에 들어온다.
그 구간에 돌진이 걸리면 `StartDashMove` 가 에이전트를 못 찾아 **조용히 return** 하고
목적지가 제자리로 남아 도착 판정이 즉시 성립한다 → **변위 0, 클립만 재생**.
**훅·잡기가 걸리면 허공에 대고 나간다 — 같은 뿌리의 다른 증상이다.**

수정 = `MonsterBase.SetServerLogicSuspended(bool)` 신규(additive, 호출처 = Director 2곳).
스폰 직후 정지 → `SnapBossToNavMesh` **뒤** 재개. `SetSpawnAnchor` 와 같은 모양의 seam 이다.

⚠️ **인수인계에 적힌 후보 2개는 둘 다 아니었다** — `552c44a` 의 `acceleration` 승계는 살아 있고
(`DashAcceleration 999` + `autoBraking=false`), 복원용 필드도 `-1f` 초기화가 정상이다.

**3. 돌진이 조용히 실패하지 않게 했다** — `StartDashMove` 가 ① 에이전트 부재/꺼짐/오프메시를
문구로 가르고 ② 클램프된 목적지가 출발점과 같으면 값과 함께 경고한다.
같은 증상이 다른 경로로 또 오면 **한 줄이 무엇이 없었는지 말한다.**

### ✅ Play 육안 검증 — 팀장 확인 (2026-08-18 낮)

| | 확인 |
|---|---|
| 회전이 한 프레임에 스냅하지 않는다 | ✅ |
| 돌진이 여전히 **밀고 지나간다**(따라 맴돌지 않는다) | ✅ |
| 잡기 무한 회전 재발 없음 | ✅ |
| 입장 연출 착지 직후 **첫 돌진이 전진한다** | ✅ |
| 몹 8종·중간보스 3종 회귀 없음(회전 여전히 즉시) | ✅ |

명중률은 이상 없다고 판단됐다. 어긋나면 첫 노브는 `No23.asset` 의 **`turnSpeed`**(현재 10) —
⚠️ 돌진 선딜은 클립 이벤트가 **0.15초**뿐이라 10 으로 약 80%만 수렴한다.

### 🔴 빌드 씬 — 정본은 이미 `-trensparent` 다. 다만 **게이트가 거꾸로 걸려 있다**

이전 인수인계의 "`Build/Windows64 Player` 가 필수 맵을 보조 씬으로 고정한다"는 **절반만 맞았다.**
실측 결과 **빌드된 게임이 실제로 여는 씬은 정본**이다.

| 지점 | 값 | 판정 |
|---|---|---|
| `0.BootStrapScene` 의 GameManager 인스턴스 오버라이드 | **`4.MapScene-trensparent`** | ✅ **이것이 실제 런타임 결정자다** |
| `EditorBuildSettings` | `4.MapScene` · `4.MapScene-trensparent` **둘 다 enabled** | ✅ 정본이 빌드에 들어간다 |
| `GameManager.prefab` 기본값 | `4.MapScene` | ⚠️ 오버라이드 없는 다른 씬에서 쓰면 레거시로 간다 |
| [BuildWindowsPlayer.cs:26](Assets/1.Scripts/Editor/BuildWindowsPlayer.cs:26) `RequiredScenes` | `4.MapScene` **만** 요구 | 🔴 **거꾸로다**(아래) |

🔴 **게이트가 지켜야 할 것을 안 지킨다.** `RequiredScenes` 는 레거시를 요구하고 정본은 요구하지 않는다.
그래서 **정본을 빌드 목록에서 실수로 빼도 빌드는 통과하고, 실행하면 게임이 맵에 못 들어간다.**
반대로 레거시를 빼면 멀쩡한 빌드가 실패한다. **판단 필요** — 레거시 `4.MapScene` 을 계속 출하할지
정하면 그에 맞춰 `RequiredScenes` 를 고친다(정본 추가 / 레거시 유지 여부).

### 🔴 남아 있는 팀장 액션 3건 (변동 없음)

1. **은희 님께 플레이어 수정분 push 요청** + `AudioManager` NRE 9건 전달
   (`TitleSceneManager.cs:33`·`LobbySceneManager.cs:77` — `Instance` 또는 `.Catalog` 가 null 인데
   두 곳 다 null 검사 없이 두 단계를 체이닝한다. 아트 머지에서 배선이 빠진 것으로 보인다)
2. **빌드 게이트** — 위 표 참조. 이전 판의 "보조 씬으로 고정" 서술은 정정됐다
3. **`layprefab.prefab` 의 `LaserBlockWall` 걷어내기** — ⚠️ Quest 통로 차단의 **대체 구현은 없다**

### 🔴 그 밖에 열려 있는 것

- **Wells 폭탄 진단 로그**(`[Wells/진단] ①②③` + `[23호/폭탄] ④`)는 **남겨 뒀다.** 다시 끊기면
  **안 찍히는 번호**가 범인이다.
- **돌진 경로 스침 넉백 없음** — 공용 `MonsterMeleeAttack` 경로라 손대면 몹 8종·중간보스 3종에
  회귀 위험. 필요하면 별건으로 판단.
- `No23.asset` 에 **제거된 키 5개**(`*KnockbackDuration`·`*Stagger`)가 남아 있다. Unity 가 로드 시
  무시하고 다음 저장에 사라진다.

---

## 이전 시작점 — 보스 회전 감속 + 돌진 미이동 (2026-08-18 새벽 종료)

**브랜치 `feature/Boss23`** · **컴파일 0에러** · **커밋 6개 미푸시** (`3a8743d..8af4b8e`)
**백업 = 로컬 `backup/boss23-20260815`**

**한 줄 상태: 다리 개통·도구 정리·레거시 보스 교체·리쉬 버그까지 닫혔고 Play 로 확인됐다.
남은 것은 보스 거동 2건이다.**

### 🔴 다음 세션 할 일 2건 (팀장 지시)

**1. 보스 회전을 플레이어처럼 부드럽게**

지금 보스는 타깃을 향해 **한 프레임에 확 돌아본다** — 비주얼이 불편하다는 판단이다.
플레이어의 감속 회전 방식을 그대로 가져온다.

| | 위치 |
|---|---|
| 보스(즉시 회전) | [MonsterBase.cs:1142](Assets/1.Scripts/Monster/MonsterBase.cs:1142) `FaceTarget()` — `transform.rotation = Quaternion.LookRotation(dir)` 한 줄 |
| 플레이어(감속 회전) | [PlayerMovement.cs:179](Assets/1.Scripts/Player/PlayerMovement.cs:179) `Quaternion.Slerp(현재, 목표, rotate_Speed * Time.deltaTime)` + `Dot > 0.999f` 도달 클램프 |
| 호출처 | `MonsterBase.cs:315`·`335`, `TwentyThreeBoss.cs:1748` |

⚠️ **`FaceTarget` 은 몹 8종·중간보스 3종이 공유한다.** 즉시 회전을 그냥 바꾸면 전부 바뀐다 —
`data` 에 회전 속도 필드를 추가하고 **0 이면 기존 즉시 회전**으로 두는 식이 안전하다(장판 SO 이관 때 쓴 규약).
⚠️ 회전은 서버 권한이고 클라는 `NetworkTransform` 보간으로 받는다 — 서버에서만 감속하면 된다.
⚠️ [TwentyThreeBoss.cs:923](Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:923) `FaceTargetWhileAttacking => false` 는
**의도된 것**이다(잡기 소켓 되먹임으로 무한 회전한 사고 때문). 공격 중 회전을 되살리지 말 것.

**2. 착지 직후 돌진이 앞으로 안 나간다 — 애니메이션만 제자리에서 돈다**

landing 직후 바로 돌진을 하는데 **실제 변위가 0** 이다.

🔴 **전에 같은 증상을 한 번 고쳤다** — `552c44a`: `StartDashMove` 가 프리팹의 `acceleration`(8m/s²)을
승계하지 않아 0.7초에 1.96m 만 가던 문제. 레거시 BT 의 `SetAgentDashModeAction` 은 `999`/`autoBraking=false`
로 바꾸고 있었다. **먼저 그 승계가 아직 살아 있는지 확인할 것.**
그 다음 후보: 돌진은 `agent` 이동이 아니라 **NavMesh 클램프 방식**이다(IMPLEMENTATION_NOTES.md) —
착지 직후엔 에이전트가 막 켜진 시점이라 클램프 기준이 안 잡혔을 수 있다.

### ✅ 2026-08-18 새벽에 닫은 것 (커밋 6개)

| 커밋 | 내용 |
|---|---|
| `fdd37eb` | ZoneL_typeB 다리 개통 복원 — 아트 V3 교체로 날아간 `ZoneBridgeGate` 재연결 |
| `0aa000c` | 아트 머지로 죽은 도구 경로 3건 정정 |
| `962c3e9` | 소진된 일회성 에디터 도구 25개 제거 (83 → 58) |
| `9181e75` | 레거시 보스 프리팹 참조를 신규 `Monster/Boss` 로 교체 (5곳) |
| `f64e599` | 보스 프리팹 검증 도구 제거 — 검증 대상이 죽었다 |
| `8af4b8e` | 착지 후 영구 리쉬 복귀 수정 — 리쉬 기준점을 전투 원점으로 |

**리쉬 버그가 이번 세션의 핵심 교훈이다.** `bossPrefab` 을 레거시(BT)에서 신규(FSM)로 바꾸자
`MonsterBase.HandleSeekAndCombat` 이 보스에 대해 처음 실행되면서 드러났다 —
상공 18m 스폰 위치가 리쉬 기준점으로 잡혀 `leashRadius`(15m)를 영구 초과했고,
`EnterReturn` 의 `Revive()` 가 매 프레임 체력을 최대로 되돌렸다.
증상은 "데미지가 안 박히고 애니메이션이 안 나온다"로 보였다. 해결 = `SetSpawnAnchor` seam.

### 🔴 새로 발견된 별건 — 은희 님 담당

**`AudioManager` NRE 9건** — Play 부트 흐름에서 터진다. BGM 이 안 나오는 것 말고 흐름은 진행된다.

```
TitleSceneManager.cs:33   AudioManager.Instance.PlayBGM(AudioManager.Instance.Catalog.TitleBGM)  ×5
LobbySceneManager.cs:77   AudioManager.Instance.PlayBGM(AudioManager.Instance.Catalog.LobbyBGM)  ×4
```

`Instance` 또는 `.Catalog` 가 null 인데 두 곳 다 null 검사 없이 두 단계를 체이닝한다.
`LobbySceneManager` 는 아트 머지에서 `BeaverLobbySceneManager` 의 GUID 를 물려받으며 613줄 개편된
파일이고 `3.LobbyScene` 도 +3,037줄 바뀌었다 — **머지 과정에서 배선이 빠진 것으로 보인다.**

### 🔴 팀장 액션

1. **은희 님께 플레이어 수정분 push 요청** — "플레이어 버그를 고쳤다"고 했으나 **원격 어디에도 없다.**
   `Player/`·`Unit/`·`PlayerVariant/` 가 모든 원격 브랜치에서 `feature/Boss23` 과 0 파일 차이이고,
   마지막 커밋은 `257cb4c`(2026-08-07)로 이미 들어와 있다. 위 `AudioManager` 건도 같이 전달.
2. **`Build/Windows64 Player`** 가 필수 맵을 보조 씬(`4.MapScene`)으로 고정한다 —
   정본은 `-trensparent`. 빌드 산출물에 영향이라 판단 필요해 손대지 않았다.
3. **`layprefab.prefab` 의 `LaserBlockWall` 걷어내기** — 걷어내면
   `Tools/Map/Authoring/Remove Quest Laser Blockers` 와 그 파일도 지울 수 있다.
   ⚠️ Quest 통로 차단의 **대체 구현은 없다**(구 주석의 `AttachQuestBlockade` 는 코드에 존재하지 않는다).

### 도구 정리 결과 — 메뉴 83 → 57개

읽기전용 검증·덤프, Bridge Gate 5종, MapGen, Rendering Look, 잡기 소켓(은희 인터페이스 대기)은 유지.
Codex CLI 교차검증에서 **Codex 가 2건 틀렸다** — `AttachQuestBlockade` 를 주석에서 읽고 구현으로 오독,
`LaserBlockWall` 잔존을 0건으로 판단. 그래서 `Remove` 는 남겼다.

⚠️ 지시대로 남긴 3개는 **지금 정상 동작하지 않는다**: `Effects/v1 기본 에셋 생성`·`Effects/스모크 테스트`
(`EntryFolder = Assets/5.VFX/Common` 사망, `EffectSmokeTestRunner` 타입 없음) · `Build/Windows64 Player`.

---

## 이전 시작점 — 아트 머지 후 Unity 검증 (2026-08-15 저녁 종료)

**브랜치 `feature/Boss23`** · 머지 커밋 `71dabb1` (부모 `3a8743d` + `86da52c`)
**백업 = 로컬 브랜치 `backup/boss23-20260815`** (머지 직전 `3a8743d` 시점)

**한 줄 상태: `origin/fix/pixel` 아트 머지를 끝냈다. Unity 를 아직 한 번도 안 열었다.**

### 0번 할 일 — Unity 열고 콘솔 읽기

머지 후 Unity 를 **한 번도 실행하지 않았다.** 임포트가 처음 돌면서 나오는 콘솔이 다음 작업의 지도다.
정본 씬은 `Assets/0.Scenes/MainFlow/4.MapScene-trensparent.unity` 이고
진입은 `0.BootStrapScene` 에서 Play (F8 = ProfilerHUD, F9 = 룩 A/B).
상세 = [Docs/tech/map-rendering-lighting-handoff.md](Docs/tech/map-rendering-lighting-handoff.md).

⚠️ **머지 전 콘솔 에러 17건의 정체**(대조용): 컨베이어 죽은 참조(ZoneL_typeA) · MCP
`README_ko.md` meta 없음 12건 · `NetworkAnimator.OnValidate` NRE 3건 · 50.Art TestAssets 3건.
**첫 번째만 이 머지로 해소된다.** 나머지 3종은 그대로 뜨는 게 정상이다.

### 예상되는 결손 참조 — 전부 이미 조사됐다. 새 버그로 오진하지 말 것

| 대상 | 결손 | 성격 |
|---|---|---|
| `LevelDeliveryV3` 프리팹 ~~124개~~ **110개** | `OcclusionSection`·`ElevationStack`·`ElevationLevel` (에셋,GUID) 쌍 132 / 컴포넌트 **560** | 🔴 아래 |
| `ModularRobots_R1.prefab` | `CommonMeleeRobot.asset` | **의도된 것** — 팀장이 삭제 결정 |
| `Stage1.prefab` | 3 | 머지 전 `HEAD`·`leejiwon` 양쪽 동일 |
| `4.MapScene(-trensparent)` | `FogManager.maskTexture` | 머지 전부터 |
| `VFXScene.unity` · `Player.prefab` | 각 1 | 아트/SVN 쪽 기존 결손 |

🔴 **`LevelDeliveryV3` ~~124개~~ 110개** (2026-08-29 정정 — 아래 참조) — `fix/pixel` 이 그대로 들고 온 상태이지 이 머지가 만든 게 아니다.
필요한 스크립트 3종이 **`feature/trensparent` 에만** 있다. 정본 씬은 이 프리팹을 쓰지 않으므로
게임 경로 영향은 없고 콘솔 Missing Script 경고만 뜬다.
→ 고치려면 `origin/feature/trensparent` 추가 머지(충돌 12건)인데 `WallOcclusion*` 코드가
`fix/pixel` 쪽이 더 최신이라 **역행 위험**이 있다. 별건으로 판단할 것.

> **✅ 2026-08-29 종결 — 현상 유지로 결정.** 위 판단이 맞았고, 근거를 네 축으로 다시 쟀다.
> - **삭제된 적이 없다.** 세 클래스(`OcclusionSection`·`ElevationLevel`·`ElevationStack`)는
>   `bb35c92` "feat: redesign registered wall occlusion"(2026-08-09, noisyboy632)에서 **추가**됐고
>   그 커밋은 `origin/feature/trensparent` 에만 있다(`git merge-base --is-ancestor bb35c92 HEAD` = false).
>   전 브랜치에 **삭제 커밋이 없다** — "왜 지웠나" 가 아니라 "아직 안 왔다" 가 맞는 질문이었다.
> - **정확한 규모**: 프리팹 **110개** / (에셋,GUID) 쌍 **132** / **실제 컴포넌트 인스턴스 560개**
>   (OcclusionSection 515 · ElevationLevel 33 · ElevationStack 12). 도구가 말하는 "132 참조" 는
>   쌍 수이지 컴포넌트 수가 아니다 — 정리를 택했다면 지울 대상은 560 이었다.
>   **110 은 두 독립 방법으로 맞췄다**(python YAML 집계 · `grep -rl` 합집합). 기존 문서의
>   "124개"/"125개" 는 근거가 없는 수다 — GUID별 108/12/12 는 도구와 정확히 일치한다.
> - **게임 경로 영향 없음(4축 전부 0)**: 씬 직접 참조 0 · **27개 씬 전수 GUID 전이 BFS 도달 0** ·
>   Addressables 그룹 교집합 0 · 코드의 경로 로드 0.
> - **왜 복원하지 않나**: 좁은 복원의 최소 폐포는 4파일(세 클래스 + `WallOcclusionRegistry`)이고
>   컴파일은 닫히지만, **우리 브랜치에는 이 타입을 쓰는 코드가 0건**이다(드라이버가 `fix/pixel` 계열).
>   살아나는 것은 인스펙터의 오소링 데이터뿐이고 런타임 동작은 없다 — 죽은 코드를 넣는 대가로
>   콘솔 경고를 지우는 셈이고, 나중에 trensparent 를 제대로 머지할 때 충돌만 늘린다.
> - **프리팹 정리는 하지 말 것.** 되돌릴 수 없고 trensparent 리디자인이 전제하는 오소링 기록
>   (`renderers`/`colliders`/`contentRoot`/`contentRenderers`)을 없앤다.
>
> **다시 판단하지 말 것.** 바뀔 조건은 하나 — `feature/trensparent` 를 머지하기로 할 때다.
> 재현 스크립트와 상세 근거는 MCP 포크의 `HANDOFF.md` §5 / §4-(44) 와
> `Tools/scan/occlusion-reach.py` 에 있다.

### ✅ 원격 `feature/maprendering` 삭제 완료

`feature/Boss23` 에 100% 포함돼 있었다(ahead 91 / behind 0). 되살리려면 `2e14b18` 로 push.

### 🔴 팀장 액션 1건

1. **은희 님께 플레이어 수정분 push 요청** — "플레이어 버그를 고쳤다"고 했으나 **원격 어디에도 없다.**
   `Player/`·`Unit/`·`PlayerVariant/` 가 모든 원격 브랜치에서 `feature/Boss23` 과 **0 파일 차이**이고,
   `Player/` 를 만진 마지막 커밋은 `257cb4c`(2026-08-07)로 이미 들어와 있다.
   은희 님 미머지분은 `fix/property_change` 1커밋뿐인데 씬 전환·빌드 설정이라 플레이어가 아니다.

### 이번 머지에서 확정된 것

- **아트 브랜치 5개는 사실상 1개다** — `fix/pixel` 이 `leejiwon`·`fix/art_zone` 을 조상으로 포함하고,
  `fix/convayor`·`fix/movingplatform` 은 내용이 체리픽(`0494685`·`a48680d`)돼 blob 해시까지 같다.
- **레이어 17 = `Effect`(아트), 18 = `CombatTarget`** 으로 확정. [layer-standard.md](Docs/tech/layer-standard.md) 갱신 완료.
- **`CommonMeleeRobot.asset` 은 머지 사고로 사라진 것이었다** — `1bf654f`(은희, feature/VFX→development)
  에서 **양쪽 부모에 다 있는데** 결과에서 빠졌다. 되돌리기 반복은 금지(원인·금지사항 =
  [Docs/tech/bt-subgraph-reference-loss.md](Docs/tech/bt-subgraph-reference-loss.md)). 팀장이 삭제 수용으로 종결.
- ⚠️ **`Assets/50.Art` 는 정션이다.** GUID 결손 조사 시 `grep -r Assets` 는 정션을 **안 따라간다** —
  `Assets/50.Art` 를 명령줄에 직접 줘야 한다.
  > **2026-08-30 정정**: 이 줄은 원래 "OneDrive 정션이고 이 PC 에 `.svn` 작업사본도 `svn`
  > 클라이언트도 없다" 였다. **둘 다 이제 틀렸다** — 2026-08-23 에 작업사본을
  > `C:\svn\GA7thFinal_VeyTrace` 로 옮기고 SlikSvn 을 설치했다(위 2026-08-23 절).
  > 셋업 절차는 [Docs/tech/environment-setup.md](Docs/tech/environment-setup.md).

---

## 이전 시작점 — 보스 거동 잔여 4건 (2026-08-10 저녁 종료)

**브랜치 `feature/Boss23`** · **컴파일 0에러 0경고** · **`8170481..HEAD` 전부 미푸시**
(개수는 적지 않는다 — 개수를 적으면 그걸 적는 커밋이 자기 자신을 못 세서 매번 어긋난다)

**한 줄 상태: 잔여 결함 4건이 전부 닫혔다. 남은 것은 Play 육안 검증이다.**

팀장 지시(Play 확인 후 기능별 분할)대로 2026-08-13 에 끊었다.

| 커밋 | 내용 |
|---|---|
| `d7445db` | 장판 값 SO 이관 — 0 이면 프리팹 값 |
| `552c44a` | 돌진 전진 복구 + 체인 예산에 선딜 반영 |
| `019431e` | 폭탄 착지 정지 + 퓨즈 5초 + 점프 범위 동반 폭발 |
| `c451259` | 잡기 소켓 한시 복구 + 잡는 동안 회전 금지 |
| `6035aaf` | 점프 착지 예고 원 2개 + 앞뒤 표식은 착지 후에만 |
| `ba66e1b` | No23 단독 변형 — 중간보스로 재활용 |
| `afab6b8` | No23 데이터 배선 + 장판 10초 + 저작 도구 |
| `c1a7342` | MonsterScene 보스 전투 씬 구성 (P7) |

🔴 **일부러 커밋하지 않은 것 3건** — 기능 변화가 0 이거나 보스 작업이 아니다.
`4.MapScene.unity`(fileID churn 46/46) · `MultiplayerManager.asset`(줄끝만, numstat 0/0) ·
`ZoneL_typeC.prefab`(**맵/아트 변경** — `crane_prefabs (1)` 중첩 추가. 이 세션 전부터 작업
트리에 있던 것이라 **누구 작업인지 확인이 필요하다**).

**한 줄 상태: 보스가 Play 에서 한 사이클을 돈다. 남은 것은 결함 4건이다.**

### ✅ 이번 세션에 Play 로 확인된 것 (팀장 육안 승인)

| | 확인 |
|---|---|
| 보스 스폰·추격·공격·페이즈·**송전기 전 시퀀스**·처치 | 로그 + 육안 |
| **폭탄** — 착지 지점 정지 · 좌클릭 당구 · **벽 1회 반사** | 육안 |
| **잡기** — 회전 폭주 없음 · 플레이어가 **서 있는 자세**로 붙음 | 육안 |
| **점프 예고** — 큰 원 + 차오르는 작은 원, 다 차는 순간 착지 | 육안 |
| **앞뒤 표식** — **착지 후에만** 나온다 | 육안 |

### ✅ 잔여 4건 — 2026-08-13 전부 처리 (커밋 `d7445db` · `552c44a`)

> **🔴 세 건 중 두 건은 인수인계에 적힌 원인이 틀렸다.** 로그 문구와 경고 문구를 원인으로
> 읽은 것이 화근이었다. 아래는 코드로 확정한 결과다.

| # | 인수인계가 말한 원인 | **실제 원인** | 결과 |
|---|---|---|---|
| 1 | 플레이어 평타가 `Unit` 을 요구해 폭탄이 걸러진다 | ❌ **틀렸다. 폭탄은 이미 맞고 있다** | 수정 없음 |
| 2 | 돌진이 안 나간다(원인 미상) | `StartDashMove` 가 **`acceleration` 을 승계하지 않았다** | 수정 |
| 3 | `grab` 클립의 `OnAttackEnd` 미수신 | ❌ **grab 이 아니라 dash 였다** — 돌진 체인 예산 부족 | 수정 |
| 4 | 수명 타이머 때문에 SO 이관이 위험하다 | ❌ **Instantiate↔Spawn 사이 창을 쓰면 안전하다** | 이관 완료 |

**1 — 폭탄 평타는 정상이다.** `PlayerDefaultAttack.HitOverlap` 은 `Unit` 을 요구하지 않는다
(`ownerUnit == null` 이어도 통과해 `TryResolveHit(hurtbox, hit)` 까지 간다 →
`Hurtbox.ResolveReferences` 가 `GetComponentInParent<IAttackReceiver>()` 로 `BossBomb` 을 찾는다).
로그의 `unit없음` 은 **필터가 아니라 진단 출력 문구**다. 거짓 경보의 뿌리는 따로 있다 —
`LogEmptySwing` 이 `swingHitBuffer.Count == 0` 으로 발동하는데 그 버퍼는 **`Unit` 만 담는다**
(패시브 통지용). 그래서 폭탄을 성공적으로 때린 스윙도 "전부 걸러졌다"로 찍힌다.
팀장 육안 승인(좌클릭 당구 작동)과도 일치한다. 🔴 **고칠 것은 플레이어 쪽 로그 3줄뿐이고 은희
담당 경계라 손대지 않았다.** 게임플레이 영향 0 — 콘솔 노이즈만 남는다.

**2 — 돌진.** 프리팹 `acceleration` 8m/s² 로는 0.7초에 5.6m/s 까지만 올라 **약 1.96m** 만 간다
(목표 15m/s·16m). 레거시 BT 의 `SetAgentDashModeAction` 은 999 / `autoBraking=false` 로 바꾸고
있었다 — FSM 재작성판이 `speed`·`stoppingDistance` 만 승계하며 이 둘을 빠뜨렸다.

**3 — 타임아웃은 dash 것이다.** 경고 문구가 `"Grab 체인이 …"` 로 **하드코딩**돼 있었고 이 안전망은
Dash·Jump·Charge·Rage 도 함께 쓴다. 돌진 예산 `0.7+0.9=1.6초` vs 실제 `0.15(히트이벤트)+0.7+0.9=1.75초`
→ 매번 0.15초 초과. 이제 경고가 **어느 공격인지 찍는다.**
참고: `grab` 클립에 `OnAttackEnd` 가 없는 것은 **의도**다(저작 도구 주석: "잡기 체인이 자기 종료를
소유한다"). 그쪽은 결함이 아니었다.

**4 — 장판 SO.** `AreaZone.OnNetworkSpawn` 이 타이머를 시작하므로 **스폰 전에** 주입하면 된다.
`SpawnOrGrow` 의 Instantiate↔Spawn 사이에 창이 있다. **0 = 프리팹 값** 규약이라 SO 를 안 채우면
동작이 그대로다. ⚠️ 지금 실효값은 여전히 프리팹(FireFloor `lifetime 10`·`radius 2`·`maxRadius 5`) —
SO 로 조절하려면 `No23.asset` 인스펙터 「폭발 장판」에 0 아닌 값을 넣으면 된다.

### ✅ 2026-08-13 저녁 — Play 피드백 6라운드 반영 (커밋 `99525da`…`30ee2e5`)

**팀장 Play 육안 승인분** — 넉백 3종 · 차징이 landingpos 로 이동 · 점프 때 플레이어 안 뜸 ·
피격 후 표식 색 유지(후방 파랑) · 훅 명중 · 폭탄 room 안 투척(장판 확인).

| 라운드 | 증상 | 진짜 원인 | 커밋 |
|---|---|---|---|
| 1 | 범위 밖인데 훅·어퍼·잡기를 함 | **데이터가 틀렸다** — 거리창이 실제 도달거리보다 넓었다. 잡기는 `maxDistance 3.5` vs `grabRadius 2.2` **확정 불일치**(밖에서 시전하면 반드시 헛잡기) | `99525da` |
| 1 | 공격 간격이 너무 빠름 | **전역 간격이 없었다** — 쿨다운이 행마다 따로라 훅L·훅R·어퍼를 번갈아 쓰면 쉬는 구간 0 | `99525da` |
| 2 | 훅이 안 맞음 | 거리창만 좁혀서 생긴 반작용. **거리창은 "고를 조건", 명중은 손 박스**가 만든다 → 둘을 같이 키워야 한다 | `57fa7ef`·`dd5e49e` |
| 2 | 점프 착지 때 플레이어가 **위로 떠오름** | `agent.Warp` 로 보스 캡슐을 플레이어 캡슐 **안에** 꽂으면 디페네트레이션이 수평으로 못 밀어 **위로** 뺀다 | `57fa7ef` |
| 3 | 표식이 피격 후 **영구 빨강** | **원인 2겹** — ① `HitFlash` 가 유닛의 모든 렌더러를 긁어 표식 호까지 물들이고 ② 원래 색을 `sharedMaterial` 에서 캐시하는데 그 재질이 장판과 공유하는 순빨강이라 **복원 색이 빨강** | `61dad2b` |
| 4 | 넉백이 **3곳 전부** 안 걸림 | `Unit.ReceiveAttack` 은 `AttackInfo.knockback*` 를 **읽지 않는다.** 진입점은 `Unit.Knockback(방향, 강도)` 하나뿐 | `04da127` |
| 5 | 점프 체공 중 **보이지 않는 보스가 맞음** | 메시만 껐다. 끌 콜라이더가 **둘**(HurtBox layer 14 + 루트 몸 layer 8, 마스크 17664 에 둘 다 포함) | `66eed97` |
| 6 | 차징 위치에 **정확히 못 감** | `stoppingDistance` base 기본값이 **1.6m** 라 0.6m 도착 판정이 영원히 성립 못 함 → 매번 타임아웃. 🔴 **돌진에서 이미 밟은 함정을 새 이동 코드에 승계하지 않았다** | `30ee2e5` |

### 🔴 지금 열려 있는 것

- **Wells 폭탄** — 정상 투척 중(장판으로 확인). 진단 로그 `[Wells/진단] ①②③` + `[23호/폭탄] ④` 는
  남겨 뒀다. 다시 끊기면 **안 찍히는 번호**가 범인이다.
- **돌진 경로 스침 넉백 없음** — 공용 `MonsterMeleeAttack` 경로라 손대면 몹 8종·중간보스 3종에
  회귀 위험. 필요하면 별도 판단.
- `No23.asset` 에 **제거된 키 5개**(`*KnockbackDuration`·`*Stagger`)가 남아 있다. Unity 가 로드 시
  무시하고 다음 저장에 사라진다.

### 🔴 세션 중 저절로 바뀌어 있던 애셋 2건 — 되돌렸다

둘 다 원인 미상이고 기능을 조용히 깨뜨리는 값이었다. **재발하면 무엇이 건드리는지 볼 것.**

- `bossroom.prefab` — 송전탑 Y `0.55 → −0.041`(바닥 아래로 가라앉는다)
- `FireFloor.prefab` — `m_Layer 9 → 0`(Default = 미분류 전부) + 컴포넌트 구성 교체.
  팀장 확인: **프리팹 에디터 작업 중 실수 저장**이었고 되돌린 것이 맞다.

### ▶ 2026-08-13 오후 — 팀장 지시 8건 구현 (커밋 `03c2966`…`acf129c`)

| 지시 | 결과 |
|---|---|
| 표식 색 처음 그대로 | ✅ 카운터 노랑 전환 제거. **후방 호가 파랑으로 바뀐다**(저작값) — 지금 화면과 달라짐 |
| 공격 중 회전 없음 | ✅ 출처 2곳(`FaceChainTarget` + `MonsterBase.HandleAttack`) + `BeginDash` 재조준까지 제거 |
| Wells 폭탄 | ▶ **원인 미확정 — 진단만 넣었다**(아래) |
| 폭탄 room 안 · 벽 금지 | ✅ 착지 지점 먼저 추첨(NavMesh 검증) → **속도 역산**. `ThrowWithVelocity` 신규 |
| 점프 중 투척 금지 | ✅ 체공 동안 억제. 해제를 **착지·체인중단 양쪽**에 |
| 차징 = 송전탑 중심으로 이동 후 애니 | ✅ `ChargeMove` 단계 신설 + `TryPrepareCenter` |
| 차징 중 접근 차단 오라 | ✅ 주기 반복 데미지+넉백, 반경 3.5 기본, 범위 표시, 전부 SO |

**🔴 Wells — 내가 두 번 오진했다. 다음 사람은 진단 로그부터 볼 것.**
- ❌ "`rig` 본 계층 누락" → **있다.** 스킨 본 147개 중 끊긴 것 0개. `BombSocket` 이 `Bone` 본
  밑이라 경로 조회가 실패한 것을 부재로 읽었다.
- ❌ "Avatar null 이라 클립이 안 돈다" → **아니다.** `TwentyThree.prefab` 도 `m_Avatar: 0` 인데
  보스 애니는 정상이다(Generic 은 Avatar 없이 경로 이름으로 바인딩).
- → `Tools/Boss/Wells — 검증 (읽기 전용)` 으로 애셋 상태를 보고, Play 에서
  **`[Wells/진단] ① ② ③` 과 `[23호/폭탄] ④`** 중 **안 찍히는 번호**를 찾으면 그게 범인이다.

⚠️ `bossroom.prefab` 이 세션 중 **송전탑 Y 0.55 → −0.041** 로 바뀌어 있었다(원인 미상, 바닥
아래로 가라앉는 값) → **커밋된 상태로 되돌렸다.** 다시 나타나면 무엇이 건드리는지 볼 것.

### 🔴 다음 세션에 할 것

1. **Play 육안 검증 — 마지막 라운드분**(`66eed97`·`30ee2e5`, 코드만 확인했다):
   ① 점프 체공 중 보스가 **안 맞는지**(그리고 착지 후 **다시 맞는지** — 무적이 남으면 전투가 안 끝난다)
   ② 차징 위치에 **정확히·빠르게** 도착하는지 ③ 오라 원이 보스를 따라가는지
   (Play 하면 화면 왼쪽 위 **Start Host** 를 눌러야 시작된다)
2. **넉백 세기 튜닝** — 팀장이 "조금 과하다"고 했다. `dashKnockbackStrength`(12) ·
   `jumpKnockbackStrength`(9) · `chargeAuraKnockbackStrength`(8). 절반쯤이면 6 / 4.5 / 4 가 출발점.
   ⚠️ **강도만 있다** — 진입점(`Unit.Knockback`)이 강도만 받으므로 지속·경직 노브는 일부러 없다.
3. 남은 것은 **팀장 액션 3건**(아래)뿐이다.

### 튜닝 노브 위치 (전부 `No23.asset` / 단독 변형은 `No23_Solo.asset`)

| 헤더 | 필드 | 현재 |
|---|---|---|
| 인지/교전 | `attackRange` — 보스가 **접근을 멈추는** 거리 | 2 |
| 공격 간격 — 전역 | `globalAttackInterval` | 1.5초 |
| attacks[] | 훅 `maxDistance` / 어퍼 / 잡기 | 3.2 / 2.8 / 2.2 |
| 돌진 | `dashKnockbackStrength` | 12 |
| 점프 어택 | `jumpLandSeparation` · `jumpKnockbackStrength` | 1.2 / 9 |
| 차징 오라 | `chargeAuraRadius`·`Interval`·`Damage`·`KnockbackStrength` | 3.5 / 1 / 20 / 8 |
| 차징 이동 | `chargeMoveSpeedMultiplier`·`ArriveDistance`·`Timeout` | 3 / 0.6 / 4 |
| 폭발 장판 | `fireZone*` (**0 = 프리팹 값**) | 전부 0 |
| 폭탄 착지 | `bombLandingMin/MaxDistance` · `bombWallMargin` | 3 / 9 / 1.2 |

⚠️ 손 히트박스(**훅이 실제로 맞는 범위**)는 SO 가 아니라 **프리팹**이다 —
`TwentyThree.prefab` 의 `Hand_L`/`Hand_R` BoxCollider `size`(현재 **2.6**).
메뉴 `Tools/Boss/보스 데이터 — 손 히트박스 크기 반영` 으로도 바꿀 수 있다.
⚠️ `attackRange` 를 거리창보다 크게 올리면 **보스가 멈춰 선 채 아무 공격도 못 한다.**
⚠️ 잡기 `maxDistance` 는 **`grabRadius`(2.2)를 넘으면 안 된다** — 넘으면 반드시 헛잡기다.

### 🔴 이번 세션에 확정된 함정 (다음 사람이 밟을 지점)

- **`Env` 를 통째로 끄면 NavMesh 가 함께 죽는다** — NavMeshSurface 가 `Env` 에 붙어 있다(`NavMesh-Env.asset`).
  그 서피스는 원래 `CollectObjects=Children`+`RenderMeshes` 였다 → bossroom 을 루트에 두면 **빈 NavMesh**.
  `MapNavMeshBaker` 가 검증한 설정(**PhysicsColliders / All / Default∪Ground**)으로 바꿔 뒀다.
- **`hand.r` 을 이름으로 깊이우선 탐색하면 Wells 의 손이 먼저 걸린다** — Wells 가 23호 리그
  `c_root_master.x` 밑에 중첩돼 있고 자기 리그에 같은 이름의 본을 갖고 있다. 내 잡기 소켓 초판이 이걸
  밟았다(검증 출력이 잡아냈다). **조상에 `BossWells` 가 없는 후보**만 골라야 한다.
  ⚠️ 기존 히트박스 앵커 3개(`Hand_L`·`Hand_R`·`DashBody`)는 **정상**임을 함께 확인했다.
- **잡기 소켓을 살리면 보스가 무한 회전한다** — 잡힌 플레이어가 손을 따라가는데 보스가 그 플레이어를
  향해 `LookRotation` 하면 되먹임이 생긴다. **잡는 동안에는 회전하지 않는 것**이 맞다.
- **플레이어는 소켓의 회전까지 복사한다**(`followTarget.rotation`) → 손 본의 자세를 물려받아 **눕는다.**
  손은 애니메이션으로 매 프레임 돌기 때문에 **정적 로컬 회전으로는 못 고친다** →
  `BossGrabSocketUpright`(LateUpdate 에서 yaw 만 물려받아 세움).
- **표식·예고는 클라 비주얼이다** — 서버에서만 끄면 클라 화면에는 그대로 보인다. 점프 구간 억제는
  `CrossFadeJumpStateClientRpc` 에 얹었고, 끊길 때 영구히 숨는 것을 막기 위해 `AbortAttackChain` 에서 해제한다.
- **`AoeTelegraph` 디스크는 XY 평면**(`(cos,sin,0)`, 지름 1, 노멀 −Z)이고 `Show()` 가 **루트를 직접**
  스케일한다. `Instantiate(prefab)` 이라 프리팹 회전이 보존되므로 **루트 X=90** 이어야 바닥에 눕는다.
- **`GrabController` 를 되살릴 때는 `enabled = false`** — `Start()` 가 LogError, `Update()` 가 초기화 안 된
  블랙보드를 읽어 NRE 를 쏟는다. 비활성 컴포넌트도 `GetComponentInChildren` 은 찾으므로 소켓 게터만 남는다.

### 🔴 팀장 액션 (변동 없음 + 1건 추가)

1. **SVN 커밋** — `svn commit "Assets/50.Art/Char/Boss/SK/SK_23.fbx.meta"`. `ZoneLayoutCatalog.asset` 은 **절대 함께 넣지 말 것**
2. **은희 님께 요청 전달** — [request-player-grabsocket-decoupling.md](Docs/tech/request-player-grabsocket-decoupling.md).
   🔴 원격 전 브랜치를 확인했다: `IGrabSocketProvider` **없음**, `GetComponentInChildren<GrabController>` 의존
   **그대로 1건**(development 포함). 미착수 확정. 그래서 프리팹에 레거시를 한시적으로 되살려 뒀다 —
   **인터페이스가 들어오면 걷어내고 `Enemy/Boss/` 삭제를 마무리한다.**
3. **`development` 19커밋 미반영** — Player/Monster 를 만진 3건은 전부 VFX(민경). `b1980d3` 이 **피격
   이펙트를 `Unit` 에 배선**하므로 보스 계통과 겹친다. **커밋 후 별도 단계로** 가져올 것.

### 산출물 (위 6커밋에 들어갔다)

프리팹 3종 신규 = `TwentyThree_Solo` · `JumpTelegraph` · (기존 4종 + `GrabSocket` 부착) ·
SO 1종 신규 = `No23_Solo`(패턴 6종) · 런타임 스크립트 1종 = `BossGrabSocketUpright` ·
저작 도구 5종 = `MonsterSceneBossSetup` · `BossDataWiring` · `BossVariantAuthoring` ·
`BossGrabSocketAuthoring` (+ 기존 `No23ClipEventAuthoring` · `BonePathDump`) · 씬 = `MonsterScene`(P7 구성 + MidBossSpawner)

---

## ▶▶ 이전 시작점 — MonsterScene 한 사이클 (2026-08-10 낮)

**브랜치 `feature/Boss23`** · **컴파일 0에러 0경고** · **P0~P6 커밋·푸시 완료** (`66d3741`, ahead 0)
— 아래 「커밋 상태」의 잔여 3건만 작업 트리에 남아 있다.

**한 줄 상태: 프리팹 4종을 새로 만들었고 레거시 정리까지 끝났다. 다음은 `MonsterScene` 을 꾸미는 것(P7)이다.**

> 팀장 지시(2026-08-10): 기존 `2.Prefabs/Wells&No.23/` 3개는 **쓰지 않는다.** MonsterScene 에
> bossroom 을 깔고 **Paladin 이 보스 연출부터 전투까지 한 사이클**을 도는 테스트 환경을 만든다.
> 계획서 = [PLAN.md](PLAN.md) 최상단(슬라이스 P0~P9).

### ✅ 이번 세션 완료 — P0~P6

| # | 한 것 | 검증 |
|---|---|---|
| **P0** | **애니 이벤트 저작** — 저작 도구 `No23ClipEventAuthoring` 신규. `OnAttackHit` 6 + `OnAttackEnd` 4, 구 이름 0건. `idle`·`charging` Loop Time on | `.fbx.meta` 직접 확인 |
| **P1** | `FireFloor.prefab` 신규 (`AreaZone`, layer 9, 콜라이더 0, Quad+Circle 알파로 **원형**) | 씬 뷰 스크린샷 |
| **P2** | `Bomb.prefab` 신규 (`BossBomb`, layer 10, **논키네마틱**+ContinuousDynamic, `bomb.fbx` 비주얼, `zonePrefab`→FireFloor) | 스크린샷 + YAML |
| **P3** | `Wells.prefab` 신규 (`BossWells`, `WellsBossController`, `hand.r/Bone/BombSocket`, **NetworkObject 없음**) | `unity_get_animator_info` 로 바인딩 확인 |
| **P4** | `TwentyThree.prefab` 신규 — **히트박스 새 설계**(`Hand_L`/`Hand_R` = 손 본 / `DashBody` = 루트 자식) | Unity 가 참조를 해석하는지 되읽어 확인 |
| **P5** | `bossroom.prefab` 송전탑 `ChargingObject`×4 → **`BossChargingPylon`×4**, `maxHp` 5→200 | 인스턴스화 후 컴포넌트 조회 |
| **P6** | **레거시 정리** — `BossArenaContext`·`BossArenaWiring` 삭제, `BossEncounterDirector` **889→624줄** | 컴파일 0/0, dll 에서 타입 소멸 확인 |

### ✅ P7 `MonsterScene` 재구성 — 완료 (2026-08-10, 미커밋)

저작 도구 **`Monster/Editor/MonsterSceneBossSetup.cs`** 신규(멱등, 재실행 가능).
메뉴 = `Tools > Boss > MonsterScene — 보스 전투 씬 구성 (P7)` + `… 구성 검증 (읽기 전용)`.

| 한 것 | 값 |
|---|---|
| `bossroom` 배치 | **`(0, -0.50, 0)`** — 보행면을 월드 y=0 으로 내렸다(아래 🔴) |
| 기존 몹 세팅 | `MonsterSpawner`·`TestBootStrap` **비활성 보존** |
| 기존 지오메트리 | `Env/Ground`·`Wall1~4` **자식만** 비활성 — `Env` 루트는 살렸다(아래 🔴) |
| `ForProfile` | `BossTestRig` 오브젝트에 신규 |
| 보스 스폰 | `TwentyThreeArenaContext`(+NetworkObject), `bossPrefab`=신규 `TwentyThree`, `bossPos`=`(0.49, 0, 5.49)` |
| `PlayerPrefab` | 구 `Player` → **Paladin** (아래 🔴) |
| NavMesh | 재베이크 — 삼각형 8개, **19.7×19.8m**(바닥 21m − 에이전트 반지름 0.5m 인셋), y 0.08 |

검증(도구의 읽기 전용 메뉴): 위 전 항목 ✓ · 송전탑 4개 ✓ · 표본 4지점(원점·보스 스폰·양 구석)
전부 메시 0.08m 이내 ✓ · 보스 `NavMeshAgent.agentTypeID=0`/radius 0.5 = 베이크 타입과 일치 ✓.
컴파일 0/0. **Play 미실행.**

### 🔴 P7 에서 문서가 틀렸던 것 2건 (실물이 이겼다)

1. **"`ForProfile` 이 없으면 아무것도 스폰되지 않는다"는 반만 맞다.** `MonsterTestBootstrap` 이
   `autoStartHostOnPlay` 로 **이미 자동 StartHost + 호스트 플레이어 스폰**을 하고 있었다.
   PLAN 대로 그걸 비활성 보존하니 **그때 비로소** `ForProfile` 이 필요해진 것이다.
   → 지금 Play 하면 **화면 왼쪽 위 "Start Host" 를 눌러야** 시작된다(자동 시작 아님).
2. 🔴 **"`PlayerPrefab` 은 이미 Paladin" 은 사실이 아니었다.** `NetworkManager.PlayerPrefab` 과
   `MonsterTestBootstrap.playerPrefab` 이 **둘 다 구 `Player.prefab`**(guid `55ee4e06…`)을 가리켰다.
   Paladin(`af4a760f…`)은 이 씬에 한 번도 나오지 않았다. 둘 다 Paladin 으로 바꿨다.

### 🔴 P7 에서 확정된 것 (다음 사람이 밟을 지점)

- **`Env` 를 통째로 끄면 안 된다** — **NavMeshSurface 가 `Env` 에 붙어 있다**(베이크 산출물이
  `NavMesh-Env.asset` 인 이유). 끄면 NavMesh 가 함께 죽는다. 지오메트리 자식만 끈다.
- 그 서피스는 `CollectObjects=Children` + `UseGeometry=RenderMeshes` 였다 → bossroom 을 루트에
  두면 **수집 대상이 아니어서 빈 NavMesh** 가 구워진다. `MapNavMeshBaker` 가 검증한 설정
  (**PhysicsColliders / All / Default∪Ground**)으로 바꿨다.
- **보행면 = 로컬 y 0.50 실측** — `BossFloorCollider` 의 BoxCollider 가 `size.y=1`·`center.y=0` 이라
  박스가 −0.5…+0.5 를 덮고 **윗면이 0.50**. `BossLandingPoint`·`BossArea`·`PlayerArrivalPoints` 와 일치.
  → bossroom 을 **y −0.50** 에 놓아 보행면을 y=0 으로 만들었다. NGO 는 `PlayerPrefab` 을 **프리팹
  좌표(원점)** 에 스폰하므로, 방을 원점에 두면 호스트든 MPPM 클라이언트든 바닥 0.5m 안에 박힌 채
  시작한다. 이 씬에서만 방이 0.5m 내려가 있다 — 4.MapScene 과 다르다.
- ⚠️ 송전탑(layer `Enemy`)·투명 경계(layer `Wall`)는 수집 마스크 밖이라 **NavMesh 를 카브하지 않는다.**
  보스가 송전탑을 관통하는 경로를 계획할 수 있다(물리로는 막힌다). P9 에서 끼는지 볼 것.

### 🔴 입장 연출은 이 씬에서 검증할 수 없다

`BossEncounterDirector` 는 자기 주석대로 **MapScene 상주**이고, 스폰이
`BossTeleportManager.AlivePlayersArrived` **뒤에** 있다. MonsterScene 에는 텔레포트 사슬이 없어
넣어도 "BossTeleportManager를 찾지 못했습니다" LogError 만 내고 **아무것도 스폰하지 않는다.**
그래서 P7 은 PLAN 의 단계표대로 `TwentyThreeArenaContext`(직접 스폰)로 갔다.
→ **PLAN 목표문의 "입장 연출 → 전투"** 중 연출은 **MapScene 몫**이다. 이 씬은 전투 사이클 전용.

### ▶ 다음 = P9 (Play 한 사이클)

`MonsterScene` Play → **"Start Host" 클릭** → `ValidateContract` LogError 0건 확인 → 추격·공격 8종·
카운터·페이즈·송전기·처치. 미확인 항목(애니 재생·어느 손·히트 타이밍)은 아래 「미확인」 참조.
P8 은 **NetworkPrefabs 등록으로 이미 끝났다**(`TwentyThree`·`Bomb`·`FireFloor`). 충돌 매트릭스는
Play 에서 안 맞는 게 나오면 그때 본다.

### 🔴 새로 만든 것 — 경로

| | |
|---|---|
| 프리팹 4종 | `Assets/2.Prefabs/Monster/Boss/` — `TwentyThree` · `Wells` · `Bomb` · `FireFloor` |
| 머티리얼 | `Assets/3.Materials/MA_AreaZone_Fire.mat` (주황 — 예고 장판 빨강과 구분) |
| 저작 도구 | `Monster/Editor/No23ClipEventAuthoring.cs` (클립 이벤트, **멱등·재실행 가능**) |
| 조사 도구 | `Monster/Editor/BonePathDump.cs` (본 경로 + **lossyScale** 덤프, 읽기 전용) |
| 요청 문서 | `Docs/tech/request-player-grabsocket-decoupling.md` (은희 님께 팀장이 전달) |

**NetworkPrefabs 18개** — `TwentyThree`·`Bomb`·`FireFloor` 등록됨. Wells 는 중첩이라 대상 아님.

### 🔴 팀장 액션 2건

1. **SVN 커밋** — `svn commit "Assets/50.Art/Char/Boss/SK/SK_23.fbx.meta"`. git 은 `50.Art` 를 무시한다(`.gitignore:84`)
2. **은희 님께 요청 전달** — 위 요청 문서. `PlayerStateController.cs:760` 이 `GrabController` 를 구체 타입으로 잡아 레거시 폴더 삭제가 막혀 있다. **4줄 변경, 동작 변경 0**

### ⚠️ 이번 세션에 확정된 것

- **리그 스케일 100배 실측 확정** — `hand.l`/`hand.r` 의 `lossyScale (100,100,100)`. 앵커의 **localScale 을 0.01** 로 둬서 `lossyScale = 1` 을 만들었다 → **콜라이더 크기를 미터 그대로 읽고 튜닝**할 수 있다(1/100 값을 안 봐도 된다)
- **보스 리그 경로**: `rig/c_pos/c_traj/forearm.{l,r}/hand.{l,r}` · `…/c_spine_01.x/c_spine_02.x/spine_02.x`
- **앵커 콜라이더는 전부 `enabled: 0`** — `ColliderInfo` 가 형상 데이터로만 읽는다
- **돌진 앵커만 본이 아니라 루트 자식** — 본에 붙이면 박스가 본 회전을 따라가는데 돌진은 보스 정면으로 뻗어야 한다
- **Animator 를 보스 루트에 두면 안 된다** — 점프 체공 중 `animator.transform` 하위 렌더러를 전부 끄므로 방향 표시기까지 사라진다
- 🔴 **레거시 보스의 Avatar 는 `SKM_Golem.fbx`(다른 테스트 모델) 것이었다** — 승계하지 않았다. 새 프리팹은 `m_Avatar: 0`(Wells 와 동일)

### ⚠️ 미확인 — Play 에서 판단할 것

- **애니 재생** — `m_Avatar` 가 null 이다. 같은 fbx 의 클립이라 트랜스폼 경로 바인딩으로 돌 것이나 확인 못 했다
- **어퍼·잡기가 어느 손인지** — 오른손(`Hand_R`)으로 잡은 건 **추정**. 클립을 보고 다르면 SO 에서 `Hand_L` 로 바꾸면 끝
- **히트 타이밍** — 훅·어퍼(≈22f)·대시(≈30f)는 **추정값**. `grab`·`landingattack` 은 아티스트 시간 승계라 신뢰 가능
- **앵커 크기** 1.2m / 돌진 2×2×2.6 은 눈대중 초기값

### 🔴 남은 셰이더 에러 2건은 우리 것이 아니다 (팀장 판단 = 그냥 둠)

`Generic_Basic`·`Generic_Standard` 셰이더그래프가 `WallOcclusionClip.hlsl` 을 못 찾는다. 그 파일은
git `ec1c996`(**`origin/feature/trensparent` 에만**, development 미머지)에 있다. SVN r274/r275 가
셰이더그래프와 `ZoneLayoutCatalog`(존 프리팹 11개 새 GUID)를 가져왔는데 **git 쪽이 안 따라온** 상태다.
영향 = 맵 프롭 머티리얼 3개. **`bossroom` 은 무관**하다. 근본 해결은 `feature/trensparent` 머지.

### 🔴🔴 SVN — **커밋하기 전에 반드시 볼 것**

`svn status` 에 **`MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset` 이 `M` 으로 떠 있는데,
이건 우리가 고친 것이 아니다.** Unity 가 **자기 메모리에 들고 있던 낡은 사본으로 덮어썼다** —
존 프리팹 11개 참조가 r274 이전 GUID 로 **되돌아갔다**.

- 원인: SVN 업데이트로 파일이 바뀌었는데 Unity 가 리프레시를 안 해 낡은 사본을 들고 있었고,
  그 뒤 어떤 계기로 dirty 가 되면서 **자기 것을 디스크에 썼다**(교훈 #69 의 반대 방향).
- 🔴 **이대로 커밋하면 이지원 님의 r274("V3 zone, stage 프리펩들 수정함")를 팀 전체에서 되돌린다.**
- 판단 필요: r275 를 되살리면(`svn revert`) 이 머신에서는 존 프리팹 11개가 **깨진 참조**가 된다
  (그 프리팹들이 git `feature/trensparent` 에만 있어서). 즉 **머지 전까지는 어느 쪽도 완전하지 않다.**
- 권장: **SVN 커밋은 `SK_23.fbx.meta` 만 경로 지정해서** 하고, 카탈로그는 `feature/trensparent`
  머지 시점에 함께 정리한다.

```bash
svn commit "Assets/50.Art/Char/Boss/SK/SK_23.fbx.meta" -m "feat(boss): 23호 클립 애니 이벤트 저작 + idle/charging Loop Time"
```

⚠️ 나머지 14건(노멀맵 7 · 슬로프/계단 fbx 4 등)은 **이전부터 밀려 있던 팀장 작업분**이라 그대로 뒀다.

### 커밋 상태 (2026-08-10 갱신)

**P0~P6 전량 커밋·푸시됨** — 7커밋 `26c70b4`~`66d3741`. 이전에 밀려 있던 50커밋도 함께 올라가
`origin/feature/Boss23` 과 완전히 동기화됐다(ahead 0). 커밋 분할은 주석규약 / 앵커재매핑 /
프리팹4종 / 레거시삭제·송전탑 / 에디터도구 / 문서 / 포그(별건) 순.

🔴 **작업 트리에 의도적으로 남긴 3건 — P7 착수 전에 처리할 것**

| 파일 | 무엇 | 판단 |
|---|---|---|
| ~~`0.Scenes/MonsterScene.unity`~~ | ✅ **해소됨** — P7 저작 도구가 씬을 저장하면서 `FireFloor`·`Bomb` 잔재가 사라졌다(파일에서 두 GUID **0건** 확인). 지금 이 씬의 변경분은 P7 구성이다 | — |
| `0.Scenes/MainFlow/4.MapScene.unity` | `FloatingDamageSpawner` 가 **같은 설정으로 fileID 만 새로 발급**됐다(46+/46−). 기능 변화 0 | 공용 씬에 무의미한 충돌을 만들 뿐이라 커밋하지 않았다. 되돌리는 게 맞다 |
| `ProjectSettings/MultiplayerManager.asset` | `--ignore-all-space` 로 diff 0 = **줄끝만 다시 써짐** | 실질 변경 없음 |

미추적으로 남긴 것: `Docs/superpowers/**`(아트 피치 세션 산출물) · `tmp/` · 레포 루트의
**`작성`(0바이트, 8/7 실수 생성)**.

⚠️ **에디터 밖 YAML 쓰기가 UTF-8 BOM 을 심는다** — `No23.asset`·`bossroom.prefab`·
`TwentyThree.prefab` 3건에 들어가 있었다. Unity 는 **예외 없이 그냥 읽어서** 알아채기 어렵다.
커밋 전에 제거했고(`tail -c +4`), 제거 후 `Assets/Refresh` → 프리팹 정상 해석·컴파일 0/0 확인.
같은 뿌리 = 교훈 #15(인코딩 확인 없이 경계 넘김).

SVN = `SK_23.fbx.meta` **미커밋(팀장 액션, 위 참조)**. git 은 `50.Art` 를 무시한다.

---

## ▶▶ 이전 시작점 — 보스 재작성 (2026-08-08 종료)

🔴 **브랜치 `feature/Boss23`** · **컴파일 0에러 0경고** · **`development` 머지 완료(behind 0)** · **전부 커밋됨**

**한 줄 상태: 코드·애니메이터·데이터·보스 프리팹까지 끝났다. 다음은 `PlayerBossTest` 에서 Play 하는 것이다.**
보스가 **아직 한 번도 스폰된 적이 없다** — `ValidateContract` 가 아직 한 번도 안 돌았다.
그래서 다음 세션의 0번 할 일은 **구현이 아니라 Play 하고 로그를 읽는 것**이다(아래 착수 순서 §0).

> **2026-08-07 밤 갱신** — 아래 **1·2단계는 완료**됐다. 컨트롤러를 고치는 게 아니라 **전면 재작성**으로
> 갔다(팀장 확정). 저작 도구 = `Assets/1.Scripts/Monster/Editor/TwentyThreeBossAuthoring.cs`
> (`Tools > Boss > 23호 — 컨트롤러 전면 재작성 + 데이터 저작`, 멱등).
>
> · `No23Controller` **신규** — 18상태 / 파라미터 `Speed`(Float)·`Groggy`(Bool)·`Death`(Trigger) /
>   전이 **5개뿐**(AnyState⇒Dead, AnyState⇒GroggyStart⇒Groggy⇒GroggyEnd⇒Locomotion). 나머지는 CrossFade.
> · `WellsBossController` **신규** — 4상태 / 트리거 4. 레거시 컨트롤러 2개는 **삭제**했다.
> · 로코모션은 `Speed` BlendTree(idle@0 / walk@2.5) — `_animSpeed` 는 `agent.velocity.magnitude` **원값(m/s)**.
> · 그로기·사망을 **파라미터 + AnyState** 로 받아 base 경로가 살아난다 → **코드·SO 스키마 수정 0줄.**
> · `No23.asset` 저작 완료(`archetype: Boss`, 공격 8행 상태명 전량, 애니 계약 필드 전량).
>
> 🔴 **`attackDuration` 은 올리지 않았다** — 아래 2단계 표의 "상향"은 오독이다. 보스가
> `_stateTimer = 체인길이 + attackDuration` 으로 이미 더한다(`TwentyThreeBoss.cs:456`).
> 🔴 **착지 상태는 원래 있었다** — `Arrive` 가 `landingattack` 을 쓰고 있었다. 다만 입장 연출
> (`BossEncounterDirector`)은 애니메이터를 **한 줄도 안 건드리므로** 고아였다. 새 컨트롤러엔
> `JumpLanding` 으로 다시 뒀다.
> 🔴 **fbx 후속(SVN)**: `Boss_23_idle`·`Boss_23_charging` 의 **Loop Time 이 꺼져 있다** — 오래
> 유지되는 상태인데 한 바퀴 뒤 마지막 프레임에서 굳는다. 애니 이벤트 저작과 같이 처리.
> 상세·이탈 근거 = [IMPLEMENTATION_NOTES.md](IMPLEMENTATION_NOTES.md) 최하단.

### 정본 문서 (읽는 순서)

| # | 문서 | 무엇 |
|---|---|---|
| 1 | [PLAN-boss-fsm.md](PLAN-boss-fsm.md) | 🔴 **구현 정본.** §5.1~5.11 에 슬라이스별 결정·근거·함정이 전부 있다 |
| 2 | [Docs/tech/boss-rebuild-standard.md](Docs/tech/boss-rebuild-standard.md) | 3층 구조·훅·규약·SO 설계(§10). §10.3.1 에 스키마 변경 이력 |
| 3 | [Docs/tech/boss-fsm-detailed-spec.md](Docs/tech/boss-fsm-detailed-spec.md) | 애니 계약(§1) · 점프(§7) · 폭탄/장판(§10.5) · 송전기(§9). **§1.1·§4 는 폐기** |
| 4 | [Docs/tech/handoff-boss-reply-interrupt-restrained.md](Docs/tech/handoff-boss-reply-interrupt-restrained.md) | 은희 회신(개정 1판) — 인터럽트·`Restrained` |
| 5 | [Docs/tech/layer-standard.md](Docs/tech/layer-standard.md) | 레이어 표준 + 이관 완료 기록 |

### ✅ 완료 — 슬라이스 **9개 전부** (코드)

| S1 | S2 | S3 | S4 | S5 | S6-0 | S6 | S7 | S8 | S9 |
|---|---|---|---|---|---|---|---|---|---|
| ✅ | ✅* | ✅ | ✅* | **✅** | ✅ | ✅ | ✅ | ✅ | ✅ |

`*` 부분 보류 2건만 남았다 — 둘 다 **지금은 막혀 있지 않다**:
- **S2 어퍼 에어본** — 팀장 판단 보류. G1 정정으로 구현 자체는 가능하다.
- **S4 Throw 변위** — "변위 경로가 없다"고 적혀 있었으나 은희 머지로 전제가 바뀌었다.
  `Unit.Knockback` 이 임펄스 1회라 **던지기에는 오히려 맞는 모양**이다(지속 밀기가 아니라서
  돌진이 `Restrained.Push` 로 간 것이지, 던지기는 임펄스가 맞다). 재검토 가치 있음 — 미확인.

**S5 돌진 = 캐리-푸시** (2026-08-08 완료). 참조 설계 = 오버워치 라인하르트 돌진:
① 끌고 가는 대상은 **첫 1명뿐**(나머지는 스침 데미지) ② **벽에 처박혔을 때만** 데미지 —
거리 소진이면 기절도 데미지도 없다 ③ 슈퍼아머는 안 밀린다(`BeginRestrainedByInstigator` 의 bool).
🔴 벽 정지는 **콜라이더가 아니라 NavMesh 클램프 앞당기기**로 했다 — 낭떠러지까지 함께 막히고
고속 이동의 트리거 터널링이 없다. 근거·되돌리는 법 = IMPLEMENTATION_NOTES.md.
SO 에 `dash*` 6필드 + `BossAttackPhase.Dash` 추가(끝에). **숫자는 전부 placeholder**(GDD가 TBD).

**신규 파일 15개** (`Assets/1.Scripts/`)

| 위치 | 파일 |
|---|---|
| `Monster/` | `AreaZone.cs` · `AreaZoneType.cs` |
| `Monster/Boss/` | `TwentyThreeBoss.cs` · `BossDataSO.cs` · `BossAttackId.cs` · `BossAttackPhase.cs` · `IBossTelegraph.cs` · `BossCounterTelegraph.cs` · `BossDirectionIndicator.cs` · `BossBomb.cs` · `BossBombState.cs` · `BossWells.cs` · `BossWellsState.cs` · `IBossChargeSequence.cs` · `BossChargeSequence.cs` · `BossChargingPylon.cs` |

**폐기 4파일 삭제 완료** (`BossBase`·`BossState`·`BossBasicAttackType`·`BossBasicAttackChoice`) — 어셈블리에서 사라진 것까지 확인.

### 🔴 base(공유 자산) 변경 4건 — 전부 additive, 기존 11종 기본값 유지

| 파일 | 추가 | 왜 |
|---|---|---|
| `MonsterBase` | `ChaseSpeedMultiplier` | 페이즈 이동속도. **`SeekBoss` 분기에서만** 곱해진다 |
| `MonsterBase` | `AutoHitReactions`(보스만 false) | base 의 자동 피격 반응 **3종**(Hit·그로기누적·Knockback)을 끈다 |
| `MonsterBase` | `ForceHitReaction(duration, groggyAfter)` | `SetState`·`EnterHit` 이 private 이라 **보스가 `Hit` 에 들어갈 방법이 없었다** |
| `MonsterMeleeAttack` | `SetColliderInfo`/`ColliderInfo` | 공격별 히트박스 앵커 스왑 |
| `HitFlash` | `SetBaseTint`/`ClearBaseTint` | 카운터 색이 피격 플래시에 안 지워지게 |
| `AoeTelegraph` | `ShowGrowing` | 점프 착지 예고(시간 성장) — ⚠️ **2026-09-09 이후 호출자 0**(카탈로그 이펙트로 이관) |

### ▶ 다음 세션 착수 순서 — **Play 검증부터**

🔴 **씬: `Assets/0.Scenes/PlayerBossTest.unity`** — 배선을 여기까지 끝내 뒀다. 열고 **Play** 하면 된다.

**1·2단계(애니메이터·데이터) ✅ 완료 · 3단계(프리팹 조립) = 보스 본체만 완료**

#### 0. 첫 관문 — `ValidateContract` 로그 읽기

보스가 **아직 한 번도 스폰된 적이 없다.** Play 하면 처음으로 `ValidateContract` 가 돈다.
그 로그가 남은 작업의 지도다 — 그래서 이게 0번이다.

- 🔴 `LogError` = **남은 배선 목록**. 상태명·파라미터·앵커 중 뭐가 비었는지 이름으로 알려 준다.
- ⚠️ **정상인 경고 3건** — 다음 단계 작업이라 지금 뜨는 게 맞다:
  · `BossWells 자식이 없어 폭탄 살포가 돌지 않는다`
  · 방향 표시기 안 그려짐(호 머티리얼 미저작)
  · 폭탄·장판 안 나옴(SO 의 `bombPrefab`·`chargeZonePrefab` 이 비어 있다)
- 🔴 **의심할 것 = NavMesh.** 이 씬의 `NavMeshSurface` 가 **다른 씬(KMKScene)의 베이크 데이터**를
  참조한다. `not close enough to the NavMesh` 가 뜨면 스폰 위치 `(10,0,10)` 가 NavMesh 밖이라는
  뜻이고, 그러면 보스가 서 있기만 한다.
- 단독 Play 로 볼 수 있는 것 = **스폰 · 계약 · 이동 · 공격 선택**까지.
  **상태이상(기절·그로기)은 MPPM 2인에서만** 걸린다(`CanWrite = IsSpawned && IsServer`).
  단독 Play 로 "기절이 안 된다"를 버그로 오진하지 말 것.

#### 🔴 히트박스 전면 재설계 — 팀장 확정 (2026-08-08). **Play 보다 먼저 판단할 것**

**현재 프리팹의 히트박스는 레거시 승계분이고 새 설계와 맞지 않는다.** in-place 교체를 하면서
리그·앵커·콜라이더를 그대로 물려받았는데, 값이 애니메이션에 맞춰 튜닝된 것이 아니라
대충 놓은 라운드 값(`2×3×2` / center ±1)이었다. **승계 사실을 검증 없이 "재사용 이점"으로 넘긴 것이 실수다.**

실측값:

| 앵커 | 콜라이더 | 문제 |
|---|---|---|
| `LeftHookAttack` | Box 2×3×2 center(-1, 1.5, 1) | 우훅·어퍼와 볼륨이 거의 겹친다 |
| `RightHookAttack` | Box 2×3×2 center(+1, 1.5, 1) | 〃 |
| `UpperAttack` | Box 2×3×2 center(0, 1.5, 1) | **두 훅 안에 완전히 포함**된다 |
| `Grab` | Box 1.2×3×1 center(0, 1.5, 0.5) | 유일하게 `enabled=0`(형상 전용) — **이게 정답 형태** |
| `DashAttack` | Sphere r=1.25 center(0, 1.71, 0) | **보스 자기 중심**. 앞으로 안 뻗어 캐리-푸시와 안 맞는다 |
| `Rage` | Sphere r=1.25 center(0, 1.71, 0) | **DashAttack 과 완전 동일**(강화판이어야 하는데) |

앵커가 전부 `pos(0,0,0)` 이라 **본에 안 붙어 있고 보스 루트 기준 고정 볼륨**이다. 주먹을 안 따라간다.
게다가 레거시는 공격마다 검출 방식이 달랐는데(`ColliderBasicAttack` / `KnockbackAttack` / **트리거**
`TriggerKnockbackAttack`) 지금은 전부 `MonsterMeleeAttack` 오버랩 하나로 바뀌었다 —
**모양은 그대로인데 의미가 바뀌었다.**

**확정된 새 설계:**
- **훅·어퍼·잡기 → 손 본에 붙인다.** 넷 다 손에서 처리되므로 고정 볼륨 4개는 중복이다.
  좌손/우손 앵커 2개로 줄이고, 어퍼·잡기가 어느 손인지는 클립을 보고 정한다.
- **돌진 → 몸통에 콜라이더를 따로** 둔다.
- **점프 → 바닥 원.** 이미 코드가 `jumpAoeRadius` OverlapSphere 로 처리한다 → **앵커 불필요**.
- 그에 맞춰 `No23.asset` 의 `hitboxAnchorName` 매핑도 다시 잡는다(현재는 레거시 앵커 6개를 가리킨다).

**함께 지울 레거시 서브트리** — 전부 새 코드가 대체했다:
`JumpAttack/FloorRoot/FloorBase`·`FloorGrow`(예고 장판 스프라이트 → `AoeTelegraph` 가 대체) ·
`ChargeAttack/Floor`(차징 장판 + 공격 2종 → `AreaZone` 이 대체) · `ColliderBasicAttack` ×4 ·
`KnockbackAttack` ×4 · `TriggerKnockbackAttack` · 컴포넌트 제거 후 남은 빈 껍데기 `Initializer`

🔴 **본에 붙일 때 함정**: `rig` 의 **스케일이 100배**, 회전이 (270.02, 0, 0) 이다.
본 아래에 콜라이더를 넣으면 그 스케일을 상속하므로 **크기를 1/100 로 넣어야** 의도한 월드 크기가 나온다.

리그는 Auto-Rig Pro 계열(`c_` 접두사 · `.x`/`.l`/`.r` 접미사)이고 경로는
`Components/Amature/rig/c_pos/c_traj/c_root_master.x/…` 다(`c_root_master.x` 의 자식 3개 중 하나가
중첩 `Wells`). ⚠️ **정확한 손 본 이름은 Hierarchy 창에서 눈으로 확인할 것** —
MCP 의 `unity_get_hierarchy` 는 **3단계까지만** 열거해서 본이 안 보인다.

#### 나머지 승계분 — 아직 미검증 (같이 점검할 것)

| 대상 | 현재 | 상태 |
|---|---|---|
| `HurtBox` | Box 2.03×3.48×1 center(0.014, 1.43, 0) | 보스 몸 크기와 맞는지 **미검증** |
| `NavMeshAgent` | — | ✅ 무관 — `ServerInitialize` 가 `speed`/`radius`/`stoppingDistance` 를 SO 값으로 덮어쓴다 |
| `Rigidbody` | — | **미검증** |
| 웰즈 손 소켓 | — | 아직 없음(웰즈 조립 때) |
| `FD_Anchor` | y=3.5 | 은희 것 — 그대로 둔다 |

#### 씬별 상태

| 씬 | 상태 |
|---|---|
| `PlayerBossTest.unity` | ✅ **배선 완료** — 기둥 4개 교체 + 스폰 주체 정리. **여기서 시작한다** |
| `BossScene.unity` | ❓ 미확인. 같은 기둥 교체가 필요할 수 있다 |
| `4.MapScene.unity` | 실제 맵. `bossroom.prefab` 의 `Env_Mv_bosscharger_upper` **4개**를 교체해야 한다 |

#### 남은 조립 4건

| 대상 | 할 일 |
|---|---|
| **웰즈** | `Wells.prefab` 이 아직 레거시(`BombLauncher`·`WellsAnimEvents`) → **`BossWells`** 로 교체 + 손 소켓 |
| **폭탄** | `Bomb.prefab` 이 아직 레거시(`Bomb`·`BombController`·`BombAreaEffect`) → **`BossBomb`**. `Rigidbody`(useGravity on / FreezeRotation / ContinuousDynamic) + `Hurtbox`(**ownerUnit 비움** → `IAttackReceiver` 폴백) + NetworkPrefabs 등록. ⚠️ **정지해도 논키네마틱 유지**(재우면 당구가 안 된다) |
| **장판** | 프리팹이 **아예 없다** — 신규 생성: `NetworkObject` + `AreaZone` + 비주얼 자식(로컬 XY 지름 1) + 레이어 **`HazardArea(9)`** + NetworkPrefabs 등록 |
| **송전탑** | `bossroom.prefab` 의 4개 교체(위 씬 표 참조) |
| **프로젝트 설정** | 🔴 충돌 매트릭스에서 **폭탄 레이어 ↔ 유닛 레이어 물리 응답을 끊을 것**(유닛 감지는 트리거로 하므로 폭발은 산다) |

#### 🔴 저작 도구 3개 — **수명에 주의**

전부 `Assets/1.Scripts/Monster/Editor/` 에 있다. 파괴적이라 **언제 그만 써야 하는지**가 중요하다.

| 도구 | 성격 | 언제 그만 쓰나 |
|---|---|---|
| `TwentyThreeBossAuthoring` | 컨트롤러 2개를 **매번 지우고 새로 만든다**(GUID 도 매번 바뀐다) + `No23.asset` 저작 | 🔴 **애니메이터를 손으로 튜닝하기 시작하면 다시 돌리면 안 된다** — 손댄 것이 전부 사라진다. 그 시점에 컨트롤러 생성부를 지우고 **SO 저작만 남길 것** |
| `BossPrefabAuthoring` | 보스 프리팹 컴포넌트 교체. **사실상 1회용** — 이미 끝났다 | 프리팹이 깨졌을 때 복구용으로만 남긴다. 4단계 레거시 정리 때 함께 삭제 |

두 도구 모두 **검증 전용 메뉴**가 따로 있다(`… — 검증만`). 파괴적 메뉴를 누르기 전에 그걸로 먼저 읽을 것.
멱등하게 짜여 있어 여러 번 눌러도 결과는 같다(`= 이미 …` 로 지나간다).

#### 조절해서 처리하는 값 — 코드 수정 불필요

전부 인스펙터에서 만진다. **GDD 가 TBD 로 비워 둔 구간이라 현재 값은 거의 placeholder 다.**

- **`No23.asset`** — `dash*` 6필드(S5 신규) · `jump*` · `grab*` · `charge*` · `rageDash*` ·
  `bombThrowInterval`/`throwImpulse`/`spreadAngle`/`bombThrowPitch` · 공격 행별 `cooldown`·`weight`·거리창·`damage`
- **프리팹** — `BossChargingPylon.maxHp`(200)·`defense` · `BossDirectionIndicator` 반지름/색/세그먼트 ·
  `BossChargeSequence` 여유시간·선택 기준
- **코드 상수 2개**(눈으로 보고 맞춰야 하는 값) — `DashCarryProbeRadius`(1.2) · `DashCarryWallMargin`(0.6)

#### 🔴 SVN 후속 (git 아님 — 팀장이 "추후"로 확정)

- `Boss_23_idle`·`Boss_23_charging` 의 **Loop Time 이 꺼져 있다** — 로코모션과 차징(최대 20초)이
  한 바퀴 뒤 마지막 프레임에서 굳는다. 저작 도구가 실행할 때마다 경고로 다시 알린다.
- 애니 이벤트 `OnAttackHit`/`OnAttackEnd` 저작 — 없으면 공격이 `attackDuration` 타임아웃으로만 끝난다.
  ⚠️ Wells 클립 이벤트 이름은 **fbx 에 이미 박혀 있다**(`ThrowBombEvent`/`BombDestroyEvent`). 바꾸면 조용히 무시된다.

#### 4단계 이후

S2 어퍼 에어본(팀장 판단 대기) → S4 Throw 변위 재검토(`Unit.Knockback` 임펄스가 던지기엔 오히려
맞는 모양이다 — **미확인**) → **어색한 것들 수정**(팀장 목록 대기) → MPPM 2인 검증 →
레거시 `Enemy/Boss`·`8.BehaviorTreeGraph` 삭제.

⚠️ 그때 `TwentyThreeArenaContext` 는 **삭제가 아니라 `Monster/Boss/` 로 이동**해야 한다 —
씬 3개가 이 컴포넌트를 참조한다. 파일을 옮기면 `.meta` 가 따라가 GUID 가 유지되므로 참조는 안 깨진다.

### 은희 의존 — ✅ **R1·R2 둘 다 종결. `development` 에 머지 완료**(`a75398c`, 2026-08-07)

브랜치 `feature/InterruptSkill-CarrySocket` — `feature/maprendering` 이 아니라 **공통 조상 `cbb51b1`**
(PR #10 머지 지점)에서 갈라졌다. 회신 문서 = `Docs/tech/player-interrupt-restrained-handoff.md`.
**합의한 계약대로 왔다 — 수정 요청할 것 없음.** 코드에서 대조한 결과:

| 계약 | 실물 |
|---|---|
| R1 인터럽트 식별자 | `AttackInfo.isInterruptAttack` (bool 플래그, enum 아님) |
| R2 `Restrained` | `RestraintMode { Carry = 0, Push = 1 }` |
| Push 만 슈퍼아머 거부 + `bool` 반환 | `PlayerStateController.cs:149` |
| **S4 Grab 무수정 보장** | `BeginGrabbedByInstigator`/`EndGrabbedByInstigator` **래퍼 유지** |

**내가 할 일 2건**
- `TwentyThreeBoss.IsInterruptAttack` 의 `isGroggyAttack` → **`isInterruptAttack`** (한 단어).
- **S5 Dash 착수 가능** — `BeginRestrainedByInstigator(gameObject, RestraintMode.Push, frontOffset)`.

🔴 **머지 충돌은 `MonsterBase.cs` 딱 1파일**(양쪽이 동시에 건드린 유일한 파일). 해소 = 둘 다 살리기
(이름은 은희 쪽, `if (!AutoHitReactions) return;` 는 보스 쪽, **조기 반환이 먼저**).
추가로 내가 삭제한 `BossBase.cs` 를 은희가 리네임 때문에 건드려서 **delete/modify 충돌**이 뜬다 →
**삭제를 채택.** `AttackType` 축소(`None/Default/Skill`)는 내 코드에 무영향(`Default` 만 쓴다).
- 🔴 **머지 충돌 1곳**: `MonsterBase.TakeDamage` 의 `isGroggyAttack` 줄. 해소 = **둘 다 살리기**
  (이름은 은희 쪽, `if (!AutoHitReactions) return;` 는 보스 쪽, **조기 반환이 먼저**).
  순서는 은희 → `development` → 보스가 받는 방향(충돌 면적이 작다).

### ⚠️ 이 세션에 확정·정정된 것 (상세는 PLAN 링크)

- **뒤집힌 것 3건**: ① "돌진 = 매 틱 넉백 재적용" → **`Restrained.Push`**(`Unit.Knockback` 은 duration 없는 임펄스 1회라 누적되면 플레이어가 튀어나간다) ② **슈퍼아머로 돌진 버티기 = 의도**(기획 회의) → `Push` 에 슈퍼아머 검사 + `bool` 반환 요청. 슈퍼아머면 **밀림✕/기절✕/데미지○** ③ **"플레이어 CC 경로 없음"은 절반 틀렸다** — 상태이상은 `Unit.StatusEffects.Apply` 로 **오늘 가능**, 막힌 건 **변위뿐** (→ PLAN §5.1 G1 정정)
- **"도달"은 송전기 실패 조건이 아니다** — `ReachEvent` 는 "상승 완료"라 모든 기둥이 반드시 도달한다. 실패는 **제한시간 초과 단독** (→ PLAN §5.11)
- **JumpAttack 의 빨간 장판은 예고 표시이지 `AreaZone` 이 아니다** — 섞지 말 것
  (⚠️ 2026-09-09 이후 그 예고는 `AoeTelegraph` 가 아니라 `EffectCatalog.Drop_Charge_*` 루프 이펙트다)

### 🔴 알아 둘 것 (함정)

- **`git status` 가 `Docs/` 변경을 숨긴다** — `core.fsmonitor` + `Docs` 정션. **`git -c core.fsmonitor=false` 로 커밋할 것**
- **컴파일 판정은 MCP 응답이 아니라 산출물로** — `Assembly-CSharp.dll` mtime > 최종 소스 + **dll 안에 새 타입 실존**(`grep -a`). MCP 의 `success:true` 는 접수 확인일 뿐이고, `Editor.log` 엔 직전 실패의 에러가 남는다(교훈 #61)
- **보스 검증은 MPPM 2인 이상에서만** — 오프라인/미스폰이면 `CanWrite = IsSpawned && IsServer` 라 **상태이상이 안 걸린다.** 단독 Play 로 "기절이 안 된다"를 버그로 오진하지 말 것
- **파생에 `Update()` 를 선언하면 `MonsterBase.Update` 를 가려 FSM 이 통째로 멈춘다**
- **enum 값 추가는 끝에만** — `MonsterArchetype`·`BossAttackId`·`AreaZoneType` 전부 SO 에 정수 직렬화
- **애니·앵커 접근이 graceful 이라 이름 오타가 무증상** — 죽은 설정값 9건 실재(교훈 #59). `ValidateContract` 가 스폰 시 잡는다
- **Codex 는 숫자·경로를 지어낸다** — 결론만 채택하고 재확인

---

## 이전 인수인계 (2026-08-07 · 보스 FSM 지원 2건 — 인터럽트 식별자 + 캐리 소켓)

작업 세션: **은희(Claude)**. 워크트리 `C:\UnityProject\MainProject-WorkTree`,
브랜치 `feature/InterruptSkill-CarrySocket` (base `MainProject/development` `6dbc1c34a`).
요청 출처: 경석 인계문 `Desktop\handoff-player-carry-socket.md` (기한 8/7 17:00).

**수정 중 (동시 편집 금지)**: `Assets/1.Scripts/Unit/Weapon/BaseAttack.cs`(enum 1줄) ·
`Assets/1.Scripts/Unit/ICarrySocketProvider.cs`(신규) · `Assets/1.Scripts/Enemy/Boss/GrabController.cs`(인터페이스 구현) ·
`Assets/1.Scripts/Player/{Player.cs, PlayerStateController.cs}` ·
`Assets/1.Scripts/Player/Skill/{PlayerSkillBase.cs, FirstMeleeMainSkill.cs, FirstMeleeInterruptSkill*.cs(신규)}` ·
`Assets/2.Prefabs/Player/Paladin/Paladin.prefab`

설계·완료조건은 [PLAN.md](PLAN.md) 최상단 참조. 이번에 확립된 계약만 적는다:

- 🔴 **인터럽트는 `AttackInfo.isInterruptAttack`이 싣는다**(기존 `isGroggyAttack` 개명).
  `AttackType`은 "어느 출처가 쐈나"라 인터럽트와 **직교**한다 — enum 값으로 넣으면 안 된다.
  **플래그는 하나뿐이고, 소비 방식은 수신측이 정한다**: 몬스터/중간보스 = `maxGroggyCount` 누적→그로기,
  보스 No.23 = 카운터 창·정면 각도(경석). `Docs/design/level-system.md:70`의 분담과 같다.
- 🔴 **`AttackType` = `{None=0, Default=1, Skill=2}`로 축소됐다**(Q/E/R 제거 — 구분해 읽는 코드가 없었다).
  `BaseAttack.attackType`·`Bomb.attackType`이 `[SerializeField]`라 **정수값 0·1은 고정**이다.
  특히 **`Bomb.attackType=1`(Default) = 폭탄은 평타에만 반응한다** — 값이 밀리면 이 기믹이 조용히 뒤집힌다.
  **값은 끝에만 추가할 것.**
- **`BaseAttack`엔 인터럽트 저작 토글이 없다.** 켜는 주체는 스킬뿐이고 스킬은 `BaseAttack`을 안 탄다.
  적 공격도 인터럽트를 걸어야 하면 그때 `[SerializeField] bool` + 생성자 인자를 되살린다(3줄).
- 🔴 **플레이어 스킬은 `BaseAttack`을 타지 않는다.** `AttackInfo`를 직접 만들어 `Hurtbox/Unit.ReceiveAttack`을
  부른다(서버 전용 경로). 인계문의 "BaseAttack.attackType을 지정" 전제는 이 코드베이스에 없는 경로였다.
- 🔴 **`PlayerActionState.Grabbed` → `Restrained`.** 서버가 플레이어 위치·입력을 잠시 통제하는 상태를
  **한 곳으로 묶었다**: `RestraintMode{Carry=잡기(소켓 종속), Push=돌진 밀기(시전자 정면 추종)}`.
  `PlayerGrabbedState`→`PlayerRestrainedState`, `GrabInteractionContext`→`RestraintContext`,
  `IGrabInteractionReceiver`→`IRestraintReceiver`. (원 요청이던 캐리 소켓 일반화 `ICarrySocketProvider`는 **폐기**.)
  - **`Push`는 소켓이 필요 없다** — 시전자 루트가 `NetworkTransform`으로 복제되므로 `position + forward × offset`이
    오너 클라에서도 성립한다. Y는 진입 시점 값으로 고정(피벗 높이 차이로 뜨거나 잠기는 것 방지).
  - **`Push`만 슈퍼아머를 거부**하고 `Carry`는 안 한다(넣으면 보스 Grab 체인 회귀). 판정은 **서버 진입에서만** —
    오너가 다시 판정하면 복제 지연 시 상태가 갈린다.
  - **`BeginRestrainedByInstigator`의 `bool` 반환이 계약이다** — 시전자는 이 값으로 후처리를 가른다
    (실제로 밀린 대상만 기절). `BeginGrabbedByInstigator`는 `Carry` 래퍼로 유지 → `GrabController` 무수정.
  - 🔴 **`followTarget == null` 널 허용을 깨지 말 것** — 위치 추종만 건너뛰고 물리 위임·입력 차단은 유지된다.
    보스에 잡기 소켓이 아직 없어서 "제자리에 붙잡힘"이 이 성질로 성립 중이다.
- 🔴 **`Unit.Knockback`은 임펄스 1회다** — duration 개념이 없다. `AttackInfo`의 `knockbackDuration`·
  `staggerDuration`은 `MonsterBase`만 소비하고 **플레이어 수신 경로는 무시**한다. 보스 돌진이 넉백 대신
  `Restrained.Push`로 간 이유다.
- 🟡 **잡기와 돌진 캐리는 `PlayerActionState.Grabbed` 하나를 공유한다.** 캐리 중 재진입은 조용히 거부되고
  (`CanReceiveGrab`), `EndGrabbedByInstigator()`는 시작 주체를 구분하지 않는다. 보스 1기 기준 무해.
- 🟡 **단죄의 방패는 이번에 처음 구현됐다** — 그 전까지 우클릭은 `PlayerInterruptState`(전방 돌진, **데미지 0**)였다.
  거동 스펙이 `Docs/design/`에 없어 "짧은 전방 강타 + Interrupt 태그"로 가정했다(PLAN 「명시적 가정」).
  판정 타이밍은 **애니 클립 수정 없이** SO 타이머(`hitDelay`)로 잡고, Hit 애니 이벤트가 나중에 심어지면
  그쪽이 우선하되 **1회만** 발동한다.
### 이전 렌더링 인수인계 보존본 (2026-08-05)

팀장이 AI 생성 레퍼런스 이미지를 **목표 룩**으로 제시했다(2026-08-05, 채팅 첨부).
탑다운 쿼터뷰 고정 / 셀셰이딩 캐릭터 + 회화풍 배경 / 부드러운 접지 그림자 +
따뜻한 국소 조명(가로등·창) / 물·금속 반사 / 원경 디포커스 / 채도 높은 소품.
이미지에는 HUD 배치안(특성·강화·스탯 육각 슬롯, 하단 스킬 슬롯, HP/MP 바, 남은 시간,
우상단 미니맵)도 손으로 얹혀 있으나 **HUD 는 이 세션 범위와 별개 트랙**이다.

🔴 **레퍼런스 이미지는 아직 채팅에만 있다 — `Docs/design/` 에 저장할 것.**

- **카메라 조절은 완료** — FOV 52→34 / Priority 100 (`0522731`, development 에 포함).
  이번 세션은 카메라가 아니라 **셰이딩·조명·포스트프로세싱**이다.
- 승인된 계획서는 `PLAN.md` 가 아니라 **`PLAN-vision.md` §7** 이다(§4 단계 2~3 재개).
- 선행 참고: 툰셰이더 현황(`e5cc012`) · 벽 차폐/투명화 · `Docs/tech/fog-system.md`.

### ▶▶ 다음 세션 시작점 — Play 테스트부터 (2026-08-05 마감, `317a2a1`, **미push ahead 10**)

**코드 작업은 일단락됐고 남은 것은 육안 검증이다.** 컴파일 0에러/0경고, 콘솔 에러 0건,
`svn status Assets/50.Art` missing 0건, 배선 14개 항목 전수 확인 완료.

**이번 세션에 닫은 것**

| 닫은 것 | 커밋 |
|---|---|
| 그레이딩 LDR→HDR + 톤매핑·화이트밸런스·컬러조정·비네트 도입 | `77e6063` |
| 벽 투명화 복원 + **포그 매니저 OFF** | `3b2d07f` |
| **카메라 포스트프로세싱 활성** + TAA | `0dfec30` |
| 벽 투명화 대상을 Stage1 로 한정 + AA·디더 토글 분리 | `82b079b` |
| ProfilerHUD 배치 + DoF Bokeh | `421b844` |
| **화면공간 마스크 블러** 신설(셰이더·피처·컨트롤러·설정) | `e07d2ac` |
| 마스크 블러 `size.x` 무시 수정 + 성능 실측 기록 | `959b8c2` |
| HUD 마커를 마스크 블러 4패스로 교체 | `317a2a1` |

**AA 결정 = SMAA** (2026-08-05 팀장 판단). TAA 와 큰 차이를 못 느꼈고,
**고스팅이 없는 쪽이 맞다**는 판단이다. 대시 잔상은 TAA 부산물이 아니라 **대시 전용
VFX 로 따로 넣는다** — 그래야 통제가 된다. `m_Antialiasing: 2` 적용됨.
🔴 SMAA 에서는 `WallOcclusionSettings.animateDither` 를 켜면 안 된다(기본값 OFF, 유지).

**다음 세션 목표 — 두 룩을 키 하나로 비교 (팀장 지시, 2026-08-05)**

지금은 채도를 살리는 방향으로 왔지만, 원래는 **어둡고 디밍이 들어간 상태**였다.
그쪽에 **픽셀레이트 + 블러**까지 얹은 것과 지금 화면을 **키 입력 한 번으로 전환**해
직접 비교할 수 있게 만든다.

| | A (현재) | B (비교 대상) |
|---|---|---|
| 채도·톤 | 살림 | 저채도 |
| 디밍 | 없음 | 있음 |
| 블러 | 마스크 블러 | 마스크 블러 |
| 픽셀레이트 | 없음 | **있음(신규)** |

착수 전 알아 둘 것:

- 🔴 **런타임에 `volume.sharedProfile` 값을 바꾸면 에디터에서 애셋이 영구 수정된다.**
  Play 를 끝내도 남는다. 씬에 Volume 을 2개 두고 weight 를 토글하거나
  `volume.profile`(런타임 클론)을 쓸 것.
- 🔴 **디밍을 되살리려고 `FogManager` 를 통째로 켜면 시야 제한(LoS)도 같이 돌아온다.**
  `dimEnabled` 와 `losEnabled` 는 독립 토글이므로 디밍만 원하면 `losEnabled: 0` 으로 둔다.
  (현재 값: `fogEnabled 0` / `dimEnabled 1` / `losEnabled 1`, 컴포넌트 자체가 OFF)
- 두 룩이 갈리는 축이 4개다(볼륨 값 · 디밍 · 마스크블러 설정 · 픽셀레이트).
  **"룩 프리셋" ScriptableObject 하나로 묶어 통째로 스왑**하는 편이 토글이 단순해진다.
- 픽셀레이트는 마스크 블러와 같은 계통의 풀스크린 패스다 — `MaskBlurFeature` 구조를
  그대로 재사용할 수 있다. ⚠️ **순서를 정해야 한다**: 블러→픽셀레이트면 블록 경계가
  또렷하고, 픽셀레이트→블러면 블록이 뭉개진다. 원하는 그림이 어느 쪽인지 먼저 결정.
- 토글 키는 **F8 을 피할 것**(ProfilerHUD 가 쓴다). 이 프로젝트는 신 Input System 이다.

**그 외 남은 것**

1. **Play 육안 검증** — 마스크 블러의 `feather`(0.35)·`blurStrength`(1)·`roundness`(4)
   는 전부 추정값이고 화면으로 확인한 적 없다.
2. **저사양 성능 재측정** — 팀장 PC 는 GPU 2.89ms 로 여유롭지만 고사양 기준이다.
   비싸면 `downsampleShift` 1→2 → `blurStrength` 하향 → 패스 축소 순.
3. 남은 격차 = **국소 조명**(가로등·창). 라이트맵 방향이 정해져야 착수 가능(아래 미해결).

**🔴 미해결 / 결정 대기**

- **라이트맵 베이크가 현재 맵 구조와 충돌한다.** `MapContentSpawner.cs:62` 가 존 레이아웃
  프리팹(바닥·벽 포함)을 런타임 `Instantiate` 하는데, 라이트맵 데이터는 씬 렌더러에
  직렬화되므로 런타임 생성물은 받지 못한다. 씬의 static 플래그도 0개다. 게다가 슬롯
  위치가 시드마다 셔플된다. 선택지 = 프리팹 라이트맵 베이크 / 라이트 프로브·APV /
  Stage1 만 굽고 생성물은 실시간. **조명 단계 전에 방향 결정 필요.**
- **퓨즈박스가 어둡다 — 미해결.** `Level_wall_hallway.prefab`(Stage1 에 13개)이 쓰는
  `MA_prop03` 은 `prop03_basecolor` + `pipe_basecolor` 두 장인데 오클루전 변종은 한 장만
  가져간다. 변종 14개 중 4개가 3~5장 → 1장으로 붕괴하고, **13개는 노멀맵을 잃었다**
  (노멀 텍스처 5개가 `textureType: 0`=Default 로 임포트돼 `IsNormalMap()` 이 false 를
  반환하기 때문 — 팀장이 재임포트 예정). 근본 해결은 변종 머티리얼을 없애고 원본
  ShaderGraph 에 디더를 심는 것. `50.Art` = SVN 이라 단일 담당 필요.
- ⚠️ **`ProfilerHUD` 는 제출 빌드 전에 MapScene 에서 제거할 것.** 클래스가
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 로 감싸져 있어 릴리스 빌드에서 missing script 가 된다.
- 🔴 **`Paladin.prefab` 의 `PlayerInput` 이 `m_Enabled: 0 → 1` 로 켜져 있다(미커밋).**
  `Player.cs:146` 주석대로 **프리팹 기본값은 비활성이 맞고**, `EnableLocalInput()` 이
  로컬(오너/오프라인)만 켠다 — 원격 클론이 디바이스 페어링을 시도해
  "Cannot find matching control scheme" 경고를 내는 것을 막기 위한 설계다.
  `Player.prefab` 은 여전히 `0` 이라 Paladin 만 어긋났다. **커밋하지 말고 되돌릴 것:**
  `git checkout -- Assets/2.Prefabs/Player/Paladin/Paladin.prefab`

**미커밋으로 남긴 것** — 전부 팀장이 인스펙터에서 튜닝 중인 값이라 손대지 않았다:
`Global Volume Profile`(비네트 색 warm→진회색, 강도 0.606 / 대비 -3.7 / 채도 -11.6 —
내가 넣었던 +12·+18 이 과해서 되돌린 것) · `MaskBlurSettings`(`size.x` 0.65) ·
`Paladin.prefab`(위 항목, 되돌릴 것) · 줄바꿈 노이즈 2건.

**이 세션에 확립된 것**

- 🔴 **"적용했는데 화면이 그대로"가 세 번 나왔고 원인이 매번 달랐다.**
  ① `VolumeProfile.Add<T>()` 는 서브에셋 등록을 안 해 저장 시 `{fileID: 0}` 널이 된다
  (게다가 널이 되기 전 메모리 인스턴스를 `TryGet` 이 찾아 "이미 있음"으로 오판한다 —
  판단 기준은 존재 여부가 아니라 `AssetDatabase.Contains`).
  ② **카메라 포스트프로세싱이 꺼져 있었다**(`m_RenderPostProcessing: 0`).
  ③ `MaskBlurSettings.ResolveSize` 가 `size.x` 를 y 에서 유도해 인스펙터 값을 버렸다.
  **공통 교훈: 도구가 "성공" 로그를 찍어도 산출물 파일을 직접 열어 확인할 것.**
- 🔴 **게임플레이 카메라는 씬에 없다.** `4.MapScene` 의 Camera 는 0개이고
  `CameraTargetSwitcher.cs:132` 가 `CameraSwitcher.prefab` → `MainCamera.prefab` 을 런타임
  `Instantiate` 한다. 그 프리팹은 `CamaraScene`·`PlayerScene`(테스트 씬)에서만 직접 쓰이므로
  **씬만 훑으면 실제 카메라 설정에 도달하지 못한다.**
- ⚠️ **`FogManager` 는 이름과 달리 포그가 범인이 아니다.** 씬 값이 `fogEnabled: 0` 이라
  포그는 안 그려지고 있었고, 화면을 어둡게 만든 것은 `dimEnabled: 1` + `losEnabled: 1`
  (`losMaxDist: 4`)이다. 지금 OFF 라 **먼 거리 적이 전부 보인다** — 게임플레이 영향 있음.
- ⚠️ **탑다운에서 DoF 로 화면 외곽을 흐리게 할 수 없다.** DoF 는 깊이로 판단하는데
  좌우 외곽은 중앙과 깊이가 같다. 그래서 화면공간 마스크 블러를 새로 만들었다.
- ⚠️ **벽 투명화의 디더 무늬는 품질 결함이 아니라 의도된 비용 절충**이다(반투명 블렌딩
  대신 `clip()` 으로 불투명 큐 한 패스). 보기 싫다고 끄지 말 것 — 끄면 벽 뒤 플레이어가
  그냥 안 보인다. 디더+TAA 로 녹이는 시도는 **실패했다**(TAA 분산 클램프가 디더 변동을
  기각해 뿌연 얼룩이 된다). 2안 = MSAA + `AlphaToMask`.
- ✅ **렌더러 피처를 코드로 추가할 때는 `AddObjectToAsset` + `m_RendererFeatureMap` 정합
  확인이 필수다.** 맵은 features 개수와 길이가 맞아야 한다(long 1개 = hex 16자).
  URP 의 `ValidateRendererFeatures()` 가 재계산하지만 `internal` 이라 리플렉션으로 깨웠다.
- ✅ 셰이더는 반드시 **직렬화 참조**로 물릴 것(`MaskBlurFeature._shader`). `Shader.Find`
  만으로는 빌드에서 스트립된다(미니맵 전례).

### 브랜치 정리 (세션 시작 시)

- `feature/map-player-merge` 삭제(`8e5a2ae`) — `origin/development` 에 전부 포함된 것을
  확인한 뒤. `ahead 2` 는 **자기 원격 ref 가 낡아서 생긴 착시**였다.
- 로컬 `development` 를 `origin/development`(`cbb51b1`) 로 갱신 후 거기서 분기.
  `fix/AlphaAlert` 에는 development 에만 있는 커밋 12개가 빠져 있었다(ConveyorBelt 정리,
  진입점 단일화, 미니맵 머티리얼 배선, 루프백 바인딩 수정, 50.Art gitignore 통일 등).
- 🔴 **git↔SVN 하이브리드 함정 — "git 추적 해제" 커밋을 가로지르는 체크아웃은 SVN 소유
  파일을 디스크에서 지운다.** development 의 `5cd384f`(SVN 소유 아트 `.meta` 140건 추적 해제)를
  넘어오면서 git 이 `TestAssets/Temp_Images` 아래 `.meta` 5개를 **삭제**했다. 그대로 Unity 를
  켰으면 GUID 재발급으로 참조가 깨졌을 것이다. SVN 이 `!`(missing) 로 잡고 있어 `svn revert`
  로 복구, GUID 가 git 원본과 일치하는 것까지 확인했다.
  **브랜치를 갈아탄 뒤 Unity 를 켜기 전에 아래를 볼 것:**
  `svn status Assets/50.Art | Select-String "^!"`

---

## 이전 인수인계 (2026-08-05 · 지연 체력바 — Claude → Codex 위임)

작업 세션: **은희(Claude → Codex 위임)**. 레인 `dash` = `C:\UnityProject\MainProject`,
브랜치 `feature/DelayedHealthBar` (base `Convayor-V2`).

**Codex가 수정 예정 (Claude·타 작업자 동시 편집 금지)**:
`Assets/1.Scripts/UI/Combat/DelayedHealthBar.cs`(신규) ·
`Assets/1.Scripts/Unit/Unit.cs`(이벤트 2줄) ·
`Assets/1.Scripts/UI/Combat/PlayerHealthHUD.cs` · `Assets/1.Scripts/UI/Combat/BossHealthHUD.cs`

설계·완료조건은 [PLAN.md](PLAN.md) 최상단 참조(중복 기재하지 않음). 요지만:
피격 시 잔상 바가 옛 HP에 0.4초 머문 뒤 고정 속도로 따라 내려온다. 피해 조각을 `Queue`로
붙잡고 `maxHeldHits=5` 초과 시 오래된 것부터 놓아준다(보스는 홀드 리셋 off) — 지속 피해에
잔상이 영구 고착하는 것을 막는 장치다.

프리팹 배선(`CombatHUD.prefab` · `BossHealthHUD.prefab`에 잔상 Image 추가·연결)은 **은희가
Unity에서 직접** 한다. Codex는 `.cs` 4개만 건드리고 프리팹·씬·`.meta`는 손대지 않는다.

⚠️ `CLAUDE.local.md`의 레인 표는 낡았다 — `soul`/`MainProject-BeaverLobby` 워크트리는 없고,
현재 살아있는 워처는 `dash`(MainProject)와 `fd`(MainProject-WorkTree, `feature/FloatingDamage`) 둘이다.

---

## 이전 인수인계 (2026-08-06 · 렌더링 룩 A/B + 픽셀레이트 + 어비스 복구 — `37a338f` **push 완료**)

브랜치 `feature/maprendering` 원격 동기화 완료(ahead 0). 커밋 5개:
`88f43cb` 픽셀레이트 · `3f7c43d` 룩 A/B 토글(F9) · `062308a` DoF 비활성 ·
`a239923` 아트 가이드+계획서 · `37a338f` 어비스 복구.

**닫은 것**

- **픽셀레이트** — 합성 패스 UV 양자화로 구현(패스 추가 0, 각 0.01ms). 블러와 **독립 반경**
  (`pixelateRegionScale`)이라 픽셀 범위만 좁힐 수 있다. 룩 A·B **공통**.
- **룩 A/B 토글(F9)** — A = 채도 살림 / B = 디밍 + 저채도 + **시야 차폐**.
  토글은 `dimEnabled`·`losEnabled` **필드만** 오간다(컴포넌트를 끄면 어비스가 함께 죽는다).
- **DoF 비활성** — 배경 흐림은 마스크 블러 단독. 삭제 아니라 `active: 0`(튜닝값 보존).
- **어비스 물안개 복구** — 포그 게이트에 묶여 **렌더되지 않고 있었다**(원인 3곳).

**문서**: [Docs/design/look-ab-tuning.md](Docs/design/look-ab-tuning.md)(아트용) ·
`PLAN-vision.md` §8.11~8.13 · 볼트 `Programming/Setting/렌더링 설정 종합 정리` ·
볼트 `Art-Planning/Setting 사용법/룩 A-B 비교와 설정 조절`.

**남은 육안 검증 2건** — ① 픽셀 영역 배율(`pixelateRegionScale` 1.242) 적정성
② **어비스 tint 는 8~19% 로 은은해 구멍 근처에서만 보인다** — 확인 필요.

**미커밋으로 남은 것** = `0.BootStrapScene` · `Paladin.prefab` · `TwentyThree.prefab` ·
`MultiplayerManager.asset` 4개인데 **전부 내용 변경 0(줄바꿈 노이즈)** 이다.
(이전 인계에 있던 "Paladin PlayerInput 되돌릴 것" 항목은 현재 diff 0 이라 **해소됨**.)

**🔴 이 세션의 반복 패턴 3건 — 다음에 의심할 것**

1. **C# 기본값을 바꿔도 이미 직렬화된 애셋은 안 바뀐다.** `pixelBlockSize` 4→16 을 코드에서만
   올려 화면이 그대로였다. 필드 기본값을 바꿀 때는 **애셋 파일의 실제 값을 열어 확인**할 것.
2. **한 계통을 다른 계통의 게이트 안에 두지 말 것.** 어비스가 포그 안에 있어서 "포그를 껐다"가
   무관한 기능을 조용히 껐다. 매니저 컴포넌트의 `enabled` 로 한 기능만 토글하는 것도 같은 실수.
3. **로그의 실패 기록이 현재 상태가 아닐 수 있다.** 셰이더 에러가 중간 편집 상태의 기록이었다 —
   로그를 믿지 말고 **현재 파일과 강제 재임포트로 재판정**할 것.

**⚠️ 신 Input System 은 에디터에서 Game View 포커스를 따른다.** 포커스 없으면 F9 가 안 먹는다.
그래서 `LookToggle` 은 콘솔에도 로그를 남긴다(입력 도달 여부와 적용 결과를 갈라내기 위함).

---

## 이전 인수인계 (2026-08-05 · 렌더링 조명·포스트프로세싱 — 브랜치 `feature/maprendering`)

작업 세션: **경석(Claude)**. 브랜치 `feature/maprendering`
(= `development` `cbb51b1` 기준으로 분기, 원격 push 완료).

**수정 예정 영역 — 렌더링/조명/포스트프로세싱.** Codex·팀원 동시 수정 주의 대상:
`Assets/99.Settings/*`(URP 애셋·볼륨 프로파일) · 툰 셰이더/머티리얼 ·
`4.MapScene`·`bossroom` 의 조명·볼륨 오브젝트. 세부 파일은 범위 확정 후 여기 추가한다.

### 목표 — "레퍼런스 이미지 수준의 플레이 화면"

팀장이 AI 생성 레퍼런스 이미지를 **목표 룩**으로 제시했다(2026-08-05, 채팅 첨부).
탑다운 쿼터뷰 고정 / 셀셰이딩 캐릭터 + 회화풍 배경 / 부드러운 접지 그림자 +
따뜻한 국소 조명(가로등·창) / 물·금속 반사 / 원경 디포커스 / 채도 높은 소품.
이미지에는 HUD 배치안(특성·강화·스탯 육각 슬롯, 하단 스킬 슬롯, HP/MP 바, 남은 시간,
우상단 미니맵)도 손으로 얹혀 있으나 **HUD 는 이 세션 범위와 별개 트랙**이다.

🔴 **레퍼런스 이미지는 아직 채팅에만 있다 — `Docs/design/` 에 저장할 것.**

- **카메라 조절은 완료** — FOV 52→34 / Priority 100 (`0522731`, development 에 포함).
  이번 세션은 카메라가 아니라 **셰이딩·조명·포스트프로세싱**이다.
- 승인된 계획서는 `PLAN.md` 가 아니라 **`PLAN-vision.md` §7** 이다(§4 단계 2~3 재개).
- 선행 참고: 툰셰이더 현황(`e5cc012`) · 벽 차폐/투명화 · `Docs/tech/fog-system.md`.

### 2026-08-05 마감 시점 기록 (`317a2a1`) — ✅ 이후 전부 push 됨

**코드 작업은 일단락됐고 남은 것은 육안 검증이다.** 컴파일 0에러/0경고, 콘솔 에러 0건,
`svn status Assets/50.Art` missing 0건, 배선 14개 항목 전수 확인 완료.

**이번 세션에 닫은 것**

| 닫은 것 | 커밋 |
|---|---|
| 그레이딩 LDR→HDR + 톤매핑·화이트밸런스·컬러조정·비네트 도입 | `77e6063` |
| 벽 투명화 복원 + **포그 매니저 OFF** | `3b2d07f` |
| **카메라 포스트프로세싱 활성** + TAA | `0dfec30` |
| 벽 투명화 대상을 Stage1 로 한정 + AA·디더 토글 분리 | `82b079b` |
| ProfilerHUD 배치 + DoF Bokeh | `421b844` |
| **화면공간 마스크 블러** 신설(셰이더·피처·컨트롤러·설정) | `e07d2ac` |
| 마스크 블러 `size.x` 무시 수정 + 성능 실측 기록 | `959b8c2` |
| HUD 마커를 마스크 블러 4패스로 교체 | `317a2a1` |

**AA 결정 = SMAA** (2026-08-05 팀장 판단). TAA 와 큰 차이를 못 느꼈고,
**고스팅이 없는 쪽이 맞다**는 판단이다. 대시 잔상은 TAA 부산물이 아니라 **대시 전용
VFX 로 따로 넣는다** — 그래야 통제가 된다. `m_Antialiasing: 2` 적용됨.
🔴 SMAA 에서는 `WallOcclusionSettings.animateDither` 를 켜면 안 된다(기본값 OFF, 유지).

**두 룩을 키 하나로 비교 (팀장 지시, 2026-08-05) — ✅ 2026-08-06 완료(위 인수인계 참조)**

지금은 채도를 살리는 방향으로 왔지만, 원래는 **어둡고 디밍이 들어간 상태**였다.
그쪽에 **픽셀레이트 + 블러**까지 얹은 것과 지금 화면을 **키 입력 한 번으로 전환**해
직접 비교할 수 있게 만든다.

| | A (현재) | B (비교 대상) |
|---|---|---|
| 채도·톤 | 살림 | 저채도 |
| 디밍 | 없음 | 있음 |
| 블러 | 마스크 블러 | 마스크 블러 |
| 픽셀레이트 | 없음 | **있음(신규)** |

착수 전 알아 둘 것:

- 🔴 **런타임에 `volume.sharedProfile` 값을 바꾸면 에디터에서 애셋이 영구 수정된다.**
  Play 를 끝내도 남는다. 씬에 Volume 을 2개 두고 weight 를 토글하거나
  `volume.profile`(런타임 클론)을 쓸 것.
- 🔴 **디밍을 되살리려고 `FogManager` 를 통째로 켜면 시야 제한(LoS)도 같이 돌아온다.**
  `dimEnabled` 와 `losEnabled` 는 독립 토글이므로 디밍만 원하면 `losEnabled: 0` 으로 둔다.
  (현재 값: `fogEnabled 0` / `dimEnabled 1` / `losEnabled 1`, 컴포넌트 자체가 OFF)
- 두 룩이 갈리는 축이 4개다(볼륨 값 · 디밍 · 마스크블러 설정 · 픽셀레이트).
  **"룩 프리셋" ScriptableObject 하나로 묶어 통째로 스왑**하는 편이 토글이 단순해진다.
- 픽셀레이트는 마스크 블러와 같은 계통의 풀스크린 패스다 — `MaskBlurFeature` 구조를
  그대로 재사용할 수 있다. ⚠️ **순서를 정해야 한다**: 블러→픽셀레이트면 블록 경계가
  또렷하고, 픽셀레이트→블러면 블록이 뭉개진다. 원하는 그림이 어느 쪽인지 먼저 결정.
- 토글 키는 **F8 을 피할 것**(ProfilerHUD 가 쓴다). 이 프로젝트는 신 Input System 이다.

**그 외 남은 것**

1. **Play 육안 검증** — 마스크 블러의 `feather`(0.35)·`blurStrength`(1)·`roundness`(4)
   는 전부 추정값이고 화면으로 확인한 적 없다.
2. **저사양 성능 재측정** — 팀장 PC 는 GPU 2.89ms 로 여유롭지만 고사양 기준이다.
   비싸면 `downsampleShift` 1→2 → `blurStrength` 하향 → 패스 축소 순.
3. 남은 격차 = **국소 조명**(가로등·창). 라이트맵 방향이 정해져야 착수 가능(아래 미해결).

**🔴 미해결 / 결정 대기**

- **라이트맵 베이크가 현재 맵 구조와 충돌한다.** `MapContentSpawner.cs:62` 가 존 레이아웃
  프리팹(바닥·벽 포함)을 런타임 `Instantiate` 하는데, 라이트맵 데이터는 씬 렌더러에
  직렬화되므로 런타임 생성물은 받지 못한다. 씬의 static 플래그도 0개다. 게다가 슬롯
  위치가 시드마다 셔플된다. 선택지 = 프리팹 라이트맵 베이크 / 라이트 프로브·APV /
  Stage1 만 굽고 생성물은 실시간. **조명 단계 전에 방향 결정 필요.**
- **퓨즈박스가 어둡다 — 미해결.** `Level_wall_hallway.prefab`(Stage1 에 13개)이 쓰는
  `MA_prop03` 은 `prop03_basecolor` + `pipe_basecolor` 두 장인데 오클루전 변종은 한 장만
  가져간다. 변종 14개 중 4개가 3~5장 → 1장으로 붕괴하고, **13개는 노멀맵을 잃었다**
  (노멀 텍스처 5개가 `textureType: 0`=Default 로 임포트돼 `IsNormalMap()` 이 false 를
  반환하기 때문 — 팀장이 재임포트 예정). 근본 해결은 변종 머티리얼을 없애고 원본
  ShaderGraph 에 디더를 심는 것. `50.Art` = SVN 이라 단일 담당 필요.
- ⚠️ **`ProfilerHUD` 는 제출 빌드 전에 MapScene 에서 제거할 것.** 클래스가
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 로 감싸져 있어 릴리스 빌드에서 missing script 가 된다.
- ✅ **해소됨 (2026-08-06 확인) — 아래 되돌리기 명령은 실행하지 말 것.**
  `Paladin.prefab` 의 `git diff` 는 현재 **내용 변경 0**(줄바꿈 노이즈만)이다. 아래는
  당시 기록으로만 남긴다.
  <br>~~`PlayerInput` 이 `m_Enabled: 0 → 1` 로 켜져 있다(미커밋).~~
  단, **규칙 자체는 유효하다**: `Player.cs:146` 주석대로 프리팹 기본값은 비활성이 맞고,
  `EnableLocalInput()` 이 로컬(오너/오프라인)만 켠다 — 원격 클론이 디바이스 페어링을 시도해
  "Cannot find matching control scheme" 경고를 내는 것을 막기 위한 설계다.
  **`PlayerInput` 을 프리팹에서 켜 두면 안 된다.**

**미커밋으로 남긴 것** — 전부 팀장이 인스펙터에서 튜닝 중인 값이라 손대지 않았다:
`Global Volume Profile`(비네트 색 warm→진회색, 강도 0.606 / 대비 -3.7 / 채도 -11.6 —
내가 넣었던 +12·+18 이 과해서 되돌린 것) · `MaskBlurSettings`(`size.x` 0.65) ·
`Paladin.prefab`(위 항목, 되돌릴 것) · 줄바꿈 노이즈 2건.

**이 세션에 확립된 것**

- 🔴 **"적용했는데 화면이 그대로"가 세 번 나왔고 원인이 매번 달랐다.**
  ① `VolumeProfile.Add<T>()` 는 서브에셋 등록을 안 해 저장 시 `{fileID: 0}` 널이 된다
  (게다가 널이 되기 전 메모리 인스턴스를 `TryGet` 이 찾아 "이미 있음"으로 오판한다 —
  판단 기준은 존재 여부가 아니라 `AssetDatabase.Contains`).
  ② **카메라 포스트프로세싱이 꺼져 있었다**(`m_RenderPostProcessing: 0`).
  ③ `MaskBlurSettings.ResolveSize` 가 `size.x` 를 y 에서 유도해 인스펙터 값을 버렸다.
  **공통 교훈: 도구가 "성공" 로그를 찍어도 산출물 파일을 직접 열어 확인할 것.**
- 🔴 **게임플레이 카메라는 씬에 없다.** `4.MapScene` 의 Camera 는 0개이고
  `CameraTargetSwitcher.cs:132` 가 `CameraSwitcher.prefab` → `MainCamera.prefab` 을 런타임
  `Instantiate` 한다. 그 프리팹은 `CamaraScene`·`PlayerScene`(테스트 씬)에서만 직접 쓰이므로
  **씬만 훑으면 실제 카메라 설정에 도달하지 못한다.**
- ⚠️ **`FogManager` 는 이름과 달리 포그가 범인이 아니다.** 씬 값이 `fogEnabled: 0` 이라
  포그는 안 그려지고 있었고, 화면을 어둡게 만든 것은 `dimEnabled: 1` + `losEnabled: 1`
  (`losMaxDist: 4`)이다. 지금 OFF 라 **먼 거리 적이 전부 보인다** — 게임플레이 영향 있음.
- ⚠️ **탑다운에서 DoF 로 화면 외곽을 흐리게 할 수 없다.** DoF 는 깊이로 판단하는데
  좌우 외곽은 중앙과 깊이가 같다. 그래서 화면공간 마스크 블러를 새로 만들었다.
- ⚠️ **벽 투명화의 디더 무늬는 품질 결함이 아니라 의도된 비용 절충**이다(반투명 블렌딩
  대신 `clip()` 으로 불투명 큐 한 패스). 보기 싫다고 끄지 말 것 — 끄면 벽 뒤 플레이어가
  그냥 안 보인다. 디더+TAA 로 녹이는 시도는 **실패했다**(TAA 분산 클램프가 디더 변동을
  기각해 뿌연 얼룩이 된다). 2안 = MSAA + `AlphaToMask`.
- ✅ **렌더러 피처를 코드로 추가할 때는 `AddObjectToAsset` + `m_RendererFeatureMap` 정합
  확인이 필수다.** 맵은 features 개수와 길이가 맞아야 한다(long 1개 = hex 16자).
  URP 의 `ValidateRendererFeatures()` 가 재계산하지만 `internal` 이라 리플렉션으로 깨웠다.
- ✅ 셰이더는 반드시 **직렬화 참조**로 물릴 것(`MaskBlurFeature._shader`). `Shader.Find`
  만으로는 빌드에서 스트립된다(미니맵 전례).

### 브랜치 정리 (세션 시작 시)

- `feature/map-player-merge` 삭제(`8e5a2ae`) — `origin/development` 에 전부 포함된 것을
  확인한 뒤. `ahead 2` 는 **자기 원격 ref 가 낡아서 생긴 착시**였다.
- 로컬 `development` 를 `origin/development`(`cbb51b1`) 로 갱신 후 거기서 분기.
  `fix/AlphaAlert` 에는 development 에만 있는 커밋 12개가 빠져 있었다(ConveyorBelt 정리,
  진입점 단일화, 미니맵 머티리얼 배선, 루프백 바인딩 수정, 50.Art gitignore 통일 등).
- 🔴 **git↔SVN 하이브리드 함정 — "git 추적 해제" 커밋을 가로지르는 체크아웃은 SVN 소유
  파일을 디스크에서 지운다.** development 의 `5cd384f`(SVN 소유 아트 `.meta` 140건 추적 해제)를
  넘어오면서 git 이 `TestAssets/Temp_Images` 아래 `.meta` 5개를 **삭제**했다. 그대로 Unity 를
  켰으면 GUID 재발급으로 참조가 깨졌을 것이다. SVN 이 `!`(missing) 로 잡고 있어 `svn revert`
  로 복구, GUID 가 git 원본과 일치하는 것까지 확인했다.
  **브랜치를 갈아탄 뒤 Unity 를 켜기 전에 아래를 볼 것:**
  `svn status Assets/50.Art | Select-String "^!"`

---

## 이전 인수인계 (2026-08-05 · MCP 무한대기 해소 + 네트워크 권한 결함 2건, **PR #10 머지 완료** `cbb51b1`)

작업 세션: **경석(Claude)**. `fix/AlphaAlert` → `development` 머지 완료, 원격 동기화됨.
워킹트리에 남은 것은 **줄바꿈 노이즈 1개**(`MultiplayerManager`)뿐이다 — 아래 07-31 항목의
"내용 변경 0" 부류와 같다. 커밋하지 말 것.

### 이번에 닫은 것

| 닫은 것 | 커밋 |
|---|---|
| **결과 씬 전환 메시지에 송신자 검증** — 클라가 named message 로 호스트를 결과 화면으로 끌고 갈 수 있었다 | `6d34ea1` |
| **대시 서버 검증에 사망/영혼 배선** — 판정 입력이 `false` 하드코딩이라 죽은 플레이어 대시를 못 막았다 | `66d99af` |
| **Unity MCP 무한대기 해소 + 브릿지 런처** (핀 `cf61cc5`→`2ea969e`) | `58f3c19` · `effdf12` |
| 툰 룩 동결값 — 카메라 FOV 52→34 / Priority 100 / Paladin 툰 4값 | `0522731` |

두 결함은 Codex·Claude 교차 코드리뷰에서 나왔다. 리뷰 원문·39건 목록은 개인 보관.

### 다음 시작점 — 렌더링 수정 (**새 브랜치에서 작업**)

팀장 지시: 다음 작업은 렌더링 수정이며 **브랜치를 새로 파서** 진행한다. 구체 범위는 다음 세션에
확정. 관련 기존 문서를 먼저 확인할 것 — 툰셰이더 현황(위 07-31 §이번에 닫은 것 `e5cc012`),
벽 차폐/투명화, `Docs/tech/fog-system.md`.

### 이 세션에 확립된 것

- 🔴 **Claude Code 는 프로젝트 `.mcp.json` 이 아니라 `~/.claude.json` 의 user 스코프
  `mcpServers` 를 우선한다.** 둘 다 `unity` 가 있으면 프로젝트 쪽은 무시된다.
  MCP 가 "고쳤는데 그대로"면 **실행 중인 프로세스의 커맨드라인부터 본다**:
  `Get-CimInstance Win32_Process -Filter "name='node.exe'" | Select CommandLine`.
- 🔴 **브릿지 사본을 홈(`~/.unity-mcp/`)에 두지 말 것.** 사본은 패키지가 갱신돼도 조용히 낡고,
  낡았다는 신호가 어디에도 안 뜬다. 경로가 흔들리는 문제는 사본이 아니라 **런처로** 푼다 —
  런처가 `PackageCache` 를 실행 시점에 탐색하므로 **패키지 핀을 갱신해도 개인 설정을 고칠
  필요가 없다.** 런처 정본은 이제 MCP 패키지 안에 있다 — `Bridge/mcp-bridge-launcher.js`
  (예전엔 이 레포 루트의 `.mcp-bridge-launcher.js`). 등록 방법은 포크 레포의
  `LOCAL_DEV_SETUP.md` 를 본다.
- ⚠️ **MCP 무한대기의 원인은 타임아웃 부재가 아니라 응답 id 유실이었다.** 서버가 합성 에러에
  id 를 안 실으면(`id:null`) HTTP 200 으로 나가고, 브릿지는 "정상 응답"으로 처리해
  **자체 타임아웃·재시도가 아예 발동하지 않는다.** 요청-응답 프로토콜에서 무한대기를 만나면
  "응답이 오는가"보다 **"응답의 상관키가 맞는가"** 를 먼저 본다.
- ✅ **MCP 패키지 수정 절차**: `C:\Users\user\unity-mcp-fork` 의 **`optimized` 브랜치**에서
  수정 → push → `Packages/manifest.json` 핀 갱신 → Unity 재시작. `main` 은 이 프로젝트가
  쓰는 계보가 아니다. lock(`packages-lock.json`)도 같이 커밋해야 팀원이 새 버전을 받는다.
- ⚠️ **`MapSceneManager` 만 송신자 검증이 빠져 있었다** — `LobbyUIController:259`,
  `NetworkLoadingFlowController:766`, `NetworkClock:264` 는 이미 하고 있었다. NGO custom
  named message 핸들러를 새로 추가할 때는 이 가드를 기본으로 넣을 것.
- ⚠️ **`PlayerDashController` 는 `PlayerLifeCycleController` 가 없으면 "살아있음"으로 폴백한다**
  (대시 전용 테스트 씬 대응). 프로덕션 프리팹에서 이 컴포넌트가 누락되면 사망 검증이 조용히
  빠지므로, 서버 스폰 경고(`[DashAlert] PlayerLifeCycleController가 없어…`)를 무시하지 말 것.
- 팀원 조치 안내는 볼트 `Core/MCP패키지_포크전환_안내.md`(2026-08-05 최신화)에 정리했다.

---

## 이전 인수인계 (2026-08-11 · 피격 이펙트 클라 복제 + 교체 HUD, 브랜치 `feature/VFX`)

작업 세션: **민경(Claude)**. 계획·근거는 [PLAN.md](PLAN.md) 최상단 항목.
수정 파일: `Effects/EffectManager.cs` · `Effects/HitVFXPlayback.cs`(신규) ·
`Monster/MonsterBase.cs` · `Enemy/Enemy.cs` · `Dev/HitVFXDebugHUD.cs`(신규).
**프리팹·씬·`EffectCatalog.asset`은 건드리지 않았다.** 컴파일 0 에러 / 0 경고.

- 🔴 **피격 이펙트는 서버가 재생하지 않는다.** `ReceiveAttack`은 서버에서만 불리므로(BaseAttack의
  `IsServer` 게이트) 거기서 `Play`하면 **호스트에서만 보인다**. 서버는 `sourcePosition`만
  unreliable RPC로 보내고, 재생은 각 피어가 `HitVFXPlayback.Play`로 로컬 처리한다.
  (`AoeTelegraph`·`GauntletBot.ShowTelegraphClientRpc`와 같은 패턴.)
- 🔴 **타격점을 서버가 계산해 보내면 안 된다.** 클라의 몹은 `NetworkTransform` 보간 때문에 서버보다
  뒤에 그려진다(TickRate 30, 100ms 안팎 → 4m/s면 0.3~0.4m). 월드 절대 좌표를 보내면 이펙트가
  몸에서 떨어진다. 수신측이 **자기 콜라이더로** 다시 계산해야 항상 표면에 붙는다.
- ⚠️ **호스트 화면으로는 이 부류의 버그가 안 보인다** — 호스트 = 서버라 어긋남이 0이다.
  검증은 반드시 **MPPM 클라이언트 창에서, 몹이 이동 중일 때**.
- ⚠️ **이펙트 오버라이드를 `EffectCatalog`(SO)에 두지 말 것.** SO는 플레이 모드 중 변경이 에셋에
  눌러앉아 `.asset` diff로 커밋되고 팀 전체 기본값이 바뀐다. `EffectManager`의 런타임 필드에 둔다.
- ⚠️ **`HitPointInfo.facingHint`에 아무거나 넣지 말 것.** `Facing` 사다리의 ①이라 나머지 계산을
  **전부 제친다.** 근접 피격에 넣으면 실제 표면점으로 계산하는 ②(`origin - point`)를 덮어써
  오히려 부정확해진다. 현재 이걸 쓰는 곳은 `CameraDir` 모드뿐이다.
- ⚠️ **`Specific_Position` 모드는 방향 추론이 `Quaternion.identity`로 떨어질 수 있다.**
  그 분기는 `center`를 `point`와 같게 놓아 ③이 구조적으로 항상 0이고, 공격자 위치가 앵커와
  겹치면 ②마저 죽는다(장판·지속피해처럼 "공격자 위치"에 피격자 좌표를 넣는 경우).
  ④(구 `-source.forward`)는 RPC 수신측에 공격자 Transform이 없어 죽은 코드라 제거했다.
  이 모드를 실제로 쓸 때 방향이 고정돼 보이면 여기가 원인이다.
- **`hitPointMode`는 유닛이 아니라 `EffectManager`가 소유한다.** 프리팹 9개가 전부 같은 값
  (`ColliderHit`)이라 거기로 올렸다 — `MonsterBase`/`Enemy`에는 더 이상 그 필드가 없다.
  반면 `hitVFXType`은 유닛마다 남아 있고, `EffectManager`의 오버라이드가 그 위에 씌워진다.
- 디버그 HUD: `F1` 이펙트 종류 순환(마지막 다음은 "프리팹 값" = 오버라이드 해제) /
  `F2` 타격점 방식 순환. `#if UNITY_EDITOR || DEVELOPMENT_BUILD`(ProfilerHUD 관례).
  변경은 **머신별**이다(각 피어가 로컬 해석) — MPPM 창별 동시 비교가 가능하다.
- ⏳ **미검증**: MPPM 2인 Play 검증이 남았다. 완료 조건 체크리스트는 PLAN.md 참조.

### 이전 인수인계 (2026-08-06 · Effect System v1, 브랜치 `feature/VFX`)

작업 세션: **민경(Claude)**. 설계 원본은 레포 밖 `D:\김민경\유니티\VFX-Learning\design\effect-system-v1.md`.

**용어**: 이펙트 재생 시스템의 코드 이름은 `VFX*`가 아니라 **`Effect*`** 다
(`UnityEngine.VFX.VFXManager`가 엔진에 실존해 S6에서 이름이 충돌한다). 에셋 폴더는 `5.VFX/` 유지.

- 코드: `Assets/1.Scripts/Effects/` — `EffectManager`(싱글톤 파사드) · `IEffectSystem`(파트 드라이버) ·
  `ShurikenEffectSystem`(v1 유일 구현) · `EffectEntry`/`EffectPart`/`EffectCatalog`(데이터) ·
  `EffectPool`(`UnityEngine.Pool` 래퍼) · `EffectHandle`(세대 카운터 있는 루프 핸들).
- 데이터: `9.ScriptableObject/Effects/EffectCatalog.asset`, `5.VFX/Common/FX_*_Entry.asset`,
  `5.VFX/EffectManager.prefab`(카탈로그 연결됨 — 씬에 드래그해서 쓴다).
- 🔴 **수명은 데이터가 진실이다.** 프리팹에서 종료를 추론하지 않고 `EffectEntry.duration` 타이머로
  회수한다. `duration`은 감으로 적지 말고 `EffectDurationProbe`로 실측한다.
- 🔴 **히트스톱은 파티클에 자동 적용되지 않는다.** 이 프로젝트는 `Time.timeScale`을 한 번도 쓰지 않고
  `MonsterTimeController`가 몬스터별로만 감속한다. `EffectManager.SetPlayRateForTarget`을
  거기서 불러주지 않으면 몬스터만 얼고 이펙트는 계속 돈다. **그 배선은 팀 합의 대기(v1 밖)다.**
- ⚠️ **풀링 전제 프리팹 규칙**: `Stop Action ≠ Destroy` / `TrailRenderer.AutoDestruct` OFF /
  프리팹 하나에 단일 기술만. 앞의 둘은 `EffectPrefabRules`가 런타임에 경고+교정한다.
- ✅ 검증: `Tools/Effects/스모크 테스트 (Play Mode)` → 23/23 통과(배관만).
  연출의 자연스러움은 `EffectSceneTester`로 VFXScene에서 눈으로 본다.

## 이전 인수인계 (2026-07-31 · 보스 이슈 정리 + 툰셰이딩 + Windows 빌드, `93716e0` — PR #10 로 머지됨)

작업 세션: **경석(Claude)**. 브랜치 `feature/map-player-merge`, origin 대비 **ahead 1**.
워킹트리에 남은 것은 **내용 변경 0 인 줄바꿈 노이즈 4개**뿐이다
(`0.BootStrapScene` · `GraphicsSettings` · `MultiplayerManager` ·
`UniversalRenderPipelineGlobalSettings` — 커밋하지 말 것. `git diff` 가 0줄이면 이 부류다).

### 이번에 닫은 것

| 닫은 것 | 커밋 |
|---|---|
| 툰셰이더 — 캐릭터 조명 독립 / 아웃라인 화면공간 px 고정 / 고정광 월드공간화, Wells·검·방패 툰 적용 | `e5cc012` |
| 보스 HUD 복구 — Paladin 의 CombatHUD 에 `BossHealthHUD` 재부착 + RectTransform 스케일 0 수정 | `ebbbf71` · `7e9c78c` |
| **Wells 폭탄 투척 복구 — 중첩 NetworkObject 제거** (아래 §정정 참고) | `58278e9` |
| `BombLauncher` 무증상 실패 제거 + `_bombController` 수명 대칭 | `88c4772` |
| Q 스킬 도중 사망 시 애니메이터 초기화(`Rebind`+`Update(0)`) | `bae2e98` |
| 부활 시 BT 플레이어 명부(`TargetGroup`/`TotalPlayerNumber`) 갱신 | `7c6ec58` |
| 은희 컨베이어벨트(`6150ee5`) rebase 병합 · SVN r258→r259 | — |
| **Windows 빌드 파이프라인** — `BuildWindowsPlayer` + 빌드 씬 목록 정리 + `productName` 복구 | `93716e0` |

### 다음 시작점 — 몬스터 배치

스포너 기계는 이미 있다: `MapContentSpawner`(마커별 그룹 스폰 구현됨) · `SpawnPoint`
(`AllowedTier` + `MonsterSpawnPoints`) · `MonsterGroupData`(티어/난이도/프리팹/가중치).
남은 것은 **저작·구성** 쪽이다.

### 이 세션에 확립된 것

- 🔴 **Wells 는 자체 `NetworkObject` 를 가지면 안 된다** — NGO 는 프리팹의 중첩
  NetworkObject 를 스폰하지 않는다. 서버 판정이 필요하면 `NetworkManager.Singleton.IsServer`
  를 쓴다(`BombLauncher`·`WellsAnimEvents` 패턴).
- ⚠️ **`Assets/8.BehaviorTreeGraph` 는 열기만 해도 런타임 그래프 RID 가 통째로 재직렬화된다**
  (No.23 = 4,675줄). 의도한 편집이 아니면 `git checkout` 으로 버릴 것. 그래프에 노드를 넣는
  대신 C# 에서 블랙보드를 쓰는 쪽이 diff·머지 비용이 훨씬 싸다.
- ⚠️ **`MonsterArea.asset` 은 고아라 삭제했다**(`58278e9`). Unity 가 BT 그래프를 열 때 다시
  지우려 들 수 있다 — `git status` 에 뜨면 정상이다.
- ✅ **Play 로그는 MCP 콘솔이 아니라 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 로 읽힌다.**
  (2026-07-29 의 "스크린샷으로만 받는다"는 전제는 과했다. 한글은 깨지지만 ASCII 마커로
  검색하면 충분하다 — 이번 폭탄·BT 진단을 전부 이 방법으로 끝냈다.)
- ✅ **Windows 빌드는 `BuildWindowsPlayer` 로 뽑는다.** 에디터를 닫은 뒤 CLI 배치모드:
  `Unity.exe -batchmode -quit -projectPath <proj> -buildTarget Win64
  -executeMethod BuildWindowsPlayer.BuildWindows64 -buildOutput <exe> -logFile <log>`
  (또는 에디터 메뉴 `Build > Windows64 Player (MainFlow)`). 출력은 레포 밖에 둔다 —
  `.gitignore` 에 `/Build/` 규칙이 없다. 첫 빌드 기준 4m41s / 369MB.
- ⚠️ **`Unity.exe` 는 자기를 자식 프로세스로 재실행해서 셸에 exit 0 을 즉시 돌려준다.**
  종료코드로 빌드 성공을 판단하면 안 된다. 판정은 로그의 `[Build] result=` 줄과
  exe 존재로 한다(`MainProject_Data/level*` 개수 = 포함 씬 수).
- ⚠️ **`Assets/Editor/` 는 `.gitignore:79` 로 무시된다.** 팀 공유용 에디터 스크립트는
  `Assets/1.Scripts/Editor/` 에 둘 것(같은 `Assembly-CSharp-Editor` 로 컴파일된다).
- 빌드가 부수적으로 만드는 것: `Assets/99.Settings/PC_RPAsset.asset` 의 `m_Prefilter*` 값과
  `Assets/AddressableAssetsData/link.xml`. 둘 다 재생성물이라 커밋하지 않는다.
- 🔴 **런타임 `Shader.Find` 는 빌드에서 null 이 된다** — 어떤 머티리얼/씬/프리팹도 참조하지 않는
  셰이더는 빌드에서 스트립되기 때문. "에디터는 되는데 빌드만 안 됨"의 대표 원인이다.
  미니맵(`UI/MinimapComposite`)이 이걸로 빌드에서 안 보였다. 프로젝트 자체 셰이더 5개 중
  참조 0 이던 건 미니맵 하나뿐이고 나머지(ToonLit·ToonGlass·WaterDark·FullScreenFog)는 안전하다.
  **커스텀 셰이더는 반드시 머티리얼 에셋 → 인스펙터 참조 체인으로 물릴 것.**
  검증법: 빌드 로그에 `Compiling shader "<이름>"` 이 찍히는지 본다.
- `#if` 전수조사 완료(자체 코드 36건) — M키·미니맵 미표시와 무관했다. 실제로 빌드에서 빠지는 건
  `ProfilerHUD`(`UNITY_EDITOR || DEVELOPMENT_BUILD`) 하나뿐이고 어느 씬에도 안 붙어 있어 무해하다.
  플레이어 어셈블리 define 확인법: `Library/Bee/artifacts/*P.dag/Assembly-CSharp.rsp`.
- ⚠️ **TeslaBot 은 메쉬가 분리된 채 전투한다 — 미해결.** 조사만 했고 담당자 배정 대기다:
  [Docs/tech/teslabot-mesh-separation-handoff.md](Docs/tech/teslabot-mesh-separation-handoff.md).
  임시 조치로 `MapGenConfig` GroupID 3 을 TeslaBot → PeekABot 으로 대체해 뒀다(GroupName 에 "임시대체" 표기).
  🔴 `MapGenConfig.asset` 은 `50.Art` = **SVN** 이라 git 커밋으로는 안 넘어간다 — TortoiseSVN 으로 커밋할 것.
- 남은 이슈: Wells `BehaviorGraphAgent` 가 모든 피어에서 돎(멀티 검증 전 서버 게이트 필요) /
  `WeaponTrailEffect` NRE 다수(로그 오염) / SVN `MapGenConfig.asset` 미커밋 /
  빌드 exe 실플레이(타이틀→로비→맵→결과) 미검증.

---

## 이전 인수인계 (2026-07-29 · 폭탄 투척 경로 복구, 커밋 `93b4e02`)

작업 세션: **경석(Claude)**. 브랜치 `feature/map-player-merge`, 마지막 커밋 `93b4e02`.
워킹트리: 씬 2개(`0.BootStrapScene`·`4.MapScene`)와 `Bomb.prefab`·머티리얼·BT 에셋 등은
**의도적으로 미커밋 보류** — 다른 담당자 작업(대시 등)과 겹치므로 이번 커밋에 넣지 않았다.

### 다음 시작점 — 폭탄이 손 높이에서 정지하는 문제

**아트 fbx는 무죄이고 `GroundProbe`도 정상이다.** Play 로그가
`바닥=Env_floor_basic_typeA (7)(layer 0) y=0.50 → 착지 y=0.55`로 매번 맞게 나온다.
그런데 폭탄 오브젝트는 `y≈2.79~2.86`(= 보스 손 높이)에 남는다. 목표에 도달했다면
`MovePosition(_targetPos)`로 Y가 정확히 0.55여야 하므로, **비행 중 `CheckHitBetween`에
막혀 위치가 갱신되지 않는 것**이다(`29cc999`에서 잡았던 증상이 다른 원인으로 재발).

`93b4e02`에 진단 로그를 넣어 뒀다 — 다음 Play에서 콘솔을 `정지`로 필터하면
`[No.23] 폭탄이 바닥 판정에 걸려 정지 — <콜라이더>(layer N), 현재 위치 …, 목표 …`가
범인을 그대로 지목한다. 유력 후보는 **`Env_Mv_bosscharger_upper`**(송전기, Default 레이어,
보스 계층 밖이라 `Unit` 제외에 안 걸린다). ground 마스크가 Default를 포함하는 한
`Unit` 제외만으로는 부족하다는 뜻이 된다.

⚠️ **MCP 브릿지로 Play 로그를 읽을 수 없다.** `totalBuffered`가 에디터 기동 시점 값에서
멈추고 Play 로그가 들어오지 않는다(도메인 리로드 후 로그 콜백 미재등록 의심). 이전 규칙
"Play 로그는 Play 중에만 읽힌다"는 무효 — **콘솔 스크린샷으로 받아야 한다.**

### 이번에 닫은 것

| 닫은 것 | 커밋 |
|---|---|
| Wells 루트에 `NetworkObject` 부착 — `WellsAnimEvents`가 `NetworkBehaviour`라 서버 판정을 못 받아 투척 애니 이벤트가 무시됐다(`BombHold`는 전역 `IsServer`를 봐서 영향 없음 → "손에 들고만 있음") | `4a84fe9` |
| `BombThrow`의 조용한 실패 복원(주석 처리돼 있던 로그) + `_bombController` 수명 일치 + `BombAction.Mode` 활성화 | `4a84fe9` |
| 폭탄 스케일 손 `0.5` → 착지 `1.0` 시간 보간 + `HandleHit` ground 분기 진단 로그 | `93b4e02` |

### 이전 세션에 닫은 것

| 닫은 것 | 커밋 |
|---|---|
| 폭탄이 보스 히트박스를 바닥으로 오인 → `GroundProbe`로 바닥 판정 통일(레이어 폴백·원점 띄우기·**Unit 계층 제외**) | `2b4226c` |
| 바닥 위 표준 간격 `0.05` 도입(절대 Y 고정은 불가 — 보스룸 보행면은 0.50, BossScene은 0) + 바닥 상단 측정을 Max→**최빈값** | `88da3c6` |
| 보스룸 안전망·기준점을 실제 보행면 **Y 0.50**에 맞춰 재생성(도구 재실행) | `95f3d61` |
| 폭탄이 보스 손 높이에서 영구 정지 — **무시할 히트를 스윕 마스크에서 제외** | `29cc999` |
| 아트 오프셋 실측 도구 + Bomb 비주얼 정합 확인(center (0,0,0) → 프리팹은 정상) | `cc22c3b` |

⚠️ **아트를 고치면 `Cube.422/423` 로컬 값이 바뀌고, 그러면 `BombVisual`의 상쇄값
`(28.0, -1.23, -6.94)`도 함께 무효가 된다.** 눈대중으로 맞추지 말고
`Tools/Map/Authoring/Measure Bomb Visual Offset`으로 재측정할 것(center가 0에 오면 정합).

### 그 다음 예정 (팀장 지시 순서)

1. **git 최신화 → SVN 업데이트**
2. **은희 `feature/PlayerSkillAnimation` 머지** — 사운드(`feature/Sound`)는 그 브랜치에 붙인 상태로 받는다.
   준비물은 §2-b/§2-c에 정리돼 있다:
   - **프리팹 컴포넌트 유실 검사법**: `grep -o "Assembly-CSharp::[A-Za-z_0-9]*" | sort -u`를 머지 전/후
     `comm` 비교(이번에 `BossHudTarget`을 놓쳤던 방식의 재발 방지)
   - **Player.prefab 레이어 마스크 함정**: `EnemyHurtBox`(14) 유지 — `--ours`로 통째 되돌리면 보스를 못 때린다
   - FMOD/AudioListener는 그 브랜치에 붙은 상태로 받기로 결정
3. ~~**미결 1건**: `Wells.prefab`이 `NetworkObject` 없이 `DefaultNetworkPrefabs`에 등록된 무효 상태~~
   ~~→ **`4a84fe9`에서 부착으로 해소.**~~ ~~현재 정상 동작하지만 NGO 중첩 지원에 의존하는 구조~~

   ⚠️ **정정 (2026-07-31, `58278e9`): 부착은 해결책이 아니었고 그 뒤에도 폭탄은 안 나갔다.**
   진단("서버 판정을 못 받는다")은 맞았지만 처방이 반대였다. **NGO 는 프리팹의 중첩
   NetworkObject 를 스폰하지 않는다**(씬 오브젝트만 지원) — 붙여도 스폰되지 않으므로
   `IsServer` 는 계속 false 다. 런타임 로그로 실증:
   `ThrowBombEvent 진입 — IsServer=False, IsSpawned=False`.
   해결은 **NetworkObject 제거 + `WellsAnimEvents` 를 MonoBehaviour 로 전환**이었다.
   🔴 **다시 붙이지 말 것.** 상세는 `58278e9` 커밋 메시지와 `WellsAnimEvents` 클래스 주석.

### 이 세션에 확립된 재사용 규칙

- **바닥 판정은 `GroundProbe.TryFindGround` 하나만 쓴다.** 직접 `Physics.Raycast(..., "Ground")`를 쓰지 말 것.
  네 군데에서 같은 원인으로 터졌다(점프 착지·폭탄 투척·MakeFloor·비행 스윕).
- **바닥에 눕는 것은 `GroundProbe.SurfaceY(hit)`** (= 찾은 바닥 + 0.05). 절대 Y 상수 금지.
- **보스룸 보행면 = Y 0.50** (`BossFloorCollider` 상단·`BossLandingPoint`·`BossArea` 전부 0.50).
  이전 문서·주석의 "0.61"은 솟은 발판을 잘못 측정한 값이었다.
- ~~**Play 로그는 Play 중에만 MCP로 읽힌다**~~ → **⚠️ 정정(2026-07-29): Play 중에도 못 읽는다.**
  `unity_get_console_logs`의 `totalBuffered`가 에디터 기동 시점 값에서 멈춘다. Play 로그는
  **콘솔 스크린샷으로 받는다**고 전제하고 진단 계획을 세울 것.
- 로컬 교훈 로그(`Docs/_local/lessons.md`) #32~#37에 이번 세션 6건을 적었다.

---

## 이전 인수인계 (2026-07-28 · SVN r235 커밋 완료)

작업 세션: **경석(Claude)**. 브랜치 `feature/map-player-merge` (`30cf1df`, origin보다 1 앞).

### 이번 세션 완료 — SVN 최신화

**커밋 r235** (225 → 235). 신규 242 / 수정 104.

- 신규: `MapObj/mesh/level`(131) · `material`(48) · `texture`(32) · `mesh/object`(28)
- **⚠️ r234 GUID 사고 처리** — 팀원이 "누락된 meta 커밋"으로 MapObj meta 87개의 GUID를 새로
  발급해 올렸다. 그대로 update 했으면 git 쪽 41개 파일(존 프리팹 12·벽 10·머티리얼 17·씬 1)이
  전부 미싱 레퍼런스가 된다. `svn merge -c -234`로 GUID 재발급분만 역머지해 원본 유지.
  경위·재발 방지 = `Docs/_local/lessons.md` #14
- **콜라이더 플래그 복구** — floor/wall/urethane fbx meta 39개가 7/28 아트 교체로 `addColliders`·
  `isReadable` 0으로 리셋돼 있던 것을 r225 값(1/1)으로 되돌림.
- 검증: 컴파일 0에러/0경고, GUID 80/80 원본 일치, 이번 작업발 미싱 레퍼런스 0.
  (기존 미싱 5건 — MapScene FoW `maskTexture`(미사용) · `MA_Wall_basic.mat` 1 · `Stage1.prefab` 3 — 은 별개)
- git 정리 커밋 `30cf1df`: Tree/TutorialInfo 템플릿 잔재 + `all_mesh.unity` 제거
  (빌드 세팅 미등록·코드 참조 0 확인).

### 이어서 완료 — 경사로 콜라이더 + dash-soul 머지

- **`caaef90`** 경사로·계단 17개 MeshCollider 부착. Play에서 slope를 밟으면 낙하하던 문제.
  프리팹 안 언팩 사본이라 fbx `addColliders`가 전파되지 않는데 `MapColliderAuthoring`
  이름 필터에 slope/stair가 없었다. 키워드 추가로 해결(소품 88개는 통행 방해 우려로 제외).
- **`7a5db51`** `feature/dash-soul` 머지(59커밋). 씬이 `0.Scenes/MainFlow/`로 재편돼
  rename+양쪽수정 충돌 9건 발생, 전부 해소. 상세 = 머지 커밋 메시지.
  - MapScene은 **UnityYAMLMerge 3-way로 충돌 0** — 우리(맵 시스템 7)와 그쪽(옵션 UI 11)이
    겹치지 않았다. 병합 후 18개 오브젝트·참조·스크립트 전수 확인.
  - 자동 해소에 맡겼으면 조용히 깨졌을 3건을 수동 처리: **BossHudTarget 유실**(보스 체력바
    미표시), **레이어 슬롯 14 이중 점유**(EnemyHurtBox↔Corpse), **HasAimGroundPoint 게이트**
    (생성맵에서 지면 스킬 전면 차단).
- 검증: Unity 배치모드 컴파일 **error CS 0**, MapScene 댕글링 참조 0 / 미싱 스크립트 0.

### 이어서 완료 — Play 검증 피드백 반영 (`dec1eb3`·`f990c73`·`35aa703`)

**★ 이번 세션 최대 교훈: dash-soul 시스템들은 "씬마다 하나씩 배치"를 전제하고, 없으면
예외 없이 조용히 비활성화된다.** 증상만 보면 기능 버그로 보인다. MapScene에 배치한 것:
`FallBoundarySettings`(추락 감지) · `PlayerDashValidationManager`(대시 서버 검증) ·
`Temp_MultiGameRule`(부활 규칙) · `PartyWipeWatcher` · `SessionStatsTracker`.

- **콜라이더**: 바닥 82개(머티리얼 이름 판정 추가 — 아트가 `Cube.209` 식으로 내보내 이름
  필터로 안 잡혔다). **계단은 경사면 BoxCollider로 대체** — 계단 형상 콜라이더는 Rigidbody
  캡슐이 턱에 걸린다(`stepOffset` 보정 없음). 보이는 건 계단 그대로.
- **경사 등판**: `PlayerMovement`가 수평 벡터를 그대로 `MovePosition`에 넣어 경사면에 파고들었다.
  접지 중이면 지면 노멀 평면에 투영하도록 수정.
- **Soul**: `Soul` 레이어 신설(16) + `Soul`/`Corpse` 충돌을 `Default`/`Ground`/`Wall`/`Env`로
  제한. 어비스 위에서는 `useGravity`를 꺼 부유(속도만 0으로는 중력이 재가속시킨다).
- **낙사**: `ServerFallDeath` 구독자가 0이어서 사망 시 몸이 추락 지점에 남았다 → 안전지점 복귀.
- **사이클**: 전멸(=전원 `PermanentDead`) → Result → 로비. 결과 = `SessionResult` +
  `SessionStatsTracker`(생존 시간·처치 수) + `ResultStatsView`. 처치 수는
  `MonsterBase`/`BossBase` 사망 지점의 `MonsterDeathEvents`로 집계.
- **시체 자홍색**: 빌트인 `Default-Material`은 URP 미지원 → URP Lit로 교체.
- `GameManager.prefab`의 씬 이름이 리네임 전 값이었다(BootStrap 인스턴스는 이미 정상이라
  정식 플로우는 무영향, MapScene 직접 Play만 실패). 프리팹 최신화.

### 이어서 완료 — 보스룸 경계 + 도착 ACK (`3b626a1`·`93f65b8`·`a3d284e`)

승인 계획의 **A단계(투명벽) + Task 1**까지. 계획 사본 = `C:\Users\user\.claude\plans\synchronous-pondering-coral.md`

- `BossRoomAuthoring` 저작 도구로 `bossroom.prefab`에 생성(재실행 가능, 렌더러 바운즈 실측):
  `BossFloorCollider`(21×1×21, Default, 상단 Y 0.61) · `InvisibleBoundaries` 4면(Wall, 높이 8,
  트리거·렌더러 없음) · `PlayerArrivalPoints/Player1..3`(2m 삼각, 착지점 응시) · `BossLandingPoint`
- `BossTeleportManager`: 황금각 산개 → 도착 지점 배열 + ACK 계약. `encounterSequence` +
  대기 집합, sender·sequence 일치만 수락(중복 무시), 전원 ACK → `AlivePlayersArrived` 1회,
  5초 타임아웃 → `ArrivalAborted`, disconnect 처리, `IsEncounterBusy`로 재진입 차단
- ⚠️ 함정: `renderer.bounds`는 월드 좌표이고 `LoadPrefabContents`는 프리팹을 원점이 아닌 프리뷰
  씬에 올린다. 로컬인 `BoxCollider.center`에 그대로 넣으면 전체가 밀린다(1차 실행에서 Z≈109).
  (로컬 로그 `Docs/_local/lessons.md` #18 — 팀 공유 대상 아님)

### 이어서 완료 — Task 2 연출 잠금 (`c35d7bd`)

`PlayerEncounterLock` 신설. **잠금 = 새 계통이 아니라 기존 게이트 재사용**:
`PlayerActionState.Cinematic`(기존 `PlayerLockedState`) 하나로 이동·공격·스킬의 서버 승인
게이트가 함께 닫히고, 피해 무시는 `PlayerInvulnerability`의 `Cinematic` 토큰이 담당한다.
dash-soul 계통(대시 예측+서버 스냅샷 / 추락 감지 / 생명주기 전이)도 잠금 중 차단.

- ⚠️ **함정**: `Unit.Died`는 `_deathNotified`로 래치돼 재발행되지 않는다. 잠금 중 사망 신호를
  버리면 HP 0인데 Alive로 남는다 → `PlayerLifeCycleController`가 보류 후 해제 시 처리.
- 오너 입력은 `PlayerLifeInputPolicy` 한 곳에서만 적용(두 계통이 `SetInputEnabled`를 각자
  부르면 나중 호출이 앞선 차단을 지운다).
- `Player.prefab` 배선은 YAML 직접 편집(+21줄). **Unity가 포커스를 받아야 재임포트된다** —
  인스펙터에 컴포넌트가 보이는지 확인 필요.
- 미검증: Play/MPPM에서 잠금 중 공격·스킬·대시·추락·부활 무효 + 해제 후 복귀.
- 이탈 기록·검증 경계 = `IMPLEMENTATION_NOTES.md`(로컬, gitignore).
- ⚠️ MCP 주의: `unity_recompile_scripts` 직후 상태 조회를 부르면 도메인 리로드 중이라 응답이
  없다(무한대기). 컴파일 확인은 `Library/ScriptAssemblies/*.dll` 타임스탬프 + 콘솔 Error로.

### 이어서 완료 — Play 피드백 5건 (2026-07-29, `703988f`~`4482d0b`)

- **인게임에서만 바닥에 구멍** — 시드 차이(TestGenerate=TickCount / 인게임=Random.Range).
  `AssignSlotRoles`는 BossRoom·PlayerSpawn 후보를 **크기 무관**하게 뽑는데 역할 디자인은 Small만
  저작돼 있다 → `GetRoleLayout`이 null → `MapContentSpawner`가 **로그 없이 continue** → 구멍.
  → 역할 디자인 없으면 같은 크기 전투 풀 폴백 + 경고, 스포너는 에러 로그. **조용한 구멍 금지.**
- **보스만 피격 빨간 틴트 없음** — `Enemy.OnNetworkSpawn`이 `base` 호출 없이 `if (!IsServer) return;`
  으로 시작했다. `Unit.OnNetworkSpawn`의 HP 복제 구독 + `HitFlash` 자동 부착이 통째로 건너뛰어졌다.
  Unit 파생 5종 중 Enemy만 누락. **★파생 클래스에서 base 누락은 "그 타입만" 조용히 기능이 빠진다.**
- **충전 중 보스가 맵 밖으로** — BT가 복귀 위치로 읽는 `SpawnPointer.SpawnPoint`가 프리팹 기본값
  `(0,0,0)`이고 코드에서 아무도 채우지 않는다. BossScene은 아레나가 원점이라 우연히 맞았다.
  → Director가 스폰 직후 착지점(방 중앙)으로 채운다.
- **보스 진입로가 막혔다(직전 세션 회귀)** — 레이저 프리팹에 심은 차단벽이 Stage1 통로 26곳을
  전부 막았다. 어느 슬롯이 Quest가 되는지는 시드마다 달라 정적 배치로는 불가.
  → 레이저 벽 제거, `MapContentSpawner`가 역할 확정 시점에 **Quest 존만** 네 변으로 감싼다.
- **중간보스 감축** — 마커 수 = 스폰 수. 엘리트 그룹(5)은 마커 1개 제한 + 초과 마커 정리
  → `ZoneL_typeC` 4 → 1. 위치 수동 조정은 재실행해도 보존(앞쪽 마커 유지).

### 이어서 완료 — Play 피드백 4건 (`60f3862`·`66ac555`)

**★ 교훈: 민경 님 보스 코드는 아레나 바닥이 `Ground` 레이어에 Y=0이라고 가정한다(BossScene 기준).
생성맵 보스룸 바닥은 `Default` 레이어에 Y≈0.61이라 바닥을 찾는 모든 레이캐스트가 조용히 빗나간다.**
증상은 제각각으로 보이지만 뿌리가 하나였다.

- **보스를 때릴 수 없었다** — 보스 몸 콜라이더는 `HurtBox(EnemyHurtBox=14)` 하나뿐이고 루트에는
  콜라이더가 없다. 플레이어 공격 마스크는 `1280`(Enemy|Projectile)이라 14를 못 봤다.
  → 기본공격·Q `17664`, 궁극기 타겟팅 `16640`. **프로젝트 관례 = 적 대상 마스크는 Enemy|EnemyHurtBox**
  (폭탄의 `enemy=16640`이 그 근거).
- **폭탄이 공중에 떴다** — `BombLauncher.groundMask`/`BombController.ground`가 `Ground(8)`뿐 →
  착지 Y 탐색·낙하 판정·장판 스냅 전부 실패. → `9`(Ground|Default).
- **장판과 몸체가 어긋났다** — `JumpController`가 바닥 레이어를 `"Ground"` 하드코딩 + 착지 Y를
  `0f` 고정. → `groundMask` 인스펙터화(비면 Default+Ground 폴백) + `hit.point.y` 사용.
- **Quest 통로 차단** — `layprefab`(레이저)에 `LaserBlockWall`(3.17×8×0.6, Wall) 생성.
  `Level_wall_hallway`에 26개 들어 있어 **26곳이 모두 막힌다**. 열어야 할 곳은 인스턴스에서 끄면 된다.
  도구 = `Tools/Map/Authoring/Setup Quest Laser Blockers`.

### 이어서 완료 — Task 3 Director + 충전 기둥 + 존 스폰 마커 (`3c3833d`·`77a6458`)

**MapScene에 보스가 등장하는 경로가 처음으로 연결됐다.** `BossEncounterDirector`(씬 상주
NetworkObject)가 등장·전투 전환의 단일 소유자다: 도착 ACK → 참가자 잠금 → 보스 1회 스폰(+18m)
→ 곡선 하강 → 착지 → `BeginCombatServer()`(NavMesh Warp + 잠금 해제 + `OpenBT` 한 번에)
→ 보스 `Unit.Died` → `Capture(cleared: true)` → 결과 화면.

- ⚠️ **순서 함정**: `RunningOnlyOnServer`가 `OnNetworkSpawn`에서 `navMeshAgent.enabled = IsServer`로
  되돌린다. **에이전트 차단은 반드시 `Spawn()` 이후** — 아니면 상공의 보스를 NavMesh로 끌어내린다.
- **충전 기둥 4개**: `bossroom`의 `Env_Mv_bosscharger_upper`에 NetworkObject·NetworkTransform·
  BoxCollider·`ChargingObject`를 저작 도구로 부착(`Tools/Map/Authoring/Setup Boss Charge Pillars`).
  Director가 스폰 직후 `ChargeController.SetList`로 주입 → BT의 `SetChargingStateAction`이 그대로 동작.
- ⚠️ **기둥 레이어는 `Enemy(8)`** — BossScene은 `EnemyHurtBox(14)`지만 플레이어 공격 마스크가
  `m_Bits=256`(Enemy)뿐이라 14에 두면 기둥을 때릴 수 없다. **민경 님과 정리 필요**(마스크를
  넓힐지, 기둥을 8에 둘지).
- `ChargingObject` 절대 Y → 로컬 숨김/활성 위치 + Hidden/Rising/Active/Lowering 상태로 재작성.
  같은 세션 반복 사용 가능.
- **존 스폰 마커 복구**: 존 프리팹 12개 중 `ZoneLayout`이 3개만 남아 있었고 마커는 0개였다
  (v11 재작업·아트 교체 때 유실). `Author ZoneLayouts (from Catalog)` 재실행 → 11존 복구,
  마커 27개. `ZoneL_typeC` = **GauntletBot 중간보스**. 마커 위치는 러프값 → Play 후 조정.
- 배선 도구: `Tools/Map/Authoring/Wire Boss Encounter (MapScene)`(MapScene 열고 실행, 재실행 안전).
- 미검증: Play/MPPM 전체 흐름. 미구현: 대사 HUD·ESC 만장일치 스킵(Task 5), 착지 카메라
  흔들림·먼지 VFX(Task 4 나머지).

### 진행 중 (2026-07-28 이어서) — 승인 계획서 전제 갱신 완료

`Docs/superpowers/plans/2026-07-24-boss-encounter-intro.md`에 **Revised Premises 9항** 추가.
Task 1·Task 7(경계 부분) 완료 표시, 씬 경로(`MainFlow/4.MapScene`) 전면 수정, Task 2를
"기존 `GameplayAccess` 게이트 확장 + dash/fall/life 차단"으로 재작성, Task 3에 클리어 판정
(`Unit.Died` → `Capture(cleared: true)`)과 `PartyWipeWatcher` 경합 항목 추가.

~~다음 = Task 2~~ **완료**. 다음 = **Task 3**(`BossEncounterDirector`). Task 2가 수정한 파일 = `1.Scripts/Player/{PlayerEncounterLock.cs(신규),
PlayerInputReader, PlayerStateController, PlayerMovement, DefaultAttackController, Player,
PlayerDashController, Skill/PlayerSkillController, Fall/PlayerFallController,
Life/PlayerLifeCycleController, Life/PlayerLifeInputPolicy}`, `1.Scripts/Unit/StatusEffectController.cs`,
`2.Prefabs/Player/Player.prefab`. ⚠️ 플레이어 계통은 은희 담당 영역 — 동시 수정 주의.

### ▶▶ 다음 세션 시작점 (2026-07-29 마감 · 팀장 지시 = **분석 먼저, 수정 나중**)

팀장 지시 원문 취지: "지금 너무 수정만 하는데 달라지는 게 아니니 **분석부터 제대로** 해야 한다.
다음 세션에서 작업하자." → 다음 세션은 **코드를 고치기 전에 아래 4건의 분석을 먼저 끝내고
결과를 보고한 뒤** 지시를 받는다. 상세 경위·교훈은 `Docs/_local/lessons.md` #20~#26 +
「⏳ 미해결」 절.

**분석 1 — `Env_Wall_doorframe` 콜라이더 부재 (확인됨, 원인 미확정)**
- 사실: 문틀은 `MeshFilter`+`MeshRenderer`만 있고 콜라이더가 없다(인스펙터 확인). 그래서 통과된다.
- 벽 투명화와 무관함은 확정: `WallOcclusionDriver`에 `Collider`/`Physics`/`SetActive`/`enabled` 참조 **0건**
  (셰이더 전용). 투명해 보이는 것과 콜라이더 부재는 별개 사건이다.
- 분석할 것: `MapColliderAuthoring`의 이름 필터에 doorframe 계열이 포함되는지, 문틀 26개(+`Env_laser`)의
  콜라이더 유무 전수, 문틀이 통행을 막아야 하는지(문이므로 열려 있어야 할 수도) — **디자인 의도 확인 필요**.

**분석 2 — M키 지도 ↔ 우측하단 미니맵 탐사 동기화 (미구현)**
- 사실: 미니맵은 `_MaskTex`(R=explored, G=visible)로 점점 밝아진다. M키 지도(`MapOverviewUI`)는
  슬롯 사각형만 그리고 탐사 개념이 없다.
- 요구: 미니맵에서 밝아진 영역이 M키 지도에도 같이 그려져야 한다.
- 분석할 것: `MinimapController.GetExploredBits()`(이미 존재) + `_worldRect` 좌표계를 `MapOverviewUI`가
  어떻게 소비할지. 존 사각형 위에 탐사 마스크를 덮는 방식 vs 베이크 텍스처를 그대로 확대 표시하는 방식 —
  둘 중 어느 쪽이 지금 UI 구조에 맞는지 먼저 결정.

**분석 3 — `BossArea` ✅ 해결(2026-07-29): BT가 태그로 스스로 찾는다, 주입 불필요**
- No.23 BT는 `FindObjectWithTagAction`(Tag=문자열 `"BossArea"`)으로 씬에서 직접 찾아 `BossArea`
  GameObject 블랙보드 변수를 채우고, 같은 부모 아래 `SetEnableBoxColliderAction(Enable=true)`로 켠다
  (`No.23.asset` rid 1567581773390152037·038 — 읽기만 함). **Director 주입 불필요.**
- MapScene 현황은 이미 완비: `bossroom.prefab`에 tag `BossArea` 트리거(20.98×2×20.98, center y=1),
  충전 기둥 4개 = `Env_Mv_bosscharger_upper` + BoxCollider + ChargingObject + NetworkObject +
  NetworkTransform(maxHp 5·defense 0·riseHeight 1·moveSpeed 1 = KMK와 동일),
  `BossEncounterDirector.chargingObjects`에 4개 전부 연결.
- ⚠️ **BossScene의 BossArea를 MapScene으로 복사해 오면 안 된다** — 태그 2개가 되어 `FindObjectWithTag`가
  어느 것을 잡을지 보장되지 않고, 기둥 8개 중 4개만 동작하며, `TwentyThreeArenaContext`가 함께 오면
  보스 이중 스폰이 된다. `BossEncounterDirector.ValidateBossAreaTag()`가 태그 0개/2개 이상을 에러로 잡는다.
- 부수 확인: `HomePoint`는 코드·No.23 BT 어디서도 참조 0건(미사용 마커). `8.BehaviorTreeGraph/Boss/
  BossArea.asset` 그래프는 어느 씬·프리팹도 참조하지 않는 고아(예제 빌더 산출물).

**분석 4 — ✅ 해결(2026-07-29): 존 저작은 이미 맞았고, 검증 도구가 잘못된 대상을 읽었다**
- **거짓 경보였다.** `Save Placements`는 **씬의 Stage1 인스턴스**에 쓰고 씬을 dirty 처리한다(프리팹에
  Apply하지 않음). 그런데 `Validate Slot Authoring`은 `Stage1.prefab` **에셋**을 읽어 씬 오버라이드를
  못 봤다 → 재저작을 마친 뒤에도 "미저작 9건"을 계속 보고했다.
- 프리팹+씬 오버라이드(슬롯 대상 61건)를 병합한 실제 상태 = **실질 미저작 0건**. 도구가 세던 3건은
  전부 도달 불가 조합이었다: Slot 5·7 × `ZoneS_typeA`(둘 다 Boss/Spawn 후보 2곳뿐 → 절대 Combat 안 됨),
  Slot 8 × `Quest01`(`QuestPrefab=Quest02` 지정 시 카탈로그 Quest 풀 미조회).
- Quest는 의도대로 고정됨: Slot 4 `IsQuestCandidate` 0으로 내리고, Slot 8만 후보 + `QuestPrefab=Quest02`
  → 시드 무관 항상 Slot 8 / Quest02. 부작용으로 카탈로그 Quest 풀 2종은 사실상 미사용.
- 셔플은 카탈로그대로 돈다: Large 0·1·2 ↔ A/B/C 1:1 순열 / Medium은 Slot 3 `FixedPrefab=ZoneM_typeC`가
  pinned 제외되어 남은 풀 {A,B} 2개 ↔ Slot 4·9 2곳 1:1 / Small 전투는 Slot 6 하나 ↔ `ZoneS_typeA`.
  총 조합 = 6 × 2 × (Boss/Spawn 스왑 2) = 24가지. **Medium은 여유 0** — Medium 슬롯을 늘리거나 Slot 4를
  Quest 후보로 되살리면 즉시 풀 부족(재사용)으로 넘어간다.
- 남은 잔재: 옛 GUID 참조 9건(Slot 3:3, 4:3, 8:2, 9:1) + 끊긴 `QuestPrefab` 2건. 런타임 무해(null은
  프리팹 비교에 안 걸림), 리포트만 오염. 청소 도구 = `Tools/Map/Authoring/Cleanup Slot Authoring (dead refs)`.
- ⚠️ **구조적 취약점**: 저작 정본이 씬 오버라이드에만 있다 → Stage1 인스턴스에서 Revert 한 번이면
  61건이 날아간다(복구선 = 커밋된 MapScene). Stage1을 다른 씬에 인스턴스화하면 저작 0 상태로 떨어진다.
  근본 해소는 씬 오버라이드를 Stage1.prefab에 Apply해 정본을 프리팹으로 옮기는 것(씬 수술 리스크, 미착수).

**참고 — 이번 세션에 실제로 검증된 것**
- 보스 등장 흐름 정상: 로그 `SpawnPoint를 방 중앙 (500.49, 0.61, 0.49)으로 설정` → 하강 → `전투 시작 — BT 개방`.
  스크린샷의 보스 좌표 `(13.33, 0.08, -4.83)`은 그 수정 **이전** Play다.
- 미니맵 베이크 자체는 정상(`중앙 샘플 평균 밝기 0.308`). 남았던 원인은 실루엣 마스크였다.
- 레이저 통로 차단(26곳)은 **의도된 것**이며 유지한다. 내 판단으로 제거했다가 원복했다(`5dee39d`).
- ⚠️ 보스룸 저작 도구(`Rebuild Boss Room Bounds`)는 `PlayerArrivalPoints`·`BossLandingPoint`를 **재생성**한다
  → 실행 후 `Wire Boss Encounter (MapScene)`를 반드시 재실행(참조 끊김). 손으로 옮긴 지점도 초기화된다.

### 이번 세션 완료 (2026-07-29 · 아레나 자립화 + 유령 타겟팅)

- **`BossArenaContext` 신설 + bossroom.prefab에 부착·저장** — 아레나가 자기 부품(착지점·BossArea·
  충전 기둥 4개·도착 지점 3개)을 프리팹 내부 참조로 들고 있다. 참조가 전부 프리팹 내부 fileID라
  **절대좌표가 개입하지 않는다**. `BossEncounterDirector`는 `arena` 하나만 물어보고, 씬 배선이
  비어 있으면 여기서 채운다 → 다른 씬에 인스턴스화해도 동작. 저작 도구가 기준점을 재생성해
  참조가 끊기는 사고도 사라진다. 부착 도구 = `Tools/Map/Authoring/Wire Boss Arena Context (bossroom)`.
  - **위치 검증 결과: 착지점 localPos (0.49, 0.61, 0.49) == BossArea localPos, 간격 0.000m.**
    즉 "아레나 중앙으로 안 간다"의 원인은 절대좌표 하드코딩이 아니다(코드 전수 검색에서도 0건).
    `Spawn Point`·`ArrivePoint` 블랙보드 Vector3는 둘 다 런타임에 채워진다
    (Director→`SpawnPointer`→`GetSpawnPointAction` / `JumpController.SetTarget`). 남은 유일한 위험은
    `GameObject.Find("BossLandingPoint")` 이름 폴백 → 이제 arena를 먼저 보므로 최후 폴백으로만 남았다.
- **유령(Soul) 상태에서 몬스터가 더 이상 반응하지 않는다** — `MonsterTargeting.IsAttackable`(신설)이
  단일 기준. 기존 `IsTargetValid`가 `null`+`activeInHierarchy`만 봐서 Soul이 통과했고, 사망 직전에
  잡힌 타겟이 유지되어 몬스터가 유령을 쫓고 공격 모션까지 냈다(데미지는 Soul에서 hurtbox가 꺼져
  안 들어갔으므로 **행동만 남은 상태**였다). 적용: `MonsterBase.IsTargetValid`·`FindNearestTarget`,
  `BossBase` 동일 2곳, `GauntletBot.CountNearbyPlayers`(유령이 스매시 단계 인원수에 잡히던 것).
  - 판정 기준은 `ShouldEnableHurtbox`가 아니라 `PlayerLifeState.Alive`다 — 무적 프레임(대시 회피)이
    hurtbox를 끌 때 "맞지 않는다"를 "노리지 않는다"로 해석하면 대시 한 번에 타겟이 풀린다.
  - ⚠️ **No.23 보스는 미적용**: 타겟 선정이 BT 노드(`FindClosestWithTagAction` 등, 민경 님 영역)라
    코드에서 못 막는다. 같은 증상이 보스전에서 재현되면 BT 쪽 조건 추가가 필요하다.

### ▶ 진행 중 — "23호가 landing 직후 (0,0,0) 근처로 이동" (2026-07-29, 원인 2개 후보 모두 차단·검증 대기)

증상: 하강·착지까지 정상, **착지 직후** `TwentyThree(Clone)` position이 x≈1.9(맵 중앙)로 이동.
아레나는 x≈500이므로 500m 순간이동이다. 절대좌표 하드코딩은 코드 전수 검색 **0건**이었고,
착지점·BossArea localPos는 간격 **0.000m**로 정합했다. 그래서 원인은 다음 둘 중 하나다.

**후보 A — NavMeshAgent를 메시 밖에서 켰다 (착지 시점에 정확히 실행됨 = 가장 유력)**
- `SnapBossToNavMesh`가 `_bossAgent.enabled = true`를 **샘플링보다 먼저** 했다. NavMesh 밖에서
  에이전트를 켜면 Unity가 내부 위치를 가장 가까운 메시에 맞추는데, 아레나에 메시가 없으면
  그 "가장 가까운 곳"이 맵 본체(원점 근처)다 → 보스가 끌려간다.
- 수정: **샘플 먼저 → 실패하거나 착지점에서 2.5m 이상 떨어진 메시를 잡으면 에이전트를 켜지 않고
  에러 로그**. 아레나 안에서 못 움직이는 게 원점으로 날아가는 것보다 낫다(lessons #26 원칙).
- ⚠️ 이 경우 보스가 아레나에서 이동하지 못한다 = **NavMesh 베이크를 고쳐야 하는 진짜 문제**가 드러난다.
  `MapNavMeshBaker`는 `useGeometry=PhysicsColliders` + `layerMask=Default만` + `collectObjects=All`이고,
  bossroom의 `BossFloorCollider`(BoxCollider)는 layer 0(Default)이라 **수집 대상은 맞다**. 경계 5개는
  layer 7(Wall)로 올바르게 벽 취급. 즉 배선상 bossroom이 특별 취급되는 곳은 없다 — 남은 의심은
  아트 바닥 콜라이더 유실/unreadable mesh다(베이커에 `UnreadableMeshColliderBakeScope` 폴백이 있는 이유).

**후보 B — BT 절대 위치 블랙보드가 (0,0,0)으로 시작한다 (구조는 확인, 발동 여부 미확인)**
- No.23 루트가 `ParallelAllComposite` + `Start` 8브랜치 **병렬**이다. 브랜치[1]에
  `NavigateToLocationAction(Location="Spawn Point")`가, 브랜치[4]에 그 값을 **쓰는**
  `GetSpawnPointAction`이 있다 → 쓰기가 먼저라는 보장이 없다.
- `ArrivePoint`도 같다. `JumpController.SetTarget`이 채우는데 연출 착지 중엔 `_isCinematicLanding`으로
  **조기 반환해 한 번도 안 채워진다**. 그 값을 `SetPositionThroughRaycastAction`·`MoveForDurationAction`이
  **위치에 직접 쓴다**(파인딩 아님) → (0,0,0) 순간이동. 단 이들은 `SwitchComposite`(상태 스위치) 아래라
  첫 틱 무조건 실행은 아니다 — 그래서 "구조는 확실, 발동은 미확인"이다.
- 수정: `BossEncounterDirector.SeedArenaPositionBlackboard`가 스폰 직후 `Spawn Point`·`ArrivePoint`를
  **방 중앙으로 미리 덮는다**. BT 그래프는 보스 담당 영역이라 손대지 않고 최악값만 없앴다.

**✅ 판별 완료 (Play 1회, Editor.log 확인)** — 원점 이동 **해결**됨:
- `[BossEncounter] 보스 NavMesh 부착 완료 — (500.49, 0.67, 0.49) (착지점 오차 0.06m)`
  → **아레나에 NavMesh 정상 존재. 후보 A 기각.** bossroom 베이크는 문제없다.
- `[BossEncounter] BT 위치 블랙보드 초기화 — Spawn Point, ArrivePoint = (500.49, 0.61, 0.49)` → 시딩 동작.
- 즉 실제로 들었던 원인은 **후보 B(블랙보드 (0,0,0))** 쪽이다.

### ▶ 후속 — "보스가 공격만 하고 걷지 않는다" = 이동 배선이 아니라 **거리창** 문제 (2026-07-29 분석 완료)

- **로그 증거: 전체 런에 `Walk`/추격 상태 전환 0건.** Idle→Upper/LeftHook/RightHook/Grab/Jump→Idle 반복,
  마지막에 Charging→Groggy→Dead. 걷는 상태에 들어간 적이 없다.
- 원인 = `TwentyThreeBasicAttackChoice` 거리창이 거의 전 구간을 덮는다(프리팹 값):
  hook 0~3 / upper 0~3 / grab 0~1 / jump 5~10 / dash 10~20, 가중치 50/50/100/100/100.
  **빈 구간은 3~5m와 20m 초과뿐**이고 `GetRandomAttack`은 그때만 `None`을 반환한다 → BT가 Walk로 가는 건
  그 두 구간에서만이다. 플레이어가 붙어 있으면 항상 공격이 뽑히므로 **걷지 않는 게 설계상 정상**이다.
  → **팀장 확인(2026-07-29): 3~5m Walk 밴드 하나는 의도된 설계다.** 그 안쪽은 hook/upper/grab이 처리한다.
  남은 쟁점은 수치를 bossroom 크기에 맞추는 것뿐이다(아래).

**아레나 스케일 실측 — 튜닝 전제가 바뀌었다 (2026-07-29)**

| | 벽 안쪽 전투 구역 | 최대 거리(대각선) | BossArea 트리거 |
|---|---|---|---|
| BossScene(튜닝 환경) | Ground scale 5 → 약 48×48m, 벽 ±24m | ~68m | 10×10, 중심에서 **x+2.79 어긋남** |
| bossroom(실전) | **21.0×21.0m** | **29.67m** | 20.98×20.98, 방 전체·정중앙 |

- bossroom이 KMK 테스트장보다 **약 2.3배 작다.** 현재 거리창은 48m 방 기준으로 잡힌 값이다.
- ⚠️ **사각지대**: dash가 20에서 끊기는데 bossroom 최대 거리는 29.67m → **20~29.67m 구간(코너 대각)**에서
  `GetRandomAttack`이 `None`을 반환해 Walk로 간다. WalkSpeed 2로는 10m를 5초 걸어온다.
- 원칙: **공격 리치는 아레나 크기와 무관, 이동 공격과 사각지대만 아레나에 의존한다.**
  hook/upper/grab(0~3, 0~3, 0~1)은 애니메이션 리치이므로 스케일하면 안 된다(팔이 안 닿는 거리에서 때린다).
  그리고 **가장 먼 공격의 max는 항상 아레나 대각선 이상**이어야 한다.
- 권고 2안 (프리팹 값이라 팀장/민경 승인 후 변경, 아직 미적용):
  - **안 1(최소 변경, 권장)**: dash max **20 → 30**. 나머지 그대로 → 사각지대 0.
  - **안 2(방 크기에 맞춤)**: jump 5~9 / dash 9~30. 대시를 더 일찍 쓰게 하고 사각지대 0.
  - 별건: **WalkSpeed 2 → 3.5 이상** 검토(NavMeshAgent 기본값보다 낮아 걸어도 티가 안 난다).
- 참고: KMK의 BossArea는 10×10에 x+2.79 어긋난 반면 bossroom은 방 전체·정중앙이다. BT가 켜고 끄는
  그 콜라이더의 범위가 두 환경에서 크게 달라, KMK에서 "구역 밖"이던 거리가 bossroom에선 전부 "구역 안"이다.

**⚠️⚠️ 위 권고는 무효 — `origin/feature/Boss` 3커밋(민경, 미머지)이 거리창을 SO로 옮기고 값을 바꿨다 (2026-07-29 확인)**

`89bc9e1`(페이즈별 Page SO) → `e61999d`(SO 리스트 통합) → `9a2cad0`(넉백 SO + 씬 리네임).
거리창은 이제 프리팹 필드가 아니라 `9.ScriptableObject/Enemy/Boss/Wells&No.23/TwentyThreePage {0,1,2}.asset`이고,
`PageEventAction` BT 노드가 페이즈 전환 시 `PageEvent(page)`로 교체한다. 실측값:

| Page | hook | upper | grab | jump | dash |
|---|---|---|---|---|---|
| 0 | 0~4 (50) | 0~4 (35) | 0~4 (15) | 5~10 (60) | **5~10** (40) |
| 1 | 0~4 (40) | 0~4 (40) | 0~4 (20) | 5~10 (45) | **5~10** (55) |
| 2 | 0~4 (35) | 0~4 (40) | 0~4 (25) | 5~10 (25) | **5~10** (75) |

- **세 페이지 모두 0~4와 5~10만 덮는다. 10m 초과를 덮는 밴드가 하나도 없다.** bossroom 최대 거리는
  대각선 **29.67m** → **10~29.67m 전체가 Walk 구간**(가능 거리의 약 2/3). 이전(dash 10~20)과 성격이 반대다:
  "사각지대가 좁아 안 걷는다" → **"멀면 거의 항상 걷는다"**.
- 따라서 **`WalkSpeed`(=`Enemy.moveSpeed`)가 2인 것이 이제 치명적이다.** 20m를 10초 걸어온다.
  이전엔 코너 케이스였지만 지금은 상시 경로다. → **최우선 조정 대상은 dash max가 아니라 WalkSpeed.**
  권고: `moveSpeed` 2 → **5~7**(NavMeshAgent 기본 3.5보다도 낮은 현재값은 21m 방에 안 맞는다).
  대안(더 침습적): dash max를 대각선까지(예: 5~30). 단 페이즈별 jump/dash 비중 설계(40→55→75)를 깨뜨린다.
- ⚠️ **팀장이 확정한 "3~5m Walk 밴드"가 이미 4~5m로 절반이 됐다** — hook/upper/grab max가 3→4로 올랐다.
  의도 재확인 필요(민경과).
- 새 구조 자체는 개선이다: `GetRandomAttack`의 `None`→Walk 로직은 그대로이고, 수치만 SO로 빠졌다.

**⚠️ 머지 리스크 (가장 큰 이슈) — 분기점 `0aba7b3`(7/20), 미머지**

- 🔴 **`No.23.asset`이 양쪽에서 변경.** 민경 쪽 11,191줄(+6041/−5546). 그런데 **내 워킹트리도 이미 dirty
  (+3373/−3951)** — 이 세션 전부터 그랬고 내가 만든 변경이 아니다. 내용은 rid 재번호 + `Name: Self` 같은
  블랙보드 변수 삭제 = **Unity 재직렬화 churn**이다. `CommonMeleeRobot.asset`도 같은 성격(+516/−516).
  → **이 churn을 커밋하면 민경의 11k줄 작업을 덮어쓴다.** 머지 전에 반드시 `git checkout --`으로 폐기해야 한다
  (메모리 규칙 "8.BehaviorTreeGraph 수정금지"와도 일치). 거대 생성 YAML은 3-way 머지가 사실상 불가능하다.
- 🟡 `ProjectSettings/EditorBuildSettings.asset` 양쪽 변경(내 쪽 dirty + 민경 씬 리네임). union 성격이라 수동 병합 가능.
- 🟢 **소스 파일 충돌 0.** 민경이 건드린 것 = `BaseAttackChoice`, `TwentyThreeBasicAttackChoice`, `Unit.cs`,
  `KnockbackAttack`, `TwentyThreeWells_*`, `TwentyThree.prefab`, `PageEventAction`(신규), `IKnockbackSettable`(신규).
  내가 건드린 것과 **겹치는 파일이 없다.** 특히 `Enemy.cs`는 민경이 안 건드려 내 `ApplyOptionalSpeed`가 그대로 산다.

**씬 리네임 파급 — `KMKScene.unity` → `BossScene.unity`**

- `.meta`를 유지해 **GUID 보존 → 에셋 참조는 안 깨진다** ✅. `TwentyThreeArenaContext`·BossArea·Cylinder1~4·
  HomePoint 모두 BossScene에 잔존 → MapScene과 분리 유지, 이중 스폰 위험 없음(내 `ValidateBossAreaTag` 가드도 유효).
- 하지만 **이름 문자열 참조는 깨진다.** 갱신 대상: `Map/BossArenaContext.cs`, `Map/BossEncounterDirector.cs`,
  `Map/Editor/BossEncounterWiring.cs`, `Map/Editor/BossRoomAuthoring.cs`(주석), `CONTEXT.md`,
  `Docs/tech/game-structure-uml.md`, `Docs/tech/script-inventory.md`, `Docs/_local/lessons.md`.
  그리고 **`Assets/1.Scripts/Scene/BossScene.cs`는 스크립트 이름이 그대로다** — 씬만 리네임됐다.
- 그 커밋에서 FMOD `Sound`/`BGM` 오브젝트가 **제거**됐다(164줄 삭제). 보스 테스트 씬에서 BGM이 사라진 것 —
  사운드 담당(민경) 의도인지 확인 필요.

**부수 — `Unit.cs`의 `////`**

`// 방어력 경감률 적용` → `//// 방어력 경감률 적용`으로 바뀌었는데 **바로 아래 계산 코드는 그대로 살아 있다.**
경감률을 끄려던 시도였다면 실패했다(여전히 적용 중). 커밋 메시지는 "주석 정리"라 의도일 수도 있으나
데미지 밸런스에 직결되므로 확인 필요.

**부수 — 넉백**

`KnockbackAttack`에 `SetKnockbackStrength`(+`IKnockbackSettable`)가 생겨 세기를 SO로 주입하게 됐지만,
내가 보고한 **"Floor 넉백 매 프레임 재적중 + 피해 0"은 해결되지 않았다** — 중복 방지 로직은 없다.
이제 "세기 0"과 "피해 0"을 구분해서 봐야 한다.

**✅ `ChaseSpeed` 경고 정리 완료** — `Enemy.ApplyOptionalSpeed`로 교체했다. 부재는 **조용히 통과**(그래프마다
선택적으로 쓰는 변수이므로 정상), 반대로 **그래프가 쓰는데 넣을 값이 0**이면 그때 경고한다. 이유:
값 0인 프리팹에 이름만 맞추는 잘못된 수정을 경고가 유도하고 있었다.
- **이동 배선 자체는 정상**: NavMesh 부착 OK / 추격 노드 `NavigateToTargetAction(Speed→WalkSpeed)` /
  `Enemy`가 `WalkSpeed = moveSpeed = 2` 기록 성공(루트 `BehaviorGraphAgent`는 1개뿐 — 프리팹에 보이는
  두 번째 항목은 `m_GameObject: 0`인 고아 직렬화라 `GetComponent`가 올바른 쪽을 잡는다).
  단 **WalkSpeed 2는 보스치고 매우 느리다**(NavMeshAgent 기본 3.5보다 낮음) — 걸어도 티가 안 난다.
- ⚠️ **함정: `ChaseSpeed` 죽은 배선.** `[Enemy] ChaseSpeed 변수를 얻어오는 것에 실패` 경고가 뜨는데,
  No.23 그래프에 `ChaseSpeed`가 **없다**(가진 그래프는 `Enemy/CommonMeleeRobot.asset`뿐). 게다가
  `TwentyThree.prefab`의 `chaseSpeed = 0`이다. 지금은 무해하지만 다음 사람이 경고를 보고 이름을 맞추면
  **추격 속도 0이 되어 보스가 진짜로 안 움직인다.** 이름을 손대기 전에 값을 먼저 넣어야 한다.

### ▶ 후속 — 대시 2회차부터 이동 없음 = **서버 거부 후 즉시 EndDash** (원인 사슬 확정, 사유는 로그 대기)

- `PlayerDashController.RespondDashClientRpc`가 거부/중단 시 곧바로 `stateController.EndDash()`를 호출한다.
  호스트에서는 ServerRpc→ClientRpc 왕복이 사실상 같은 프레임이라 **`PlayerDashState.Tick`이 변위를 한 번도
  적용하기 전에 상태가 끝난다.** `PlayerDashState.Enter`가 `SetAnimatorMoving(false)`를 호출하므로 증상이
  "가만히 있는 애니메이션만 순간 출력 + 이동 0"으로 정확히 나타난다.
- 왜 1회차만 통과하는지는 서버 `PlayerDashValidationManager.ValidateRequest`의 거부 사유에 달렸다.
  유력 후보 = `NoFreshSnapshot`(clientLocalTime↔serverNow 시계 도메인 + `SnapshotFreshnessTolerance`) 또는
  충전 장부 epoch/revision 불일치.
- **거부가 로그 0줄이었다**(조용한 실패). 경고를 추가했다 → 다음 Play에서
  `[Dash] 서버가 대시를 취소했습니다 — approved=… / reason=… / 남은시간=… / 권한충전=…` 한 줄로 확정된다.
- ⏩ **후속 결론은 아래 「3. 대시 — 쿨타임 결함 3건 수정 완료」 참조.** 여기 적힌 후보 중
  `NoFreshSnapshot`은 확정되지 않았고, 실제로 잡힌 것은 "거부 시 충전 미환불 + 스냅샷 기준 충전 판정 +
  오프라인 멈춘 시계" 3건이다.

### ▶ 부수 발견 (미처리, 보고만)

- `[No.23] Floor 넉백 공격 적중: Player(Clone) (피해 0)`이 **19회 이상 연속**으로 찍힌다.
  장판 넉백이 매 프레임 재적중하면서 데미지는 0 — 중복 방지 누락과 데미지 값 둘 다 의심된다.

### ▶▶ 다음 세션 시작점 (2026-07-29 마감 · 팀장 지시)

**1. 몬스터가 NavMesh 없는 공중을 걸어서 건너온다 (확정 — 원인 미규명)**
- 팀장 확인: 몹이 순간이동한 게 아니라 **걸어서** 고립 플랫폼으로 건너왔다. 즉 NavMesh가 **틈 위 공중에
  깔려 있다.** `ReattachAgents` Warp는 원인이 아니다(그 건은 별개로 1.5m 제한으로 수정 완료 — lessons #31).
- 조사할 것: `NavMeshSurface` 설정(`agentClimb`/`agentRadius`/voxel size). 낮은 단차를 이어붙이는
  `agentClimb`가 크면 플랫폼 사이 틈이 walkable로 연결된다. `MapNavMeshBaker.Awake`가 강제하는 값은
  `useGeometry`·`collectObjects`·`layerMask`뿐이고 **에이전트 파라미터는 씬 세팅 그대로**다 — 거기부터 본다.
- ⚠️ 이번 세션에 내가 바꾼 것도 용의자다: NavMesh를 **다리가 열린 상태로** 굽게 했다(`BakeOpenScope`).
  카브(`ZoneBridgeGate` 의 `BridgeGapCarve`)가 안 먹으면 다리가 물러난 구간이 walkable로 남는다.
  Play에서 그 오브젝트가 생성되는지, 크기가 다리 구간을 덮는지 먼저 확인할 것.

**2. 민경 팀원 커밋 받기 (`origin/feature/Boss`) — 커밋 추가 도착, 총 13개 ahead**
- 기존 3개(`89bc9e1` 페이즈별 Page SO · `e61999d` SO 리스트 통합 · `9a2cad0` 넉백 SO + BossScene 리네임)에
  더해 `465a934`(몬스터 시간 제어 HitStop/SlowMotion + `WaitForAnimState` BT 노드) ·
  `01fd648`(`SetNumberWithTag` onlyCountRoot) · `677ffc4`(No.23 BT 그래프) · `63242b2`(maxRageCount 2→5).
- 팀장 방침(2026-07-29): **feature/Boss 쪽을 권위로 받고 내 로컬 수정본은 폐기**한다.
- 상세 분석·리스크는 이 문서 위쪽 「⚠️⚠️ 위 권고는 무효」 절 참조.

**2-b. ✅ 머지 완료 (2026-07-29 · `3ef3cab`, 컴파일 0에러 0경고 / Play 검증 대기)**

- 충돌 10건 해결: 보스 소유 에셋(BossScene·TwentyThree·Wells·No.23) = theirs / TagManager·Unit.cs =
  ours / BombLauncher = theirs / ChargeController·JumpController = 수동 병합 / Player.prefab = 충돌
  블록만 ours.
- ⚠️ **Player.prefab에서 살린 저쪽 clean hunk**: `PlayerDefaultAttack.targetLayer` ·
  `DefaultAttack.hittableLayers` 가 **Enemy(8) → EnemyHurtBox(14)** 로 전환됐다. 보스와 일반몹
  (`ModularRobots_R1`) 모두 레이어 14 노드가 있어 정합. `--ours`로 파일 전체를 되돌리면 256으로
  회귀해 **보스를 못 때리게 된다** — 다음에 이 파일 충돌 시 주의.
- 머지 후 컴파일 수정 1건: `BossBasicAttackChoice.PageEvent(int)` 구현 추가(`BaseAttackChoice`에
  추상 멤버 신설). 코드 FSM 보스는 Page SO 체계가 없어 의도적 no-op.
- **의도적으로 안 받은 것**: Player.prefab의 `AudioListener` + `FMODUnity.StudioListener`.
  이 머지는 FMOD 파일·참조 코드를 하나도 안 가져오고(`51adcf1`에서 사운드 분리) 이 워킹카피에 FMOD가
  미설치라 받으면 Missing Script가 된다. AudioListener를 Player 프리팹에 두면 멀티에서 리스너가
  여러 개가 되는 문제도 있다 → `feature/Sound` 머지 때 함께 검토.
- 머지 직후 Unity가 BT 에셋 3개(`No.23` +4598/-5033 등)를 재직렬화했다 → 규칙대로 폐기했다.
  커밋하면 민경 저작 바이트를 덮어쓴다.
- 후속 커밋 `c273ad8`: `EditorBuildSettings` 경로를 BossScene으로 갱신.

**2-c. 머지 후 Play 검증에서 나온 회귀 3건 — 수정 완료 (2026-07-29)**

- **보스가 멈추고 애니메이션이 아무것도 안 보인다(사망 연출 포함)** = `MonsterTimeController.HitStop` 재진입 결함.
  코루틴이 복원값으로 `currentScale`을 기억했는데, HitStop 진행 중(배율 0)에 또 맞으면 두 번째
  코루틴이 **0을 복원값으로 기억** → 0.25초 뒤 배율을 0으로 "복원" → `animator.speed`·`agent.speed`
  **영구 0**. `Enemy.TakeDamage`가 피격마다 부르므로 0.25초 내 2연타면 재현된다.
  게다가 BT의 `WaitForAnimStateAction`은 `normalizedTime`을 보므로 애니메이터가 멈추면 **BT도 로그
  없이 영원히 대기**한다(장판·데미지는 시간 기반이라 계속 돌아 원인이 가려진다).
  → 복원값을 최초 진입에서만 기록 + `OnDisable`에서 배율 1 복구.
  ⚠️ 부수 발견: `Enemy.OnNetworkSpawn`이 `IsServer` 게이트 **뒤에서** `_monsterTimeController`를
  잡으므로 HitStop은 서버에서만 돈다 — 클라는 타격감 연출을 못 받고, 이 정지도 호스트 화면 한정이었다.
- **보스 HP bar가 다시 안 보인다** = `BossHudTarget`이 TwentyThree.prefab에서 **머지로 유실**됐다
  (theirs 채택). 원본 블록(fileID `9114957203948571100`, 루트 `TwentyThree`에 부착, 직렬화 필드 없음)을
  그대로 복원했다.
  ★**다음 머지 때 쓸 검사법**: 커밋 로그로 유실을 추정하면 틀린다. 프리팹별로
  `grep -o "Assembly-CSharp::[A-Za-z_0-9]*" | sort -u`를 머지 전/후로 `comm`하면 사라진 컴포넌트가
  바로 나온다(이번엔 TwentyThree에서 `BossHudTarget` 1건, Wells는 0건으로 확정됐다).
- **폭탄이 바닥으로 떨어지지 않는다** = `BombLauncher.groundMask`가 Wells.prefab에서 **Ground(3) 단독**
  이었다. 생성맵 바닥은 **Default(0)** 이라 레이캐스트가 빗나가고, 빗나가면 `target.y`를 그대로 둬서
  폭탄이 공중 지점에 착지한다. → `m_Bits: 8` → `9`(Default+Ground). 같은 계열인
  `BombController.ground`는 이미 9였다(런처만 저작 누락). 빗나갈 때 경고 로그도 추가했다.
  TwentyThree는 중첩 Wells 인스턴스의 컴포넌트를 참조(stripped)하고 오버라이드가 없어 이 값이 그대로 적용된다.

**2-a. 🔴 머지로 유실된 내 작업 — 다시 해야 함 (팀장 지시로 기록)**

프리팹 YAML은 수동 머지하지 않는다(GUID/fileID 깨짐 위험). Boss 쪽을 통째로 받고 아래를 재작업한다.

⚠️ **머지 후 실측하니 이 표의 예상이 대부분 틀렸다.** 아래가 검증된 결과다(2026-07-29).

| 대상 | 실제 결과 | 근거 |
|---|---|---|
| `Bomb.prefab` 아트 모델 교체 | ✅ **온전함 — 재작업 불필요.** 충돌 없이 auto-merge되어 양쪽이 합쳐졌다. 플레이스홀더 `Sphere`의 MeshRenderer는 비활성(`m_Enabled: 0`) 유지, 아트 인스턴스 `BombVisual`(scale 0.684) 존재. 부모 오프셋(28.0, −1.23, −6.94)이 fbx 내부 오프셋(−40.96, 1.80, 10.16)×0.684와 상쇄되어 폭탄 원점(≈0.15, 0, −0.03)에 놓인다 | `1b13d6e` |
| "TwentyThree 피격 가능+지면 인식" | ❌ **애초에 프리팹 작업이 아니었다.** `60f3862`의 TwentyThree.prefab diff는 **비어 있다** — 코드 작업(JumpController `groundMask`/`GroundProbe`)이고 그 코드는 병합에서 우리 것으로 살렸다. 현재 theirs 프리팹도 레이어14(EnemyHurtBox) 노드 + Hurtbox를 갖고 있다 | `60f3862` |
| `maxShield` 직렬화 제거 | 무해. 코드에 이미 없는 필드의 잔여 직렬화값이라 다음 재저장 때 사라진다 | `1271b85` |
| `BossScene.unity` | 재작업 아님 — 보스 테스트 씬은 민경 소유로 인계(팀장 확인). 우리 `8e0215b`(102줄 추가)는 버린다 | `8e0215b` |
| `Player.prefab` 오디오 2개 | 유실 아님(의도적 미채택) — `feature/Sound` 머지 때. 팀장 방침: 사운드 브랜치를 **은희 `feature/PlayerSkillAnimation`에 붙인 상태로** 받는다 | 위 2-b |

**🔴 남은 실제 후속 1건 — `Wells.prefab`**
- 머지 후에도 `NetworkObject`가 **없는데** `DefaultNetworkPrefabs.asset`에는 **등록돼 있다**(`6e2c783`, 민경 작업). 즉 "네트워크 프리팹으로 등록됐지만 NetworkObject가 없는" 무효 상태가 그대로다.
- 선택지 두 개이고 **민경 확인이 필요하다**: ① Wells를 네트워크 스폰할 것이면 루트에 `NetworkObject` 부착, ② 스폰 주체가 없으면(현재 `BossEncounterWiring`의 보스 프리팹은 `TwentyThree.prefab`이다) 등록을 제거. 둘 중 뭐든 하기 전에는 무효 항목 경고가 남는다.

- Boss 쪽도 같은 파일을 만졌다: `18befc0`·`89bc9e1`·`e61999d`·`9dbdf8c`·`9a2cad0`·`465a934`.
  → `Bomb.prefab`/`TwentyThree.prefab`은 **양쪽 커밋 충돌**이므로 "theirs" 채택 시 위 3건이 사라진다.
- `Wells.prefab`의 `NetworkObject`는 **HEAD에도 Boss에도 없다.** 그런데 Wells는 오래전부터
  `DefaultNetworkPrefabs.asset`에 등록돼 있다(`6e2c783`) → 지금 레포는 "네트워크 프리팹으로 등록됐지만
  `NetworkObject`가 없는" 무효 상태다(`TwentyThree.prefab`은 갖고 있다). 머지 후 인스펙터로 재부착하고
  별도 커밋할 것. 민경과 담당 경계 확인 필요.
- `CommonMeleeRobot.asset` dirty(내 로컬 1032줄)는 BT 리세이브 churn → `git checkout --`으로 폐기하고
  Boss 쪽 1828줄을 받는다. `0.BootStrapScene.unity` dirty는 **내용 차이 0**(개행 변환뿐), 팀장 지시로 보존.
- 🔴 **머지 전 필수**: `git status`로 `Assets/8.BehaviorTreeGraph/*` dirty 확인 → dirty면 `git checkout --`으로
  폐기. Unity가 리컴파일마다 재직렬화하므로 계속 되살아난다. 커밋하면 민경의 11k줄을 덮어쓴다.
- 🔴 **`git add -A` 금지** — 위 BT 에셋이 섞여 들어간다(이번 세션에 실제로 한 번 섞여 amend로 제거).

**3. 대시 — 쿨타임 결함 3건 수정 완료 (2026-07-29 후속, Play 검증 대기)**

팀장 판단("1회 제한이 아니라 쿨타임이 안 돈다")이 맞았다. `DashChargeLedger`의 회복 계산 자체는
정상이고(별도 콘솔 하네스로 실행 검증), 문제는 **쿨타임이 리셋/동결되는 경로**였다.

- **(1) 거부 시 예측 충전이 환불되지 않았다** — 오너는 입력 순간 소비로 `Revision`을 올리고, 거부한
  서버는 소비를 안 해 `Revision`이 더 낮다. 그래서 응답의 권한 충전값이 `SyncToAuthoritative`의
  과거-리비전 가드에 걸려 **조용히 버려졌다**. 결과: 거부 1회 = 대시 안 나가고 재충전 2초는 통째 손실.
  → `DashChargeLedger.ForceAdoptAuthoritative`(리비전 무시 채택) 추가, 거부 경로에서만 사용.
  잔여시간은 응답의 `nextChargeReadyServerTime`을 오너 도메인으로 환산해 이식한다.
- **(2) 충전 유무를 과거 스냅샷으로 판정했다** — `snapshot.ChargeCount`는 마지막 물리 tick 값이라
  회복 경계 직후에는 아직 0이다. 충전은 서버만 바꾸는 자원이라 지연보정할 이유가 없다(오탐만 생긴다).
  → `DashValidationPolicy.Validate`에 `authoritativeChargeCount` 파라미터 추가, 현재 서버 장부로 판정.
- **(3) 오프라인 Play에서 시계가 멈춰 충전이 영구히 회복되지 않았다** — `NetworkClock`은 세션이 안 돌면
  `LocalNow`/`ServerNow`를 **상수 0**으로 돌려준다. 그런데 `OwnerNow()`는 `Instance != null`만 보고
  폴백을 결정했다 → 프리팹은 씬에 있고 세션은 안 켠 상태(예: `PlayerDashTest` 단독 Play)에서
  `Advance(0.0)`만 반복 → **대시 딱 1회**. → `NetworkClock.IsRunning` 추가하고 그걸로 폴백 판단.
- 부수: 서버 충전 소비 시각을 RPC 도착시각 → **추정 입력시각**으로 옮겼다(오너/서버 회복 시점 정렬).

⚠️ **반증된 가설 (기록용)**: "원격 클라는 오너가 서버보다 먼저 회복해서 경계 입력이 구조적으로
NoCharge 거부된다"— 12초 시뮬레이션으로 반증됐다. 승인 응답의 `SyncToAuthoritative`가 오너 타이머를
**응답 도착 시점**으로 재시작하기 때문에 오너는 항상 서버보다 RTT만큼 **늦다**. 부작용으로 오너
체감 쿨타임 = `rechargeDuration + RTT`(100ms RTT면 2.1초)다. 지금은 안전한 방향이라 그대로 뒀다.

- 남은 검증: Play 1회. 성공 경로에도 로그를 넣었으므로
  `[Dash] 시작 — 남은충전 n/1, 재충전 2.00s, now=…, 시계=NetworkClock|Time.timeAsDouble` 한 줄이 뜬다.
  **`시계=Time.timeAsDouble`로 찍히면 (3)의 상황**이고, 간격이 2초보다 훨씬 길면 아직 다른 원인이 있다.
- EditMode 테스트: 정책 2건·장부 3건 추가(`DashValidationPolicyTests`·`DashChargeLedgerTests`).
  에디터가 열려 있어 Test Runner 배치 실행은 못 했고, 대신 순수 로직 파일을 `dotnet`으로 떼어
  같은 단정을 실제 실행해 전부 통과 확인했다. **Test Runner 실행은 아직 안 했다.**

**3-a. 보스룸 진입 경사(`Env_object_bossroomenter`) 콜라이더 — 완료 (2026-07-29)**

- 증상: 보스룸 진입 4방향 경사를 못 올라간다. 콜라이더가 없었다(lessons #29 재발).
- 원인 = **두 저작 경로가 모두 놓치는 사각지대**. 이 오브젝트는 존 프리팹 안의 **fbx 모델 프리팹
  인스턴스**다. ① fbx 임포터 `addColliders: 0`이라 모델 쪽에서 안 붙고, ② `MapColliderAuthoring`의
  `AddFloorWallColliders`는 `IsPartOfPrefabInstance`면 건너뛴다(원본 프리팹에서 1회 붙이는 전제인데,
  여기서 원본은 fbx라 컴포넌트를 붙일 수 없다). 이름 필터(`floor/wall/hallway/slope/stair`)에도
  `Env_object_*`는 소품으로 분류돼 안 걸린다.
- 조치: 새 메뉴 `Tools/Map/Authoring/Add MeshColliders to Walkable Model Instances`.
  허용목록(`bossroomenter`) − 제외목록(`_mv_`)으로만 동작한다. **엘리베이터
  `Env_object_MV_bossroomenter`는 이동 플랫폼이라 의도적으로 제외**(팀장 확인).
  fbx `.meta`는 SVN 관리라 임포터 설정을 못 건드리므로 git 쪽(존 프리팹)에 인스턴스 오버라이드로 붙였다.
- 형상 실측(면적 가중, 삼각형 법선): 위쪽 면이 **0~10도 7% / 20~30도 84% / 30~40도 8% → 전부 60도 이하.**
  완경사라 MeshCollider(비볼록)로 충분하고 계단식 램프 박스 대체는 불필요하다.
  ⚠️ bounds(rise/run)로 각도를 추정하면 헛값이 나온다(첫 시도 65.8도 — 중앙 구조물 높이를 경사로 착각).
  도구가 이제 각도 분포를 로그로 남긴다.
- ⚠️ **부수효과 주의**: 이 계열 도구는 `Assets/2.Prefabs/Map` 전체를 `LoadPrefabContents`로 순회하는데,
  그 과정에서 **중첩 프리팹 에셋의 루트 위치가 0으로 정규화**되는 일이 있다(이번에 `bossroom.prefab`의
  루트가 `(6.199, 0, 108.774)` → `(0,0,0)`으로 바뀌어 되돌렸다). MapScene 인스턴스는 위치 오버라이드를
  3축 다 갖고 있어 영향은 없었지만, **도구 실행 후 `git status`로 의도 외 프리팹 변경을 확인할 것.**

**4. 미착수 (오후 목표 잔여)**
- 벤트에서 증기 나옴
- 이동 플랫폼 바닥 `Env_MV_floor_typeA` 컨베이어 — 은희와 협의 후 추가

**완료된 것 (이번 세션 후반)**
- 다리 개통 F 상호작용 **동작 확인**(패널 4개 → 링 4개 → 다리 lerp 이동). 링 각도 버그 수정
  (원을 로컬 XZ에 그리고 또 90° 돌려 벽면이 됐던 것 → 로컬 XY로 그림).
- 다리 조각에 MeshCollider 자동 부착(없으면 NavMesh에 안 올라간다 — lessons #29).
- 열림 위치 저작 완료(팀장 확인: 의도 맞음). `Record Bridge CLOSED/OPEN Positions` 도구 2개.

### ▶ 이전 세션 시작점(참고용)

**증상: 보스룸으로 이동은 되는데 보스가 안 나온다 — 정상이다.** MapScene에는 보스를 스폰하는
주체가 없다. 보스를 스폰하는 `TwentyThreeArenaContext`(`OnNetworkSpawn`에서 `boss.Spawn()`)는
`BossScene`·`PlayerBossTest`에만 배치돼 있고, **MapScene의 `TwentyThree.prefab` 참조는 0건**이다.
그게 **Task 3 `BossEncounterDirector`**의 일이고 이번 범위는 "도착까지"였다.

1. **승인 계획서에 "달라진 전제" 반영** — 착수 첫 단계. 씬 경로(`MainFlow/4.MapScene`),
   플레이어 프리팹 1개, 연출 잠금 대상 확대(dash·fall·revive·soul), `PartyWipeWatcher` 오발 억제,
   카메라 우선순위(Float 뷰), 클리어 판정 연결, 담당 경계, DynamicsManager 재수정 불필요.
2. **Task 2~8** 순서대로. Task 3에서 보스 스폰 소유자를 Director 하나로 정리한다
   (`TwentyThreeArenaContext`는 민경 님 영역 — 중복 스폰이 실제로 생기면 그때 수정, 팀장 승인 받음).
3. 보스 격파 시 `SessionStatsTracker.Active.Capture(cleared: true)` 연결 → 결과 클리어 판정 완성.
4. **미구현 확인분**: dash HUD 위젯(충전 개수·재충전 게이지), 로딩바 보간,
   RMB 스킬(`FirstMeleeInterruptSkill` 구현체 자체가 없음), Result UI 서체·배치.
5. push 안 된 상태. 롤백 지점 = `backup/pre-dash-soul-merge`(`caaef90`).

### 확인만 하고 넘긴 것

- **로비 Ready**: 호스트는 자동 Ready + GameStart 버튼, 클라이언트만 Ready — **의도된 설계**.
- `ProjectSettings/DynamicsManager.asset`이 serializedVersion 13 → 23으로 포맷 마이그레이션됨
  (Unity가 저장 시 재작성). 신규 필드는 전부 기본값이지만 팀원 pull 시 통째로 바뀐 diff를 본다.
- 몬스터 루트 레이어가 제각각(ChompBot=19 이름없는 레이어, SpinnerBot·WallBot=0). 콜라이더는
  전부 Enemy(8)라 현재 증상은 없으나 레이어 마스크 로직에서 물릴 수 있다.

### 주의 — SVN meta

`50.Art/Char/Boss/bomb.fbx.meta`가 로컬 미버전 상태다. r233에서 fbx만 meta 없이 올라와
우리 Unity가 GUID를 새로 발급한 것 — **커밋하지 말 것**(r234와 같은 사고가 된다).
boss 담당(민경)이 자기 프로젝트에서 커밋해야 한다.

### 이전 세션 완료

1. **벽 투명화 per-pixel 재설계** — 오브젝트당 스칼라 불투명도(MPB) → 프래그먼트 월드좌표 기반
   셰이더 계산. 물리 쿼리 0, MPB 0, 약 1,600줄 → 473줄. Play 검증 통과.
   설계·검증·한계 = [Docs/tech/wall-occlusion-implementation.md](Docs/tech/wall-occlusion-implementation.md)
   - 삭제된 타입: `WallOcclusionUnit/Proxy/Manager/VisibilityContributor/Core/RuntimeBinder/ProjectBridge`
   - 현행 타입: `WallOcclusionDriver`(Assembly-CSharp) + `Globals`/`MaterialBinder`/`Settings`(Occlusion asmdef)
2. **맵 프리팹 콜라이더 복구** — 아트 교체로 12개 프리팹 콜라이더 0개였던 것 복구.
3. **`Assets/level` 아트팩 145개 재배치** — GUID 참조 그래프로 사용/미사용 판정 후 이전.
   `AssetDatabase.MoveAsset`만 사용, **GUID 145/145 보존, 미싱 레퍼런스 0**.
   - `50.Art/MapGen/MapObj/{mesh/level, material, texture}` ← FBX·머티리얼·셰이더그래프·텍스처 (**SVN**)
   - `2.Prefabs/Map/Props` ← 프롭 프리팹 38개 (git)
   - `99.Settings` ← `PP.asset`, `PP_Renderer.asset` (미참조 URP 파이프라인, 위치만 잡아둠)
   - 이름 충돌 3건은 `_level` 접미사로 개명 (`MA_prop03_level` 등 — 기존 50.Art와 이름만 같고 별개 에셋)
4. **오클루전 머티리얼 매핑 5쌍 → 14쌍** — level 폴더 머티리얼 9종이 매핑에서 빠져 프롭들이
   디더 셰이더를 못 달고 있던 문제 수정.

### 다음 작업 (이어서)

1. **SVN 최신화** — `50.Art/MapGen/MapObj` 아래 신규 파일들을 SVN에 add/commit. git에는 안 보인다
   (`.gitignore:83`이 `50.Art/` 제외, `Assets/50.Art.meta` 1개만 추적).
2. **머지** — SVN 상태 맞춘 뒤 진행.

### 주의

- **`8.BehaviorTreeGraph/**` 는 다른 담당자 작업물이다. 수정·커밋하지 말 것.** (현재 워킹트리에
  수정 상태로 있으나 의도적으로 커밋에서 제외했다.)
- 미커밋으로 남긴 것: `TitleScene`·`ProjectSettings`(줄바꿈만 변경, 내용 0), `all_mesh.unity`,
  `FogProfile.asset`, BT 에셋 2종.
- 아트(FBX·텍스처·머티리얼)는 SVN, 코드·씬·프리팹은 git. 이 경계를 넘기지 말 것.

## 이전 인수인계 (2026-07-21 → Codex)

작업 세션: **경석(Claude)** — MapScene 몬스터/보스입장 통합 완료(컴파일 0, 1차 플레이 검증). 다음 작업자 = Codex.

- **상세 현황·남은작업·조사항목 = [Docs/tech/map-monster-boss-handoff.md](Docs/tech/map-monster-boss-handoff.md)** (이 세션 산출물 전체 + 우선순위 목록).
- 계획 잠금: `PLAN.md` §"MapScene 몬스터 통합" + §6(보스 입장).
- 최근 수정 파일(동시수정 주의): `1.Scripts/{Map/*, Monster/MonsterBase.cs, Player/PlayerMovement.cs·PlayerAimIndicator.cs, Unit/Weapon/BaseAttack.cs, Player/Skill/FirstMeleeMainSkill.cs}`, `MapScene.unity`, 존 프리팹 12개.
- **아직 push/커밋 안 됨.** git + SV( 50.Art meta·MapGenConfig) 분리 커밋 예정 — 핸드오프 문서 §4.
- 즉시 다음 후보: 패드 y 가림 조치 / 멀티(MPPM) 텔레포트 검증 / 터렛 스폰 재확인 / **MortarBot 복귀 후 간헐 Idle 회귀 조사**(핸드오프 §3).

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
