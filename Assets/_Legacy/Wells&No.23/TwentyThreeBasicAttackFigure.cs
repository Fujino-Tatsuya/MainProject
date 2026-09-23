using UnityEngine;

[CreateAssetMenu(fileName = "TwentyThreeBasicAttackFigure", menuName = "Scriptable Objects/TwentyThreeBasicAttackFigure")]
public class TwentyThreeBasicAttackFigure : ScriptableObject
{
    [Header("Hook Attack")]
    public float hookAttackMinDistance;
    public float hookAttackMaxDistance;
    public float hookAttackPercentage;

    [Header("Upper Attack")]
    public float upperAttackMinDistance;
    public float upperAttackMaxDistance;
    public float upperAttackPercentage;

    [Header("Grab Attack")]
    public float grabAttackMinDistance;
    public float grabAttackMaxDistance;
    public float grabAttackPercentage;

    [Header("Jump Attack")]
    public float jumpAttackMinDistance;
    public float jumpAttackMaxDistance;
    public float jumpAttackPercentage;

    [Header("Dash Attack")]
    public float dashAttackMinDistance;
    public float dashAttackMaxDistance;
    public float dashAttackPercentage;
}
