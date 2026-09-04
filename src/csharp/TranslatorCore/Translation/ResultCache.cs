//=============================================================
// Translation/ResultCache.cs - 相同文本翻译结果缓存（进程内）
// 键 = 源语言 + 目标语言 + 原文 + 配置 Provider（2026-09-03 回归修复：
//   切换服务商后页面自动补发 translate，键不含 Provider 时命中旧服务商
//   缓存结果 → 用户看到"没重翻"；含 Provider 后切换即新键，真实重翻）。
// TTL 5 分钟 + 容量上限 LRU 淘汰（防长文本大量划词撑爆内存）。
// 线程安全（lock）；时钟经 Func<DateTime> 注入，可单测 TTL/LRU。
//=============================================================
using System;
using System.Collections.Generic;

namespace Translator.Core.Translation
{
    public sealed class ResultCache
    {
        private sealed class Entry
        {
            public TranslationResult Result;
            public long ExpiresTicks;
            public long LastUsedTicks;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<string, Entry> _map = new Dictionary<string, Entry>();
        private readonly TimeSpan _ttl;
        private readonly int _capacity;
        private readonly Func<DateTime> _now;

        public ResultCache(TimeSpan? ttl = null, int capacity = 64, Func<DateTime> now = null)
        {
            _ttl = ttl ?? TimeSpan.FromMinutes(5);
            _capacity = capacity;
            _now = now ?? delegate { return DateTime.UtcNow; };
        }

        /// <summary>缓存键：语言对 + 配置 Provider + 原文（\u0001 分隔避免
        /// 拼接歧义）。Provider 取配置值（mymemory/baidu）——降级产出的结果
        /// 也以配置键存储：TTL 内重复划词同样命中（仍省额度），用户切换
        /// Provider 后即新键（真实重翻）。</summary>
        public static string MakeKey(string sourceLang, string targetLang, string provider, string text)
        {
            return (sourceLang ?? "") + "\u0001" + (targetLang ?? "") + "\u0001"
                + (provider ?? "") + "\u0001" + (text ?? "");
        }

        public bool TryGet(string key, out TranslationResult result)
        {
            lock (_lock)
            {
                result = null;
                Entry e;
                if (!_map.TryGetValue(key ?? "", out e)) return false;
                long nowTicks = _now().Ticks;
                if (nowTicks >= e.ExpiresTicks)
                {
                    _map.Remove(key ?? "");   // 过期即清
                    return false;
                }
                e.LastUsedTicks = nowTicks;
                result = e.Result;
                return true;
            }
        }

        public void Put(string key, TranslationResult result)
        {
            if (result == null) return;
            lock (_lock)
            {
                long nowTicks = _now().Ticks;
                if (_map.Count >= _capacity && !_map.ContainsKey(key ?? ""))
                {
                    // LRU 淘汰：移除最近最少使用（键数=容量量级，O(n) 扫描成本可忽略）
                    string oldest = null;
                    long oldestTicks = long.MaxValue;
                    foreach (var kv in _map)
                    {
                        if (kv.Value.LastUsedTicks < oldestTicks)
                        {
                            oldestTicks = kv.Value.LastUsedTicks;
                            oldest = kv.Key;
                        }
                    }
                    if (oldest != null) _map.Remove(oldest);
                }
                _map[key ?? ""] = new Entry
                {
                    Result = result,
                    ExpiresTicks = nowTicks + _ttl.Ticks,
                    LastUsedTicks = nowTicks
                };
            }
        }
    }
}
