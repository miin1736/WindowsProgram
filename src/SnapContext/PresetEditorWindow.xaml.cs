using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapContext;

/// <summary>
/// 프리셋 편집 창. 트레이 메뉴에서 사용자가 직접 열 때만 나타나므로 캡처 흐름과는 무관하다(규칙 A-1/A-2 영향 없음).
/// 편집은 복사본에서 하고 "저장"을 눌러야 파일에 반영된다. 저장하지 않은 변경이 있으면 닫을 때 물어 본다.
/// </summary>
public partial class PresetEditorWindow : Window
{
    private sealed class Item
    {
        public string Id = "";
        public string Name = "";
        public string Caption = "";
    }

    private readonly PresetStore _store;
    private readonly Action<PresetSet>? _onSaved;
    private readonly List<Item> _items = new();
    private string _defaultId = PresetStore.NonePresetId;
    private bool _loading;
    private bool _dirty;
    private bool _forceClose;

    public PresetEditorWindow(PresetStore store, Action<PresetSet>? onSaved = null)
    {
        InitializeComponent();
        _store = store;
        _onSaved = onSaved;
        LoadFrom(_store.Load());
    }

    private void LoadFrom(PresetSet set)
    {
        _items.Clear();
        _items.AddRange(set.Presets.Select(p => new Item { Id = p.Id, Name = p.Name, Caption = p.Caption }));
        _defaultId = set.DefaultPresetId;
        _dirty = false;
        Rebuild(_items.Count > 0 ? 0 : -1);
    }

    private PresetSet Snapshot() => new(
        _items.Select(i => new Preset(i.Id, i.Name.Trim(), i.Caption.Trim())).ToList(),
        _defaultId);

    private int Index => PresetList.SelectedIndex;
    private Item? Current => Index >= 0 && Index < _items.Count ? _items[Index] : null;

    /// <summary>목록, 편집 칸, 기본 프리셋 선택 상자를 현재 데이터에 맞춰 다시 그린다.</summary>
    private void Rebuild(int select)
    {
        _loading = true;
        try
        {
            PresetList.Items.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                PresetList.Items.Add(Label(i));
            }

            if (select >= _items.Count)
            {
                select = _items.Count - 1;
            }

            PresetList.SelectedIndex = select;
            RebuildDefaultBox();
            ShowCurrent();
            UpdateButtons();
        }
        finally
        {
            _loading = false;
        }
    }

    private static string DisplayName(Item item) =>
        string.IsNullOrWhiteSpace(item.Name) ? "(이름 없음)" : item.Name.Trim();

    private string Label(int i) =>
        $"{i + 1}. {DisplayName(_items[i])}" + (_items[i].Id == _defaultId ? "  ★" : "");

    private void RebuildDefaultBox()
    {
        DefaultBox.Items.Clear();
        DefaultBox.Items.Add("설명 없음");
        int selected = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            DefaultBox.Items.Add($"{i + 1}. {DisplayName(_items[i])}");
            if (_items[i].Id == _defaultId)
            {
                selected = i + 1;
            }
        }

        if (selected == 0)
        {
            _defaultId = PresetStore.NonePresetId;
        }

        DefaultBox.SelectedIndex = selected;
    }

    private void ShowCurrent()
    {
        var item = Current;
        bool has = item is not null;
        EditPanel.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        if (item is null)
        {
            return;
        }

        NameBox.Text = item.Name;
        CaptionBox.Text = item.Caption;
        UpdateCounter();
    }

    private void UpdateCounter() =>
        CounterText.Text = $"{CaptionBox.Text.Length} / {PresetStore.MaxCaptionLength}";

    private void UpdateButtons()
    {
        AddButton.IsEnabled = _items.Count < PresetStore.MaxPresets;
        DeleteButton.IsEnabled = Current is not null;
        UpButton.IsEnabled = Index > 0;
        DownButton.IsEnabled = Index >= 0 && Index < _items.Count - 1;
    }

    private void SetStatus(string message, bool error)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(error ? Color.FromRgb(0xC6, 0x28, 0x28) : Color.FromRgb(0x2E, 0x7D, 0x32));
    }

    private void MarkDirty()
    {
        _dirty = true;
        StatusText.Text = "";
    }

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            ShowCurrent();
            UpdateButtons();
        }
        finally
        {
            _loading = false;
        }
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Current is not { } item)
        {
            return;
        }

        item.Name = NameBox.Text;
        MarkDirty();
        RefreshLabels();
    }

    private void CaptionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCounter();
        if (_loading || Current is not { } item)
        {
            return;
        }

        item.Caption = CaptionBox.Text;
        MarkDirty();
    }

    /// <summary>이름을 고칠 때 선택이 풀리지 않도록 항목 글자만 바꾼다.</summary>
    private void RefreshLabels()
    {
        _loading = true;
        try
        {
            int sel = Index;
            for (int i = 0; i < _items.Count; i++)
            {
                PresetList.Items[i] = Label(i);
            }

            PresetList.SelectedIndex = sel;
            RebuildDefaultBox();
        }
        finally
        {
            _loading = false;
        }
    }

    private void DefaultBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefaultBox.SelectedIndex < 0)
        {
            return;
        }

        _defaultId = DefaultBox.SelectedIndex == 0 ? PresetStore.NonePresetId : _items[DefaultBox.SelectedIndex - 1].Id;
        MarkDirty();

        _loading = true;
        try
        {
            int sel = Index;
            for (int i = 0; i < _items.Count; i++)
            {
                PresetList.Items[i] = Label(i);
            }

            PresetList.SelectedIndex = sel;
        }
        finally
        {
            _loading = false;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count >= PresetStore.MaxPresets)
        {
            return;
        }

        var ids = new HashSet<string>(_items.Select(i => i.Id));
        _items.Add(new Item { Id = PresetStore.NewId(ids), Name = "새 프리셋", Caption = "" });
        MarkDirty();
        Rebuild(_items.Count - 1);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } item)
        {
            return;
        }

        int idx = Index;
        _items.RemoveAt(idx);
        if (_defaultId == item.Id)
        {
            _defaultId = PresetStore.NonePresetId;
        }

        MarkDirty();
        Rebuild(Math.Min(idx, _items.Count - 1));
    }

    private void UpButton_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void DownButton_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int delta)
    {
        int idx = Index;
        int target = idx + delta;
        if (idx < 0 || target < 0 || target >= _items.Count)
        {
            return;
        }

        (_items[idx], _items[target]) = (_items[target], _items[idx]);
        MarkDirty();
        Rebuild(target);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "지금 편집한 내용이 사라지고 기본 프리셋 4개로 바뀝니다(저장하기 전까지 파일은 그대로입니다). 계속할까요?",
            "SnapContext",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        LoadFrom(PresetStore.Defaults());
        _dirty = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => TrySaveNow();

    private bool TrySaveNow()
    {
        var set = Snapshot();
        if (_store.TrySave(set, out var error))
        {
            _dirty = false;
            SetStatus("✓ 저장했습니다. 다음 캡처부터 적용됩니다.", error: false);
            _onSaved?.Invoke(set);
            return true;
        }

        SetStatus("⚠ " + error, error: true);
        return false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose || !_dirty)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "저장하지 않은 변경이 있습니다. 저장할까요?",
            "SnapContext",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !TrySaveNow()))
        {
            e.Cancel = true;
        }
    }

    /// <summary>시험용: 저장 확인 없이 닫는다.</summary>
    public void CloseWithoutPrompt()
    {
        _forceClose = true;
        Close();
    }
}
