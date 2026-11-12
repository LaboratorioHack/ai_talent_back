from ai_engine.anonymization.sanitizer import anonymize_cv_text, anonymize_cv_text_with_report


def test_redacts_email_phone_and_name_line() -> None:
    text = "Jane Doe\njane@corp.com\n+57 300 123 4567\nExperience: APIs"
    redacted, report = anonymize_cv_text_with_report(text)
    assert "[EMAIL]" in redacted
    assert "[PHONE]" in redacted
    assert "[CANDIDATE]" in redacted
    assert "jane@corp.com" not in redacted
    assert report.names_removed == 1
    assert report.contact_info_removed == 2
    assert report.gender_indicators_detected == 0
    assert anonymize_cv_text(text) == redacted


def test_counts_age_dob_and_address() -> None:
    text = (
        "Senior software engineer with distributed systems\n"
        "Experienced backend developer building APIs and services\n"
        "Python specialist with cloud platform experience\n"
        "Age: 42\n"
        "Fecha de nacimiento: 01/02/03\n"
        "Address: Fake Street\n"
        "Built APIs"
    )
    redacted, report = anonymize_cv_text_with_report(text)
    assert "[AGE]" in redacted
    assert "[DOB]" in redacted
    assert "[ADDRESS]" in redacted
    assert "42" not in redacted
    assert "Fake Street" not in redacted
    assert report.age_or_dob_indicators_removed == 2
    assert report.location_indicators_removed == 1
    assert report.names_removed == 0
