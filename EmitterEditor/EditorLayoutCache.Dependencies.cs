using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

/// <summary>静态布局依赖检查，跟踪实际加载的引用文本、贴图资源及其文件内容。</summary>
internal sealed partial class EditorLayoutCache
{
    /// <summary>仅保存内容指纹与校验相关尺寸，不保留运行树或Godot资源对象。</summary>
    private sealed class Dependencies
    {
        // 文件键按Windows路径身份比较；Emitter文本始终取自磁盘。
        private readonly Dictionary<string, string> _sources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (int Width, int Height)> _textures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _references = new(StringComparer.Ordinal);
        private readonly HashSet<string> _resources = new(StringComparer.Ordinal);
        // 无法完整观察文件依赖的打包资源继续使用正式校验，不猜测它们保持不变。
        private bool _reusable = true;

        /// <summary>同一次Boss检查中只解析一次相同Emitter引用，不保留解析所得运行树。</summary>
        /// <param name="path">正式协议中的Emitter路径。</param>
        internal void CheckEmitter(string path)
        {
            string full = Path.GetFullPath(ProjectSettings.GlobalizePath(path));
            if (_sources.ContainsKey(full)) return;
            string source = File.ReadAllText(full);
            var emitter = VBulletEmitter.FromJson(source, path);
            AddEmitter(emitter); _sources.Add(full, source);
        }

        /// <summary>从已展开的实际树收集显示依赖，避免在工具中重复维护贴图名称映射。</summary>
        /// <param name="emitter">仅本次读取的独立树。</param>
        internal void AddEmitter(VBulletEmitter emitter) => AddCreator(emitter.Root);
        /// <summary>按前序收集Creator与后代使用的贴图。</summary>
        /// <param name="creator">已校验的Creator。</param>
        private void AddCreator(VNodeCreator creator)
        {
            if (creator is VBulletCreator { TextureResourcePath: { } path }) AddTexture(path);
            foreach (var child in creator.Children) AddCreator(child);
        }

        /// <summary>记录实际贴图的尺寸及递归文件依赖；不能观察时禁用复用但不改变加载器语义。</summary>
        /// <param name="path">已校验贴图的资源路径。</param>
        internal void AddTexture(string path)
        {
            if (_textures.ContainsKey(path)) return;
            var stamp = TextureStamp(path);
            if (stamp is null) { _reusable = false; return; }
            _textures.Add(path, stamp.Value);
            try { AddResource(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _reusable = false; }
        }

        /// <summary>记录资源源文件、导入产物及外部引用，循环引用只访问一次。</summary>
        /// <param name="path">Godot资源路径，可为贴图的嵌套资源。</param>
        private void AddResource(string path)
        {
            if (!_resources.Add(path)) return;
            string full = Path.GetFullPath(ProjectSettings.GlobalizePath(path));
            string? digest = FileDigest(full); _files[full] = digest;
            if (digest is null) { _reusable = false; return; }
            // 导入描述本身与所有产物都参与比较，不能仅靠源PNG时间戳判断。
            string imported = full + ".import";
            _files[imported] = FileDigest(imported);
            if (_files[imported] is not null)
            {
                using var config = new ConfigFile();
                if (config.Load(imported) != Error.Ok) { _reusable = false; return; }
                if (config.HasSectionKey("deps", "dest_files"))
                    foreach (string artifact in config.GetValue("deps", "dest_files").AsStringArray())
                    {
                        string artifactPath = Path.GetFullPath(ProjectSettings.GlobalizePath(artifact));
                        string? artifactDigest = FileDigest(artifactPath);
                        _files[artifactPath] = artifactDigest;
                        if (artifactDigest is null) _reusable = false;
                    }
            }
            foreach (string reference in ResourceLoader.GetDependencies(path))
            {
                string resolved = ResolveReference(reference);
                _references[reference] = resolved; AddResource(resolved);
            }
        }

        /// <summary>比较当前真实内容及资源状态；失败时重新走正式加载器生成准确诊断。</summary>
        /// <returns>全部依赖可观察且没有变化时为真。</returns>
        internal bool Matches()
        {
            if (!_reusable) return false;
            try
            {
                foreach (var source in _sources)
                    if (File.ReadAllText(source.Key) != source.Value) return false;
                foreach (var file in _files)
                    if (file.Value is null ? File.Exists(file.Key) : FileDigest(file.Key) != file.Value) return false;
                foreach (var reference in _references)
                    if (ResolveReference(reference.Key) != reference.Value) return false;
                foreach (var texture in _textures)
                    if (TextureStamp(texture.Key) != texture.Value) return false;
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { return false; }
        }

        /// <summary>读取源或导入文件的完整内容指纹，缺失表示null，不依赖长度和更新时间。</summary>
        /// <param name="path">操作系统绝对路径。</param>
        /// <returns>SHA256指纹；文件缺失为null，访问错误交给调用方处理。</returns>
        private static string? FileDigest(string path)
        {
            try
            {
                // 已存在依赖直接打开一次；流哈希自行使用缓冲，避免重复文件状态查询和流缓冲分配。
                using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read, 1);
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        /// <summary>只记录当前Godot贴图的校验相关尺寸，不因资源回收后重新加载而误判内容变化。</summary>
        /// <param name="path">贴图路径。</param>
        /// <returns>贴图宽高像素；路径当前不可加载为贴图时为空。</returns>
        private static (int Width, int Height)? TextureStamp(string path)
        {
            if (!ResourceLoader.Exists(path, "Texture2D") || ResourceLoader.Load(path) is not Texture2D texture) return null;
            return (texture.GetWidth(), texture.GetHeight());
        }

        /// <summary>按Godot规则解析依赖UID，未登记时使用返回的后备路径。</summary>
        /// <param name="reference">GetDependencies返回的路径或UID::后备路径描述。</param>
        /// <returns>本次实际资源路径。</returns>
        private static string ResolveReference(string reference)
        {
            var parts = reference.Split("::", StringSplitOptions.None);
            if (parts.Length != 3) return reference;
            long id = ResourceUid.TextToId(parts[0]);
            return ResourceUid.HasId(id) ? ResourceUid.GetIdPath(id) : parts[2];
        }
    }
}
