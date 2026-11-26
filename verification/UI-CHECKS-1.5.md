# Native conversation UI checks — 1.5

All checks used work/conversation-ui-1.5, a synthetic profile with no workspace and no loaded model. User Data was not used. Native screenshots were inspected inline through the computer-use skill; no desktop recording was retained.

- Right-clicked an unselected synthetic compacted chat. The row became selected, the title changed, and Delete chat… opened a confirmation naming that exact conversation. The prompt described attachment snapshots, separate memories, exports/backups and no in-app undo. No was the default. Declined deletion; both chats remained.
- Focused the selected sidebar row and pressed Delete. The same confirmation opened with No focused. Declined it. Actual confirmed deletion, cancellation, neighbor/last-chat selection and persistence were verified through the routed handler in the synthetic automated suite.
- Opened View summary…. Verified the actual window showed the covered-turn count, model/date, summary text in a selectable read-only scrollable field, the lossiness/image limitation explanation and Use original history.
- Opened Compact context…. Verified its confirmation explained the last four complete turns, retained visible history, possible omissions, review/reset and session usage. Declined this extra UI request; real generation, recall and repeated compaction were already tested through the actual compactor and Agent.
- Verified Compact context… was disabled on the short one-turn chat and enabled on the eligible long chat. Header actions and View summary fit without clipping at the tested window size.
- Fixed pale context-menu text inherited from the dark app theme. Inspected the rebuilt menu with a black Delete chat… label on its light background. Removed the redundant pale shortcut hint afterward; the Delete key remains bound only to the chat list.
- Closed the synthetic app cleanly and released the exact native capture helper created by this test. The separate existing swift helper was not stopped. All inference regression work used sequential bounded test runs.

Limitations: timed cancellation during an active summary stream and physical file dragging were not newly exercised. Cancellation before inference, failure/oversize guards, persistent totals/reset, actual summary recall, existing chat/vision/tool cancellation and window/process lifetime were covered by automated checks. Summary fidelity is model-dependent and is not guaranteed by these fixtures.
