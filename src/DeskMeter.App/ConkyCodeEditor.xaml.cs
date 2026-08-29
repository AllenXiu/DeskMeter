using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskMeter.App;

/// <summary>
/// conky.conf 编辑器（FR-SET-5）：等宽排版 + 行号 + Conky/Lua 语法高亮 + 垂直滚动条。
/// 简化实现：RichTextBox + 防抖着色 + 行号同步滚动。
/// </summary>
public partial class ConkyCodeEditor : UserControl
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _applying;
    private ScrollViewer? _editorScroller;

    public ConkyCodeEditor()
    {
        InitializeComponent();
        _debounce.Tick += (_, _) => { _debounce.Stop(); Colorize(); };
        Loaded += OnLoaded;
    }

    public string Text
    {
        get => GetPlainText(Editor);
        set
        {
            _applying = true;
            Editor.Document.Blocks.Clear();
            var para = new Paragraph();
            foreach (var (part, _) in Highlight.Tokenize(value))
            {
                para.Inlines.Add(new Run(part) { Foreground = Highlight.BrushFor(part, value) });
            }
            Editor.Document.Blocks.Add(para);
            _applying = false;
            UpdateLineNumbers();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 找到 RichTextBox 内部 ScrollViewer，同步行号滚动
        _editorScroller = FindScrollViewer(Editor);
        if (_editorScroller != null)
            _editorScroller.ScrollChanged += (_, se) =>
                LineScroll.ScrollToVerticalOffset(se.VerticalOffset);
        UpdateLineNumbers();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateLineNumbers();
        if (_applying) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void UpdateLineNumbers()
    {
        var count = GetPlainText(Editor).Split('\n').Length;
        var sb = new System.Text.StringBuilder();
        for (var i = 1; i <= count; i++) sb.AppendLine(i.ToString());
        LineNumbers.Text = sb.ToString();
    }

    private void Colorize()
    {
        if (_applying) return;
        _applying = true;
        try
        {
            var text = GetPlainText(Editor);
            var caretOffset = Editor.CaretPosition.DocumentStart.GetOffsetToPosition(Editor.CaretPosition);
            Editor.Document.Blocks.Clear();
            var para = new Paragraph { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            foreach (var token in Highlight.Tokenize(text))
            {
                var run = new Run(token.Text) { Foreground = token.Brush };
                para.Inlines.Add(run);
            }
            Editor.Document.Blocks.Add(para);
            var pos = Editor.Document.ContentStart.GetPositionAtOffset(Math.Min(caretOffset, Editor.Document.ContentStart.GetOffsetToPosition(Editor.Document.ContentEnd)));
            if (pos != null) Editor.CaretPosition = pos;
        }
        finally
        {
            _applying = false;
        }
    }

    private static string GetPlainText(RichTextBox box)
    {
        var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
        return range.Text.Replace("\r\n", "\n").TrimEnd('\n');
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }
}

/// <summary>Conky/Lua 语法高亮：注释/字符串/变量/关键字/数字。</summary>
internal static class Highlight
{
    private static readonly SolidColorBrush Comment = Frozen(Color.FromRgb(0x6A, 0x99, 0x55));
    private static readonly SolidColorBrush String = Frozen(Color.FromRgb(0xCE, 0x91, 0x78));
    private static readonly SolidColorBrush Variable = Frozen(Color.FromRgb(0xE5, 0xC0, 0x7B));
    private static readonly SolidColorBrush Keyword = Frozen(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly SolidColorBrush Number = Frozen(Color.FromRgb(0xB5, 0xCE, 0xA8));
    private static readonly SolidColorBrush Plain = Frozen(Color.FromRgb(0xD4, 0xD4, 0xD4));
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "conky", "config", "text", "true", "false", "nil", "local", "function",
        "end", "if", "then", "else", "elseif", "return", "and", "or", "not",
    };

    public readonly record struct Token(string Text, SolidColorBrush Brush);

    public static IEnumerable<Token> Tokenize(string text)
    {
        var inLongString = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            var i = 0;
            while (i < line.Length)
            {
                if (inLongString)
                {
                    var end = line.IndexOf("]]", i, StringComparison.Ordinal);
                    if (end < 0) { yield return new Token(line[i..], String); yield return new Token("\n", Plain); i = line.Length; }
                    else
                    {
                        yield return new Token(line[i..(end + 2)], String);
                        i = end + 2;
                        inLongString = false;
                    }
                    continue;
                }

                if (line[i] == '-' && i + 1 < line.Length && line[i + 1] == '-')
                {
                    yield return new Token(line[i..], Comment);
                    i = line.Length;
                    continue;
                }

                if (line[i] == '[' && i + 1 < line.Length && line[i + 1] == '[')
                {
                    inLongString = true;
                    continue;
                }

                if (line[i] == 0x27 || line[i] == '"') // 0x27 = 单引号
                {
                    var quote = line[i];
                    var j = i + 1;
                    while (j < line.Length && line[j] != quote) j++;
                    var len = Math.Min(j + 1, line.Length) - i;
                    yield return new Token(line.Substring(i, len), String);
                    i += len;
                    continue;
                }

                if (line[i] == '$')
                {
                    var j = i + 1;
                    if (j < line.Length && line[j] == '{')
                    {
                        var end = line.IndexOf('}', j + 1);
                        if (end >= 0) { yield return new Token(line[i..(end + 1)], Variable); i = end + 1; continue; }
                    }
                    yield return new Token("$", Variable);
                    i++;
                    continue;
                }

                if (char.IsLetter(line[i]) || line[i] == '_')
                {
                    var j = i;
                    while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '_')) j++;
                    var word = line[i..j];
                    yield return new Token(word, Keywords.Contains(word) ? Keyword : Plain);
                    i = j;
                    continue;
                }

                if (char.IsDigit(line[i]))
                {
                    var j = i;
                    while (j < line.Length && (char.IsDigit(line[j]) || line[j] == '.')) j++;
                    yield return new Token(line[i..j], Number);
                    i = j;
                    continue;
                }

                yield return new Token(line[i].ToString(), Plain);
                i++;
            }
            yield return new Token("\n", Plain);
        }
    }

    public static SolidColorBrush BrushFor(string _, string __) => Plain;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
