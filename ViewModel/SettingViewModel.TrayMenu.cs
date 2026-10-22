using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using WinExSpectrumTest.Effects;
using WinExSpectrumTest.Effects.Folia;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.ViewModel;

/// <summary>托盘"切换效果"子菜单（MVVM 绑定，见 Extensions/MenuFlyoutExtensions）。</summary>
public partial class SettingViewModel
{
    /// <summary>三个效果单选一组；folia 本体无勾选态，其激活状态由歌词模式子级的选中项体现。</summary>
    private const string EffectGroup = "TrayEffect";
    private const string FoliaLyricGroup = "TrayFoliaLyricMode";

    private ObservableCollection<MenuModel>? _trayEffectMenu;

    /// <summary>
    /// 托盘效果子菜单模型树：结构固定（EffectRegistry 目录 + FoliaSettings.LyricModes），
    /// 选中态由 <see cref="PrepareTrayEffectMenuCommand"/> 在每次展开前刷新。
    /// </summary>
    public ObservableCollection<MenuModel> TrayEffectMenu => _trayEffectMenu ??= CreateEffectMenu();

    /// <summary>菜单展开前刷新选中态；RadioMenuFlyoutItem 的勾选在重建时读取这里的最新值。</summary>
    [RelayCommand]
    private void PrepareTrayEffectMenu()
    {
        string active = EffectRegistry.Resolve(AppSettings.VisualEffect).Id;
        string lyricMode = AppSettings.FoliaLyricMode;
        foreach (var item in TrayEffectMenu)
        {
            if (item.Children is { } children)
            {
                foreach (var child in children)
                    if (child.Tag is string mode) child.IsChecked = mode == lyricMode;
            }
            else if (item.Tag is string id)
            {
                item.IsChecked = id == active;
            }
        }
    }

    [RelayCommand]
    private void SelectEffect(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        VisualEffect = id;
    }

    /// <summary>选择歌词模式即进入辞曲新境；重复点击当前模式也可单独进入该效果。</summary>
    [RelayCommand]
    private void SelectFoliaLyricMode(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        FoliaLyricMode = mode;
        VisualEffect = FoliaGpuEffect.EffectId;
    }

    private ObservableCollection<MenuModel> CreateEffectMenu()
    {
        string active = EffectRegistry.Resolve(AppSettings.VisualEffect).Id;
        string lyricMode = AppSettings.FoliaLyricMode;
        var menu = new ObservableCollection<MenuModel>();
        foreach (var (id, name) in EffectRegistry.GetCatalog())
        {
            if (id == FoliaGpuEffect.EffectId)
            {
                // folia 挂歌词模式子级；歌词模式名称使用无属性后缀的独立资源键。
                var resources = new ResourceLoader();
                var lyricItems = new MenuModel[FoliaSettings.LyricModes.Length];
                for (int i = 0; i < lyricItems.Length; i++)
                {
                    string mode = FoliaSettings.LyricModes[i];
                    lyricItems[i] = new MenuModel
                    {
                        Title = resources.GetString("FoliaModeName_" + mode),
                        Tag = mode,
                        GroupName = FoliaLyricGroup,
                        IsChecked = mode == lyricMode,
                        AutomationId = "SwitchEffect_" + id + "_" + mode,
                        Command = SelectFoliaLyricModeCommand
                    };
                }
                menu.Add(new MenuModel { Title = name, Tag = id, Children = lyricItems, AutomationId = "SwitchEffect_" + id });
            }
            else
            {
                menu.Add(new MenuModel
                {
                    Title = name,
                    Tag = id,
                    GroupName = EffectGroup,
                    IsChecked = id == active,
                    AutomationId = "SwitchEffect_" + id,
                    Command = SelectEffectCommand
                });
            }
        }
        return menu;
    }
}
