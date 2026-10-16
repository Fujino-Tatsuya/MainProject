using System;
using UnityEngine;

/// <summary>
/// Authored Dodge hair trajectory on the original seven four-bone chains.
/// Drive after v13 Advance. It changes only hair bone rotations, not the solver,
/// source mesh, skin weights, body animation, bone positions or bone scale.
/// </summary>
[DisallowMultipleComponent]
public sealed class AssassinDiveHairAnimation : MonoBehaviour
{
    [Range(0,1)] public float strength=1f;
    [Range(0,40)] public float fanDegrees=14f;
    [Range(-15,35)] public float rootDropDegrees=12f;
    [Range(-15,25)] public float tipDropDegrees=-2f;
    [Range(0,6)] public float rippleDegrees=2f;
    public float anticipationStart=.18f;
    public float streamFull=.30f;
    public float releaseStart=.65f;
    public float settledTime=1.15f;
    [Range(0,.12f)] public float tipLag=.06f;
    public Vector3 worldUp=Vector3.up;
    public bool motionEnabled=true;

    public bool Captured { get { return captured; } }
    public float CurrentWeight { get; private set; }
    public Vector3 CurrentTrailingDirection { get; private set; }
    public float MaximumSegmentLengthError { get; private set; }
    public float MaximumRootPositionError { get; private set; }
    public float MaximumAuthoredRotation { get; private set; }

    AssassinHairMotion source;
    Transform modelRoot;
    AssassinHairMotion.Chain[] chains;
    Quaternion[][] baseLocal,baseWorld,appliedLocal;
    Vector3[][] guides;
    Vector3[] roots;
    float[] fanCoordinates;
    bool captured,overlayApplied;

    /// <summary>Capture after v13 initialization and before sampling animation.</summary>
    public void Capture(AssassinHairMotion hair,Transform root)
    {
        RestoreBasePose();
        if(!hair)throw new ArgumentNullException(nameof(hair));
        if(!root)throw new ArgumentNullException(nameof(root));
        hair.Initialize();
        if(hair.chains==null||hair.chains.Length!=7)throw new InvalidOperationException("Dodge guide requires the original seven hair chains.");
        source=hair;modelRoot=root;chains=hair.chains;
        baseLocal=new Quaternion[chains.Length][];baseWorld=new Quaternion[chains.Length][];appliedLocal=new Quaternion[chains.Length][];guides=new Vector3[chains.Length][];roots=new Vector3[chains.Length];fanCoordinates=new float[chains.Length];
        float extent=0;Vector3 modelRight=root.rotation*Vector3.right;
        for(int c=0;c<chains.Length;c++)
        {
            var chain=chains[c];
            if(chain.bones==null||chain.bones.Length!=4||chain.lengths==null||chain.axisLocal==null)throw new InvalidOperationException("Dodge guide requires initialized four-segment source chains.");
            baseLocal[c]=new Quaternion[4];baseWorld[c]=new Quaternion[4];appliedLocal[c]=new Quaternion[4];guides[c]=new Vector3[5];
            // Use original root geometry projected on model-right. Imported Head
            // right/forward axes are reversed on this FBX and are not model axes.
            Vector3 originalRootOffset=hair.head.rotation*chain.neutralHeadOffsets[0];
            fanCoordinates[c]=Vector3.Dot(originalRootOffset,modelRight);extent=Mathf.Max(extent,Mathf.Abs(fanCoordinates[c]));
            for(int b=0;b<4;b++)
            {
                if(!chain.bones[b]||chain.lengths[b]<=0||chain.axisLocal[b].sqrMagnitude<.5f)throw new InvalidOperationException("Invalid original hair segment.");
                baseLocal[c][b]=appliedLocal[c][b]=chain.bones[b].localRotation;
            }
        }
        for(int c=0;c<chains.Length;c++)fanCoordinates[c]=extent>1e-6f?fanCoordinates[c]/extent:0;
        captured=true;overlayApplied=false;CurrentWeight=0;
    }

    /// <summary>Call before the next Animator sample/v13 Advance, or to remove the overlay.</summary>
    public void RestoreBasePose()
    {
        if(!captured||!overlayApplied)return;
        for(int c=0;c<chains.Length;c++)for(int b=0;b<4;b++)if(chains[c].bones[b])chains[c].bones[b].localRotation=baseLocal[c][b];
        overlayApplied=false;
    }

    public void ResetPose()
    {
        RestoreBasePose();CurrentWeight=0;CurrentTrailingDirection=Vector3.zero;
        MaximumSegmentLengthError=MaximumRootPositionError=MaximumAuthoredRotation=0;
    }

    static float Smooth01(float x){x=Mathf.Clamp01(x);return x*x*x*(x*(x*6-15)+10);}
    float PhaseWeight(float time,int segment)
    {
        float delay=tipLag*segment/3f;
        float t=time-delay;
        float rise=Smooth01((t-anticipationStart)/Mathf.Max(.001f,streamFull-anticipationStart));
        float fall=1-Smooth01((t-releaseStart)/Mathf.Max(.001f,settledTime-releaseStart));
        return Mathf.Clamp01(strength)*rise*fall;
    }
    public float EvaluateWeight(float clipTime,int segment=0)
    {
        return motionEnabled?PhaseWeight(clipTime,Mathf.Clamp(segment,0,3)):0;
    }
    static Quaternion Between(Vector3 from,Vector3 to)
    {
        from.Normalize();to.Normalize();float dot=Mathf.Clamp(Vector3.Dot(from,to),-1,1);
        if(dot<-.999999f){Vector3 axis=Vector3.Cross(from,Mathf.Abs(from.y)<.9f?Vector3.up:Vector3.right);return Quaternion.AngleAxis(180,axis.normalized);}
        Vector3 cross=Vector3.Cross(from,to);return new Quaternion(cross.x,cross.y,cross.z,1+dot).normalized;
    }
    static bool Finite(Vector3 p)=>!(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z));
    bool StillOurPose()
    {
        for(int c=0;c<chains.Length;c++)for(int b=0;b<4;b++)if(Mathf.Abs(Quaternion.Dot(chains[c].bones[b].localRotation,appliedLocal[c][b]))<.999999f)return false;
        return true;
    }

    /// <summary>
    /// clipTime is original Dodge seconds. Pass stable intended travel-opposite
    /// direction, e.g. -(modelRoot.rotation*Vector3.forward), for both in-place
    /// and root-motion playback. Do not infer it from Head.forward or tiny
    /// recovery root velocities. Call RestoreBasePose before base Advance.
    /// </summary>
    public void ApplyPose(float clipTime,Vector3 trailingWorldDirection)
    {
        if(!captured)throw new InvalidOperationException("Capture the Dodge hair guide first.");
        if(float.IsNaN(clipTime)||float.IsInfinity(clipTime)||!Finite(trailingWorldDirection))throw new ArgumentException("Nonfinite Dodge guide input.");
        // Repeated review calls in the same pose do not accumulate a correction.
        // If v13 already supplied a new pose, use that fresh pose instead.
        if(overlayApplied){if(StillOurPose())RestoreBasePose();else overlayApplied=false;}
        Vector3 up=worldUp.sqrMagnitude>1e-8f?worldUp.normalized:Vector3.up;
        Vector3 trailing=Vector3.ProjectOnPlane(trailingWorldDirection,up);
        if(trailing.sqrMagnitude<1e-8f)trailing=Vector3.ProjectOnPlane(-(modelRoot.rotation*Vector3.forward),up);
        if(trailing.sqrMagnitude<1e-8f)throw new InvalidOperationException("Dodge trailing direction must have a horizontal component.");
        trailing.Normalize();Vector3 right=Vector3.Cross(trailing,up).normalized;
        CurrentTrailingDirection=trailing;CurrentWeight=0;MaximumSegmentLengthError=MaximumRootPositionError=MaximumAuthoredRotation=0;
        for(int c=0;c<chains.Length;c++)
        {
            roots[c]=chains[c].bones[0].position;
            for(int b=0;b<4;b++){baseLocal[c][b]=chains[c].bones[b].localRotation;baseWorld[c][b]=chains[c].bones[b].rotation;}
        }
        float maximumBlend=0;for(int b=0;b<4;b++)maximumBlend=Mathf.Max(maximumBlend,EvaluateWeight(clipTime,b));
        if(maximumBlend<=0)
        {
            // Preserve exact source quaternions outside the authored interval.
            for(int c=0;c<chains.Length;c++)
            {
                for(int b=0;b<4;b++){guides[c][b]=chains[c].bones[b].position;appliedLocal[c][b]=baseLocal[c][b];}
                guides[c][4]=chains[c].bones[3].TransformPoint(chains[c].tipLocal);
            }
            overlayApplied=false;return;
        }
        for(int c=0;c<chains.Length;c++)
        {
            var chain=chains[c];guides[c][0]=roots[c];
            for(int b=0;b<4;b++)
            {
                float depth=(b+.5f)/4f;
                // A shared smooth fan keeps the continuous sheet ordered; no
                // arbitrary strand splitting or contact-driven vertex displacement.
                float lateral=fanCoordinates[c]*fanDegrees*Mathf.Lerp(.65f,1,depth);
                Vector3 horizontal=(trailing*Mathf.Cos(lateral*Mathf.Deg2Rad)+right*Mathf.Sin(lateral*Mathf.Deg2Rad)).normalized;
                float drop=Mathf.Lerp(rootDropDegrees,tipDropDegrees,Smooth01(depth));
                float ripple=rippleDegrees*depth*depth*Mathf.Sin(clipTime*12-depth*3.5f);
                drop+=ripple;
                Vector3 desired=(horizontal*Mathf.Cos(drop*Mathf.Deg2Rad)-up*Mathf.Sin(drop*Mathf.Deg2Rad)).normalized;
                Vector3 originalDirection=baseWorld[c][b]*chain.axisLocal[b];
                Quaternion target=(Between(originalDirection,desired)*baseWorld[c][b]).normalized;
                float blend=EvaluateWeight(clipTime,b);CurrentWeight=Mathf.Max(CurrentWeight,blend);
                Quaternion output=blend<=0?baseWorld[c][b]:Quaternion.Slerp(baseWorld[c][b],target,blend).normalized;
                // Setting world rotation in hierarchy order preserves all original
                // child translations, root attachments and segment lengths.
                chain.bones[b].rotation=output;
                appliedLocal[c][b]=chain.bones[b].localRotation;
                MaximumAuthoredRotation=Mathf.Max(MaximumAuthoredRotation,Quaternion.Angle(baseWorld[c][b],output));
                guides[c][b+1]=guides[c][b]+(output*chain.axisLocal[b]).normalized*chain.lengths[b];
            }
            MaximumRootPositionError=Mathf.Max(MaximumRootPositionError,Vector3.Distance(roots[c],chain.bones[0].position));
            for(int b=0;b<4;b++)
            {
                Vector3 end=b<3?chain.bones[b+1].position:chain.bones[b].TransformPoint(chain.tipLocal);
                if(!Finite(end))throw new InvalidOperationException("Nonfinite authored hair output.");
                MaximumSegmentLengthError=Mathf.Max(MaximumSegmentLengthError,Mathf.Abs(Vector3.Distance(chain.bones[b].position,end)-chain.lengths[b]));
            }
        }
        overlayApplied=CurrentWeight>0;
    }

    public Vector3[][] GetGuidePoints()
    {
        if(!captured)return new Vector3[0][];
        var result=new Vector3[guides.Length][];for(int c=0;c<guides.Length;c++)result[c]=(Vector3[])guides[c].Clone();return result;
    }
    public Vector3[][] GetAppliedGuidePoints()
    {
        if(!captured)return new Vector3[0][];
        var result=new Vector3[chains.Length][];
        for(int c=0;c<chains.Length;c++){result[c]=new Vector3[5];for(int b=0;b<4;b++)result[c][b]=chains[c].bones[b].position;result[c][4]=chains[c].bones[3].TransformPoint(chains[c].tipLocal);}
        return result;
    }
    void OnDisable(){RestoreBasePose();}
    void OnDestroy(){RestoreBasePose();}
}
