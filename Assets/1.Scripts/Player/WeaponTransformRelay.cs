using UnityEngine;

/// <summary>
/// PlayerWeaponSlot에 붙는 단일 컴포넌트. 각 쌍(target, weapon)에 대해
/// weapon이 target(손 소켓 본)의 월드 위치·회전을 매 프레임 그대로 따라가게 한다.
/// 무기를 본 계층에 부모로 박지 않고 Player 바로 아래 WeaponSlot에 두면서도
/// 손을 따라가게 하려는 용도. 애니메이션 평가가 끝난 뒤 반영하려고 LateUpdate에서 갱신한다.
/// target은 스켈레톤 안(예: R_/L_WeaponSocket 본)에 있어야 애니메이션으로 같이 움직인다.
///
/// 보조 고정점(손잡이2, 팔라딘 방패): 지정한 Animator 상태(0층 지금/다음 상태)를 재생하는 동안만
/// weapon 을 손잡이1(primaryHandle — 손이 쥐는 점, 없으면 weapon 원점) 둘레로 돌려 손잡이1→손잡이2 방향을 손잡이1→보조 대상(팔꿈치) 방향에 맞춘다.
/// 손잡이1 월드 위치는 회전 전과 같다(원점을 pivot 둘레로 같이 돌려 옮김 — <see cref="AlignAboutPivot"/>).
/// 거리는 맞추지 않는다(단단한 물체). 가중치는 블렌드 시간 동안 0↔1(unscaled — 처치 연출 정지·느려짐에도 끝까지 간다).
/// Animator 를 각 피어가 재생하므로 네트워크 추가 없이 모든 피어 같다. 순수 계산은 <see cref="AlignToSecondary"/> 등(EditMode 테스트).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class WeaponTransformRelay : MonoBehaviour
{
    [System.Serializable]
    private struct WeaponFollow
    {
        [Tooltip("따라갈 대상. 스켈레톤 안의 손 소켓 본(R_/L_WeaponSocket 등).")]
        public Transform target;
        [Tooltip("같은 줄의 target을 따라가는 무기. 보통 WeaponSlot 자식.")]
        public Transform weapon;

        [Tooltip("손잡이1 — 무기 자식, 손이 쥐는 점. 보조 고정은 이 점을 제자리에 둔 채 돈다. 비우면 무기 원점(= target 자리).")]
        public Transform primaryHandle;
        [Tooltip("보조 고정점 — 무기 자식의 손잡이2(예: 방패 팔 띠). 비우면 보조 고정 없음(지금처럼 손만 따라감).")]
        public Transform secondaryHandle;
        [Tooltip("보조 대상 — 손잡이2 가 향할 본 자식(예: forearm_stretch.l 아래 ElbowSocket).")]
        public Transform secondaryTarget;
        [Tooltip("보조 고정을 켤 Animator 상태 이름(0층 지금 또는 전이 중 다음 상태). 예: Garen_Ultimate_Skill.")]
        public string secondaryState;
        [Tooltip("보조 고정 켜고 끄는 블렌드 시간(초, unscaled). 0 = 즉시.")]
        [Min(0f)] public float secondaryBlend;

        [System.NonSerialized] public int stateHash;
        [System.NonSerialized] public string hashedState;
        [System.NonSerialized] public float weight;
    }

    /// <summary>보조 고정 블렌드 기본(초).</summary>
    public const float DefaultSecondaryBlend = 0.1f;

    [Tooltip("target을 weapon이 1:1로 따라간다. 인덱스가 아니라 한 줄로 짝지어진다.")]
    [SerializeField] private WeaponFollow[] follows;
    [Tooltip("보조 고정 상태를 읽을 Animator. 비우면 같은 캐릭터(부모) 아래에서 찾는다.")]
    [SerializeField] private Animator animator;

    /// <summary>
    /// 에디터 미리보기 가중치(손잡이 미리보기 창) — 0 이상이면 Animator 상태 대신 이 값으로 보조 고정(Play 아닐 때만). 음수 = 미리보기 아님.
    /// 저장되지 않는다.
    /// </summary>
    [System.NonSerialized] public float previewWeight = -1f;

    private void LateUpdate() => ApplyNow();

    /// <summary>지금 자세로 따라가기 + 보조 고정을 바로 적용한다(미리보기 창이 애니 샘플링 직후 부른다).</summary>
    public void ApplyNow()
    {
        if (follows == null)
            return;

        for (int i = 0; i < follows.Length; i++)
        {
            Transform target = follows[i].target;
            Transform weapon = follows[i].weapon;
            if (target == null || weapon == null)
                continue;

            weapon.position = target.position;
            weapon.rotation = target.rotation;
            ApplySecondary(ref follows[i], weapon);
        }
    }

    /// <summary>보조 고정이 설정된 첫 줄(무기·손잡이2·보조 대상·블렌드). 없으면 false.</summary>
    public bool TryGetSecondary(out Transform weapon, out Transform handle, out Transform secondaryTarget, out float blend)
    {
        weapon = handle = secondaryTarget = null;
        blend = 0f;
        if (follows == null)
            return false;
        for (int i = 0; i < follows.Length; i++)
        {
            if (follows[i].weapon == null || follows[i].secondaryHandle == null || follows[i].secondaryTarget == null)
                continue;
            weapon = follows[i].weapon;
            handle = follows[i].secondaryHandle;
            secondaryTarget = follows[i].secondaryTarget;
            blend = follows[i].secondaryBlend;
            return true;
        }
        return false;
    }

    private void ApplySecondary(ref WeaponFollow f, Transform weapon)
    {
        if (f.secondaryHandle == null || f.secondaryTarget == null)
            return;

        if (!Application.isPlaying && previewWeight >= 0f)
            f.weight = Mathf.Clamp01(previewWeight);
        else
            f.weight = StepWeight(f.weight, Application.isPlaying && IsInState(ResolveAnimator(), StateHash(ref f)),
                f.secondaryBlend, Time.unscaledDeltaTime);
        if (f.weight <= 0f)
            return;

        Vector3 pivot = f.primaryHandle != null ? f.primaryHandle.position : weapon.position;
        AlignAboutPivot(weapon.position, weapon.rotation, pivot, f.secondaryHandle.position, f.secondaryTarget.position,
            f.weight, out Vector3 pos, out Quaternion rot);
        weapon.SetPositionAndRotation(pos, rot);
    }

    /// <summary>손 소켓 대상과 손잡이1(없으면 null) — 미리보기 창 표시용. 보조 고정 줄이 없으면 false.</summary>
    public bool TryGetGrip(out Transform handSocket, out Transform primaryHandle)
    {
        handSocket = primaryHandle = null;
        if (follows == null)
            return false;
        for (int i = 0; i < follows.Length; i++)
        {
            if (follows[i].weapon == null || follows[i].secondaryHandle == null || follows[i].secondaryTarget == null)
                continue;
            handSocket = follows[i].target;
            primaryHandle = follows[i].primaryHandle;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 손잡이1(<paramref name="pivot"/>, 월드) 을 제자리에 둔 채 무기를 돌린다 — <see cref="AlignToSecondary"/> 로 회전을 구하고
    /// 무기 원점을 pivot 둘레로 같은 만큼 돌려 옮긴다. 결과: 회전 후에도 손잡이1 월드 위치가 그대로(손에서 안 떨어짐).
    /// </summary>
    public static void AlignAboutPivot(Vector3 position, Quaternion rotation, Vector3 pivot, Vector3 handle, Vector3 target,
        float weight, out Vector3 newPosition, out Quaternion newRotation)
    {
        newRotation = AlignToSecondary(rotation, pivot, handle, target, weight);
        Quaternion delta = newRotation * Quaternion.Inverse(rotation);
        newPosition = pivot + delta * (position - pivot);
    }

    private static int StateHash(ref WeaponFollow f)
    {
        if (f.hashedState != f.secondaryState)
        {
            f.hashedState = f.secondaryState;
            f.stateHash = string.IsNullOrEmpty(f.secondaryState) ? 0 : Animator.StringToHash(f.secondaryState);
        }
        return f.stateHash;
    }

    private Animator ResolveAnimator()
    {
        if (animator == null && transform.parent != null)
            animator = transform.parent.GetComponentInChildren<Animator>();
        return animator;
    }

    private static bool IsInState(Animator a, int stateHash)
    {
        if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null || stateHash == 0)
            return false;
        bool inTransition = a.IsInTransition(0);
        return MatchesState(a.GetCurrentAnimatorStateInfo(0).shortNameHash, inTransition,
            inTransition ? a.GetNextAnimatorStateInfo(0).shortNameHash : 0, stateHash);
    }

    /// <summary>보조 고정 상태인가 — 0층 지금 상태이거나, 전이 중 다음 상태(짧은 이름 해시).</summary>
    public static bool MatchesState(int currentHash, bool inTransition, int nextHash, int wantedHash) =>
        wantedHash != 0 && (currentHash == wantedHash || (inTransition && nextHash == wantedHash));

    /// <summary>가중치 한 걸음 — 켜면 1, 끄면 0 쪽으로 <paramref name="blendSeconds"/> 에 걸쳐(0 이하면 즉시).</summary>
    public static float StepWeight(float weight, bool on, float blendSeconds, float deltaTime)
    {
        float target = on ? 1f : 0f;
        if (blendSeconds <= 0f)
            return target;
        return Mathf.MoveTowards(weight, target, Mathf.Max(0f, deltaTime) / blendSeconds);
    }

    /// <summary>
    /// 손잡이1(<paramref name="pivot"/>) 축으로 무기 회전을 돌려, 손잡이1→손잡이2(<paramref name="handle"/>) 방향이
    /// 손잡이1→보조 대상(<paramref name="target"/>) 방향을 향하게 한다(최소 회전). <paramref name="weight"/> 0 = 그대로, 1 = 완전히. 거리는 안 맞춘다.
    /// </summary>
    public static Quaternion AlignToSecondary(Quaternion rotation, Vector3 pivot, Vector3 handle, Vector3 target, float weight)
    {
        Vector3 from = handle - pivot;
        Vector3 to = target - pivot;
        if (weight <= 0f || from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f)
            return rotation;
        Quaternion delta = Quaternion.FromToRotation(from, to);
        return Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(weight)) * rotation;
    }
}
