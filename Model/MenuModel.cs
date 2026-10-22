using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinExSpectrumTest.Model;

/// <summary>
/// 托盘菜单树节点（配合 <c>Extensions/MenuFlyoutExtensions</c> 使用）。
/// <see cref="Children"/> 非空时渲染为子菜单；<see cref="GroupName"/> 非空时渲染为单选项。
/// </summary>
public partial class MenuModel : ObservableObject
{
    private string? _title;
    public string? Title { get => _title; set => SetProperty(ref _title, value); }

    private bool _isEnabled = true;
    /// <summary>菜单项是否可用；置灰但保留条目，让用户知道该操作暂时不可用。</summary>
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }

    private bool _isChecked;
    /// <summary>单选项（<see cref="GroupName"/> 非空）的选中态。</summary>
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }

    /// <summary>传给 <see cref="Command"/> 的参数，同时作为条目的稳定标识。</summary>
    public object? Tag { get; set; }

    /// <summary>非空时创建 RadioMenuFlyoutItem，同组内互斥。</summary>
    public string? GroupName { get; set; }

    /// <summary>可选的 UIA AutomationId，供自动化验证定位生成的菜单项。</summary>
    public string? AutomationId { get; set; }

    public ICommand? Command { get; set; }

    public IReadOnlyList<MenuModel>? Children { get; set; }
}
