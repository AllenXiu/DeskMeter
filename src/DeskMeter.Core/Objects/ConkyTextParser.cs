using DeskMeter.Core.Config;

namespace DeskMeter.Core.Objects;

/// <summary>
/// TEXT 段解析器：把 conky.text 解析为 Object Tree（节点列表）。
/// 支持 $name / 括号参数形式 / $$ 转义 / \n 换行（FR-CFG-2）。
/// </summary>
public static class ConkyTextParser
{
    public static List<ObjectNode> Parse(string text, ObjectRegistry registry, ConfigSettings settings)
    {
        var nodes = new List<ObjectNode>();
        var i = 0;
        var literal = new System.Text.StringBuilder();

        void Flush()
        {
            if (literal.Length == 0) return;
            var s = literal.ToString();
            literal.Clear();
            AppendTextWithNewlines(nodes, s);
        }

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '$' && i + 1 < text.Length && text[i + 1] == '$')
            {
                literal.Append('$');
                i += 2;
                continue;
            }

            if (c == '$')
            {
                Flush();
                i = ParseVariable(text, i, nodes, registry, settings);
                continue;
            }

            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == 'n') { Flush(); nodes.Add(new NewlineNode()); i += 2; continue; }
                if (next == '\\') { literal.Append('\\'); i += 2; continue; }
                if (next == 't') { literal.Append('	'); i += 2; continue; }
                literal.Append('\\');
                i += 1;
                continue;
            }

            literal.Append(c);
            i++;
        }

        Flush();
        return nodes;
    }

    private static int ParseVariable(string text, int start, List<ObjectNode> nodes,
        ObjectRegistry registry, ConfigSettings settings)
    {
        // start 指向 '$'
        var i = start + 1;
        if (i < text.Length && text[i] == '{')
        {
            var end = text.IndexOf('}', i + 1);
            if (end < 0)
            {
                nodes.Add(new TextNode("$" + "{"));
                return i + 1;
            }
            var inner = text[(i + 1)..end];
            var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var name = parts.Length > 0 ? parts[0] : string.Empty;
            var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
            nodes.Add(registry.Create(name, args, settings));
            return end + 1;
        }

        // $name（字母/数字/下划线）
        var j = i;
        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
        if (j == i)
        {
            nodes.Add(new TextNode("$"));
            return i;
        }
        nodes.Add(registry.Create(text[i..j], Array.Empty<string>(), settings));
        return j;
    }

    private static void AppendTextWithNewlines(List<ObjectNode> nodes, string s)
    {
        var parts = s.Split('\n');
        for (var k = 0; k < parts.Length; k++)
        {
            if (k > 0) nodes.Add(new NewlineNode());
            if (parts[k].Length > 0) nodes.Add(new TextNode(parts[k]));
        }
    }
}
