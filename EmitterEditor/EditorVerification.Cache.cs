using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>静态画布缓存的内容、引用、文件与资源失效验证，不启动或复用战斗实例。</summary>
public partial class EditorVerification
{
    /// <summary>验证静态结果复用、完整依赖失效及显示数组隔离。</summary>
    private void VerifyLayoutCache()
    {
        // 所有文件只写入忽略目录，正式资源和目录保持不变。
        const string sourceResource = "res://.tools/cache-opened.json", externalResource = "res://.tools/cache-external.json";
        const string textureResource = "res://.tools/cache-texture.tres", gradientResource = "res://.tools/cache-gradient.tres";
        string sourcePath = ProjectSettings.GlobalizePath(sourceResource), externalPath = ProjectSettings.GlobalizePath(externalResource);
        string texturePath = ProjectSettings.GlobalizePath(textureResource), gradientPath = ProjectSettings.GlobalizePath(gradientResource);
        _temporary.AddRange(new[] { sourcePath, externalPath, texturePath, gradientPath });
        var fixture = new EmitterDocument(); fixture.Save(sourcePath); fixture.Save(externalPath);
        var session = new EditorSession(); var opened = session.OpenEmitter(sourceResource);
        session.Catalog.Edit(root =>
        {
            // 三类移动显示和重复引用覆盖阶段切换，全部仍走正式Boss协议。
            var boss = root["Bosses"]![0]!; var phases = boss["Phases"]!.AsArray(); phases.Clear();
            foreach (string mode in new[] { "RandomCircle", "RandomRect", "Path" })
            {
                var phase = BossEditorSchema.Phase(); phase["Movement"] = BossEditorSchema.Movement(mode); phases.Add(phase);
            }
            phases[0]!["Emitters"] = new JsonArray(sourceResource, sourceResource, externalResource);
            phases[1]!["Emitters"] = new JsonArray(sourceResource);
            BossEditorSchema.SumHealth(boss.AsObject());
        });
        var cache = new EditorLayoutCache(); var canvas = new EditorCanvas { Visible = false }; AddChild(canvas);
        try
        {
            VMath.setRandomSeed(91); double expected = VMath.getRandomDouble(0, 1); VMath.setRandomSeed(91);
            cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == 1 && canvas.Paths.All(path => path.Points.Length == 65), "首次Boss布局使用正式移动校验及圆周采样");
            cache.ApplyBoss(session.Catalog, 0, 1, session, canvas);
            Check(cache.BuildCount == 1 && canvas.Paths.Single().Points.Length == 5, "仅切换阶段复用完整Boss及引用校验");
            cache.ApplyBoss(session.Catalog, 0, 2, session, canvas);
            Check(cache.BuildCount == 1 && canvas.Paths.Single().Points.Length == 1025, "路径阶段按原密度采样且不重载Emitter");
            // 画布公开列表可以修改，但不能污染缓存中的下一次显示。
            Vector2 firstPoint = canvas.Paths[0].Points[0];
            canvas.Paths[0].Points[0] += Vector2.One * 50; canvas.Markers.Clear();
            cache.ApplyBoss(session.Catalog, 0, 2, session, canvas);
            Check(canvas.Paths[0].Points[0] == firstPoint && canvas.Markers.Count > 0 && cache.BuildCount == 1, "画布数组不反向污染冻结布局");
            cache.ApplyBoss(session.Catalog, 0, -1, session, canvas);
            Check(canvas.Paths.Count == 0 && canvas.Markers.Count == 1 && cache.BuildCount == 1, "Boss共用属性选择复用校验且只显示出生点");
            Check(VMath.getRandomDouble(0, 1) == expected, "静态构建及命中缓存均不消耗随机");
            // 缓存不强持有资源；回收后的同内容资源重新加载不应使静态校验失效。
            GC.Collect(); GC.WaitForPendingFinalizers();
            cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == 1, "资源回收不改变内容及尺寸时仍可复用静态结果");

            opened.Edit(root => root["Core"]!["Damage"] = 2);
            cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == 2, "已打开Emitter内容修订使Boss布局失效");
            opened.Undo(); cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == 3, "撤销引用文档仍按新修订重新检查");
            opened.Draft = "{";
            Reject(() => cache.ApplyBoss(session.Catalog, 0, 0, session, canvas), "引用新增草稿不能沿用旧成功状态");
            opened.Draft = null; cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == 3, "放弃草稿且已应用内容未变时恢复有效结果");
            var unrelated = session.NewEmitter(); unrelated.Draft = "{";
            cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == 3, "未引用文档的草稿不误伤当前Boss布局");

            // 同长度同更新时间的外部改动必须被发现，不能仅使用文件元数据判断。
            string external = File.ReadAllText(externalPath); DateTime modified = File.GetLastWriteTimeUtc(externalPath);
            string changed = external.Replace("\"Damage\": 1", "\"Damage\": 2");
            Check(changed != external && changed.Length == external.Length, "外部文件夹具保持相同长度");
            File.WriteAllText(externalPath, changed); File.SetLastWriteTimeUtc(externalPath, modified);
            cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == 4, "外部JSON实际文本变化使缓存失效");
            File.WriteAllText(externalPath, external.Replace("\"Damage\": 1", "\"Damage\": 0"));
            Reject(() => cache.ApplyBoss(session.Catalog, 0, 0, session, canvas), "外部引用变为非法内容时必须重新报错");
            File.Delete(externalPath);
            Reject(() => cache.ApplyBoss(session.Catalog, 0, 0, session, canvas), "删除外部引用不能复用旧校验");
            File.WriteAllText(externalPath, external); cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == 5, "修复外部引用后恢复正常布局");
            session.Catalog.Edit(root => root["Bosses"]![0]!["Phases"]![0]!["Unknown"] = 1);
            Reject(() => cache.ApplyBoss(session.Catalog, 0, 0, session, canvas), "目录修订继续拒绝未知字段");
            session.Catalog.Undo(); cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == 6, "目录撤销之后重新校验正确内容");

            // 独立Emitter缓存不外借路径数组，也不能跨文档身份复用。
            var emitterCache = new EditorLayoutCache();
            fixture.New(); fixture.Edit(root => root["VNodes"] = EditorSchema.Creator("VPath"));
            emitterCache.ApplyEmitter(fixture, canvas); firstPoint = canvas.Paths[0].Points[0];
            canvas.Paths[0].Points[0] += Vector2.One * 90; emitterCache.ApplyEmitter(fixture, canvas);
            Check(emitterCache.BuildCount == 1 && canvas.Paths[0].Points[0] == firstPoint, "Emitter布局命中缓存仍给画布独立折线");
            fixture.Edit(root => root["Core"]!["RefObject"] = null); emitterCache.ApplyEmitter(fixture, canvas);
            Check(emitterCache.BuildCount == 2 && canvas.Markers[0].Position == Vector2.Zero, "空间配置修改生成新布局");
            fixture.Undo(); emitterCache.ApplyEmitter(fixture, canvas);
            Check(emitterCache.BuildCount == 3 && canvas.Paths[0].Points[0] == firstPoint, "Emitter撤销恢复原空间布局");
            var other = new EmitterDocument(); other.ApplyText(fixture.Text); emitterCache.ApplyEmitter(other, canvas);
            Check(emitterCache.BuildCount == 4, "相同内容的不同文档不能共用身份缓存");
            other.Draft = "{"; emitterCache.ApplyEmitter(other, canvas);
            Check(emitterCache.BuildCount == 4 && other.Draft == "{", "静态显示继续只使用已应用内容，不消费或丢弃当前草稿");

            VerifyLayoutImportChanges(session, cache, canvas);
            VerifyLayoutResourceChanges(session, cache, canvas, textureResource, gradientResource, texturePath, gradientPath);
            // 恢复正式图片，成本对比包含相同的三个阶段和引用，暖缓存仍执行全部依赖检查。
            session.Catalog.Undo(); cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            MeasureLayoutCost("boss-full-validation", () => session.Catalog.ValidateBoss(0, session.CaptureEmitters()));
            int beforeBuilds = cache.BuildCount;
            MeasureLayoutCost("boss-cached-layout", () => cache.ApplyBoss(session.Catalog, 0, 0, session, canvas));
            Check(cache.BuildCount == beforeBuilds, "稳定依赖的连续选择不重复解析Boss和Emitter");
            // 正式Boss01与路径Emitter补充真实引用及几何成本，不用单一小模板推断所有刷新性能。
            var production = new EditorSession(); production.EnsureCatalog();
            var productionCache = new EditorLayoutCache();
            MeasureLayoutCost("boss01-full-validation", () => production.Catalog.ValidateBoss(0, production.CaptureEmitters()));
            MeasureLayoutCost("boss01-cached-layout", () => productionCache.ApplyBoss(production.Catalog, 0, 0, production, canvas));
            MeasureLayoutCost("path-emitter-full-layout", () => canvas.Rebuild(fixture.Validate()));
            MeasureLayoutCost("path-emitter-cached-layout", () => emitterCache.ApplyEmitter(fixture, canvas));
        }
        finally { canvas.Free(); }
        GD.Print($"PASS: {_checks} layout cache assertions");
    }

    /// <summary>验证Godot导入描述、源图片和二进制产物均按内容检查，不修改正式资源。</summary>
    /// <param name="session">包含合法Boss的会话。</param>
    /// <param name="cache">已使用的布局缓存。</param>
    /// <param name="canvas">隐藏画布。</param>
    private void VerifyLayoutImportChanges(EditorSession session, EditorLayoutCache cache, EditorCanvas canvas)
    {
        // 复制一套独立导入文件；UID不沿用正式资源，所有修改及清理只作用于忽略目录。
        const string resource = "res://.tools/cache-imported.png", artifactResource = "res://.tools/cache-imported.ctex";
        string path = ProjectSettings.GlobalizePath(resource), artifact = ProjectSettings.GlobalizePath(artifactResource);
        string original = ProjectSettings.GlobalizePath("res://Assets/Units/Boss_01.png");
        _temporary.AddRange(new[] { path, path + ".import", artifact });
        using var config = new ConfigFile();
        Check(config.Load(original + ".import") == Error.Ok, "资源夹具读取现有导入描述");
        string imported = config.GetValue("remap", "path").AsString();
        File.Copy(original, path, true); File.Copy(ProjectSettings.GlobalizePath(imported), artifact, true);
        config.EraseSectionKey("remap", "uid"); config.SetValue("remap", "path", artifactResource);
        config.SetValue("deps", "source_file", resource); config.SetValue("deps", "dest_files", new[] { artifactResource });
        Check(config.Save(path + ".import") == Error.Ok, "独立资源夹具导入描述写入成功");
        session.Catalog.Edit(root => root["Bosses"]![0]!["Core"]!["TexturePath"] = resource);
        try
        {
            var texture = GD.Load<Texture2D>(resource);
            cache.ApplyBoss(session.Catalog, 0, 0, session, canvas); int builds = cache.BuildCount;
            cache.ApplyBoss(session.Catalog, 0, 1, session, canvas);
            Check(cache.BuildCount == builds, "源图片与导入产物稳定时命中缓存");
            File.AppendAllText(path + ".import", "\n; changed import description\n");
            cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == builds + 1, "导入描述变化触发正式校验");
            // 产物仍由Godot自身资源缓存管理；同长度同时间戳变动必须让静态校验重新执行。
            byte[] bytes = File.ReadAllBytes(artifact); DateTime modified = File.GetLastWriteTimeUtc(artifact);
            bytes[^1] ^= 1; File.WriteAllBytes(artifact, bytes); File.SetLastWriteTimeUtc(artifact, modified);
            cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == builds + 2, "二进制导入产物同元数据变化也使缓存失效");
            File.AppendAllText(path, "source changed");
            cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
            Check(cache.BuildCount == builds + 3, "导入产物未变时源图片变化也使缓存失效");
            GC.KeepAlive(texture);
        }
        finally { session.Catalog.Undo(); }
    }
    /// <summary>验证贴图源文件、嵌套资源以及内存尺寸变化均参与依赖失效。</summary>
    /// <param name="session">已有合法Boss与Emitter引用的会话。</param>
    /// <param name="cache">已经预热的缓存。</param>
    /// <param name="canvas">隐藏的真实画布。</param>
    /// <param name="textureResource">临时Texture2D资源路径。</param>
    /// <param name="gradientResource">临时嵌套Gradient路径。</param>
    /// <param name="texturePath">贴图的绝对路径。</param>
    /// <param name="gradientPath">嵌套资源的绝对路径。</param>
    private void VerifyLayoutResourceChanges(EditorSession session, EditorLayoutCache cache, EditorCanvas canvas,
        string textureResource, string gradientResource, string texturePath, string gradientPath)
    {
        File.WriteAllText(gradientPath, "[gd_resource type=\"Gradient\" format=3]\n\n[resource]\n");
        File.WriteAllText(texturePath, $"[gd_resource type=\"GradientTexture2D\" load_steps=2 format=3]\n\n[ext_resource type=\"Gradient\" path=\"{gradientResource}\" id=\"1\"]\n\n[resource]\ngradient = ExtResource(\"1\")\nwidth = 8\nheight = 8\n");
        session.Catalog.Edit(root => root["Bosses"]![0]!["Core"]!["TexturePath"] = textureResource);
        // 强引用仅属于测试，生产缓存不得保留Texture对象。
        var texture = GD.Load<GradientTexture2D>(textureResource);
        cache.ApplyBoss(session.Catalog, 0, 0, session, canvas); int builds = cache.BuildCount;
        cache.ApplyBoss(session.Catalog, 0, 1, session, canvas);
        Check(cache.BuildCount == builds, "含外部Gradient的贴图依赖稳定时可复用");
        File.AppendAllText(gradientPath, "; dependency changed\n");
        cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
        Check(cache.BuildCount == builds + 1, "嵌套资源文件变化使Boss校验失效");
        File.AppendAllText(texturePath, "; source changed\n");
        cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
        Check(cache.BuildCount == builds + 2, "贴图源内容变化使缓存失效");
        texture.Width = 9;
        Reject(() => cache.ApplyBoss(session.Catalog, 0, 0, session, canvas), "贴图内存尺寸改变后重新拒绝不可整除图集");
        texture.Width = 8; cache.ApplyBoss(session.Catalog, 0, 0, session, canvas);
        Check(canvas.Markers.Count > 0, "贴图尺寸恢复后可再次显示布局");
    }

    /// <summary>预热后输出单线程平均耗时与托管分配，不使用依赖硬件的成功阈值。</summary>
    /// <param name="name">被测路径名称。</param>
    /// <param name="operation">一次当前Boss校验或完整缓存应用。</param>
    private static void MeasureLayoutCost(string name, Action operation)
    {
        for (int index = 0; index < 5; index++) operation();
        long allocated = GC.GetAllocatedBytesForCurrentThread(), begin = Stopwatch.GetTimestamp();
        for (int index = 0; index < 50; index++) operation();
        double microseconds = Stopwatch.GetElapsedTime(begin).TotalMilliseconds * 1000 / 50;
        long bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 50;
        GD.Print(FormattableString.Invariant($"LAYOUT_COST {name} mean_us={microseconds:F3} bytes={bytes} samples=50"));
    }
}
