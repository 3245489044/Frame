from abc import ABC, abstractmethod
from typing import Generator


class BaseModelClient(ABC):
    """AI 模型客户端抽象基类。

    所有模型客户端（DeepSeek、GPT、通义千问等）应继承此类，
    实现统一的 chat / chat_stream 接口。
    """

    @abstractmethod
    def chat(self, system_prompt: str, user_input: str) -> str:
        """同步对话：发送 system_prompt + user_input，返回完整回复。"""
        ...

    @abstractmethod
    def chat_stream(self, system_prompt: str, user_input: str) -> Generator[str, None, None]:
        """流式对话：逐 chunk 产出回复内容。"""
        ...

    @abstractmethod
    def set_api_key(self, api_key: str) -> None:
        """动态设置 API Key。"""
        ...
