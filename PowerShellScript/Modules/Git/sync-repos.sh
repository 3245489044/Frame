#!/bin/bash

# Git双平台同步脚本
# 功能：同时推送到GitHub和Gitee

set -e

GITHUB_REMOTE="github"
GITEE_REMOTE="gitee"
COMMIT_MESSAGE=""
TARGET_BRANCH="main"

show_usage() {
    cat << EOF
用法: $0 [选项] [提交信息]

选项:
    -b, --branch <分支名>    指定目标分支 (默认: main)
    -h, --help              显示帮助信息

参数:
    [提交信息]              可选，指定提交信息。如果不提供，将进入交互式输入模式。

示例:
    $0 "更新README文档"
    $0 -b main "修复bug"
    $0 --branch main
EOF
}

while [[ $# -gt 0 ]]; do
    case $1 in
        -b|--branch)
            TARGET_BRANCH="$2"
            shift 2
            ;;
        -h|--help)
            show_usage
            exit 0
            ;;
        *)
            COMMIT_MESSAGE="$1"
            shift
            ;;
    esac
done

check_git_repo() {
    if ! git rev-parse --git-dir > /dev/null 2>&1; then
        echo "错误: 当前目录不是Git仓库"
        exit 1
    fi
}

init_remotes() {
    echo "正在检查远程仓库配置..."
    
    if ! git remote | grep -q "^${GITHUB_REMOTE}$"; then
        echo "请输入GitHub仓库URL (例如: https://github.com/username/repo.git):"
        read -r github_url
        if [ -n "$github_url" ]; then
            git remote add "$GITHUB_REMOTE" "$github_url"
            echo "已添加GitHub远程仓库"
        fi
    fi
    
    if ! git remote | grep -q "^${GITEE_REMOTE}$"; then
        echo "请输入Gitee仓库URL (例如: https://gitee.com/username/repo.git):"
        read -r gitee_url
        if [ -n "$gitee_url" ]; then
            git remote add "$GITEE_REMOTE" "$gitee_url"
            echo "已添加Gitee远程仓库"
        fi
    fi
}

get_commit_message() {
    if [ -z "$COMMIT_MESSAGE" ]; then
        echo "请输入提交信息 (Ctrl+C 取消):"
        read -r COMMIT_MESSAGE
    fi
    
    if [ -z "$COMMIT_MESSAGE" ]; then
        echo "错误: 提交信息不能为空"
        exit 1
    fi
}

sync_to_remote() {
    local remote_name=$1
    echo ""
    echo ">>> 正在推送到 $remote_name 的 $TARGET_BRANCH 分支..."
    
    if git push "$remote_name" "$TARGET_BRANCH" --tags 2>&1; then
        echo "✓ $remote_name 推送成功"
        return 0
    else
        echo "✗ $remote_name 推送失败"
        return 1
    fi
}

main() {
    echo "=========================================="
    echo "   GitHub & Gitee 同步推送工具"
    echo "=========================================="
    echo ""
    
    check_git_repo
    
    current_branch=$(git branch --show-current)
    if [ -z "$current_branch" ]; then
        current_branch="master"
    fi
    echo "目标分支: $TARGET_BRANCH"
    echo "当前分支: $current_branch"
    
    if [ "$current_branch" != "$TARGET_BRANCH" ]; then
        echo ""
        echo "正在切换到 $TARGET_BRANCH 分支..."
        if ! git checkout "$TARGET_BRANCH" 2>/dev/null; then
            echo "分支 $TARGET_BRANCH 不存在，是否创建并切换? (y/n)"
            read -r confirm
            if [ "$confirm" = "y" ] || [ "$confirm" = "Y" ]; then
                git checkout -b "$TARGET_BRANCH"
                echo "已创建并切换到新分支: $TARGET_BRANCH"
            else
                echo "取消操作"
                exit 1
            fi
        else
            echo "已切换到 $TARGET_BRANCH 分支"
        fi
    fi
    
    git remote | grep -q "^${GITHUB_REMOTE}$" || git remote | grep -q "^${GITEE_REMOTE}$"
    if [ $? -ne 0 ]; then
        init_remotes
    fi
    
    echo ""
    echo "检查文件状态..."
    if git diff --quiet && git diff --cached --quiet; then
        echo "没有检测到更改"
    else
        echo "检测到未提交的更改:"
        git status --short
        echo ""
        echo "是否添加所有更改并提交? (y/n)"
        read -r confirm
        if [ "$confirm" = "y" ] || [ "$confirm" = "Y" ]; then
            git add -A
        else
            echo "取消操作"
            exit 0
        fi
    fi
    
    get_commit_message
    echo ""
    echo "提交信息: $COMMIT_MESSAGE"
    echo ""
    
    if git diff --cached --quiet; then
        echo "没有暂存的更改，跳过提交"
    else
        git commit -m "$COMMIT_MESSAGE"
        echo "✓ 提交成功"
    fi
    
    failed=0
    
    if git remote | grep -q "^${GITHUB_REMOTE}$"; then
        sync_to_remote "$GITHUB_REMOTE" || ((failed++))
    else
        echo "⚠ 未配置GitHub远程仓库"
    fi
    
    if git remote | grep -q "^${GITEE_REMOTE}$"; then
        sync_to_remote "$GITEE_REMOTE" || ((failed++))
    else
        echo "⚠ 未配置Gitee远程仓库"
    fi
    
    echo ""
    echo "=========================================="
    if [ $failed -eq 0 ]; then
        echo "✓ 所有远程仓库推送完成!"
    else
        echo "⚠ 有 $failed 个远程仓库推送失败"
        exit 1
    fi
    echo "=========================================="
}

if [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
    show_usage
    exit 0
fi

main
