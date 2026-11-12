.PHONY: dev test-engine seed-resumes
dev:
	uv run dev
test-engine:
	uv run test-engine
seed-resumes:
	uv run seed-resumes
