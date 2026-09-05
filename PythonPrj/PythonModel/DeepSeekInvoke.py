"""
DeepSeekInvoke — 兼容层

保留原有模块级函数接口，内部委托给 clients.deepseek.DeepSeekClient。
新代码应直接使用 `from PythonModel.clients import DeepSeekClient`。
"""

from .clients.deepseek import DeepSeekClient as _DeepSeekClient

_client = _DeepSeekClient()


def chat_complete(system_prompt: str, user_input: str) -> str:
    """同步对话（兼容接口）。"""
    return _client.chat(system_prompt, user_input)


def chat_complete_stream(system_prompt: str, user_input: str):
    """流式对话（兼容接口）。"""
    yield from _client.chat_stream(system_prompt, user_input)


def set_api_key(api_key: str):
    """设置 API Key（兼容接口）。"""
    _client.set_api_key(api_key)


def reset_client():
    """重置客户端（兼容接口）。"""
    _client._reset_client()


if __name__ == "__main__":
    system_prompt = input()
    while True:
        user_input = input()
        if user_input.lower() in ("exit", "quit"):
            break
        try:
            result = chat_complete(system_prompt, user_input)
            print(result)
        except Exception as e:
            print(f"Error: {e}")
