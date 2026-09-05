﻿
#nullable enable

using Framework.Tree;
using FrameworkAbstractions.Utilities;
using FrontEndDriven.Container;

namespace FrontEndDriven.Finder
{
    public class RecursionPolicy
    {
        public int MaxDepth { get; set; }

        public static RecursionPolicy Default => new();

        public static RecursionPolicy DirectChildren => new() { MaxDepth = 1 };
    }

    public static class ControlFinder
    {
        private static readonly TreeWalker<Control> _treeWalker =
            new(c => c.Controls.Cast<Control>());

        #region 基于 TreeWalker 的查找

        public static T? FindByType<T>(Control root) where T : Control
        {
            if (root == null) return null;
            return _treeWalker.FindFirst(root, c => c is T) as T;
        }

        public static Control? Find(Control root, Func<Control, bool> predicate)
        {
            if (root == null || predicate == null) return null;
            return _treeWalker.FindFirst(root, predicate);
        }

        public static List<Control> FindAll(Control root, Func<Control, bool> predicate)
        {
            if (root == null || predicate == null) return new List<Control>();
            return _treeWalker.FindAll(root, predicate);
        }

        public static List<T> FindAllByType<T>(Control root) where T : Control
        {
            if (root == null) return new List<T>();
            return _treeWalker.FindAll(root, c => c is T).Cast<T>().ToList();
        }

        #endregion

        #region 支持 RecursionPolicy 的查找（基于 TreeWalker）

        public static Control? FindDirectChild(Control parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;
            return parent.Controls.ContainsKey(name) ? parent.Controls[name] : null;
        }

        public static Control? FindRecursive(Control parent, string name, RecursionPolicy? policy = null)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;
            policy ??= RecursionPolicy.Default;

            // 按名递归深搜统一到 Share.ControlLookup.ControlTraverser（Controls.Find(name,true) / 显式栈 DFS），
            // 消除与框架遍历的双轨（RecursionPolicy.MaxDepth → ControlTraverser.maxDepth 对齐）。
            return Share.ControlLookup.ControlTraverser.FindByName(parent, name, policy.MaxDepth);
        }

        public static T? FindByType<T>(Control parent, string? name = null,
            RecursionPolicy? policy = null) where T : Control
        {
            if (parent == null) return null;
            policy ??= RecursionPolicy.Default;

            // 统一到 ControlTraverser（不含根自身，与旧 BuildTraversalOptions(IncludeRoot=false) 语义一致）
            return Share.ControlLookup.ControlTraverser.FindFirst(
                c => c is T && (name == null || c.Name == name), parent, policy.MaxDepth) as T;
        }

        public static Control? FindByPredicate(Control parent, Func<Control, bool> predicate,
            RecursionPolicy? policy = null)
        {
            if (parent == null || predicate == null) return null;
            policy ??= RecursionPolicy.Default;

            return Share.ControlLookup.ControlTraverser.FindFirst(predicate, parent, policy.MaxDepth);
        }

        public static List<Control> FindAll(Control parent, string name,
            RecursionPolicy? policy = null)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return new List<Control>();
            policy ??= RecursionPolicy.Default;

            return Share.ControlLookup.ControlTraverser.FindAll(c => c.Name == name, parent, policy.MaxDepth);
        }

        #endregion

        #region 基于祖先链的工具方法

        public static EcsConfigContainer? FindNearestConfigContainer(Control control)
        {
            if (control == null) return null;
            // 最近祖先容器（不含自身，语义与原 TreePathFinder.FindNearestAncestor 一致）
            for (var current = control.Parent; current != null; current = current.Parent)
            {
                if (current is EcsConfigContainer container)
                    return container;
            }
            return null;
        }

        public static bool IsWithinDepth(Control root, Control target, int maxDepth)
        {
            if (root == null || target == null) return false;

            // 显式栈外祖先链：与原 TreePathFinder.IsWithinDepth 逐语义对齐（target 自身不计入深度）
            int depth = 0;
            for (var current = target.Parent; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, root))
                    return depth < maxDepth;
                depth++;
                if (depth >= maxDepth) return false;
            }
            return false;
        }

        public static bool IsDescendantOf(Control control, Control ancestor)
            => Share.ControlLookup.ControlTraverser.IsDescendantOf(control, ancestor);

        public static int GetDepth(Control control)
            => Share.ControlLookup.ControlTraverser.GetDepth(control);

        #endregion

        #region 跨 Form 查找（整合 WinFormsCommandEngine 的手写遍历逻辑）

        public static bool ForEachOpenFormSafe(Func<Form, bool> formAction)
        {
            return SafeEnumerator.ForEachSafe<Form>(
                Application.OpenForms,
                formAction,
                isValid: form => !form.IsDisposed);
        }

        public static Control? FindAcrossForms(
            Func<Form, bool> formMatcher,
            Func<Form, Control?> controlLookup)
        {
            Control? result = null;
            ForEachOpenFormSafe(form =>
            {
                if (formMatcher(form))
                {
                    var found = controlLookup(form);
                    if (found != null)
                    {
                        result = found;
                        return true;
                    }
                }
                return false;
            });
            return result;
        }

        public static Control? FindAcrossOpenForms(string controlName, RecursionPolicy? policy = null)
        {
            if (string.IsNullOrEmpty(controlName)) return null;
            policy ??= RecursionPolicy.Default;

            return FindAcrossForms(
                formMatcher: form => form.Visible,
                controlLookup: form => FindRecursive(form, controlName, policy));
        }

        public static TControl? FindByName<TControl>(string controlName, RecursionPolicy? policy = null)
            where TControl : Control
        {
            return FindAcrossForms(
                formMatcher: _ => true,
                controlLookup: form => FindRecursive(form, controlName, policy) as TControl) as TControl;
        }

        public static Control? FindInForm(string formName, string controlName, RecursionPolicy? policy = null)
        {
            if (string.IsNullOrEmpty(formName) || string.IsNullOrEmpty(controlName)) return null;
            policy ??= RecursionPolicy.Default;

            return FindAcrossForms(
                formMatcher: form => form.Name == formName || form.Text == formName,
                controlLookup: form => FindRecursive(form, controlName, policy));
        }

        #endregion


        public static Form? FindForm(Control? control)
        {
            return control?.FindForm();
        }
    }
}
