from unittest.mock import AsyncMock, MagicMock

import pytest

from ai_engine.agents.profile_assistant.agent import ProfileAssistantAgent
from ai_engine.schemas.role_profile import (
    Equivalence,
    ExistingRequirement,
    ProposedRequirement,
    ProposeRequirementsRequest,
    ProposeRequirementsResponse,
    SuggestEquivalencesRequest,
    SuggestEquivalencesResponse,
)
from ai_engine.services.role_profile_service import (
    RoleProfileService,
    fit_weights,
    weight_budget,
)

_DESCRIPTION = (
    "Buscamos un desarrollador de videojuegos con Unity y C# para un estudio que publica "
    "juegos móviles; trabajará con diseño y arte."
)


def _requirement(description: str, type_: str = "MANDATORY", weight: int = 10, eq=None):
    return ProposedRequirement(
        description=description, type=type_, weight=weight, equivalences=eq or []
    )


def test_weight_budget_ignores_disqualifying() -> None:
    existing = [
        ExistingRequirement(description="Unity", weight=30),
        ExistingRequirement(description="C#", type="DESIRABLE", weight=20),
        ExistingRequirement(description="Sin licencia", type="DISQUALIFYING", weight=50),
    ]
    assert weight_budget(existing) == 50
    assert weight_budget([ExistingRequirement(description="Todo", weight=100)]) == 0


def test_fit_weights_adds_up_to_budget_and_skips_disqualifying() -> None:
    requirements = [
        _requirement("Unity", weight=40),
        _requirement("C#", weight=40),
        _requirement("Shaders", "DESIRABLE", weight=20),
        _requirement("Sin portafolio", "DISQUALIFYING", weight=0),
    ]
    fit_weights(requirements, 70)
    assert sum(r.weight for r in requirements) == 70
    assert requirements[0].weight == requirements[1].weight
    assert requirements[3].weight == 0


@pytest.mark.asyncio
async def test_propose_drops_existing_and_duplicates_and_cleans_equivalences() -> None:
    agent = MagicMock(spec=ProfileAssistantAgent)
    agent.propose = AsyncMock(
        return_value=ProposeRequirementsResponse(
            requirements=[
                _requirement("unity", weight=50),
                _requirement(
                    "C#  avanzado",
                    weight=30,
                    eq=[
                        Equivalence(label="C++", percent=70),
                        Equivalence(label="c++", percent=60),
                        Equivalence(label="Java", percent=150),
                        Equivalence(label=" ", percent=50),
                    ],
                ),
                _requirement("C# avanzado", weight=20),
                _requirement("Inglés B2", "DESIRABLE", weight=20),
            ]
        )
    )
    request = ProposeRequirementsRequest(
        description=_DESCRIPTION,
        existing=[ExistingRequirement(description="Unity", weight=40)],
    )

    result = await RoleProfileService(agent).propose(request)

    assert [r.description for r in result.requirements] == ["C# avanzado", "Inglés B2"]
    assert sum(r.weight for r in result.requirements) == 60
    assert [(e.label, e.percent) for e in result.requirements[0].equivalences] == [
        ("C++", 70),
        ("Java", 100),
    ]
    agent.propose.assert_awaited_once_with(request, 60)


@pytest.mark.asyncio
async def test_suggest_equivalences_skips_existing_and_requirement_itself() -> None:
    agent = MagicMock(spec=ProfileAssistantAgent)
    agent.suggest_equivalences = AsyncMock(
        return_value=SuggestEquivalencesResponse(
            equivalences=[
                Equivalence(label="Unity", percent=90),
                Equivalence(label="Unreal Engine", percent=70),
                Equivalence(label="godot", percent=60),
            ]
        )
    )
    request = SuggestEquivalencesRequest(requirement="Unity", existing=["Godot"])

    result = await RoleProfileService(agent).suggest_equivalences(request)

    assert [(e.label, e.percent) for e in result.equivalences] == [("Unreal Engine", 70)]


@pytest.mark.asyncio
async def test_agent_sends_budget_and_existing_and_forbids_protected_attributes() -> None:
    llm = MagicMock()
    llm.complete_json = AsyncMock(return_value=ProposeRequirementsResponse(requirements=[]))
    request = ProposeRequirementsRequest(
        description=_DESCRIPTION,
        title="Game Developer",
        existing=[ExistingRequirement(description="Unity", weight=10)],
    )

    await ProfileAssistantAgent(llm=llm).propose(request, 90)

    system, user, model_type = llm.complete_json.await_args.args
    assert "protected attributes" in system
    assert '"weight_budget": 90' in user
    assert "Game Developer" in user
    assert '"Unity"' in user
    assert model_type is ProposeRequirementsResponse
    assert llm.complete_json.await_args.kwargs["tool_name"] == "propose_requirements"
