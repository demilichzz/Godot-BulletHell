using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>选关视图只提交语义请求并显示指定选项，不决定导航或场景切换。</summary>
public interface IBossSelectionView
{
    /// <summary>请求选择目录下标。</summary>
    event Action<int>? SelectionRequested;
    /// <summary>请求按网格方向导航。</summary>
    event Action<Vector2I>? NavigationRequested;
    /// <summary>请求确认当前选项。</summary>
    event Action? ConfirmationRequested;
    /// <summary>请求修改AI加入选项。</summary>
    event Action<bool>? AISelectionRequested;
    /// <summary>显示控制器确定的选项。</summary>
    /// <param name="index">有效目录下标。</param>
    void ShowSelection(int index);
}

/// <summary>选关业务控制器，持有选择状态与网格规则，不引用具体控件。</summary>
public sealed class BossSelectionController : IMenuActions, IDisposable
{
    // 冻结目录标识和网格宽度；视图仅接收显示结果。
    private readonly string[] _ids;
    private readonly int _columns;
    private readonly IBossSelectionView _view;
    private bool _disposed;
    /// <summary>当前目录下标，空目录为-1。</summary>
    public int SelectedIndex { get; private set; } = -1;
    /// <summary>有效选择变化，供场景记忆选择。</summary>
    public event Action<string>? SelectionChanged;
    /// <summary>确认有效Boss，供场景请求切换。</summary>
    public event Action<string>? Confirmed;
    /// <summary>转发用户的AI加入选择。</summary>
    public event Action<bool>? AISelectionChanged;
    /// <summary>连接视图请求与选关业务。</summary>
    /// <param name="ids">有序Boss标识。</param>
    /// <param name="columns">正数网格列数。</param>
    /// <param name="view">可替换的视图接口。</param>
    public BossSelectionController(IEnumerable<string> ids, int columns, IBossSelectionView view)
    {
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        _ids = ids.ToArray(); _columns = columns; _view = view;
        view.SelectionRequested += Select; view.NavigationRequested += Navigate;
        view.ConfirmationRequested += Confirm; view.AISelectionRequested += SetAI;
    }
    /// <summary>恢复上次选择，缺失标识时选第一项，空目录保持无选择。</summary>
    /// <param name="id">上次Boss标识。</param>
    public void Restore(string id) => Select(Math.Max(0, Array.IndexOf(_ids, id)));
    /// <summary>校验选择并通知显示与场景，不触发确认。</summary>
    /// <param name="index">目录零基下标。</param>
    public void Select(int index)
    {
        if (_disposed || index < 0 || index >= _ids.Length) return;
        SelectedIndex = index; _view.ShowSelection(index); SelectionChanged?.Invoke(_ids[index]);
    }
    /// <summary>保持边缘不环绕和末行缺项规则。</summary>
    /// <param name="direction">单位网格方向，右下为正。</param>
    public void Navigate(Vector2I direction)
    {
        if (_disposed || SelectedIndex < 0) return;
        // 当前列和候选项共同决定边界，禁止水平跨行。
        int column = SelectedIndex % _columns, target = SelectedIndex;
        if (direction == Vector2I.Left && column > 0) target--;
        else if (direction == Vector2I.Right && column < _columns - 1 && target + 1 < _ids.Length) target++;
        else if (direction == Vector2I.Up && target >= _columns) target -= _columns;
        else if (direction == Vector2I.Down && (target / _columns + 1) * _columns < _ids.Length) target = Math.Min(target + _columns, _ids.Length - 1);
        Select(target);
    }
    /// <summary>空目录不确认；只有行为层决定当前Boss标识。</summary>
    public void Confirm() { if (!_disposed && SelectedIndex >= 0) Confirmed?.Invoke(_ids[SelectedIndex]); }
    /// <summary>接收视图AI选项请求。</summary>
    /// <param name="enabled">是否加入AI。</param>
    private void SetAI(bool enabled) { if (!_disposed) AISelectionChanged?.Invoke(enabled); }
    /// <summary>将语义菜单动作路由到选关行为，返回操作交由其他场景处理。</summary>
    /// <param name="action">已经解码的操作。</param>
    /// <returns>当前菜单是否接收该动作。</returns>
    public bool Handle(MenuAction action)
    {
        if (_disposed) return false;
        switch (action)
        {
            case MenuAction.Left: Navigate(Vector2I.Left); break;
            case MenuAction.Right: Navigate(Vector2I.Right); break;
            case MenuAction.Up: Navigate(Vector2I.Up); break;
            case MenuAction.Down: Navigate(Vector2I.Down); break;
            case MenuAction.Confirm: Confirm(); break;
            default: return false;
        }
        return true;
    }
    /// <summary>离场解除请求订阅，旧控制器不会继续触发业务。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view.SelectionRequested -= Select; _view.NavigationRequested -= Navigate;
        _view.ConfirmationRequested -= Confirm; _view.AISelectionRequested -= SetAI;
    }
}
