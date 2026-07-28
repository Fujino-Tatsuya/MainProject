using System.Collections.Generic;
using UnityEngine;
using VeyTrace.Rendering.Occlusion;

// 벽 투명화 진입점. 하는 일은 두 가지뿐이다.
//   1) 맵이 생성되면 벽 머티리얼을 오클루전 변종으로 한 번 교체한다.
//   2) 매 LateUpdate에 카메라/플레이어 월드 위치를 셰이더 전역 유니폼으로 넘긴다.
//
// 불투명도 계산, 벽 선별, 페이드 타이밍은 전부 셰이더가 프래그먼트 단위로 한다.
// 물리 쿼리와 MaterialPropertyBlock은 쓰지 않는다.
//
// Assembly-CSharp에 두는 이유는 CameraTargetSwitcher를 참조하기 때문이다.
// (VeyTrace.Rendering.Occlusion 어셈블리는 프로젝트 타입을 참조하지 않는다.)
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // CinemachineBrain(기본 0) 이후에 확정된 카메라 위치를 읽는다.
public sealed class WallOcclusionDriver : MonoBehaviour
{
    private const string StaticStageRootName = "Stage1";

    [SerializeField] private WallOcclusionSettings settings;

    private bool isActive;
    private bool loggedInactiveReason;

    public WallOcclusionSettings Settings => settings;

    public void SetSettings(WallOcclusionSettings newSettings)
    {
        settings = newSettings;
    }

    private void OnEnable()
    {
        MapGenerator.OnGenerated += HandleMapGenerated;

        // 절차 생성 없이 이미 배치된 정적 스테이지(Stage1 등)도 여기서 잡힌다.
        // 예전 구조는 OnGenerated에서만 바인딩해서 정적 씬이 영원히 누락됐다.
        Rebind();
    }

    private void OnDisable()
    {
        MapGenerator.OnGenerated -= HandleMapGenerated;
        WallOcclusionGlobals.Disable();
        isActive = false;
        loggedInactiveReason = false;
    }

    private void LateUpdate()
    {
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        Camera gameplayCamera = switcher != null ? switcher.GameplayCamera : null;
        Transform followTarget = switcher != null ? switcher.CurrentFollowTarget : null;

        if (settings == null ||
            gameplayCamera == null ||
            !gameplayCamera.isActiveAndEnabled ||
            followTarget == null)
        {
            Deactivate(gameplayCamera, followTarget);
            return;
        }

        WallOcclusionGlobals.Apply(
            settings,
            gameplayCamera.transform.position,
            followTarget.position);
        Activate(gameplayCamera, followTarget);
    }

    private void HandleMapGenerated(MapGenerator _)
    {
        // OnGenerated는 생성물 배치가 끝난 뒤에 발생하므로 렌더러가 이미 존재한다.
        // 물리 동기화를 기다릴 이유가 없어졌으므로 다음 프레임까지 미루지 않는다.
        Rebind();
    }

    // 머티리얼 교체를 다시 수행한다. 멱등이므로 몇 번 불러도 안전하다.
    public void Rebind()
    {
        if (settings == null)
        {
            Debug.LogWarning(
                "[WallOcclusion] Settings asset이 비어 있어 머티리얼 바인딩을 건너뛴다.",
                this);
            return;
        }

        if (!settings.HasValidMaterialMappings)
        {
            Debug.LogWarning(
                "[WallOcclusion] 머티리얼 매핑이 비었다. " +
                "Tools > Rendering > Wall Occlusion > Apply All 을 먼저 실행할 것.",
                this);
            return;
        }

        List<Transform> roots = CollectRoots();
        if (roots.Count == 0)
        {
            Debug.LogWarning(
                $"[WallOcclusion] 바인딩 루트를 찾지 못했다. " +
                $"('{MapContentSpawner.RootName}', '{StaticStageRootName}')",
                this);
            return;
        }

        WallOcclusionBindReport report =
            WallOcclusionMaterialBinder.Bind(settings, roots);

        Debug.Log(
            $"[WallOcclusion] 바인딩 완료 — roots={roots.Count}, " +
            $"renderers={report.InspectedRenderers}, " +
            $"boundSlots={report.BoundSlots} (신규 {report.SwappedSlots} / 기존 {report.AlreadyBoundSlots}), " +
            $"unmappedMaterials={report.UnmappedMaterialNames.Count} " +
            $"[{WallOcclusionMaterialBinder.DescribeUnmapped(report.UnmappedMaterialNames)}]",
            this);

        if (report.BoundSlots == 0)
        {
            Debug.LogWarning(
                "[WallOcclusion] 교체된 머티리얼 슬롯이 하나도 없다. " +
                "설정의 sourceMaterials가 실제 맵 머티리얼과 일치하는지 확인할 것.",
                this);
        }
    }

    private List<Transform> CollectRoots()
    {
        var roots = new List<Transform>(2);
        AddRoot(roots, MapContentSpawner.RootName);
        AddRoot(roots, StaticStageRootName);
        return roots;
    }

    private static void AddRoot(List<Transform> roots, string rootName)
    {
        GameObject root = GameObject.Find(rootName);
        if (root != null)
            roots.Add(root.transform);
    }

    private void Activate(Camera gameplayCamera, Transform followTarget)
    {
        if (isActive)
            return;

        isActive = true;
        loggedInactiveReason = false;
        Debug.Log(
            $"[WallOcclusion] 활성 — camera='{gameplayCamera.name}' " +
            $"scene='{gameplayCamera.gameObject.scene.name}', " +
            $"target='{followTarget.name}'.",
            this);
    }

    private void Deactivate(Camera gameplayCamera, Transform followTarget)
    {
        WallOcclusionGlobals.Disable();
        isActive = false;

        if (loggedInactiveReason)
            return;

        loggedInactiveReason = true;
        Debug.Log(
            $"[WallOcclusion] 대기 — settings={(settings != null)}, " +
            $"camera={(gameplayCamera != null)}, target={(followTarget != null)}. " +
            "카메라와 오너 플레이어가 준비되면 자동으로 켜진다.",
            this);
    }
}
