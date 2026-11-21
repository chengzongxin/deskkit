using System.Text.Json;
using DeskKit.Core;
using Xunit;

namespace DeskKit.Core.Tests;

public class FixtureTests
{
    readonly ContractStore store = new(ContractStore.Locate(AppContext.BaseDirectory) ?? throw new InvalidOperationException("找不到 contracts"));

    [Fact]
    public void HashAndGroupColor()
    {
        Assert.Equal(64, store.Hash.Length);
        Assert.Equal(store.Hash, ContractStore.HashTree(store.Root));
        Assert.Equal("#9B6CF6", store.GroupColors[3]);
        Assert.Equal(800, store.Layout("launcher", "width"));
        Assert.Equal(64, store.Layout("launcher", "inputRowHeight"));
    }

    [Fact]
    public void EscapeChains()
    {
        var fixture = store.Fixture("esc.json");
        foreach (var sequence in fixture.GetProperty("sequences").EnumerateArray())
        {
            var machine = Reach(sequence.GetProperty("from").GetString() ?? "");
            var expected = sequence.GetProperty("steps").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Equal(expected, machine.EscapeSequence());
        }
    }

    [Fact]
    public void HiddenCannotJump()
    {
        var machine = new LauncherStateMachine(store);
        var result = machine.Transition("A3");
        Assert.False(result.Ok);
        Assert.Equal("INVALID_ARG", result.Code);
        Assert.Equal("HIDDEN", machine.State);
    }

    [Fact]
    public void TriggersAndKeymap()
    {
        var triggers = store.Fixture("triggers.json");
        foreach (var item in triggers.GetProperty("cases").EnumerateArray())
        {
            Assert.Equal(item.GetProperty("toolId").GetString(), TriggerTable.Resolve(store, item.GetProperty("word").GetString() ?? ""));
        }

        foreach (var word in triggers.GetProperty("absent").EnumerateArray())
        {
            Assert.Null(TriggerTable.Resolve(store, word.GetString() ?? ""));
        }

        var keys = store.Fixture("keymap.json");
        foreach (var item in keys.GetProperty("cases").EnumerateArray())
        {
            Assert.Equal(item.GetProperty("display").GetString(), store.FormatChord(item.GetProperty("platform").GetString() ?? "", item.GetProperty("action").GetString() ?? ""));
        }
    }

    [Fact]
    public void CalculatorSearchAndGroups()
    {
        foreach (var item in store.Fixture("calc.json").GetProperty("cases").EnumerateArray())
        {
            var result = ExpressionEvaluator.Evaluate(item.GetProperty("expr").GetString() ?? "");
            if (item.TryGetProperty("error", out var error))
            {
                Assert.False(result.Ok);
                Assert.Equal(error.GetString(), result.Code);
            }
            else
            {
                Assert.True(result.Ok);
                Assert.Equal(item.GetProperty("value").GetString(), result.Data);
            }
        }

        var search = store.Fixture("search.json");
        var rows = search.GetProperty("items").EnumerateArray().Select(item => new IndexRow(
            item.GetProperty("id").GetString() ?? "",
            item.GetProperty("kind").GetString() ?? "",
            item.GetProperty("title").GetString() ?? "",
            item.GetProperty("keywords").EnumerateArray().Select(word => word.GetString() ?? "").ToArray())).ToArray();
        foreach (var item in search.GetProperty("cases").EnumerateArray())
        {
            var expect = item.GetProperty("expectIds").EnumerateArray().Select(id => id.GetString()).ToArray();
            Assert.Equal(expect, KeywordMatcher.Match(rows, item.GetProperty("query").GetString() ?? ""));
        }

        var groupsFixture = store.Fixture("group-delete.json");
        var groups = groupsFixture.GetProperty("document").GetProperty("groups").EnumerateArray().Select(item => new GroupRecord(
            item.GetProperty("id").GetString() ?? "",
            item.GetProperty("name").GetString() ?? "",
            item.GetProperty("color").GetString() ?? "",
            item.GetProperty("itemIds").EnumerateArray().Select(id => id.GetString() ?? ""))).ToList();
        var items = groupsFixture.GetProperty("document").GetProperty("items").EnumerateArray().Select(item => new ItemRecord(
            item.GetProperty("id").GetString() ?? "",
            item.GetProperty("title").GetString() ?? "")).ToList();
        var deleted = GroupEditor.Delete(groups, items, groupsFixture.GetProperty("deleteGroupId").GetString() ?? "", "ungrouped", "未分组");
        Assert.True(deleted.Ok);
        Assert.Equal(Strings(groupsFixture, "expectRemainingGroupIds"), groups.Select(group => group.Id).ToArray());
        Assert.Equal(Strings(groupsFixture, "expectItemIdsInUngrouped"), groups.First(group => group.Id == "ungrouped").ItemIds);
        Assert.Equal(Strings(groupsFixture, "expectItemIds"), items.Select(item => item.Id).ToArray());
        Assert.NotNull(ColorConvert.Describe("#3B6CF6"));
        Assert.Contains("rgb(59,108,246)", ColorConvert.Describe("#3B6CF6"), StringComparison.Ordinal);
    }

    [Fact]
    public void StarterSkipsMissingAndEmptyGroups()
    {
        var catalog = store.Starter();
        Assert.Equal(new[] { "#3B6CF6", "#18A058", "#F0A020", "#9B6CF6" }, catalog.Groups.Select(group => group.Color).ToArray());
        Assert.Equal("应用", catalog.Groups[3].Name);
        Assert.Equal("#98A1B0", catalog.Ungrouped.Color);
        Assert.Equal("未分组", catalog.Ungrouped.Name);

        var found = new[]
        {
            new StarterFound("files", "文件资源管理器", @"C:\Windows\explorer.exe", "system"),
            new StarterFound("notepad", "记事本", @"C:\Windows\System32\notepad.exe", "tools"),
            new StarterFound("snip", "截图工具", @"C:\Users\me\截图工具.lnk", "tools"),
            new StarterFound("gone", "不存在", "  ", "tools")
        };
        var built = StarterPack.Compose(catalog.Groups, found, catalog.Ungrouped);
        Assert.Equal(new[] { "system", "tools", "ungrouped" }, built.Groups.Select(group => group.Id).ToArray());
        Assert.Equal(new[] { "files" }, built.Groups[0].ItemIds);
        Assert.Equal(new[] { "notepad", "snip" }, built.Groups[1].ItemIds);
        Assert.Empty(built.Groups[2].ItemIds);
        Assert.Equal(new[] { "app", "app", "app" }, built.Items.Select(item => item.Kind).ToArray());
        Assert.Equal("截图工具", built.Items[2].Title);

        var empty = StarterPack.Compose(catalog.Groups, Array.Empty<StarterFound>(), catalog.Ungrouped);
        Assert.Equal(new[] { "ungrouped" }, empty.Groups.Select(group => group.Id).ToArray());
        Assert.Empty(empty.Items);
    }

    static string?[] Strings(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(item => item.GetString()).ToArray();

    LauncherStateMachine Reach(string state)
    {
        var machine = new LauncherStateMachine(store);
        if (state == "TOOLPAGE")
        {
            machine.Transition("TOOLPAGE");
            return machine;
        }

        machine.Transition("A1");
        machine.Transition("A2");
        machine.Transition(state);
        return machine;
    }
}
