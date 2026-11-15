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
}
