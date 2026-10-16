using UnityEngine;

/// <summary>
/// 할 일 47 — 팔꿈치가 머리카락 양옆으로 튀어나오는 문제의 후처리. v13 은 옆 가닥을 몸통만 보는 가상 중심 가닥에서
/// 8°(seg0)·12°(seg1) 이상 못 벗어나게 묶어 팔 접촉을 다 못 푼다(진단: 잔여 47~73mm, 필요 회전 22~56°).
/// v13 Advance·동작별 보정이 끝난 화면 자세에서, 팔 캡슐(좌우 상완·전완)에 닿는 마디만 뿌리 쪽부터 회전시켜 밀어낸다.
/// 회전만 쓰므로 뿌리 위치·마디 길이는 그대로다. 캡슐은 v13 이 이미 가진 것을 이름으로 찾아 쓴다(반경·bindAllowance 동일).
/// 몸통(가슴·허리) 캡슐은 밀어낸 가닥이 원래보다 깊게 들어가지 않게 막는 장애물로만 쓴다 — 팔에 안 닿는 가닥은 그대로다.
/// 판단(<see cref="SolveSegment"/>)은 순수 함수 — EditMode 로 검증한다.
/// </summary>
public static class AssassinHairArmClearance
{
    public struct Capsule
    {
        public Vector3 a, b;
        public float radius;

        public Capsule(Vector3 a, Vector3 b, float radius)
        {
            this.a = a;
            this.b = b;
            this.radius = radius;
        }
    }

    /// <summary>구동기가 Awake 에서 한 번 만든다 — 캡슐 인덱스와 프레임마다 쓰는 작업 버퍼.</summary>
    public sealed class Buffers
    {
        public readonly int[] arm, torso;
        internal readonly Capsule[] scratch;
        internal readonly float[,] torsoBaseline;

        public Buffers(int[] arm, int[] torso)
        {
            this.arm = arm;
            this.torso = torso;
            scratch = new Capsule[arm.Length + torso.Length];
            torsoBaseline = new float[SegmentCount, torso.Length];
        }
    }

    public static readonly string[] ArmCapsuleNames = { "Left upper arm", "Left forearm", "Right upper arm", "Right forearm" };
    // 밀어낸 가닥이 가슴·허리로 파고들지 않게 막는 장애물 — 이미 겹친 만큼은 그대로 둔다(v13 몫).
    public static readonly string[] TorsoCapsuleNames = { "Chest", "Waist" };

    private const int ChainCount = 7;
    private const int SegmentCount = 4;
    private const int Samples = 12;
    private const int Iterations = 4;
    private const float MinimumRadius = .006f; // v13 ProjectContacts 와 같은 하한
    private const float Epsilon = 1e-7f;

    /// <summary>v13 캡슐 배열에서 이름이 맞는 캡슐의 인덱스. 없는 이름은 건너뛴다.</summary>
    public static int[] FindCapsules(AssassinHairMotion.BodyCapsule[] capsules, string[] names)
    {
        var found = new System.Collections.Generic.List<int>(names.Length);
        if (capsules == null)
            return found.ToArray();

        for (int k = 0; k < capsules.Length; k++)
            if (capsules[k] != null && System.Array.IndexOf(names, capsules[k].name) >= 0)
                found.Add(k);
        return found.ToArray();
    }

    public static Buffers Prepare(AssassinHairMotion hair)
    {
        return new Buffers(FindCapsules(hair.capsules, ArmCapsuleNames), FindCapsules(hair.capsules, TorsoCapsuleNames));
    }

    /// <summary>마디 위 밀어내기 표본(뿌리 제외 12점 — 측정과 같은 간격)과 캡슐 축의 최소 거리. 몸통 기준선 — 이보다 가까이 가지 못하게 한다.</summary>
    public static float SampledDistance(Vector3 pivot, Vector3 end, Vector3 a, Vector3 b)
    {
        float best = float.PositiveInfinity;
        for (int s = 1; s <= Samples; s++)
        {
            Vector3 p = Vector3.Lerp(pivot, end, s / (float)Samples);
            best = Mathf.Min(best, Vector3.Distance(p, Nearest(p, a, b)));
        }
        return best;
    }

    /// <summary>
    /// 마디 [pivot, end] 를 pivot 기준으로 돌려 캡슐 밖으로 내보낼 월드 회전. 총 회전각은 <paramref name="maxDegrees"/> 를 넘지 않는다.
    /// <paramref name="guardFrom"/> 부터 끝까지는 가드 캡슐 — 한 걸음이 가드 겹침을 그 걸음 전보다 늘리면 늘리지 않는 만큼만 간다.
    /// 반환값 = 적용 회전. <paramref name="degrees"/> = 적용 각도(도), 겹침이 없으면 identity·0.
    /// </summary>
    public static Quaternion SolveSegment(Vector3 pivot, Vector3 end, Capsule[] capsules, int count, int guardFrom, float maxDegrees, out float degrees, out bool clamped)
    {
        Quaternion total = Quaternion.identity;
        degrees = 0f;
        clamped = false;
        Vector3 arm = end - pivot;
        if (arm.sqrMagnitude < Epsilon || maxDegrees <= 0f)
            return total;

        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            Vector3 current = total * arm;
            if (!Deepest(pivot, current, capsules, count, out Vector3 sample, out Vector3 pushed))
                break;

            Quaternion step = FromTo(sample - pivot, pushed - pivot);
            Quaternion next = (step * total).normalized;
            if (guardFrom < count)
            {
                float allowed = Mathf.Max(0f, GuardPenetration(pivot, current, capsules, guardFrom, count)) + GuardTolerance;
                if (GuardPenetration(pivot, next * arm, capsules, guardFrom, count) > allowed)
                {
                    // 가드를 넘지 않는 가장 큰 비율을 이분법으로 찾는다. 거의 못 가면 여기서 멈춘다.
                    float lo = 0f, hi = 1f;
                    for (int i = 0; i < GuardBisections; i++)
                    {
                        float mid = (lo + hi) * .5f;
                        Quaternion probe = (Quaternion.Slerp(Quaternion.identity, step, mid) * total).normalized;
                        if (GuardPenetration(pivot, probe * arm, capsules, guardFrom, count) > allowed) hi = mid;
                        else lo = mid;
                    }
                    if (lo <= 0f)
                        break;
                    next = (Quaternion.Slerp(Quaternion.identity, step, lo) * total).normalized;
                }
            }

            float angle = 2f * Mathf.Acos(Mathf.Min(1f, Mathf.Abs(next.w))) * Mathf.Rad2Deg;
            if (angle > maxDegrees)
            {
                total = Quaternion.Slerp(Quaternion.identity, next, maxDegrees / angle);
                degrees = maxDegrees;
                clamped = true;
                return total;
            }
            total = next;
            degrees = angle;
        }
        return total;
    }

    private const float GuardTolerance = .0005f;
    private const int GuardBisections = 6;

    private static float GuardPenetration(Vector3 pivot, Vector3 arm, Capsule[] capsules, int from, int count)
    {
        float worst = float.NegativeInfinity;
        for (int k = from; k < count; k++)
            worst = Mathf.Max(worst, capsules[k].radius - SampledDistance(pivot, pivot + arm, capsules[k].a, capsules[k].b));
        return worst;
    }

    /// <summary>
    /// v13·보정이 쓴 지금 본 회전 위에 한 번 덧쓴다. 다음 프레임 v13 Advance 가 CaptureTargets 에서 바인드 회전으로 되돌린 뒤
    /// 자기 내부 점(points/frames)으로 다시 쓰므로 v13 시뮬레이션 상태에는 들어가지 않는다.
    /// </summary>
    public static void Apply(AssassinHairMotion hair, Buffers buffers, float maxDegrees)
    {
        if (hair == null || hair.chains == null || buffers == null || buffers.arm.Length == 0)
            return;

        int armCount = buffers.arm.Length;
        int count = armCount + buffers.torso.Length;
        for (int c = 0; c < ChainCount && c < hair.chains.Length; c++)
        {
            AssassinHairMotion.Chain chain = hair.chains[c];
            if (chain?.bones == null || chain.bindAllowance == null)
                continue;

            // 몸통 기준선은 이 가닥을 건드리기 전(②) 자세로 잰다 — 부모 마디 회전이 데려간 자식도 원래보다 깊게 못 들어간다.
            for (int b = 0; b < SegmentCount; b++)
                for (int j = 0; j < buffers.torso.Length; j++)
                {
                    AssassinHairMotion.BodyCapsule cap = hair.capsules[buffers.torso[j]];
                    buffers.torsoBaseline[b, j] = SampledDistance(chain.bones[b].position, SegmentEnd(chain, b),
                        cap.first.TransformPoint(cap.firstOffset), cap.last.TransformPoint(cap.lastOffset));
                }

            for (int b = 0; b < SegmentCount; b++)
            {
                for (int i = 0; i < count; i++)
                {
                    int k = i < armCount ? buffers.arm[i] : buffers.torso[i - armCount];
                    AssassinHairMotion.BodyCapsule cap = hair.capsules[k];
                    float radius = Mathf.Max(MinimumRadius, cap.radius + hair.hairRadius - chain.bindAllowance[b, k]);
                    if (i >= armCount)
                        radius = Mathf.Min(radius, buffers.torsoBaseline[b, i - armCount]);
                    buffers.scratch[i] = new Capsule(cap.first.TransformPoint(cap.firstOffset), cap.last.TransformPoint(cap.lastOffset), radius);
                }

                Transform bone = chain.bones[b];
                Quaternion push = SolveSegment(bone.position, SegmentEnd(chain, b), buffers.scratch, count, armCount, maxDegrees, out float applied, out _);
                // 본 회전은 자식 마디를 함께 데려간다 — 다음 마디는 옮겨진 자리에서 다시 판단한다.
                if (applied > 0f)
                    bone.rotation = (push * bone.rotation).normalized;
            }
        }
    }

    private static Vector3 SegmentEnd(AssassinHairMotion.Chain chain, int b)
    {
        return b < SegmentCount - 1 ? chain.bones[b + 1].position : chain.bones[b].TransformPoint(chain.tipLocal);
    }

    /// <summary>마디 위 표본 중 가장 깊이 파고든 점과 그 점을 캡슐 표면으로 낸 위치.</summary>
    private static bool Deepest(Vector3 pivot, Vector3 arm, Capsule[] capsules, int count, out Vector3 sample, out Vector3 pushed)
    {
        sample = pushed = pivot;
        float best = 1e-5f;
        bool hit = false;
        for (int s = 1; s <= Samples; s++)
        {
            Vector3 p = pivot + arm * (s / (float)Samples);
            for (int k = 0; k < count; k++)
            {
                Capsule cap = capsules[k];
                Vector3 close = Nearest(p, cap.a, cap.b);
                Vector3 away = p - close;
                float distance = away.magnitude;
                float penetration = cap.radius - distance;
                if (penetration <= best)
                    continue;

                Vector3 normal = distance > Epsilon ? away / distance : Fallback(pivot, close, cap);
                best = penetration;
                sample = p;
                pushed = close + normal * cap.radius;
                hit = true;
            }
        }
        return hit;
    }

    // 표본이 캡슐 축 위에 있으면 뿌리 쪽(캡슐 축에 수직 성분)으로 낸다.
    private static Vector3 Fallback(Vector3 pivot, Vector3 close, Capsule cap)
    {
        Vector3 axis = cap.b - cap.a;
        Vector3 away = pivot - close;
        if (axis.sqrMagnitude > Epsilon)
            away = Vector3.ProjectOnPlane(away, axis.normalized);
        if (away.sqrMagnitude < Epsilon)
            away = Vector3.Cross(axis.sqrMagnitude > Epsilon ? axis : Vector3.right, Vector3.up);
        if (away.sqrMagnitude < Epsilon)
            away = Vector3.forward;
        return away.normalized;
    }

    private static Vector3 Nearest(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float t = d.sqrMagnitude < Epsilon ? 0f : Mathf.Clamp01(Vector3.Dot(point - a, d) / d.sqrMagnitude);
        return a + d * t;
    }

    // v13 RotationBetween 과 같은 식 — 내장 FromToRotation 은 아주 작은 각을 버릴 수 있다. 밀어내기는 180° 근처가 나오지 않는다.
    private static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        if (from.sqrMagnitude < Epsilon || to.sqrMagnitude < Epsilon)
            return Quaternion.identity;
        from.Normalize();
        to.Normalize();
        Vector3 cross = Vector3.Cross(from, to);
        return new Quaternion(cross.x, cross.y, cross.z, 1f + Vector3.Dot(from, to)).normalized;
    }
}
