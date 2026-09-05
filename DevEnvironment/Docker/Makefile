# WorkFlowPrj Docker MCP 环境 Makefile
# 用法: make <target>

DOCKER_COMPOSE = docker compose -f Docker/docker-compose.yml
CONTAINER_NAME = workflow-mcp-env

.PHONY: build up down restart logs shell clean dotnet-build python-run

# 构建镜像
build:
	$(DOCKER_COMPOSE) build

# 启动容器
up:
	$(DOCKER_COMPOSE) up -d

# 停止容器
down:
	$(DOCKER_COMPOSE) down

# 重启容器
restart: down up

# 查看日志
logs:
	docker logs -f $(CONTAINER_NAME)

# 进入容器 Shell
shell:
	docker exec -it $(CONTAINER_NAME) bash

# 编译 C# 项目
dotnet-build:
	docker exec $(CONTAINER_NAME) dotnet build

# 运行 Python 脚本
python-run:
	docker exec -it $(CONTAINER_NAME) python $(cmd)

# 清理
clean:
	$(DOCKER_COMPOSE) down -v
	docker rmi workflow-mcp-env:latest 2>/dev/null || true
	docker builder prune -f
