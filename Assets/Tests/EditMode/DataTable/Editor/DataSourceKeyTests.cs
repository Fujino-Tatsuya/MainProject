using NUnit.Framework;

// 데이터 출처·Dev Boot 개인 설정 키. MPPM 클론이 메인과 다른 키를 쓰면 호스트=테이블, 클론=인스펙터처럼 어긋난다(PLAN R7).
public sealed class DataSourceKeyTests
{
    [Test]
    public void MppmClonePath_UsesMainProjectKey()
    {
        string main = DevBootTarget.ComputeWorkspaceKey("C:/UnityProject/MainProject/Assets");
        string clone = DevBootTarget.ComputeWorkspaceKey("C:/UnityProject/MainProject/Library/VP/mppm2bdbbfaf/Assets");

        Assert.That(clone, Is.EqualTo(main));
    }

    [Test]
    public void OtherWorktree_HasDifferentKey()
    {
        string main = DevBootTarget.ComputeWorkspaceKey("C:/UnityProject/MainProject/Assets");
        string other = DevBootTarget.ComputeWorkspaceKey("C:/UnityProject/MainProject-MLAgent/Assets");

        Assert.That(other, Is.Not.EqualTo(main));
    }

    [Test]
    public void BackslashesAndCase_DoNotMatter()
    {
        Assert.That(DevBootTarget.ComputeWorkspaceKey(@"C:\UnityProject\MainProject\Assets"),
            Is.EqualTo(DevBootTarget.ComputeWorkspaceKey("c:/unityproject/mainproject/assets")));
    }
}
