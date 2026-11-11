"""OpenAPI tweaks for Scalar (internal API key on /v1/* routes)."""

from fastapi import FastAPI
from fastapi.openapi.utils import get_openapi

_INTERNAL_KEY_SCHEME = "InternalApiKey"


def configure_openapi(app: FastAPI) -> None:
    def custom_openapi() -> dict:
        if app.openapi_schema:
            return app.openapi_schema

        schema = get_openapi(
            title=app.title,
            version=app.version,
            description=app.description,
            routes=app.routes,
        )
        components = schema.setdefault("components", {})
        security_schemes = components.setdefault("securitySchemes", {})
        security_schemes[_INTERNAL_KEY_SCHEME] = {
            "type": "apiKey",
            "in": "header",
            "name": "X-Internal-Api-Key",
            "description": "Shared secret with the .NET API (see ENGINE_API_KEY).",
        }

        for path, path_item in schema.get("paths", {}).items():
            if not path.startswith("/v1"):
                continue
            for operation in path_item.values():
                if isinstance(operation, dict):
                    operation["security"] = [{_INTERNAL_KEY_SCHEME: []}]

        app.openapi_schema = schema
        return schema

    app.openapi = custom_openapi  # type: ignore[method-assign]
