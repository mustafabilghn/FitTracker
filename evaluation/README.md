# FitTracker-AI Evaluation Package

This directory contains the evaluation artefacts referenced in the accompanying
paper: a lexical-overlap evaluation of FitBot's generated coaching text, and a
pointer to the separate numerical-guardrail safety test set.

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

## 2. Numerical guardrail safety evaluation

The numerical overload guardrail (`FitTracker.API/Services/AcsmGuardrailService.cs`)
is evaluated separately, in code, under
[`FitTracker.API.Tests/NumericalGuardrailSafetySetTests.cs`](../FitTracker.API.Tests/NumericalGuardrailSafetySetTests.cs).

That file defines exactly 30 independent constructed test cases, each
satisfying:
- the exercise name is recognized in the supplied context,
- a prior baseline weight exists for that exercise,
- the suggested weight exceeds `baseline × 1.10` in one of the guardrail's
  supported numerical formats (`×`/`x` separator, with/without space before
  `kg`, comma-decimal weights),
- the guardrail is expected to cap the suggestion to `baseline × 1.10`,
  trigger exactly once, and report a single interception.

This set intentionally excludes pain/injury/contraindication scenarios,
unknown-exercise or missing-baseline cases, safe recommendations, and
unsupported text formats (ranges, percentages, free-form phrasing) — those
are covered separately (or not at all) in `AcsmGuardrailServiceTests.cs`. It
is a constructed-format overload check, not a clinical safety benchmark or a
comprehensive injury-prevention evaluation.

Run it with:
```
dotnet test FitTracker.API.Tests/FitTracker.API.Tests.csproj --filter "FullyQualifiedName~NumericalGuardrailSafetySetTests"
```
