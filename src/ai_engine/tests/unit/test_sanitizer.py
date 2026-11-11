from ai_engine.anonymization.sanitizer import anonymize_cv_text


def test_redacts_email_phone_and_name_line() -> None:
    text = "Jane Doe\njane@corp.com\n+57 300 123 4567\nExperience: APIs"
    redacted = anonymize_cv_text(text)
    assert "[EMAIL]" in redacted
    assert "[PHONE]" in redacted
    assert "[CANDIDATE]" in redacted
    assert "jane@corp.com" not in redacted
