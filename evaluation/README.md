# FitTracker-AI Evaluation Package

This directory contains the evaluation artefacts referenced in the accompanying
paper: a lexical-overlap evaluation of FitBot's generated coaching text, and
numerical-guardrail safety and scope-preservation tests.

## 1. Lexical-overlap evaluation (`evaluate_fitbot.py`, `reference_plans.py`)

**What this measures.** FitBot is prompted with 20 constructed scenarios (5
each across 4 goal categories: strength, hypertrophy, weight_loss, endurance).
Each scenario pairs a goal with a deterministically-seeded mock workout
history (`_mock_workouts` in `evaluate_fitbot.py`). The model's generated
coaching report is compared against a hand-written constructed reference
response for the same scenario using corpus BLEU-4 and per-plan/average
ROUGE-L.

**Scenarios and references (`reference_plans.py`).** The 20 entries are
constructed scenarios with corresponding reference responses, written to
exercise FitBot's four coaching-goal categories with plausible training
histories and plateaus. They are not authored or validated by a certified
trainer, and this is not a clinical or professional coaching benchmark.

**Model configuration.** `llama-3.3-70b-versatile` via the Groq API,
`temperature = 0.3`, `max_tokens = 800`. Configured in `evaluate_fitbot.py`.

**Interpreting BLEU-4 / ROUGE-L.** These are reported as exploratory
lexical-overlap indicators only. Open-ended coaching text can be correct,
safe, and useful while using entirely different wording from any single
reference response, so no acceptance threshold is defined for these scores —
low overlap does not by itself indicate poor coaching quality, and neither
metric is used as a pass/fail gate.

**Results.** `evaluation_results.json` contains the actual output of running
`evaluate_fitbot.py` against the 20 scenarios above: corpus BLEU-4, average
ROUGE-L, per-goal-category breakdowns, and per-plan scores with response
previews.

**Reproducing a run.**
```
pip install groq nltk rouge-score
# set GROQ_API_KEY in evaluate_fitbot.py
python evaluate_fitbot.py
```

## 2. Numerical guardrail evaluation

The numerical load guardrail (`FitTracker.API/Services/AcsmGuardrailService.cs`)
performs automatic intervention only for the recognized structured
recommendation format:

```
ExerciseName: N set × M tekrar @ W kg
```

The structured scope supports `×`/`x`, optional spacing before `kg`, and
comma or dot decimals. Free-form conversational text and historical numeric
statements are intentionally preserved rather than automatically rewritten.
This scope prevents the guardrail from interpreting historical loads, body
weight, dates, calories, or other numeric text as a load recommendation.

### Unsafe recognized-format cases

`FitTracker.API.Tests/NumericalGuardrailSafetySetTests.cs` defines 30
independent constructed cases. Each case has a recognized exercise name, an
available prior baseline, and a structured load recommendation above
`baseline × 1.10`. The expected behavior is one interception and replacement
with the configured maximum progression.

### Scope-preservation cases

`FitTracker.API.Tests/GuardrailScopePreservationTests.cs` defines 10
constructed non-recommendation or out-of-scope numeric cases. They cover
historical recorded loads, body weight, free-form recommendations, calories,
dates, repetition counts, and an unsupported `lbs` unit. The expected behavior
is no intervention and exact preservation of the original text.

Additional unit checks in `AcsmGuardrailServiceTests.cs` cover the 10% boundary,
safe structured recommendations, mixed safe/unsafe structured multi-exercise
responses, missing baselines, and supported formatting variants.

This is a deterministic, constructed-format software test suite. It is not a
clinical safety benchmark, does not estimate real-world injury prevention, and
does not cover pain, contraindications, inconsistent exercise naming, or all
natural-language formats.

**Run all guardrail tests.**
```
dotnet test FitTracker.API.Tests/FitTracker.API.Tests.csproj --filter "FullyQualifiedName~Guardrail"
```
