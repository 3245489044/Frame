import os
from openai import OpenAI
from typing import Generator

from .base import BaseModelClient


class DeepSeekClient(BaseModelClient):
    """DeepSeek 模型客户端。

    封装 OpenAI SDK，连接 DeepSeek API (https://api.deepseek.com)。
    支持同步 / 流式对话，以及动态设置 API Key。
    """

    def __init__(self, api_key: str | None = None):
        self._api_key_override = api_key
        self._client: OpenAI | None = None

    # ── 内部方法 ────────────────────────────────────────────

    def _get_client(self) -> OpenAI:
        if self._client is not None:
            return self._client

        api_key = self._api_key_override or os.environ.get("DeepSeekAPI")
        if not api_key:
            raise ValueError(
                "DeepSeek API key not found. "
                "Set DeepSeekAPI environment variable or pass api_key to constructor."
            )
        self._client = OpenAI(api_key=api_key, base_url="https://api.deepseek.com")
        return self._client

    def _reset_client(self) -> None:
        if self._client is not None:
            self._client.close()
            self._client = None

    # ── 公开接口 ────────────────────────────────────────────

    def chat(self, system_prompt: str, user_input: str) -> str:
        client = self._get_client()
        response = client.chat.completions.create(
            model="deepseek-chat",
            messages=[
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_input},
            ],
            stream=False,
        )
        return response.choices[0].message.content

    def chat_stream(self, system_prompt: str, user_input: str) -> Generator[str, None, None]:
        client = self._get_client()
        response = client.chat.completions.create(
            model="deepseek-chat",
            messages=[
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_input},
            ],
            stream=True,
        )
        for chunk in response:
            delta = chunk.choices[0].delta
            if delta and delta.content:
                yield delta.content

    def set_api_key(self, api_key: str) -> None:
        self._api_key_override = api_key
        self._reset_client()
