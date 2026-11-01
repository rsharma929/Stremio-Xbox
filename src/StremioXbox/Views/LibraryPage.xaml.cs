using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Stremio.Core.Api;
using StremioXbox.Services;
using Windows.Gaming.Input;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace StremioXbox.Views
{
    public sealed class LibraryRow
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public ObservableCollection<LibraryItem> Items { get; } =
            new ObservableCollection<LibraryItem>();

        public Visibility RowVisibility =>
            Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public sealed partial class LibraryPage : Page
    {
        private readonly ObservableCollection<LibraryRow> _rows =
            new ObservableCollection<LibraryRow>();
        private readonly Dictionary<string, int> _selectedIndexes =
            new Dictionary<string, int>();
        private readonly DispatcherTimer _gamepadTimer;

        private bool _loaded;
        private bool _horizontalHeld;
        private bool _verticalHeld;

        private static readonly SolidColorBrush SelectedPosterBrush =
            new SolidColorBrush(Color.FromArgb(255, 192, 32, 255));
        private static readonly SolidColorBrush TransparentBrush =
            new SolidColorBrush(Colors.Transparent);

        public LibraryPage()
        {
            InitializeComponent();
            RequiresPointer = Windows.UI.Xaml.Controls.RequiresPointer.Never;
            App.SetPointerInputEnabled(false);
            Rows.ItemsSource = _rows;

            _gamepadTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _gamepadTimer.Tick += GamepadTimer_Tick;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RequiresPointer = Windows.UI.Xaml.Controls.RequiresPointer.Never;
            App.SetPointerInputEnabled(false);
            _horizontalHeld = false;
            _verticalHeld = false;
            if (_loaded)
            {
                RestoreCurrentSelection();
                return;
            }

            _loaded = true;
            await LoadAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _gamepadTimer.Stop();
            base.OnNavigatedFrom(e);
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            if (!AppServices.Settings.IsLoggedIn)
            {
                ShowMessage("Log in under Settings to see your Stremio library.");
                Loading.IsActive = false;
                Loading.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var items = await AppServices.Api.GetLibraryAsync(
                    AppServices.Settings.AuthKey);
                var visible = items
                    .Where(i => !i.Removed && !i.Temp)
                    .OrderByDescending(i => i.ModifiedTime ?? DateTime.MinValue)
                    .ToList();

                _rows.Clear();
                AddRow("movies", "Movies", visible.Where(IsMovie));
                AddRow("series", "Series", visible.Where(IsSeries));
                AddRow("other", "Other", visible.Where(i => !IsMovie(i) && !IsSeries(i)));

                if (_rows.Count == 0)
                    ShowMessage("Your library is empty.");
            }
            catch (Exception ex)
            {
                ShowMessage("Couldn't load your library: " + ex.Message);
            }
            finally
            {
                Loading.IsActive = false;
                Loading.Visibility = Visibility.Collapsed;
                RestoreCurrentSelection();
            }
        }

        private void AddRow(string key, string title, IEnumerable<LibraryItem> items)
        {
            var row = new LibraryRow { Key = key, Title = title };
            foreach (var item in items)
                row.Items.Add(item);
            if (row.Items.Count > 0)
                _rows.Add(row);
        }

        private static bool IsMovie(LibraryItem item) =>
            string.Equals(item.Type, "movie", StringComparison.OrdinalIgnoreCase);

        private static bool IsSeries(LibraryItem item) =>
            string.Equals(item.Type, "series", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Type, "tv", StringComparison.OrdinalIgnoreCase);

        private void GamepadTimer_Tick(object sender, object e)
        {
            var gamepad = Gamepad.Gamepads.FirstOrDefault();
            if (gamepad == null)
                return;

            var reading = gamepad.GetCurrentReading();
            var left = (reading.Buttons & GamepadButtons.DPadLeft) != 0 ||
                       reading.LeftThumbstickX < -0.65;
            var right = (reading.Buttons & GamepadButtons.DPadRight) != 0 ||
                        reading.LeftThumbstickX > 0.65;
            var up = (reading.Buttons & GamepadButtons.DPadUp) != 0 ||
                     reading.LeftThumbstickY > 0.65;
            var down = (reading.Buttons & GamepadButtons.DPadDown) != 0 ||
                       reading.LeftThumbstickY < -0.65;

            if ((left || right) && !_horizontalHeld)
                MovePosterFocus(right ? 1 : -1);
            if ((up || down) && !_verticalHeld)
                MoveRow(down ? 1 : -1);

            _horizontalHeld = left || right;
            _verticalHeld = up || down;
        }

        private void LibraryPage_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.RunAsync(CoreDispatcherPriority.Low, RestoreCurrentSelection);
        }

        private void LibraryList_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.RunAsync(
                CoreDispatcherPriority.Low,
                RestoreCurrentSelection);
        }

        private void LibraryPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Let XAML XY focus navigation handle controller movement.
        }

        private void LibraryList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Let XAML XY focus navigation handle controller movement.
        }

        private void MovePosterFocus(int amount)
        {
            var focused = FocusManager.GetFocusedElement() as DependencyObject;
            var item = FindParent<ListViewItem>(focused);
            var list = FindParent<ListView>(focused);
            if (item == null || list == null)
                return;

            var current = list.IndexFromContainer(item);
            var next = Math.Max(0, Math.Min(list.Items.Count - 1, current + amount));
            if (next == current)
                return;

            SaveSelection(list, next);
            FocusListItem(list, next);
        }

        private void MoveRow(int amount)
        {
            var focused = FocusManager.GetFocusedElement() as DependencyObject;
            var currentList = FindParent<ListView>(focused);
            if (currentList == null)
                return;

            var lists = FindDescendants<ListView>(this).ToList();
            var currentRow = lists.IndexOf(currentList);
            var nextRow = Math.Max(0, Math.Min(lists.Count - 1, currentRow + amount));
            if (nextRow == currentRow)
                return;

            var target = lists[nextRow];
            var currentItem = FindParent<ListViewItem>(focused);
            var sourceIndex = currentItem == null ? 0 : currentList.IndexFromContainer(currentItem);
            var targetIndex = Math.Max(0, Math.Min(target.Items.Count - 1, sourceIndex));
            FocusListItem(target, targetIndex);
        }

        private void RestoreCurrentSelection()
        {
            foreach (var list in FindDescendants<ListView>(this))
            {
                if (list.Items.Count == 0)
                    continue;

                var row = FindParent<StackPanel>(list)?.DataContext as LibraryRow;
                var index = row != null && _selectedIndexes.TryGetValue(row.Key, out var saved)
                    ? saved
                    : 0;
                FocusListItem(list, Math.Max(0, Math.Min(list.Items.Count - 1, index)));
                return;
            }
        }

        private void FocusListItem(ListView list, int index)
        {
            list.UpdateLayout();
            var item = list.ContainerFromIndex(index) as Control;
            if (item == null)
            {
                list.ScrollIntoView(list.Items[index]);
                list.UpdateLayout();
                item = list.ContainerFromIndex(index) as Control;
            }
            item?.Focus(FocusState.Programmatic);
        }

        private void SaveSelection(ListView list, int index)
        {
            var row = FindParent<StackPanel>(list)?.DataContext as LibraryRow;
            if (row != null)
                _selectedIndexes[row.Key] = index;
        }

        private void LibraryList_ContainerContentChanging(
            ListViewBase sender,
            ContainerContentChangingEventArgs args)
        {
            var item = args.ItemContainer as ListViewItem;
            if (item == null)
                return;

            item.GotFocus -= PosterItem_GotFocus;
            item.LostFocus -= PosterItem_LostFocus;
            item.GotFocus += PosterItem_GotFocus;
            item.LostFocus += PosterItem_LostFocus;

            if (args.InRecycleQueue)
                SetPosterHighlight(item, false);
        }

        private void PosterItem_GotFocus(object sender, RoutedEventArgs e)
        {
            var item = sender as ListViewItem;
            var list = FindParent<ListView>(item);
            if (item == null || list == null)
                return;

            SaveSelection(list, list.IndexFromContainer(item));
            foreach (var other in FindDescendants<ListViewItem>(this))
                SetPosterHighlight(other, ReferenceEquals(other, item));
        }

        private void PosterItem_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ListViewItem item)
                SetPosterHighlight(item, false);
        }

        private static void SetPosterHighlight(ListViewItem item, bool selected)
        {
            var ring = FindDescendants<Border>(item)
                .FirstOrDefault(x => x.Name == "PosterFocusRing");
            if (ring == null)
                return;

            ring.BorderBrush = selected ? SelectedPosterBrush : TransparentBrush;
            ring.BorderThickness = selected ? new Thickness(3) : new Thickness(0);
        }

        private void ShowMessage(string text)
        {
            Loading.IsActive = false;
            Message.Text = text;
            Message.Visibility = Visibility.Visible;
        }

        private void Items_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as LibraryItem;
            if (item == null)
                return;

            Frame.Navigate(typeof(DetailPage), new MetaArgs
            {
                Type = item.Type,
                Id = item.Id
            });
        }

        private static T FindParent<T>(DependencyObject child)
            where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null)
                yield break;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;

                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }
    }
}
