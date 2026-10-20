//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Common;
using Telegram.Controls;
using Telegram.Controls.Cells;
using Telegram.Controls.Chats;
using Telegram.Controls.Media;
using Telegram.Controls.Messages;
using Telegram.Services;
using Telegram.Services.Settings;
using Telegram.Td.Api;
using Telegram.ViewModels;
using Telegram.ViewModels.Drawers;
using Telegram.ViewModels.Settings;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace Telegram.Views.Settings
{
    public sealed partial class SettingsAppearancePage : HostedPage
    {
        public SettingsAppearanceViewModel ViewModel => DataContext as SettingsAppearanceViewModel;

        public SettingsAppearancePage()
        {
            InitializeComponent();
            Title = Strings.Appearance;

            Preview.CreateInsetClip();
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            ViewModel.UpdateActualTheme(true);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (ViewModel.Window.UpdateChatTheme(ActualTheme, null, null, null, null))
            {
                ViewModel.Aggregator.Publish(new UpdateDefaultBackground(false, ViewModel.ClientService.GetDefaultBackground(false)));
                ViewModel.Aggregator.Publish(new UpdateDefaultBackground(true, ViewModel.ClientService.GetDefaultBackground(true)));
            }

            BackgroundControl.Update(ViewModel.ClientService, ViewModel.Aggregator);

            _background ??= new MessageBubbleBackgroundCoordinator(Bubbles, ViewModel.Window);
            _background.Attach(Message2);

            ViewModel.PropertyChanged += OnPropertyChanged;
            ViewModel.Aggregator.Subscribe<UpdateDefaultReactionType>(this, Handle);

            UpdateDefaultReactionType();

            if (ViewModel.ClientService.TryGetUser(ViewModel.ClientService.Options.MyId, out User user))
            {
                if (ViewModel.IsPremiumAvailable)
                {
                    ProfileColor.SetUser(ViewModel.ClientService, user);
                }
                else
                {
                    NameColor.Visibility = Visibility.Collapsed;
                }

                Message1.Mockup(ViewModel.ClientService, Strings.FontSizePreviewLine1, user, Strings.FontSizePreviewReply, false, DateTime.Now.AddSeconds(-25));
                Message2.Mockup(Strings.FontSizePreviewLine2, true, DateTime.Now);
            }

            InitializePlayground();
        }

        #region Custom colours

        private void CustomColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement target || target.Tag is not string name)
            {
                return;
            }

            var picker = new Microsoft.UI.Xaml.Controls.ColorPicker
            {
                Color = ViewModel.GetCustomColor(name),
                IsAlphaEnabled = false,
                IsMoreButtonVisible = false,
                IsColorChannelTextInputVisible = false,
                IsHexInputVisible = true
            };

            // Live, as the picker is dragged: the playground above and any open chat follow.
            picker.ColorChanged += (s, args) => ViewModel.SetCustomColor(name, args.NewColor);

            var flyout = new Flyout { Content = picker };
            flyout.ShowAt(target);
        }

        #endregion

        #region Playground

        private MessageBubbleBackgroundCoordinator _playgroundBackground;

        private void InitializePlayground()
        {
            PlaygroundBackground.Update(ViewModel.ClientService, ViewModel.Aggregator);

            _playgroundBackground ??= new MessageBubbleBackgroundCoordinator(PlaygroundBubbles, ViewModel.Window);
            _playgroundBackground.Attach(PlaygroundOutgoing);
            _playgroundBackground.Attach(PlaygroundOutgoing2);

            PlaygroundIncoming.Mockup("Як тобі нове скло? Бульбашки тепер просвічують шпалери ✨", false, DateTime.Now.AddMinutes(-3));
            PlaygroundOutgoing.Mockup("Виглядає чудово! Ще трохи сяйва — і буде ідеально 😍", true, DateTime.Now.AddMinutes(-2));
            PlaygroundIncoming2.Mockup("Прокрути вгору: повідомлення плавно заходять під шапку 👆", false, DateTime.Now.AddMinutes(-1));
            PlaygroundOutgoing2.Mockup("А дати й реакції теж можна зробити скляними 🫧", true, DateTime.Now.AddSeconds(-40));
            PlaygroundIncoming3.Mockup("Спробуй «Матове скло» і «Скло з сяйвом» нижче 👇", false, DateTime.Now);

            UpdatePlaygroundHeader();

            // Opened scrolled to the end, as a chat is, so the first messages already sit under
            // the header and show what it does to them.
            PlaygroundScroll.Loaded += (s, args) => PlaygroundScroll.ChangeView(null, PlaygroundScroll.ScrollableHeight, null, true);
        }

        private void UpdatePlaygroundHeader()
        {
            var style = AppSettings.Appearance.HeaderStyle;
            var original = style == HeaderStyle.Original;

            PlaygroundHeaderOriginal.Visibility = original ? Visibility.Visible : Visibility.Collapsed;
            PlaygroundHeaderGlass.Visibility = original ? Visibility.Collapsed : Visibility.Visible;

            if (!original)
            {
                MasterDetailView.ApplyHeaderStyle(PlaygroundHeaderGlass, style, ActualTheme, null, default, 48);
            }

            // The same sizes the real header shows its title at once a page is scrolled; inside
            // the capsule it sits a little further in.
            PlaygroundTitle.FontSize = original ? 14 : 18;
            PlaygroundTitle.Margin = style == HeaderStyle.Capsule ? new Thickness(28, 16, 16, 0) : new Thickness(16, 12, 16, 0);
        }

        /// <summary>
        /// The colours change with the theme pass the setting triggers, which runs after this
        /// returns; the fills that are not a colour are re-read once it has.
        /// </summary>
        private async void RefreshPlayground()
        {
            await Task.Delay(250);

            foreach (var bubble in new[] { Message1, Message2, PlaygroundIncoming, PlaygroundOutgoing, PlaygroundIncoming2, PlaygroundOutgoing2, PlaygroundIncoming3 })
            {
                bubble.RefreshStyle();
            }

            UpdatePlaygroundHeader();
        }

        #endregion

        private MessageBubbleBackgroundCoordinator _background;

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            ViewModel.PropertyChanged -= OnPropertyChanged;
            ViewModel.Aggregator.Unsubscribe(this);
        }

        #region Binding

        private string ConvertNightMode(NightMode mode)
        {
            return mode == NightMode.Scheduled
                ? Strings.AutoNightScheduled
                : mode == NightMode.Automatic
                ? Strings.AutoNightAutomatic
                : mode == NightMode.System
                ? Strings.AutoNightSystemDefault
                : Strings.AutoNightDisabled;
        }

        private string ConvertQuickAction(bool reply)
        {
            return reply
                ? Strings.QuickReactionInfo2
                : Strings.QuickReactionInfo;
        }

        private string ConvertPercent(string header, double value)
        {
            return string.Format("{0}: {1:0}%", header, value);
        }

        private double ConvertEnabledOpacity(bool enabled)
        {
            return enabled ? 1 : 0.5;
        }

        #endregion

        private void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.FontSize) || e.PropertyName == nameof(ViewModel.BubbleRadius))
            {
                Message1.UpdateMockup(false, true, true);
                Message2.UpdateMockup(true, true, true);
                PlaygroundIncoming.UpdateMockup(false, true, true);
                PlaygroundOutgoing.UpdateMockup(true, true, true);
                PlaygroundIncoming2.UpdateMockup(false, true, true);
                PlaygroundOutgoing2.UpdateMockup(true, true, true);
                PlaygroundIncoming3.UpdateMockup(false, true, true);
            }
            else if (e.PropertyName is nameof(ViewModel.BubbleStyleIndex)
                or nameof(ViewModel.BubbleOpacity)
                or nameof(ViewModel.ReactionStyleIndex)
                or nameof(ViewModel.ServiceStyleIndex)
                or nameof(ViewModel.MessageTextStyleIndex)
                or nameof(ViewModel.HeaderStyleIndex)
                or nameof(ViewModel.UseCustomMessageColors)
                or nameof(ViewModel.BackgroundOpacity)
                or nameof(ViewModel.WindowMaterialIndex))
            {
                RefreshPlayground();
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            VisualUtilities.QueueCallbackForCompositionRendered(OnSelectionChanged);

            if (List.SelectedItem is ChatThemeViewModel chatTheme && ViewModel.SelectionChanged)
            {
                // Speed up background preview by manually applying it
                BackgroundControl.Update(chatTheme.LightSettings?.Background, chatTheme.DarkSettings?.Background);
            }
        }

        private void OnSelectionChanged()
        {
            if (List.SelectedItem != null)
            {
                List.ScrollIntoView(List.SelectedItem);
            }
        }

        #region Context menu

        private void Theme_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            var theme = List.ItemFromContainer(sender) as ChatThemeViewModel;

            var flyout = new MenuFlyout();
            flyout.CreateFlyoutItem(ViewModel.CreateTheme, theme, Strings.CreateNewThemeMenu, Icons.Color);

            //if (!theme.IsOfficial)
            //{
            //    flyout.CreateFlyoutSeparator();
            //    flyout.CreateFlyoutItem(ViewModel.ThemeShareCommand, theme, Strings.ShareFile, Icons.Share);
            //    flyout.CreateFlyoutItem(ViewModel.ThemeEditCommand, theme, Strings.Edit, Icons.Edit);
            //    flyout.CreateFlyoutItem(ViewModel.ThemeDeleteCommand, theme, Strings.Delete, Icons.Delete);
            //}

            flyout.ShowAt(sender, args);
        }

        #endregion

        #region Recycle

        private void OnChoosingItemContainer(ListViewBase sender, ChoosingItemContainerEventArgs args)
        {
            if (args.ItemContainer == null)
            {
                args.ItemContainer = new MultipleListViewItem(sender, false);
                args.ItemContainer.Style = sender.ItemContainerStyle;
                args.ItemContainer.ContentTemplate = sender.ItemTemplate;
                args.ItemContainer.ContextRequested += Theme_ContextRequested;
            }

            args.IsContainerPrepared = true;
        }

        private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer.ContentTemplateRoot is not ChatThemeCell content)
            {
                return;
            }

            if (args.InRecycleQueue)
            {
                return;
            }
            else if (args.Item is ChatThemeViewModel theme)
            {
                content.Update(theme);
                args.Handled = true;
            }
        }

        #endregion

        private bool _compact;

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var compact = e.NewSize.Width < 500;
            if (compact == _compact)
            {
                return;
            }

            _compact = compact;

            Grid.SetColumnSpan(TextSizeHeader, compact ? 2 : 1);
            Grid.SetColumnSpan(BubbleRadiusHeader, compact ? 2 : 1);

            Grid.SetRow(FontSizeSlider, compact ? 1 : 0);
            Grid.SetColumn(FontSizeSlider, compact ? 0 : 2);

            Grid.SetRow(BubbleRadiusSlider, compact ? 1 : 0);
            Grid.SetColumn(BubbleRadiusSlider, compact ? 0 : 2);
        }

        private void Handle(UpdateDefaultReactionType update)
        {
            this.BeginOnUIThread(UpdateDefaultReactionType);
        }

        private void UpdateDefaultReactionType()
        {
            var reaction = ViewModel.ClientService.DefaultReaction;

            var clientService = ViewModel.ClientService;
            var senderId = new MessageSenderUser(clientService.Options.MyId);

            var message = new Message(0, senderId, null, 0, null, null, false, false, false, false, false, false, false, false, false, false, 0, 0, null, null, null, Array.Empty<UnreadReaction>(), null, null, null, null, null, 0, 0, 0, null, 0, 0, string.Empty, 0, string.Empty, 0, 0, null, string.Empty, null, null, null);

            var settings = clientService.Session.Resolve<ISettingsService>();

            var delegato = new ChatMessageDelegate(clientService, settings, null);
            var viewModel = new MessageViewModel(clientService, delegato, null, null, null, message, true);

            Reaction.SetReaction(viewModel, new MessageReaction(reaction, 1, false, senderId, new MessageSender[] { }));

            if (Reaction.IsLoaded)
            {
                Reaction.SetUnread(new UnreadReaction(reaction, senderId, false));
            }
        }

        private void Reaction_Click(object sender, RoutedEventArgs e)
        {
            var empty = Array.Empty<AvailableReaction>();
            var reactions = ViewModel.ClientService.ActiveReactions
                .Select(x => new AvailableReaction(new ReactionTypeEmoji(x), false))
                .ToVector();

            var viewModel = EmojiDrawerViewModel.Create(ViewModel.ClientService.Session, EmojiDrawerMode.Reactions);
            _ = viewModel.UpdateReactions(new AvailableReactions(reactions, empty, empty, true, false, null));

            var flyout = EmojiMenuFlyout.ShowAt(ViewModel.ClientService, EmojiDrawerMode.Reactions, Reaction, EmojiFlyoutAlignment.TopRight, viewModel);

            flyout.EmojiSelected += (s, args) =>
            {
                ViewModel.DoubleClickToReact = true;
            };
        }
    }
}
