using NUnit.Framework;

/// <summary>
/// 팔라딘 특성 EditMode 테스트(할 일 126 팔라딘 레인 전용 파일 — 할 일 125 5/5 가 자리만 만들었다).
/// 이름이 <c>Paladin</c>Trait…<c>Tests</c> 로 끝나는 픽스처는 맵씬 테스트 메뉴가 자동으로 돈다(<c>MapSceneEditModeTestRunner</c> 를 고치지 않는다).
/// 특성별 수용 조건(명세 11장)은 이 파일이나 같은 폴더의 <c>PaladinTrait…Tests.cs</c> 에 더한다.
/// </summary>
public class PaladinTraitTests
{
    [Test]
    public void Content_RegistryPoolAndCardTexts_Valid() =>
        TraitContentTestKit.AssertCharacterContent(TutorialCharacter.Paladin);
}
