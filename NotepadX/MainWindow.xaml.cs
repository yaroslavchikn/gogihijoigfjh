using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NotepadX
{
    public partial class MainWindow : Window
    {
        private class DocInfo
        {
            public string? FilePath;
            public bool IsModified;
        }

        private bool _isDark = true;
        private bool _wordWrap = false;
        private double _fontSize = 15;
        private bool _isFullscreen = false;
        private WindowState _previousState = WindowState.Normal;
        private readonly DispatcherTimer _autoSaveTimer;

        public MainWindow()
        {
            InitializeComponent();
            LoadFonts();
            NewDocument();

            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _autoSaveTimer.Tick += AutoSave_Tick;
            _autoSaveTimer.Start();
        }

        // ====== Шрифты ======
        private void LoadFonts()
        {
            FontCombo.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList();
            FontCombo.SelectedItem = "Consolas";
        }

        private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontCombo.SelectedItem is string fontName)
            {
                var family = new FontFamily(fontName);
                foreach (TabItem tab in tabs.Items)
                {
                    if (tab.Content is TextBox tb) tb.FontFamily = family;
                }
            }
        }

        // ====== Документы ======
        private TextBox CreateTextBox()
        {
            var tb = new TextBox();
            tb.Style = (Style)FindResource("EditorBox");
            tb.FontSize = _fontSize;
            tb.TextWrapping = _wordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (FontCombo.SelectedItem is string fontName) tb.FontFamily = new FontFamily(fontName);
            tb.TextChanged += Editor_TextChanged;
            tb.SelectionChanged += Editor_SelectionChanged;
            tb.PreviewMouseWheel += Editor_PreviewMouseWheel;
            return tb;
        }

        private TabItem CreateTabItem(string header, TextBox textBox)
        {
            var tab = new TabItem();
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var title = new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center };
            var closeBtn = new Button
            {
                Content = "✕",
                FontSize = 10,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(2),
                Focusable = false
            };
            closeBtn.Click += (s, e) => CloseTab(tab);
            panel.Children.Add(title);
            panel.Children.Add(closeBtn);
            tab.Header = panel;
            tab.Content = textBox;
            tab.Tag = new DocInfo();
            return tab;
        }

        private void NewDocument()
        {
            var tb = CreateTextBox();
            var tab = CreateTabItem("Без имени", tb);
            tabs.Items.Add(tab);
            tabs.SelectedItem = tab;
            tb.Focus();
        }

        private void AddTab_Click(object sender, RoutedEventArgs e) => NewDocument();

        private void CloseTab(TabItem tab)
        {
            if (tab.Tag is DocInfo info && info.IsModified)
            {
                var result = MessageBox.Show("Файл изменён. Сохранить?", "NotepadX",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes)
                {
                    tabs.SelectedItem = tab;
                    if (!SaveFile(false)) return;
                }
            }
            tabs.Items.Remove(tab);
            if (tabs.Items.Count == 0) NewDocument();
        }

        private TextBox? GetCurrentTextBox()
        {
            return (tabs.SelectedItem as TabItem)?.Content as TextBox;
        }

        private DocInfo? GetCurrentDoc()
        {
            return (tabs.SelectedItem as TabItem)?.Tag as DocInfo;
        }

        private void UpdateTabTitle(TabItem tab)
        {
            if (tab.Tag is DocInfo info && tab.Header is StackPanel panel && panel.Children[0] is TextBlock title)
            {
                var name = info.FilePath != null ? Path.GetFileName(info.FilePath) : "Без имени";
                title.Text = info.IsModified ? name + " •" : name;
            }
        }

        // ====== Файлы ======
        private void New_Click(object sender, RoutedEventArgs e) => NewDocument();

        private void Open_Click(object sender, RoutedEventArgs e) => OpenFile();

        private void OpenFile(string? path = null)
        {
            if (path == null)
            {
                var dlg = new OpenFileDialog();
                if (dlg.ShowDialog() != true) return;
                path = dlg.FileName;
            }

            try
            {
                var text = File.ReadAllText(path);
                var tb = CreateTextBox();
                tb.Text = text;
                var tab = CreateTabItem(Path.GetFileName(path), tb);
                ((DocInfo)tab.Tag).FilePath = path;
                tabs.Items.Add(tab);
                tabs.SelectedItem = tab;
                UpdateTabTitle(tab);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка открытия: " + ex.Message, "NotepadX", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool SaveFile(bool saveAs)
        {
            var tab = tabs.SelectedItem as TabItem;
            var tb = GetCurrentTextBox();
            var info = GetCurrentDoc();
            if (tab == null || tb == null || info == null) return false;

            if (saveAs || info.FilePath == null)
            {
                var dlg = new SaveFileDialog();
                if (info.FilePath != null) dlg.FileName = info.FilePath;
                if (dlg.ShowDialog() != true) return false;
                info.FilePath = dlg.FileName;
            }

            try
            {
                File.WriteAllText(info.FilePath, tb.Text, Encoding.UTF8);
                info.IsModified = false;
                UpdateTabTitle(tab);
                UpdateTitle();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка сохранения: " + ex.Message, "NotepadX", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) => SaveFile(false);
        private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveFile(true);

        // ====== Автосохранение ======
        private void AutoSave_Tick(object? sender, EventArgs e)
        {
            foreach (TabItem tab in tabs.Items)
            {
                if (tab.Tag is DocInfo info && info.IsModified && info.FilePath != null)
                {
                    try { File.WriteAllText(info.FilePath, ((TextBox)tab.Content).Text, Encoding.UTF8); }
                    catch { /* игнорируем */ }
                }
            }
        }

        // ====== Поиск и замена ======
        private void ShowSearchPanel()
        {
            SearchPanel.Visibility = Visibility.Visible;
            var animY = new DoubleAnimation(-60, 0, TimeSpan.FromSeconds(0.25))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            SearchPanelTransform.BeginAnimation(TranslateTransform.YProperty, animY);
            var animOp = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25));
            SearchPanel.BeginAnimation(OpacityProperty, animOp);
            SearchTextBox.Focus();
        }

        private void CloseSearch_Click(object sender, RoutedEventArgs e)
        {
            var animY = new DoubleAnimation(0, -60, TimeSpan.FromSeconds(0.2))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            animY.Completed += (s, e) => SearchPanel.Visibility = Visibility.Collapsed;
            SearchPanelTransform.BeginAnimation(TranslateTransform.YProperty, animY);
            var animOp = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2));
            SearchPanel.BeginAnimation(OpacityProperty, animOp);
        }

        private void Find_Click(object sender, RoutedEventArgs e) => ShowSearchPanel();

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SearchNext();
        }

        private void SearchNext_Click(object sender, RoutedEventArgs e) => SearchNext();

        private void SearchNext()
        {
            var tb = GetCurrentTextBox();
            if (tb == null || string.IsNullOrEmpty(SearchTextBox.Text)) return;

            var text = tb.Text;
            var search = SearchTextBox.Text;
            var comparison = CaseCheckBox.IsChecked == true
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            int startIndex = tb.SelectionStart + tb.SelectionLength;
            int index = text.IndexOf(search, startIndex, comparison);

            if (WordCheckBox.IsChecked == true)
            {
                while (index >= 0 && !IsWholeWord(text, index, search.Length))
                {
                    index = text.IndexOf(search, index + 1, comparison);
                }
            }

            if (index >= 0)
            {
                tb.Select(index, search.Length);
                tb.ScrollToCaret();
            }
            else
            {
                tb.Select(0, 0);
            }
        }

        private bool IsWholeWord(string text, int index, int length)
        {
            bool beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            int end = index + length;
            bool afterOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            return beforeOk && afterOk;
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            var tb = GetCurrentTextBox();
            if (tb == null || string.IsNullOrEmpty(SearchTextBox.Text)) return;

            if (tb.SelectionLength > 0 &&
                tb.SelectedText.Equals(SearchTextBox.Text,
                    CaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            {
                tb.SelectedText = ReplaceTextBox.Text;
            }
            SearchNext();
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            var tb = GetCurrentTextBox();
            if (tb == null || string.IsNullOrEmpty(SearchTextBox.Text)) return;

            var text = tb.Text;
            var search = SearchTextBox.Text;
            var replace = ReplaceTextBox.Text;
            var comparison = CaseCheckBox.IsChecked == true
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            int count = 0;
            int index = 0;
            var sb = new StringBuilder();

            while (index < text.Length)
            {
                int found = text.IndexOf(search, index, comparison);
                if (found < 0) { sb.Append(text, index, text.Length - index); break; }

                if (WordCheckBox.IsChecked == true && !IsWholeWord(text, found, search.Length))
                {
                    sb.Append(text, index, found + search.Length - index);
                    index = found + search.Length;
                    continue;
                }

                sb.Append(text, index, found - index);
                sb.Append(replace);
                index = found + search.Length;
                count++;
            }

            if (count > 0)
            {
                tb.Text = sb.ToString();
                MarkModified();
            }
            MessageBox.Show($"Заменено: {count}", "NotepadX", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ====== Редактирование ======
        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            MarkModified();
            UpdateStatus();
        }

        private void MarkModified()
        {
            var info = GetCurrentDoc();
            var tab = tabs.SelectedItem as TabItem;
            if (info != null && !info.IsModified)
            {
                info.IsModified = true;
                if (tab != null) UpdateTabTitle(tab);
                UpdateTitle();
            }
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateStatus();

        private void UpdateStatus()
        {
            var tb = GetCurrentTextBox();
            if (tb == null) return;

            int caret = tb.CaretIndex;
            int line = tb.GetLineIndexFromCharacterIndex(caret);
            int col = caret - tb.GetCharacterIndexFromLineIndex(line);
            StatusPos.Text = $"Стр {line + 1}, Стлб {col + 1}";

            var text = tb.Text;
            int words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            StatusWords.Text = $"Слов: {words}";
            StatusChars.Text = $"Символов: {text.Length}";
        }

        private void UpdateTitle()
        {
            var info = GetCurrentDoc();
            if (info != null)
            {
                var name = info.FilePath != null ? Path.GetFileName(info.FilePath) : "Без имени";
                TitleText.Text = info.IsModified ? name + " •" : name;
            }
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatus();
            UpdateTitle();
        }

        // ====== Масштаб и перенос ======
        private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                _fontSize = Math.Clamp(_fontSize + (e.Delta > 0 ? 1 : -1), 8, 72);
                ApplyFontSize();
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _fontSize = Math.Min(_fontSize + 1, 72);
            ApplyFontSize();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _fontSize = Math.Max(_fontSize - 1, 8);
            ApplyFontSize();
        }

        private void ApplyFontSize()
        {
            foreach (TabItem tab in tabs.Items)
            {
                if (tab.Content is TextBox tb) tb.FontSize = _fontSize;
            }
            StatusZoom.Text = $"{(int)(_fontSize / 15.0 * 100)}%";
        }

        private void WordWrap_Click(object sender, RoutedEventArgs e)
        {
            _wordWrap = !_wordWrap;
            foreach (TabItem tab in tabs.Items)
            {
                if (tab.Content is TextBox tb)
                {
                    tb.TextWrapping = _wordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
                    tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    tb.HorizontalScrollBarVisibility = _wordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
                }
            }
        }

        // ====== Тема ======
        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            _isDark = !_isDark;
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            var res = Application.Current.Resources;
            if (_isDark)
            {
                res["BackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x1A));
                res["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
                res["SurfaceLightBrush"] = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));
                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
                res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));
                SunIcon.Visibility = Visibility.Collapsed;
                MoonIcon.Visibility = Visibility.Visible;
            }
            else
            {
                res["BackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
                res["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                res["SurfaceLightBrush"] = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                SunIcon.Visibility = Visibility.Visible;
                MoonIcon.Visibility = Visibility.Collapsed;
            }
        }

        // ====== Полный экран ======
        private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

        private void ToggleFullscreen()
        {
            _isFullscreen = !_isFullscreen;
            if (_isFullscreen)
            {
                _previousState = WindowState;
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.CanResize;
                WindowState = _previousState;
            }
        }

        // ====== Окно ======
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Max_Click(sender, e);
                return;
            }
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                Left = e.GetPosition(this).X - Width / 2;
                Top = e.GetPosition(this).Y - 10;
            }
            DragMove();
        }

        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Max_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            bool hasModified = tabs.Items.Cast<TabItem>().Any(t => (t.Tag as DocInfo)?.IsModified == true);
            if (hasModified)
            {
                var result = MessageBox.Show("Есть несохранённые файлы. Выйти?", "NotepadX",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No) return;
            }
            Application.Current.Shutdown();
        }

        // ====== Горячие клавиши ======
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (ctrl && !shift && e.Key == Key.N) { NewDocument(); e.Handled = true; }
            else if (ctrl && !shift && e.Key == Key.O) { OpenFile(); e.Handled = true; }
            else if (ctrl && !shift && e.Key == Key.S) { SaveFile(false); e.Handled = true; }
            else if (ctrl && shift && e.Key == Key.S) { SaveFile(true); e.Handled = true; }
            else if (ctrl && !shift && e.Key == Key.F) { ShowSearchPanel(); e.Handled = true; }
            else if (ctrl && !shift && e.Key == Key.T) { NewDocument(); e.Handled = true; }
            else if (ctrl && !shift && e.Key == Key.W) { if (tabs.SelectedItem is TabItem tab) CloseTab(tab); e.Handled = true; }
            else if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
            else if (e.Key == Key.Escape && SearchPanel.Visibility == Visibility.Visible) { CloseSearch_Click(sender, e); e.Handled = true; }
        }

        // ====== Drag & Drop ======
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var file in files)
                {
                    if (File.Exists(file)) OpenFile(file);
                }
            }
        }
    }
}
