using System.Collections.Generic;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Extensions;

/// <summary>
/// MenuFlyout / MenuFlyoutSubItem 的 ItemsSource 绑定扩展，移植自 music_player 的同名扩展。
/// 挂在 MenuFlyout 上时在 Opening 前重建；挂在 MenuFlyoutSubItem 上时（子菜单没有 Opening 事件）
/// 在其 Loaded——即所属菜单每次展开、条目进入可视化树时——重建。重建前先执行 PrepareCommand，
/// 让 ViewModel 刷新模型（选中态、可用性），再从模型生成菜单项。
/// 只重建宿主自身的 Items，同一菜单中手工声明的静态条目不受影响。
/// </summary>
public static class MenuFlyoutExtensions
{
    public static readonly DependencyProperty PrepareCommandProperty =
        DependencyProperty.RegisterAttached("PrepareCommand", typeof(ICommand), typeof(MenuFlyoutExtensions), new PropertyMetadata(null));

    public static void SetPrepareCommand(DependencyObject target, ICommand value) => target.SetValue(PrepareCommandProperty, value);

    public static ICommand GetPrepareCommand(DependencyObject target) => (ICommand)target.GetValue(PrepareCommandProperty);

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.RegisterAttached("ItemsSource", typeof(IEnumerable<MenuModel>), typeof(MenuFlyoutExtensions), new PropertyMetadata(null, OnItemsSourceChanged));

    public static void SetItemsSource(DependencyObject element, IEnumerable<MenuModel> value) => element.SetValue(ItemsSourceProperty, value);

    public static IEnumerable<MenuModel> GetItemsSource(DependencyObject element) => (IEnumerable<MenuModel>)element.GetValue(ItemsSourceProperty);

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        switch (d)
        {
            case MenuFlyout flyout:
                flyout.Opening -= OnFlyoutOpening;
                flyout.Opening += OnFlyoutOpening;
                Rebuild(flyout);
                break;
            case MenuFlyoutSubItem subItem:
                subItem.Loaded -= OnSubItemLoaded;
                subItem.Loaded += OnSubItemLoaded;
                Rebuild(subItem);
                break;
        }
    }

    private static void OnFlyoutOpening(object? sender, object e)
    {
        if (sender is MenuFlyout flyout) Rebuild(flyout);
    }

    private static void OnSubItemLoaded(object sender, RoutedEventArgs e) => Rebuild((MenuFlyoutSubItem)sender);

    private static void Rebuild(DependencyObject host)
    {
        var models = GetItemsSource(host);
        // Host 类型在 OnItemsSourceChanged 已过滤；此处再取一次以兼容任意宿主调用。
        IList<MenuFlyoutItemBase>? items = (host as MenuFlyout)?.Items ?? (host as MenuFlyoutSubItem)?.Items;
        if (models == null || items == null) return;
        var prepare = GetPrepareCommand(host);
        if (prepare?.CanExecute(null) == true) prepare.Execute(null);
        items.Clear();
        foreach (var model in models)
        {
            var item = CreateItem(model);
            if (item != null) items.Add(item);
        }
    }

    private static MenuFlyoutItemBase? CreateItem(MenuModel model)
    {
        if (model == null) return null;

        if (model.Children is { Count: > 0 } children)
        {
            var subItem = new MenuFlyoutSubItem { Text = model.Title ?? string.Empty, IsEnabled = model.IsEnabled };
            SetAutomationId(subItem, model);
            foreach (var child in children)
            {
                var childItem = CreateItem(child);
                if (childItem != null) subItem.Items.Add(childItem);
            }
            return subItem;
        }

        MenuFlyoutItem item = model.GroupName is null
            ? new MenuFlyoutItem { Text = model.Title ?? string.Empty, IsEnabled = model.IsEnabled }
            : new RadioMenuFlyoutItem
            {
                Text = model.Title ?? string.Empty,
                IsEnabled = model.IsEnabled,
                GroupName = model.GroupName,
                IsChecked = model.IsChecked
            };
        item.Command = model.Command;
        item.CommandParameter = model.Tag;
        SetAutomationId(item, model);
        return item;
    }

    private static void SetAutomationId(MenuFlyoutItemBase item, MenuModel model)
    {
        if (!string.IsNullOrEmpty(model.AutomationId)) AutomationProperties.SetAutomationId(item, model.AutomationId);
    }
}
