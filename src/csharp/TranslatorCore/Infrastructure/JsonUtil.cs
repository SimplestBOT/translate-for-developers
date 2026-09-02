//=============================================================
// Infrastructure/JsonUtil.cs - JSON 序列化/解析统一入口
// 落点说明：architecture.md 原计划 System.Text.Json，阶段 3 实施时
// 改用 GAC 程序集 System.Web.Extensions 的 JavaScriptSerializer——
// net48 离线可构建、部署零新增 DLL（绿色便携）；若未来需换实现，
// 业务代码只经此处，替换成本集中在一个文件。
//=============================================================
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Translator.Core.Infrastructure
{
    public static class JsonUtil
    {
        // JavaScriptSerializer 非线程安全：所有调用串行化
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static string Serialize(object value)
        {
            lock (Ser)
                return Ser.Serialize(value);
        }

        /// <summary>解析 JSON 对象；失败返回 null（协议层容错：坏帧按静默/错误帧处理）</summary>
        public static Dictionary<string, object> ParseObject(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                var o = Ser.Deserialize<Dictionary<string, object>>(json);
                return o;
            }
            catch (System.ArgumentException)
            {
                return null;
            }
        }

        public static string GetString(IDictionary<string, object> obj, string key)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v) && v is string)
                return (string)v;
            return null;
        }

        public static bool GetBool(IDictionary<string, object> obj, string key)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v))
            {
                if (v is bool) return (bool)v;
                bool parsed;
                if (bool.TryParse("" + v, out parsed)) return parsed;
            }
            return false;
        }

        public static int GetInt(IDictionary<string, object> obj, string key)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v))
            {
                if (v is int) return (int)v;
                if (v is long) return checked((int)(long)v);
                if (v is double) return checked((int)(double)v);
                int parsed;
                if (int.TryParse("" + v, out parsed)) return parsed;
            }
            return 0;
        }

        /// <summary>取数组值（JavaScriptSerializer 反序列化为 ArrayList）</summary>
        public static IList GetList(IDictionary<string, object> obj, string key)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v) && v is IList)
                return (IList)v;
            return null;
        }
    }
}
