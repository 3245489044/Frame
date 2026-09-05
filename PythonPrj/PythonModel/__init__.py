# PythonModel - AI 模型调用层
#
# 模块结构：
#   clients/         模型客户端实现（DeepSeek 等）
#     base.py        BaseModelClient 抽象基类
#     deepseek.py    DeepSeekClient 实现
#   DeepSeekInvoke.py  兼容层（委托给 clients.deepseek）

from .clients import BaseModelClient, DeepSeekClient
from .DeepSeekInvoke import chat_complete, chat_complete_stream, set_api_key, reset_client

__all__ = [
    "BaseModelClient",
    "DeepSeekClient",
    "chat_complete",
    "chat_complete_stream",
    "set_api_key",
    "reset_client",
]
