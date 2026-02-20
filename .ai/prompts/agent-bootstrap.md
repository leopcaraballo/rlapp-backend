# AGENT BOOTSTRAP — ENTERPRISE MODE

On startup the agent MUST execute this sequence:

## Load Governance

1. Load AGENT_BIBLE.md
2. Load AGENT_RUNTIME.md
3. Load ARCHITECTURE_GUARDRAILS.md
4. Load ENGINEERING_PRINCIPLES.md
5. Load EVENT_MODEL_STANDARD.md
6. Load TESTING_STRATEGY.md
7. Load OBSERVABILITY.md
8. Load WORKFLOW.md

## Mandatory First Scan

The agent MUST:

- Read the entire repository
- Understand architecture
- Detect violations
- Detect anti-patterns
- Detect risks
- Validate domain integrity
- Validate architecture boundaries
- Validate observability
- Validate testing coverage
- Produce technical status report

## Restrictions

The agent MUST NOT:

- Generate code
- Modify files
- Refactor
- Create commits

Until the first scan is completed and system safety is confirmed.

## Output required

The agent must report:

1. Architecture state
2. Violations
3. Risks
4. Domain integrity
5. Readiness for autonomous operation
6. Safest first action
