#!/bin/bash
# ============================================================
# WorkFlowPrj MCP 环境健康检查脚本
# 检查 mcp-proxy 的 /ping 端点（所有 mcp-proxy 实例都支持）
# ============================================================

# 检查 MCP 服务是否就绪（通过 mcp-proxy 的 /ping 端点）
if curl -s -o /dev/null -w "%{http_code}" --max-time 5 http://localhost:3100/ping 2>/dev/null | grep -q "200"; then
    exit 0
fi

# 备用方案：使用 bash 的 /dev/tcp
exec 3<>/dev/tcp/localhost/3100 2>/dev/null && { exec 3>&-; exit 0; } || exit 1
