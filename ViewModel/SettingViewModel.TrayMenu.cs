using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using WinExSpectrumTest.Effects;
using WinExSpectrumTest.Effects.Folia;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.ViewModel;

/// <summary>主窗口与托盘共用的场景快捷切换。</summary>
public partial class SettingViewModel
{
    private const string EffectGroup = "QuickSceneEffect";
    private const string FoliaLyricGroup = "QuickSceneFoliaLyricMode";
    private const string MachiSceneGroup = "QuickSceneMachiScene";
    private static readonly string[] MachiScenes = ["auto", "konbini", "station", "kowloon"];

    private ObservableCollection<MenuModel>? _sceneMenu;
    private MenuModel[]? _foliaSceneMenu;
    private MenuModel[]? _machiSceneMenu;

    public string AuroraEffectName => EffectRegistry.GetDisplayName(AuroraRingEffect.EffectId);
    public string SonicEffectName => EffectRegistry.GetDisplayName(SonicTopographyEffect.EffectId);
    public string ActiveSceneName
    {
        get
        {
            string id = EffectRegistry.Resolve(AppSettings.VisualEffect).Id;
            string name = EffectRegistry.GetDisplayName(id);
            if (id != FoliaGpuEffect.EffectId) return name;
            name += " · " + CurrentFoliaModeName;
            return AppSettings.FoliaLyricMode == "machi" ? name + " · " + CurrentMachiSceneName : name;
        }
    }
    public string FoliaSceneMenuTitle => EffectRegistry.GetDisplayName(FoliaGpuEffect.EffectId) + " · " + CurrentFoliaModeName;
    public bool IsAuroraSelected => EffectRegistry.Resolve(AppSettings.VisualEffect).Id == AuroraRingEffect.EffectId;
    public bool IsSonicSelected => EffectRegistry.Resolve(AppSettings.VisualEffect).Id == SonicTopographyEffect.EffectId;
    private static string CurrentFoliaModeName =>
        new ResourceLoader().GetString("FoliaModeName_" + FoliaSettings.NormalizeLyricMode(AppSettings.FoliaLyricMode));
    private static string CurrentMachiSceneName =>
        new ResourceLoader().GetString("MachiSceneName_" + AppSettings.FoliaMachiScene);
    private static string MachiSceneMenuTitle =>
        new ResourceLoader().GetString("FoliaModeName_machi") + " · " + CurrentMachiSceneName;

    public ObservableCollection<MenuModel> SceneMenu => _sceneMenu ??= CreateSceneMenu();
    public IReadOnlyList<MenuModel> FoliaSceneMenu => _foliaSceneMenu ??= CreateFoliaSceneMenu();
    private IReadOnlyList<MenuModel> MachiSceneMenu => _machiSceneMenu ??= CreateMachiSceneMenu();

    [RelayCommand]
    private void PrepareSceneMenu()
    {
        string active = EffectRegistry.Resolve(AppSettings.VisualEffect).Id;
        string lyricMode = AppSettings.FoliaLyricMode;
        string machiScene = AppSettings.FoliaMachiScene;
        foreach (var item in SceneMenu)
        {
            if (item.Children is { } children)
            {
                foreach (var child in children)
                {
                    if (child.Tag is not string mode) continue;
                    child.IsChecked = mode == lyricMode;
                    child.IsEnabled = mode != "machi" || MachiAvailable;
                    if (mode != "machi" || child.Children is not { } machiItems) continue;
                    child.Title = MachiSceneMenuTitle;
                    foreach (var sceneItem in machiItems)
                        if (sceneItem.Tag is string scene)
                            sceneItem.IsChecked = scene == machiScene;
                }
            }
            else if (item.Tag is string id)
            {
                item.IsChecked = id == active;
            }
        }
    }

    private void NotifySceneSelection()
    {
        OnPropertyChanged(nameof(ActiveSceneName));
        OnPropertyChanged(nameof(FoliaSceneMenuTitle));
        OnPropertyChanged(nameof(IsAuroraSelected));
        OnPropertyChanged(nameof(IsSonicSelected));
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
        if (string.IsNullOrEmpty(mode) || (mode == "machi" && !MachiAvailable)) return;
        FoliaLyricMode = mode;
        VisualEffect = FoliaGpuEffect.EffectId;
    }

    [RelayCommand]
    private void SelectFoliaMachiScene(string? scene)
    {
        if (string.IsNullOrEmpty(scene) || !MachiAvailable) return;
        FoliaMachiScene = scene;
        FoliaLyricMode = "machi";
        VisualEffect = FoliaGpuEffect.EffectId;
    }

    private ObservableCollection<MenuModel> CreateSceneMenu()
    {
        string active = EffectRegistry.Resolve(AppSettings.VisualEffect).Id;
        var menu = new ObservableCollection<MenuModel>();
        foreach (var (id, name) in EffectRegistry.GetCatalog())
        {
            if (id == FoliaGpuEffect.EffectId)
            {
                menu.Add(new MenuModel { Title = name, Tag = id, Children = FoliaSceneMenu, AutomationId = "SwitchEffect_" + id });
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

    private MenuModel[] CreateFoliaSceneMenu()
    {
        var resources = new ResourceLoader();
        string lyricMode = AppSettings.FoliaLyricMode;
        var items = new MenuModel[FoliaSettings.LyricModes.Length];
        for (int i = 0; i < items.Length; i++)
        {
            string mode = FoliaSettings.LyricModes[i];
            if (mode == "machi")
            {
                items[i] = new MenuModel
                {
                    Title = MachiSceneMenuTitle,
                    Tag = mode,
                    IsEnabled = MachiAvailable,
                    Children = MachiSceneMenu,
                    AutomationId = "SwitchEffect_" + FoliaGpuEffect.EffectId + "_" + mode
                };
                continue;
            }
            items[i] = new MenuModel
            {
                Title = resources.GetString("FoliaModeName_" + mode),
                Tag = mode,
                GroupName = FoliaLyricGroup,
                IsChecked = mode == lyricMode,
                AutomationId = "SwitchEffect_" + FoliaGpuEffect.EffectId + "_" + mode,
                Command = SelectFoliaLyricModeCommand
            };
        }
        return items;
    }

    private MenuModel[] CreateMachiSceneMenu()
    {
        var resources = new ResourceLoader();
        var items = new MenuModel[MachiScenes.Length];
        for (int i = 0; i < items.Length; i++)
        {
            string scene = MachiScenes[i];
            items[i] = new MenuModel
            {
                Title = resources.GetString("MachiSceneName_" + scene),
                Tag = scene,
                GroupName = MachiSceneGroup,
                IsChecked = scene == AppSettings.FoliaMachiScene,
                AutomationId = "SwitchEffect_" + FoliaGpuEffect.EffectId + "_machi_" + scene,
                Command = SelectFoliaMachiSceneCommand
            };
        }
        return items;
    }
}
