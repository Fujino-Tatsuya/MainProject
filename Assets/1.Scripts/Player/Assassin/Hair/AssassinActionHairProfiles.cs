using System;
using UnityEngine;

/// <summary>Authored post-pass profiles for the remaining selected actions.</summary>
public static class AssassinActionHairProfiles
{
    public struct Pose
    {
        public string family;
        public float weight, rootDropDegrees, tipDropDegrees, yawDegrees, fanDegrees;
        public float rippleDegrees, ripplePhase;
        public bool directionGuide;
    }

    public static float Smooth01(float x) { x=Mathf.Clamp01(x);return x*x*x*(x*(x*6-15)+10); }
    static float Envelope(float t,float start,float full,float release,float end)
    {
        return Smooth01((t-start)/Mathf.Max(.001f,full-start))*(1-Smooth01((t-release)/Mathf.Max(.001f,end-release)));
    }
    static float Cycle(float t,float length)
    {
        if(length<.0001f)return 0;
        return Mathf.Repeat(t/length,1)*Mathf.PI*2;
    }
    static Pose Base(string family) { return new Pose { family=family }; }
    static Pose Guide(string family,float w,float rootDrop,float tipDrop,float yaw,float fan,float ripple,float phase)
    {
        return new Pose {family=family,weight=w,rootDropDegrees=rootDrop,tipDropDegrees=tipDrop,
            yawDegrees=yaw,fanDegrees=fan,rippleDegrees=ripple,ripplePhase=phase,directionGuide=true};
    }
    static Pose Sweep(string family,float w,float yaw,float fan=0)
    {
        return new Pose {family=family,weight=w,yawDegrees=yaw,fanDegrees=fan,directionGuide=false};
    }

    public static bool IsSupported(string name)
    {
        if(string.IsNullOrEmpty(name))return false;
        switch(name)
        {
            case "Idle":case "Idle_Combat":case "Run_Combat_Fast_Start":case "Run_Combat_Fast_Loop":
            case "Run_Combat_Fast_Stop":case "Speed_Attack_Loop":case "Combo_Attack_Wave_All":
            case "Combo_Attack_Wave_01":case "Combo_Attack_Wave_02":case "Combo_Attack_Wave_03":case "Combo_Attack_Wave_04":
            case "Combo_Attack_02_04":case "Combo_Attack_03_04":case "Combo_Attack_02_01":
            case "Buff":case "Attack_Up_01":case "Parry_R":case "Skill_02":return true;
        }
        return TrySkillScale(name,out _);
    }
    public static bool IsRetained(string name)
    {
        return name=="Idle"||name=="Idle_Combat"||name=="Buff"||name=="Attack_Up_01"||name=="Parry_R"||name=="Combo_Attack_02_01"||TrySkillScale(name,out _);
    }
    public static bool IsPeriodic(string name) { return name=="Run_Combat_Fast_Loop"||name=="Speed_Attack_Loop"; }
    static bool TrySkillScale(string name,out float amount)
    {
        amount=1;
        if(name=="Skill_02")return true;
        const string prefix="Skill_02_Move_";
        if(name==null||!name.StartsWith(prefix,StringComparison.Ordinal)||!name.EndsWith("pct",StringComparison.Ordinal))return false;
        string number=name.Substring(prefix.Length,name.Length-prefix.Length-3);
        if(!int.TryParse(number,out int pct)||pct<0||pct>90||pct%10!=0)return false;
        amount=pct*.01f;return true;
    }

    // Wave01/02/03 reuse the first 25 frames (60 Hz) of their source attack,
    // followed by their OWN authored recovery. Wave04 is the complete last
    // 1.25 seconds of Wave_All. Offsets were verified from non-root muscle curves.
    public static float WaveSourceOffset(string name)
    {
        switch(name) { case "Combo_Attack_Wave_02":return 25f/60;case "Combo_Attack_Wave_03":return 50f/60;
            case "Combo_Attack_Wave_04":return 75f/60;default:return 0; }
    }
    static float WaveYaw(float sourceTime)
    {
        // Four distinct follow-through sweeps share one continuous source clock.
        return 11*Envelope(sourceTime,.03f,.17f,.31f,.60f)
            -13*Envelope(sourceTime,.44f,.57f,.77f,1.03f)
            +14*Envelope(sourceTime,.86f,1.03f,1.18f,1.44f)
            -12*Envelope(sourceTime,1.29f,1.48f,1.70f,2.22f);
    }

    public static Pose Evaluate(string name,float time,float length,int segment=0)
    {
        segment=Mathf.Clamp(segment,0,3);
        if(!IsSupported(name))return Base(name=="Dodge_Combat_F_0"?"Accepted v15 Dodge":"Unsupported");
        if(IsRetained(name))return Base("Reviewed v13 retained");
        float delay=segment*.014f;
        float t=time-delay;
        if(name=="Run_Combat_Fast_Loop")
        {
            float phase=Cycle(t,length);
            return Guide("Running",.76f,34,20,2.2f*Mathf.Sin(phase),15,1.2f,phase*2);
        }
        if(name=="Run_Combat_Fast_Start")
        {
            float rise=Smooth01((t-.06f)/.23f);
            return Guide("Run acceleration",.76f*rise,34,20,0,15,1.1f,Cycle(t,.4666667f)*2);
        }
        if(name=="Run_Combat_Fast_Stop")
        {
            float fall=1-Smooth01((t-.06f)/.55f);
            // The lifted trailing shape settles while the original simulation
            // supplies the small forward rebound; do not invent a new body pose.
            return Guide("Run braking",.76f*fall,34,20,0,15,.8f,Cycle(t,.4666667f)*2);
        }
        if(name=="Speed_Attack_Loop")
        {
            float phase=Cycle(t,length);
            return Sweep("Repeated attack",1,7.5f*Mathf.Sin(phase)+2*Mathf.Sin(phase*2),2*(1-Mathf.Cos(phase))*.5f);
        }
        if(name.StartsWith("Combo_Attack_Wave_",StringComparison.Ordinal))
        {
            bool full=name=="Combo_Attack_Wave_All";
            bool fourth=name=="Combo_Attack_Wave_04";
            float sourceTime=t+WaveSourceOffset(name);
            float recovery=1;
            if(!full&&!fourth)
            {
                sourceTime=Mathf.Min(sourceTime,WaveSourceOffset(name)+25f/60);
                recovery=1-Smooth01((t-25f/60)/.55f);
            }
            float yaw=WaveYaw(sourceTime)*recovery;
            return Sweep("Wave combo",Mathf.Abs(yaw)>1e-6f?1:0,yaw);
        }
        if(name=="Combo_Attack_02_04")
        {
            float w=.72f*Envelope(t,0,.14f,.42f,.92f);
            return Guide("Low forward Q",w,35,22,-4,15,1.2f,t*12);
        }
        if(name=="Combo_Attack_03_04")
        {
            float w=.55f*Envelope(t,0,.18f,.58f,1.15f);
            float yaw=-20*Envelope(t,.03f,.23f,.51f,.95f);
            return Guide("Airborne turning Q",w,43,30,yaw,14,1.2f,t*11);
        }
        return Base("Reviewed v13 retained");
    }
}
