using System.Text;
using ScheduleRoadmapBuilder.Core;
using Xunit;

namespace ScheduleRoadmapBuilder.Tests;

public sealed class StorageTests
{
    [Fact]
    public void JsonRoundTripPreservesIdsRenumbersAndKeepsOneBackup()
    {
        string path = TestFiles.NewPath(".srb.json");
        var major = new ScheduleItem { Kind = ItemKind.Major, Name = "設計" };
        var a = new ScheduleItem { Name = "設計A", ManualStart = new(2026, 11, 16) };
        var b = new ScheduleItem { Name = "設計B", Predecessors = [a.Id] };
        var project = new ScheduleProject { Items = [major, a, b] };
        ProjectStorage.Save(path, project);
        string original = File.ReadAllText(path);
        project.Items = [major, b, a];
        ProjectStorage.Save(path, project);
        Assert.Equal(original, File.ReadAllText(path + ".bak"));
        var loaded = ProjectStorage.Load(path);
        Assert.Equal(ProjectStorage.Serialize(project), ProjectStorage.Serialize(loaded));
        Assert.Equal(2, loaded.Numbers()[a.Id]);
        Assert.Equal(a.Id, loaded.Items[1].Predecessors.Single());
        Assert.DoesNotContain("\"no\"", original);
        Assert.DoesNotContain("\"finish\"", original);
        project.Name = "変更";
        ProjectStorage.Save(path, project);
        Assert.Equal("新規プロジェクト", ProjectStorage.Load(path + ".bak").Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HolidayFilesAcceptBothEncodingsFormatsAndDuplicates(bool shiftJis)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string path = TestFiles.NewPath(".csv");
        File.WriteAllText(path, "2026-11-23,祝日\n2027/1/1,元日\n2026/11/23,重複",
            shiftJis ? Encoding.GetEncoding(932) : new UTF8Encoding(true));
        var settings = new ProjectSettings();
        Assert.Equal(new HolidayImportResult(2, 1), HolidayImporter.Import(HolidayImporter.ReadFile(path), settings));
        Assert.All(settings.Calendar, h => Assert.Equal(HolidayKind.Holiday, h.Kind));
        Assert.Equal("元日", settings.Calendar[1].Name);
    }

    [Fact]
    public void InvalidHolidayInputDoesNotPartiallyImport()
    {
        var settings = new ProjectSettings();
        Assert.Throws<FormatException>(() => HolidayImporter.Import("2026-11-23,祝日\n不正", settings));
        Assert.Empty(settings.Calendar);
    }

    [Fact]
    public void SettingsCanMergeOrReplaceAndClearRemovedValues()
    {
        var p = new ScheduleProject { Items = [new() { Name = "設計", Assignee = "A", Status = "済" }] };
        var settings = new ProjectSettings { Statuses = [new() { Name = "独自" }], Assignees = [] };
        ProjectStorage.ImportSettings(p, settings, false);
        Assert.Equal(4, p.Settings.Statuses.Count);
        ProjectStorage.ImportSettings(p, settings, false);
        Assert.Equal(4, p.Settings.Statuses.Count);
        ProjectStorage.ImportSettings(p, settings, true);
        Assert.Null(p.Items[0].Assignee);
        Assert.Null(p.Items[0].Status);
    }

    [Fact]
    public void InvalidJsonAndPredecessorNumbersAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Deserialize("{\"schemaVersion\":2}"));
        var p = new ScheduleProject { Items = [new() { Name = "設計" }] };
        Assert.Throws<ArgumentException>(() => p.ParsePredecessors(p.Items[0], "99"));
        p.Items.Add(new() { Id = p.Items[0].Id });
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Deserialize(ProjectStorage.Serialize(p)));
    }
}

internal static class TestFiles
{
    public static string NewPath(string extension)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "_tmp_test_outputs");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid()}{extension}");
    }
}
