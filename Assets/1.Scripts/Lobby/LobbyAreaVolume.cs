using UnityEngine;

/// <summary>
/// 로비 영역 볼륨(할 일 157 커밋 ③) — 비네트워크 씬 오브젝트. BoxCollider(트리거) = 바닥 footprint + 높이 범위(박스 y 크기).
/// 판정은 트리거 Enter/Exit 가 아니라 서버 진행기(<see cref="LobbyTrainingDirector"/>)가 폴링한다 — <see cref="PartyGatherZone"/> 와 같은 규칙:
/// 플레이어 본체 기준점(transform.position)이 박스(회전 포함) 안이고 쓰러짐·사망이 아니면 안(<see cref="PartyGatherZone.IsIncapacitated"/>).
/// 박스 높이 밖(다른 층·위 통로)은 안이 아니다. 지금 쓰는 곳 = L00 라커룸 내부(<c>Area_Locker</c>) — 같은 볼륨이 캐릭터 선택 영역이기도 하다
/// (<see cref="LobbyInteractable"/> 의 area 칸, 할 일 161: 프롬프트·F·서버 재검증·안내 캐릭터 스텝이 이 박스 하나로 판정).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class LobbyAreaVolume : MonoBehaviour
{
    [Tooltip("로그·158 표식용 영역 키(예 AREA_LOCKER).")]
    [SerializeField] private string areaId = "AREA_LOCKER";

    private BoxCollider _box;

    public string AreaId => areaId;

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        // 위치 검사만 한다 — 막는 벽이 되지 않게 트리거로 둔다.
        _box.isTrigger = true;
    }

    private void Reset()
    {
        var box = GetComponent<BoxCollider>();
        if (box != null)
            box.isTrigger = true;
    }

    /// <summary>월드 좌표가 박스(회전·높이 범위 포함) 안인가.</summary>
    public bool ContainsPoint(Vector3 worldPosition) => ContainsPoint(worldPosition, 0f);

    /// <summary>
    /// 월드 좌표가 박스 안인가 — 바닥 footprint(로컬 x·z)만 <paramref name="horizontalMargin"/>(월드 m)만큼 넓혀 본다. 높이 범위는 넓히지 않는다.
    /// 서버 재검증 여유용(클라가 경계에서 누른 순간 서버 위치가 조금 뒤처진 몫 — <see cref="LobbyInteractable.ServerReachTolerance"/>).
    /// </summary>
    public bool ContainsPoint(Vector3 worldPosition, float horizontalMargin)
    {
        var box = _box != null ? _box : GetComponent<BoxCollider>();
        if (box == null)
            return false;

        var scale = box.transform.lossyScale;
        return ContainsLocal(box.transform.InverseTransformPoint(worldPosition), box.center, box.size,
            LocalMargin(horizontalMargin, scale.x), LocalMargin(horizontalMargin, scale.z));
    }

    /// <summary>본체 기준점이 안이고 쓰러지지 않았으면 영역 안으로 센다.</summary>
    public bool Counts(Vector3 bodyPosition, bool incapacitated) => !incapacitated && ContainsPoint(bodyPosition);

    /// <summary>[서버] 이 플레이어를 영역 안으로 세는가(본체 기준점·쓰러짐 규칙 = 집결 구역과 같음).</summary>
    public bool CountsPlayer(Player player) => player != null && Counts(player.transform.position, PartyGatherZone.IsIncapacitated(player));

    /// <summary>박스 로컬 좌표가 중심·크기 안인가(경계 포함).</summary>
    public static bool ContainsLocal(Vector3 local, Vector3 center, Vector3 size) => ContainsLocal(local, center, size, 0f, 0f);

    /// <summary>박스 로컬 좌표가 중심·크기 안인가 — x·z 절반 크기에 로컬 여유를 더한다(경계 포함, 높이는 그대로).</summary>
    public static bool ContainsLocal(Vector3 local, Vector3 center, Vector3 size, float marginX, float marginZ)
    {
        var d = local - center;
        var half = size * 0.5f;
        return Mathf.Abs(d.x) <= half.x + Mathf.Max(0f, marginX)
            && Mathf.Abs(d.y) <= half.y
            && Mathf.Abs(d.z) <= half.z + Mathf.Max(0f, marginZ);
    }

    // 월드 m 여유 → 박스 로컬 단위(스케일 0 이면 여유 없음).
    private static float LocalMargin(float worldMargin, float axisScale)
    {
        var s = Mathf.Abs(axisScale);
        return worldMargin > 0f && s > 1e-5f ? worldMargin / s : 0f;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null)
            return;

        Gizmos.matrix = box.transform.localToWorldMatrix;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.15f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
#endif
}
