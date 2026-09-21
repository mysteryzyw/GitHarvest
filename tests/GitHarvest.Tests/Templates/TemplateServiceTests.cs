using GitHarvest.Core.Export;
using GitHarvest.Core.Git;
using GitHarvest.Core.Settings;
using GitHarvest.Core.Templates;
using GitHarvest.Tests.Support;

namespace GitHarvest.Tests.Templates;

/// <summary>
/// 更新说明模板（ticket 10，spec 用户故事 36～39）：
/// 内置模板按当前范围生成说明草稿，12 个中文占位符全部渲染；
/// 设置中指定的自定义 .md 模板生效，丢失 / 读取失败回退内置并给出提示；
/// 未知占位符原样保留并警告。只断言服务的外部行为（加载结果、渲染文本、警告列表）。
/// </summary>
public sealed class TemplateServiceTests : IDisposable
{
    private static readonly Serilog.ILogger SilentLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    private readonly TestDirectory _directory = new();

    private string SettingsFilePath => Path.Combine(_directory.Path, "settings.json");

    private string RepositoryStateFilePath => Path.Combine(_directory.Path, "repository-state.json");

    [Fact]
    public async Task 未配置自定义模板时加载内置模板()
    {
        var service = CreateService();

        var loaded = await service.LoadTemplateAsync();

        Assert.False(loaded.IsCustom);
        Assert.Null(loaded.Notice);
        // 内置模板本身携带占位符（渲染发生在生成草稿时）。
        Assert.Contains("{更新日期}", loaded.Text, StringComparison.Ordinal);
        Assert.Contains("{变更统计}", loaded.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 内置模板渲染出全部范围要素与手写栏目()
    {
        var service = CreateService();

        var loaded = await service.LoadTemplateAsync();
        var rendered = service.Render(loaded.Text, Context(range: [MakeFile("a.cs", ChangeKind.Added)]));

        Assert.Empty(rendered.Warnings);
        Assert.Contains("# 更新说明", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("2026-09-20", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("main", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("a1b2c3d", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("chore(release): 2.4.0 版本冻结", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("e4f5g6h", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("feat(export): 支持更新说明模板占位符", rendered.Content, StringComparison.Ordinal);

        // 三个可手写栏目（用户故事 36）——留空让使用者填写，而不是省略小节。
        Assert.Contains("更新内容说明", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("操作步骤", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("注意事项", rendered.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 内置模板渲染后12个占位符无一残留()
    {
        var service = CreateService();
        var loaded = await service.LoadTemplateAsync();

        var rendered = service.Render(loaded.Text, Context(range: [MakeFile("a.cs", ChangeKind.Added)]));

        Assert.Empty(rendered.Warnings);
        foreach (var placeholder in NotesTemplateCatalog.Placeholders)
        {
            Assert.DoesNotContain($"{{{placeholder.Name}}}", rendered.Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 占位符目录恰好是规格约定的12个()
    {
        // spec 用户故事 37 的固定清单，顺序即工具栏「插入占位符」菜单的顺序。
        var names = NotesTemplateCatalog.Placeholders.Select(placeholder => placeholder.Name).ToArray();

        Assert.Equal(
            ["更新日期", "分支名", "基准哈希", "基准信息", "Head哈希", "Head信息",
                "新增清单", "删除清单", "修改清单", "重命名清单", "其他清单", "变更统计"],
            names);
    }

    [Fact]
    public void 每个占位符都渲染为对应范围值()
    {
        var service = CreateService();
        var context = Context(range: [MakeFile("src/新增.cs", ChangeKind.Added)]);

        Assert.Equal("2026-09-20", service.Render("{更新日期}", context).Content);
        Assert.Equal("main", service.Render("{分支名}", context).Content);
        Assert.Equal("a1b2c3d", service.Render("{基准哈希}", context).Content);
        Assert.Equal("chore(release): 2.4.0 版本冻结", service.Render("{基准信息}", context).Content);
        Assert.Equal("e4f5g6h", service.Render("{Head哈希}", context).Content);
        Assert.Equal("feat(export): 支持更新说明模板占位符", service.Render("{Head信息}", context).Content);
        Assert.Contains("src/新增.cs", service.Render("{新增清单}", context).Content, StringComparison.Ordinal);
        Assert.Contains("共 1 个文件", service.Render("{变更统计}", context).Content, StringComparison.Ordinal);
    }

    [Fact]
    public void 清单按类型分组且空组渲染为空()
    {
        var service = CreateService();
        var context = Context(range:
        [
            MakeFile("src/新增.cs", ChangeKind.Added),
            MakeFile("src/删除.cs", ChangeKind.Deleted),
            MakeFile("src/保持.cs", ChangeKind.Modified),
        ]);

        var rendered = service.Render("{新增清单}\n{删除清单}\n{修改清单}\n{重命名清单}\n{其他清单}", context);

        Assert.Contains("### 新增（1）", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("### 删除（1）", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("### 修改（1）", rendered.Content, StringComparison.Ordinal);
        // 重命名与其他两类一个文件都没有，不该出现空小节。
        Assert.DoesNotContain("### 重命名", rendered.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("### 其他", rendered.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void 变更统计给出总数文件数与占比()
    {
        var service = CreateService();
        var context = Context(range:
        [
            MakeFile("a1.cs", ChangeKind.Added),
            MakeFile("a2.cs", ChangeKind.Added),
            MakeFile("d.cs", ChangeKind.Deleted),
            MakeFile("m.cs", ChangeKind.Modified),
            MakeFile("m2.cs", ChangeKind.Modified),
        ]);

        var statistics = service.Render("{变更统计}", context).Content;

        // 共 5 个文件：新增 2（40%）、删除 1（20%）、修改 2（40%）。
        Assert.Contains("共 5 个文件", statistics, StringComparison.Ordinal);
        Assert.Contains("| 新增 | 2 | 40% |", statistics, StringComparison.Ordinal);
        Assert.Contains("| 删除 | 1 | 20% |", statistics, StringComparison.Ordinal);
        Assert.Contains("| 修改 | 2 | 40% |", statistics, StringComparison.Ordinal);
    }

    [Fact]
    public void 重命名清单列出旧路径到新路径与相似度()
    {
        var service = CreateService();
        var context = Context(range:
        [
            new ChangedFile(
                "src/新名.cs",
                "src/旧名.cs",
                ChangeKind.Renamed,
                OtherChangeReason.None,
                Additions: 1,
                Deletions: 1,
                SimilarityPercent: 92,
                SizeBytes: 10,
                OldBlobHash: "b0",
                NewBlobHash: "b1"),
        ]);

        var list = service.Render("{重命名清单}", context).Content;

        Assert.Contains("src/旧名.cs", list, StringComparison.Ordinal);
        Assert.Contains("src/新名.cs", list, StringComparison.Ordinal);
        Assert.Contains("92%", list, StringComparison.Ordinal);
    }

    [Fact]
    public void 其他清单标注具体原因()
    {
        var service = CreateService();
        var context = Context(range:
        [
            new ChangedFile("vendor/lib", null, ChangeKind.Other, OtherChangeReason.SubmodulePointer, 1, 1, null, null, "b0", "b1"),
            new ChangedFile("link.txt", null, ChangeKind.Other, OtherChangeReason.TypeChange, 1, 1, null, 10, "b0", "b1"),
        ]);

        var list = service.Render("{其他清单}", context).Content;

        Assert.Contains("子模块指针", list, StringComparison.Ordinal);
        Assert.Contains("类型变更", list, StringComparison.Ordinal);
    }

    [Fact]
    public void 二进制文件在清单里标注()
    {
        var service = CreateService();
        var context = Context(range:
        [
            // 二进制：git numstat 两侧都以 "-" 占位（行数为空）。
            new ChangedFile("logo.bin", null, ChangeKind.Added, OtherChangeReason.None, null, null, null, 11, null, "b1"),
        ]);

        Assert.Contains("二进制", service.Render("{新增清单}", context).Content, StringComparison.Ordinal);
    }

    [Fact]
    public void 二进制重命名同样标注二进制()
    {
        // 用户故事 29 的组合场景：重命名的二进制文件既要有「旧 → 新」与相似度，
        // 也要标注「二进制」——两个标注并存，不能让相似度把「二进制」挤掉。
        var service = CreateService();
        var context = Context(range:
        [
            new ChangedFile(
                "assets/logo-new.bin",
                "assets/logo-old.bin",
                ChangeKind.Renamed,
                OtherChangeReason.None,
                Additions: null,
                Deletions: null,
                SimilarityPercent: 88,
                SizeBytes: 11,
                OldBlobHash: "b0",
                NewBlobHash: "b1"),
        ]);

        var list = service.Render("{重命名清单}", context).Content;

        Assert.Contains("88%", list, StringComparison.Ordinal);
        Assert.Contains("二进制", list, StringComparison.Ordinal);
    }

    [Fact]
    public void 更新日期取实际使用的目录名()
    {
        // 目录名被重名让位成带时分的形态时，说明里的更新日期与实际目录一致。
        var service = CreateService();

        var rendered = service.Render("{更新日期}", Context(range: [MakeFile("a.cs", ChangeKind.Added)], folderName: "2026-09-20_134500"));

        Assert.Equal("2026-09-20_134500", rendered.Content);
    }

    [Fact]
    public async Task 自定义模板路径生效()
    {
        var templatePath = Path.Combine(_directory.Path, "我的模板.md");
        await File.WriteAllTextAsync(templatePath, "# 交付说明\n分支：{分支名}\n{新增清单}");
        var service = CreateService(templatePath);

        var loaded = await service.LoadTemplateAsync();

        Assert.True(loaded.IsCustom);
        Assert.Null(loaded.Notice);
        Assert.Contains("# 交付说明", loaded.Text, StringComparison.Ordinal);

        var rendered = service.Render(loaded.Text, Context(range: [MakeFile("src/新增.cs", ChangeKind.Added)]));
        Assert.Contains("分支：main", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("src/新增.cs", rendered.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 自定义模板丢失时回退内置并提示()
    {
        var missingPath = Path.Combine(_directory.Path, "不存在的模板.md");
        var service = CreateService(missingPath);

        var loaded = await service.LoadTemplateAsync();

        Assert.False(loaded.IsCustom);
        Assert.NotNull(loaded.Notice);
        Assert.Contains(missingPath, loaded.Notice, StringComparison.Ordinal);
        // 回退到内置模板：草稿照常生成，导出不会因模板问题失败（用户故事 38）。
        Assert.Contains("{更新日期}", loaded.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void 模板文件存在时可用性判定为真()
    {
        var templatePath = Path.Combine(_directory.Path, "存在的模板.md");
        File.WriteAllText(templatePath, "# 模板");
        var service = CreateService(templatePath);

        Assert.True(service.IsTemplateFileAvailable(templatePath));
    }

    [Fact]
    public void 模板文件缺失或路径为空时可用性判定为假()
    {
        var missingPath = Path.Combine(_directory.Path, "已被移走的模板.md");
        var service = CreateService(missingPath);

        Assert.False(service.IsTemplateFileAvailable(missingPath));
        // 未配置路径时同样不可用（按钮置灰的另一半口径）。
        Assert.False(service.IsTemplateFileAvailable(string.Empty));
    }

    [Fact]
    public void 可用性判定对目录与非法路径给假而不抛异常()
    {
        // 路径指向目录、或含非法字符（如手改 settings.json 写坏）：File.Exists 都返回 false，
        // 可用性判定要的是「永远给结论」，绝不把异常抛给设置页。
        var directoryAsTemplate = Path.Combine(_directory.Path, "模板目录");
        Directory.CreateDirectory(directoryAsTemplate);
        var service = CreateService(directoryAsTemplate);

        Assert.False(service.IsTemplateFileAvailable(directoryAsTemplate));
        Assert.False(service.IsTemplateFileAvailable(@"D:\模板\非法|字符.md"));
    }

    [Fact]
    public async Task 自定义模板读取失败时回退内置并提示()
    {
        // 路径指向一个目录而不是文件：ReadAllText 抛 IOException，与「丢失」同属回退分支。
        var directoryAsTemplate = Path.Combine(_directory.Path, "模板目录");
        Directory.CreateDirectory(directoryAsTemplate);
        var service = CreateService(directoryAsTemplate);

        var loaded = await service.LoadTemplateAsync();

        Assert.False(loaded.IsCustom);
        Assert.NotNull(loaded.Notice);
        Assert.Contains("{更新日期}", loaded.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 自定义模板路径含非法字符时回退内置并提示()
    {
        // 手改 settings.json 把路径写成含 | 的形态：.NET 抛 NotSupportedException（不是 IOException），
        // 同属「模板读不到」——必须回退，绝不让模板问题导致导出失败（用户故事 38）。
        var service = CreateService(@"D:\模板\非法|字符.md");

        var loaded = await service.LoadTemplateAsync();

        Assert.False(loaded.IsCustom);
        Assert.NotNull(loaded.Notice);
        Assert.Contains("{更新日期}", loaded.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void 未知占位符原样保留并警告()
    {
        var service = CreateService();
        var context = Context(range: [MakeFile("a.cs", ChangeKind.Added)]);

        var rendered = service.Render("日期：{更新日期}，笔误：{更新曰期}、{仓库名称}", context);

        Assert.Contains("{更新曰期}", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("{仓库名称}", rendered.Content, StringComparison.Ordinal);
        Assert.Contains("2026-09-20", rendered.Content, StringComparison.Ordinal);
        Assert.Equal(2, rendered.Warnings.Count);
        Assert.Contains(rendered.Warnings, warning => warning.Contains("更新曰期", StringComparison.Ordinal));
        Assert.Contains(rendered.Warnings, warning => warning.Contains("仓库名称", StringComparison.Ordinal));
    }

    [Fact]
    public void 同一未知占位符重复出现只警告一次()
    {
        var service = CreateService();
        var context = Context(range: [MakeFile("a.cs", ChangeKind.Added)]);

        var rendered = service.Render("{错别字}\n{错别字}", context);

        Assert.Single(rendered.Warnings);
        // 两处都原样保留（保留的是文本，警告按占位符名去重）。
        Assert.Equal("{错别字}\n{错别字}", rendered.Content);
    }

    [Fact]
    public void 已知占位符不产生警告()
    {
        var service = CreateService();
        var context = Context(range: [MakeFile("a.cs", ChangeKind.Added)]);

        var rendered = service.Render("{更新日期}{分支名}{基准哈希}{Head哈希}{变更统计}", context);

        Assert.Empty(rendered.Warnings);
    }

    public void Dispose() => _directory.Dispose();

    /// <summary>构造模板服务；<paramref name="templatePath"/> 非空时写入全局设置的默认说明模板路径。</summary>
    private TemplateService CreateService(string? templatePath = null)
    {
        var settings = new SettingsService(SettingsFilePath, RepositoryStateFilePath, SilentLogger);
        if (templatePath is not null)
        {
            settings.Save(settings.Settings with { DefaultTemplatePath = templatePath });
        }

        return new TemplateService(settings, SilentLogger);
    }

    private static NotesTemplateContext Context(
        IReadOnlyList<ChangedFile> range,
        string folderName = "2026-09-20")
        => NotesTemplateContext.From(
            new ExportRequest(
                RepositoryPath: @"D:\Code\DemoService",
                OutputRootPath: @"D:\交付物\DemoService",
                FolderName: folderName,
                RequestedAt: new DateTimeOffset(2026, 9, 20, 13, 45, 0, TimeSpan.FromHours(8)),
                BranchName: "main",
                Base: new CommitSummary("a1b2c3d", "chore(release): 2.4.0 版本冻结", "王磊", DateTimeOffset.Now),
                Head: new CommitSummary("e4f5g6h", "feat(export): 支持更新说明模板占位符", "张伟", DateTimeOffset.Now)),
            new ChangeRangeSummary(range));

    private static ChangedFile MakeFile(string path, ChangeKind kind)
        => new(path, null, kind, OtherChangeReason.None, Additions: 1, Deletions: 0, SimilarityPercent: null, SizeBytes: 10, "b0", "b1");
}
