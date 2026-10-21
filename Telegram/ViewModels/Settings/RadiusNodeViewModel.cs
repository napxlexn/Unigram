//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System.Collections.Generic;
using Telegram.Navigation;
using Telegram.Services;

namespace Telegram.ViewModels.Settings
{
    /// <summary>
    /// One slider of the corner radius settings: the master, a category or an element in one.
    /// </summary>
    public partial class RadiusNodeViewModel : BindableBase
    {
        private readonly SettingsAppearanceViewModel _owner;

        public RadiusNodeViewModel(SettingsAppearanceViewModel owner, string id, string label, RadiusNodeViewModel parent)
        {
            _owner = owner;
            Id = id;
            Label = label;
            Parent = parent;
        }

        public string Id { get; }

        public string Label { get; }

        public RadiusNodeViewModel Parent { get; }

        public List<RadiusNodeViewModel> Children { get; } = new();

        public bool HasChildren => Children.Count > 0;

        public double Value
        {
            get => AppSettings.Appearance.GetRadius(Id);
            set
            {
                if ((int)value != AppSettings.Appearance.GetRadius(Id))
                {
                    _owner.SetRadius(this, (int)value);
                }
            }
        }

        /// <summary>
        /// Set apart from what it would follow.
        /// </summary>
        public bool IsOwn => Parent != null && AppSettings.Appearance.GetOwnRadius(Id) >= 0;

        public string Text => string.Format(IsOwn ? "{0}: {1} px · окремо" : "{0}: {1} px", Label, (int)Value);

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (Set(ref _isExpanded, value))
                {
                    RaisePropertyChanged(nameof(ExpandGlyph));
                }
            }
        }

        public string ExpandGlyph => _isExpanded ? "" : "";

        // The rows are settings cards, as round as those are.
        public Windows.UI.Xaml.CornerRadius Corner => new(AppSettings.Appearance.GetRadius(Services.Settings.RadiusTree.SettingsItems));

        public void Refresh()
        {
            RaisePropertyChanged(nameof(Value));
            RaisePropertyChanged(nameof(Text));
            RaisePropertyChanged(nameof(IsOwn));
            RaisePropertyChanged(nameof(Corner));

            foreach (var child in Children)
            {
                child.Refresh();
            }
        }
    }
}
