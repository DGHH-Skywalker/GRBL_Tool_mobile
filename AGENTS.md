# Engineering instructions

Read docs/ANDROID_IMPLEMENTATION_PLAN.md and docs/reuse-matrix.md before changing architecture.
Prioritize existing open-source libraries. Record versions, licenses, source commits and integration evidence before replacing functionality.
Keep Domain independent of Android and Xingguang. One device session owns one serialized writer.
GRBL ok acknowledges a line, not physical completion. Preserve explicit drain/barrier completion, timeout, reset and alarm tests.
Never automatically replay movement or resume after reconnect/restart. Unknown hardware capabilities remain unknown.
Run `dotnet test GRBL.Tool.Mobile.Core.slnx` and `dotnet build GRBL.Tool.Mobile.slnx -c Release` for relevant changes. Use locked restore after dependency changes are committed.
Keep signing keys, credentials, device serial identifiers and raw private logs out of Git.
Report completed verification and outstanding hardware validation accurately.
