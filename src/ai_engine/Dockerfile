FROM python:3.15-slim
COPY --from=ghcr.io/astral-sh/uv:0.12 /uv /bin/uv
WORKDIR /app
ENV UV_COMPILE_BYTECODE=1 UV_LINK_MODE=copy PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
COPY pyproject.toml uv.lock ./
RUN uv sync --frozen --no-dev --no-install-project
COPY README.md ./
COPY src ./src
RUN uv sync --frozen --no-dev
ENV PATH="/app/.venv/bin:$PATH"
# OWASP A02: never run as root
RUN useradd --system --uid 10001 --no-create-home engine
USER engine
EXPOSE 8000
CMD ["uvicorn", "ai_engine.api.main:app", "--host", "0.0.0.0", "--port", "8000", "--no-server-header"]
