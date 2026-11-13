"""Criba AI chat assistant: the API sends who is asking (audience), what they may see (context)
and the conversation so far; the engine answers the last user turn."""

from typing import Any, Literal

from pydantic import BaseModel, Field, field_validator


class ChatTurn(BaseModel):
    role: Literal["user", "assistant"]
    content: str = Field(min_length=1, max_length=2000)


class AssistantRequest(BaseModel):
    audience: Literal["company", "applicant"]
    context: dict[str, Any] = Field(default_factory=dict)
    messages: list[ChatTurn] = Field(min_length=1, max_length=20)

    @field_validator("messages")
    @classmethod
    def ends_with_user(cls, value: list[ChatTurn]) -> list[ChatTurn]:
        if value[-1].role != "user":
            raise ValueError("the last message must come from the user")
        return value


class AssistantResponse(BaseModel):
    reply: str
