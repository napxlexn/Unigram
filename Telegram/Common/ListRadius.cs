//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using Telegram.Controls;
using Telegram.Navigation;
using Telegram.Services;
using Telegram.Services.Settings;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Telegram.Common
{
    /// <summary>
    /// The rows of the lists that stay on screen - the chats, the folders, the tables - take
    /// their radius from <c>ListItemCornerRadius</c> as their style is applied, and keep it;
    /// the settings' cards take theirs as they are prepared. Their hover is a fill of that
    /// shape, so after a change it was the old corner that lit up beside everything already
    /// rounded anew. The rows already made are given the new radius here; the theme's resource
    /// and the settings are what new ones are made with.
    /// </summary>
    public static class ListRadius
    {
        public static void Apply()
        {
            var radius = new CornerRadius(AppSettings.Appearance.GetRadius(RadiusTree.ListItems));

            WindowContext.ForEach(window =>
            {
                if (window.Content is DependencyObject root)
                {
                    Walk(root, radius);
                }
            });
        }

        private static void Walk(DependencyObject node, CornerRadius radius)
        {
            if (node is ChatListListViewItem or TableListViewItem or Telegram.Controls.NavigationViewItem
                || (node is ListViewItem item && ItemsControl.ItemsControlFromItemContainer(item) is TableListView or ChatFoldersNavView))
            {
                ((Control)node).CornerRadius = radius;
            }
            else if (node is HeaderedControl headered && headered.ItemsPanelRoot is Panel rows)
            {
                // The settings' cards, rounded as they are prepared - and with them their hover.
                foreach (var row in rows.Children)
                {
                    HeaderedControl.ApplyCardLayout(row);
                }
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                Walk(VisualTreeHelper.GetChild(node, i), radius);
            }
        }
    }
}
