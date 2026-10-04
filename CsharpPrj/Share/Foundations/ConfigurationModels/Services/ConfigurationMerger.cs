#nullable enable
using TConfig;

namespace Modules.ConfigurationModels.Services

{
    public static class ConfigurationMerger
    {

        private static T? Coalesce<T>(T? overrideValue, T? baseValue) where T : class
            => overrideValue ?? baseValue;

        private static T CoalesceValue<T>(T overrideValue, T baseValue, Func<T, bool> isSet) where T : struct
            => isSet(overrideValue) ? overrideValue : baseValue;


        private static List<T> MergeList<T>(List<T> overrideList, List<T> baseList)
            => overrideList.Count > 0 ? overrideList : baseList;

        private static List<T> MergeListByKey<T>(List<T> overrideList, List<T> baseList, Func<T, string> keySelector, IEqualityComparer<string>? comparer = null)
        {
            if (overrideList.Count == 0)
                return baseList;

            var result = new List<T>();
            var overrideByKey = overrideList.ToDictionary(keySelector, comparer ?? StringComparer.OrdinalIgnoreCase);

            foreach (var baseItem in baseList)
            {
                var key = keySelector(baseItem);
                if (overrideByKey.TryGetValue(key, out var overrideItem))
                {
                    result.Add(overrideItem);
                    overrideByKey.Remove(key);
                }
                else
                {
                    result.Add(baseItem);
                }
            }

            result.AddRange(overrideByKey.Values);
            return result;
        }


        private static EventConfig? MergeEventConfig(EventConfig? baseEvents, EventConfig? overrideEvents)
        {
            if (overrideEvents == null) return baseEvents;
            if (baseEvents == null) return overrideEvents;

            var merged = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (baseEvents.EventsDictionary != null)
            {
                foreach (var kv in baseEvents.EventsDictionary)
                {
                    merged[kv.Key] = new List<string>(kv.Value ?? new List<string>());
                }
            }

            if (overrideEvents.EventsDictionary != null)
            {
                foreach (var kv in overrideEvents.EventsDictionary)
                {
                    if (kv.Value == null || kv.Value.Count == 0)
                    {
                        merged.Remove(kv.Key);
                    }
                    else
                    {
                        merged[kv.Key] = new List<string>(kv.Value);
                    }
                }
            }

            return new EventConfig
            {
                EventsDictionary = merged
            };
        }

        private static Dictionary<string, List<string>>? MergeCustomEvents(
            Dictionary<string, List<string>>? baseCustom,
            Dictionary<string, List<string>>? overrideCustom)
        {
            if (overrideCustom == null || overrideCustom.Count == 0)
                return baseCustom;
            if (baseCustom == null || baseCustom.Count == 0)
                return overrideCustom;

            var result = new Dictionary<string, List<string>>(baseCustom, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in overrideCustom)
            {
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }


        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NavigationConfig, NavigationConfigSetFlags> _navFlags = new();

        private static bool GetIsShowNavigationBarSet(NavigationConfig nav)
        {
            if (_navFlags.TryGetValue(nav, out var flags))
                return flags.IsShowNavigationBarSet;
            return false;
        }

        internal static void MarkShowNavigationBarSet(NavigationConfig nav)
        {
            var flags = _navFlags.GetOrCreateValue(nav);
            flags.IsShowNavigationBarSet = true;
        }

        private class NavigationConfigSetFlags
        {
            public bool IsShowNavigationBarSet { get; set; }
        }


        public static UIConfiguration Merge(UIConfiguration baseConfig, UIConfiguration overrideConfig)
        {
            baseConfig = PortGuard.Required(baseConfig, nameof(baseConfig));
            overrideConfig = PortGuard.Required(overrideConfig, nameof(overrideConfig));

            var result = new UIConfiguration
            {
                Extends = overrideConfig.Extends ?? baseConfig.Extends,
                Form = MergeForm(baseConfig.Form, overrideConfig.Form),
                Navigation = MergeNavigation(baseConfig.Navigation, overrideConfig.Navigation),
                Views = MergeViews(baseConfig.Views, overrideConfig.Views),
                DataBindings = MergeList(overrideConfig.DataBindings, baseConfig.DataBindings),
                Styles = MergeStyles(baseConfig.Styles, overrideConfig.Styles),
                DataLoad = Coalesce(overrideConfig.DataLoad, baseConfig.DataLoad),
                InitCommands = Coalesce(overrideConfig.InitCommands, baseConfig.InitCommands),
                InitCommandMode = Coalesce(overrideConfig.InitCommandMode, baseConfig.InitCommandMode),
            };

            return result;
        }


        private static FormConfig MergeForm(FormConfig baseForm, FormConfig overrideForm)
        {
            return new FormConfig
            {
                Title = Coalesce(overrideForm.Title, baseForm.Title),
                Width = CoalesceValue(overrideForm.Width, baseForm.Width, _ => overrideForm.IsWidthSet),
                Height = CoalesceValue(overrideForm.Height, baseForm.Height, _ => overrideForm.IsHeightSet),
                MaximizeBox = CoalesceValue(overrideForm.MaximizeBox, baseForm.MaximizeBox, _ => overrideForm.IsMaximizeBoxSet),
                MinimizeBox = CoalesceValue(overrideForm.MinimizeBox, baseForm.MinimizeBox, _ => overrideForm.IsMinimizeBoxSet),
                WindowState = overrideForm.IsWindowStateSet ? overrideForm.WindowState : baseForm.WindowState,
                IconPath = Coalesce(overrideForm.IconPath, baseForm.IconPath),
                StartPosition = Coalesce(overrideForm.StartPosition, baseForm.StartPosition),
                IsWidthSet = overrideForm.IsWidthSet || baseForm.IsWidthSet,
                IsHeightSet = overrideForm.IsHeightSet || baseForm.IsHeightSet,
                IsMaximizeBoxSet = overrideForm.IsMaximizeBoxSet || baseForm.IsMaximizeBoxSet,
                IsMinimizeBoxSet = overrideForm.IsMinimizeBoxSet || baseForm.IsMinimizeBoxSet,
                IsWindowStateSet = overrideForm.IsWindowStateSet || baseForm.IsWindowStateSet,
            };
        }

        private static NavigationConfig MergeNavigation(NavigationConfig baseNav, NavigationConfig overrideNav)
        {
            return new NavigationConfig
            {
                DefaultView = Coalesce(overrideNav.DefaultView, baseNav.DefaultView),
                Items = MergeList(overrideNav.Items, baseNav.Items),
                ShowNavigationBar = GetIsShowNavigationBarSet(overrideNav) ? overrideNav.ShowNavigationBar : baseNav.ShowNavigationBar,
                NavigationBarPosition = overrideNav.NavigationBarPosition ?? baseNav.NavigationBarPosition,
                Buttons = MergeList(overrideNav.Buttons, baseNav.Buttons),
            };
        }

        private static List<ViewConfig> MergeViews(List<ViewConfig> baseViews, List<ViewConfig> overrideViews)
        {
            return MergeListByKey(overrideViews, baseViews, v => v.Name);
        }

        private static ViewConfig MergeView(ViewConfig baseView, ViewConfig overrideView)
        {
            return new ViewConfig
            {
                Name = Coalesce(overrideView.Name, baseView.Name),
                Type = Coalesce(overrideView.Type, baseView.Type),
                Layout = MergeLayout(baseView.Layout, overrideView.Layout),
                Controls = MergeList(overrideView.Controls, baseView.Controls),
                DataSource = Coalesce(overrideView.DataSource, baseView.DataSource),
                Events = MergeEventConfig(baseView.Events, overrideView.Events),
            };
        }

        private static LayoutConfig MergeLayout(LayoutConfig baseLayout, LayoutConfig overrideLayout)
        {
            return new LayoutConfig
            {
                Type = overrideLayout.Type != LayoutType.Flow ? overrideLayout.Type : baseLayout.Type,
                Columns = CoalesceValue(overrideLayout.Columns, baseLayout.Columns, v => v > 0),
                Rows = CoalesceValue(overrideLayout.Rows, baseLayout.Rows, v => v > 0),
                Padding = Coalesce(overrideLayout.Padding, baseLayout.Padding),
                Spacing = Coalesce(overrideLayout.Spacing, baseLayout.Spacing),
            };
        }

        private static StyleConfig MergeStyles(StyleConfig baseStyle, StyleConfig overrideStyle)
        {
            return new StyleConfig
            {
                FontFamily = Coalesce(overrideStyle.FontFamily, baseStyle.FontFamily),
                FontSize = CoalesceValue(overrideStyle.FontSize, baseStyle.FontSize, v => v > 0),
                FontStyle = Coalesce(overrideStyle.FontStyle, baseStyle.FontStyle),
                ForeColor = Coalesce(overrideStyle.ForeColor, baseStyle.ForeColor),
                BackColor = Coalesce(overrideStyle.BackColor, baseStyle.BackColor),
                BorderColor = Coalesce(overrideStyle.BorderColor, baseStyle.BorderColor),
                BorderWidth = CoalesceValue(overrideStyle.BorderWidth, baseStyle.BorderWidth, v => v > 0),
                BorderStyle = Coalesce(overrideStyle.BorderStyle, baseStyle.BorderStyle),
            };
        }
    }
}
