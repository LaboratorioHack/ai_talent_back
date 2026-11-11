# ruff: noqa: B008 — FastAPI multipart defaults require Form()/File() in signatures
import uuid

from fastapi import APIRouter, File, Form, HTTPException, UploadFile

from ai_engine.schemas.cv import CvIngestRequest, CvIngestResponse
from ai_engine.services.ingest_service import IngestService

router = APIRouter(prefix="/v1/cvs", tags=["cvs"])


@router.post("/ingest", response_model=CvIngestResponse)
async def ingest_cv(  # noqa: B008
    resume_id: uuid.UUID = Form(...),
    file: UploadFile | None = File(default=None),
    from_storage: bool = Form(default=False),
) -> CvIngestResponse:
    service = IngestService()
    try:
        if from_storage or file is None:
            return await service.ingest_from_storage(resume_id)
        return await service.ingest_upload(resume_id, file.file)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except RuntimeError as exc:
        raise HTTPException(status_code=502, detail=str(exc)) from exc


@router.post("/ingest-json", response_model=CvIngestResponse)
async def ingest_cv_json(body: CvIngestRequest) -> CvIngestResponse:
    service = IngestService()
    try:
        return await service.ingest_from_storage(body.resume_id)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
