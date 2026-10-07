using System;
using System.Collections.Generic;
using System.Reflection;

namespace SetsunaAccess
{
    /// <summary>
    /// Reads private fields of game objects, including fields of the game's private structs
    /// (boxed array elements). FieldInfo lookups are cached per type and name.
    /// </summary>
    internal static class Reflect
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<string, FieldInfo> _cache = new Dictionary<string, FieldInfo>();

        public static T Get<T>(object obj, string field)
        {
            if (obj == null) return default(T);
            var f = Field(obj.GetType(), field);
            if (f == null) return default(T);
            var v = f.GetValue(obj);
            return v is T ? (T)v : default(T);
        }

        /// <summary>An int or private enum field as int; -1 if missing.</summary>
        public static int Int(object obj, string field)
        {
            var v = Get<object>(obj, field);
            return v == null ? -1 : Convert.ToInt32(v);
        }

        /// <summary>A private array field as Array, so struct elements can be read with Get.</summary>
        public static Array Arr(object obj, string field) { return Get<Array>(obj, field); }

        private static FieldInfo Field(Type t, string name)
        {
            var key = t.FullName + "::" + name;
            FieldInfo f;
            if (_cache.TryGetValue(key, out f)) return f;
            for (var cur = t; cur != null && f == null; cur = cur.BaseType)
                f = cur.GetField(name, All);
            if (f == null) Log.Warn("Reflect", "no field " + key);
            _cache[key] = f;
            return f;
        }
    }
}
