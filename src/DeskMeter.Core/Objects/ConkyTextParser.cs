using DeskMeter.Core.Config;

namespace DeskMeter.Core.Objects;

/// <summary>
/// TEXT 段解析器：把 conky.text 解析为 Object Tree（节点列表）。
/// 支持 $name / 括号参数形式 / $$ 转义 / \n 换行；括号内嵌套变量（如 ${if_match ${cpu} > 50}）
/// 以及 if_* 条件块（${if_...}...${else}...${endif}，可嵌套）——FR-CFG-2 / FR-LATER.if。
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
                // ${...} 括号形式（含 if_* 块与嵌套）
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    var token = TryReadBraceToken(text, i);
                    if (token is not null)
                    {
                        if (IsConditionName(token.Value.Name))
                        {
                            Flush();
                            i = ParseConditional(text, i, token.Value, nodes, registry, settings);
                            continue;
                        }
                        if (token.Value.Name.Equals("else", StringComparison.OrdinalIgnoreCase) ||
                            token.Value.Name.Equals("endif", StringComparison.OrdinalIgnoreCase))
                        {
                            // 游离的 else/endif：无对应 if，忽略（不产生内容）
                            i = token.Value.End;
                            continue;
                        }
                        Flush();
                        nodes.Add(registry.Create(token.Value.Name, token.Value.Args, settings));
                        i = token.Value.End;
                        continue;
                    }
                }
                Flush();
                i = ParseDollarName(text, i, nodes, registry, settings);
                continue;
            }

            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == 'n') { Flush(); nodes.Add(new NewlineNode()); i += 2; continue; }
                if (next == '\\') { literal.Append('\\'); i += 2; continue; }
                if (next == 't') { literal.Append('\t'); i += 2; continue; }
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

    /// <summary>${name args} 词法单元（花括号深度感知，支持嵌套变量）。</summary>
    private static (int End, string Name, string[] Args)? TryReadBraceToken(string text, int start)
    {
        // start 指向 '$'，其后应为 '{'
        var i = start + 2;
        var depth = 1;
        var tokenStart = i;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '{' && i > 0 && text[i - 1] == '$') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) break;
            }
            i++;
        }
        if (i >= text.Length || depth != 0) return null; // 未闭合：按字面处理
        var inner = text[tokenStart..i];
        var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = parts.Length > 0 ? parts[0] : string.Empty;
        var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
        return (i + 1, name, args);
    }

    private static bool IsConditionName(string name) =>
        name.StartsWith("if_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析 ${if_xxx ...} 条件块：扫描到配对的 ${endif}（支持嵌套与 ${else}），
    /// 主体文本递归解析后交给 ConditionalNode 在运行时按条件求值。
    /// </summary>
    private static int ParseConditional(string text, int start, (int End, string Name, string[] Args) token,
        List<ObjectNode> nodes, ObjectRegistry registry, ConfigSettings settings)
    {
        var then = new System.Text.StringBuilder();
        var els = new System.Text.StringBuilder();
        var active = then;
        var depth = 0;
        var i = token.End;

        while (i < text.Length)
        {
            var c = text[i];
            if (c == '$' && i + 1 < text.Length && text[i + 1] == '$') { active.Append("$$"); i += 2; continue; }
            if (c == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                var t = TryReadBraceToken(text, i);
                if (t is not null)
                {
                    var n = t.Value.Name;
                    if (IsConditionName(n))
                    {
                        depth++;
                        active.Append(text[i..t.Value.End]);
                        i = t.Value.End;
                        continue;
                    }
                    if (n.Equals("endif", StringComparison.OrdinalIgnoreCase))
                    {
                        if (depth > 0) { depth--; active.Append(text[i..t.Value.End]); i = t.Value.End; continue; }
                        // 本层 endif：结束
                        i = t.Value.End;
                        break;
                    }
                    if (n.Equals("else", StringComparison.OrdinalIgnoreCase))
                    {
                        if (depth == 0) { active = els; i = t.Value.End; continue; }
                        active.Append(text[i..t.Value.End]);
                        i = t.Value.End;
                        continue;
                    }
                    active.Append(text[i..t.Value.End]);
                    i = t.Value.End;
                    continue;
                }
            }
            active.Append(c);
            i++;
        }

        var thenNodes = Parse(then.ToString(), registry, settings);
        var elseNodes = Parse(els.ToString(), registry, settings);
        nodes.Add(new ConditionalNode(token.Name, token.Args, thenNodes, elseNodes, registry, settings));
        return i;
    }

    /// <summary>$name（字母/数字/下划线）。</summary>
    private static int ParseDollarName(string text, int start, List<ObjectNode> nodes,
        ObjectRegistry registry, ConfigSettings settings)
    {
        var i = start + 1;
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