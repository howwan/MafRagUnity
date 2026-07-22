// Markdown 分块器（D3 / FR-2 / NFR-3）。
// 策略：字符滑动窗口（窗口 ≈ ChunkSize，步长 ChunkSize-Overlap），
//       断块时优先在自然语义边界截断，分隔符按语义层级降序尝试：
//         ["\n\n", "\n", ". ", "。", "! ", "? "]
//       即 段落 > 换行 > 句子结束符。仅当窗口后半段找不到更高层级边界时
//       才降级到下一层级，保证每块尽量落在完整的句子/段落内（语义完整性）。
//       同时为每个块携带其之前最近的 # 标题作为上下文；块间保留 overlap 重叠；
//       去除连续空行噪声。纯文本处理，无外部依赖，移动端安全。
//
// 参考：unified-rag/src/chunking.py 的 chunk_text（句子感知分块）。

using System;
using System.Collections.Generic;
using System.Text;

public static class MarkdownChunker
{
    // 语义分隔符优先级：段落 -> 换行 -> 英文/中文句子结束符。
    // 顺序不可随意调换：先找粒度更大的边界，保证语义完整性。
    private static readonly string[] Separators = { "\n\n", "\n", ". ", "。", "! ", "? " };

    public static List<string> Chunk(string markdown, int chunkSize = 500, int overlap = 100)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(markdown)) return result;

        // 统一换行符，保证分隔符匹配一致
        string text = markdown.Replace("\r\n", "\n");

        // 预构建「字符位置 -> 其之前最近的 # 标题」映射，供每块携带上下文
        var headers = BuildHeaderMap(text);

        // 短文本直接整段返回（单块兜底）
        if (text.Length <= chunkSize)
        {
            string single = PrependHeader(headers, 0, Compact(text));
            if (!string.IsNullOrWhiteSpace(single)) result.Add(single.Trim());
            if (result.Count == 0) result.Add(Compact(text));
            return result;
        }

        int start = 0;
        while (start < text.Length)
        {
            int end = start + chunkSize;
            if (end > text.Length) end = text.Length; // 末块：钳制到串尾，避免 Substring 越界（C# 不似 Python 自动截断）
            // 仅当不是最后一块时，尝试在窗口后半段按语义边界回退断点
            if (end < text.Length)
            {
                int cut = FindSeparatorCut(text, start + chunkSize / 2, end);
                if (cut != -1) end = cut; // cut 已包含分隔符本身
            }

            string body = Compact(text.Substring(start, end - start));
            if (!string.IsNullOrWhiteSpace(body))
            {
                result.Add(PrependHeader(headers, start, body).Trim());
            }

            // 重叠推进：下一块起点前移 overlap，防止死循环
            int newStart = end - overlap;
            if (newStart <= start) newStart = end;
            start = newStart;
        }

        if (result.Count == 0) result.Add(Compact(text)); // 极端情况兜底
        return result;
    }

    // 在 [from, to) 后半段内按优先级反向查找首个（最高层级）分隔符，
    // 返回「包含该分隔符之后」的断点位置；找不到返回 -1。
    private static int FindSeparatorCut(string text, int from, int to)
    {
        if (from < 0) from = 0;
        if (to > text.Length) to = text.Length;
        if (to <= from) return -1;
        // LastIndexOf(value, startIndex, count)：在 [startIndex-count+1, startIndex] 区间反向查找
        int span = to - from;
        foreach (string sep in Separators)
        {
            int cut = text.LastIndexOf(sep, to - 1, span);
            if (cut >= from) return cut + sep.Length; // 把分隔符本身留在当前块内
        }
        return -1;
    }

    // 扫描所有 # 标题行，记录其起始字符位置与文本。
    private static List<(int index, string header)> BuildHeaderMap(string text)
    {
        var list = new List<(int, string)>();
        int lineStart = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == '\n')
            {
                string line = text.Substring(lineStart, i - lineStart).TrimEnd('\r').TrimEnd();
                if (line.Length > 0 && line[0] == '#') list.Add((lineStart, line));
                lineStart = i + 1;
            }
        }
        return list;
    }

    // 取 start 之前（含）最近的 # 标题，拼到正文前作为上下文。
    private static string PrependHeader(List<(int index, string header)> headers, int start, string body)
    {
        string header = null;
        for (int i = headers.Count - 1; i >= 0; i--)
        {
            if (headers[i].index <= start) { header = headers[i].header; break; }
        }
        return header == null ? body : header + "\n" + body;
    }

    // 去除连续空行，避免噪声
    private static string Compact(string text)
    {
        var sb = new StringBuilder();
        bool lastEmpty = false;
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            bool empty = string.IsNullOrWhiteSpace(line);
            if (empty)
            {
                if (lastEmpty) continue;
                lastEmpty = true;
            }
            else
            {
                lastEmpty = false;
            }
            sb.Append(line).Append("\n");
        }
        return sb.ToString().Trim();
    }
}
