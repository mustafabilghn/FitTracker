# FitTracker.Evaluation — FitBot NON-RAG baseline (production path)

Measures the **current production FitBot** through its real C# execution path, without RAG:

```
AiWorkoutCoachService (ChatAsync / GetInsightsAsync)
  → production DI registrations (AddGroqSemanticKernel, AddFitBotWorkoutPlugin, AddFitBotWorkoutPlanPlugin)
  → WorkoutAnalysisService (Completed-only history) + WorkoutRepository
  → Semantic Kernel function calling: WorkoutPlugin read tools, WorkoutPlan.GetPlannedWorkouts, SaveWorkoutPlan
  → SaveWorkoutPlan write lifecycle (validation, per-exercise ACSM guardrail, atomic save, deterministic reply)
  → AcsmGuardrailService (chat text) + sanitization
  → openai/gpt-oss-120b via Groq
```

It does **not** re-implement the prompt and does **not** change production code. The only differences from the
deployed API are: an isolated SQLite fixture database per case (instead of SQL Server), and two measurement-only hooks
(an HTTP recording handler on the Groq client and an innermost auto-function-invocation recording filter). Neither
alters requests, responses, results or termination. The HTTP/JWT/controller layer is not exercised; the controller only
resolves the user id and forwards to `AiWorkoutCoachService`.

> The legacy lexical evaluation in `evaluation/` (`evaluate_fitbot.py`, `llama-3.3-70b-versatile`, re-implemented
> prompt) is **not comparable** with this baseline and must not be mixed with it.

## Model

Pinned: `openai/gpt-oss-120b` (`EvaluationHost.Model`). The model actually sent and returned on the wire is recorded
per call, so a run can be checked against the configured model.

## Fixtures (`Harness/FixtureBuilder.cs`)

Fresh SQLite DB **per case**, deterministic relative to today (UTC), created with production rules:

- schema via `EnsureCreated`, then the real `UpdateSeeds` migration data operations → Turkish intensity levels
  (`Düşük/Orta/Yüksek`) with the stable seed ids (`IntensitySeedIds`), as in migrated production databases;
- **Completed** workouts via the production `IWorkoutRepository.CreateAsync` (the `POST /api/Workout` path);
- **Planned** workouts via the production `WorkoutPlanPlugin.SaveWorkoutPlan` (invoked directly, no LLM);
- primary user: Bench Press 90 → 100 kg (trend UP, baseline 100), Squat 120 kg for three weeks (plateau, baseline 120),
  plus Overhead Press / Romanian Deadlift; Planned "Pull" (+2 days) and "Upper" (+5 days, Bench Press 105 kg, which
  must never become a baseline);
- a second user (Hack Squat / "B Secret Plan") for cross-user leakage checks.

`--fixtures-only` seeds a fixture and prints the Completed/Planned view without any LLM call.

## Scenarios (`Scenarios/ScenarioCatalog.cs`)

20 cases in four groups: personal data / SQL tools (incl. planned workouts and the JSON insights path), general
knowledge, mixed, and write actions (safe saves, guardrail rejections, a planned-weight-must-not-be-baseline case and a
"do not save" case). The smoke suite is exactly:
`sql_planned`, `know_deload`, `mixed_plateau_strategy`, `write_safe_push`, `write_unsafe_bench`.

## Recorded per case (`evaluation/runs/<run>/cases.jsonl`)

Scenario id/category/prompt, full final response, configured + observed model ids, git SHA and dirty flags (repo and
production source), timestamp, generation settings actually sent (temperature, response_format, tool_choice, tools
offered, …), every LLM call (status, latency, tool calls with arguments and argument keys, token usage,
rate-limit headers, redacted error), server-side tool executions (arguments, result summary), the SaveWorkoutPlan
outcome, guardrail result, DB before/after summary and created planned workouts, and all validation checks.
API keys, `Authorization` headers and Groq organisation ids are never recorded.

## Metrics (`summary.json`, `report.md`)

Primary: task success, correct tool selection, tool execution success, SaveWorkoutPlan success/rejection correctness,
guardrail correctness, false save claims, cross-user leakage, model-sent identity arguments, second LLM completion
after a write, error rate, 429 rate, average latency / LLM calls / tokens.
Secondary (exploratory): Turkish language check, forbidden-pattern residue after sanitization, JSON validity for the
insights path, BLEU-4 / ROUGE-L against new production-compatible references (legacy references are not used).

Unsafe-plan scenarios also record *how* the plan was blocked (`UnsafeWriteHandling`):
`server_guardrail_rejection` (SaveWorkoutPlan called, rejected by the server-side per-exercise guardrail),
`model_pre_tool_refusal` (the model declined without calling SaveWorkoutPlan), `unsafe_plan_persisted` or `other`.
This is reported separately ("Unsafe plans blocked (any path)") and does not count as guardrail correctness, which
requires the server-side path.

Notes on interpretation: personal data for recent workouts / trends / plateau is already injected into the system
prompt, so those read tools are *acceptable* rather than *required*; planned workouts are not in the context, so
`GetPlannedWorkouts` is required. Content checks are coarse keyword heuristics, not quality judgements.

## Running

```bash
dotnet run --project FitTracker.Evaluation -- --list
dotnet run --project FitTracker.Evaluation -- --fixtures-only
dotnet run --project FitTracker.Evaluation -- --suite smoke
```

API key: `GROQ_API_KEY`, otherwise `Groq:ApiKey` from the FitTracker.API user-secrets. No retries are performed:
a 429 is recorded as an error. Between cases the runner waits for the token window when the remaining TPM budget is
low (Groq free plan: 8K TPM / 200K TPD). A per-case cap (default 6 LLM requests) aborts unexpected tool loops.
