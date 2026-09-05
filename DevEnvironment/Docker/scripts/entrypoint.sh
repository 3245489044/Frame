#!/bin/bash
# ============================================================
# WorkFlowPrj MCP 环境容器入口脚本
# 数据驱动版：从 mcp-servers.json 读取配置，通用循环启动
# ============================================================
# ⚠️ 使用 set +e 而非 set -e：后台进程失败不应导致整个脚本退出
set +e

echo "============================================"
echo " WorkFlowPrj MCP 开发环境"
echo " 基础系统: Debian 12 Slim"
echo " Node.js:  $(node --version)"
echo " .NET SDK: $(dotnet --version 2>/dev/null || echo 'N/A')"
echo " Python:   $(python3 --version 2>/dev/null || echo 'N/A')"
echo "============================================"

# ============================================================
# 启动前清理
# ============================================================
echo "[entrypoint] 清理 /tmp 临时文件..."
rm -rf /tmp/* 2>/dev/null || true

# 设置 Node.js 内存限制
export NODE_OPTIONS="--max-old-space-size=2048"
echo "[entrypoint] NODE_OPTIONS=$NODE_OPTIONS"

# ============================================================
# MCP 服务配置路径（容器内路径）
# ============================================================
MCP_CONFIG="/workspace/WorkFlowPrj/DevEnvironment/Docker/config/mcp-servers.json"

# ============================================================
# 通用服务启动函数
# 从 JSON 配置中读取 name/port/command/args/env/preStart/condition
# ============================================================
start_service() {
  local name port command args_str env_json pre_start condition

  name=$(echo "$1" | python3 -c "import sys,json; print(json.load(sys.stdin).get('name',''))")
  port=$(echo "$1" | python3 -c "import sys,json; print(json.load(sys.stdin).get('port',''))")
  command=$(echo "$1" | python3 -c "import sys,json; print(json.load(sys.stdin).get('command',''))")
  args_str=$(echo "$1" | python3 -c "
import sys,json
args = json.load(sys.stdin).get('args', [])
print(' '.join(args))
")
  env_json=$(echo "$1" | python3 -c "
import sys,json
env = json.load(sys.stdin).get('env', {})
for k,v in env.items():
    print(f'{k}={v}')
")
  pre_start=$(echo "$1" | python3 -c "
import sys,json
print(json.load(sys.stdin).get('preStart', '') or '')
")
  condition=$(echo "$1" | python3 -c "
import sys,json
print(json.load(sys.stdin).get('condition', '') or '')
")

  # 条件判断：condition 为空或执行成功才继续
  if [ -n "$condition" ]; then
    if ! eval "$condition" > /dev/null 2>&1; then
      echo "[entrypoint] ⚠️  跳过 $name（条件不满足: $condition）"
      return
    fi
  fi

  # 前置操作
  if [ -n "$pre_start" ]; then
    echo "[entrypoint] $name: 执行前置操作..."
    eval "$pre_start" 2>/dev/null || true
  fi

  # 设置环境变量
  local env_prefix=""
  if [ -n "$env_json" ]; then
    while IFS= read -r line; do
      [ -z "$line" ] && continue
      env_prefix="$env_prefix $line"
    done <<< "$env_json"
  fi

  # 启动服务
  echo "[entrypoint] 启动 $name (端口 $port)..."
  # shellcheck disable=SC2086
  env $env_prefix npx mcp-proxy --port "$port" --stateless -- $command $args_str
}

# ============================================================
# 并行启动所有 MCP 服务
# ============================================================
echo "[entrypoint] 并行启动 MCP 服务..."

if [ ! -f "$MCP_CONFIG" ]; then
  echo "[entrypoint] ❌ 未找到配置文件: $MCP_CONFIG"
  echo "[entrypoint] 保持容器运行，等待请求..."
  wait
  exit 0
fi

# 读取服务列表并逐个启动
# 使用临时文件避免管道子 shell 问题
SERVER_LIST=$(python3 -c "
import json, sys
with open('$MCP_CONFIG') as f:
    config = json.load(f)
for s in config['servers']:
    print(json.dumps(s))
")
while IFS= read -r server_json; do
  [ -z "$server_json" ] && continue
  start_service "$server_json" &
done <<< "$SERVER_LIST"

# ============================================================
# 并行等待所有端口就绪（整体超时 20 秒）
# ============================================================
echo "[entrypoint] 并行等待所有 MCP 服务端口就绪..."

# 获取所有端口列表
PORTS=$(python3 -c "
import json
with open('$MCP_CONFIG') as f:
    config = json.load(f)
print(' '.join(str(s['port']) for s in config['servers']))
")

wait_for_port() {
  local port=$1
  local timeout=20
  local waited=0
  while [ $waited -lt $timeout ]; do
    if command -v ss > /dev/null 2>&1 && ss -tlnp 2>/dev/null | grep -q ":$port "; then
      echo "  ✅ 端口 $port 已就绪（${waited}s）"
      return 0
    fi
    sleep 1
    waited=$((waited + 1))
  done
  echo "  ⚠️  端口 $port 超时未就绪（已等待 ${timeout} 秒）"
  return 1
}

# 并行等待所有端口
for port in $PORTS; do
  wait_for_port "$port" &
done

# 等待所有端口检测完成
wait
echo "[entrypoint] 端口就绪检测完成"

# 保持容器运行
echo "[entrypoint] 容器就绪，等待请求..."
wait
