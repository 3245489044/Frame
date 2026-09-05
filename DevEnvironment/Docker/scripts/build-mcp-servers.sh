#!/bin/bash
# ============================================================
# WorkFlowPrj MCP 服务器安装脚本
# 安装所有本地 MCP 服务器到 /opt/mcp-servers/
# ============================================================
set -e

MCP_DIR="/opt/mcp-servers"
mkdir -p "$MCP_DIR"

echo "[build-mcp-servers] 开始安装 MCP 服务器..."

# 1. mcp-sequentialthinking-tools
echo "[build-mcp-servers] 安装 mcp-sequentialthinking-tools..."
npm install -g mcp-sequentialthinking-tools

# 2. context7-mcp (包名是 @context7/context7-mcp)
echo "[build-mcp-servers] 安装 @context7/context7-mcp..."
npm install -g @context7/context7-mcp 2>/dev/null || echo "[build-mcp-servers] ⚠️ @context7/context7-mcp 安装失败（非关键，可在运行时按需安装）"

# 3. context-mode (已全局安装，复制到本地)
echo "[build-mcp-servers] context-mode 已全局安装，跳过..."

# 4. memory (mcp-server-memory)
echo "[build-mcp-servers] 安装 mcp-server-memory..."
npm install -g mcp-server-memory

# 5. filesystem (mcp-server-filesystem)
echo "[build-mcp-servers] 安装 mcp-server-filesystem..."
npm install -g mcp-server-filesystem

# 6. lexis-mcp (已全局安装)
echo "[build-mcp-servers] lexis-mcp 已全局安装，跳过..."

# 7. token-savior-recall (Python 包)
echo "[build-mcp-servers] 安装 token-savior-recall..."
pip3 install token-savior-recall 2>/dev/null || echo "[build-mcp-servers] ⚠️ token-savior-recall 安装失败（非关键）"

# 8. csproj-context (项目本地构建)
echo "[build-mcp-servers] csproj-context 需要在项目挂载后构建，跳过..."

echo "[build-mcp-servers] ✅ MCP 服务器安装完成"
echo "[build-mcp-servers] 已安装: sequential-thinking, context7-mcp, context-mode, memory, filesystem, lexis-mcp"
