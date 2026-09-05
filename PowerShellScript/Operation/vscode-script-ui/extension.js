// ============================================================
// WorkFlowPrj Script 工具面板 — VS Code 扩展入口
// 提供侧边栏 WebView 面板，用于执行 Script/ 目录下的工具脚本
// ============================================================

const vscode = require('vscode');
const path = require('path');
const fs = require('fs');
const { spawn } = require('child_process');

/**
 * @param {vscode.ExtensionContext} context
 */
function activate(context) {
    console.log('[ScriptTools] 扩展已激活');

    // 注册 WebView 面板提供者
    const provider = new ScriptToolPanelProvider(context.extensionUri);
    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider('workflowprjScriptTools', provider, {
            webviewOptions: { retainContextWhenHidden: true }
        })
    );

    // 注册刷新命令
    context.subscriptions.push(
        vscode.commands.registerCommand('workflowprjScriptTools.refresh', () => {
            provider.refresh();
        })
    );

    // 注册打开终端命令
    context.subscriptions.push(
        vscode.commands.registerCommand('workflowprjScriptTools.openLauncher', () => {
            const terminal = vscode.window.createTerminal('ScriptLauncher');
            terminal.sendText(`& "${path.join(getScriptDir(), 'ScriptLauncher.ps1')}"`, true);
            terminal.show();
        })
    );
}

function deactivate() { }

/**
 * 获取 Script 目录路径
 */
function getScriptDir() {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (workspaceFolders && workspaceFolders.length > 0) {
        return path.join(workspaceFolders[0].uri.fsPath, 'Script');
    }
    return path.join(__dirname, '..');
}

/**
 * 获取项目根目录路径
 */
function getProjectRoot() {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (workspaceFolders && workspaceFolders.length > 0) {
        return workspaceFolders[0].uri.fsPath;
    }
    return path.join(__dirname, '..', '..');
}

/**
 * 加载 launcher.config.json 配置
 */
function loadConfig() {
    const configPath = path.join(getScriptDir(), 'launcher.config.json');
    try {
        if (fs.existsSync(configPath)) {
            const raw = fs.readFileSync(configPath, 'utf-8');
            return JSON.parse(raw);
        }
    } catch (err) {
        console.error('[ScriptTools] 加载配置失败:', err.message);
    }
    return null;
}

/**
 * 执行 PowerShell 脚本，通过回调返回输出
 * @param {string} scriptPath - 脚本完整路径
 * @param {string} workDir - 工作目录
 * @param {function} onData - 输出回调 (text, isError)
 * @param {function} onDone - 完成回调 (exitCode)
 * @returns {object} - { cancel: function }
 */
function executePowerShell(scriptPath, workDir, onData, onDone) {
    const args = [
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', scriptPath
    ];

    const child = spawn('powershell.exe', args, {
        cwd: workDir,
        windowsHide: true
    });

    let cancelled = false;

    child.stdout.on('data', (data) => {
        if (!cancelled) {
            const text = data.toString();
            onData(text, false);
        }
    });

    child.stderr.on('data', (data) => {
        if (!cancelled) {
            const text = data.toString();
            onData(text, true);
        }
    });

    child.on('close', (code) => {
        if (!cancelled) {
            onDone(code !== null ? code : -1);
        }
    });

    child.on('error', (err) => {
        if (!cancelled) {
            onData(`[错误] 启动进程失败: ${err.message}\n`, true);
            onDone(-1);
        }
    });

    return {
        cancel: () => {
            cancelled = true;
            if (child && !child.killed) {
                child.kill();
            }
        }
    };
}

/**
 * 执行 ScriptLauncher.ps1 -Tool <alias> 方式
 */
function executeByAlias(alias, onData, onDone) {
    const scriptPath = path.join(getScriptDir(), 'ScriptLauncher.ps1');
    const workDir = getProjectRoot();

    const args = [
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', scriptPath,
        '-Tool', alias
    ];

    const child = spawn('powershell.exe', args, {
        cwd: workDir,
        windowsHide: true
    });

    let cancelled = false;

    child.stdout.on('data', (data) => {
        if (!cancelled) onData(data.toString(), false);
    });

    child.stderr.on('data', (data) => {
        if (!cancelled) onData(data.toString(), true);
    });

    child.on('close', (code) => {
        if (!cancelled) onDone(code !== null ? code : -1);
    });

    child.on('error', (err) => {
        if (!cancelled) {
            onData(`[错误] ${err.message}\n`, true);
            onDone(-1);
        }
    });

    return {
        cancel: () => {
            cancelled = true;
            if (child && !child.killed) child.kill();
        }
    };
}

// ============================================================
// WebView 面板提供者
// ============================================================

class ScriptToolPanelProvider {
    constructor(extensionUri) {
        this._extensionUri = extensionUri;
        this._view = null;
        this._currentExecution = null;
    }

    refresh() {
        if (this._view) {
            const config = loadConfig();
            this._view.webview.postMessage({
                type: 'updateConfig',
                config: config
            });
        }
    }

    resolveWebviewView(webviewView, context, _token) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [
                this._extensionUri,
                vscode.Uri.joinPath(this._extensionUri, 'webview'),
                vscode.Uri.joinPath(this._extensionUri, 'media')
            ]
        };

        webviewView.webview.html = this._getHtmlContent(webviewView.webview);

        // 加载配置并发送到 WebView
        const config = loadConfig();
        if (config) {
            webviewView.webview.postMessage({
                type: 'updateConfig',
                config: config
            });
        }

        // 监听来自 WebView 的消息
        webviewView.webview.onDidReceiveMessage(async (message) => {
            switch (message.type) {
                case 'executeTool':
                    this._handleExecuteTool(message, webviewView);
                    break;

                case 'executeByAlias':
                    this._handleExecuteByAlias(message, webviewView);
                    break;

                case 'cancelExecution':
                    this._handleCancelExecution();
                    break;

                case 'openScript':
                    this._handleOpenScript(message.path);
                    break;

                case 'openTerminal':
                    vscode.commands.executeCommand('workflowprjScriptTools.openLauncher');
                    break;

                case 'getConfig':
                    const cfg = loadConfig();
                    webviewView.webview.postMessage({
                        type: 'updateConfig',
                        config: cfg
                    });
                    break;

                case 'log':
                    console.log(`[ScriptTools-WebView] ${message.text}`);
                    break;
            }
        });
    }

    _handleExecuteTool(message, webviewView) {
        const { scriptPath, workDir, toolName } = message;

        webviewView.webview.postMessage({
            type: 'executionStarted',
            toolName: toolName
        });

        const fullPath = path.join(getScriptDir(), scriptPath);

        this._currentExecution = executePowerShell(
            fullPath,
            workDir === '.' ? getProjectRoot() : path.resolve(getScriptDir(), workDir),
            (text, isError) => {
                if (webviewView && webviewView.webview) {
                    webviewView.webview.postMessage({
                        type: 'executionOutput',
                        text: text,
                        isError: isError
                    });
                }
            },
            (exitCode) => {
                this._currentExecution = null;
                if (webviewView && webviewView.webview) {
                    webviewView.webview.postMessage({
                        type: 'executionDone',
                        exitCode: exitCode
                    });
                }
            }
        );
    }

    _handleExecuteByAlias(message, webviewView) {
        const { alias, toolName } = message;

        webviewView.webview.postMessage({
            type: 'executionStarted',
            toolName: toolName
        });

        this._currentExecution = executeByAlias(
            alias,
            (text, isError) => {
                if (webviewView && webviewView.webview) {
                    webviewView.webview.postMessage({
                        type: 'executionOutput',
                        text: text,
                        isError: isError
                    });
                }
            },
            (exitCode) => {
                this._currentExecution = null;
                if (webviewView && webviewView.webview) {
                    webviewView.webview.postMessage({
                        type: 'executionDone',
                        exitCode: exitCode
                    });
                }
            }
        );
    }

    _handleCancelExecution() {
        if (this._currentExecution) {
            this._currentExecution.cancel();
            this._currentExecution = null;
        }
    }

    _handleOpenScript(scriptPath) {
        const fullPath = path.join(getScriptDir(), scriptPath);
        if (fs.existsSync(fullPath)) {
            vscode.workspace.openTextDocument(fullPath).then(doc => {
                vscode.window.showTextDocument(doc);
            });
        } else {
            vscode.window.showErrorMessage(`文件不存在: ${scriptPath}`);
        }
    }

    _getHtmlContent(webview) {
        const htmlPath = vscode.Uri.joinPath(this._extensionUri, 'webview', 'index.html');
        let html = fs.readFileSync(htmlPath.fsPath, 'utf-8');

        // 替换资源路径
        const webviewUri = webview.asWebviewUri(this._extensionUri);
        html = html.replace(/\{\{webviewUri\}\}/g, webviewUri.toString());

        return html;
    }
}

module.exports = {
    activate,
    deactivate
};
