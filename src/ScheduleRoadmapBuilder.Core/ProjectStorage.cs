using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleRoadmapBuilder.Core;

public static class ProjectStorage
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ScheduleProject project) => JsonSerializer.Serialize(project, Options);

    public static ScheduleProject Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<ScheduleProject>(json, Options)
            ?? throw new InvalidDataException("プロジェクトが空です。");
        if (project.SchemaVersion != 1) throw new InvalidDataException("対応していないschemaVersionです。");
        if (project.Name is null || project.Items is null || project.Settings is null ||
            project.View is null || project.Ppt is null || project.Settings.Statuses is null ||
            project.Settings.Assignees is null || project.Settings.Calendar is null)
            throw new InvalidDataException("必須のプロジェクトデータがありません。");
        if (project.View.End < project.View.Start || !Enum.IsDefined(project.View.Mode) ||
            !Enum.IsDefined(project.Ppt.Mode) || !Enum.IsDefined(project.View.DisplayMode))
            throw new InvalidDataException("表示設定が不正です。");
        if (project.Items.Any(i => i is null || i.Id == Guid.Empty || i.Predecessors is null ||
            i.Name is null || i.Notes is null || !Enum.IsDefined(i.Kind)) ||
            project.Items.Select(i => i.Id).Distinct().Count() != project.Items.Count)
            throw new InvalidDataException("項目のIDまたは内容が不正です。");
        if (project.Settings.Statuses.Any(s => s is null || string.IsNullOrWhiteSpace(s.Name)) ||
            project.Settings.Assignees.Any(a => a is null || string.IsNullOrWhiteSpace(a.Name) || !ValidColor(a.Color)) ||
            project.Settings.Calendar.Any(h => h is null || h.Name is null || !Enum.IsDefined(h.Kind)) ||
            project.Settings.Statuses.Select(s => s.Name).Distinct().Count() != project.Settings.Statuses.Count ||
            project.Settings.Assignees.Select(a => a.Name).Distinct().Count() != project.Settings.Assignees.Count ||
            project.Settings.Calendar.Select(h => h.Date).Distinct().Count() != project.Settings.Calendar.Count)
            throw new InvalidDataException("設定に不正値または重複があります。");
        return project;
    }

    private static bool ValidColor(string? color) => color is { Length: 7 } && color[0] == '#' &&
        int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    public static ScheduleProject Load(string path) => Deserialize(File.ReadAllText(path, Encoding.UTF8));

    public static void Save(string path, ScheduleProject project)
    {
        string json = Serialize(project);
        Deserialize(json);
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }

    public static void ImportSettings(ScheduleProject target, ProjectSettings source, bool replace)
    {
        var copy = JsonSerializer.Deserialize<ProjectSettings>(JsonSerializer.Serialize(source, Options), Options)!;
        if (replace)
        {
            target.Settings = copy;
            foreach (var item in target.Items)
            {
                if (!copy.Statuses.Any(s => s.Name == item.Status)) item.Status = null;
                if (!copy.Assignees.Any(a => a.Name == item.Assignee)) item.Assignee = null;
            }
        }
        else
        {
            target.Settings.Statuses.AddRange(copy.Statuses.Where(s => !target.Settings.Statuses.Any(t => t.Name == s.Name)));
            target.Settings.Assignees.AddRange(copy.Assignees.Where(a => !target.Settings.Assignees.Any(t => t.Name == a.Name)));
            target.Settings.Calendar.AddRange(copy.Calendar.Where(h => !target.Settings.Calendar.Any(t => t.Date == h.Date)));
        }
    }
}

public sealed record HolidayImportResult(int Imported, int Skipped);

public static class HolidayImporter
{
    public static string ReadFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
        }
    }

    public static HolidayImportResult Import(string text, ProjectSettings settings)
    {
        var pending = new List<HolidaySetting>();
        var known = settings.Calendar.Select(h => h.Date).ToHashSet();
        int skipped = 0, lineNumber = 0;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            int comma = line.IndexOf(',');
            if (comma < 0 || !DateOnly.TryParseExact(line[..comma].Trim().TrimStart('\uFEFF'),
                ["yyyy-MM-dd", "yyyy/M/d"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                string.IsNullOrWhiteSpace(line[(comma + 1)..]))
                throw new FormatException($"祝日データの{lineNumber}行目は「yyyy-MM-dd,名称」または「yyyy/M/d,名称」で入力してください。");
            if (!known.Add(date)) { skipped++; continue; }
            pending.Add(new() { Date = date, Name = line[(comma + 1)..].Trim(), Kind = HolidayKind.Holiday });
        }
        settings.Calendar.AddRange(pending);
        return new(pending.Count, skipped);
    }
}
