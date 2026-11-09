# Engineering instructions

Read docs/ANDROID_IMPLEMENTATION_PLAN.md and docs/reuse-matrix.md before changing architecture.
Prioritize existing open-source libraries. Record versions, licenses, source commits and integration evidence before replacing functionality.
Keep Domain independent of Android and Xingguang. One device session owns one serialized writer.
GRBL ok acknowledges a line, not physical completion. Preserve explicit drain/barrier completion, timeout, reset and alarm tests.
Never automatically replay movement or resume after reconnect/restart. Unknown hardware capabilities remain unknown.
Use GitHub Actions for builds and tests (user preference). Push relevant changes and inspect core/Android jobs, fixing failures before delivery. Local dependency restore is permitted to update lockfiles; preserve locked restore in CI. Native imaging tests run on the Linux runner.
Keep signing keys, credentials, device serial identifiers and raw private logs out of Git.
Report completed verification and outstanding hardware validation accurately.
