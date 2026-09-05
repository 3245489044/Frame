# Docker MCP 环境安装步骤

> 请按照以下步骤顺序操作，每一步完成后确认再继续下一步。

---

## 阶段一：安装 WSL2

### 步骤 1.1：启用 WSL 功能
**以管理员身份打开 PowerShell**（右键 → 以管理员身份运行），执行：

```powershell
dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
```

### 步骤 1.2：启用虚拟机平台
在同一个管理员 PowerShell 中执行：

```powershell
dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart
```

### 步骤 1.3：重启电脑
执行完成后，**重启电脑**。

### 步骤 1.4：下载并安装 WSL2 内核更新包
重启后，打开浏览器访问：
https://wslstorestorage.blob.core.windows.net/wslblob/wsl_update_x64.msi

下载并安装该 MSI 包。

### 步骤 1.5：将 WSL2 设为默认版本
以管理员身份打开 PowerShell，执行：

```powershell
wsl --set-default-version 2
```

### 步骤 1.6：安装 Ubuntu 发行版
在 PowerShell 中执行：

```powershell
wsl --install -d Ubuntu
```

安装完成后会提示创建 Linux 用户名和密码，请记住它们。

### 步骤 1.7：验证 WSL2
```powershell
wsl --list --verbose
```

应该看到 Ubuntu 的状态为 `Running`，版本为 `2`。

---

## 阶段二：安装 Docker Desktop

### 步骤 2.1：下载 Docker Desktop
访问 https://www.docker.com/products/docker-desktop/ 下载 Docker Desktop for Windows。

### 步骤 2.2：安装 Docker Desktop
运行安装程序，**务必勾选 "Use WSL 2 based engine"**。

### 步骤 2.3：重启电脑
安装完成后重启电脑。

### 步骤 2.4：验证安装
打开 PowerShell，执行：

```powershell
docker --version
docker info
```

---

## 阶段三：Docker Desktop 配置

### 步骤 3.1：启动 Docker Desktop
双击桌面图标启动 Docker Desktop，等待右下角 Docker 图标变为稳定状态。

### 步骤 3.2：配置 WSL Integration
1. 右键任务栏 Docker 图标 → Settings
2. 进入 **Resources → WSL Integration**
3. 确保 Ubuntu 开关已打开
4. 点击 Apply & Restart

### 步骤 3.3：配置资源限制（可选）
1. Settings → Resources → Advanced
2. 建议设置：CPUs: 4, Memory: 8GB, Swap: 2GB
3. 点击 Apply & Restart

---

## 阶段四：最终验证

### 步骤 4.1：测试 Docker 运行
```powershell
docker run hello-world
```

### 步骤 4.2：确认 WSL2 + Docker 集成
```powershell
wsl -d Ubuntu
# 在 Ubuntu 内执行
docker ps
exit
```

---

## 完成

所有步骤完成后，Docker 环境就搭建好了！后续可以继续构建 MCP 开发环境镜像。
