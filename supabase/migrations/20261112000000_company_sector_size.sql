-- Onboarding captures the organization's sector and size (MVP section 4.1).
-- Nullable so companies created before this migration stay valid.

alter table app.companies
  add column sector text,
  add column size text;

alter table app.companies
  add constraint companies_sector_check
  check (sector is null or char_length(sector) between 2 and 80);

alter table app.companies
  add constraint companies_size_check
  check (size is null or size in ('1-10', '11-50', '51-200', '201-500', '501-1000', '1000+'));
