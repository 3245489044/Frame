#nullable enable

using System.Collections.Generic;

namespace FrontEndDriven.Abstractions
{
    /// <summary>
    /// UI 用户态布局缓存端口（框架层抽象，实现由宿主注入）。
    /// <para>
    /// 对象域分离：配置面（MainLayout.json 等）是「声明默认值」，本端口只承载「用户拖拽/调整后的结果」，
    /// 二者不互相写回（配置保持版本化/可分发，用户态落在数据根）。
    /// </para>
    /// <para>
    /// 消费方：<c>ConfigDrivenControlFactory.ConvertControl</c>（读：ECS 真源创建前覆盖默认比例）、
    /// <c>WinFormsOutputPort.OnSplitterMoved</c>（写：拖拽唯一真源回写点）、
    /// <c>MainForm</c>（窗体尺寸/位置读写与退出 Flush）。
    /// </para>
    /// </summary>
    public interface IUiLayoutStateStore
    {
        /// <summary>读取某作用域下指定 splitContainer 的用户比例；未缓存返回 false（调用方回退配置值）。</summary>
        bool TryGetSplitterRatio(string scope, string controlName, out double ratio);

        /// <summary>记录用户拖拽后的比例（值域 [0,1]；实现负责去抖与原子落盘）。</summary>
        void SetSplitterRatio(string scope, string controlName, double ratio);

        /// <summary>读取某作用域上次加载时记录的配置布局指纹；未记录（旧版缓存）返回 null。</summary>
        string? GetLayoutFingerprint(string scope);

        /// <summary>记录本次加载的配置布局指纹（供下次启动判定缓存是否仍属同一版式）。</summary>
        void SetLayoutFingerprint(string scope, string? layoutFingerprint);

        /// <summary>清除用户分隔比例（<paramref name="scope"/> 为 null ⇒ 全部作用域），指纹一并清除；下次加载回退配置设计值。</summary>
        void ResetSplitterRatios(string? scope = null);

        /// <summary>
        /// 读取某面板作用域下持久化的控件状态（键 → 值）；未缓存返回 false。
        /// <para>与 splitter/form 同一存储件、同一落盘策略（数据根 \UI\ui-layout-state.json）。</para>
        /// </summary>
        bool TryGetPanelValues(string scope, out IReadOnlyDictionary<string, string> values);

        /// <summary>记录单个面板控件值（null/空 ⇒ 删除该键）。</summary>
        void SetPanelValue(string scope, string key, string? value);

        /// <summary>清除面板状态（<paramref name="scope"/> 为 null ⇒ 全部）；不影响分隔比例与窗体边界。</summary>
        void ResetPanelValues(string? scope = null);

        /// <summary>读取上次关闭时的窗体边界；未缓存返回 false。</summary>
        bool TryGetFormBounds(out UiFormBounds bounds);

        /// <summary>记录当前窗体边界（窗口状态用于最大化恢复）。</summary>
        void SetFormBounds(UiFormBounds bounds);

        /// <summary>立即落盘（关闭/退出时调用；幂等）。</summary>
        void Flush();
    }

    /// <summary>窗体边界快照（像素；WindowState 取 <see cref="System.Windows.Forms.FormWindowState"/> 名）。</summary>
    public readonly record struct UiFormBounds(int Width, int Height, int Left, int Top, string? WindowState);
}
