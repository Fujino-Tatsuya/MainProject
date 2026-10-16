using System;
using UnityEngine;

/// <summary>
/// Per-action authored hair post-pass, layered after the unchanged v13 solver.
/// Only the original 28 hair rotations are written. Dodge belongs to v15.
/// </summary>
[DisallowMultipleComponent]
public sealed class AssassinActionHairAnimation : MonoBehaviour
{
    [Range(0,1)] public float strength=1;
    public bool motionEnabled=true;
    public Vector3 worldUp=Vector3.up;
    public bool Captured { get { return captured; } }
    public string CurrentProfile { get; private set; }
    public float CurrentWeight { get; private set; }
    public Vector3 CurrentTrailingDirection { get; private set; }
    public float MaximumSegmentLengthError { get; private set; }
    public float MaximumRootPositionError { get; private set; }
    public float MaximumAuthoredRotation { get; private set; }

    Transform modelRoot;
    AssassinHairMotion.Chain[] chains;
    Quaternion[][] baseLocal,baseWorld,appliedLocal;
    Vector3[] roots;
    float[] fanCoordinates;
    bool captured,overlayApplied;

    /// <summary>Capture before any animation samples, after v13 initialization.</summary>
    public void Capture(AssassinHairMotion hair,Transform root)
    {
        RestoreBasePose();
        if(!hair)throw new ArgumentNullException(nameof(hair));
        if(!root)throw new ArgumentNullException(nameof(root));
        hair.Initialize();
        if(hair.chains==null||hair.chains.Length!=7)throw new InvalidOperationException("Action guide requires the original seven hair chains.");
        modelRoot=root;chains=hair.chains;
        baseLocal=new Quaternion[7][];baseWorld=new Quaternion[7][];appliedLocal=new Quaternion[7][];
        roots=new Vector3[7];fanCoordinates=new float[7];
        float extent=0;Vector3 right=root.rotation*Vector3.right;
        for(int c=0;c<7;c++)
        {
            var chain=chains[c];
            if(chain.bones==null||chain.bones.Length!=4||chain.lengths==null||chain.axisLocal==null)
                throw new InvalidOperationException("Action guide requires the original four-segment chains.");
            baseLocal[c]=new Quaternion[4];baseWorld[c]=new Quaternion[4];appliedLocal[c]=new Quaternion[4];
            fanCoordinates[c]=Vector3.Dot(hair.head.rotation*chain.neutralHeadOffsets[0],right);
            extent=Mathf.Max(extent,Mathf.Abs(fanCoordinates[c]));
            for(int b=0;b<4;b++)
            {
                if(!chain.bones[b]||chain.lengths[b]<=0||chain.axisLocal[b].sqrMagnitude<.5f)
                    throw new InvalidOperationException("Invalid source hair chain.");
                baseLocal[c][b]=appliedLocal[c][b]=chain.bones[b].localRotation;
            }
        }
        for(int c=0;c<7;c++)fanCoordinates[c]=extent>1e-6f?fanCoordinates[c]/extent:0;
        captured=true;overlayApplied=false;CurrentWeight=0;CurrentProfile="Captured";
    }
    public void RestoreBasePose()
    {
        if(!captured||!overlayApplied)return;
        for(int c=0;c<7;c++)for(int b=0;b<4;b++)if(chains[c].bones[b])chains[c].bones[b].localRotation=baseLocal[c][b];
        overlayApplied=false;
    }
    public void ResetPose()
    {
        RestoreBasePose();CurrentWeight=0;CurrentProfile="Inactive";CurrentTrailingDirection=Vector3.zero;
        MaximumSegmentLengthError=MaximumRootPositionError=MaximumAuthoredRotation=0;
    }
    public float EvaluateWeight(string clipName,float time,float length,int segment=0)
    {
        return motionEnabled?Mathf.Clamp01(strength)*AssassinActionHairProfiles.Evaluate(clipName,time,length,segment).weight:0;
    }
    static bool Finite(Vector3 p)
    {
        return !(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z));
    }
    static Quaternion Between(Vector3 from,Vector3 to)
    {
        from.Normalize();to.Normalize();float dot=Mathf.Clamp(Vector3.Dot(from,to),-1,1);
        if(dot<-.999999f)
        {
            Vector3 axis=Vector3.Cross(from,Mathf.Abs(from.y)<.9f?Vector3.up:Vector3.right);
            return Quaternion.AngleAxis(180,axis.normalized);
        }
        Vector3 cross=Vector3.Cross(from,to);return new Quaternion(cross.x,cross.y,cross.z,1+dot).normalized;
    }
    bool StillOurPose()
    {
        for(int c=0;c<7;c++)for(int b=0;b<4;b++)
            if(Mathf.Abs(Quaternion.Dot(chains[c].bones[b].localRotation,appliedLocal[c][b]))<.999999f)return false;
        return true;
    }

    /// <summary>
    /// Apply after base v13 Advance. RestoreBasePose must run before next Advance.
    /// launchTrailingWorldDirection is opposite the intended forward launch,
    /// independent of whether root translation is enabled for preview.
    /// </summary>
    public void ApplyPose(string clipName,float clipTime,float clipLength,Vector3 launchTrailingWorldDirection)
    {
        if(!captured)throw new InvalidOperationException("Capture action hair before playback.");
        if(float.IsNaN(clipTime)||float.IsInfinity(clipTime)||float.IsNaN(clipLength)||float.IsInfinity(clipLength)||!Finite(launchTrailingWorldDirection))
            throw new ArgumentException("Nonfinite action hair input.");
        if(overlayApplied) { if(StillOurPose())RestoreBasePose();else overlayApplied=false; }
        CurrentWeight=0;MaximumSegmentLengthError=MaximumRootPositionError=MaximumAuthoredRotation=0;
        CurrentProfile=AssassinActionHairProfiles.Evaluate(clipName,clipTime,clipLength).family;
        for(int b=0;b<4;b++)CurrentWeight=Mathf.Max(CurrentWeight,EvaluateWeight(clipName,clipTime,clipLength,b));
        if(CurrentWeight<=0)
        {
            // Exact no-write for Dodge, retained actions and off-phase samples.
            overlayApplied=false;return;
        }
        Vector3 up=worldUp.sqrMagnitude>1e-8f?worldUp.normalized:Vector3.up;
        Vector3 trailing=Vector3.ProjectOnPlane(launchTrailingWorldDirection,up);
        if(trailing.sqrMagnitude<1e-8f)trailing=Vector3.ProjectOnPlane(-(modelRoot.rotation*Vector3.forward),up);
        if(trailing.sqrMagnitude<1e-8f)throw new InvalidOperationException("Action launch direction has no horizontal component.");
        trailing.Normalize();CurrentTrailingDirection=trailing;
        for(int c=0;c<7;c++)
        {
            roots[c]=chains[c].bones[0].position;
            for(int b=0;b<4;b++) { baseLocal[c][b]=chains[c].bones[b].localRotation;baseWorld[c][b]=chains[c].bones[b].rotation; }
        }
        for(int c=0;c<7;c++)
        {
            var chain=chains[c];
            for(int b=0;b<4;b++)
            {
                var pose=AssassinActionHairProfiles.Evaluate(clipName,clipTime,clipLength,b);
                float blend=motionEnabled?Mathf.Clamp01(strength)*pose.weight:0;
                float depth=(b+.5f)/4;
                Quaternion output=baseWorld[c][b];
                if(blend>0)
                {
                    if(pose.directionGuide)
                    {
                        Vector3 heading=Quaternion.AngleAxis(pose.yawDegrees,up)*trailing;
                        Vector3 right=Vector3.Cross(heading,up).normalized;
                        float fan=fanCoordinates[c]*pose.fanDegrees*Mathf.Lerp(.65f,1,depth)*Mathf.Deg2Rad;
                        Vector3 horizontal=(heading*Mathf.Cos(fan)+right*Mathf.Sin(fan)).normalized;
                        float drop=Mathf.Lerp(pose.rootDropDegrees,pose.tipDropDegrees,AssassinActionHairProfiles.Smooth01(depth));
                        drop+=pose.rippleDegrees*depth*depth*Mathf.Sin(pose.ripplePhase-depth*2.5f);
                        Vector3 desired=(horizontal*Mathf.Cos(drop*Mathf.Deg2Rad)-up*Mathf.Sin(drop*Mathf.Deg2Rad)).normalized;
                        Vector3 originalDirection=baseWorld[c][b]*chain.axisLocal[b];
                        Quaternion target=(Between(originalDirection,desired)*baseWorld[c][b]).normalized;
                        output=Quaternion.Slerp(baseWorld[c][b],target,blend).normalized;
                    }
                    else
                    {
                        // Preserve the existing body-driven lift and curvature:
                        // a small shared yaw provides delayed follow-through.
                        float yaw=(pose.yawDegrees*Mathf.Lerp(.30f,1,depth)+fanCoordinates[c]*pose.fanDegrees*depth)*blend;
                        output=(Quaternion.AngleAxis(yaw,up)*baseWorld[c][b]).normalized;
                    }
                }
                chain.bones[b].rotation=output;
                appliedLocal[c][b]=chain.bones[b].localRotation;
                MaximumAuthoredRotation=Mathf.Max(MaximumAuthoredRotation,Quaternion.Angle(baseWorld[c][b],output));
            }
            MaximumRootPositionError=Mathf.Max(MaximumRootPositionError,Vector3.Distance(roots[c],chain.bones[0].position));
            for(int b=0;b<4;b++)
            {
                Vector3 end=b<3?chain.bones[b+1].position:chain.bones[b].TransformPoint(chain.tipLocal);
                if(!Finite(end))throw new InvalidOperationException("Nonfinite action hair output.");
                MaximumSegmentLengthError=Mathf.Max(MaximumSegmentLengthError,Mathf.Abs(Vector3.Distance(chain.bones[b].position,end)-chain.lengths[b]));
            }
        }
        overlayApplied=true;
    }
    public Vector3[][] GetAppliedGuidePoints()
    {
        if(!captured)return new Vector3[0][];
        var result=new Vector3[7][];
        for(int c=0;c<7;c++)
        {
            result[c]=new Vector3[5];for(int b=0;b<4;b++)result[c][b]=chains[c].bones[b].position;
            result[c][4]=chains[c].bones[3].TransformPoint(chains[c].tipLocal);
        }
        return result;
    }
    void OnDisable() { RestoreBasePose(); }
    void OnDestroy() { RestoreBasePose(); }
}
