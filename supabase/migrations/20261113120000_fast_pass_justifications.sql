-- The AI Engine used to store the fast-pass (Jev) result as a technical English note, e.g.
-- "Jev fast-pass noul=0.15 (skipped Claude)", and the app showed it to recruiters as is.
-- New evaluations store plain Spanish; this rewrites the ones already saved the same way.

update app.requirement_results rr
set justification = case
    when r.type = 'DISQUALIFYING' then format(
        'La lectura rápida encontró en la hoja de vida esta condición que descarta (certeza %s %%).',
        round(100 * (substring(rr.justification from 'noul=([0-9.]+)'))::numeric))
    else format(
        'La lectura rápida no encontró evidencia de este requisito obligatorio (certeza %s %%). Pide la evidencia en la entrevista si crees que sí lo cumple.',
        round(100 * (1 - (substring(rr.justification from 'noul=([0-9.]+)'))::numeric)))
end
from app.requirements r
where r.id = rr.requirement_id
  and rr.justification ~ '^Jev fast-pass noul=[0-9.]+ \(skipped Claude\)$';

update app.requirement_results
set justification = 'No se revisó en detalle: la lectura rápida ya encontró un requisito obligatorio sin cumplir.'
where justification = 'Jev fast-pass did not flag this requirement; Claude skipped.';
