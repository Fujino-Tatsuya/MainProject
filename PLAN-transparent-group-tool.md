# PLAN — 투명화 그룹 편집 툴 (Scene / Prefab 에디터 툴)

> 작업 세션: **은희(Claude)**, 브랜치 `feature/TransparentSettingTool` (base `development` `31170603`),
> 워크트리 `C:\UnityProject\MainProject-Worktree`.
> 설계는 2026-09-22 grill 로 확정했다(Q1~Q32). **승인 전 — 구현 미착수.**
>
> 선행 작업: [PLAN.md](PLAN.md) 최상단 「구역 진입 기반 벽 투명화 1단계」(검증 완료).
> 이 툴은 거기 「남은 것」의 **"존 프리팹 오서링(구역 볼륨 + 그룹 리스트)"** 을 사람이 하기 쉽게 만드는 보조 도구다.

## 1. 목표

**벽 투명화 세팅 작업을 할 때, 오브젝트 묶음을 이름 붙여 저장해 두고 씬 뷰에서 색으로 구분한다.**

한 줄로: **"이름 붙은 영속 선택(selection) + 색 표시"**. 그 이상은 하지 않는다.

- 값을 편집하는 UI는 **만들지 않는다.** 그룹을 `Selection` 에 통째로 넣으면
  Unity 기본 다중 오브젝트 인스펙터가 공통 컴포넌트를 묶어 편집해 준다. 그걸 그대로 쓴다.
- 그룹은 **컴포넌트 타입과 무관한 `GameObject` 배열**이다. `WallTransparencyGroup` 과 1:1 대응이 아니다.
- 런타임 코드 0줄. 전부 에디터 전용이다.

### 왜 기본 기능으로 안 되는가

Unity 다중 선택 편집은 이미 `private [SerializeField]` 필드까지 잘 동작한다. 부족한 건 편집이 아니라 **선택** 이다:
씬 뷰에서 아무거나 한 번 클릭하면 선택이 날아가고, 벽 수십 개를 다시 골라야 한다.
이 툴은 그 선택을 **이름 붙여 저장하고 한 번에 복원** 한다.

## 2. 확정 결정

| # | 결정 | 근거 |
|---|---|---|
| 1 | 용도 = **투명화 세팅 전용** 보조 툴 | 범용 그룹 편집기는 Unity 기본과 겹쳐 안 쓰이게 된다 |
| 2 | 그룹 = **`GameObject` 배열** (명명된 고정 선택) | 요구사항의 "배열에 담는다" 그대로. 타입 제약 없음 |
| 3 | 값 편집 UI **자체 제작 안 함** — `Selection` 주입 | 기본 다중 편집이 이미 완전하다. 재구현은 순손해 |
| 4 | Scene + **Prefab Stage 둘 다** 지원, 컨텍스트별 **완전 분리** | 벽이 `Assets/2.Prefabs/Environment/Layouts/Zones/Zone*.prefab` 안에 있다 |
| 5 | 저장 = **JSON**, `Assets/` **밖**, **git 추적** | `.meta`·리임포트·씬 dirty 를 전부 회피. 텍스트라 충돌 나도 손으로 풀린다 |
| 6 | JSON 파일 = **씬/프리팹마다 1개**, 이름 `<에셋이름>.<GUID앞8자>.json` | 동시 작업 충돌 면적 0. 이름을 바꿔도 GUID 로 찾아 파일명을 정정 |
| 7 | 저장 트리거 = **변경 즉시 자동 저장** (Ctrl+S 훅 아님) | 씬이 dirty 하지 않으면 `sceneSaved` 가 안 떠 작업이 조용히 날아간다 |
| 8 | 되돌리기 = **메모리 `ScriptableObject` + Unity Undo 스택** | Undo 는 `UnityEngine.Object` 에만 걸린다. 씬 편집과 같은 Ctrl+Z 로 섞인다 |
| 9·10 | 멤버 키 = **`GlobalObjectId` + Transform 경로 폴백. 씬·프리팹 공통** | 🔴 **2026-09-28 R7 프로브로 통합.** 원래는 프리팹만 `(에셋 GUID, fileID)` 로 따로 가려 했으나(Q23=C), 프리팹 편집 모드에서도 `GlobalObjectId` 가 임시 씬이 아니라 **프리팹 에셋 GUID** 를 참조하고(`identifierType=2`), 문자열 왕복 후 같은 오브젝트로 복원되는 것을 실측했다. 중첩 프리팹 인스턴스도 `targetPrefabId` 로 구분된다. 키가 하나면 분기도 하나다 |
| 11 | 참조 유실은 **드롭하지 않고 표시** + 정리 버튼 | 조용히 사라지면 디버깅이 불가능하다 |
| 12 | 한 오브젝트의 **다중 그룹 소속 허용** | 벽이 두 구역 경계에 걸치는 건 흔하다 |
| 13 | 색 = **48색 고정 팔레트 자동 배정 + 수정 가능** | NGO `CategoricalColorPalette` 와 같은 방식(색맹 친화, 인접 대비 최대화) |
| 14 | 시각화 = **`ObjectIdRequest` + 풀스크린 blit** | 원본 머티리얼·MPB 무오염. URP Renderer 에셋도 안 건드린다 |
| 15 | 색 합성 = **반투명 블렌드**(형상 보임) + 경계 아웃라인 | 투명화 세팅은 벽 형상을 보며 하는 작업이다. NetVis 의 100% 치환과 다름 |
| 16 | 배경 탈색 = **슬라이더, 기본 0.5** | 완전 흑백이면 머티리얼 톤 확인이 막힌다 |
| 17 | 시각 계층 **3단**: 활성 그룹(또렷) / 비활성 그룹(블렌드 낮춤) / 배경(탈색) | "활성" = 툴 창에서 고른 그룹. `Selection` 연동 아님(화면이 요동친다) |
| 18 | **열린 씬 뷰 전부**에 적용 | NetVis 는 `sceneViews[0]` 만 한다. 비용은 씬 뷰 수만큼 는다 |
| 19 | 멤버의 **자식 Renderer 전부 포함** | 벽 프리팹은 부모 아래 메시가 여러 개다 |
| 20 | Renderer 없는 멤버 **허용**, 목록에 "표시 없음" 아이콘 | 존 볼륨을 같이 묶고 싶을 수 있다 |
| 21 | **플레이 모드 진입 시 시각화 자동 OFF**, 나오면 복구 | 플레이 중엔 실제 투명화 결과를 봐야 한다 |
| 22 | UI = **IMGUI `EditorWindow`**, 메뉴 `Tools/Group Painter` | 레포 관행(`ZoneRotationAuthoringWindow`, `FogPainterWindow`) |

## 3. 데이터 모델

메모리 컨테이너는 `ScriptableObject`(`CreateInstance` + `hideFlags = DontSave`) — **에셋으로 저장하지 않는다.**
Undo 를 걸기 위한 껍데기이고, 진짜 저장소는 JSON 이다.

```
GroupPainter/                           ← 프로젝트 루트, Assets/ 밖, git 추적
  4.MapScene.a1b2c3d4.json
  ZoneL_typeA.9f8e7d6c.json
```

```jsonc
{
  "version": 1,
  "contextKind": "Scene",            // "Scene" | "Prefab"
  "contextGuid": "a1b2c3d4…",        // 씬/프리팹 에셋 GUID (파일명 정정용)
  "contextName": "4.MapScene",
  "groups": [
    {
      "id": "…",                     // GUID, 이름 변경과 무관한 안정 키
      "name": "복도 서측 벽",
      "color": [0.0, 0.706, 0.031, 1.0],
      "colorIsCustom": false,        // false = 팔레트 자동 배정
      "members": [
        {
          "globalObjectId": "GlobalObjectId_V1-2-…",  // 1차 키. 씬·프리팹 공통
          "path": "Zone Layout/wall_basic_035",        // 폴백
          "lastSeenName": "wall_basic_035"             // 유실 표시용. 해석에는 쓰지 않는다
        }
      ]
    }
  ]
}
```

**해석 순서**: 1차 키(`globalObjectId` 또는 `fileId`) → 실패 시 `path` → 둘 다 실패하면 **유실**로 표시하고 항목은 유지.

⚠️ 아직 저장 안 된 새 씬 오브젝트는 `GlobalObjectId` 가 0 이다. 그룹에 담으려면 씬을 먼저 저장해야 하고, 창에서 그 이유를 안내한다.

## 4. 파일 구성

> 🔴 **2026-09-22 변경.** 처음에는 `Assets/1.Scripts/Rendering/Editor/`(= `Assembly-CSharp-Editor`)에 두기로 했으나,
> **asmdef 테스트 어셈블리는 predefined 어셈블리(`Assembly-CSharp-Editor`)를 참조할 수 없다.** 그 배치로는 §7 의
> EditMode 테스트가 불가능하다. 그렇다고 `Rendering/Editor/` 에 asmdef 를 놓으면 거기 있던
> `WallOcclusionAuthoring.cs`·`WallOcclusionTestRunner.cs` 가 같이 끌려나와 남의 코드가 깨진다.
> 그래서 **툴 전용 폴더 + Editor 전용 asmdef** 로 간다. 레포 최초의 Editor asmdef 다.
> 되돌리려면 asmdef 만 지우면 원래 배치가 된다(테스트만 잃는다).

`Assets/1.Scripts/Rendering/Occlusion/Editor/` + `VeyTrace.Rendering.Occlusion.Editor.asmdef`
(`includePlatforms: [Editor]`, `references: [VeyTrace.Rendering.Occlusion]`).

| 파일 | 역할 | 단계 |
|---|---|---|
| `TransparentGroupData.cs` | 직렬화 DTO + **에디터에 의존하지 않는 순수 로직**(파일명 규칙, 폴백 선택) | S1 ✅ |
| `TransparentGroupStore.cs` | JSON 읽기/쓰기, 파일명 정정, 컨텍스트 판별(Scene/Prefab Stage) | S1 ✅ |
| `TransparentGroupResolver.cs` | 멤버 키 ↔ `GameObject`, 경로 폴백, 유실 판정 | S1 ✅ |
| `TransparentGroupPalette.cs` | 색 자동 배정. NetVis 의 48색 배열을 옮기지 않고 황금비 색상환으로 같은 성질을 만든다 | S1 ✅ |
| `TransparentGroupSet.cs` | 메모리 `ScriptableObject` — Undo 대상 + 변경 즉시 JSON 저장 | S1 ✅ |
| `TransparentGroupWindow.cs` | IMGUI `EditorWindow` — 목록·색·조작·토글·슬라이더 | S2 |
| `TransparentGroupVisualizer.cs` | `ObjectIdRequest` + blit, 씬 뷰별 수명 관리 | S3 |
| `Shaders/TransparentGroupOverlay.shader` | `Hidden/Rendering/TransparentGroupOverlay` (S0 것을 옮겨 온다) | S3 |
| `Assets/Tests/EditMode/Occlusion/TransparentGroupStoreTests.cs` | JSON 왕복 + 폴백/유실 + 팔레트 | S1 ✅ |

## 5. 구현 단계

**S0 — 스파이크 ✅ 2026-09-22 통과**
에디트 모드 씬 뷰에서 `Camera.SubmitRenderRequest(ObjectIdRequest)` → blit 으로 오브젝트를 칠하는 데 성공했다.
검증 로그(`Editor.log`): `씬뷰 카메라 'SceneCamera' 1660x566, ObjectIdRequest.result=있음` / `매칭=1개`.
파일: `Assets/1.Scripts/Rendering/Editor/TransparentGroupSpike.cs`, `.../Shaders/TransparentGroupOverlay.shader` (S3 에서 대체·삭제).

확정된 사실 3가지:

| 확인한 것 | 결과 |
|---|---|
| `ObjectIdRequest` 가 **에디트 모드**에서 동작하는가 (R1) | ✅ **동작한다.** NetVis 의 플레이 모드 전용 제약은 오너십 데이터 때문이지 기법의 한계가 아니다 |
| **Render Graph 켜짐**(`m_EnableRenderCompatibilityMode: 0`)에서 `cmd.Blit(BuiltinRenderTextureType.CameraTarget)` 이 먹히는가 | ✅ **먹힌다.** 별도 대응 불필요 |
| 🔴 `idToObjectMapping` 에 담기는 객체의 정체 | **Renderer 컴포넌트다. GameObject 가 아니다.** `renderer.GetInstanceID()` 로 비교해야 하고, `renderer.gameObject.GetInstanceID()` 는 영원히 0개 매칭된다 (S0 에서 실제로 밟은 함정) |

**S1 — 데이터** `TransparentGroupSet` / `Store` / `Resolver` + EditMode 테스트. UI 없음.

**S1 — 데이터 ✅ 2026-09-22** `TransparentGroupData/Store/Resolver/Palette/Set` + EditMode 14개 통과.

**S2 — 창 ✅ 2026-09-23** `TransparentGroupWindow` + `TransparentGroupSession`.

> 계획에 없던 `TransparentGroupSession` 을 하나 더 뒀다. 창과 시각화가 같은 상태
> (열린 컨텍스트 / 선택된 그룹 / 슬라이더)를 봐야 하는데, 그걸 창 안에 두면 **시각화가 창에 묶여
> 창을 닫으면 그려지지 않는다.** 공유하는 것만 정적 세션으로 빼서 창은 UI 만, 시각화는 그리기만 한다.

**S3 — 시각화 ✅ 2026-09-23** `TransparentGroupVisualizer`. S0 스파이크는 역할을 마쳐 삭제했다.
셰이더는 `Assets/1.Scripts/Rendering/Editor/Shaders/` 에 그대로 둔다 — `Shader.Find` 는 경로와 무관하고,
Unity 가 켜진 채 에셋을 옮기는 것은 CLAUDE.md 6번의 위험군이다. **Unity 를 닫을 일이 생기면 그때 옮긴다.**

> `CommandBufferPool` 은 SRP Core 패키지에 있어 asmdef 의존을 늘려야 한다. 그것 하나 때문에
> 패키지를 끌어오지 않고 카메라별 `CommandBuffer` 하나를 재사용한다.

**S4 — 프리팹 모드 ✅ 2026-09-28** 별도 키 체계가 필요 없어져(위 결정 9·10) 새로 만들 것이 거의 없었다.
`PrefabStage` 진입/이탈 훅과 컨텍스트별 파일 분리는 S1·S2 에서 이미 들어가 있었고, 여기서는
`fileId` 필드와 프리팹 전용 분기를 **걷어냈다**. 프로브 파일도 삭제했다.

**S5 — 수동 검증 ✅ 2026-09-28** §7 완료 조건 8/8 통과.

**S6 — 선택 모드(페인트) 2026-09-28 추가 요청** `TransparentGroupPaintTool.cs`.
씬 뷰 클릭·드래그가 오브젝트 선택 대신 **선택된 그룹에 담기**로 바뀐다.

| # | 결정 | 근거 |
|---|---|---|
| 23 | 그룹이 **정확히 하나** 선택됐을 때만 켜진다 | 창은 그룹 다중 선택을 허용한다(결정 12·17). 둘 이상이면 "어디로 들어갔는지" 를 화면에서 알 수 없다. 버튼을 비활성화하고 이유를 툴팁에 쓴다 |
| 24 | **Ctrl + 드래그 = 제거** | 페인트 툴의 통상 관례. 잘못 칠했을 때 모드를 끄지 않고 고친다. 🔴 **Alt 는 쓰지 않는다** — 씬 뷰 카메라 오비트라 빼앗으면 화면을 못 돌린다 |
| 25 | 피킹 = `HandleUtility.PickGameObject(selectPrefabRoot: false)` | 콜라이더가 없어도 렌더러 기준으로 잡힌다 — 벽에 콜라이더가 없는 경우가 있어 `Physics.Raycast` 는 못 쓴다. `selectPrefabRoot: true` 면 씬에서 존 프리팹 루트가 통째로 잡혀 벽 하나를 고를 수 없다 |
| 26 | 드래그 **경로를 보간**한다(6px 간격) | 마우스 이동 이벤트는 픽셀을 건너뛴다. 빠르게 그으면 중간 오브젝트가 통째로 빠진다 |
| 27 | **드래그 한 번 = Undo 한 번** (`IncrementCurrentGroup` → `CollapseUndoOperations`) | 안 묶으면 오브젝트 수만큼 Undo 기록이 쌓여 Ctrl+Z 를 수십 번 눌러야 한다 |
| 28 | 저장은 **마우스 이벤트당 한 번** | 오브젝트마다 `AddMembers` 를 부르면 그만큼 JSON 을 다시 쓴다. 보간된 지점들을 모아 한 번에 넘긴다 |
| 29 | `HandleUtility.AddDefaultControl` 로 기본 클릭 선택·이동 기즈모를 가져온다. 카메라 조작은 건드리지 않는다 | 클릭이 양쪽으로 가면 칠하면서 선택이 바뀐다 |
| 30 | 마우스 아래 오브젝트를 `Handles.DrawOutline` 로 하이라이트 + 좌하단에 대상 그룹 HUD | 무엇이 잡힐지, 어디로 들어가는지가 보여야 한다. 제거 모드면 빨강 |
| 31 | 이름은 **Group Painter** (2026-09-28 변경). 메뉴 `Tools/Group Painter`, 저장 폴더 `GroupPainter/` | 처음 이름은 `Transparency Groups` / `Tools/Rendering/Transparency/Group Tool` 이었으나 **구현이 투명화와 아무 관계가 없다** — 그룹은 GameObject 배열이고 컴포넌트 타입을 가리지 않는다. 투명화는 첫 사용처일 뿐이라 이름이 용도를 좁게 오해시켰다. 🔴 **내부 클래스·파일·네임스페이스(`TransparentGroup*`, `VeyTrace.Rendering.Occlusion.Editor`)는 일부러 그대로 뒀다** — 갓 커밋한 것을 통째로 옮기면 git 히스토리만 지저분해지고 asmdef guid 도 새로 생긴다. 이름이 어긋나 보이면 이 줄이 이유다 |

## 6. 리스크

| # | 리스크 | 영향 | 대응 |
|---|---|---|---|
| ~~R1~~ | ~~`ObjectIdRequest` 가 에디트 모드에서 되는지 미검증~~ | — | ✅ **2026-09-22 S0 에서 해소.** 동작 확인. 대안(`PlayerSilhouetteFeature` 확장)은 불필요해졌다 |
| ~~R7~~ | ~~프리팹 편집 모드의 fileID 를 얻을 수 있는지 미검증~~ | — | ✅ **2026-09-28 프로브로 해소, 그리고 질문 자체가 불필요해졌다.** fileID 가 아니라 `GlobalObjectId` 로 통일했다(결정 9·10). ⚠️ 프로브에서 함께 드러난 함정: `PrefabUtility.GetCorrespondingObjectFromSource` 의 fileID 는 **원본 프리팹**의 오브젝트를 가리켜, 같은 프리팹을 여러 번 배치한 존에서는 전부 같은 값이 된다. 멤버 키로 쓰면 안 된다 |
| R2 | **Prefab Stage 선례 없음.** NetVis 에 `PrefabStage` 참조 0건 | S4 지연 | S4 를 마지막에 두어 씬만으로도 쓸 수 있게 한다 |
| R3 | 씬 뷰마다 ObjectId 패스가 1회씩 추가 | 씬 뷰 3개면 비용 3배 | 표시 토글로 끌 수 있다. 실측 후 문제되면 결정 18 을 `sceneViews[0]` 로 되돌린다 |
| R4 | 미저장 오브젝트의 `GlobalObjectId` = 0 | 그룹에 못 담김 | 창에서 경고 + "씬 저장" 버튼 |
| R5 | git 추적 JSON 이 자동 저장으로 자주 바뀜 | `git status` 가 계속 지저분 | 텍스트라 diff 가 읽힌다. 정 거슬리면 `.gitignore` 한 줄로 결정 5 를 로컬 전용으로 되돌릴 수 있다 |
| R6 | 투명화 디더가 이미 적용된 벽 위에 오버레이가 겹침 | 색이 예상과 다를 수 있음 | 시각화는 최종 화면 blit 이라 기능 간섭은 없다. 보기 문제면 탈색/블렌드 슬라이더로 조정 |

## 7. 완료 조건

1. `TransparentV3.unity` 에서 벽 여러 개 선택 → 그룹 생성 → 씬 뷰에 그룹 색이 보인다
2. 그룹 클릭 → 인스펙터에 공통 컴포넌트가 뜨고, 값 수정이 전원에 반영된다
3. Unity 재시작 후에도 그룹이 그대로 복원된다 (JSON 왕복)
4. 멤버 오브젝트 하나를 삭제 → 목록에 "유실 1개"로 표시된다
5. `ZoneL_typeA.prefab` 프리팹 모드에서 그룹 생성 → 닫았다 다시 열면 복원된다
6. Ctrl+Z 로 그룹 조작이 되돌아간다
7. 플레이 모드 진입 시 시각화가 꺼지고, 나오면 돌아온다
8. 배경 탈색 슬라이더와 표시 토글이 동작한다

**자동 테스트는 JSON 직렬화/역직렬화와 참조 폴백 로직에만** 붙인다. 렌더링은 수동 확인.

## 8. 범위 밖 (이번에 하지 않는다)

- **`WallTransparencyGroup`/`Zone` 자동 배선** — 선택한 렌더러로 `targetRenderers` 를 채워 주는 기능
- **레이어 기반 자동 렌더러 수집** — [PLAN.md](PLAN.md) 결정 2: 199개 중 193개가 layer 0 이라 불가능(실측 기록)
- **범용 컴포넌트 일괄 편집 UI** — Unity 기본으로 충분
- **머티리얼 tint 방식 시각화** — `_WallOcclusionOpacity` 와 충돌
- **그룹 공유/머지 전용 도구** — JSON 을 손으로 고치면 된다

**범위 안이지만 비용이 있는 것**: 열린 씬 뷰 전부 지원(결정 18, R3).
