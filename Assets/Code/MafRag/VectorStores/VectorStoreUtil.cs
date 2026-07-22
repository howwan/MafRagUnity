// 远程向量库共用小工具：float[] <-> JSON、数值转换。

using System.Globalization;
using System.Text;

public static class VectorStoreUtil
{
    public static string FloatsToJson(float[] v)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < v.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(v[i].ToString(CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    public static int ToInt(object o)
    {
        if (o is double d) return (int)d;
        if (o is int i) return i;
        if (o is string s && int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r)) return r;
        return 0;
    }

    public static double ToDouble(object o)
    {
        if (o is double d) return d;
        if (o is int i) return i;
        if (o is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r)) return r;
        return 0;
    }
}
