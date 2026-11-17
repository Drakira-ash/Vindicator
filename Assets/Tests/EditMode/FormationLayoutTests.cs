using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 纯编队算法测试：列数、槽位方向、锚点居中与列表复用。
/// </summary>
public class FormationLayoutTests
{
    [Test]
    public void Flatten_DropsHeightAndFallsBackWhenZero()
    {
        var flat = FormationLayout.Flatten(new Vector3(0f, 5f, 2f));
        Assert.AreEqual(0f, flat.y, 0.0001f);
        Assert.AreEqual(1f, flat.magnitude, 0.0001f);
        Assert.AreEqual(0f, flat.x, 0.0001f);

        Assert.AreEqual(Vector3.forward, FormationLayout.Flatten(Vector3.zero));
    }

    [Test]
    public void ColumnsFor_MatchesEachFormation()
    {
        Assert.AreEqual(5, FormationLayout.ColumnsFor(5, FormationType.Line));
        Assert.AreEqual(1, FormationLayout.ColumnsFor(5, FormationType.Column));
        Assert.AreEqual(3, FormationLayout.ColumnsFor(5, FormationType.Grid));
        Assert.AreEqual(1, FormationLayout.ColumnsFor(0, FormationType.Grid));
    }

    [Test]
    public void GridFour_CentersSlotsAroundAnchor()
    {
        var slots = new List<Vector3>();
        FormationLayout.FillSlots(
            new Vector3(10f, 0f, 10f), Vector3.forward, 4, FormationType.Grid, 2f, slots);

        Assert.AreEqual(4, slots.Count);
        Assert.AreEqual(new Vector3(9f, 0f, 11f), slots[0]);
        Assert.AreEqual(new Vector3(11f, 0f, 11f), slots[1]);
        Assert.AreEqual(new Vector3(9f, 0f, 9f), slots[2]);
        Assert.AreEqual(new Vector3(11f, 0f, 9f), slots[3]);
    }

    [Test]
    public void Line_PutsEveryoneOnOneRow()
    {
        var slots = new List<Vector3>();
        FormationLayout.FillSlots(Vector3.zero, Vector3.forward, 3, FormationType.Line, 2f, slots);

        Assert.AreEqual(new Vector3(-2f, 0f, 0f), slots[0]);
        Assert.AreEqual(Vector3.zero, slots[1]);
        Assert.AreEqual(new Vector3(2f, 0f, 0f), slots[2]);
    }

    [Test]
    public void Column_PutsEveryoneInOneFile_FacingForward()
    {
        var slots = new List<Vector3>();
        FormationLayout.FillSlots(Vector3.zero, Vector3.forward, 3, FormationType.Column, 2f, slots);

        Assert.AreEqual(new Vector3(0f, 0f, 2f), slots[0], "第一排应在朝向前方");
        Assert.AreEqual(Vector3.zero, slots[1]);
        Assert.AreEqual(new Vector3(0f, 0f, -2f), slots[2]);
    }

    [Test]
    public void GetSlot_OutOfRangeFallsBackToAnchor()
    {
        Assert.AreEqual(
            new Vector3(5f, 0f, 5f),
            FormationLayout.GetSlot(new Vector3(5f, 0f, 5f), Vector3.forward, 2, FormationType.Grid, 2f, 9));
    }

    [Test]
    public void FillSlots_ReusesAndClearsList()
    {
        var slots = new List<Vector3> { Vector3.one, Vector3.one, Vector3.one };
        FormationLayout.FillSlots(Vector3.zero, Vector3.back, 2, FormationType.Grid, 2f, slots);

        Assert.AreEqual(2, slots.Count);
        Assert.AreEqual(new Vector3(1f, 0f, 0f), slots[0], "朝向 -Z 时右方向翻转，槽位镜像");
        Assert.AreEqual(new Vector3(-1f, 0f, 0f), slots[1]);
    }

    [Test]
    public void FillLocalOffsets_GridFour_MatchesWorldSlots()
    {
        var offsets = new List<Vector2>();
        FormationLayout.FillLocalOffsets(4, FormationType.Grid, 2f, offsets);

        Assert.AreEqual(4, offsets.Count);
        Assert.AreEqual(new Vector2(-1f, 1f), offsets[0]);
        Assert.AreEqual(new Vector2(1f, 1f), offsets[1]);
        Assert.AreEqual(new Vector2(-1f, -1f), offsets[2]);
        Assert.AreEqual(new Vector2(1f, -1f), offsets[3]);

        var slots = new List<Vector3>();
        Vector3 anchor = new Vector3(10f, 0f, 10f);
        FormationLayout.FillSlots(anchor, Vector3.forward, 4, FormationType.Grid, 2f, slots);
        for (int i = 0; i < offsets.Count; i++)
        {
            Assert.AreEqual(anchor.x + offsets[i].x, slots[i].x, 0.0001f);
            Assert.AreEqual(anchor.z + offsets[i].y, slots[i].z, 0.0001f);
        }
    }

    [Test]
    public void FillSlots_TranslatesWithAnchor()
    {
        var before = new List<Vector3>();
        var after = new List<Vector3>();
        var anchor = new Vector3(3f, 0f, -2f);
        var shift = new Vector3(5f, 0f, 7f);

        FormationLayout.FillSlots(anchor, Vector3.forward, 4, FormationType.Grid, 1.6f, before);
        FormationLayout.FillSlots(anchor + shift, Vector3.forward, 4, FormationType.Grid, 1.6f, after);

        for (int i = 0; i < before.Count; i++)
        {
            Assert.AreEqual(before[i] + shift, after[i], "锚点平移时槽位应等量平移");
        }
    }

    [Test]
    public void FillSlots_RotatesAroundAnchor()
    {
        var forward = new List<Vector3>();
        var right = new List<Vector3>();
        var anchor = new Vector3(1f, 0f, 1f);

        FormationLayout.FillSlots(anchor, Vector3.forward, 4, FormationType.Grid, 2f, forward);
        FormationLayout.FillSlots(anchor, Vector3.right, 4, FormationType.Grid, 2f, right);

        for (int i = 0; i < forward.Count; i++)
        {
            Vector3 before = forward[i] - anchor;
            Vector3 after = right[i] - anchor;
            Assert.AreEqual(before.magnitude, after.magnitude, 0.0001f, "旋转不改变到锚点距离");
        }

        Assert.AreEqual(new Vector3(2f, 0f, 2f), right[0], "朝 +X 时第一排前排应在 +X");
    }

    [Test]
    public void FillLocalOffsets_ReusesAndClearsList()
    {
        var offsets = new List<Vector2> { Vector2.one, Vector2.one, Vector2.one };
        FormationLayout.FillLocalOffsets(2, FormationType.Grid, 2f, offsets);

        Assert.AreEqual(2, offsets.Count);
        Assert.AreEqual(new Vector2(-1f, 0f), offsets[0]);
        Assert.AreEqual(new Vector2(1f, 0f), offsets[1]);
    }
}
