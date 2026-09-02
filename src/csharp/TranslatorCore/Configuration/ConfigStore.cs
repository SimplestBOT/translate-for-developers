//=============================================================
// Configuration/ConfigStore.cs - config.conf 唯一 Owner（阶段 3 移交）
// 规则（architecture.md 边界规则 3）：
//   - 阶段 3 起 C# 是唯一写方（SaveConfig 等价物只有本类 WriteAsync/WriteSync）
//   - AHK 降级为只读兼容（读自身内存态）+ 变更经桥 config_set 单向下发
//   - 读：启动时 C# 从 config.conf 读入内存快照；写：整体重写文件
// 文件格式（与 AHK SaveConfig 逐字节对齐）：UTF-8 带 BOM，
//   键序 hotkey/provider/src_lang/tgt_lang/baidu_appid/baidu_secret，行尾 \n
//   （AHK FileOpen("w","UTF-8") 写带 BOM UTF-8，"UTF-8-RAW" 才是无 BOM；
//    用户现网 config.conf 含 BOM，C# 写盘保持同格式，注释行 ; 开头容忍）
//=============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Translator.Core.Configuration
{
    public sealed class AppConfig
    {
        public string Hotkey;        // AHK 原始串（^!t）
        public string Provider;      // mymemory / baidu
        public string SourceLang;    // 语言 ID（auto 合法）
        public string TargetLang;    // 语言 ID（auto 非法，写入前由 AHK/C# 各自校验）
        public string BaiduAppid;
        public string BaiduSecret;

        public bool HasBaiduKeys()
        {
            return !string.IsNullOrEmpty(BaiduAppid) && !string.IsNullOrEmpty(BaiduSecret);
        }
    }

    public sealed class ConfigStore : IDisposable
    {
        private readonly string _path;
        private readonly object _lock = new object();
        private volatile AppConfig _snapshot;

        /// <summary>文件路径不可用时抛出（宿主此时应保持降级模式，AHK 继续当写方）</summary>
        public ConfigStore(string configFilePath)
        {
            if (string.IsNullOrEmpty(configFilePath))
                throw new ArgumentNullException("configFilePath");
            _path = configFilePath;
            _snapshot = ReadFileOrEmpty();
        }

        public string FilePath { get { return _path; } }

        /// <summary>当前配置快照（业务侧只读引用；写经 Update 原子替换）</summary>
        public AppConfig Current { get { return _snapshot; } }

        private AppConfig ReadFileOrEmpty()
        {
            var cfg = new AppConfig
            {
                Hotkey = "^!t",
                Provider = "mymemory",
                SourceLang = "auto",
                TargetLang = "zh-CN",
                BaiduAppid = "",
                BaiduSecret = ""
            };
            try
            {
                // 兼容旧版 hotkey.conf（仅热键；config.conf 缺失场景，对齐 AHK LoadConfig）
                if (!File.Exists(_path))
                {
                    string hk = ReadHotkeyConf();
                    if (hk != null) cfg.Hotkey = hk;
                    return cfg;
                }
                string[] lines = File.ReadAllLines(_path, Encoding.UTF8);
                foreach (var raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";"))
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "hotkey": if (val.Length > 0) cfg.Hotkey = val; break;
                        case "provider": if (val == "baidu" || val == "mymemory") cfg.Provider = val; break;
                        case "src_lang": cfg.SourceLang = val; break;
                        case "tgt_lang": cfg.TargetLang = val; break;
                        case "baidu_appid": cfg.BaiduAppid = val; break;
                        case "baidu_secret": cfg.BaiduSecret = val; break;
                    }
                }
                if (string.IsNullOrEmpty(cfg.TargetLang) || cfg.TargetLang == "auto")
                    cfg.TargetLang = "zh-CN";
                if (string.IsNullOrEmpty(cfg.SourceLang))
                    cfg.SourceLang = "auto";
                return cfg;
            }
            catch (Exception)
            {
                // 读取失败：用默认值（AHK LoadConfig 文件缺失时也走代码默认）
                return cfg;
            }
        }

        /// <summary>旧版 hotkey.conf（仅一行热键，可含 BOM）；不存在/为空返回 null。</summary>
        private string ReadHotkeyConf()
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(dir)) return null;
                string p = Path.Combine(dir, "hotkey.conf");
                if (!File.Exists(p)) return null;
                string s = File.ReadAllText(p, Encoding.UTF8).Trim().TrimStart('\uFEFF').Trim();
                return s.Length > 0 ? s : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>全量写入（异步）。写方校验职责在调用方（宿主 handler）。</summary>
        /// <returns>false = 写入失败（磁盘/权限），调用方回 error 帧 save_failed</returns>
        public Task<bool> WriteAsync(AppConfig cfg)
        {
            return Task.Run(delegate
            {
                lock (_lock)
                {
                    return WriteSyncCore(cfg);
                }
            });
        }

        /// <summary>全量写入（同步版，供 UI 线程快速路径/测试）</summary>
        public bool WriteSync(AppConfig cfg)
        {
            lock (_lock)
                return WriteSyncCore(cfg);
        }

        private bool WriteSyncCore(AppConfig cfg)
        {
            if (cfg == null) return false;
            // 键序与 AHK SaveConfig 完全一致，保证 AHK 只读兼容解析
            var sb = new StringBuilder();
            sb.Append("hotkey=").Append(cfg.Hotkey == null ? "" : cfg.Hotkey).Append('\n');
            sb.Append("provider=").Append(cfg.Provider == null ? "" : cfg.Provider).Append('\n');
            sb.Append("src_lang=").Append(cfg.SourceLang == null ? "" : cfg.SourceLang).Append('\n');
            sb.Append("tgt_lang=").Append(cfg.TargetLang == null ? "" : cfg.TargetLang).Append('\n');
            sb.Append("baidu_appid=").Append(cfg.BaiduAppid == null ? "" : cfg.BaiduAppid).Append('\n');
            sb.Append("baidu_secret=").Append(cfg.BaiduSecret == null ? "" : cfg.BaiduSecret).Append('\n');
            try
            {
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(true)); // 带 BOM，与 AHK 写盘格式一致
                _snapshot = cfg; // 原子替换快照（volatile 读侧一致）
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>重新读取文件（外部进程改了配置时同步用；迁移期 AHK 兜底直写后宿主重入）</summary>
        public void Reload()
        {
            lock (_lock)
                _snapshot = ReadFileOrEmpty();
        }

        public void Dispose() { }
    }
}
