using System;
using UnityEngine;

// =========================================================
// TYPE SERIALIZATION HELPER
// Converts between string storage and Unity/C# types for
// conditions and fixed-value parameters
// =========================================================
/// <summary>Serialization helper that converts between string storage and Unity/C# types for EventBus conditions and fixed-value parameters.</summary>
public static class TypeHelper
{
    private static readonly System.Globalization.CultureInfo IC =
        System.Globalization.CultureInfo.InvariantCulture;

    public static bool IsSupported(Type t) =>
        t == typeof(int)    || t == typeof(float)   || t == typeof(double)    ||
        t == typeof(long)   || t == typeof(bool)    || t == typeof(string)    ||
        t == typeof(Vector2)    || t == typeof(Vector3)    || t == typeof(Vector4)    ||
        t == typeof(Vector2Int) || t == typeof(Vector3Int) ||
        t == typeof(Color)  || t == typeof(Color32) ||
        t == typeof(Quaternion) || t == typeof(Rect)       ||
        t == typeof(Bounds) || t == typeof(RectInt)  ||
        t == typeof(BoundsInt)  || t.IsEnum;

    public static bool IsOrdered(Type t) =>
        t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long);

    // ── Parse ────────────────────────────────────────────────────────────
    public static object Parse(string s, Type t)
    {
        if (string.IsNullOrEmpty(s)) s = "";
        if (t == typeof(int))    return int.Parse(s, IC);
        if (t == typeof(float))  return float.Parse(s, IC);
        if (t == typeof(double)) return double.Parse(s, IC);
        if (t == typeof(long))   return long.Parse(s, IC);
        if (t == typeof(bool))   return bool.Parse(s);
        if (t == typeof(string)) return s;
        if (t.IsEnum)            return Enum.Parse(t, s);
        if (t == typeof(Vector2))    return ParseV2(s);
        if (t == typeof(Vector3))    return ParseV3(s);
        if (t == typeof(Vector4))    return ParseV4(s);
        if (t == typeof(Vector2Int)) return ParseV2Int(s);
        if (t == typeof(Vector3Int)) return ParseV3Int(s);
        if (t == typeof(Color))      return ParseColor(s);
        if (t == typeof(Color32))    { var c = ParseColor(s); return (Color32)c; }
        if (t == typeof(Quaternion)) return ParseQuat(s);
        if (t == typeof(Rect))       return ParseRect(s);
        if (t == typeof(Bounds))     return ParseBounds(s);
        if (t == typeof(RectInt))    return ParseRectInt(s);
        if (t == typeof(BoundsInt))  return ParseBoundsInt(s);
        throw new NotSupportedException($"TypeHelper: unsupported type {t.Name}");
    }

    // ── Serialize ─────────────────────────────────────────────────────────
    public static string Serialize(object v, Type t)
    {
        if (v == null) return "";
        if (t == typeof(float))  return ((float)v).ToString(IC);
        if (t == typeof(double)) return ((double)v).ToString(IC);
        if (t == typeof(Vector2))    { var u = (Vector2)v;    return $"{u.x.ToString(IC)},{u.y.ToString(IC)}"; }
        if (t == typeof(Vector3))    { var u = (Vector3)v;    return $"{u.x.ToString(IC)},{u.y.ToString(IC)},{u.z.ToString(IC)}"; }
        if (t == typeof(Vector4))    { var u = (Vector4)v;    return $"{u.x.ToString(IC)},{u.y.ToString(IC)},{u.z.ToString(IC)},{u.w.ToString(IC)}"; }
        if (t == typeof(Vector2Int)) { var u = (Vector2Int)v; return $"{u.x},{u.y}"; }
        if (t == typeof(Vector3Int)) { var u = (Vector3Int)v; return $"{u.x},{u.y},{u.z}"; }
        if (t == typeof(Color))      { var u = (Color)v;      return $"{u.r.ToString(IC)},{u.g.ToString(IC)},{u.b.ToString(IC)},{u.a.ToString(IC)}"; }
        if (t == typeof(Color32))    { var u = (Color32)v;    return $"{u.r},{u.g},{u.b},{u.a}"; }
        if (t == typeof(Quaternion)) { var u = (Quaternion)v; return $"{u.x.ToString(IC)},{u.y.ToString(IC)},{u.z.ToString(IC)},{u.w.ToString(IC)}"; }
        if (t == typeof(Rect))       { var u = (Rect)v;       return $"{u.x.ToString(IC)},{u.y.ToString(IC)},{u.width.ToString(IC)},{u.height.ToString(IC)}"; }
        if (t == typeof(Bounds))     { var u = (Bounds)v;     return $"{u.center.x.ToString(IC)},{u.center.y.ToString(IC)},{u.center.z.ToString(IC)},{u.size.x.ToString(IC)},{u.size.y.ToString(IC)},{u.size.z.ToString(IC)}"; }
        if (t == typeof(RectInt))    { var u = (RectInt)v;    return $"{u.x},{u.y},{u.width},{u.height}"; }
        if (t == typeof(BoundsInt))  { var u = (BoundsInt)v;  return $"{u.x},{u.y},{u.z},{u.size.x},{u.size.y},{u.size.z}"; }
        return v.ToString();
    }

    // ── Private parsers ───────────────────────────────────────────────────
    private static float[] Floats(string s, int n)
    {
        var parts = s.Split(',');
        var r = new float[n];
        for (int i = 0; i < n && i < parts.Length; i++)
            float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, IC, out r[i]);
        return r;
    }

    private static int[] Ints(string s, int n)
    {
        var parts = s.Split(',');
        var r = new int[n];
        for (int i = 0; i < n && i < parts.Length; i++)
            int.TryParse(parts[i].Trim(), out r[i]);
        return r;
    }

    private static Vector2    ParseV2(string s)    { var f = Floats(s, 2); return new Vector2(f[0], f[1]); }
    private static Vector3    ParseV3(string s)    { var f = Floats(s, 3); return new Vector3(f[0], f[1], f[2]); }
    private static Vector4    ParseV4(string s)    { var f = Floats(s, 4); return new Vector4(f[0], f[1], f[2], f[3]); }
    private static Vector2Int ParseV2Int(string s) { var i = Ints(s, 2);   return new Vector2Int(i[0], i[1]); }
    private static Vector3Int ParseV3Int(string s) { var i = Ints(s, 3);   return new Vector3Int(i[0], i[1], i[2]); }
    private static Color      ParseColor(string s) { var f = Floats(s, 4); return new Color(f[0], f[1], f[2], f.Length > 3 ? f[3] : 1f); }
    private static Quaternion ParseQuat(string s)  { var f = Floats(s, 4); return new Quaternion(f[0], f[1], f[2], f[3]); }
    private static Rect       ParseRect(string s)  { var f = Floats(s, 4); return new Rect(f[0], f[1], f[2], f[3]); }
    private static RectInt    ParseRectInt(string s)  { var i = Ints(s, 4); return new RectInt(i[0], i[1], i[2], i[3]); }
    private static Bounds     ParseBounds(string s)   { var f = Floats(s, 6); return new Bounds(new Vector3(f[0], f[1], f[2]), new Vector3(f[3], f[4], f[5])); }
    private static BoundsInt  ParseBoundsInt(string s){ var i = Ints(s, 6);   return new BoundsInt(i[0], i[1], i[2], i[3], i[4], i[5]); }
}
