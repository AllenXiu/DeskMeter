using DeskMeter.Core.Config;
using DeskMeter.Core.Data;

namespace DeskMeter.Core.Objects;

/// <summary>渲染上下文：携带数据快照、配置与输出布局。</summary>
public sealed class RenderContext
{
    public RenderContext(SystemSnapshot data, ConfigSettings settings, WidgetLayout layout)
    {
        Data = data;
        Settings = settings;
        Layout = layout;
        CurrentBrush = ColorParser.Parse(settings.GetDefaultColor());
    }

    public SystemSnapshot Data { get; }

    public ConfigSettings Settings { get; }

    public WidgetLayout Layout { get; }

    /// <summary>当前文字颜色（颜色变量修改，换行不重置——与 Conky 行为一致）。</summary>
    public WidgetBrush CurrentBrush { get; set; }
}

/// <summary>
/// 对象树节点（≈ Conky text_object）：每个变量/文本片段一个节点，Print 回调输出到布局。
/// </summary>
public abstract class ObjectNode
{
    public abstract void Print(RenderContext ctx);
}
