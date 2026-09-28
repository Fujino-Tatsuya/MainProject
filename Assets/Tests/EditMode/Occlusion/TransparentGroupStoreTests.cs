// 투명화 그룹 툴 — 데이터 계층 테스트.
// PLAN-transparent-group-tool.md §7: 자동 테스트는 JSON 직렬화/역직렬화와 참조 폴백 로직에만 붙인다.
// 렌더링과 실제 오브젝트 해석은 에디터 상태에 묶여 있어 수동 확인 항목이다.

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VeyTrace.Rendering.Occlusion.Editor;

namespace VeyTrace.Rendering.Occlusion.Tests
{
    public class TransparentGroupStoreTests
    {
        // ── 파일명 규칙 (PLAN 결정 6) ────────────────────────────────────

        [Test]
        public void MakeFileName_CombinesNameAndShortGuid()
        {
            var fileName = TransparentGroupLogic.MakeFileName("4.MapScene", "a1b2c3d4e5f6000000000000");
            Assert.AreEqual("4_MapScene.a1b2c3d4.json", fileName);
        }

        [Test]
        public void ExtractShortGuid_RoundTripsWithMakeFileName()
        {
            const string guid = "0123456789abcdef0123456789abcdef";
            var fileName = TransparentGroupLogic.MakeFileName("ZoneL_typeA", guid);

            Assert.AreEqual(TransparentGroupLogic.ShortGuid(guid),
                TransparentGroupLogic.ExtractShortGuid(fileName));
        }

        [Test]
        public void MakeFileName_StripsDotsFromContextName()
        {
            // 이름의 '.' 을 남기면 GUID 구분자와 헷갈려 ExtractShortGuid 가 이름 조각을 집는다.
            var fileName = TransparentGroupLogic.MakeFileName("a.b.c", "deadbeefcafe0000");
            Assert.AreEqual("a_b_c.deadbeef.json", fileName);
            Assert.AreEqual("deadbeef", TransparentGroupLogic.ExtractShortGuid(fileName));
        }

        [Test]
        public void ExtractShortGuid_ReturnsNullForNonJsonOrMalformed()
        {
            Assert.IsNull(TransparentGroupLogic.ExtractShortGuid("scene.a1b2c3d4.txt"));
            Assert.IsNull(TransparentGroupLogic.ExtractShortGuid("noguid.json"));
            Assert.IsNull(TransparentGroupLogic.ExtractShortGuid(null));
        }

        // ── 폴백 선택 (PLAN 결정 9·10·11) ───────────────────────────────

        [Test]
        public void ChooseStrategy_PrefersGlobalObjectId()
        {
            var member = new GroupMemberData { globalObjectId = "GlobalObjectId_V1-2-x-1-0", path = "Root/Wall" };
            Assert.AreEqual(MemberLookupStrategy.GlobalObjectId, TransparentGroupLogic.ChooseStrategy(member));
        }

        [Test]
        public void ChooseStrategy_FallsBackToPathWhenIdMissing()
        {
            var member = new GroupMemberData { path = "Root/Wall" };
            Assert.AreEqual(MemberLookupStrategy.PathOnly, TransparentGroupLogic.ChooseStrategy(member));
        }

        [Test]
        public void ChooseStrategy_IsIdenticalForSceneAndPrefabMembers()
        {
            // 2026-09-28 R7 프로브 결과 프리팹 편집 모드에서도 GlobalObjectId 가 프리팹 에셋을 참조하고
            // 왕복 복원된다. 그래서 프리팹 전용 키(fileId) 분기를 없앴다. 이 테스트가 그 통일을 고정한다.
            var sceneMember = new GroupMemberData
            {
                globalObjectId = "GlobalObjectId_V1-2-0123456789abcdef0123456789abcdef-111-0",
                path = "WallGroup/Cube",
            };
            var prefabMember = new GroupMemberData
            {
                globalObjectId = "GlobalObjectId_V1-2-abf0f7b102c958b488d9c99a38d4201b-111-222",
                path = "Zone Layout/wall_basic_035",
            };

            Assert.AreEqual(TransparentGroupLogic.ChooseStrategy(sceneMember),
                TransparentGroupLogic.ChooseStrategy(prefabMember));
            Assert.AreEqual(MemberLookupStrategy.GlobalObjectId,
                TransparentGroupLogic.ChooseStrategy(prefabMember));
        }

        // ── 신원 키 (2026-09-28 버그) ───────────────────────────────────

        [Test]
        public void IdentityKey_DistinguishesObjectsThatShareAPath()
        {
            // 형제 이름 중복은 Unity 가 허용하고 중첩 프리팹에서 흔하다. 경로로 신원을 판단하면
            // 뒤에 칠한 것이 조용히 버려지고, 하나를 빼면 엉뚱한 것이 빠진다. 실제로 겪은 증상이다.
            var a = new GroupMemberData
            {
                globalObjectId = "GlobalObjectId_V1-2-guid-111-0",
                path = "Floor/floor_hallway_003/floor_stone (2)",
            };
            var b = new GroupMemberData
            {
                globalObjectId = "GlobalObjectId_V1-2-guid-222-0",
                path = "Floor/floor_hallway_003/floor_stone (2)",   // 경로가 같다
            };

            Assert.AreEqual(a.path, b.path, "전제: 경로가 겹치는 상황이다");
            Assert.AreNotEqual(TransparentGroupLogic.IdentityKey(a), TransparentGroupLogic.IdentityKey(b),
                "경로가 같아도 신원은 달라야 한다");
        }

        [Test]
        public void IdentityKey_FallsBackToPathWhenIdMissing()
        {
            var member = new GroupMemberData { path = "Root/Wall" };
            Assert.AreEqual("path:Root/Wall", TransparentGroupLogic.IdentityKey(member));
        }

        [Test]
        public void IdentityKey_PathFallbackCannotCollideWithGlobalObjectId()
        {
            // 두 키 공간이 섞이면 "path:..." 라는 이름의 GlobalObjectId 가 있을 때 충돌한다.
            var byId = new GroupMemberData { globalObjectId = "path:Root/Wall" };
            var byPath = new GroupMemberData { path = "Root/Wall" };
            Assert.AreNotEqual(TransparentGroupLogic.IdentityKey(byId), TransparentGroupLogic.IdentityKey(byPath));
        }

        [Test]
        public void IdentityKey_NullWhenNothingToGoOn()
        {
            Assert.IsNull(TransparentGroupLogic.IdentityKey(null));
            Assert.IsNull(TransparentGroupLogic.IdentityKey(new GroupMemberData()));
        }

        [Test]
        public void ChooseStrategy_UnusableWhenNoKeyAtAll()
        {
            Assert.AreEqual(MemberLookupStrategy.Unusable,
                TransparentGroupLogic.ChooseStrategy(new GroupMemberData()));
            Assert.AreEqual(MemberLookupStrategy.Unusable,
                TransparentGroupLogic.ChooseStrategy(null));
        }

        // ── JSON 왕복 ───────────────────────────────────────────────────

        [Test]
        public void GroupFileData_SurvivesJsonRoundTrip()
        {
            var original = new GroupFileData
            {
                version = GroupFileData.CurrentVersion,
                contextKind = GroupContextKind.Scene.ToString(),
                contextGuid = "0123456789abcdef0123456789abcdef",
                contextName = "TransparentV3",
                groups = new List<GroupData>
                {
                    new GroupData
                    {
                        id = "abc123",
                        name = "복도 서측 벽",
                        color = new Color(0.1f, 0.2f, 0.3f, 0.5f),
                        colorIsCustom = true,
                        members = new List<GroupMemberData>
                        {
                            new GroupMemberData
                            {
                                globalObjectId = "GlobalObjectId_V1-2-guid-123-0",
                                path = "WallGroup/Cube",
                                lastSeenName = "Cube",
                            },
                        },
                    },
                },
            };

            var restored = JsonUtility.FromJson<GroupFileData>(JsonUtility.ToJson(original));

            Assert.AreEqual(original.version, restored.version);
            Assert.AreEqual(original.contextGuid, restored.contextGuid);
            Assert.AreEqual(original.contextName, restored.contextName);
            Assert.AreEqual(1, restored.groups.Count);

            var group = restored.groups[0];
            Assert.AreEqual("abc123", group.id);
            Assert.AreEqual("복도 서측 벽", group.name, "한글 그룹 이름이 깨지면 안 된다");
            Assert.IsTrue(group.colorIsCustom);
            Assert.AreEqual(original.groups[0].color, group.color);

            Assert.AreEqual(1, group.members.Count);
            Assert.AreEqual("GlobalObjectId_V1-2-guid-123-0", group.members[0].globalObjectId);
            Assert.AreEqual("WallGroup/Cube", group.members[0].path);
            Assert.AreEqual("Cube", group.members[0].lastSeenName);
        }

        [Test]
        public void GroupFileData_EmptyGroupListRoundTrips()
        {
            var restored = JsonUtility.FromJson<GroupFileData>(JsonUtility.ToJson(new GroupFileData()));
            Assert.IsNotNull(restored.groups);
            Assert.AreEqual(0, restored.groups.Count);
        }

        // ── 팔레트 (PLAN 결정 13) ───────────────────────────────────────

        [Test]
        public void Palette_IsStablePerIndex()
        {
            Assert.AreEqual(TransparentGroupPalette.GetColor(7), TransparentGroupPalette.GetColor(7));
        }

        [Test]
        public void Palette_GivesDistinctColorsToAdjacentIndices()
        {
            for (var i = 0; i < 16; i++)
            {
                var a = TransparentGroupPalette.GetColor(i);
                var b = TransparentGroupPalette.GetColor(i + 1);
                var distance = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
                Assert.Greater(distance, 0.3f, $"인덱스 {i} 와 {i + 1} 의 색이 너무 가깝다");
            }
        }

        [Test]
        public void Palette_HandlesNegativeIndex()
        {
            var color = TransparentGroupPalette.GetColor(-5);
            Assert.IsTrue(color.r >= 0f && color.g >= 0f && color.b >= 0f);
        }
    }
}
