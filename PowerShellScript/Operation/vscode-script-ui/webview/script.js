// ============================================================
// WorkFlowPrj Script 工具面板 — 前端逻辑
// 负责 UI 渲染、用户交互、与扩展通信
// ============================================================

(function () {
    'use strict';

    // VS Code API
    const vscode = acquireVsCodeApi();

    // ============================================================
    // 状态管理
    // ============================================================

    const state = {
        config: null,
        currentCategory: null,
        isExecuting: false,
        executingToolName: null,
        autoScroll: true,
        logLines: []
    };

    // ============================================================
    // DOM 引用
    // ============================================================

    const $ = (id) => document.getElementById(id);
    const dom = {
        statusText: $('statusBar').querySelector('.status-text'),
        statusDot: $('statusDot'),
        categoryGrid: $('categoryGrid'),
        categoriesSection: $('categoriesSection'),
        toolsSection: $('toolsSection'),
        categoryTitle: $('categoryTitle'),
        toolList: $('toolList'),
        logContainer: $('logContainer'),
        btnBack: $('btnBack'),
        btnRefresh: $('btnRefresh'),
        btnTerminal: $('btnTerminal'),
        btnCancel: $('btnCancel'),
        btnClearLog: $('btnClearLog'),
        btnScrollLock: $('btnScrollLock')
    };

    // ============================================================
    // 工具函数
    // ============================================================

    function formatTime() {
        const now = new Date();
        return now.toLocaleTimeString('zh-CN', { hour12: false });
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function getCategoryIcon(catKey) {
        const icons = {
            'build': '🔧',
            'solution': '📋',
            'analysis': '🔍',
            'git': '🌿',
            'env-config': '⚙️',
            'filedriven': '🧪',
            'vscode': '💻',
            'test': '🧪'
        };
        return icons[catKey] || '📁';
    }

    // ============================================================
    // 状态栏更新
    // ============================================================

    function setStatus(text, type) {
        dom.statusText.textContent = text;
        dom.statusDot.className = 'status-dot ' + (type || 'idle');
    }

    // ============================================================
    // 分类卡片渲染
    // ============================================================

    function renderCategories() {
        if (!state.config || !state.config.categories) return;

        const categories = state.config.categories;
        const tools = state.config.tools;

        // 按 order 排序
        const sortedKeys = Object.keys(categories).sort((a, b) =>
            (categories[a].order || 99) - (categories[b].order || 99)
        );

        dom.categoryGrid.innerHTML = '';

        sortedKeys.forEach(catKey => {
            const cat = categories[catKey];
            const icon = cat.icon || getCategoryIcon(catKey);

            // 计算该分类下的工具数量
            let toolCount = 0;
            if (tools) {
                Object.values(tools).forEach(t => {
                    if (t.category === catKey) toolCount++;
                });
            }

            const card = document.createElement('div');
            card.className = 'category-card';
            card.dataset.catKey = catKey;
            card.innerHTML = `
                <span class="cat-icon">${icon}</span>
                <span class="cat-name">${escapeHtml(cat.name)}</span>
                <span class="cat-count">${toolCount} 个工具</span>
            `;
            card.addEventListener('click', () => showCategoryTools(catKey));
            dom.categoryGrid.appendChild(card);
        });
    }

    // ============================================================
    // 工具列表渲染
    // ============================================================

    function showCategoryTools(catKey) {
        if (!state.config) return;

        const cat = state.config.categories[catKey];
        if (!cat) return;

        state.currentCategory = catKey;
        const icon = cat.icon || getCategoryIcon(catKey);

        dom.categoryTitle.textContent = `${icon} ${cat.name}`;
        dom.categoriesSection.style.display = 'none';
        dom.toolsSection.style.display = 'block';

        // 收集该分类下的工具
        const tools = [];
        if (state.config.tools) {
            Object.entries(state.config.tools).forEach(([key, value]) => {
                if (value.category === catKey) {
                    tools.push({ key, ...value });
                }
            });
        }

        dom.toolList.innerHTML = '';

        if (tools.length === 0) {
            dom.toolList.innerHTML = `
                <div class="empty-state">
                    <span class="empty-icon">📭</span>
                    <span class="empty-text">该分类下暂无工具</span>
                </div>
            `;
            return;
        }

        tools.forEach(tool => {
            const item = document.createElement('div');
            item.className = 'tool-item';
            item.dataset.toolKey = tool.key;

            const alias = tool.alias ? `<span class="alias">[${tool.alias}]</span>` : '';
            const fileName = tool.key.split('/').pop();

            item.innerHTML = `
                <div class="tool-item-header">
                    <div class="tool-info">
                        <div class="tool-desc">${escapeHtml(tool.description || '（无描述）')}</div>
                        <div class="tool-meta">
                            ${escapeHtml(fileName)} ${alias}
                        </div>
                    </div>
                    <div class="tool-actions">
                        <button class="btn-open" data-path="${escapeHtml(tool.key)}">📄</button>
                        <button class="btn-run" data-alias="${escapeHtml(tool.alias || '')}" data-path="${escapeHtml(tool.key)}" data-workdir="${escapeHtml(tool.workDir || '.')}">
                            ▶ 运行
                        </button>
                    </div>
                </div>
            `;

            // 绑定事件
            const runBtn = item.querySelector('.btn-run');
            runBtn.addEventListener('click', () => handleRunTool(tool, runBtn));

            const openBtn = item.querySelector('.btn-open');
            openBtn.addEventListener('click', () => {
                vscode.postMessage({ type: 'openScript', path: tool.key });
            });

            dom.toolList.appendChild(item);
        });
    }

    // ============================================================
    // 工具执行
    // ============================================================

    function handleRunTool(tool, btn) {
        if (state.isExecuting) {
            addLogLine('[系统] 已有任务正在执行，请等待完成或取消', 'system');
            return;
        }

        state.isExecuting = true;
        state.executingToolName = tool.description || tool.key;

        btn.classList.add('running');
        btn.textContent = '⏳ 执行中';
        dom.btnCancel.style.display = 'inline-block';

        setStatus(`执行中: ${state.executingToolName}`, 'running');

        addLogLine(`[系统] ▶ 开始执行: ${tool.description || tool.key}`, 'info');
        addLogLine(`[系统]   脚本: ${tool.key}`, 'system');
        addLogLine(`[系统]   时间: ${formatTime()}`, 'system');
        addLogLine('', 'system');

        // 通过别名执行（更可靠，与 ScriptLauncher 一致）
        if (tool.alias) {
            vscode.postMessage({
                type: 'executeByAlias',
                alias: tool.alias,
                toolName: tool.description || tool.key
            });
        } else {
            vscode.postMessage({
                type: 'executeTool',
                scriptPath: tool.key,
                workDir: tool.workDir || '.',
                toolName: tool.description || tool.key
            });
        }
    }

    function handleCancelExecution() {
        if (state.isExecuting) {
            vscode.postMessage({ type: 'cancelExecution' });
            addLogLine('[系统] ⏹ 用户取消执行', 'error');
            finishExecution(-1);
        }
    }

    function finishExecution(exitCode) {
        state.isExecuting = false;
        state.executingToolName = null;

        dom.btnCancel.style.display = 'none';

        // 重置所有运行按钮
        document.querySelectorAll('.btn-run').forEach(btn => {
            btn.classList.remove('running');
            btn.textContent = '▶ 运行';
        });

        if (exitCode === 0) {
            setStatus('执行成功', 'success');
            addLogLine('', 'system');
            addLogLine(`[系统] ✅ 执行完毕 (退出码: ${exitCode})`, 'success');
        } else {
            setStatus(`执行失败 (退出码: ${exitCode})`, 'error');
            addLogLine('', 'system');
            addLogLine(`[系统] ❌ 执行失败 (退出码: ${exitCode})`, 'error');
        }
    }

    // ============================================================
    // 日志管理
    // ============================================================

    function addLogLine(text, className) {
        const line = document.createElement('div');
        line.className = 'log-line ' + (className || 'stdout');

        if (text === '') {
            line.innerHTML = '&nbsp;';
        } else {
            line.textContent = text;
        }

        // 移除占位符
        const placeholder = dom.logContainer.querySelector('.log-placeholder');
        if (placeholder) placeholder.remove();

        dom.logContainer.appendChild(line);
        state.logLines.push({ text, className });

        // 自动滚动
        if (state.autoScroll) {
            dom.logContainer.scrollTop = dom.logContainer.scrollHeight;
        }
    }

    function clearLog() {
        dom.logContainer.innerHTML = `
            <div class="log-placeholder">点击工具按钮执行脚本，输出将显示在这里...</div>
        `;
        state.logLines = [];
    }

    // ============================================================
    // 消息处理（来自扩展）
    // ============================================================

    function handleMessage(event) {
        const message = event.data;

        switch (message.type) {
            case 'updateConfig':
                state.config = message.config;
                renderCategories();
                // 如果当前在分类视图，刷新
                if (state.currentCategory && state.config) {
                    const catExists = state.config.categories &&
                        state.config.categories[state.currentCategory];
                    if (catExists) {
                        showCategoryTools(state.currentCategory);
                    } else {
                        backToCategories();
                    }
                }
                break;

            case 'executionStarted':
                // 已在 handleRunTool 中处理
                break;

            case 'executionOutput':
                addLogLine(message.text, message.isError ? 'stderr' : 'stdout');
                break;

            case 'executionDone':
                finishExecution(message.exitCode);
                break;
        }
    }

    // ============================================================
    // 导航
    // ============================================================

    function backToCategories() {
        state.currentCategory = null;
        dom.categoriesSection.style.display = 'block';
        dom.toolsSection.style.display = 'none';
    }

    // ============================================================
    // 初始化
    // ============================================================

    function init() {
        // 请求配置
        vscode.postMessage({ type: 'getConfig' });

        // 事件绑定
        dom.btnBack.addEventListener('click', backToCategories);
        dom.btnRefresh.addEventListener('click', () => {
            vscode.postMessage({ type: 'getConfig' });
            setStatus('刷新中...', 'idle');
        });
        dom.btnTerminal.addEventListener('click', () => {
            vscode.postMessage({ type: 'openTerminal' });
        });
        dom.btnCancel.addEventListener('click', handleCancelExecution);
        dom.btnClearLog.addEventListener('click', clearLog);
        dom.btnScrollLock.addEventListener('click', () => {
            state.autoScroll = !state.autoScroll;
            dom.btnScrollLock.style.opacity = state.autoScroll ? '1' : '0.5';
        });

        // 监听来自扩展的消息
        window.addEventListener('message', handleMessage);

        setStatus('就绪', 'idle');
    }

    // 页面加载完成后初始化
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
