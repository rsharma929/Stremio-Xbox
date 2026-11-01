using Stremio.Core.Addons;
using StremioXbox.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace StremioXbox.Views
{
    public sealed partial class DetailPage : Page
    {
        private MetaArgs _args;
        private MetaDetail _meta;
        private Dictionary<int, List<Video>> _seasons = new Dictionary<int, List<Video>>();

        public DetailPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _args = (MetaArgs)e.Parameter;
            try
            {
                await AppServices.EnsureAddonsLoadedAsync();
                _meta = await AppServices.Addons.GetMetaAsync(_args.Type, _args.Id);
            }
            catch (Exception)
            {
                _meta = null;
            }

            Loading.IsActive = false;
            Loading.Visibility = Visibility.Collapsed;

            if (_meta == null)
            {
                Title.Text = "Couldn't load details";
                return;
            }

            Title.Text = _meta.Name ?? "";
            Info.Text = string.Join("   ·   ", new[] { _meta.ReleaseInfo, _meta.Runtime,
                _meta.ImdbRating != null ? "IMDb " + _meta.ImdbRating : null, string.Join(", ", _meta.Genres ?? new List<string>()) }
                .Where(s => !string.IsNullOrEmpty(s)));
            Description.Text = _meta.Description ?? "";
            SetImage(Poster, _meta.Poster);
            SetImage(Backdrop, _meta.Background);

            var videos = _meta.Videos ?? new List<Video>();
            if (_args.Type == "movie" || videos.Count == 0)
            {
                PlayButton.Visibility = Visibility.Visible;
                PlayButton.Focus(FocusState.Programmatic);
                return;
            }

            // Season 0 is specials; list it last.
            _seasons = videos.GroupBy(v => v.Season ?? 0).ToDictionary(g => g.Key, g => g.OrderBy(v => v.Episode ?? 0).ToList());
            SeasonPicker.ItemsSource = _seasons.Keys.OrderBy(s => s == 0 ? int.MaxValue : s)
                .Select(s => s == 0 ? "Specials" : "Season " + s).ToList();
            EpisodesPanel.Visibility = Visibility.Visible;
            SeasonPicker.SelectedIndex = 0;
            SeasonPicker.Focus(FocusState.Programmatic);
        }

        private void SeasonPicker_DropDownClosed(object sender, object e)
        {
            FocusFirstEpisode();
        }

        private void SeasonPicker_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if ((e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape) &&
                SeasonPicker.IsDropDownOpen)
            {
                SeasonPicker.IsDropDownOpen = false;
                FocusFirstEpisode();
                e.Handled = true;
            }
        }

        private void FocusFirstEpisode()
        {
            if (Episodes.Items.Count == 0)
                return;

            Episodes.UpdateLayout();
            var item = Episodes.ContainerFromIndex(0) as Control;

            if (item == null)
            {
                Episodes.ScrollIntoView(Episodes.Items[0]);
                Episodes.UpdateLayout();
                item = Episodes.ContainerFromIndex(0) as Control;
            }

            item?.Focus(FocusState.Programmatic);
        }

        private static void SetImage(Image image, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                image.Source = new BitmapImage(uri);
        }

        private void SeasonPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var label = SeasonPicker.SelectedItem as string;
            if (label == null)
                return;
            var season = label == "Specials" ? 0 : int.Parse(label.Substring("Season ".Length));
            Episodes.ItemsSource = _seasons[season];
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(
                typeof(StreamsPage),
                new StreamsArgs
                {
                    Type = _args.Type,
                    VideoId = _meta.Id,
                    Title = _meta.Name,
                    Background = _meta.Background
                });
        }

        private void Episodes_ItemClick(object sender, ItemClickEventArgs e)
        {
            var video = (Video)e.ClickedItem;

            var title =
                _meta.Name +
                " S" +
                (video.Season ?? 0).ToString("00") +
                "E" +
                (video.Episode ?? 0).ToString("00");

            Frame.Navigate(
                typeof(StreamsPage),
                new StreamsArgs
                {
                    Type = _args.Type,
                    VideoId = video.Id,
                    Title = title,
                    Background = _meta.Background
                });
        }
    }
}
