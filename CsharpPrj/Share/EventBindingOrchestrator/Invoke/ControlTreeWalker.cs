
using Framework.Tree;
using Modules.EventFramework.Utils;
using Share.EventBindingOrchestrator.Parameters;
using TConfig;

namespace Share.EventBindingOrchestrator.Utils
{
    public static class ControlTreeWalker
    {
        private static readonly Lazy<TreeWalker<ControlConfig>> _walker =
            new(() => new TreeWalker<ControlConfig>(GetDirectChildren));

        private static TreeWalker<ControlConfig> Walker => _walker.Value;

        public static IEnumerable<ControlConfig> GetDirectChildren(ControlConfig config)
        {
            if (config == null) yield break;

            if (config.Controls is { Count: > 0 })
                foreach (var child in config.Controls)
                    yield return child;

            if (config.Panel1Controls is { Count: > 0 })
                foreach (var child in config.Panel1Controls)
                    yield return child;

            if (config.Panel2Controls is { Count: > 0 })
                foreach (var child in config.Panel2Controls)
                    yield return child;
        }

        public static void WalkAllViews(UIConfiguration config, Action<ControlConfig> action)
        {
            if (config?.Views == null) return;

            foreach (var view in config.Views)
            {
                if (view.Controls != null)
                    WalkControls(view.Controls, action);
            }
        }

        public static void WalkControls(List<ControlConfig> controls, Action<ControlConfig> action)
        {
            if (controls == null) return;

            foreach (var control in controls)
            {
                Walker.Walk(control, (node, _) => action(node));
            }
        }

        public static async Task WalkAllViewsAsync(UIConfiguration config, Func<ControlConfig, int, Task> asyncAction)
        {
            if (config?.Views == null) return;

            foreach (var view in config.Views)
            {
                if (view.Controls != null)
                    await WalkControlsAsync(view.Controls, asyncAction);
            }
        }

        public static Task WalkAllViewsAsync(UIConfiguration config, Func<ControlConfig, Task> asyncAction)
            => WalkAllViewsAsync(config, (ctrl, _) => asyncAction(ctrl));

        public static async Task WalkControlsAsync(List<ControlConfig> controls, Func<ControlConfig, int, Task> asyncAction)
        {
            if (controls == null) return;

            foreach (var control in controls)
            {
                await Walker.WalkAsync(control, (node, depth) => asyncAction(node, depth));
            }
        }

        public static Task WalkControlsAsync(List<ControlConfig> controls, Func<ControlConfig, Task> asyncAction)
            => WalkControlsAsync(controls, (ctrl, _) => asyncAction(ctrl));

        public static ControlConfig? FindControl(UIConfiguration config, string name)
        {
            if (config?.Views == null || string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var view in config.Views)
            {
                if (view.Controls != null)
                {
                    var found = FindControl(view.Controls, name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        public static ControlConfig? FindControl(List<ControlConfig> controls, string name)
        {
            if (controls == null || controls.Count == 0 || string.IsNullOrWhiteSpace(name))
                return null;

            ControlConfig? found = null;
            WalkControls(controls, ctrl =>
            {
                if (found == null && string.Equals(ctrl.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = ctrl;
                }
            });
            return found;
        }


        public static object? FindControl(object? container, string name, string? scopePath)
        {
            if (container == null || string.IsNullOrWhiteSpace(name))
                return null;

            if (!string.IsNullOrWhiteSpace(scopePath))
            {
                var byPath = FindControlByPath(container, scopePath);
                if (byPath != null)
                    return byPath;
            }

            if (container is System.Windows.Forms.Control rootControl)
            {
                var found = rootControl.Controls.Find(name, true);
                if (found.Length > 0)
                    return found[0];

                if (string.Equals(rootControl.Name, name, StringComparison.OrdinalIgnoreCase))
                    return rootControl;
            }

            object? foundByReflection = null;
            var walker = new TreeWalker<object>(ReflectionChildControlProvider.GetChildControls);
            walker.Walk(container, (ctrl, _) =>
            {
                if (foundByReflection != null) return;
                var ctrlName = EventBindingHelper.GetControlName(ctrl);
                if (string.Equals(ctrlName, name, StringComparison.OrdinalIgnoreCase))
                {
                    foundByReflection = ctrl;
                }
            });

            return foundByReflection;
        }

        private static object? FindControlByPath(object? container, string scopePath)
        {
            if (container == null || string.IsNullOrWhiteSpace(scopePath))
                return null;

            if (container is not System.Windows.Forms.Control root)
                return null;

            var segments = scopePath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return null;

            object? current = root;
            foreach (var segment in segments)
            {
                if (current is not System.Windows.Forms.Control currentCtrl)
                    return null;

                if (string.Equals(currentCtrl.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var next = currentCtrl.Controls.Find(segment, false);
                if (next.Length == 0)
                    return null;
                current = next[0];
            }

            return current;
        }

        public static Dictionary<string, ControlConfig> BuildControlMap(UIConfiguration config)
        {
            var map = new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase);
            WalkAllViews(config, ctrl =>
            {
                if (!string.IsNullOrWhiteSpace(ctrl.Name) && !map.ContainsKey(ctrl.Name))
                    map[ctrl.Name] = ctrl;
            });
            return map;
        }

        public static Dictionary<string, string> BuildControlPathMap(UIConfiguration config)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (config?.Views == null) return map;

            foreach (var view in config.Views)
            {
                if (view.Controls == null) continue;

                foreach (var root in view.Controls)
                {
                    WalkWithPath(root, string.Empty, map);
                }
            }
            return map;
        }

        private static void WalkWithPath(ControlConfig node, string parentPath, Dictionary<string, string> map)
        {
            if (node == null) return;

            var path = string.IsNullOrWhiteSpace(parentPath)
                ? node.Name
                : $"{parentPath}.{node.Name}";

            if (!string.IsNullOrWhiteSpace(node.Name))
                map[node.Name] = path;

            if (node.Controls is { Count: > 0 })
            {
                foreach (var child in node.Controls)
                    WalkWithPath(child, path, map);
            }

            if (node.Panel1Controls is { Count: > 0 })
            {
                foreach (var child in node.Panel1Controls)
                    WalkWithPath(child, path, map);
            }

            if (node.Panel2Controls is { Count: > 0 })
            {
                foreach (var child in node.Panel2Controls)
                    WalkWithPath(child, path, map);
            }
        }

        public static List<string> CollectControlNames(UIConfiguration config, Func<ControlConfig, bool>? predicate = null)
        {
            var names = new List<string>();
            WalkAllViews(config, ctrl =>
            {
                if (!string.IsNullOrWhiteSpace(ctrl.Name) && (predicate == null || predicate(ctrl)))
                {
                    names.Add(ctrl.Name);
                }
            });
            return names;
        }

        public static List<string> CollectNames(List<ControlConfig> controls, List<string>? names = null)
        {
            names ??= new List<string>();
            if (controls == null) return names;

            WalkControls(controls, ctrl =>
            {
                if (!string.IsNullOrWhiteSpace(ctrl.Name))
                {
                    names.Add(ctrl.Name);
                }
            });
            return names;
        }
    }
}