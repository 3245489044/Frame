# -*- coding: utf-8 -*-
print("=" * 44)
print("  RunCode 测试 - PY 文件执行成功！")
print("=" * 44)
print()
import datetime
import os
import sys
print(f" 当前时间: {datetime.datetime.now()}")
print(f" 当前目录: {os.getcwd()}")
print(f" 脚本路径: {os.path.abspath(__file__)}")
print(f" 脚本目录: {os.path.dirname(os.path.abspath(__file__))}")
print()
print(" [信息] 这个文件是通过 Code Runner 插件")
print(" [信息] 在 VS Code 文件资源管理器中右键执行的。")
print()
print(" [信息] 如果看到这个输出，说明 RunCode 插件")
print(" [信息] 的 py 文件执行功能正常！")
print()
print("=" * 44)
try:
    input("按 Enter 键退出...")
except EOFError:
    pass
