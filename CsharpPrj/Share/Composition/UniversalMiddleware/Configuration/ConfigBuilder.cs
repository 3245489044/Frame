#nullable enable
using System;
using System.Collections.Generic;
using UIConfiguration = TConfig.UIConfiguration;
using FormConfig = TConfig.FormConfig;
using NavigationConfig = TConfig.NavigationConfig;
using NavigationItem = TConfig.NavigationItem;
using ViewConfig = TConfig.ViewConfig;
using LayoutType = TConfig.LayoutType;
using ControlConfig = TConfig.ControlConfig;
using LayoutPosition = TConfig.LayoutPosition;
using DataBindingConfig = TConfig.DataBindingConfig;
using System.Text.Json;
using UniversalMiddleware.Serialization;

namespace UniversalMiddleware.Configuration;





    public class UiConfigBuilder
    {
        private readonly UIConfiguration _config = new();

        public UiConfigBuilder WithForm(string title, int width = 1200, int height = 800)
        {
            _config.Form.Title = title;
            _config.Form.Width = width;
            _config.Form.Height = height;
            return this;
        }

        public UiConfigBuilder WithFormStyle(
            bool maximize = true, bool minimize = true)
        {
            _config.Form.MaximizeBox = maximize;
            _config.Form.MinimizeBox = minimize;
            return this;
        }

        public UiConfigBuilder WithNavigation(string defaultView, string position = "Top")
        {
            _config.Navigation.DefaultView = defaultView;
            _config.Navigation.NavigationBarPosition = position;
            return this;
        }

        public UiConfigBuilder AddNavigationItem(string id, string text, string viewName, string? icon = null)
        {
            _config.Navigation.Items.Add(new NavigationItem
            {
                Id = id,
                Text = text,
                ViewName = viewName,
                Icon = icon ?? string.Empty
            });
            return this;
        }

        public UiConfigBuilder AddView(string name, string type, Action<ViewConfigBuilder>? configure = null)
        {
            var view = new ViewConfig { Name = name, Type = type };
            var builder = new ViewConfigBuilder(view);
            configure?.Invoke(builder);
            _config.Views.Add(view);
            return this;
        }

        public UiConfigBuilder AddDataBinding(string dataSource, string dataMember)
        {
            _config.DataBindings.Add(new DataBindingConfig
            {
                DataSource = dataSource,
                DataMember = dataMember
            });
            return this;
        }

        public UIConfiguration Build() => _config;
    }

    public class ViewConfigBuilder
    {
        private readonly ViewConfig _view;

        internal ViewConfigBuilder(ViewConfig view) => _view = view;

        public ViewConfigBuilder WithLayout(LayoutType type, int columns = 1)
        {
            _view.Layout.Type = type;
            _view.Layout.Columns = columns;
            return this;
        }

        public ViewConfigBuilder AddControl(string type, string name, Action<ControlConfigBuilder>? configure = null)
        {
            var control = new ControlConfig { Type = type, Name = name };
            var builder = new ControlConfigBuilder(control);
            configure?.Invoke(builder);
            _view.Controls.Add(control);
            return this;
        }
    }

    public class ControlConfigBuilder
    {
        private readonly ControlConfig _control;

        public ControlConfigBuilder(ControlConfig control) => _control = control;

        public ControlConfigBuilder At(int x, int y)
        {
            _control.X = x;
            _control.Y = y;
            return this;
        }

        public ControlConfigBuilder WithSize(int width, int height)
        {
            _control.Width = width;
            _control.Height = height;
            return this;
        }

        public ControlConfigBuilder WithText(string text)
        {
            _control.Text = text;
            return this;
        }

        public ControlConfigBuilder WithLayout(double left = -1, double top = -1, double width = -1, double height = -1)
        {
            _control.Layout = new LayoutPosition
            {
                Left = left,
                Top = top,
                Width = width,
                Height = height
            };
            return this;
        }
    }




