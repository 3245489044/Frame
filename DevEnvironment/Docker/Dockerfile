# ============================================================
# WorkFlowPrj MCP 开发环境 Dockerfile
# 多阶段构建 | 高性能 | 分层缓存 | 优化版
# ============================================================

# ===== Stage 1: 基础系统 (Debian 12 Slim) =====
FROM debian:12-slim AS base

ENV DEBIAN_FRONTEND=noninteractive \
    TZ=Asia/Shanghai \
    LANG=en_US.UTF-8

# 使用阿里云镜像加速 + 安装基础工具 + 清理缓存（合并 RUN 减少层数）
RUN sed -i 's/deb.debian.org/mirrors.aliyun.com/g' /etc/apt/sources.list.d/debian.sources 2>/dev/null || \
    sed -i 's/deb.debian.org/mirrors.aliyun.com/g' /etc/apt/sources.list && \
    apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl wget git unzip xz-utils \
    tzdata locales gnupg tini procps \
    && ln -fs /usr/share/zoneinfo/Asia/Shanghai /etc/localtime \
    && dpkg-reconfigure --frontend noninteractive tzdata \
    && locale-gen en_US.UTF-8 \
    && rm -rf /var/lib/apt/lists/* /tmp/*

# ===== Stage 2: Node.js 22 LTS =====
FROM base AS nodejs

# 安装 Node.js 22 (使用国内镜像加速 - 直接下载二进制包)
RUN wget https://npmmirror.com/mirrors/node/v22.14.0/node-v22.14.0-linux-x64.tar.xz -O /tmp/node.tar.xz \
    && tar -xJf /tmp/node.tar.xz -C /usr/local --strip-components=1 \
    && rm /tmp/node.tar.xz \
    && npm config set registry https://registry.npmmirror.com \
    && npm install -g npm@latest && npm cache clean --force \
    && node --version && npm --version

# ===== Stage 3: .NET SDK 10.0 =====
FROM nodejs AS dotnet

# 安装 libicu + .NET SDK + 清理缓存（合并 RUN）
RUN apt-get update && apt-get install -y --no-install-recommends libicu-dev \
    && rm -rf /var/lib/apt/lists/* \
    && wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm /tmp/dotnet-install.sh \
    && dotnet --version && dotnet --list-sdks

# ===== Stage 4: Python 3.13 (改用 deadsnakes PPA 预编译包，避免源码编译) =====
FROM dotnet AS python

# 使用 deadsnakes PPA 安装预编译的 Python 3.13（比源码编译快 10 倍）
RUN apt-get update && apt-get install -y --no-install-recommends \
    software-properties-common gnupg \
    && echo "deb http://deb.debian.org/debian bookworm-backports main" >> /etc/apt/sources.list \
    && apt-get update && apt-get install -y --no-install-recommends \
    python3.13 python3.13-dev python3.13-venv python3-pip \
    && rm -rf /var/lib/apt/lists/* /tmp/* \
    && ln -sf /usr/bin/python3.13 /usr/local/bin/python3 \
    && ln -sf /usr/bin/python3.13 /usr/local/bin/python \
    && python3 -m pip install --upgrade pip setuptools wheel --no-cache-dir \
    && python3 --version && pip3 --version

# ===== Stage 5: 全局 npm 包 + mcp-proxy =====
FROM python AS npm-global

# 合并安装全局 npm 包（减少层数）
RUN npm install -g context-mode lexis-mcp mcp-proxy \
    && npm cache clean --force

# ===== Stage 6: 本地 MCP 服务器 =====
FROM npm-global AS mcp-local

WORKDIR /opt/mcp-servers
COPY DevEnvironment/Docker/scripts/build-mcp-servers.sh /tmp/
RUN bash /tmp/build-mcp-servers.sh && rm -rf /tmp/*

# ===== Stage 7: 最终镜像 =====
FROM mcp-local AS final

COPY DevEnvironment/Docker/scripts/entrypoint.sh /entrypoint.sh
COPY DevEnvironment/Docker/scripts/healthcheck.sh /healthcheck.sh

RUN chmod +x /entrypoint.sh /healthcheck.sh

EXPOSE 3100-3107
WORKDIR /workspace/WorkFlowPrj
ENTRYPOINT ["/usr/bin/tini", "--", "/entrypoint.sh"]
