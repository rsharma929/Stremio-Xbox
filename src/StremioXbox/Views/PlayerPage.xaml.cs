using System;
using System.Collections.Generic;
using System.Linq;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using StremioXbox.Services;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using Windows.Gaming.Input;


namespace StremioXbox.Views
{
    public sealed partial class PlayerPage : Page
    {
        private DispatcherTimer _gamepadTimer;
        private GamepadButtons _previousButtons;
        private double _previousLeftTrigger;
        private double _previousRightTrigger;
        private static readonly TimeSpan OverlayTimeout = TimeSpan.FromSeconds(4);
        private double _previousThumbstickX;

        private readonly DispatcherTimer _overlayTimer = new DispatcherTimer { Interval = OverlayTimeout };
        private readonly DisplayRequest _displayRequest = new DisplayRequest();
        private PlayerArgs _args;
        private LibVLC _libVlc;
        private MediaPlayer _player;
        private bool _displayMatched;

        private readonly DispatcherTimer _clockTimer =
        new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        public PlayerPage()
        {
            InitializeComponent();
            _overlayTimer.Tick += (s, e) => HideOverlay();
            _gamepadTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };

            _gamepadTimer.Tick += GamepadTimer_Tick;
            _clockTimer.Tick += (s, e) => UpdateClocks();
        }

        private static bool StickPressed(double current, double previous, double threshold = 0.5)
        {
            return Math.Abs(current) >= threshold &&
                   Math.Abs(previous) < threshold;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _args = (PlayerArgs)e.Parameter;
            TitleText.Text = _args.Title ?? "";
            _displayRequest.RequestActive();
            ShowOverlay();
            _gamepadTimer.Start();
            _clockTimer.Start();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _overlayTimer.Stop();
            try { _displayRequest.RequestRelease(); } catch (Exception) { }

            var player = _player;
            var libVlc = _libVlc;
            _player = null;
            _libVlc = null;
            if (player != null)
            {
                Video.MediaPlayer = null;
                player.Stop();
                player.Dispose();
            }
            libVlc?.Dispose();
            _gamepadTimer.Stop();
            _clockTimer.Stop();
            DisplayModeService.RestoreDefault();
        }

        // VideoView creates its swap chain first; LibVLC must be created with those options.
        private void Video_Initialized(object sender, InitializedEventArgs e)
        {
            var options = new List<string>(e.SwapChainOptions)
            {
                "--avcodec-hw=d3d11va",     // hardware decode on the console GPU (H.264/HEVC/VP9/AV1)
                "--network-caching=3000",
                "--sub-track-id=-1",
            };
            if (AppServices.Settings.AudioPassthrough)
                options.Add("--spdif"); // bitstream AC3/E-AC3/DTS to the TV or receiver

            _libVlc = new LibVLC(false, options.ToArray());
            _player = new MediaPlayer(_libVlc) { EnableHardwareDecoding = true };
            _player.Playing += (s, a) => OnUi(OnPlaying);
            _player.Buffering += (s, a) => OnUi(() => Buffering.IsActive = a.Cache < 100);
            _player.TimeChanged += (s, a) => OnUi(() => UpdatePosition(a.Time));
            _player.EncounteredError += (s, a) => OnUi(() => ShowError("Playback failed. The stream may be offline or unsupported."));
            _player.EndReached += (s, a) => OnUi(() => { if (Frame.CanGoBack) Frame.GoBack(); });
            _player.Vout += (s, a) => OnUi(MatchDisplay);
            Video.MediaPlayer = _player;

            var media = new Media(_libVlc, new Uri(_args.Url));
            if (_args.Headers != null && _args.Headers.TryGetValue("User-Agent", out var userAgent))
                media.AddOption(":http-user-agent=" + userAgent);
            _player.Play(media);
            media.Dispose();

            // Attach addon subtitles; the preferred language is selected, others stay in the menu.
            var preferred = AppServices.Settings.PreferredSubtitleLanguage;
            var subtitles = (_args.Subtitles ?? new List<Stremio.Core.Addons.Subtitle>())
                .Where(s => !string.IsNullOrEmpty(s.Url)).Take(20).ToList();
            var selected = subtitles.FirstOrDefault(s => s.Lang == preferred);
            foreach (var sub in subtitles)
                _player.AddSlave(MediaSlaveType.Subtitle, sub.Url, sub == selected);
            _player.Paused += (s, a) => OnUi(() =>
            {
                PlayPauseIcon.Symbol = Symbol.Play;
                Window.Current.CoreWindow.PointerCursor =
                    new CoreCursor(CoreCursorType.Arrow, 0);
                ShowOverlay();
            });
        }

        private void OnPlaying()
        {
            Window.Current.CoreWindow.PointerCursor = null;

            Buffering.IsActive = false;
            ErrorText.Visibility = Visibility.Collapsed;
            PlayPauseIcon.Symbol = Symbol.Pause;
            DurationText.Text = Format(_player?.Length ?? 0);

            ShowOverlay();
        }

        private async void MatchDisplay()
        {
            if (_displayMatched || _player == null || !AppServices.Settings.MatchDisplayMode)
                return;
            uint width = 0, height = 0;
            if (!_player.Size(0, ref width, ref height) || width == 0)
                return;
            _displayMatched = true;
            await DisplayModeService.MatchAsync(width, height, _player.Fps, _args.LooksHdr);
        }

        private void UpdatePosition(long time)
        {
            var length = _player?.Length ?? 0;
            PositionText.Text = Format(time);
            if (length > 0)
            {
                DurationText.Text = Format(length);
                Progress.Value = (double)time / length;
            }
        }

        private void ShowError(string message)
        {
            Buffering.IsActive = false;
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
            ShowOverlay();
        }

        private void ShowOverlay(bool focusPlayPause = false)
        {
            var wasHidden = Overlay.Visibility != Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            if (focusPlayPause ||
                wasHidden ||
                FocusManager.GetFocusedElement() == null)
            {
                PlayPauseButton.Focus(FocusState.Keyboard);
            }

            _overlayTimer.Stop();
            _overlayTimer.Start();
        }

        private void HideOverlay()
        {
            _overlayTimer.Stop();
            if (_player != null && _player.IsPlaying && !AudioMenu.IsOpen && !SubtitleMenu.IsOpen)
                Overlay.Visibility = Visibility.Collapsed;
        }

        private void GamepadTimer_Tick(object sender, object e)
        {
            if (Gamepad.Gamepads.Count == 0)
                return;

            var reading = Gamepad.Gamepads[0].GetCurrentReading();
            var buttons = reading.Buttons;

            // A = play/pause
            if (Pressed(buttons, GamepadButtons.A))
                if (Overlay.Visibility != Visibility.Visible)
                {
                    TogglePause();
                }


            // Left stick or D-pad left/right = seek
            if (Overlay.Visibility != Visibility.Visible)
            {
                if (Pressed(buttons, GamepadButtons.DPadLeft) ||
                    (StickPressed(reading.LeftThumbstickX, _previousThumbstickX) &&
                     reading.LeftThumbstickX < 0))
                {
                    Seek(-10000);
                }

                if (Pressed(buttons, GamepadButtons.DPadRight) ||
                    (StickPressed(reading.LeftThumbstickX, _previousThumbstickX) &&
                     reading.LeftThumbstickX > 0))
                {
                    Seek(30000);
                }
            }

            // Bumpers = seek
            if (Pressed(buttons, GamepadButtons.LeftShoulder))
                Seek(-10000);

            if (Pressed(buttons, GamepadButtons.RightShoulder))
                Seek(30000);

            // Triggers = large seek
            if (reading.LeftTrigger > 0.5 &&
                _previousLeftTrigger <= 0.5)
            {
                Seek(-300000);
            }

            if (reading.RightTrigger > 0.5 &&
                _previousRightTrigger <= 0.5)
            {
                Seek(300000);
            }

            _previousButtons = buttons;
            _previousLeftTrigger = reading.LeftTrigger;
            _previousRightTrigger = reading.RightTrigger;
            _previousThumbstickX = reading.LeftThumbstickX;
        }

        private bool Pressed(GamepadButtons current, GamepadButtons button)
        {
            return current.HasFlag(button) &&
                   !_previousButtons.HasFlag(button);
        }

        private void Seek(long deltaMs)
        {
            if (_player == null || !_player.IsSeekable)
                return;
            var target = Math.Max(0, Math.Min(_player.Length - 1000, _player.Time + deltaMs));
            _player.Time = target;
            UpdatePosition(target);
        }

        private void TogglePause()
        {
            if (_player == null)
                return;
            if (_player.IsPlaying)
                _player.SetPause(true);
            else
                _player.Play();
        }

        private void Rewind_Click(object sender, RoutedEventArgs e) { Seek(-10000); }
        private void Forward_Click(object sender, RoutedEventArgs e) { Seek(30000); }
        private void PlayPause_Click(object sender, RoutedEventArgs e) { TogglePause(); }

        private static void FillTrackMenu(MenuFlyout menu, LibVLCSharp.Shared.Structures.TrackDescription[] tracks, int current, Action<int> select)
        {
            menu.Items.Clear();
            if (tracks == null || tracks.Length == 0)
            {
                menu.Items.Add(new MenuFlyoutItem { Text = "No tracks", IsEnabled = false });
                return;
            }
            foreach (var track in tracks)
            {
                var id = track.Id;
                var item = new ToggleMenuFlyoutItem
                {
                    Text = track.Name ?? ("Track " + id),
                    IsChecked = id == current,
                    Tag = id
                };
                item.Click += (s, a) => select(id);
                menu.Items.Add(item);
            }

        }

        private void OnUi(Action action)
        {
            var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (_player != null)
                    action();
            });
        }

        private static string Format(long ms)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }

        private void AudioMenu_Opening(object sender, object e)
        {
            FillTrackMenu(
                AudioMenu,
                _player?.AudioTrackDescription,
                _player?.AudioTrack ?? -1,
                id => _player?.SetAudioTrack(id));
        }

        private void SubtitleMenu_Opening(object sender, object e)
        {
            FillTrackMenu(
                SubtitleMenu,
                _player?.SpuDescription,
                _player?.Spu ?? -1,
                id => _player?.SetSpu(id));
        }

        private void UpdateClocks()
        {
            CurrentClockText.Text =
                DateTime.Now.ToString("h:mm tt");

            var remaining = Math.Max(
                0,
                (_player?.Length ?? 0) -
                (_player?.Time ?? 0));

            var finishTime = DateTime.Now.AddMilliseconds(remaining);

            RemainingClockText.Text =
                "Ends: " + finishTime.ToString("h:mm tt");
        }
    }
}
