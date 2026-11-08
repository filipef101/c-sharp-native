# Poteto log

## Plugin

The pstack plugin was already on disk. It was not installed again, and `setup-pstack` was not run.

Cache path. `/home/ubuntu/.cursor/plugins/cache/cursor-public/9717366/ae5650febf4320793844c12c4adaa937f417f8da`

Local manifest. `.cursor-plugin/plugin.json` in that directory, name `pstack`, version `0.15.36`.

Upstream check. `https://raw.githubusercontent.com/cursor/plugins/main/pstack/.cursor-plugin/plugin.json` was fetched this session and also says version `0.15.36`.

Homepage recorded in the manifest. `https://github.com/cursor/plugins/tree/main/pstack`

## Model limit

Every role was required to be Grok 4.7 with fast off. The Task tool's listed Grok slugs are `grok-4.7-low`, `grok-4.7-medium`, `grok-4.7-high`, and `grok-4.7-xhigh`, each also offered with `-fast`. There is no slug that is only `grok-4.7`. `inherit` was forbidden because it does not name the model. The plugin defaults, `grok-4.7-xhigh-fast` for code and explorers and `claude-opus-5-5-xhigh` for explainers and judgment, were forbidden.

No subagent was spawned. How, architect, and the proof checks ran in this chat. That chat is Grok 4.7 with fast off. Independent checks in the same model replaced a second model. They are listed below.

## Skills read

| Skill | Path |
| --- | --- |
| poteto-mode | `skills/poteto-mode/SKILL.md` under the cache path above |
| Investigation | `skills/poteto-mode/playbooks/investigation.md` |
| Prototype | `skills/poteto-mode/playbooks/prototype.md` |
| how | `skills/how/SKILL.md` and `skills/how/references/explainer-prompt.md` |
| architect | `skills/architect/SKILL.md`, `skills/architect/references/design-red-flags.md`, `skills/architect/references/rationale-template.md` |
| principle-prove-it-works | `skills/principle-prove-it-works/SKILL.md` |
| principle-laziness-protocol | `skills/principle-laziness-protocol/SKILL.md` |
| principle-model-the-domain | `skills/principle-model-the-domain/SKILL.md` |
| principle-boundary-discipline | `skills/principle-boundary-discipline/SKILL.md` |
| principle-test-behavior-not-implementation | `skills/principle-test-behavior-not-implementation/SKILL.md` |
| unslop | `skills/unslop/SKILL.md` |
| technical-writing | `skills/technical-writing/SKILL.md` |

## Steps applied

Investigation.

1. How was applied in this chat. Why was skipped. The question is a runtime constraint, not a history of why the Android host exists.
2. `throughput checkpoint: n/a, read-only investigation`. The investigation did not edit `src/`. The prototype, a different playbook, did write `spikes/ios`.
3. `docs/ios/feasibility.md` is the how-shaped explanation. Section titles are sentence case because unslop and technical-writing both reject title case. The section order matches the how template.
4. Unslop was applied to the docs and to the final reply.

Prototype.

1. The decision is whether the existing screen can sit on a UIKit host, and whether Android's collectible load is the reload mechanism. The answer is a host, and not that load.
2. Reference gathering was skipped. The Android screen and the `dotnet new ios` template already set the direction. This is not an open visual design.
3. The throwaway is `spikes/ios`, separate from `src/` and from `NativePoc.slnx`. It is not production code.
4. A UI switcher was skipped. The two shapes were not two runnable UIs. Building a second UIKit tree on Linux would not have been observable.
5. Verification on Linux was `check-authored.sh`, the spike tests, the Core tests, and the failed `net10.0-ios` build. Simulator and device were not verified.
6. The recommendation is shape B in `docs/ios/architecture.md`. The prototype is throwaway.

Architect.

1. Ground. The how explanation is the grounding.
2. Sketch. Shape A and shape B were screened against the red-flag list in this chat.
3. Agree. Checkpoint skipped. Nobody asked to stop before implementation.
4. Implement. Shape B is the code under `spikes/ios/NativePoc.iOS`.
5. Scrap. Skipped. One closure bug was fixed before the tests. That is one miss, not a repeated workaround.

Prove it works. The script, both test runs, and the iOS build log are the artifacts. A `net10.0` compile was not treated as a UIKit run.

## Same-model checks

1. After writing `UIKitHost`, the button and field handlers closed over the path from mount time. `Retarget` would have updated a field the handlers never read. The handlers now read the path stored on the slot. This was fixed before the tests.
2. `sh spikes/ios/scripts/check-authored.sh` was run after that fix and printed `authored-check ok`.
3. `dotnet test` on the spike project passed 3 tests. `dotnet test` on `tests/NativePoc.Core.Tests` passed 27 tests. SDK `10.0.403`.
4. The iOS build was run twice. The normal log names `wasm-tools`. The diagnostic log names missing pack `Microsoft.iOS.Sdk.net10.0_26.0` and `TargetPlatformIdentifier = ios`. Those two outputs are both in `docs/ios/linux-ios-build.txt`.

## Steps skipped

| Step | Reason |
| --- | --- |
| why skill | Not a motivation question. |
| how explorers and how explainer as Task subagents | No legal Grok 4.7 non-fast slug could be guaranteed for those roles. |
| arena runners | Architect asks for `claude-opus-5-5-xhigh` and `grok-4.7-xhigh-fast` when the pstack model rule is missing. Both are forbidden. The two shapes were compared in this chat. |
| interrogate | Same model ban. No contested product fork remained after the runtime sources were read. |
| swarm | No coverage matrix was required. |
| Feature playbook | The user asked for a bounded spike, not a production feature. |
| Opening a PR | This session does not open a PR. |
| setup-pstack | The user forbade it. Global role and budget config was not changed. |
| comment-sicko subagent | The no-comments skill says to spawn it. That spawn could not be pinned to a non-fast Grok 4.7 slug. The spike files were read in this chat instead. No comments were in the C# or the scripts. Nothing was deleted. |
| deslop | Read from the cursor-team-kit plugin at `~/.cursor/plugins/cache/cursor-public/677/ae5650febf4320793844c12c4adaa937f417f8da/skills/deslop/SKILL.md`. No extra comments, `any` casts, or new try/catch blocks were in the spike. No edit. |
