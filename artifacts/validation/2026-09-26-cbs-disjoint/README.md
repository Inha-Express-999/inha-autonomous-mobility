# Disjoint CBS comparison — 2026-09-26

Uncommitted work after v0.4.0.0. Own token-based disjoint CBS implementation; all
plans remain executable=false. See Docs/cbs_coordination.md for source reference,
model, tradeoffs and integration gaps.

- strict-500.json: five repeats of priority, standard CBS, disjoint CBS on each
  original case with CT=500 / LL=50,000. Both CBS variants exceed CT for both
  full-horizon cases. The short-horizon case is unsuccessful for all algorithms.
- comparison-2000.json: the same cases, five repeats, both CBS variants receive
  CT=2,000 / LL=50,000. Disjoint CBS succeeds 5/5 per full-horizon case at CT=666;
  standard CBS exceeds CT 5/5. Priority succeeds 5/5. Short horizon remains infeasible.
- source-hashes.json: final algorithm, evaluation, test, graph and CT=2,000 config
  source. The exact earlier CT=500 configuration is embedded in strict-500.json.
  The last test-only change adds disjoint coverage to existing budget assertions;
  benchmark algorithm source did not change after the recorded runs.

Evaluation was invoked with compare_coordination(config, repetitions=5), the same
function used by campus-sim coordination-compare. The config was changed from
CT=500 to CT=2,000 between runs; all other scenario/model/LL parameters are equal.
Neither file is a real-time guarantee: five samples on a development PC, with
part of the run overlapping Python regression tests. Preprocessing time is separate.
Physical collisions are unmeasured (null); only planned token conflicts are tested.

Validation actually run:
- python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache:
  397 passed in 17.61s, one existing Starlette/httpx deprecation warning.
- After adding two disjoint budget test instances only:
  python -m pytest backend/tests/test_coordination.py -q -o cache_dir=tmp/pytest-cache:
  40 passed in 1.31s. This includes 160 independent two-agent start/goal cases
  inside one matrix test, plus multi-tick and three-job shared-resource bounds.
- python -m ruff check backend AgentScripts/MapData AgentScripts/UnityPhysicsIntegration: passed.
- git diff --check: passed.
- Unity was not rerun; these changes do not connect the new offline plans to control.

M5/T25/E4 remain incomplete pending live delay/replan/safety and physical comparison.
