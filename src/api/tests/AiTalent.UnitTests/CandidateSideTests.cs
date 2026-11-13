using AiTalent.Application.Abstractions;
using AiTalent.Application.Features.Interviews;
using AiTalent.Application.Features.Portal;

namespace AiTalent.UnitTests;

public class CandidateSideTests
{
    private static readonly RequirementGapRow[] Requirements =
    [
        new("Kubernetes en producción", "Plataforma", "MANDATORY", "MEETS"),
        new("Inglés B2", "Idiomas", "MANDATORY", "PARTIALLY_MEETS"),
        new("Terraform", null, "DESIRABLE", "NO_EVIDENCE"),
        new("Azure", "Cloud", "MANDATORY", "DOES_NOT_MEET"),
    ];

    [Fact]
    public void BuildQuestions_AsksFirstWhatTheEvaluationCouldNotConfirm()
    {
        var questions = CandidateInterviewService.BuildQuestions(Requirements, "es");

        Assert.Equal(4, questions.Count);
        Assert.Equal("Idiomas", questions[0].Topic);
        Assert.Contains("no del todo", questions[0].Question, StringComparison.Ordinal);
        Assert.Equal("Terraform", questions[1].Topic);
        Assert.Contains("no menciona", questions[1].Question, StringComparison.Ordinal);
        Assert.Equal("Plataforma", questions[3].Topic);
        Assert.DoesNotContain(questions, question => question.Question.StartsWith("In English", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildQuestions_SpanishWithEnglish_AsksTheLastTwoInEnglish()
    {
        var questions = CandidateInterviewService.BuildQuestions(Requirements, "es-en");

        Assert.Equal(2, questions.Count(question => question.Question.StartsWith("In English", StringComparison.Ordinal)));
        Assert.StartsWith("In English", questions[^1].Question, StringComparison.Ordinal);
        Assert.False(questions[0].Question.StartsWith("In English", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildQuestions_CapsTheInterview()
    {
        var many = Enumerable.Range(1, 10).Select(i => new RequirementGapRow($"Requisito {i}", null, "MANDATORY", "MEETS")).ToList();

        Assert.Equal(CandidateInterviewService.MaxQuestions, CandidateInterviewService.BuildQuestions(many, "es").Count);
    }

    [Fact]
    public void MatchOf_UsesWholeWordsOfTheCvSkills()
    {
        var job = new[]
        {
            new PublicJobRequirementRow(Guid.NewGuid(), "MANDATORY", "Kubernetes en producción", []),
            new PublicJobRequirementRow(Guid.NewGuid(), "MANDATORY", "AWS o Azure", ["GCP"]),
            new PublicJobRequirementRow(Guid.NewGuid(), "DESIRABLE", "Terraform", []),
        };

        var high = PortalService.MatchOf(["Kubernetes", "Azure", "Terraform", "7 años de experiencia"], job);
        var none = PortalService.MatchOf(["Go"], job);

        Assert.Equal("alta", high.Level);
        Assert.Equal(["Kubernetes", "Azure", "Terraform"], high.Reasons);
        Assert.Equal("baja", none.Level);
        Assert.Empty(none.Reasons);
    }

    [Fact]
    public void ResumeDocBuilder_TurnsTheExtractionIntoACv()
    {
        const string json = """
            {
              "summary": "Ingeniera de datos con 5 años.",
              "skills": ["SQL", "Python", "sql"],
              "languages": [{"language": "Inglés", "level": "B2"}],
              "certifications": ["AWS Data Analytics"],
              "total_experience_months": 62,
              "education": [{"degree": "Ingeniería de Sistemas", "field": null}],
              "work_experience": [
                {"title": "Analista de datos", "company": "Company Name", "start_date": "2021", "end_date": "Current",
                 "highlights": ["Tableros de cartera", "Modelos en dbt"], "technologies": ["dbt"]}
              ]
            }
            """;

        var doc = ResumeDocBuilder.Build("Laura Gómez", ["laura@example.com"], json);
        var skills = ResumeDocBuilder.Skills(json);

        Assert.Equal("Analista de datos", doc.Headline);
        Assert.Equal(["Perfil", "Experiencia", "Educación", "Certificaciones", "Idiomas", "Habilidades"], doc.Sections.Select(section => section.Title));
        var role = doc.Sections[1].Lines[0];
        Assert.Equal("Analista de datos", role.Text);
        Assert.Equal("2021 – hoy", role.Right);
        Assert.Equal("5 años de experiencia", skills[0]);
        Assert.Single(skills, skill => skill.Equals("sql", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Inglés B2", skills);
        Assert.Contains("dbt", skills);
    }

    [Fact]
    public void ResumeDocBuilder_WithoutExtraction_SaysItIsStillBeingRead()
    {
        var doc = ResumeDocBuilder.Build("Laura Gómez", [], null);

        Assert.Single(doc.Sections);
        Assert.Contains("leyendo", doc.Sections[0].Lines[0].Text, StringComparison.Ordinal);
    }
}
